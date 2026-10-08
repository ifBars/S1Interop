using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace S1Interop.Compiler;

/// <summary>
/// Lowers rank-1 primitive and mapped enum arrays connected to a native slot onto <c>Il2CppStructArray&lt;T&gt;</c>, and rank-1
/// arrays of mapped native reference classes onto <c>Il2CppReferenceArray&lt;T&gt;</c>.
/// Every decision is made on the original node through <see cref="CollectionStorageAnalysis"/>, so arrays outside the
/// connected flow stay CLR arrays and an existing array is never copied into native storage.
/// </summary>
internal sealed class NativeArrayLowering(SemanticModel model, MetadataSymbolMap map, CollectionStorageAnalysis storage,
    ImmutableArray<Diagnostic>.Builder diagnostics)
{
    // Native arrays are not System.Array, so conversions to these cannot exist; report them instead of leaving a bare CS0029.
    private static readonly HashSet<string> NonNativeTargets =
    [
        "System.Array", "System.ICloneable", "System.Collections.IList", "System.Collections.ICollection",
        "System.Collections.IStructuralComparable", "System.Collections.IStructuralEquatable",
        "System.Collections.Generic.ICollection<T>", "System.Collections.Generic.IList<T>",
        "System.Span<T>", "System.ReadOnlySpan<T>", "System.Memory<T>", "System.ReadOnlyMemory<T>"
    ];

    public static string Display(MetadataSymbolMap map, IArrayTypeSymbol array) =>
        (!NativeArraySource.IsSupportedArray(map, array) ? NativeReferenceArraySource.ReferenceArrayName : NativeArraySource.StructArrayName) +
        "<" + map.TargetDisplay(array.ElementType) + ">";

    // Eligibility has already checked the enum mapping and layout before helper selection.
    private bool IsReference(IArrayTypeSymbol array) => !NativeArraySource.IsSupportedArray(map, array);

    private string Helper(IArrayTypeSymbol array) =>
        IsReference(array) ? NativeReferenceArraySource.TypeName : NativeArraySource.TypeName;

    public TypeSyntax? Type(ArrayTypeSyntax original)
    {
        // The creation expression is replaced as a whole; its Type slot must stay an ArrayTypeSyntax until then.
        if (original.Parent is ArrayCreationExpressionSyntax || !storage.IsBridgedType(original, model) ||
            model.GetTypeInfo(original).Type is not IArrayTypeSymbol array) return null;
        if (original.Parent is ParameterSyntax parameter && parameter.Modifiers.Any(SyntaxKind.ParamsKeyword))
        {
            Report(original, "params", "a params array parameter cannot use native storage; declare an explicit array parameter");
            return null;
        }
        return ParseTypeName(Display(map, array)).WithTriviaFrom(original);
    }

    public ExpressionSyntax? Creation(ExpressionSyntax original, ExpressionSyntax visited)
    {
        if (!storage.IsNativeArray(original, model, out IArrayTypeSymbol? array)) return null;
        string element = map.TargetDisplay(array!.ElementType);
        string helper = Helper(array);
        bool reference = IsReference(array);
        switch (visited)
        {
            case CollectionExpressionSyntax collection:
                // The target-typed expression owns fresh storage, including spread copies. Give it
                // a CLR temporary target, then initialize native storage without detaching aliases.
                return Call(helper, "Of", element, Argument(CastExpression(ParseTypeName(element + "[]"),
                    collection.WithoutTrivia()))).WithTriviaFrom(original);
            case ArrayCreationExpressionSyntax { Initializer: { } initializer } creation:
                return Of(helper, reference, element, initializer, creation.Type.GetLastToken().TrailingTrivia).WithTriviaFrom(original);
            case ImplicitArrayCreationExpressionSyntax { Initializer: { } initializer } implicitCreation:
                return Of(helper, reference, element, initializer, implicitCreation.CloseBracketToken.TrailingTrivia).WithTriviaFrom(original);
            case ArrayCreationExpressionSyntax { Type.RankSpecifiers: { Count: 1 } ranks } when ranks[0].Sizes.Count == 1:
                return Call(helper, "Create", element, Argument(ranks[0].Sizes[0].WithoutTrivia())).WithTriviaFrom(original);
            default:
                return null;
        }
    }

    // `int[] x = { 1, 2 }` has no creation expression to replace, so the declarator's initializer becomes the allocation.
    public ExpressionSyntax? DeclaratorInitializer(InitializerExpressionSyntax original, InitializerExpressionSyntax visited)
    {
        if (!original.IsKind(SyntaxKind.ArrayInitializerExpression) ||
            original.Parent is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variable } ||
            model.GetDeclaredSymbol(variable) is not { } symbol || !storage.IsNativeArray(symbol, out IArrayTypeSymbol? array))
            return null;
        return Of(Helper(array!), IsReference(array!), map.TargetDisplay(array!.ElementType), visited, default).WithTriviaFrom(original);
    }

    public ExpressionSyntax? Element(ElementAccessExpressionSyntax original, ElementAccessExpressionSyntax visited)
    {
        if (!storage.IsNativeArray(original.Expression, model, out IArrayTypeSymbol? array) ||
            original.ArgumentList.Arguments.Count != 1) return null;
        ITypeSymbol? indexType = model.GetTypeInfo(original.ArgumentList.Arguments[0].Expression).Type;
        if (indexType?.ToDisplayString() == "System.Range")
        {
            Report(original, "range indexer", "slicing allocates a CLR array; copy the range with Array.Copy instead");
            return null;
        }
        bool reference = IsReference(array!);
        // The adaptor call wraps the receiver, so a receiver that is itself a conditional-access binding cannot be wrapped.
        if (reference && HasOuterBinding(original.Expression))
        {
            Report(original, "null-conditional element access",
                "an element access on a native reference array reached through '?.' is not lowered; test for null explicitly");
            return null;
        }

        ArgumentSyntax index = visited.ArgumentList.Arguments[0];
        bool converted = indexType?.SpecialType is SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64;
        // The native indexer takes int only; CLR arrays also accept uint, long, and ulong.
        if (converted)
            index = index.WithExpression(Call(Helper(array!), "Index", null, Argument(index.Expression.WithoutTrivia())).WithTriviaFrom(index.Expression));
        // A native span's interior pointer does not retain the wrapper's native GC handle. A CLR
        // array ref does retain its owner; exposing one here needs separate lifetime lowering.
        if (original.Parent is RefExpressionSyntax || original.Parent is ArgumentSyntax argument &&
            model.GetOperation(argument) is IArgumentOperation { Parameter.RefKind: not RefKind.None })
        {
            Report(original, "element reference", "native element references do not yet preserve their array owner's lifetime");
            return null;
        }
        if (reference)
        {
            // `Elements(array)[index]` keeps receiver, index, and assigned value evaluation in source order, and routes loads and
            // stores (including compound and `??=` assignment and the value of an assignment expression) through the checked accessors.
            ExpressionSyntax elements = Call(Helper(array!), "Elements", map.TargetDisplay(array!.ElementType),
                Argument(visited.Expression.WithoutTrivia())).WithTriviaFrom(visited.Expression);
            return visited.WithExpression(elements).WithArgumentList(visited.ArgumentList.WithArguments(SingletonSeparatedList(index)));
        }
        return converted ? visited.WithArgumentList(visited.ArgumentList.WithArguments(SingletonSeparatedList(index))) : null;
    }

    public ExpressionSyntax? ConditionalElementAccess(ConditionalAccessExpressionSyntax original, ConditionalAccessExpressionSyntax visited)
    {
        if (!storage.IsNativeArray(original.Expression, model, out IArrayTypeSymbol? array) || !IsReference(array!) ||
            !original.WhenNotNull.DescendantNodesAndSelf().OfType<ElementBindingExpressionSyntax>().Any(binding =>
                binding.Ancestors().OfType<ConditionalAccessExpressionSyntax>().FirstOrDefault() == original)) return null;
        if (HasOuterBinding(original.Expression))
        {
            Report(original, "nested null-conditional element access", "the enclosing conditional receiver needs separate adaptation");
            return null;
        }
        // A nullable accessor preserves the conditional chain and evaluates the original receiver exactly once.
        return visited.WithExpression(Call(Helper(array!), "NullableElements", map.TargetDisplay(array!.ElementType),
            Argument(visited.Expression.WithoutTrivia())).WithTriviaFrom(visited.Expression));
    }

    public ExpressionSyntax? ElementBinding(ElementBindingExpressionSyntax original, ElementBindingExpressionSyntax visited)
    {
        // The nearest conditional access whose when-not-null part holds the binding supplies the receiver.
        ConditionalAccessExpressionSyntax? access = original.Ancestors().OfType<ConditionalAccessExpressionSyntax>()
            .FirstOrDefault(candidate => candidate.WhenNotNull.Span.Contains(original.Span));
        if (access is null || !storage.IsNativeArray(access.Expression, model, out IArrayTypeSymbol? array) || !IsReference(array!) ||
            original.ArgumentList.Arguments.Count != 1) return null;
        ITypeSymbol? indexType = model.GetTypeInfo(original.ArgumentList.Arguments[0].Expression).Type;
        if (indexType?.ToDisplayString() == "System.Range")
        {
            Report(original, "range indexer", "slicing allocates a CLR array; copy the range with Array.Copy instead");
            return null;
        }
        if (indexType?.SpecialType is not (SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64)) return null;
        ArgumentSyntax index = visited.ArgumentList.Arguments[0];
        return visited.WithArgumentList(visited.ArgumentList.WithArguments(SingletonSeparatedList(index.WithExpression(
            Call(Helper(array!), "Index", null, Argument(index.Expression.WithoutTrivia())).WithTriviaFrom(index.Expression)))));
    }

    public ExpressionSyntax? Member(MemberAccessExpressionSyntax original, MemberAccessExpressionSyntax visited)
    {
        if (model.GetSymbolInfo(original).Symbol is not IPropertySymbol { ContainingType.SpecialType: SpecialType.System_Array } property ||
            !storage.IsNativeArray(original.Expression, model, out IArrayTypeSymbol? array)) return null;
        switch (property.Name)
        {
            case "Length":
                return null;
            case "LongLength" or "Rank":
                return Call(Helper(array!), property.Name, map.TargetDisplay(array!.ElementType), Argument(visited.Expression.WithoutTrivia()))
                    .WithTriviaFrom(original);
            default:
                Report(original, property.Name, "this System.Array member has no native-array counterpart");
                return null;
        }
    }

    public ExpressionSyntax? Invocation(InvocationExpressionSyntax original, InvocationExpressionSyntax visited)
    {
        // Identity calls route through whichever helper surface exists; both expose the same identity members.
        string? identityHelper = map.SupportsNativeArrays ? NativeArraySource.TypeName
            : map.SupportsNativeReferenceArrays ? NativeReferenceArraySource.TypeName : null;
        if (identityHelper is null ||
            model.GetSymbolInfo(original).Symbol is not IMethodSymbol method)
            return null;

        if (map.SupportsNativeArrays && !map.IsAuthorType(method.ContainingType) && method.IsStatic &&
            method.ContainingType.ToDisplayString() == "System.BitConverter")
        {
            if (method.Name == "GetBytes" && method.Parameters.Length == 1 &&
                method.ReturnType is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte } &&
                storage.IsNativeArray(original, model, out _))
                // Framework GetBytes allocates its result; transfer it before exposing an alias.
                return Call(NativeArraySource.TypeName, "Of", "byte", Argument(visited.WithoutTrivia())).WithTriviaFrom(original);
            if (NativeArraySource.BitConverterReads.ContainsKey(method.Name) &&
                method.Parameters is [{ Type: IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte } },
                    { Type.SpecialType: SpecialType.System_Int32 }] &&
                original.ArgumentList.Arguments.Any(argument => model.GetOperation(argument) is IArgumentOperation { Parameter.Ordinal: 0 } &&
                    storage.IsNativeArray(argument.Expression, model, out _)))
                return Call(NativeArraySource.TypeName, method.Name, null, visited.ArgumentList).WithTriviaFrom(original);
        }

        if (map.SupportsNativeArrays && !map.IsAuthorType(method.ContainingType) && method.IsStatic &&
            method.ContainingType.ToDisplayString() == "System.Convert" && method.Name == "ToBase64String" &&
            method.Parameters[0].Type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte } &&
            original.ArgumentList.Arguments.Any(argument => model.GetOperation(argument) is IArgumentOperation { Parameter.Ordinal: 0 } &&
                storage.IsNativeArray(argument.Expression, model, out _)))
            return Call(NativeArraySource.TypeName, "ToBase64String", null, visited.ArgumentList).WithTriviaFrom(original);

        if (map.SupportsNativeArrays && !map.IsAuthorType(method.ContainingType) && method.IsStatic &&
            method.ContainingType.ToDisplayString() == "System.Convert" && method.Name == "FromBase64String" &&
            method.Parameters is [{ Type.SpecialType: SpecialType.System_String }] &&
            storage.IsNativeArray(original, model, out var decoded) && decoded?.ElementType.SpecialType == SpecialType.System_Byte)
            // Convert owns a newly allocated result, including empty results. Transfer it before
            // exposing it to author code; keep the original decoder's validation and exception order.
            return Call(NativeArraySource.TypeName, "Of", "byte", Argument(visited.WithoutTrivia())).WithTriviaFrom(original);

        if (map.SupportsNativeArrays && !map.IsAuthorType(method.ContainingType) &&
            method.ContainingType.ToDisplayString() == "System.Text.Encoding" && method.Name == "GetString" &&
            method.Parameters.Length is 1 or 3 && method.Parameters[0].Type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte } &&
            original.Expression is MemberAccessExpressionSyntax { Expression: { } encoding } &&
            model.GetSymbolInfo(encoding).Symbol is IPropertySymbol { Name: "UTF8", IsStatic: true } encodingProperty &&
            encodingProperty.ContainingType.ToDisplayString() == "System.Text.Encoding" &&
            original.ArgumentList.Arguments.Any(argument => model.GetOperation(argument) is IArgumentOperation { Parameter.Ordinal: 0 } &&
                storage.IsNativeArray(argument.Expression, model, out _)))
            return Call(NativeArraySource.TypeName, "Utf8String", null,
                visited.ArgumentList.WithArguments(visited.ArgumentList.Arguments.Insert(0,
                    Argument(((MemberAccessExpressionSyntax)visited.Expression).Expression.WithoutTrivia())))).WithTriviaFrom(original);

        if (method.ContainingType.ToDisplayString() == "System.MemoryExtensions" && method.Name is "AsSpan" or "AsMemory" &&
            (original.Expression is MemberAccessExpressionSyntax extension && storage.IsNativeArray(extension.Expression, model, out _) ||
             original.ArgumentList.Arguments.Any(argument => storage.IsNativeArray(argument.Expression, model, out _))))
        {
            Report(original, method.Name, "native memory views do not yet preserve their array owner's lifetime");
            return null;
        }

        if (method.ContainingType.SpecialType == SpecialType.System_Object && method.Name == "Equals" &&
            (method.IsStatic || original.Expression is MemberAccessExpressionSyntax { Expression: not BaseExpressionSyntax }))
        {
            var arguments = visited.ArgumentList.Arguments;
            for (int i = 0; i < arguments.Count; i++)
                if (arguments[i].NameColon is not null && model.GetOperation(original.ArgumentList.Arguments[i]) is IArgumentOperation { Parameter: { } parameter })
                    arguments = arguments.Replace(arguments[i], arguments[i].WithNameColon(NameColon(
                        method.IsStatic && parameter.Ordinal == 0 ? "left" : "right")));
            if (!method.IsStatic)
                arguments = arguments.Insert(0, Argument(((MemberAccessExpressionSyntax)visited.Expression).Expression.WithoutTrivia()));
            return Call(identityHelper, method.IsStatic ? "ObjectEquals" : "InstanceEquals", null,
                visited.ArgumentList.WithArguments(arguments)).WithTriviaFrom(original);
        }
        if (!method.IsStatic && method.ContainingType.SpecialType == SpecialType.System_Object && method.Name == "GetHashCode" &&
            original.Expression is MemberAccessExpressionSyntax { Expression: not BaseExpressionSyntax })
            return Call(identityHelper, "IdentityHash", null, Argument(((MemberAccessExpressionSyntax)visited.Expression).Expression.WithoutTrivia()))
                .WithTriviaFrom(original);
        if (method.ContainingType.SpecialType != SpecialType.System_Array) return null;

        ExpressionSyntax? receiver = null;
        ExpressionSyntax? loweredReceiver = null;
        if (!method.IsStatic)
        {
            if (original.Expression is MemberAccessExpressionSyntax access)
            {
                receiver = access.Expression;
                loweredReceiver = ((MemberAccessExpressionSyntax)visited.Expression).Expression;
            }
            else if (original.Expression is MemberBindingExpressionSyntax && original.Parent is ConditionalAccessExpressionSyntax conditional)
            {
                receiver = conditional.Expression;
            }
        }

        IArrayTypeSymbol? element = receiver is not null ? NativeElement(receiver)
            : original.ArgumentList.Arguments.Select(argument => NativeElement(argument.Expression)).FirstOrDefault(type => type is not null);
        if (element is null || method.Name == "GetEnumerator") return null;

        string helper = Helper(element);
        bool reference = IsReference(element);
        string typeArguments = map.TargetDisplay(element.ElementType);
        ArgumentListSyntax list = visited.ArgumentList;
        switch (method.Name)
        {
            case "Copy" when method.IsStatic && list.Arguments.Count is 3 or 5:
            case "CopyTo" when !method.IsStatic && list.Arguments.Count == 2:
                if (reference)
                {
                    if (ReferenceOperands(original, visited, method, element) is not { } referenceOperands) return null;
                    list = referenceOperands.List;
                    typeArguments = referenceOperands.TypeArguments;
                }
                else
                {
                    if (Operands(original, visited, element) is not { } operands) return null;
                    list = operands;
                }
                break;
            case "Clear" or "Resize" when method.IsStatic:
            case "GetLength" or "GetLowerBound" or "GetUpperBound" or "Clone" when !method.IsStatic:
                break;
            default:
                Report(original, method.Name, "this System.Array member has no native-array counterpart");
                return null;
        }

        if (!method.IsStatic)
        {
            if (loweredReceiver is null)
            {
                Report(original, method.Name, "null-conditional calls on native-backed arrays are not lowered; test for null explicitly");
                return null;
            }
            list = list.WithArguments(list.Arguments.Insert(0, Argument(loweredReceiver.WithoutTrivia())));
        }
        return Call(helper, method.Name, typeArguments, list).WithTriviaFrom(original);
    }

    /// <summary>
    /// Converts an implicit covariant array conversion between two eligible reference arrays (for example <c>Employee[]</c> to
    /// <c>Actor[]</c>) into a second native wrapper over the same array. The wrapper keeps the native identity and the array's
    /// actual runtime element class, so no snapshot is made and array-store checks keep seeing the real element type.
    /// </summary>
    public ExpressionSyntax Covariance(ExpressionSyntax original, ExpressionSyntax converted)
    {
        if (!map.SupportsNativeReferenceArrays || original.IsKind(SyntaxKind.NullLiteralExpression) ||
            model.GetTypeInfo(original) is not { Type: IArrayTypeSymbol { IsSZArray: true } source, ConvertedType: IArrayTypeSymbol { IsSZArray: true } target } ||
            SymbolEqualityComparer.Default.Equals(source, target) ||
            !NativeReferenceArraySource.IsMappedReferenceArray(map, source) || !NativeReferenceArraySource.IsMappedReferenceArray(map, target) ||
            model.Compilation.ClassifyConversion(source, target) is not { IsImplicit: true, IsReference: true } ||
            !storage.IsNativeArray(original, model, out _))
            return converted;
        return Call(NativeReferenceArraySource.TypeName, "Covariant",
            map.TargetDisplay(target.ElementType) + ", " + map.TargetDisplay(source.ElementType),
            Argument(converted.WithoutTrivia())).WithTriviaFrom(original);
    }

    public bool IsTypeTest(BinaryExpressionSyntax node) =>
        node.IsKind(SyntaxKind.IsExpression) && node.Right is ArrayTypeSyntax array && storage.IsBridgedType(array, model);

    public void CheckConversion(ExpressionSyntax original)
    {
        if (!(map.SupportsNativeArrays || map.SupportsNativeReferenceArrays) ||
            model.GetTypeInfo(original) is not { Type: IArrayTypeSymbol type, ConvertedType: { } target } ||
            SymbolEqualityComparer.Default.Equals(type, target) || !storage.IsNativeArray(original, model, out _)) return;
        bool nonNative = NonNativeTargets.Contains(target.OriginalDefinition.ToDisplayString());
        // A reference array converts covariantly only to another mapped native class array; anything else (object[], string[],
        // interface arrays) has no native array representation, and an implicit CLR copy would detach the array's identity.
        bool unrepresentable = IsReference(type) && target is IArrayTypeSymbol &&
            !NativeReferenceArraySource.IsMappedReferenceArray(map, target);
        if (!nonNative && !unrepresentable) return;
        // Operands of lowered System.Array calls are converted by the helpers' parameters, not by the compiler.
        if (original.Parent is ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax call } &&
            model.GetSymbolInfo(call).Symbol is IMethodSymbol { ContainingType.SpecialType: SpecialType.System_Array }) return;
        Report(original, "conversion to " + target.ToDisplayString(), unrepresentable
            ? "a native reference array can only convert to an array of a mapped native class; other element types need a copy"
            : "this interface or memory view needs native array semantic and lifetime adaptation");
    }

    // Copies and clears accept any array operand, so mixed CLR/native operands must agree on scalar element type.
    private ArgumentListSyntax? Operands(InvocationExpressionSyntax original, InvocationExpressionSyntax visited, IArrayTypeSymbol element)
    {
        var arguments = visited.ArgumentList.Arguments;
        for (int i = 0; i < arguments.Count; i++)
        {
            ArgumentSyntax source = original.ArgumentList.Arguments[i];
            if (model.GetOperation(source) is not IArgumentOperation { Parameter.Type.SpecialType: SpecialType.System_Array }) continue;
            if (source.Expression.IsKind(SyntaxKind.NullLiteralExpression))
            {
                ExpressionSyntax none = ParseExpression("default(" + NativeArraySource.RefTypeName + "<" + map.TargetDisplay(element.ElementType) + ">)");
                arguments = arguments.Replace(arguments[i], arguments[i].WithExpression(none.WithTriviaFrom(arguments[i].Expression)));
            }
            else if (model.GetTypeInfo(source.Expression).Type is not IArrayTypeSymbol { IsSZArray: true } type ||
                     !SymbolEqualityComparer.Default.Equals(type.ElementType, element.ElementType))
            {
                Report(original, ((IMethodSymbol)model.GetSymbolInfo(original).Symbol!).Name,
                    "array operands must be one-dimensional arrays with the same primitive or mapped enum element type");
                return null;
            }
        }
        return visited.ArgumentList.WithArguments(arguments);
    }

    // Reference copies take each operand's own static element type, so covariant operands pair without any conversion. Each
    // operand is either a CLR array or a native array; the helper moves elements one at a time through the checked store.
    private (ArgumentListSyntax List, string TypeArguments)? ReferenceOperands(InvocationExpressionSyntax original,
        InvocationExpressionSyntax visited, IMethodSymbol method, IArrayTypeSymbol nativeOperand)
    {
        var arguments = visited.ArgumentList.Arguments;
        var operands = new List<(int Index, IArrayTypeSymbol? Type, bool IsSource)>();
        for (int i = 0; i < arguments.Count; i++)
        {
            ArgumentSyntax source = original.ArgumentList.Arguments[i];
            if (model.GetOperation(source) is not IArgumentOperation { Parameter: { Type.SpecialType: SpecialType.System_Array } parameter }) continue;
            bool isSource = method.IsStatic && parameter.Ordinal == 0;
            if (source.Expression.IsKind(SyntaxKind.NullLiteralExpression))
            {
                operands.Add((i, null, isSource));
            }
            else if (model.GetTypeInfo(source.Expression).Type is IArrayTypeSymbol { IsSZArray: true } type &&
                     NativeReferenceArraySource.IsMappedReferenceArray(map, type))
            {
                operands.Add((i, type, isSource));
            }
            else
            {
                Report(original, method.Name, "array operands must be one-dimensional arrays of mapped native reference classes");
                return null;
            }
        }
        if (operands.Count != (method.IsStatic ? 2 : 1))
        {
            Report(original, method.Name, "the array operands of this overload cannot be identified");
            return null;
        }

        IArrayTypeSymbol? copyFrom = method.IsStatic ? operands.Single(operand => operand.IsSource).Type : nativeOperand;
        IArrayTypeSymbol? copyTo = operands.Single(operand => !operand.IsSource).Type;
        // A null operand throws before any element moves, so it only needs a type that binds.
        copyFrom ??= copyTo ?? nativeOperand;
        copyTo ??= copyFrom;
        foreach ((int position, IArrayTypeSymbol? operandType, bool isSource) in operands)
        {
            if (operandType is not null) continue;
            IArrayTypeSymbol role = isSource ? copyFrom : copyTo;
            ExpressionSyntax none = ParseExpression("default(" + NativeReferenceArraySource.RefTypeName + "<" + map.TargetDisplay(role.ElementType) + ">)");
            arguments = arguments.Replace(arguments[position], arguments[position].WithExpression(none.WithTriviaFrom(arguments[position].Expression)));
        }
        return (visited.ArgumentList.WithArguments(arguments), map.TargetDisplay(copyFrom.ElementType) + ", " + map.TargetDisplay(copyTo.ElementType));
    }

    // The params array is a temporary, so Of<T> is the only copy and no alias of an existing array is detached.
    private static InvocationExpressionSyntax Of(string helper, bool reference, string element, InitializerExpressionSyntax initializer,
        SyntaxTriviaList before)
    {
        if (reference) return OfReference(helper, element, initializer, before);
        var parts = initializer.Expressions.GetWithSeparators().ToList();
        SyntaxTriviaList closing = initializer.CloseBraceToken.LeadingTrivia;
        if (parts.Count > 0 && parts[^1].IsToken)
        {
            // A trailing comma is legal in an initializer but not in an argument list.
            SyntaxToken comma = parts[^1].AsToken();
            closing = comma.LeadingTrivia.AddRange(comma.TrailingTrivia).AddRange(closing);
            parts.RemoveAt(parts.Count - 1);
        }
        // Keeping the elements' separators and the braces' trivia keeps original line numbers stable.
        var arguments = SeparatedList<ArgumentSyntax>(parts.Select(part => part.IsToken ? part : Argument((ExpressionSyntax)part.AsNode()!)));
        return Call(helper, "Of", element, ArgumentList(
            Token(before.AddRange(initializer.OpenBraceToken.LeadingTrivia), SyntaxKind.OpenParenToken, initializer.OpenBraceToken.TrailingTrivia),
            arguments, Token(closing, SyntaxKind.CloseParenToken, default)));
    }

    // An explicit temporary keeps a lone `null` element from binding as a null values array, and a conversion to the element
    // type happens in the CLR temporary exactly where the original initializer converted it.
    private static InvocationExpressionSyntax OfReference(string helper, string element, InitializerExpressionSyntax initializer,
        SyntaxTriviaList before)
    {
        var temporary = (ArrayCreationExpressionSyntax)ParseExpression("new " + element + "[]");
        temporary = temporary.WithType(temporary.Type.WithTrailingTrivia(before)).WithInitializer(initializer);
        return Call(helper, "Of", element, Argument(temporary));
    }

    private static InvocationExpressionSyntax Call(string helper, string method, string? typeArgument, params ArgumentSyntax[] arguments) =>
        Call(helper, method, typeArgument, ArgumentList(SeparatedList(arguments)));

    private static InvocationExpressionSyntax Call(string helper, string method, string? typeArgument, ArgumentListSyntax arguments) =>
        InvocationExpression(ParseExpression(helper + "." + method +
            (typeArgument is null ? string.Empty : "<" + typeArgument + ">")), arguments);

    // A binding inside the receiver belongs to an enclosing `?.`, so the receiver cannot move into an argument list.
    private static bool HasOuterBinding(ExpressionSyntax expression) =>
        expression.DescendantNodesAndSelf().Where(node => node is MemberBindingExpressionSyntax or ElementBindingExpressionSyntax)
            .Any(binding => !binding.Ancestors().TakeWhile(ancestor => ancestor != expression.Parent)
                .OfType<ConditionalAccessExpressionSyntax>().Any(access => access.WhenNotNull.Span.Contains(binding.Span)));

    private IArrayTypeSymbol? NativeElement(ExpressionSyntax expression) =>
        storage.IsNativeArray(expression, model, out IArrayTypeSymbol? array) ? array : null;

    private void Report(SyntaxNode node, string operation, string reason) =>
        diagnostics.Add(Diagnostic.Create(LoweringDiagnostics.UnsupportedNativeArrayOperation, node.GetLocation(), operation, reason));
}
