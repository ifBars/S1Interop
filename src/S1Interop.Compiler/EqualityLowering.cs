using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace S1Interop.Compiler;

/// <summary>Preserves native default equality at managed collection and LINQ boundaries.</summary>
internal sealed class EqualityLowering(SemanticModel model, MetadataSymbolMap map)
{
    public bool Used { get; private set; }

    public ExpressionSyntax Rewrite(ExpressionSyntax original, ExpressionSyntax visited)
    {
        if (original.Parent is MemberAccessExpressionSyntax member && member.Name == original ||
            original.Parent is QualifiedNameSyntax or AliasQualifiedNameSyntax or MemberBindingExpressionSyntax)
            return visited;
        if (model.GetSymbolInfo(original).Symbol is IPropertySymbol { Name: "Default" } property &&
            property.ContainingType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.EqualityComparer<T>" &&
            map.IsNative(property.ContainingType.TypeArguments[0]))
            return Default(property.ContainingType.TypeArguments[0]).WithTriviaFrom(original);

        if (model.GetOperation(original) is IObjectCreationOperation { Type: INamedTypeSymbol type } &&
            type.OriginalDefinition.ToDisplayString() is "System.Collections.Generic.HashSet<T>" or "System.Collections.Generic.Dictionary<TKey, TValue>" &&
            map.IsNative(type.TypeArguments[0]))
        {
            return (original, visited) switch
            {
                (ObjectCreationExpressionSyntax source, ObjectCreationExpressionSyntax target) =>
                    target.WithArgumentList(WithComparer(source.ArgumentList, target.ArgumentList, type.TypeArguments[0])),
                (ImplicitObjectCreationExpressionSyntax source, ImplicitObjectCreationExpressionSyntax target) =>
                    target.WithArgumentList(WithComparer(source.ArgumentList, target.ArgumentList, type.TypeArguments[0])),
                _ => visited
            };
        }

        if (original is InvocationExpressionSyntax invocation && visited is InvocationExpressionSyntax lowered &&
            model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method && method.ContainingType.ToDisplayString() == "System.Linq.Enumerable")
        {
            int keyIndex = method.Name switch
            {
                "Contains" or "Distinct" or "Union" or "Intersect" or "Except" or "ToHashSet" or "SequenceEqual" => 0,
                "GroupBy" or "ToDictionary" or "ToLookup" or "DistinctBy" or "UnionBy" or "IntersectBy" or "ExceptBy" => 1,
                "Join" or "GroupJoin" => 2,
                _ => -1
            };
            if (keyIndex >= 0 && keyIndex < method.TypeArguments.Length && map.IsNative(method.TypeArguments[keyIndex]))
                return lowered.WithArgumentList(WithComparer(invocation.ArgumentList, lowered.ArgumentList, method.TypeArguments[keyIndex]));
        }
        return visited;
    }

    private ArgumentListSyntax WithComparer(ArgumentListSyntax? original, ArgumentListSyntax? visited, ITypeSymbol key)
    {
        visited ??= ArgumentList();
        if (original is not null)
        {
            for (int i = 0; i < original.Arguments.Count; i++)
            {
                if (model.GetOperation(original.Arguments[i]) is not IArgumentOperation argument ||
                    argument.Parameter?.Type is not INamedTypeSymbol parameter ||
                    parameter.OriginalDefinition.ToDisplayString() != "System.Collections.Generic.IEqualityComparer<T>") continue;
                var target = visited.Arguments[i];
                ExpressionSyntax fallback = BinaryExpression(SyntaxKind.CoalesceExpression,
                    ParenthesizedExpression(target.Expression.WithoutTrivia()), Default(key));
                return visited.WithArguments(visited.Arguments.Replace(target, target.WithExpression(fallback.WithTriviaFrom(target.Expression))));
            }
        }
        return visited.AddArguments(Argument(Default(key)).WithNameColon(NameColon("comparer")));
    }

    private ExpressionSyntax Default(ITypeSymbol key)
    {
        Used = true;
        return ParseExpression(NativeEqualityComparerSource.TypeName + "<" + map.TargetDisplay(key) + ">.Instance");
    }
}
