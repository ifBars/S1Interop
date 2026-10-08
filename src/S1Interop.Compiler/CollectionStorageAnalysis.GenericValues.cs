using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace S1Interop.Compiler;

internal sealed partial class CollectionStorageAnalysis
{
    private readonly Dictionary<(InvocationExpressionSyntax Call, int Ordinal), int> genericCallSlots = new();

    // Each call can instantiate T with a different storage representation. Only storage
    // required by the helper body propagates to every call of that helper.
    private void ConnectGenericValueStorage(Compilation author, CancellationToken cancellationToken)
    {
        var edges = new List<(int Definition, int Constructed)>();
        var calls = new List<List<(int Definition, int Constructed)>>();
        foreach (var tree in author.SyntaxTrees)
        {
            var model = author.GetSemanticModel(tree);
            foreach (var call in tree.GetRoot(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetOperation(call, cancellationToken) is not IInvocationOperation invocation ||
                    !invocation.TargetMethod.IsGenericMethod || !map.IsAuthorType(invocation.TargetMethod.ContainingType)) continue;
                var definition = invocation.TargetMethod.OriginalDefinition;
                var ports = new List<(int Definition, int Constructed)>();
                void Port(ITypeSymbol type, ISymbol symbol, ExpressionSyntax value)
                {
                    if (type is not ITypeParameterSymbol parameter ||
                        !SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, definition)) return;
                    var key = (call, parameter.Ordinal);
                    if (!genericCallSlots.TryGetValue(key, out int slot)) genericCallSlots.Add(key, slot = NewNode());
                    Join(slot, Expression(value, model));
                    ports.Add((Symbol(symbol), slot));
                }
                Port(definition.ReturnType, definition, call);
                if (call.Parent is ConditionalAccessExpressionSyntax conditional && conditional.WhenNotNull == call)
                    Join(Expression(call, model), Expression(conditional, model));
                foreach (var argument in invocation.Arguments)
                {
                    if (argument.ArgumentKind == ArgumentKind.DefaultValue || argument.Parameter is not { } parameter ||
                        argument.Value.Syntax is not ExpressionSyntax value) continue;
                    var original = parameter.OriginalDefinition;
                    // IInvocationOperation includes the implicit receiver of a reduced extension call.
                    Port(original.Type, original, value);
                }
                calls.Add(ports);
                edges.AddRange(ports);
            }
        }
        bool changed;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            changed = false;
            // A nested helper can connect definition slots after its caller was visited.
            foreach (var ports in calls)
                for (int left = 0; left < ports.Count; left++)
                    for (int right = left + 1; right < ports.Count; right++)
                        if (Root(ports[left].Definition) == Root(ports[right].Definition) &&
                            Root(ports[left].Constructed) != Root(ports[right].Constructed))
                        {
                            Join(ports[left].Constructed, ports[right].Constructed);
                            changed = true;
                        }
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
        TypeSyntax argument = syntax;
        while (argument.Parent is NullableTypeSyntax or QualifiedNameSyntax or AliasQualifiedNameSyntax)
            argument = (TypeSyntax)argument.Parent;
        if (argument.Parent is not TypeArgumentListSyntax arguments || arguments.Parent is not GenericNameSyntax generic ||
            model.GetSymbolInfo(generic).Symbol is not IMethodSymbol method || !map.IsAuthorType(method.ContainingType)) return false;
        SyntaxNode invoked = generic;
        if (invoked.Parent is MemberAccessExpressionSyntax member && member.Name == invoked) invoked = member;
        else if (invoked.Parent is MemberBindingExpressionSyntax binding && binding.Name == invoked) invoked = binding;
        return invoked.Parent is InvocationExpressionSyntax call &&
            genericCallSlots.TryGetValue((call, arguments.Arguments.IndexOf(argument)), out int node) && nativeRoots.Contains(Root(node));
    }
}
