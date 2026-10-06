using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace S1Interop.Compiler;

internal enum TypeMappingStatus
{
    /// <summary>Author-declared or BCL type; it stays as written.</summary>
    Unchanged,
    Mapped,
    Missing,
    Ambiguous,
}

internal readonly record struct TypeMapping(TypeMappingStatus Status, INamedTypeSymbol? Target, string Detail);

internal enum NamespaceMappingKind
{
    Keep,
    Rename,
    Remove,
}

/// <summary>
/// Maps author-side referenced types onto target metadata purely by metadata name: the exact name first, then the
/// IL2CPP-prefixed namespace. Nothing is hard-coded per game type and no assembly is loaded or executed.
/// </summary>
internal sealed class MetadataSymbolMap
{
    private const string Il2CppPrefix = "Il2Cpp";
    private const string NativeObjectBaseName = "Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase";

    private readonly CSharpCompilation author;
    private readonly CSharpCompilation target;
    private readonly Dictionary<INamedTypeSymbol, TypeMapping> cache = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<string, INamedTypeSymbol?> targetTypes = new(StringComparer.Ordinal);
    private bool? nativeArrays;
    private bool? nativeReferenceArrays;

    public MetadataSymbolMap(CSharpCompilation author, CSharpCompilation target)
    {
        this.author = author;
        this.target = target;
        NativeObjectBase = FindUnique(NativeObjectBaseName);
    }

    /// <summary>Gets the target's IL2CPP proxy base, or null when the target is not an IL2CPP surface.</summary>
    public INamedTypeSymbol? NativeObjectBase { get; }
    public bool UsesNativeLists { get; private set; }

    /// <summary>Gets whether the target exposes the native scalar array surface the array helpers need.</summary>
    public bool SupportsNativeArrays => nativeArrays ??= NativeArraySource.HasSurface(this);

    /// <summary>
    /// Gets whether the target exposes the native reference array surface the reference helpers need. Independent of
    /// <see cref="SupportsNativeArrays"/>: either surface can exist without the other.
    /// </summary>
    public bool SupportsNativeReferenceArrays => nativeReferenceArrays ??= NativeReferenceArraySource.HasSurface(this);

    public bool IsBridgedList(ITypeSymbol? type) => NativeObjectBase is not null &&
        type is INamedTypeSymbol named && named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>" &&
        IsNative(named.TypeArguments[0]);

    private string ListDisplay(INamedTypeSymbol type)
    {
        UsesNativeLists = true;
        return NativeListSource.TypeName + "<" + TargetDisplay(type.TypeArguments[0]) + ">";
    }

    internal INamedTypeSymbol? FindTargetType(string metadataName)
    {
        if (!targetTypes.TryGetValue(metadataName, out var type)) targetTypes[metadataName] = type = FindUnique(metadataName);
        return type;
    }

    public bool IsAuthorType(ITypeSymbol type) =>
        SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, author.Assembly);

    public TypeMapping Resolve(INamedTypeSymbol type)
    {
        INamedTypeSymbol definition = type.OriginalDefinition;
        if (cache.TryGetValue(definition, out TypeMapping cached))
        {
            return cached;
        }

        TypeMapping mapping = ResolveUncached(definition);
        cache[definition] = mapping;
        return mapping;
    }

    /// <summary>
    /// Gets whether the author type, or its nearest referenced base, maps to a target IL2CPP proxy class.
    /// </summary>
    public bool IsNative(ITypeSymbol? type)
    {
        if (type is ITypeParameterSymbol parameter)
        {
            return parameter.ConstraintTypes.Any(IsNative);
        }

        if (NativeObjectBase is null || type is not INamedTypeSymbol
            { TypeKind: TypeKind.Class or TypeKind.Interface or TypeKind.Delegate } named)
        {
            return false;
        }

        for (INamedTypeSymbol? current = named; current is not null; current = current.BaseType)
        {
            if (IsAuthorType(current))
            {
                continue;
            }

            TypeMapping mapping = Resolve(current);
            INamedTypeSymbol? candidate = mapping.Status switch
            {
                TypeMappingStatus.Mapped => mapping.Target,
                _ => null,
            };
            return candidate is not null && DerivesFrom(candidate, NativeObjectBase);
        }

        return false;
    }

    /// <summary>
    /// Renders an author type as a fully qualified target type name, mapping every referenced type it contains.
    /// </summary>
    public string TargetDisplay(ITypeSymbol type) => type switch
    {
        INamedTypeSymbol named when IsBridgedList(named) => ListDisplay(named),
        IArrayTypeSymbol array => TargetDisplay(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]",
        INamedTypeSymbol { IsTupleType: false, SpecialType: SpecialType.None } named => NamedDisplay(named),
        _ => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
    };

    /// <summary>
    /// Gets the qualifier (without the trailing dot) for a target type name, or null for the global namespace.
    /// </summary>
    public string? TargetQualifier(INamedTypeSymbol sourceType, INamedTypeSymbol targetType)
    {
        if (sourceType.ContainingType is not null)
        {
            return TargetDisplay(sourceType.ContainingType);
        }

        return targetType.ContainingNamespace.IsGlobalNamespace
            ? null
            : "global::" + targetType.ContainingNamespace.ToDisplayString();
    }

    public (NamespaceMappingKind Kind, string Name) MapNamespace(string name)
    {
        if (FindNamespace(target.GlobalNamespace, name) is not null)
        {
            return (NamespaceMappingKind.Keep, name);
        }

        string prefixed = Il2CppPrefix + name;
        if (FindNamespace(target.GlobalNamespace, prefixed) is not null)
        {
            return (NamespaceMappingKind.Rename, prefixed);
        }

        return FindNamespace(author.Assembly.GlobalNamespace, name) is not null
            ? (NamespaceMappingKind.Keep, name)
            : (NamespaceMappingKind.Remove, name);
    }

    private TypeMapping ResolveUncached(INamedTypeSymbol definition)
    {
        if (IsAuthorType(definition) || IsManagedFramework(definition.ContainingAssembly))
        {
            return new TypeMapping(TypeMappingStatus.Unchanged, null, string.Empty);
        }

        if (definition.ContainingType is not null)
        {
            TypeMapping container = Resolve(definition.ContainingType);
            if (container.Status != TypeMappingStatus.Mapped)
            {
                return container with { Target = null };
            }

            INamedTypeSymbol? nested = container.Target!
                .GetTypeMembers(definition.Name, definition.Arity)
                .FirstOrDefault();
            return nested is null
                ? new TypeMapping(TypeMappingStatus.Missing, null, container.Target!.ToDisplayString() + "+" + definition.MetadataName)
                : new TypeMapping(TypeMappingStatus.Mapped, nested, string.Empty);
        }

        string ns = definition.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : definition.ContainingNamespace.ToDisplayString();
        string exact = ns.Length == 0 ? definition.MetadataName : ns + "." + definition.MetadataName;
        string prefixed = ns.Length == 0
            ? Il2CppPrefix + "." + definition.MetadataName
            : Il2CppPrefix + exact;

        foreach (string name in new[] { exact, prefixed })
        {
            List<INamedTypeSymbol> candidates = FindAll(name);
            if (candidates.Count > 1)
            {
                // Prefer the assembly with the same simple name (e.g. Assembly-CSharp on both runtimes).
                candidates = candidates
                    .Where(candidate => candidate.ContainingAssembly.Name == definition.ContainingAssembly.Name)
                    .ToList();
                if (candidates.Count != 1)
                {
                    return new TypeMapping(TypeMappingStatus.Ambiguous, null, string.Join(", ", FindAll(name)
                        .Select(candidate => $"'{candidate.ToDisplayString()}' in '{candidate.ContainingAssembly.Name}'")));
                }
            }

            if (candidates.Count == 1)
            {
                return new TypeMapping(TypeMappingStatus.Mapped, candidates[0], string.Empty);
            }
        }

        return new TypeMapping(TypeMappingStatus.Missing, null, $"'{exact}', '{prefixed}'");
    }

    private string NamedDisplay(INamedTypeSymbol type)
    {
        INamedTypeSymbol mapped = Resolve(type) is { Status: TypeMappingStatus.Mapped, Target: { } target }
            ? target
            : type.OriginalDefinition;
        string qualifier = TargetQualifier(type, mapped) is { } value ? value + "." : "global::";
        string arguments = type.TypeArguments.Length == 0
            ? string.Empty
            : "<" + string.Join(", ", type.TypeArguments.Select(TargetDisplay)) + ">";
        return qualifier + mapped.Name + arguments;
    }

    private INamedTypeSymbol? FindUnique(string metadataName)
    {
        List<INamedTypeSymbol> candidates = FindAll(metadataName);
        return candidates.Count == 1 ? candidates[0] : null;
    }

    // Compilation.GetTypeByMetadataName returns null on cross-assembly duplicates, so search per assembly.
    private List<INamedTypeSymbol> FindAll(string metadataName) =>
        target.SourceModule.ReferencedAssemblySymbols
            .Select(assembly => assembly.GetTypeByMetadataName(metadataName))
            .OfType<INamedTypeSymbol>()
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .ToList();

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsManagedFramework(IAssemblySymbol? assembly)
    {
        string name = assembly?.Name ?? string.Empty;
        return name is "mscorlib" or "netstandard" or "System" or "System.Private.CoreLib" ||
               name.StartsWith("System.", StringComparison.Ordinal) ||
               name.StartsWith("Microsoft.", StringComparison.Ordinal);
    }

    private static INamespaceSymbol? FindNamespace(INamespaceSymbol root, string dottedName)
    {
        INamespaceSymbol? current = root;
        foreach (string part in dottedName.Split('.'))
        {
            current = current?.GetNamespaceMembers().FirstOrDefault(member => member.Name == part);
        }

        return current;
    }
}
