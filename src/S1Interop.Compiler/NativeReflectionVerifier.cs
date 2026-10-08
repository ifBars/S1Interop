using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.CSharp;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using FieldAttributes = System.Reflection.FieldAttributes;

namespace S1Interop.Compiler;

internal static partial class NativeReflectionVerifier
{
    private static readonly DiagnosticDescriptor FieldBecameProperty = new("S1IC034", "Native reflection shape changed",
        "Field lookup '{0}.{1}' targets an IL2CPP property; this FieldInfo usage is not supported and could lose the field at runtime",
        "S1Interop.Compiler", DiagnosticSeverity.Error, true);

    public static IEnumerable<Diagnostic> Verify(SemanticModel model, MetadataSymbolMap map, CancellationToken cancellationToken)
    {
        if (map.NativeObjectBase is null) yield break;
        foreach (var syntax in model.SyntaxTree.GetRoot(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (model.GetOperation(syntax, cancellationToken) is not IInvocationOperation invocation) continue;
            IOperation? typeExpression;
            IOperation? nameExpression;
            var method = invocation.TargetMethod;
            if (method.ContainingType.ToDisplayString() == "HarmonyLib.AccessTools" && method.Name == "Field")
            {
                typeExpression = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)?.Value;
                nameExpression = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 1)?.Value;
            }
            else if (method.ContainingType.ToDisplayString() == "System.Type" && method.Name == "GetField")
            {
                typeExpression = invocation.Instance;
                nameExpression = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)?.Value;
            }
            else continue;
            while (typeExpression is IConversionOperation conversion) typeExpression = conversion.Operand;
            if (typeExpression is not ITypeOfOperation { TypeOperand: INamedTypeSymbol source } || map.IsAuthorType(source)) continue;
            var mapped = map.Resolve(source);
            if (mapped.Status != TypeMappingStatus.Mapped || mapped.Target is null) continue;
            if (nameExpression?.ConstantValue is not { HasValue: true, Value: string name })
            {
                if (nameExpression?.ConstantValue.HasValue == false && !HasOnlyValueAccess(syntax, model))
                {
                    for (INamedTypeSymbol? owner = source; owner is not null; owner = owner.BaseType)
                    {
                        if (map.Resolve(owner).Target is not { } nativeOwner ||
                            !map.ReferenceFields.FieldNames(owner).Any(fieldName => nativeOwner.GetMembers(fieldName).OfType<IPropertySymbol>().Any())) continue;
                        yield return Diagnostic.Create(FieldBecameProperty, syntax.GetLocation(), source.ToDisplayString(), "<computed name>");
                        break;
                    }
                }
                continue;
            }
            var field = FindField(source, name, map, MayIgnoreCase(invocation));
            if (Find(mapped.Target, field?.Name ?? name) is IPropertySymbol property &&
                HasPhysicalField(source, field?.Name ?? name, invocation, map) &&
                !(ResolveLookup(syntax, model, map) is { } resolved &&
                    HasOnlyValueAccess(syntax, model, map, CollectionAdapter(resolved.Field.Type, resolved.Property.Type, map))))
                yield return Diagnostic.Create(FieldBecameProperty, syntax.GetLocation(), source.ToDisplayString(), name);
        }
    }

    public static ExpressionSyntax? Rewrite(InvocationExpressionSyntax syntax, InvocationExpressionSyntax visited, SemanticModel model, MetadataSymbolMap map)
    {
        if (RewriteDynamic(syntax, visited, model, map) is { } dynamicLookup) return dynamicLookup;
        if (ResolveLookup(syntax, model, map) is not { } resolved ||
            !HasOnlyValueAccess(syntax, model, map, CollectionAdapter(resolved.Field.Type, resolved.Property.Type, map))) return null;
        var (invocation, direct, source, fieldName, field, property) = resolved;
        var arguments = new List<ArgumentSyntax> {
                Argument(TypeOfExpression(ParseTypeName(map.TargetDisplay(field.Owner)))),
                Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(field.Name))),
                Argument(CastExpression(ParseTypeName("global::System.Reflection.FieldAttributes"),
                    LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal((int)field.Attributes))))
        };
        if (direct)
        {
            arguments.Insert(0, Argument(TypeOfExpression(ParseTypeName(map.TargetDisplay(source)))));
            var flags = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 1);
            ExpressionSyntax expression = ParseExpression("global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Static");
            if (flags?.Syntax is ArgumentSyntax flagsSyntax)
                expression = visited.ArgumentList.Arguments[syntax.ArgumentList.Arguments.IndexOf(flagsSyntax)].Expression;
            arguments.Add(Argument(expression));
            arguments.Add(Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(fieldName))));
        }
        if (CollectionAdapter(field.Type, property.Type, map) is { } adapter)
        {
            string native = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            arguments.Add(Argument(ParseExpression($"static __value => {adapter}.FromNative(({native})__value)")));
            arguments.Add(Argument(ParseExpression($"static __value => __value == null || __value is {adapter} ? {adapter}.ToNative(({adapter})__value!) : throw new global::System.ArgumentException(\"Value does not match the field type.\")")));
        }
        return InvocationExpression(ParseExpression(NativeFieldInfoSource.TypeName + (direct ? ".Lookup" : ".Create")),
            ArgumentList(SeparatedList(arguments))).WithTriviaFrom(syntax);
    }

    private sealed record Field(INamedTypeSymbol Owner, string Name, ITypeSymbol Type, FieldAttributes Attributes);
    private sealed record ResolvedLookup(IInvocationOperation Invocation, bool Direct, INamedTypeSymbol Source,
        string RequestedName, Field Field, IPropertySymbol Property);

    private static ResolvedLookup? ResolveLookup(InvocationExpressionSyntax syntax, SemanticModel model, MetadataSymbolMap map)
    {
        if (map.NativeObjectBase is null || model.GetOperation(syntax) is not IInvocationOperation invocation) return null;
        bool direct = invocation.TargetMethod.ContainingType.ToDisplayString() == "System.Type" && invocation.TargetMethod.Name == "GetField";
        if (!direct && !(invocation.TargetMethod.ContainingType.ToDisplayString() == "HarmonyLib.AccessTools" && invocation.TargetMethod.Name == "Field")) return null;
        IOperation? type = direct ? invocation.Instance : invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)?.Value;
        while (type is IConversionOperation conversion) type = conversion.Operand;
        var name = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == (direct ? 0 : 1))?.Value.ConstantValue;
        if (type is not ITypeOfOperation { TypeOperand: INamedTypeSymbol source } ||
            name is not { HasValue: true, Value: string fieldName } || map.IsAuthorType(source) ||
            map.Resolve(source) is not { Status: TypeMappingStatus.Mapped, Target: { } target } ||
            FindField(source, fieldName, map, MayIgnoreCase(invocation)) is not { } field || Find(target, field.Name) is not IPropertySymbol property ||
            !CanAdapt(invocation.TargetMethod, source, field, property, map, MayIgnoreCase(invocation))) return null;
        return new(invocation, direct, source, fieldName, field, property);
    }

    internal static string? CollectionAdapter(ITypeSymbol source, ITypeSymbol target, MetadataSymbolMap map)
    {
        if (source is not INamedTypeSymbol authored || target is not INamedTypeSymbol native) return null;
        string? helper = (authored.OriginalDefinition.ToDisplayString(), native.OriginalDefinition.ToDisplayString()) switch
        {
            ("System.Collections.Generic.Dictionary<TKey, TValue>", "Il2CppSystem.Collections.Generic.Dictionary<TKey, TValue>") => NativeDictionarySource.TypeName,
            ("System.Collections.Generic.List<T>", "Il2CppSystem.Collections.Generic.List<T>") => NativeListSource.TypeName,
            _ => null
        };
        if (helper is null || authored.TypeArguments.Length != native.TypeArguments.Length) return null;
        for (int index = 0; index < authored.TypeArguments.Length; index++)
        {
            var argument = authored.TypeArguments[index];
            if (!CollectionStorageAnalysis.IsRepresentable(map, argument) ||
                map.TargetDisplay(argument) != native.TypeArguments[index].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)) return null;
        }
        return helper + "<" + string.Join(", ", authored.TypeArguments.Select(map.TargetDisplay)) + ">";
    }

    internal static bool ReturnsNativeCollectionDescriptor(InvocationExpressionSyntax syntax, SemanticModel model, MetadataSymbolMap map)
    {
        if (ResolveDynamicLookup(syntax, model, map) is not null) return true;
        return ResolveLookup(syntax, model, map) is { } resolved &&
            CollectionAdapter(resolved.Field.Type, resolved.Property.Type, map) is { } adapter &&
            HasOnlyValueAccess(syntax, model, map, adapter);
    }

    private static bool MayIgnoreCase(IInvocationOperation invocation)
    {
        if (invocation.TargetMethod.ContainingType.ToDisplayString() != "System.Type") return false;
        var flags = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 1)?.Value;
        return flags is not null && (flags.ConstantValue is not { HasValue: true, Value: int value } ||
            ((System.Reflection.BindingFlags)value).HasFlag(System.Reflection.BindingFlags.IgnoreCase));
    }

    private static Field? FindField(INamedTypeSymbol source, string name, MetadataSymbolMap map, bool ignoreCase)
    {
        for (INamedTypeSymbol? current = source; current is not null; current = current.BaseType)
        {
            string? actual = ignoreCase ? map.ReferenceFields.FieldNames(current).FirstOrDefault(candidate =>
                string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)) : name;
            if (actual is not null && map.ReferenceFields.TryGetAttributes(current, actual, out var attributes))
                return map.ReferenceFields.FieldType(current, actual) is { } type ? new(current, actual, type, attributes) : null;
        }
        return null;
    }

    private static bool CanAdapt(IMethodSymbol method, INamedTypeSymbol source, Field field, IPropertySymbol property, MetadataSymbolMap map, bool ignoreCase)
    {
        if (!CanAdaptValue(field, property, map)) return false;
        if (method.ContainingType.ToDisplayString() == "HarmonyLib.AccessTools") return true;
        // A single known candidate preserves runtime flag selection. Hidden fields and
        // case-folded ambiguities require a candidate-set lookup, not a guessed winner.
        int candidates = 0;
        for (INamedTypeSymbol? current = source; current is not null; current = current.BaseType)
            candidates += map.ReferenceFields.FieldNames(current).Count(name => string.Equals(name, property.Name, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        return candidates == 1;
    }

    private static bool CanAdaptValue(Field field, IPropertySymbol property, MetadataSymbolMap map)
    {
        if ((field.Attributes & (FieldAttributes.Literal | FieldAttributes.InitOnly)) != 0 ||
            property.GetMethod is null || property.SetMethod is null ||
            field.Attributes.HasFlag(FieldAttributes.Static) != property.IsStatic ||
            !SymbolEqualityComparer.Default.Equals(map.Resolve(field.Owner).Target, property.ContainingType) ||
            map.TargetDisplay(field.Type) != property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) &&
            CollectionAdapter(field.Type, property.Type, map) is null) return false;
        return true;
    }

    private static bool HasOnlyValueAccess(InvocationExpressionSyntax lookup, SemanticModel model, MetadataSymbolMap? map = null, string? collectionAdapter = null)
    {
        SyntaxNode value = lookup;
        while (value.Parent is ParenthesizedExpressionSyntax ||
            value.Parent is BinaryExpressionSyntax coalesce && coalesce.IsKind(SyntaxKind.CoalesceExpression) && coalesce.Left == value && coalesce.Right is ThrowExpressionSyntax ||
            value.Parent is PostfixUnaryExpressionSyntax postfix && postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            value = value.Parent;
        ISymbol? storage = value.Parent switch
        {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variable } => model.GetDeclaredSymbol(variable),
            AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && assignment.Right == value &&
                assignment.Parent is ExpressionStatementSyntax =>
                model.GetSymbolInfo(assignment.Left).Symbol,
            _ => null
        };
        bool cachedField = storage is IFieldSymbol { IsReadOnly: true, DeclaredAccessibility: Accessibility.Private } field &&
            field.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString() == "System.Reflection.FieldInfo" &&
            SymbolEqualityComparer.Default.Equals(field.ContainingAssembly, model.Compilation.Assembly);
        if (storage is not ILocalSymbol && !cachedField) return false;
        storage = storage!.OriginalDefinition;
        // Private readonly caches are closed over the source compilation, including partial
        // declarations. Public/mutable descriptors and metadata/alias escapes stay unsupported.
        var models = cachedField
            ? model.Compilation.SyntaxTrees.Select(tree => model.Compilation.GetSemanticModel(tree))
            : new[] { model };
        foreach (var useModel in models)
        {
            if (collectionAdapter is not null)
            {
                foreach (var declarator in useModel.SyntaxTree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>())
                    if (SymbolEqualityComparer.Default.Equals(useModel.GetDeclaredSymbol(declarator)?.OriginalDefinition, storage) &&
                        declarator.Initializer is { } initializer && !CompatibleWrite(initializer.Value, useModel, map!, collectionAdapter)) return false;
            }
            foreach (var reference in useModel.SyntaxTree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (!SymbolEqualityComparer.Default.Equals(useModel.GetSymbolInfo(reference).Symbol?.OriginalDefinition, storage)) continue;
                ExpressionSyntax use = reference;
                if (cachedField && use.Parent is MemberAccessExpressionSyntax qualified && qualified.Name == use) use = qualified;
                while (use.Parent is ParenthesizedExpressionSyntax parentheses) use = parentheses;
                if (cachedField && use.Parent is AssignmentExpressionSyntax write &&
                    write.IsKind(SyntaxKind.SimpleAssignmentExpression) && write.Left == use && write.Parent is ExpressionStatementSyntax)
                {
                    if (collectionAdapter is not null && !CompatibleWrite(write.Right, useModel, map!, collectionAdapter)) return false;
                    continue;
                }
                if (use.Parent is BinaryExpressionSyntax comparison &&
                    (comparison.IsKind(SyntaxKind.EqualsExpression) || comparison.IsKind(SyntaxKind.NotEqualsExpression)))
                {
                    var other = comparison.Left == use ? comparison.Right : comparison.Left;
                    if (other.IsKind(SyntaxKind.NullLiteralExpression)) continue;
                }
                if (use.Parent is IsPatternExpressionSyntax pattern && IsNullPattern(pattern.Pattern)) continue;
                if (use.Parent is ConditionalAccessExpressionSyntax { WhenNotNull: InvocationExpressionSyntax conditional } &&
                    IsValueAccess(conditional, useModel, collectionAdapter is not null)) continue;
                if (use.Parent is PostfixUnaryExpressionSyntax suppression && suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression)) use = suppression;
                if (use.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax access } member &&
                    member.Expression == use && IsValueAccess(access, useModel, collectionAdapter is not null)) continue;
                return false;
            }
        }
        return true;
    }

    private static bool CompatibleWrite(ExpressionSyntax expression, SemanticModel model, MetadataSymbolMap map, string adapter)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesis) expression = parenthesis.Expression;
        if (expression.IsKind(SyntaxKind.NullLiteralExpression)) return true;
        return expression is InvocationExpressionSyntax invocation && ResolveLookup(invocation, model, map) is { } resolved &&
            CollectionAdapter(resolved.Field.Type, resolved.Property.Type, map) == adapter;
    }

    private static bool IsValueAccess(InvocationExpressionSyntax access, SemanticModel model, bool collection)
    {
        if (model.GetOperation(access) is not IInvocationOperation invocation ||
            invocation.TargetMethod.ContainingType.ToDisplayString() != "System.Reflection.FieldInfo") return false;
        var operation = invocation.TargetMethod;
        if (operation.Name == "GetValue" && operation.Parameters.Length == 1) return true;
        if (operation.Name != "SetValue" || operation.Parameters.Length != 2) return false;
        if (collection && invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 1)?.Value is { } value)
        {
            while (value is IConversionOperation { IsImplicit: true } conversion) value = conversion.Operand;
            if (value.Type is INamedTypeSymbol named)
                for (var parent = named.BaseType; parent is not null; parent = parent.BaseType)
                    if (parent.OriginalDefinition.ToDisplayString() is "System.Collections.Generic.List<T>" or "System.Collections.Generic.Dictionary<TKey, TValue>")
                        return false; // Derived CLR collection identity cannot be represented by a sealed native view.
        }
        return true;
    }

    private static bool IsNullPattern(PatternSyntax pattern) => pattern switch
    {
        ConstantPatternSyntax constant => constant.Expression.IsKind(SyntaxKind.NullLiteralExpression),
        UnaryPatternSyntax unary when unary.IsKind(SyntaxKind.NotPattern) => IsNullPattern(unary.Pattern),
        ParenthesizedPatternSyntax parenthesized => IsNullPattern(parenthesized.Pattern),
        _ => false
    };

    private static ISymbol? Find(INamedTypeSymbol type, string name)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
            if (current.GetMembers(name).FirstOrDefault(member => member is IFieldSymbol or IPropertySymbol) is { } member)
                return member;
        return null;
    }

    private static bool HasPhysicalField(INamedTypeSymbol type, string name, IInvocationOperation lookup, MetadataSymbolMap map)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (!map.ReferenceFields.TryGetAttributes(current, name, out var attributes)) continue;
            if (lookup.TargetMethod.ContainingType.ToDisplayString() == "HarmonyLib.AccessTools") return true;
            var flagsValue = lookup.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 1)?.Value;
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
            if (flagsValue is not null)
            {
                if (flagsValue.ConstantValue is not { HasValue: true, Value: int value }) return true;
                flags = (System.Reflection.BindingFlags)value;
            }
            bool inherited = !SymbolEqualityComparer.Default.Equals(type, current);
            var visibility = attributes & FieldAttributes.FieldAccessMask;
            if (inherited && (flags.HasFlag(System.Reflection.BindingFlags.DeclaredOnly) || visibility == FieldAttributes.Private)) continue;
            bool isStatic = attributes.HasFlag(FieldAttributes.Static);
            if (inherited && isStatic && !flags.HasFlag(System.Reflection.BindingFlags.FlattenHierarchy)) continue;
            if (!flags.HasFlag(isStatic ? System.Reflection.BindingFlags.Static : System.Reflection.BindingFlags.Instance)) continue;
            if (!flags.HasFlag(visibility == FieldAttributes.Public ? System.Reflection.BindingFlags.Public : System.Reflection.BindingFlags.NonPublic)) continue;
            return true;
        }
        return false;
    }
}
