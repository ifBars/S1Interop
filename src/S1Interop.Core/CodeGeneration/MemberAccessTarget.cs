namespace S1Interop.Core.CodeGeneration;

/// <summary>
/// Identifies the runtime member shape required by a discovered field or property access.
/// </summary>
public enum MemberAccessKind
{
    /// <summary>
    /// Resolves a field first and then a property with the same name.
    /// </summary>
    FieldOrProperty = 0,

    /// <summary>
    /// Resolves only a field.
    /// </summary>
    Field = 1,

    /// <summary>
    /// Resolves only a property.
    /// </summary>
    Property = 2
}

/// <summary>
/// Describes a source-level member access that can be represented by generated S1Interop declarations.
/// </summary>
/// <param name="SourceFilePath">The source file containing the access.</param>
/// <param name="Line">The one-based source line containing the access.</param>
/// <param name="OwnerAlias">The generated alias for the declaring game type.</param>
/// <param name="OwnerTypeName">The fully qualified Mono name of the declaring game type.</param>
/// <param name="MemberName">The runtime field or property name.</param>
/// <param name="MemberAlias">The generated alias for the member binding.</param>
/// <param name="IsStatic">True when the member belongs to the type rather than an instance.</param>
/// <param name="Kind">The field or property lookup strategy.</param>
public sealed record MemberAccessTarget(
    string SourceFilePath,
    int Line,
    string OwnerAlias,
    string OwnerTypeName,
    string MemberName,
    string MemberAlias,
    bool IsStatic,
    MemberAccessKind Kind = MemberAccessKind.FieldOrProperty);
