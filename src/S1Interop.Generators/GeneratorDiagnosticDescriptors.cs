using Microsoft.CodeAnalysis;

namespace S1Interop.Generators;

internal static class GeneratorDiagnosticDescriptors
{
#pragma warning disable RS2008
    public static readonly DiagnosticDescriptor TypeNotFoundDiagnostic = new(
        "S1I001",
        "S1Interop type was not found",
        "S1Interop type '{0}' for alias '{1}' was not found in referenced {2} assemblies",
        "S1Interop",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MemberOwnerNotFoundDiagnostic = new(
        "S1I002",
        "S1Interop member owner was not declared",
        "S1Interop member '{0}' references owner alias '{1}', but no S1InteropType declaration with that alias was found",
        "S1Interop",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MemberNotFoundDiagnostic = new(
        "S1I003",
        "S1Interop member was not found",
        "S1Interop member '{0}' for owner alias '{1}' was not found on type '{2}' in referenced {3} assemblies",
        "S1Interop",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
#pragma warning restore RS2008
}
