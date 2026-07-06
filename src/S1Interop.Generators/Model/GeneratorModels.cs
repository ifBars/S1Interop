using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace S1Interop.Generators.Model;

internal readonly struct S1InteropTypeEntry
{
    public S1InteropTypeEntry(string alias, string monoTypeName, string il2CppTypeName, bool discoverMembers = true)
    {
        Alias = alias;
        MonoTypeName = monoTypeName;
        Il2CppTypeName = il2CppTypeName;
        DiscoverMembers = discoverMembers;
    }

    public string Alias { get; }

    public string MonoTypeName { get; }

    public string Il2CppTypeName { get; }

    public bool DiscoverMembers { get; }

    public S1InteropTypeEntry WithAlias(string alias) =>
        new(alias, MonoTypeName, Il2CppTypeName, DiscoverMembers);
}

internal readonly struct S1InteropNamespaceEntry
{
    public S1InteropNamespaceEntry(string namespaceName, bool includeSubnamespaces, bool includeMembers)
    {
        NamespaceName = namespaceName;
        IncludeSubnamespaces = includeSubnamespaces;
        IncludeMembers = includeMembers;
    }

    public string NamespaceName { get; }

    public bool IncludeSubnamespaces { get; }

    public bool IncludeMembers { get; }
}

internal readonly struct TypeFacadeName
{
    public TypeFacadeName(string namespaceName, string typeName)
    {
        NamespaceName = namespaceName;
        TypeName = typeName;
    }

    public string NamespaceName { get; }

    public string TypeName { get; }
}

internal readonly struct S1InteropEnumEntry
{
    public S1InteropEnumEntry(
        string ownerAlias,
        string monoTypeName,
        string il2CppTypeName,
        string underlyingTypeName,
        ImmutableArray<S1InteropEnumValueEntry> values)
    {
        OwnerAlias = ownerAlias;
        MonoTypeName = monoTypeName;
        Il2CppTypeName = il2CppTypeName;
        UnderlyingTypeName = underlyingTypeName;
        Values = values;
    }

    public string OwnerAlias { get; }

    public string MonoTypeName { get; }

    public string Il2CppTypeName { get; }

    public string UnderlyingTypeName { get; }

    public ImmutableArray<S1InteropEnumValueEntry> Values { get; }
}

internal readonly struct S1InteropEnumValueEntry
{
    public S1InteropEnumValueEntry(string name, string valueLiteral)
    {
        Name = name;
        ValueLiteral = valueLiteral;
    }

    public string Name { get; }

    public string ValueLiteral { get; }
}

internal enum S1InteropMemberKind
{
    FieldOrProperty,
    Method,
    Field,
    Property
}

internal readonly struct S1InteropBridgeRequests
{
    public S1InteropBridgeRequests(bool generateUnityEventBridge, bool generateDelegateEventBridge)
    {
        GenerateUnityEventBridge = generateUnityEventBridge;
        GenerateDelegateEventBridge = generateDelegateEventBridge;
    }

    public bool GenerateUnityEventBridge { get; }

    public bool GenerateDelegateEventBridge { get; }
}

internal readonly struct S1InteropMemberEntry
{
    public S1InteropMemberEntry(
        string alias,
        string ownerAlias,
        string memberName,
        S1InteropMemberKind kind,
        bool isStatic,
        bool canWrite,
        ImmutableArray<string> parameterTypeNames,
        string? valueTypeName,
        ImmutableArray<string> parameterNames)
    {
        Alias = alias;
        OwnerAlias = ownerAlias;
        MemberName = memberName;
        Kind = kind;
        IsStatic = isStatic;
        CanWrite = canWrite;
        ParameterTypeNames = parameterTypeNames;
        ValueTypeName = valueTypeName;
        ParameterNames = parameterNames;
    }

    public string Alias { get; }

    public string OwnerAlias { get; }

    public string MemberName { get; }

    public S1InteropMemberKind Kind { get; }

    public bool IsStatic { get; }

    public bool CanWrite { get; }

    public ImmutableArray<string> ParameterTypeNames { get; }

    public string? ValueTypeName { get; }

    public ImmutableArray<string> ParameterNames { get; }
}

internal readonly struct S1InteropConstructorEntry
{
    public S1InteropConstructorEntry(
        string ownerAlias,
        ImmutableArray<string> parameterTypeNames,
        ImmutableArray<string> parameterNames)
    {
        OwnerAlias = ownerAlias;
        ParameterTypeNames = parameterTypeNames;
        ParameterNames = parameterNames;
    }

    public string OwnerAlias { get; }

    public ImmutableArray<string> ParameterTypeNames { get; }

    public ImmutableArray<string> ParameterNames { get; }
}

internal readonly struct DiscoveredMember
{
    public DiscoveredMember(
        string name,
        S1InteropMemberKind kind,
        bool isStatic,
        bool canWrite,
        ImmutableArray<string> parameterTypeNames,
        string? valueTypeName,
        ImmutableArray<string> parameterNames)
    {
        Name = name;
        Kind = kind;
        IsStatic = isStatic;
        CanWrite = canWrite;
        ParameterTypeNames = parameterTypeNames;
        ValueTypeName = valueTypeName;
        ParameterNames = parameterNames;
    }

    public string Name { get; }

    public S1InteropMemberKind Kind { get; }

    public bool IsStatic { get; }

    public bool CanWrite { get; }

    public ImmutableArray<string> ParameterTypeNames { get; }

    public string? ValueTypeName { get; }

    public ImmutableArray<string> ParameterNames { get; }
}

internal readonly struct DiscoveredConstructor
{
    public DiscoveredConstructor(
        ImmutableArray<string> parameterTypeNames,
        ImmutableArray<string> parameterNames)
    {
        ParameterTypeNames = parameterTypeNames;
        ParameterNames = parameterNames;
    }

    public ImmutableArray<string> ParameterTypeNames { get; }

    public ImmutableArray<string> ParameterNames { get; }
}

internal readonly struct S1InteropTypeDiagnosticTarget
{
    public S1InteropTypeDiagnosticTarget(S1InteropTypeEntry entry, Location? location)
    {
        Entry = entry;
        Location = location;
    }

    public S1InteropTypeEntry Entry { get; }

    public Location? Location { get; }
}

internal readonly struct S1InteropMemberDiagnosticTarget
{
    public S1InteropMemberDiagnosticTarget(S1InteropMemberEntry entry, Location? location)
    {
        Entry = entry;
        Location = location;
    }

    public S1InteropMemberEntry Entry { get; }

    public Location? Location { get; }
}

internal sealed class S1InteropTypeEntryComparer : IEqualityComparer<S1InteropTypeEntry>
{
    public static readonly S1InteropTypeEntryComparer Instance = new();

    public bool Equals(S1InteropTypeEntry x, S1InteropTypeEntry y) =>
        string.Equals(x.Alias, y.Alias, StringComparison.Ordinal) &&
        string.Equals(x.MonoTypeName, y.MonoTypeName, StringComparison.Ordinal) &&
        string.Equals(x.Il2CppTypeName, y.Il2CppTypeName, StringComparison.Ordinal) &&
        x.DiscoverMembers == y.DiscoverMembers;

    public int GetHashCode(S1InteropTypeEntry obj)
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(obj.Alias);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(obj.MonoTypeName);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(obj.Il2CppTypeName);
            hash = (hash * 31) + (obj.DiscoverMembers ? 1 : 0);
            return hash;
        }
    }
}

internal sealed class S1InteropTypeDiagnosticTargetComparer : IEqualityComparer<S1InteropTypeDiagnosticTarget>
{
    public static readonly S1InteropTypeDiagnosticTargetComparer Instance = new();

    public bool Equals(S1InteropTypeDiagnosticTarget x, S1InteropTypeDiagnosticTarget y) =>
        S1InteropTypeEntryComparer.Instance.Equals(x.Entry, y.Entry);

    public int GetHashCode(S1InteropTypeDiagnosticTarget obj) =>
        S1InteropTypeEntryComparer.Instance.GetHashCode(obj.Entry);
}

internal sealed class S1InteropMemberEntryComparer : IEqualityComparer<S1InteropMemberEntry>
{
    public static readonly S1InteropMemberEntryComparer Instance = new();

    public bool Equals(S1InteropMemberEntry x, S1InteropMemberEntry y) =>
        string.Equals(x.Alias, y.Alias, StringComparison.Ordinal) &&
        string.Equals(x.OwnerAlias, y.OwnerAlias, StringComparison.Ordinal) &&
        string.Equals(x.MemberName, y.MemberName, StringComparison.Ordinal) &&
        x.Kind == y.Kind &&
        x.IsStatic == y.IsStatic &&
        x.CanWrite == y.CanWrite &&
        string.Equals(x.ValueTypeName, y.ValueTypeName, StringComparison.Ordinal) &&
        x.ParameterTypeNames.SequenceEqual(y.ParameterTypeNames, StringComparer.Ordinal) &&
        x.ParameterNames.SequenceEqual(y.ParameterNames, StringComparer.Ordinal);

    public int GetHashCode(S1InteropMemberEntry obj)
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(obj.Alias);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(obj.OwnerAlias);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(obj.MemberName);
            hash = (hash * 31) + (int)obj.Kind;
            hash = (hash * 31) + (obj.IsStatic ? 1 : 0);
            hash = (hash * 31) + (obj.CanWrite ? 1 : 0);
            hash = (hash * 31) + (obj.ValueTypeName is null ? 0 : StringComparer.Ordinal.GetHashCode(obj.ValueTypeName));
            foreach (string parameterTypeName in obj.ParameterTypeNames)
            {
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(parameterTypeName);
            }

            foreach (string parameterName in obj.ParameterNames)
            {
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(parameterName);
            }

            return hash;
        }
    }
}

internal sealed class S1InteropConstructorEntryComparer : IEqualityComparer<S1InteropConstructorEntry>
{
    public static readonly S1InteropConstructorEntryComparer Instance = new();

    public bool Equals(S1InteropConstructorEntry x, S1InteropConstructorEntry y) =>
        string.Equals(x.OwnerAlias, y.OwnerAlias, StringComparison.Ordinal) &&
        x.ParameterTypeNames.SequenceEqual(y.ParameterTypeNames, StringComparer.Ordinal);

    public int GetHashCode(S1InteropConstructorEntry obj)
    {
        unchecked
        {
            int hash = StringComparer.Ordinal.GetHashCode(obj.OwnerAlias);
            foreach (string parameterTypeName in obj.ParameterTypeNames)
            {
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(parameterTypeName);
            }

            return hash;
        }
    }
}

internal sealed class S1InteropMemberDiagnosticTargetComparer : IEqualityComparer<S1InteropMemberDiagnosticTarget>
{
    public static readonly S1InteropMemberDiagnosticTargetComparer Instance = new();

    public bool Equals(S1InteropMemberDiagnosticTarget x, S1InteropMemberDiagnosticTarget y) =>
        S1InteropMemberEntryComparer.Instance.Equals(x.Entry, y.Entry);

    public int GetHashCode(S1InteropMemberDiagnosticTarget obj) =>
        S1InteropMemberEntryComparer.Instance.GetHashCode(obj.Entry);
}
