using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace S1Interop.Generators;

public sealed partial class S1InteropTypeRegistryGenerator
{
    private static ImmutableArray<S1InteropTypeEntry> GetAssemblyEntries(Compilation compilation)
    {
        INamedTypeSymbol? attributeType = compilation.GetTypeByMetadataName(AttributeMetadataName);
        if (attributeType is null)
        {
            return ImmutableArray<S1InteropTypeEntry>.Empty;
        }

        return compilation.Assembly.GetAttributes()
            .Where(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
            .Select(TryCreateEntry)
            .Where(entry => entry is not null)
            .Select(entry => entry!.Value)
            .ToImmutableArray();
    }

    private static ImmutableArray<S1InteropMemberEntry> GetAssemblyMemberEntries(Compilation compilation)
    {
        INamedTypeSymbol? attributeType = compilation.GetTypeByMetadataName(MemberAttributeMetadataName);
        if (attributeType is null)
        {
            return ImmutableArray<S1InteropMemberEntry>.Empty;
        }

        return compilation.Assembly.GetAttributes()
            .Where(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
            .Select(TryCreateMemberEntry)
            .Where(entry => entry is not null)
            .Select(entry => entry!.Value)
            .Distinct(S1InteropMemberEntryComparer.Instance)
            .ToImmutableArray();
    }

    private static ImmutableArray<S1InteropMemberEntry> DiscoverPublicMemberEntries(
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
            INamedTypeSymbol? monoType = compilation.GetTypeByMetadataName(entry.MonoTypeName);
            INamedTypeSymbol? il2CppType = compilation.GetTypeByMetadataName(entry.Il2CppTypeName);

            foreach (DiscoveredMember member in DiscoverCompatiblePublicMembers(monoType, il2CppType))
            {
                builder.Add(new S1InteropMemberEntry(
                    GetDiscoveredMemberAlias(entry, member),
                    entry.Alias,
                    member.Name,
                    member.Kind,
                    member.IsStatic,
                    member.ParameterTypeNames));
            }
        }

        return builder.ToImmutable();
    }

    private static string GetDiscoveredMemberAlias(S1InteropTypeEntry entry, DiscoveredMember member) =>
        SanitizeIdentifier(entry.Alias + ToPascalIdentifier(member.Name));

    private static ImmutableArray<S1InteropMemberEntry> MergeMemberEntries(
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
            .Distinct(S1InteropMemberEntryComparer.Instance)
            .ToImmutableArray();
    }

    private static string GetMemberDeclarationKey(S1InteropMemberEntry member) =>
        $"{member.OwnerAlias}.{member.Alias}";

    private static string GetFacadeMemberKey(S1InteropMemberEntry member) =>
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
        AreParameterTypeNamesCompatible(monoMember.ParameterTypeNames, il2CppMember.ParameterTypeNames);

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
                    method.Parameters.Select(GetParameterTypeName).ToImmutableArray()));
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
        string typeName = NormalizeComparableTypeName(parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        return parameter.RefKind == RefKind.None ? typeName : typeName + "&";
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
            case IFieldSymbol field when !field.IsConst:
                member = new DiscoveredMember(field.Name, S1InteropMemberKind.FieldOrProperty, field.IsStatic, ImmutableArray<string>.Empty);
                return true;
            case IPropertySymbol property when property.Parameters.Length == 0:
                member = new DiscoveredMember(property.Name, S1InteropMemberKind.FieldOrProperty, property.IsStatic, ImmutableArray<string>.Empty);
                return true;
            default:
                return false;
        }
    }

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

    private static S1InteropBridgeRequests GetBridgeRequests(Compilation compilation) =>
        new(
            HasAssemblyAttribute(compilation, UnityEventBridgeAttributeMetadataName),
            HasAssemblyAttribute(compilation, DelegateEventBridgeAttributeMetadataName));

    private static bool HasAssemblyAttribute(Compilation compilation, string metadataName)
    {
        INamedTypeSymbol? attributeType = compilation.GetTypeByMetadataName(metadataName);
        return attributeType is not null &&
               compilation.Assembly.GetAttributes().Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType));
    }

    private static S1InteropTypeEntry? GetTypeEntry(GeneratorSyntaxContext context)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node) is not INamedTypeSymbol typeSymbol)
        {
            return null;
        }

        AttributeData? attribute = typeSymbol.GetAttributes()
            .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == AttributeMetadataName);
        S1InteropTypeEntry? entry = attribute is null ? null : TryCreateEntry(attribute);
        if (entry is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(entry.Value.Alias)
            ? entry.Value.WithAlias(SanitizeIdentifier(typeSymbol.Name))
            : entry;
    }

    private static S1InteropTypeEntry? TryCreateEntry(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length == 0 ||
            attribute.ConstructorArguments[0].Value is not string monoTypeName ||
            string.IsNullOrWhiteSpace(monoTypeName))
        {
            return null;
        }

        string? il2CppTypeName = null;
        string? alias = null;
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (argument.Value.Value is not string value || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (argument.Key == "Il2CppTypeName")
            {
                il2CppTypeName = value;
            }
            else if (argument.Key == "Alias")
            {
                alias = value;
            }
        }

        return new S1InteropTypeEntry(
            SanitizeIdentifier(alias ?? GetSimpleName(monoTypeName)),
            monoTypeName,
            il2CppTypeName ?? ToIl2CppTypeName(monoTypeName));
    }

    private static ImmutableArray<S1InteropTypeEntry> MergeTypeEntries(ImmutableArray<S1InteropTypeEntry> entries)
    {
        var merged = new Dictionary<string, S1InteropTypeEntry>(StringComparer.Ordinal);
        foreach (S1InteropTypeEntry entry in entries)
        {
            if (!merged.TryGetValue(entry.Alias, out S1InteropTypeEntry existing) ||
                ShouldPreferTypeEntry(entry, existing))
            {
                merged[entry.Alias] = entry;
            }
        }

        return merged.Values.ToImmutableArray();
    }

    private static bool ShouldPreferTypeEntry(S1InteropTypeEntry candidate, S1InteropTypeEntry existing)
    {
        bool candidateHasExplicitIl2CppName = !string.Equals(
            candidate.Il2CppTypeName,
            ToIl2CppTypeName(candidate.MonoTypeName),
            StringComparison.Ordinal);
        bool existingHasExplicitIl2CppName = !string.Equals(
            existing.Il2CppTypeName,
            ToIl2CppTypeName(existing.MonoTypeName),
            StringComparison.Ordinal);

        if (candidateHasExplicitIl2CppName != existingHasExplicitIl2CppName)
        {
            return candidateHasExplicitIl2CppName;
        }

        return false;
    }

    private static S1InteropMemberEntry? TryCreateMemberEntry(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length < 2 ||
            attribute.ConstructorArguments[0].Value is not string ownerAlias ||
            attribute.ConstructorArguments[1].Value is not string memberName ||
            string.IsNullOrWhiteSpace(ownerAlias) ||
            string.IsNullOrWhiteSpace(memberName))
        {
            return null;
        }

        string? alias = null;
        S1InteropMemberKind kind = S1InteropMemberKind.FieldOrProperty;
        bool isStatic = false;
        ImmutableArray<string> parameterTypeNames = ImmutableArray<string>.Empty;
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (argument.Key == "Alias" && argument.Value.Value is string aliasValue && !string.IsNullOrWhiteSpace(aliasValue))
            {
                alias = aliasValue;
            }
            else if (argument.Key == "Kind" && argument.Value.Value is int kindValue)
            {
                kind = kindValue switch
                {
                    (int)S1InteropMemberKind.Method => S1InteropMemberKind.Method,
                    (int)S1InteropMemberKind.Field => S1InteropMemberKind.Field,
                    (int)S1InteropMemberKind.Property => S1InteropMemberKind.Property,
                    _ => S1InteropMemberKind.FieldOrProperty
                };
            }
            else if (argument.Key == "IsStatic" && argument.Value.Value is bool isStaticValue)
            {
                isStatic = isStaticValue;
            }
            else if (argument.Key == "ParameterTypeNames" && !argument.Value.Values.IsDefaultOrEmpty)
            {
                parameterTypeNames = argument.Value.Values
                    .Select(value => value.Value as string)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!)
                    .ToImmutableArray();
            }
        }

        return new S1InteropMemberEntry(
            SanitizeIdentifier(alias ?? memberName),
            SanitizeIdentifier(ownerAlias),
            memberName,
            kind,
            isStatic,
            parameterTypeNames);
    }
}
