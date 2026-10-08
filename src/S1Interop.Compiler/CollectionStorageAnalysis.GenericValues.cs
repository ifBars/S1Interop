using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace S1Interop.Compiler;

internal sealed partial class CollectionStorageAnalysis
{
    // A generic helper whose T slot holds native storage must carry that representation
    // through each constructed call, including calls that infer T from an argument.
    private void ConnectGenericValueStorage(Compilation author, CancellationToken cancellationToken)
    {
        var edges = new List<(int Definition, int Constructed)>();
        foreach (var tree in author.SyntaxTrees)
        {
            var model = author.GetSemanticModel(tree);
            foreach (var call in tree.GetRoot(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(call, cancellationToken).Symbol is not IMethodSymbol { IsGenericMethod: true } method ||
                    !map.IsAuthorType(method.ContainingType)) continue;
                var definition = method.OriginalDefinition;
                if (definition.ReturnType is ITypeParameterSymbol)
                    edges.Add((Symbol(definition), Expression(call, model)));
                foreach (var parameter in method.Parameters)
                    if (parameter.OriginalDefinition.Type is ITypeParameterSymbol)
                        edges.Add((Symbol(parameter.OriginalDefinition), Symbol(parameter)));
            }
        }
        bool changed;
        do
        {
            changed = false;
            var roots = seeds.Select(Root).ToHashSet();
            foreach (var edge in edges)
            {
                if (Root(edge.Definition) == Root(edge.Constructed) || !roots.Contains(Root(edge.Definition))) continue;
                Join(edge.Definition, edge.Constructed);
                changed = true;
            }
        } while (changed);
    }

    private bool IsNativeMethodTypeArgument(TypeSyntax syntax, SemanticModel model)
    {
        if (syntax.Parent is not TypeArgumentListSyntax arguments || arguments.Parent is not GenericNameSyntax generic ||
            model.GetSymbolInfo(generic).Symbol is not IMethodSymbol method || !map.IsAuthorType(method.ContainingType)) return false;
        int index = arguments.Arguments.IndexOf(syntax);
        if (index < 0) return false;
        var definition = method.OriginalDefinition;
        bool Connected(ISymbol symbol) => symbols.TryGetValue(StorageDefinition(symbol), out int node) && nativeRoots.Contains(Root(node));
        bool Matches(ITypeSymbol type) => type is ITypeParameterSymbol parameter &&
            SymbolEqualityComparer.Default.Equals(parameter, definition.TypeParameters[index]);
        return Matches(definition.ReturnType) && Connected(definition) ||
            definition.Parameters.Any(parameter => Matches(parameter.Type) && Connected(parameter));
    }
}
