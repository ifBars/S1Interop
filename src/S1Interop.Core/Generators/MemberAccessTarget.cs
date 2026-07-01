namespace S1Interop.Core.Generators;

public enum MemberAccessKind
{
    FieldOrProperty = 0,
    Field = 1,
    Property = 2
}

public sealed record MemberAccessTarget(
    string SourceFilePath,
    int Line,
    string OwnerAlias,
    string OwnerTypeName,
    string MemberName,
    string MemberAlias,
    bool IsStatic,
    MemberAccessKind Kind = MemberAccessKind.FieldOrProperty);
