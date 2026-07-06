using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace S1Interop.Generators.Discovery;

internal static class InteropDeclarationReader
{
    public static ImmutableArray<S1InteropTypeEntry> GetAssemblyEntries(Compilation compilation)
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

    public static ImmutableArray<S1InteropTypeEntry> GetNamespaceEntries(Compilation compilation)
    {
        INamedTypeSymbol? attributeType = compilation.GetTypeByMetadataName(NamespaceAttributeMetadataName);
        if (attributeType is null)
        {
            return ImmutableArray<S1InteropTypeEntry>.Empty;
        }

        S1InteropNamespaceEntry[] namespaces = compilation.Assembly.GetAttributes()
            .Where(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
            .Select(TryCreateNamespaceEntry)
            .Where(entry => entry is not null)
            .Select(entry => entry!.Value)
            .ToArray();
        if (namespaces.Length == 0)
        {
            return ImmutableArray<S1InteropTypeEntry>.Empty;
        }

        string[] monoTypeNames = EnumerateReferencedTypes(compilation.GlobalNamespace)
            .Select(symbol => ToMonoTypeName(symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
            .Where(typeName => namespaces.Any(ns => IsNamespaceMatch(typeName, ns)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(typeName => typeName, StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, int> simpleNameCounts = monoTypeNames
            .GroupBy(GetSimpleName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var usedAliases = new HashSet<string>(StringComparer.Ordinal);
        var entries = ImmutableArray.CreateBuilder<S1InteropTypeEntry>(monoTypeNames.Length);
        foreach (string monoTypeName in monoTypeNames)
        {
            string simpleName = GetSimpleName(monoTypeName);
            string alias = simpleNameCounts[simpleName] == 1
                ? SanitizeIdentifier(simpleName)
                : CreateQualifiedAlias(monoTypeName);
            alias = EnsureUniqueAlias(alias, usedAliases);
            bool discoverMembers = namespaces.Any(ns => ns.IncludeMembers && IsNamespaceMatch(monoTypeName, ns));
            entries.Add(new S1InteropTypeEntry(alias, monoTypeName, ToIl2CppTypeName(monoTypeName), discoverMembers));
        }

        return entries.ToImmutable();
    }

    public static ImmutableArray<S1InteropMemberEntry> GetAssemblyMemberEntries(Compilation compilation)
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

    public static S1InteropBridgeRequests GetBridgeRequests(Compilation compilation) =>
        new(
            HasAssemblyAttribute(compilation, UnityEventBridgeAttributeMetadataName),
            HasAssemblyAttribute(compilation, DelegateEventBridgeAttributeMetadataName));

    private static bool HasAssemblyAttribute(Compilation compilation, string metadataName)
    {
        INamedTypeSymbol? attributeType = compilation.GetTypeByMetadataName(metadataName);
        return attributeType is not null &&
               compilation.Assembly.GetAttributes().Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType));
    }

    public static S1InteropTypeEntry? GetTypeEntry(GeneratorSyntaxContext context)
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

    public static S1InteropTypeEntry? TryCreateEntry(AttributeData attribute)
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

    private static S1InteropNamespaceEntry? TryCreateNamespaceEntry(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length == 0 ||
            attribute.ConstructorArguments[0].Value is not string namespaceName ||
            string.IsNullOrWhiteSpace(namespaceName))
        {
            return null;
        }

        bool includeSubnamespaces = false;
        bool includeMembers = false;
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (argument.Key == "IncludeSubnamespaces" && argument.Value.Value is bool value)
            {
                includeSubnamespaces = value;
            }
            else if (argument.Key == "IncludeMembers" && argument.Value.Value is bool includeMembersValue)
            {
                includeMembers = includeMembersValue;
            }
        }

        return new S1InteropNamespaceEntry(
            namespaceName.StartsWith("Il2Cpp", StringComparison.Ordinal)
                ? namespaceName.Substring("Il2Cpp".Length)
                : namespaceName,
            includeSubnamespaces,
            includeMembers);
    }

    private static bool IsNamespaceMatch(string monoTypeName, S1InteropNamespaceEntry entry)
    {
        string prefix = entry.NamespaceName + ".";
        if (!monoTypeName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (entry.IncludeSubnamespaces)
        {
            return true;
        }

        string remainder = monoTypeName.Substring(prefix.Length);
        return !remainder.Contains(".", StringComparison.Ordinal);
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateReferencedTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (INamedTypeSymbol type in namespaceSymbol.GetTypeMembers())
        {
            if (IsNamespaceImportCandidate(type))
            {
                yield return type;
            }
        }

        foreach (INamespaceSymbol child in namespaceSymbol.GetNamespaceMembers())
        {
            foreach (INamedTypeSymbol type in EnumerateReferencedTypes(child))
            {
                yield return type;
            }
        }
    }

    private static bool IsNamespaceImportCandidate(INamedTypeSymbol type) =>
        type.DeclaredAccessibility == Accessibility.Public &&
        type.TypeKind != TypeKind.Error &&
        type.TypeKind != TypeKind.Delegate &&
        type.TypeParameters.Length == 0 &&
        type.ContainingType is null &&
        !type.IsImplicitlyDeclared &&
        !string.Equals(type.Name, "Handle", StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(type.ContainingNamespace?.ToDisplayString());

    private static string ToMonoTypeName(string typeName)
    {
        string normalized = typeName.Replace("global::", string.Empty);
        return normalized.StartsWith("Il2Cpp", StringComparison.Ordinal)
            ? normalized.Substring("Il2Cpp".Length)
            : normalized;
    }

    private static string CreateQualifiedAlias(string monoTypeName)
    {
        string[] parts = monoTypeName.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return SanitizeIdentifier(monoTypeName);
        }

        int start = parts.Length > 1 && string.Equals(parts[0], "ScheduleOne", StringComparison.Ordinal) ? 1 : 0;
        var builder = new System.Text.StringBuilder();
        for (int index = start; index < parts.Length; index++)
        {
            builder.Append(SanitizeIdentifier(parts[index]));
        }

        return builder.Length == 0 ? SanitizeIdentifier(parts[parts.Length - 1]) : builder.ToString();
    }

    private static string EnsureUniqueAlias(string alias, HashSet<string> usedAliases)
    {
        string candidate = alias;
        int suffix = 2;
        while (!usedAliases.Add(candidate))
        {
            candidate = alias + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            suffix++;
        }

        return candidate;
    }

    public static ImmutableArray<S1InteropTypeEntry> MergeTypeEntries(ImmutableArray<S1InteropTypeEntry> entries)
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

        if (candidate.DiscoverMembers != existing.DiscoverMembers)
        {
            return candidate.DiscoverMembers;
        }

        return false;
    }

    public static S1InteropMemberEntry? TryCreateMemberEntry(AttributeData attribute)
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
            parameterTypeNames,
            null,
            ImmutableArray<string>.Empty);
    }
}
