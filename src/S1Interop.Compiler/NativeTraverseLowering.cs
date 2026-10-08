using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace S1Interop.Compiler;

/// <summary>Adapts immediate Harmony field reads when metadata proves the native property representation.</summary>
internal static class NativeTraverseLowering
{
    private static readonly DiagnosticDescriptor Unsupported = new("S1IC041", "Native traversal shape changed",
        "Harmony field traversal '{0}' targets a native property; this traversal usage is not yet adapted",
        "S1Interop.Compiler", DiagnosticSeverity.Error, true);

    public static IEnumerable<Diagnostic> Verify(SemanticModel model, MetadataSymbolMap map, CancellationToken token)
    {
        if (map.NativeObjectBase is null) yield break;
        foreach (var syntax in model.SyntaxTree.GetRoot(token).DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (model.GetOperation(syntax, token) is not IInvocationOperation field || !IsTraverse(field, "Field") ||
                field.Arguments.Length != 1 || field.Arguments[0].Value.ConstantValue is not { HasValue: true, Value: string name } ||
                field.Instance is not IInvocationOperation create || !IsTraverse(create, "Create") || create.Arguments.Length != 1) continue;
            IOperation root = create.Arguments[0].Value;
            while (root is IConversionOperation { IsImplicit: true } conversion &&
                (conversion.Conversion.IsReference || conversion.Conversion.IsIdentity)) root = conversion.Operand;
            var source = root is ITypeOfOperation typeOf ? typeOf.TypeOperand as INamedTypeSymbol : root.Type as INamedTypeSymbol;
            if (source is null || map.IsAuthorType(source) || map.Resolve(source).Status != TypeMappingStatus.Mapped) continue;
            for (INamedTypeSymbol? owner = source; owner is not null; owner = owner.BaseType)
            {
                if (!map.ReferenceFields.TryGetAttributes(owner, name, out _)) continue;
                if (map.Resolve(owner).Target?.GetMembers(name).OfType<IPropertySymbol>().Any() == true &&
                    !(syntax.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax read } &&
                      Rewrite(read, read, model, map) is not null))
                    yield return Diagnostic.Create(Unsupported, syntax.GetLocation(), source.ToDisplayString() + "." + name);
                break;
            }
        }
    }

    public static ExpressionSyntax? Rewrite(InvocationExpressionSyntax syntax, InvocationExpressionSyntax visited,
        SemanticModel model, MetadataSymbolMap map)
    {
        if (map.NativeObjectBase is null || model.GetOperation(syntax) is not IInvocationOperation read ||
            !IsTraverse(read, "GetValue") || read.TargetMethod.TypeArguments.Length != 1 || read.Arguments.Length != 0 ||
            read.Instance is not IInvocationOperation field || !IsTraverse(field, "Field") ||
            field.TargetMethod.TypeArguments.Length != 0 || field.Arguments.Length != 1 ||
            field.Arguments[0].Value.ConstantValue is not { HasValue: true, Value: string name } ||
            field.Instance is not IInvocationOperation create || !IsTraverse(create, "Create") ||
            create.Arguments.Length != 1) return null;

        IOperation root = create.Arguments[0].Value;
        while (root is IConversionOperation { IsImplicit: true } conversion &&
            (conversion.Conversion.IsReference || conversion.Conversion.IsIdentity)) root = conversion.Operand;
        var source = root is ITypeOfOperation typeOf ? typeOf.TypeOperand as INamedTypeSymbol : root.Type as INamedTypeSymbol;
        if (source is null || map.IsAuthorType(source) || map.Resolve(source).Status != TypeMappingStatus.Mapped) return null;

        for (INamedTypeSymbol? owner = source; owner is not null; owner = owner.BaseType)
        {
            if (!map.ReferenceFields.TryGetAttributes(owner, name, out var attributes)) continue;
            if (map.Resolve(owner).Target is not { } target ||
                map.ReferenceFields.FieldType(owner, name) is not { } fieldType ||
                target.GetMembers(name).OfType<IPropertySymbol>().Where(property => !property.IsIndexer).ToArray() is not [ { GetMethod: not null } property ] ||
                property.IsStatic != attributes.HasFlag(System.Reflection.FieldAttributes.Static) ||
                map.TargetDisplay(fieldType) != property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)) return null;

            var resultType = SyntaxFactory.ParseTypeName(map.TargetDisplay(read.TargetMethod.TypeArguments[0]));
            // Keep the root expression once. Native metadata chooses the actual field;
            // looking up a property by name alone could select a derived source property.
            if (visited.Expression is not MemberAccessExpressionSyntax
                { Expression: InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax
                    { Expression: InvocationExpressionSyntax createCall } } })
                return null;
            var convert = SyntaxFactory.ParseExpression(NativeFieldInfoSource.TypeName + ".ReadTraverse<" + resultType + ">");
            return SyntaxFactory.InvocationExpression(convert, SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList(new[] {
                    SyntaxFactory.Argument(createCall.ArgumentList.Arguments[0].Expression),
                    SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(name))),
                    SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(root is ITypeOfOperation ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression))
                }))).WithTriviaFrom(syntax);
        }
        return null;
    }

    private static bool IsTraverse(IInvocationOperation invocation, string name) =>
        invocation.TargetMethod.ContainingType.ToDisplayString() == "HarmonyLib.Traverse" && invocation.TargetMethod.Name == name;
}
