using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace S1Interop.Compiler;

internal sealed partial class CollectionStorageAnalysis
{
    private readonly Dictionary<(int Owner, int Position), int> delegateSlots = new();

    private void ConnectDelegateStorage(Compilation author, CancellationToken cancellationToken)
    {
        // Finish delegate-owner equivalence before slot keys are indexed by those roots.
        foreach (var tree in author.SyntaxTrees)
        {
            var model = author.GetSemanticModel(tree);
            foreach (var expression in tree.GetRoot(cancellationToken).DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
                if (model.GetOperation(expression, cancellationToken) is IDelegateCreationOperation creation &&
                    creation.Target.Syntax is ExpressionSyntax target)
                    Join(Expression(expression, model), Expression(target, model));
        }
        // Delegate values already share the ordinary assignment/argument graph. Their invoke
        // slots are separate nodes: two unrelated Func<byte[], int> values need not share storage.
        foreach (var tree in author.SyntaxTrees)
        {
            var model = author.GetSemanticModel(tree);
            var targetSlots = new DelegateConversions(model, map, System.Collections.Immutable.ImmutableArray.CreateBuilder<Diagnostic>());
            foreach (var expression in tree.GetRoot(cancellationToken).DescendantNodes().OfType<ExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (expression is InvocationExpressionSyntax invocation &&
                    model.GetOperation(invocation) is IInvocationOperation { TargetMethod.MethodKind: MethodKind.DelegateInvoke } call &&
                    DelegateReceiver(invocation, model) is { } receiver &&
                    BuiltInDelegate(call.TargetMethod.ContainingType))
                {
                    int owner = Root(Expression(receiver, model));
                    foreach (var argument in call.Arguments)
                        if (argument.Parameter is { } parameter && Eligible(parameter.Type) &&
                            argument.Syntax is ArgumentSyntax syntax)
                            Join(DelegateSlot(owner, parameter.Ordinal), Expression(syntax.Expression, model));
                    if (Eligible(call.TargetMethod.ReturnType)) Join(DelegateSlot(owner, -1), Expression(invocation, model));
                }

                if (model.GetTypeInfo(expression).ConvertedType is not INamedTypeSymbol { DelegateInvokeMethod: { } invoke } type ||
                    !BuiltInDelegate(type)) continue;
                int valueOwner = Root(Expression(expression, model));
                foreach (var target in new[] { targetSlots.ExpectedTargetType(expression), targetSlots.InferredTargetType(expression) })
                {
                    if (target?.DelegateInvokeMethod is not { } targetInvoke || targetInvoke.Parameters.Length != invoke.Parameters.Length) continue;
                    for (int index = 0; index < invoke.Parameters.Length; index++)
                        if (Eligible(invoke.Parameters[index].Type) &&
                            NativeSlot(targetInvoke.Parameters[index].Type as INamedTypeSymbol, invoke.Parameters[index].Type))
                            seeds.Add(DelegateSlot(valueOwner, index));
                    if (Eligible(invoke.ReturnType) && NativeSlot(targetInvoke.ReturnType as INamedTypeSymbol, invoke.ReturnType))
                        seeds.Add(DelegateSlot(valueOwner, -1));
                }
                IMethodSymbol? implementation = model.GetOperation(expression) switch
                {
                    IAnonymousFunctionOperation lambda => lambda.Symbol,
                    IMethodReferenceOperation method => model.GetSymbolInfo(method.Syntax).Symbol as IMethodSymbol ?? method.Method,
                    IDelegateCreationOperation { Target: IMethodReferenceOperation method } => model.GetSymbolInfo(method.Syntax).Symbol as IMethodSymbol ?? method.Method,
                    IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda } => lambda.Symbol,
                    _ => null
                };
                if (implementation is null || !map.IsAuthorType(implementation.ContainingType) ||
                    implementation.Parameters.Length != invoke.Parameters.Length) continue;
                for (int index = 0; index < invoke.Parameters.Length; index++)
                    if (Eligible(invoke.Parameters[index].Type) &&
                        SymbolEqualityComparer.Default.Equals(invoke.Parameters[index].Type, implementation.Parameters[index].Type))
                        Join(DelegateSlot(valueOwner, index), Symbol(implementation.ReducedFrom is { } extension
                            ? extension.Parameters[index + 1] : implementation.Parameters[index]));
                if (Eligible(invoke.ReturnType) && SymbolEqualityComparer.Default.Equals(invoke.ReturnType, implementation.ReturnType))
                {
                    Join(DelegateSlot(valueOwner, -1), Symbol(implementation.ReducedFrom ?? implementation));
                    if (expression is LambdaExpressionSyntax { Body: ExpressionSyntax body })
                        Join(DelegateSlot(valueOwner, -1), Expression(body, model));
                }
            }
        }
    }

    private int DelegateSlot(int owner, int position)
    {
        var key = (Root(owner), position);
        if (!delegateSlots.TryGetValue(key, out int node)) delegateSlots.Add(key, node = NewNode());
        return node;
    }

    private bool BuiltInDelegate(INamedTypeSymbol type) => !map.IsAuthorType(type) && type.TypeKind == TypeKind.Delegate &&
        type.ContainingNamespace.ToDisplayString() == "System" && type.Name is "Func" or "Action";

    private static ExpressionSyntax? DelegateReceiver(InvocationExpressionSyntax invocation, SemanticModel model) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Invoke" } member => member.Expression,
        MemberBindingExpressionSyntax { Name.Identifier.ValueText: "Invoke" } when
            invocation.Parent is ConditionalAccessExpressionSyntax conditional &&
            model.GetTypeInfo(conditional.Expression).Type?.TypeKind == TypeKind.Delegate => conditional.Expression,
        _ => invocation.Expression
    };

    private bool IsNativeDelegateTypeArgument(TypeSyntax syntax, SemanticModel model)
    {
        if (syntax.Parent is not TypeArgumentListSyntax arguments || arguments.Parent is not GenericNameSyntax generic ||
            (model.GetTypeInfo(generic).Type ?? model.GetSymbolInfo(generic).Symbol as ITypeSymbol) is not INamedTypeSymbol type || !BuiltInDelegate(type)) return false;
        int position = arguments.Arguments.IndexOf(syntax);
        if (type.Name == "Func" && position == type.TypeArguments.Length - 1) position = -1;
        SyntaxNode owner = generic;
        while (owner.Parent is QualifiedNameSyntax or AliasQualifiedNameSyntax or NullableTypeSyntax) owner = owner.Parent;
        ISymbol? symbol = owner.Parent switch
        {
            VariableDeclarationSyntax variables => model.GetDeclaredSymbol(variables.Variables[0]),
            ParameterSyntax parameter => model.GetDeclaredSymbol(parameter),
            PropertyDeclarationSyntax property => model.GetDeclaredSymbol(property),
            MethodDeclarationSyntax method when method.ReturnType == owner => model.GetDeclaredSymbol(method),
            LocalFunctionStatementSyntax method when method.ReturnType == owner => model.GetDeclaredSymbol(method),
            _ => null
        };
        int value = -1;
        bool found = symbol is not null && symbols.TryGetValue(StorageDefinition(symbol), out value);
        if (!found && owner.Parent is ObjectCreationExpressionSyntax creation) found = expressions.TryGetValue(creation, out value);
        else if (!found) value = -1;
        return found &&
            delegateSlots.TryGetValue((Root(value), position), out int slot) && nativeRoots.Contains(Root(slot));
    }
}
