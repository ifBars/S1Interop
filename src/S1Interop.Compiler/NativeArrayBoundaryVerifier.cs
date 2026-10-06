using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace S1Interop.Compiler;

/// <summary>Rejects array snapshot conversions after binding, including conversions hidden by inferred types.</summary>
internal static class NativeArrayBoundaryVerifier
{
    public static IEnumerable<Diagnostic> Verify(Compilation compilation, CancellationToken cancellationToken)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var walker = new ConversionWalker(cancellationToken);
            foreach (var node in tree.GetRoot(cancellationToken).DescendantNodesAndSelf())
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Walking only operation roots visits implicit argument and return conversions too.
                if (model.GetOperation(node, cancellationToken) is { Parent: null } operation)
                    walker.Visit(operation);
            }
            foreach (var diagnostic in walker.Diagnostics) yield return diagnostic;
        }
    }

    private sealed class ConversionWalker(CancellationToken cancellationToken) : OperationWalker
    {
        private readonly HashSet<Microsoft.CodeAnalysis.Text.TextSpan> reported = [];
        public List<Diagnostic> Diagnostics { get; } = [];

        public override void VisitConversion(IConversionOperation operation)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DelegateConversions.IsNativeArray(operation.OperatorMethod?.ContainingType) &&
                (operation.Type is IArrayTypeSymbol || operation.Operand.Type is IArrayTypeSymbol) &&
                !IsFreshExactArray(operation) &&
                reported.Add(operation.Syntax.Span))
                Diagnostics.Add(Diagnostic.Create(LoweringDiagnostics.UnsupportedNativeArray,
                    operation.Syntax.GetLocation(), operation.Syntax.ToString()));
            base.VisitConversion(operation);
        }

        private static bool IsFreshExactArray(IConversionOperation operation) =>
            // A directly created array has no other owner. Copying it into native storage cannot
            // detach an existing alias. Do not allow intervening covariance conversions: they can
            // lose the runtime element type and change array-store checks.
            operation.Operand is IArrayCreationOperation { Type: IArrayTypeSymbol { Rank: 1 } array } &&
            operation.OperatorMethod is { Parameters.Length: 1 } method &&
            SymbolEqualityComparer.Default.Equals(array, method.Parameters[0].Type) &&
            // The stock Il2CppReferenceArray<T>(T[]) conversion writes element slots without a runtime check, so a native
            // class element array is never exempt: lowered code populates fresh reference arrays through the checked store.
            !IsNativeClassElement(array.ElementType);

        private static bool IsNativeClassElement(ITypeSymbol element)
        {
            for (ITypeSymbol? current = element.BaseType; current is not null; current = current.BaseType)
                if (current.ToDisplayString() == "Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase") return true;
            return false;
        }
    }
}
