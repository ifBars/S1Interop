namespace S1Interop.Core.CodeGeneration;

/// <summary>
/// Describes a Harmony method lookup that can be represented by generated S1Interop declarations.
/// </summary>
/// <param name="SourceFilePath">The source file containing the lookup.</param>
/// <param name="StartLine">The first one-based source line occupied by the lookup.</param>
/// <param name="EndLine">The last one-based source line occupied by the lookup.</param>
/// <param name="VariableName">The local or field name that receives the resolved method.</param>
/// <param name="OwnerAlias">The generated alias for the declaring game type.</param>
/// <param name="OwnerTypeName">The fully qualified Mono name of the declaring game type.</param>
/// <param name="MethodName">The runtime method name.</param>
/// <param name="MethodAlias">The generated alias for the method binding.</param>
/// <param name="ParameterTypeNames">The Mono parameter type names used to select an overload.</param>
public sealed record HarmonyMethodTarget(
    string SourceFilePath,
    int StartLine,
    int EndLine,
    string VariableName,
    string OwnerAlias,
    string OwnerTypeName,
    string MethodName,
    string MethodAlias,
    IReadOnlyList<string> ParameterTypeNames);
