using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace S1Interop.Generators.Model;

/// <summary>
/// Which referenced APIs the runtime helpers may use, so a mod with a narrow reference set still compiles.
/// </summary>
internal readonly struct RuntimeHelperCapabilities : IEquatable<RuntimeHelperCapabilities>
{
    public RuntimeHelperCapabilities(
        bool hasIl2CppObjectBase,
        bool hasIl2CppList,
        bool hasIl2CppDictionary,
        bool hasUnityEvent,
        bool unityActionIsDelegate,
        bool hasNotNullWhen,
        bool canDeclarePlatformDomain)
    {
        HasIl2CppObjectBase = hasIl2CppObjectBase;
        HasIl2CppList = hasIl2CppList;
        HasIl2CppDictionary = hasIl2CppDictionary;
        HasUnityEvent = hasUnityEvent;
        UnityActionIsDelegate = unityActionIsDelegate;
        HasNotNullWhen = hasNotNullWhen;
        CanDeclarePlatformDomain = canDeclarePlatformDomain;
    }

    public bool HasIl2CppObjectBase { get; }

    public bool HasIl2CppList { get; }

    public bool HasIl2CppDictionary { get; }

    public bool HasUnityEvent { get; }

    /// <summary>
    /// Gets whether <c>UnityAction</c> is a real delegate (Mono) rather than an Il2CppInterop wrapper class that
    /// converts implicitly from <see cref="System.Action"/>.
    /// </summary>
    public bool UnityActionIsDelegate { get; }

    public bool HasNotNullWhen { get; }

    /// <summary>
    /// Gets whether the assembly is a melon (has <c>MelonInfo</c>), MelonLoader exposes <c>MelonPlatformDomain</c>,
    /// and the author has not declared one already.
    /// </summary>
    public bool CanDeclarePlatformDomain { get; }

    public bool Equals(RuntimeHelperCapabilities other) =>
        HasIl2CppObjectBase == other.HasIl2CppObjectBase &&
        HasIl2CppList == other.HasIl2CppList &&
        HasIl2CppDictionary == other.HasIl2CppDictionary &&
        HasUnityEvent == other.HasUnityEvent &&
        UnityActionIsDelegate == other.UnityActionIsDelegate &&
        HasNotNullWhen == other.HasNotNullWhen &&
        CanDeclarePlatformDomain == other.CanDeclarePlatformDomain;

    public override bool Equals(object? obj) => obj is RuntimeHelperCapabilities other && Equals(other);

    public override int GetHashCode() =>
        (HasIl2CppObjectBase ? 1 : 0) |
        (HasIl2CppList ? 2 : 0) |
        (HasIl2CppDictionary ? 4 : 0) |
        (HasUnityEvent ? 8 : 0) |
        (HasNotNullWhen ? 16 : 0) |
        (CanDeclarePlatformDomain ? 32 : 0) |
        (UnityActionIsDelegate ? 64 : 0);
}

/// <summary>
/// A class registered with MelonLoader's <c>RegisterTypeInIl2Cpp</c> attribute. IL2CPP builds need an
/// <c>IntPtr</c> constructor on it, which Mono builds must not require the author to write.
/// </summary>
internal readonly struct Il2CppInjectedTypeEntry : IEquatable<Il2CppInjectedTypeEntry>
{
    public Il2CppInjectedTypeEntry(
        string metadataName,
        string? namespaceName,
        ImmutableArray<string> containingTypeDeclarations,
        string name,
        bool canExtend,
        bool hasIntPtrConstructor,
        bool needsManagedConstructor,
        bool baseHasIntPtrConstructor,
        Location? location)
    {
        MetadataName = metadataName;
        NamespaceName = namespaceName;
        ContainingTypeDeclarations = containingTypeDeclarations;
        Name = name;
        CanExtend = canExtend;
        HasIntPtrConstructor = hasIntPtrConstructor;
        NeedsManagedConstructor = needsManagedConstructor;
        BaseHasIntPtrConstructor = baseHasIntPtrConstructor;
        Location = location;
    }

    public string MetadataName { get; }

    public string? NamespaceName { get; }

    /// <summary>
    /// Gets partial declarations for containing types, outermost first, such as <c>partial class Outer</c>.
    /// </summary>
    public ImmutableArray<string> ContainingTypeDeclarations { get; }

    public string Name { get; }

    /// <summary>
    /// Gets whether the type and every containing type are partial, non-generic classes that generated code can extend.
    /// </summary>
    public bool CanExtend { get; }

    public bool HasIntPtrConstructor { get; }

    /// <summary>
    /// Gets whether adding the <c>IntPtr</c> constructor would remove the implicit parameterless constructor that
    /// managed code may still call. Components are created by Unity and never need one.
    /// </summary>
    public bool NeedsManagedConstructor { get; }

    public bool BaseHasIntPtrConstructor { get; }

    public Location? Location { get; }

    public bool Equals(Il2CppInjectedTypeEntry other) =>
        MetadataName == other.MetadataName &&
        NamespaceName == other.NamespaceName &&
        ContainingTypeDeclarations.SequenceEqual(other.ContainingTypeDeclarations) &&
        Name == other.Name &&
        CanExtend == other.CanExtend &&
        HasIntPtrConstructor == other.HasIntPtrConstructor &&
        NeedsManagedConstructor == other.NeedsManagedConstructor &&
        BaseHasIntPtrConstructor == other.BaseHasIntPtrConstructor &&
        Equals(Location, other.Location);

    public override bool Equals(object? obj) => obj is Il2CppInjectedTypeEntry other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(MetadataName);
}
