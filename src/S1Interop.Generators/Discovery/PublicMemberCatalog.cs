using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace S1Interop.Generators.Discovery;

internal static class PublicMemberCatalog
{
    public static ImmutableArray<S1InteropMemberEntry> DiscoverMemberEntries(
        Compilation compilation,
        ImmutableArray<S1InteropTypeEntry> entries)
    {
        if (entries.IsDefaultOrEmpty)
        {
            return ImmutableArray<S1InteropMemberEntry>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<S1InteropMemberEntry>();
        foreach (S1InteropTypeEntry entry in entries)
        {
            if (!entry.DiscoverMembers)
            {
                continue;
            }

            var seenAliases = new HashSet<string>(StringComparer.Ordinal);
            INamedTypeSymbol? monoType = compilation.GetTypeByMetadataName(entry.MonoTypeName);
            INamedTypeSymbol? il2CppType = compilation.GetTypeByMetadataName(entry.Il2CppTypeName);

            foreach (DiscoveredMember member in DiscoverCompatiblePublicMembers(monoType, il2CppType))
            {
                string alias = GetDiscoveredMemberAlias(entry, member);
                if (!seenAliases.Add(alias))
                {
                    continue;
                }

                builder.Add(new S1InteropMemberEntry(
                    alias,
                    entry.Alias,
                    member.Name,
                    member.Kind,
                    member.IsStatic,
                    member.ParameterTypeNames,
                    member.ValueTypeName,
                    member.ParameterNames));
            }
        }

        return builder.ToImmutable();
    }

    private static string GetDiscoveredMemberAlias(S1InteropTypeEntry entry, DiscoveredMember member) =>
        SanitizeIdentifier(entry.Alias + ToPascalIdentifier(member.Name));

    public static ImmutableArray<S1InteropMemberEntry> MergeMemberEntries(
        ImmutableArray<S1InteropMemberEntry> explicitMembers,
        ImmutableArray<S1InteropMemberEntry> discoveredMembers)
    {
        var merged = new Dictionary<string, S1InteropMemberEntry>(StringComparer.Ordinal);
        var explicitFacadeKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (S1InteropMemberEntry member in explicitMembers)
        {
            explicitFacadeKeys.Add(GetFacadeMemberKey(member));
        }

        foreach (S1InteropMemberEntry member in discoveredMembers)
        {
            if (!explicitFacadeKeys.Contains(GetFacadeMemberKey(member)))
            {
                merged[GetMemberDeclarationKey(member)] = member;
            }
        }

        foreach (S1InteropMemberEntry member in explicitMembers)
        {
            merged[GetMemberDeclarationKey(member)] = member;
        }

        return merged.Values
            .OrderBy(member => member.OwnerAlias, StringComparer.Ordinal)
            .ThenBy(member => member.Alias, StringComparer.Ordinal)
            .ThenBy(member => member.MemberName, StringComparer.Ordinal)
            .GroupBy(member => member.Alias, StringComparer.Ordinal)
            .Select(group => group.First())
            .WhereNoGeneratedRegistryNameCollisions()
            .Distinct(S1InteropMemberEntryComparer.Instance)
            .ToImmutableArray();
    }

    private static IEnumerable<S1InteropMemberEntry> WhereNoGeneratedRegistryNameCollisions(
        this IEnumerable<S1InteropMemberEntry> members)
    {
        var generatedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (S1InteropMemberEntry member in members)
        {
            string[] memberGeneratedNames = GetGeneratedRegistryMemberNames(member);
            if (memberGeneratedNames.Any(name => generatedNames.Contains(name)))
            {
                continue;
            }

            foreach (string name in memberGeneratedNames)
            {
                generatedNames.Add(name);
            }

            yield return member;
        }
    }

    private static string[] GetGeneratedRegistryMemberNames(S1InteropMemberEntry member)
    {
        if (member.Kind == S1InteropMemberKind.Method)
        {
            return [$"Invoke{member.Alias}"];
        }

        return
        [
            $"Get{member.Alias}",
            $"Get{member.Alias}Value",
            $"TrySet{member.Alias}"
        ];
    }

    private static string GetMemberDeclarationKey(S1InteropMemberEntry member) =>
        $"{member.OwnerAlias}.{member.Alias}";

    public static string GetFacadeMemberKey(S1InteropMemberEntry member) =>
        $"{member.OwnerAlias}.{GetFacadeMemberCategory(member)}.{member.IsStatic}.{ToPascalIdentifier(member.MemberName)}";

    private static string GetFacadeMemberCategory(S1InteropMemberEntry member) =>
        member.Kind == S1InteropMemberKind.Method ? "Method" : "Value";

    private static IEnumerable<DiscoveredMember> DiscoverCompatiblePublicMembers(
        INamedTypeSymbol? monoType,
        INamedTypeSymbol? il2CppType)
    {
        IReadOnlyDictionary<string, DiscoveredMember> monoMembers = DiscoverPublicFieldPropertyMembers(monoType);
        IReadOnlyDictionary<string, DiscoveredMember> il2CppMembers = DiscoverPublicFieldPropertyMembers(il2CppType);
        foreach (DiscoveredMember member in SelectCompatibleMembers(monoMembers, il2CppMembers))
        {
            yield return member;
        }

        IReadOnlyDictionary<string, DiscoveredMember> monoMethods = DiscoverPublicMethods(monoType);
        IReadOnlyDictionary<string, DiscoveredMember> il2CppMethods = DiscoverPublicMethods(il2CppType);
        foreach (DiscoveredMember method in SelectCompatibleMembers(monoMethods, il2CppMethods))
        {
            yield return method;
        }
    }

    private static IEnumerable<DiscoveredMember> SelectCompatibleMembers(
        IReadOnlyDictionary<string, DiscoveredMember> monoMembers,
        IReadOnlyDictionary<string, DiscoveredMember> il2CppMembers)
    {
        if (monoMembers.Count > 0 && il2CppMembers.Count > 0)
        {
            foreach (DiscoveredMember monoMember in monoMembers.Values.OrderBy(member => member.Name, StringComparer.Ordinal))
            {
                if (il2CppMembers.TryGetValue(monoMember.Name, out DiscoveredMember il2CppMember) &&
                    AreDiscoveredMembersCompatible(monoMember, il2CppMember))
                {
                    yield return monoMember;
                }
            }

            yield break;
        }

        IReadOnlyDictionary<string, DiscoveredMember> availableMembers = monoMembers.Count > 0 ? monoMembers : il2CppMembers;
        foreach (DiscoveredMember member in availableMembers.Values.OrderBy(member => member.Name, StringComparer.Ordinal))
        {
            yield return member;
        }
    }

    private static bool AreDiscoveredMembersCompatible(DiscoveredMember monoMember, DiscoveredMember il2CppMember) =>
        monoMember.Kind == il2CppMember.Kind &&
        monoMember.IsStatic == il2CppMember.IsStatic &&
        AreBackendNeutralTypeNamesCompatible(monoMember.ValueTypeName, il2CppMember.ValueTypeName) &&
        AreParameterTypeNamesCompatible(monoMember.ParameterTypeNames, il2CppMember.ParameterTypeNames);

    private static bool AreBackendNeutralTypeNamesCompatible(string? monoTypeName, string? il2CppTypeName)
    {
        if (monoTypeName is null || il2CppTypeName is null)
        {
            return monoTypeName is null && il2CppTypeName is null;
        }

        return string.Equals(
            NormalizeBackendNeutralParameterTypeName(monoTypeName),
            NormalizeBackendNeutralParameterTypeName(il2CppTypeName),
            StringComparison.Ordinal);
    }

    private static bool AreParameterTypeNamesCompatible(
        ImmutableArray<string> monoParameterTypeNames,
        ImmutableArray<string> il2CppParameterTypeNames)
    {
        if (monoParameterTypeNames.Length != il2CppParameterTypeNames.Length)
        {
            return false;
        }

        for (int index = 0; index < monoParameterTypeNames.Length; index++)
        {
            if (!string.Equals(
                NormalizeBackendNeutralParameterTypeName(monoParameterTypeNames[index]),
                NormalizeBackendNeutralParameterTypeName(il2CppParameterTypeNames[index]),
                StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeBackendNeutralParameterTypeName(string typeName)
    {
        bool byRef = typeName.EndsWith("&", StringComparison.Ordinal);
        string normalized = byRef ? typeName.Substring(0, typeName.Length - 1) : typeName;
        normalized = normalized
            .Replace("Il2CppScheduleOne.", "ScheduleOne.")
            .Replace("Il2CppSystem.", "System.");
        return byRef ? normalized + "&" : normalized;
    }

    private static IReadOnlyDictionary<string, DiscoveredMember> DiscoverPublicFieldPropertyMembers(INamedTypeSymbol? type)
    {
        var members = new Dictionary<string, DiscoveredMember>(StringComparer.Ordinal);
        if (type is null)
        {
            return members;
        }

        foreach (ISymbol symbol in type.GetMembers())
        {
            if (!TryCreateDiscoveredMember(symbol, out DiscoveredMember member))
            {
                continue;
            }

            if (!members.ContainsKey(member.Name))
            {
                members.Add(member.Name, member);
            }
        }

        return members;
    }

    private static IReadOnlyDictionary<string, DiscoveredMember> DiscoverPublicMethods(INamedTypeSymbol? type)
    {
        var methodsByName = new Dictionary<string, List<IMethodSymbol>>(StringComparer.Ordinal);
        if (type is null)
        {
            return new Dictionary<string, DiscoveredMember>(StringComparer.Ordinal);
        }

        foreach (IMethodSymbol method in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (!IsPublicMethodCandidate(method))
            {
                continue;
            }

            if (!methodsByName.TryGetValue(method.Name, out List<IMethodSymbol>? overloads))
            {
                overloads = new List<IMethodSymbol>();
                methodsByName.Add(method.Name, overloads);
            }

            overloads.Add(method);
        }

        var members = new Dictionary<string, DiscoveredMember>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, List<IMethodSymbol>> pair in methodsByName)
        {
            if (pair.Value.Count != 1)
            {
                continue;
            }

            IMethodSymbol method = pair.Value[0];
            members.Add(
                pair.Key,
                new DiscoveredMember(
                    method.Name,
                    S1InteropMemberKind.Method,
                    method.IsStatic,
                    method.Parameters.Select(GetParameterTypeName).ToImmutableArray(),
                    GetTypeName(method.ReturnType),
                    CreateParameterNames(method.Parameters.Length)));
        }

        return members;
    }

    private static bool IsPublicMethodCandidate(IMethodSymbol method) =>
        method.DeclaredAccessibility == Accessibility.Public &&
        method.MethodKind == MethodKind.Ordinary &&
        !method.IsImplicitlyDeclared &&
        !method.IsGenericMethod &&
        !method.IsExtensionMethod &&
        IsInteropSafeMemberName(method.Name) &&
        !IsCommonObjectMethodName(method.Name);

    private static bool IsCommonObjectMethodName(string name) =>
        name == "ToString" ||
        name == "Equals" ||
        name == "GetHashCode" ||
        name == "GetType";

    private static string GetParameterTypeName(IParameterSymbol parameter)
    {
        string typeName = GetTypeName(parameter.Type);
        return parameter.RefKind == RefKind.None ? typeName : typeName + "&";
    }

    private static string GetTypeName(ITypeSymbol type) =>
        NormalizeComparableTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

    private static ImmutableArray<string> CreateParameterNames(int count)
    {
        if (count == 0)
        {
            return ImmutableArray<string>.Empty;
        }

        var names = ImmutableArray.CreateBuilder<string>(count);
        for (int index = 0; index < count; index++)
        {
            names.Add("arg" + index.ToString(CultureInfo.InvariantCulture));
        }

        return names.ToImmutable();
    }

    private static bool TryCreateDiscoveredMember(ISymbol symbol, out DiscoveredMember member)
    {
        member = default;
        if (symbol.DeclaredAccessibility != Accessibility.Public ||
            symbol.IsImplicitlyDeclared ||
            !IsInteropSafeMemberName(symbol.Name))
        {
            return false;
        }

        switch (symbol)
        {
            case IFieldSymbol field when !field.IsConst && !IsBackingFieldCandidate(field):
                member = new DiscoveredMember(
                    field.Name,
                    S1InteropMemberKind.FieldOrProperty,
                    field.IsStatic,
                    ImmutableArray<string>.Empty,
                    GetTypeName(field.Type),
                    ImmutableArray<string>.Empty);
                return true;
            case IPropertySymbol property when property.Parameters.Length == 0:
                member = new DiscoveredMember(
                    property.Name,
                    S1InteropMemberKind.FieldOrProperty,
                    property.IsStatic,
                    ImmutableArray<string>.Empty,
                    GetTypeName(property.Type),
                    ImmutableArray<string>.Empty);
                return true;
            default:
                return false;
        }
    }

    private static bool IsBackingFieldCandidate(IFieldSymbol field)
    {
        if (field.AssociatedSymbol is IPropertySymbol)
        {
            return true;
        }

        return field.Name.Contains("BackingField", StringComparison.Ordinal) ||
            (HasCompilerGeneratedAttribute(field) && field.Name.Contains("Backing", StringComparison.Ordinal));
    }

    private static bool HasCompilerGeneratedAttribute(IFieldSymbol field) =>
        field.GetAttributes().Any(attribute =>
            string.Equals(
                attribute.AttributeClass?.ToDisplayString(),
                "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
                StringComparison.Ordinal));

    private static bool IsInteropSafeMemberName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        !name.StartsWith("<", StringComparison.Ordinal) &&
        !name.StartsWith("get_", StringComparison.Ordinal) &&
        !name.StartsWith("set_", StringComparison.Ordinal) &&
        !name.StartsWith("add_", StringComparison.Ordinal) &&
        !name.StartsWith("remove_", StringComparison.Ordinal) &&
        !name.StartsWith("op_", StringComparison.Ordinal) &&
        name != ".ctor" &&
        name != ".cctor";
}
