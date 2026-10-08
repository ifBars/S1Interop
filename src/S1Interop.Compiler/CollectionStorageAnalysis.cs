using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace S1Interop.Compiler;

/// <summary>Finds collection storage connected to native slots without changing unrelated CLR collections.</summary>
internal sealed partial class CollectionStorageAnalysis
{
    private readonly Dictionary<SyntaxNode, int> expressions = new();
    private readonly Dictionary<ISymbol, int> symbols = new(SymbolEqualityComparer.Default);
    private readonly List<int> parents = new();
    private readonly HashSet<int> seeds = new();
    private readonly HashSet<int> nativeRoots;
    private readonly MetadataSymbolMap map;

    public CollectionStorageAnalysis(Compilation author, MetadataSymbolMap map, CancellationToken cancellationToken)
    {
        this.map = map;
        foreach (var tree in author.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = author.GetSemanticModel(tree);
            var slots = new DelegateConversions(model, map, System.Collections.Immutable.ImmutableArray.CreateBuilder<Diagnostic>());
            var fields = new ComponentFieldLowering(model, map, System.Collections.Immutable.ImmutableArray.CreateBuilder<Diagnostic>());
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (node is VariableDeclaratorSyntax fieldDeclarator && model.GetDeclaredSymbol(fieldDeclarator) is IFieldSymbol field &&
                    field.Type is IArrayTypeSymbol && Eligible(field.Type) && fields.IsLowered(field))
                    seeds.Add(Symbol(field));
                if (node is ExpressionSyntax expression && model.GetTypeInfo(expression).Type is { } type &&
                    Eligible(type) && (NativeSlot(slots.InferredTargetType(expression), type) || map.IsBridgedList(type)))
                    seeds.Add(Expression(expression, model));
                switch (node)
                {
                    case InvocationExpressionSyntax traversal when NativeTraverseLowering.ReturnsNativeCollection(traversal, model, map):
                        seeds.Add(Expression(traversal, model));
                        break;
                    case InvocationExpressionSyntax reflection when NativeReflectionVerifier.ReturnsNativeCollectionDescriptor(reflection, model, map):
                        seeds.Add(Expression(reflection, model));
                        break;
                    case InvocationExpressionSyntax reflectionAccess when model.GetOperation(reflectionAccess) is IInvocationOperation call &&
                        call.TargetMethod.ContainingType.ToDisplayString() == "System.Reflection.FieldInfo":
                        ExpressionSyntax? receiver = reflectionAccess.Expression is MemberAccessExpressionSyntax member ? member.Expression
                            : reflectionAccess.Parent is ConditionalAccessExpressionSyntax conditionalAccess ? conditionalAccess.Expression : null;
                        if (receiver is null) break;
                        if (call.TargetMethod.Name == "GetValue")
                        {
                            Join(Expression(reflectionAccess, model), Expression(receiver, model));
                            if (reflectionAccess.Parent is ConditionalAccessExpressionSyntax conditionalRead)
                                Join(Expression(conditionalRead, model), Expression(reflectionAccess, model));
                        }
                        else if (call.TargetMethod.Name == "SetValue" && call.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 1)?.Syntax is ArgumentSyntax valueArgument)
                            Join(Expression(valueArgument.Expression, model), Expression(receiver, model));
                        break;
                    case VariableDeclaratorSyntax { Initializer.Value: { } value } variable:
                        if (model.GetDeclaredSymbol(variable) is { } symbol) Join(Symbol(symbol), Expression(value, model));
                        break;
                    case PropertyDeclarationSyntax { Initializer.Value: { } value } property:
                        if (model.GetDeclaredSymbol(property) is { } propertySymbol) Join(Symbol(propertySymbol), Expression(value, model));
                        break;
                    case AssignmentExpressionSyntax assignment:
                        Join(Expression(assignment.Left, model), Expression(assignment.Right, model));
                        break;
                    case ReturnStatementSyntax { Expression: { } value }:
                        if (model.GetEnclosingSymbol(value.SpanStart) is IMethodSymbol method)
                            Join(Symbol(method.AssociatedSymbol ?? method), Expression(value, model));
                        break;
                    case ArrowExpressionClauseSyntax arrow:
                        if (model.GetDeclaredSymbol(arrow.Parent!) is { } owner)
                            Join(Symbol(owner), Expression(arrow.Expression, model));
                        break;
                    case ArgumentSyntax argument when model.GetOperation(argument) is IArgumentOperation { Parameter: { } parameter }:
                        if (map.IsAuthorType(parameter.ContainingType))
                        {
                            // Generic T arguments have call-specific representation slots.
                            if (parameter.OriginalDefinition.Type is not ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method })
                                Join(Symbol(parameter), Expression(argument.Expression, model));
                        }
                        else if (Eligible(parameter.Type) && NativeSlot(slots.ExpectedTargetType(argument.Expression), parameter.Type))
                            seeds.Add(Expression(argument.Expression, model));
                        break;
                    case ConditionalExpressionSyntax conditional:
                        Join(Expression(conditional, model), Expression(conditional.WhenTrue, model));
                        Join(Expression(conditional, model), Expression(conditional.WhenFalse, model));
                        break;
                    case SwitchExpressionSyntax switchExpression:
                        foreach (var arm in switchExpression.Arms)
                            Join(Expression(switchExpression, model), Expression(arm.Expression, model));
                        break;
                    case BinaryExpressionSyntax binary when binary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.CoalesceExpression):
                        Join(Expression(binary, model), Expression(binary.Left, model));
                        Join(Expression(binary, model), Expression(binary.Right, model));
                        break;
                    case BinaryExpressionSyntax binary when binary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AsExpression):
                        Join(Expression(binary, model), Expression(binary.Left, model));
                        break;
                    case DeclarationPatternSyntax { Designation: SingleVariableDesignationSyntax designation } declarationPattern
                        when declarationPattern.Ancestors().OfType<IsPatternExpressionSyntax>().FirstOrDefault() is { } test:
                        if (model.GetDeclaredSymbol(designation) is { } patternVariable)
                            Join(Symbol(patternVariable), Expression(test.Expression, model));
                        break;
                    case DeclarationPatternSyntax { Designation: SingleVariableDesignationSyntax caseDesignation, Parent: CasePatternSwitchLabelSyntax or SwitchExpressionArmSyntax } casePattern
                        when PatternInput(casePattern) is { } governing:
                        // Only array switch variables are tied to the input; list switch patterns are not lowered.
                        if (model.GetDeclaredSymbol(caseDesignation) is ILocalSymbol { Type: IArrayTypeSymbol } caseVariable)
                            Join(Symbol(caseVariable), Expression(governing, model));
                        break;
                    case ParenthesizedExpressionSyntax parentheses:
                        Join(Expression(parentheses, model), Expression(parentheses.Expression, model));
                        break;
                    case PostfixUnaryExpressionSyntax suppression when suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                        Join(Expression(suppression, model), Expression(suppression.Operand, model));
                        break;
                    case CastExpressionSyntax cast:
                        Join(Expression(cast, model), Expression(cast.Expression, model));
                        break;
                    case VariableDeclarationSyntax declaration:
                        // A shared declaration has one written type, so all declarators need the same representation.
                        var declared = declaration.Variables.Select(variable => model.GetDeclaredSymbol(variable)).OfType<ISymbol>().ToArray();
                        for (int i = 1; i < declared.Length; i++) Join(Symbol(declared[0]), Symbol(declared[i]));
                        break;
                }
                if (node is InvocationExpressionSyntax invocation &&
                    model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { Name: "GetRange" or "FindAll" } listMethod &&
                    Eligible(listMethod.ContainingType) && invocation.Expression is MemberAccessExpressionSyntax access)
                    Join(Expression(invocation, model), Expression(access.Expression, model));
                if (node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax cloned } clone &&
                    model.GetSymbolInfo(clone).Symbol is IMethodSymbol { Name: "Clone", ContainingType.SpecialType: SpecialType.System_Array })
                    // A clone keeps its source's representation, so both sides must agree on native or CLR storage.
                    Join(Expression(clone, model), Expression(cloned.Expression, model));
                if (node is MemberAccessExpressionSyntax view &&
                    model.GetSymbolInfo(view).Symbol is IPropertySymbol { Name: "Keys" or "Values" } propertyView &&
                    Eligible(propertyView.ContainingType))
                    Join(Expression(view, model), Expression(view.Expression, model));
                if (node is InvocationExpressionSyntax enumerate &&
                    model.GetSymbolInfo(enumerate).Symbol is IMethodSymbol { Name: "GetEnumerator" } enumerator &&
                    Eligible(enumerator.ContainingType) && enumerate.Expression is MemberAccessExpressionSyntax sequence)
                    Join(Expression(enumerate, model), Expression(sequence.Expression, model));
            }
        }
        ConnectGenericValueStorage(author, cancellationToken);
        ConnectDelegateStorage(author, cancellationToken);
        ResolveGenericValueStorage(cancellationToken);
        nativeRoots = seeds.Select(Root).ToHashSet();
    }

    public bool IsBridged(ExpressionSyntax expression, SemanticModel model) =>
        Eligible(model.GetTypeInfo(expression).Type) && HasNativeStorage(expression);

    private bool HasNativeStorage(ExpressionSyntax expression) =>
        expressions.TryGetValue(expression, out int node) && nativeRoots.Contains(Root(node));

    /// <summary>
    /// Gets whether the expression's flow component holds native storage. Unlike <see cref="IsBridged"/> this also resolves
    /// plain uses of connected locals, parameters, fields, and author methods, which no flow edge registers as expressions.
    /// </summary>
    public bool IsConnected(ExpressionSyntax expression, SemanticModel model)
    {
        if (HasNativeStorage(expression)) return true;
        ISymbol? symbol = model.GetSymbolInfo(expression).Symbol;
        if (symbol is null || symbol is IMethodSymbol method && !map.IsAuthorType(method.ContainingType)) return false;
        return symbols.TryGetValue(StorageDefinition(symbol), out int node) && nativeRoots.Contains(Root(node));
    }

    /// <summary>Gets whether the expression is an eligible scalar or mapped native reference array whose storage must be native.</summary>
    public bool IsNativeArray(ExpressionSyntax expression, SemanticModel model, out IArrayTypeSymbol? array)
    {
        array = model.GetTypeInfo(expression).Type as IArrayTypeSymbol;
        if (array is null && expression is CollectionExpressionSyntax)
            array = model.GetTypeInfo(expression).ConvertedType as IArrayTypeSymbol;
        return array is not null && Eligible(array) && IsConnected(expression, model);
    }

    /// <summary>Gets whether the declared local, field, or property is an eligible scalar or reference array with native storage.</summary>
    public bool IsNativeArray(ISymbol symbol, out IArrayTypeSymbol? array)
    {
        ITypeSymbol? declared = symbol switch { ILocalSymbol local => local.Type, IFieldSymbol field => field.Type, IPropertySymbol property => property.Type, _ => null };
        array = declared as IArrayTypeSymbol;
        return array is not null && Eligible(array) && symbols.TryGetValue(StorageDefinition(symbol), out int node) && nativeRoots.Contains(Root(node));
    }

    // The expression a pattern variable or type test is matched against.
    private static ExpressionSyntax? PatternInput(SyntaxNode pattern)
    {
        foreach (SyntaxNode ancestor in pattern.Ancestors())
        {
            switch (ancestor)
            {
                case IsPatternExpressionSyntax test: return test.Expression;
                case SwitchExpressionSyntax switchExpression: return switchExpression.GoverningExpression;
                case SwitchStatementSyntax switchStatement: return switchStatement.Expression;
            }
        }
        return null;
    }

    public bool IsBridgedType(TypeSyntax syntax, SemanticModel model)
    {
        ITypeSymbol? written = model.GetTypeInfo(syntax).Type ?? model.GetSymbolInfo(syntax).Symbol as ITypeSymbol;
        if (!Eligible(written)) return false;
        if (IsNativeDelegateTypeArgument(syntax, model) || IsNativeMethodTypeArgument(syntax, model)) return true;
        SyntaxNode node = syntax;
        while (node.Parent is NullableTypeSyntax or RefTypeSyntax or QualifiedNameSyntax or AliasQualifiedNameSyntax) node = node.Parent;
        ISymbol? symbol = node.Parent switch
        {
            VariableDeclarationSyntax variables => model.GetDeclaredSymbol(variables.Variables[0]),
            ParameterSyntax parameter => model.GetDeclaredSymbol(parameter),
            MethodDeclarationSyntax method when method.ReturnType == node => model.GetDeclaredSymbol(method),
            LocalFunctionStatementSyntax method when method.ReturnType == node => model.GetDeclaredSymbol(method),
            PropertyDeclarationSyntax property when property.Type == node => model.GetDeclaredSymbol(property),
            DeclarationPatternSyntax { Designation: SingleVariableDesignationSyntax designation } => model.GetDeclaredSymbol(designation),
            _ => null
        };
        if (symbol is not null && symbols.TryGetValue(StorageDefinition(symbol), out int storage)) return nativeRoots.Contains(Root(storage));
        if (written is IArrayTypeSymbol)
        {
            // `x is T[]` and type patterns test the input's representation, so they follow the input's storage.
            return node.Parent switch
            {
                BinaryExpressionSyntax { RawKind: (int)SyntaxKind.IsExpression } test when test.Right == node => IsConnected(test.Left, model),
                PatternSyntax => PatternInput(node) is { } input && IsConnected(input, model),
                ExpressionSyntax arrayExpression => IsBridged(arrayExpression, model),
                _ => false
            };
        }
        if (node.Parent is PatternSyntax && node.Ancestors().OfType<IsPatternExpressionSyntax>().FirstOrDefault() is { } pattern)
            return IsConnected(pattern.Expression, model);
        return node.Parent is ExpressionSyntax expression && IsBridged(expression, model);
    }

    public string Display(ITypeSymbol type)
    {
        var named = (INamedTypeSymbol)type;
        if (named.ContainingType is { } owner) return Display(owner) + "." + named.Name;
        string helper = named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.Dictionary<TKey, TValue>"
            ? NativeDictionarySource.TypeName : NativeListSource.TypeName;
        return helper + "<" + string.Join(", ", named.TypeArguments.Select(map.TargetDisplay)) + ">";
    }
    private int Expression(ExpressionSyntax expression, SemanticModel model)
    {
        if (expressions.TryGetValue(expression, out int existing)) return existing;
        int node = NewNode();
        expressions.Add(expression, node);
        var symbol = model.GetSymbolInfo(expression).Symbol;
        if (symbol is ILocalSymbol or IParameterSymbol or IFieldSymbol or IPropertySymbol ||
            expression is InvocationExpressionSyntax && symbol is IMethodSymbol)
        {
            if (symbol is not IMethodSymbol method)
                Join(node, Symbol(symbol));
            else if (map.IsAuthorType(method.ContainingType) &&
                method.OriginalDefinition.ReturnType is not ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method })
                Join(node, Symbol(symbol));
        }
        return node;
    }

    private int Symbol(ISymbol symbol)
    {
        symbol = StorageDefinition(symbol);
        if (!symbols.TryGetValue(symbol, out int node)) symbols.Add(symbol, node = NewNode());
        return node;
    }

    private static ISymbol StorageDefinition(ISymbol symbol)
    {
        // A fixed byte[] parameter in M<T> has one source declaration, even though Roslyn
        // supplies different constructed parameter symbols at M<int> and M<string> calls.
        // Do not merge storage whose type itself changes with the generic arguments.
        static ITypeSymbol? StorageType(ISymbol value) => value switch
        {
            IParameterSymbol parameter => parameter.Type,
            IMethodSymbol method => method.ReturnType,
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null
        };
        var definition = symbol.OriginalDefinition;
        var owner = symbol is IMethodSymbol method ? method : symbol.ContainingSymbol as IMethodSymbol;
        if (owner is not null && GenericCollectionSpecialization.IsSpecialized(owner)) return definition;
        return StorageType(symbol) is { } type && SymbolEqualityComparer.Default.Equals(type, StorageType(definition))
            ? definition : symbol;
    }

    private int NewNode() { int node = parents.Count; parents.Add(node); return node; }
    private int Root(int node)
    {
        int root = node;
        while (parents[root] != root) root = parents[root];
        while (parents[node] != node)
        {
            int next = parents[node];
            parents[node] = root;
            node = next;
        }
        return root;
    }
    private void Join(int left, int right) => parents[Root(left)] = Root(right);

    private static bool NativeCollection(INamedTypeSymbol? type) => type?.OriginalDefinition.ToDisplayString() is
        "Il2CppSystem.Collections.Generic.List<T>" or "S1Interop.Compiler.Generated.S1InteropList<T>" or
        "Il2CppSystem.Collections.Generic.Dictionary<TKey, TValue>" or "S1Interop.Compiler.Generated.S1InteropDictionary<TKey, TValue>";

    // Abstract native array slots retain the concrete reference representation for supported mapped class elements.
    private bool NativeSlot(INamedTypeSymbol? slot, ITypeSymbol authored) => NativeCollection(slot) || SupportsNativeArraySlot(map, slot, authored);

    internal static bool SupportsNativeArraySlot(MetadataSymbolMap map, INamedTypeSymbol? slot, ITypeSymbol authored) =>
        authored is IArrayTypeSymbol array &&
        (map.SupportsNativeArrays && NativeArraySource.IsSupportedArray(map, array) ||
         map.SupportsNativeReferenceArrays && NativeReferenceArraySource.IsMappedReferenceArray(map, array)) &&
        slot is { Arity: 1 } && DelegateConversions.IsNativeArray(slot) &&
        (slot.Name == "Il2CppStructArray" &&
         (array.ElementType.SpecialType != SpecialType.None && slot.TypeArguments[0].SpecialType == array.ElementType.SpecialType ||
          array.ElementType is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumeration &&
          map.Resolve(enumeration).Target is { } nativeEnum && SymbolEqualityComparer.Default.Equals(slot.TypeArguments[0], nativeEnum)) ||
         // The slot's element must be the mapped class itself: a covariant slot would need a reinterpreted view at the boundary.
         slot.Name is "Il2CppReferenceArray" or "Il2CppArrayBase" && array.ElementType is INamedTypeSymbol { TypeKind: TypeKind.Class } element &&
         map.Resolve(element).Target is { } mapped && SymbolEqualityComparer.Default.Equals(slot.TypeArguments[0], mapped));

    private bool Eligible(ITypeSymbol? type) => type is IArrayTypeSymbol array
        ? map.SupportsNativeArrays && NativeArraySource.IsSupportedArray(map, array) ||
          map.SupportsNativeReferenceArrays && NativeReferenceArraySource.IsMappedReferenceArray(map, array)
        : type is INamedTypeSymbol named &&
        (named.ContainingType is { } owner ? Eligible(owner) :
         named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>" && Representable(named.TypeArguments[0]) ||
         named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.Dictionary<TKey, TValue>" &&
         named.TypeArguments.All(Representable));

    private bool Representable(ITypeSymbol type) => IsRepresentable(map, type) ||
        type is ITypeParameterSymbol { ContainingSymbol: IMethodSymbol method } && GenericCollectionSpecialization.IsSpecialized(method);

    internal static bool IsRepresentable(MetadataSymbolMap map, ITypeSymbol type) => Scalar(type) || map.IsNative(type) ||
        type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumeration && map.Resolve(enumeration).Status == TypeMappingStatus.Mapped;

    private static bool Scalar(ITypeSymbol type) => type.SpecialType is SpecialType.System_String or SpecialType.System_Boolean or
        SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
        SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or
        SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Char;
}
