namespace S1Interop.Core.Generators;

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
