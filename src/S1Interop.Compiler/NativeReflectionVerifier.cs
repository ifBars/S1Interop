using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.CSharp;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using FieldAttributes = System.Reflection.FieldAttributes;

namespace S1Interop.Compiler;

internal static class NativeReflectionVerifier
{
    private static readonly DiagnosticDescriptor FieldBecameProperty = new("S1IC034", "Native reflection shape changed",
        "Field lookup '{0}.{1}' targets an IL2CPP property; FieldInfo adaptation is not implemented and this lookup would lose the field at runtime",
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
            if (typeExpression is not ITypeOfOperation { TypeOperand: INamedTypeSymbol source } ||
                nameExpression?.ConstantValue is not { HasValue: true, Value: string name } || map.IsAuthorType(source)) continue;
            var mapped = map.Resolve(source);
            if (mapped.Status != TypeMappingStatus.Mapped || mapped.Target is null) continue;
            if (Find(source, name) is IFieldSymbol field && Find(mapped.Target, name) is IPropertySymbol property &&
                !(CanAdapt(method, field, property, map) && HasOnlyValueAccess(syntax, model)))
                yield return Diagnostic.Create(FieldBecameProperty, syntax.GetLocation(), source.ToDisplayString(), name);
        }
    }

    public static ExpressionSyntax? Rewrite(InvocationExpressionSyntax syntax, SemanticModel model, MetadataSymbolMap map)
    {
        if (map.NativeObjectBase is null || model.GetOperation(syntax) is not IInvocationOperation invocation ||
            invocation.TargetMethod.ContainingType.ToDisplayString() != "HarmonyLib.AccessTools" ||
            invocation.TargetMethod.Name != "Field") return null;
        IOperation? type = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)?.Value;
        while (type is IConversionOperation conversion) type = conversion.Operand;
        var name = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 1)?.Value.ConstantValue;
        if (type is not ITypeOfOperation { TypeOperand: INamedTypeSymbol source } ||
            name is not { HasValue: true, Value: string fieldName } || map.IsAuthorType(source) ||
            map.Resolve(source) is not { Status: TypeMappingStatus.Mapped, Target: { } target } ||
            Find(source, fieldName) is not IFieldSymbol field || Find(target, fieldName) is not IPropertySymbol property ||
            !CanAdapt(invocation.TargetMethod, field, property, map) || !HasOnlyValueAccess(syntax, model)) return null;
        FieldAttributes attributes = field.DeclaredAccessibility switch
        {
            Accessibility.Public => FieldAttributes.Public,
            Accessibility.Private => FieldAttributes.Private,
            Accessibility.Protected => FieldAttributes.Family,
            Accessibility.Internal => FieldAttributes.Assembly,
            Accessibility.ProtectedOrInternal => FieldAttributes.FamORAssem,
            Accessibility.ProtectedAndInternal => FieldAttributes.FamANDAssem,
            _ => FieldAttributes.PrivateScope
        };
        if (field.IsStatic) attributes |= FieldAttributes.Static;
        return InvocationExpression(ParseExpression(NativeFieldInfoSource.TypeName + ".Create"),
            ArgumentList(SeparatedList(new[] {
                Argument(TypeOfExpression(ParseTypeName(map.TargetDisplay(field.ContainingType)))),
                Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(fieldName))),
                Argument(CastExpression(ParseTypeName("global::System.Reflection.FieldAttributes"),
                    LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal((int)attributes))))
            }))).WithTriviaFrom(syntax);
    }

    private static bool CanAdapt(IMethodSymbol method, IFieldSymbol field, IPropertySymbol property, MetadataSymbolMap map) =>
        method.ContainingType.ToDisplayString() == "HarmonyLib.AccessTools" && method.Name == "Field" &&
        !field.IsConst && !field.IsReadOnly && property.GetMethod is not null && property.SetMethod is not null &&
        field.IsStatic == property.IsStatic &&
        SymbolEqualityComparer.Default.Equals(map.Resolve(field.ContainingType).Target, property.ContainingType) &&
        map.TargetDisplay(field.Type) == property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static bool HasOnlyValueAccess(InvocationExpressionSyntax lookup, SemanticModel model)
    {
        if (lookup.Parent is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variable } ||
            model.GetDeclaredSymbol(variable) is not ILocalSymbol local) return false;
        foreach (var reference in model.SyntaxTree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (!SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(reference).Symbol, local)) continue;
            if (reference.Parent is BinaryExpressionSyntax comparison &&
                (comparison.IsKind(SyntaxKind.EqualsExpression) || comparison.IsKind(SyntaxKind.NotEqualsExpression)))
            {
                var other = comparison.Left == reference ? comparison.Right : comparison.Left;
                if (other.IsKind(SyntaxKind.NullLiteralExpression)) continue;
            }
            if (reference.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax access } member &&
                member.Expression == reference && model.GetSymbolInfo(access).Symbol is IMethodSymbol operation &&
                operation.ContainingType.ToDisplayString() == "System.Reflection.FieldInfo" &&
                (operation.Name == "GetValue" && operation.Parameters.Length == 1 ||
                 operation.Name == "SetValue" && operation.Parameters.Length == 2)) continue;
            return false;
        }
        return true;
    }

    private static ISymbol? Find(INamedTypeSymbol type, string name)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
            if (current.GetMembers(name).FirstOrDefault(member => member is IFieldSymbol or IPropertySymbol) is { } member)
                return member;
        return null;
    }
}
