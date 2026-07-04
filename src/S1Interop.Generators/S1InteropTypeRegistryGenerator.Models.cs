using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace S1Interop.Generators;

internal readonly struct S1InteropTypeEntry
{
    public S1InteropTypeEntry(string alias, string monoTypeName, string il2CppTypeName)
    {
        Alias = alias;
        MonoTypeName = monoTypeName;
        Il2CppTypeName = il2CppTypeName;
    }

    public string Alias { get; }

    public string MonoTypeName { get; }

    public string Il2CppTypeName { get; }

    public S1InteropTypeEntry WithAlias(string alias) =>
        new(alias, MonoTypeName, Il2CppTypeName);
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
        ImmutableArray<string> parameterTypeNames)
    {
        Alias = alias;
        OwnerAlias = ownerAlias;
        MemberName = memberName;
        Kind = kind;
        IsStatic = isStatic;
        ParameterTypeNames = parameterTypeNames;
    }

    public string Alias { get; }

    public string OwnerAlias { get; }

    public string MemberName { get; }

    public S1InteropMemberKind Kind { get; }

    public bool IsStatic { get; }

    public ImmutableArray<string> ParameterTypeNames { get; }
}

internal readonly struct DiscoveredMember
{
    public DiscoveredMember(
        string name,
        S1InteropMemberKind kind,
        bool isStatic,
        ImmutableArray<string> parameterTypeNames)
    {
        Name = name;
        Kind = kind;
        IsStatic = isStatic;
        ParameterTypeNames = parameterTypeNames;
    }

    public string Name { get; }

    public S1InteropMemberKind Kind { get; }

    public bool IsStatic { get; }

    public ImmutableArray<string> ParameterTypeNames { get; }
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
        string.Equals(x.Il2CppTypeName, y.Il2CppTypeName, StringComparison.Ordinal);

    public int GetHashCode(S1InteropTypeEntry obj)
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(obj.Alias);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(obj.MonoTypeName);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(obj.Il2CppTypeName);
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
        x.ParameterTypeNames.SequenceEqual(y.ParameterTypeNames, StringComparer.Ordinal);

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
