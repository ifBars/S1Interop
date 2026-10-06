using Microsoft.CodeAnalysis;

namespace S1Interop.Compiler;

internal static class LoweringDiagnostics
{
    private const string Category = "S1Interop.Compiler";

    public static readonly DiagnosticDescriptor UnsupportedNativeArray = Error(
        "S1IC032",
        "Native array storage is not lowered",
        "Array boundary '{0}' requires shared native storage; implicit CLR/IL2CPP array conversions copy elements and do not preserve aliases. This array shape is not lowered to native storage (only rank-1 primitive scalar arrays connected to an Il2CppStructArray slot, and rank-1 arrays of mapped native reference classes connected to an Il2CppReferenceArray slot, are)");

    public static readonly DiagnosticDescriptor UnsupportedNativeArrayOperation = Error(
        "S1IC033",
        "Native array operation is not lowered",
        "Operation '{0}' on a native-backed array is not supported: {1}");

    public static readonly DiagnosticDescriptor MissingTargetType = Error(
        "S1IC001",
        "Type has no target counterpart",
        "'{0}' from '{1}' has no counterpart in the target references (looked up {2})");

    public static readonly DiagnosticDescriptor AmbiguousTargetType = Error(
        "S1IC002",
        "Type maps to several target types",
        "'{0}' maps to several target types: {1}");

    public static readonly DiagnosticDescriptor NativeSubclassNotLowered = Error(
        "S1IC010",
        "Source subclass of a native type is not lowered",
        "'{0}' derives from native type '{1}'; IL2CPP class injection (IntPtr constructor and ClassInjector registration) is not lowered yet");

    public static readonly DiagnosticDescriptor UnsupportedNativePattern = Error(
        "S1IC020",
        "Native type test inside an unsupported pattern",
        "Type test for native type '{0}' would run as a CLR check against an IL2CPP proxy; only top-level 'is T', 'is T x', 'is T {{ ... }}' and 'is not T' patterns are lowered");

    public static readonly DiagnosticDescriptor UnsupportedNativeForEachCast = Error(
        "S1IC021",
        "foreach performs a native downcast",
        "foreach variable of native type '{0}' implies a CLR cast from '{1}'; iterate with 'var' and cast inside the loop");

    public static readonly DiagnosticDescriptor UnsupportedNativeEventMetadata = Error(
        "S1IC022",
        "Event metadata cannot be preserved",
        "Native-delegate event '{0}' is lowered to accessor methods; event-targeted attributes cannot be preserved on the target CLR assembly");

    private static DiagnosticDescriptor Error(string id, string title, string message) =>
        new(id, title, message, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
