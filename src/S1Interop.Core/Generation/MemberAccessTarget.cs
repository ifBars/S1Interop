namespace S1Interop.Core;

public sealed record MemberAccessTarget(
    string SourceFilePath,
    int Line,
    string OwnerAlias,
    string OwnerTypeName,
    string MemberName,
    string MemberAlias,
    bool IsStatic);
