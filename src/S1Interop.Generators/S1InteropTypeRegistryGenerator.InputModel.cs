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

    private static void ReportDeclarationDiagnostics(SourceProductionContext context, Compilation compilation)
    {
        ReportIl2CppSourceDiagnostics(context, compilation);

        ImmutableArray<S1InteropTypeDiagnosticTarget> typeTargets = GetDeclaredTypeDiagnosticTargets(compilation);
        ImmutableArray<S1InteropMemberDiagnosticTarget> memberTargets = GetDeclaredMemberDiagnosticTargets(compilation);
        if (typeTargets.IsDefaultOrEmpty && memberTargets.IsDefaultOrEmpty)
        {
            return;
        }

        bool hasMonoSurface = HasRuntimeSurface(compilation, RuntimeBackend.Mono);
        bool hasIl2CppSurface = HasRuntimeSurface(compilation, RuntimeBackend.Il2Cpp);
        if (!hasMonoSurface && !hasIl2CppSurface)
        {
            return;
        }

        Dictionary<string, S1InteropTypeEntry> entriesByAlias = typeTargets
            .Select(target => target.Entry)
            .GroupBy(entry => entry.Alias, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (S1InteropTypeDiagnosticTarget target in typeTargets)
        {
            if (hasMonoSurface && compilation.GetTypeByMetadataName(target.Entry.MonoTypeName) is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    TypeNotFoundDiagnostic,
                    target.Location,
                    target.Entry.MonoTypeName,
                    target.Entry.Alias,
                    "Mono"));
            }

            if (hasIl2CppSurface && compilation.GetTypeByMetadataName(target.Entry.Il2CppTypeName) is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    TypeNotFoundDiagnostic,
                    target.Location,
                    target.Entry.Il2CppTypeName,
                    target.Entry.Alias,
                    "IL2CPP"));
            }
        }

        foreach (S1InteropMemberDiagnosticTarget target in memberTargets)
        {
            if (!entriesByAlias.TryGetValue(target.Entry.OwnerAlias, out S1InteropTypeEntry ownerEntry))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MemberOwnerNotFoundDiagnostic,
                    target.Location,
                    target.Entry.MemberName,
                    target.Entry.OwnerAlias));
                continue;
            }

            if (hasMonoSurface)
            {
                ReportMissingMemberDiagnostic(context, compilation, target, ownerEntry.MonoTypeName, RuntimeBackend.Mono, entriesByAlias, "Mono");
            }

            if (hasIl2CppSurface)
            {
                ReportMissingMemberDiagnostic(context, compilation, target, ownerEntry.Il2CppTypeName, RuntimeBackend.Il2Cpp, entriesByAlias, "IL2CPP");
            }
        }
    }

    private static ImmutableArray<S1InteropTypeDiagnosticTarget> GetDeclaredTypeDiagnosticTargets(Compilation compilation)
    {
        INamedTypeSymbol? attributeType = compilation.GetTypeByMetadataName(AttributeMetadataName);
        if (attributeType is null)
        {
            return ImmutableArray<S1InteropTypeDiagnosticTarget>.Empty;
        }

        var targets = ImmutableArray.CreateBuilder<S1InteropTypeDiagnosticTarget>();
        foreach (AttributeData attribute in compilation.Assembly.GetAttributes().Where(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType)))
        {
            S1InteropTypeEntry? entry = TryCreateEntry(attribute);
            if (entry is not null)
            {
                targets.Add(new S1InteropTypeDiagnosticTarget(entry.Value, GetAttributeLocation(attribute)));
            }
        }

        foreach (INamedTypeSymbol typeSymbol in GetSourceTypeSymbols(compilation))
        {
            AttributeData? attribute = typeSymbol.GetAttributes()
                .FirstOrDefault(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType));
            S1InteropTypeEntry? entry = attribute is null ? null : TryCreateEntry(attribute);
            if (entry is null)
            {
                continue;
            }

            S1InteropTypeEntry resolvedEntry = string.IsNullOrWhiteSpace(entry.Value.Alias)
                ? entry.Value.WithAlias(SanitizeIdentifier(typeSymbol.Name))
                : entry.Value;
            targets.Add(new S1InteropTypeDiagnosticTarget(resolvedEntry, GetAttributeLocation(attribute!)));
        }

        return targets.Distinct(S1InteropTypeDiagnosticTargetComparer.Instance).ToImmutableArray();
    }

    private static ImmutableArray<S1InteropMemberDiagnosticTarget> GetDeclaredMemberDiagnosticTargets(Compilation compilation)
    {
        INamedTypeSymbol? attributeType = compilation.GetTypeByMetadataName(MemberAttributeMetadataName);
        if (attributeType is null)
        {
            return ImmutableArray<S1InteropMemberDiagnosticTarget>.Empty;
        }

        return compilation.Assembly.GetAttributes()
            .Where(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
            .Select(attribute => (Entry: TryCreateMemberEntry(attribute), Location: GetAttributeLocation(attribute)))
            .Where(target => target.Entry is not null)
            .Select(target => new S1InteropMemberDiagnosticTarget(target.Entry!.Value, target.Location))
            .Distinct(S1InteropMemberDiagnosticTargetComparer.Instance)
            .ToImmutableArray();
    }

    private static IEnumerable<INamedTypeSymbol> GetSourceTypeSymbols(Compilation compilation)
    {
        foreach (SyntaxTree syntaxTree in compilation.SyntaxTrees)
        {
            SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            foreach (TypeDeclarationSyntax typeDeclaration in syntaxTree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (semanticModel.GetDeclaredSymbol(typeDeclaration) is INamedTypeSymbol typeSymbol)
                {
                    yield return typeSymbol;
                }
            }
        }
    }

    private static Location? GetAttributeLocation(AttributeData attribute) =>
        attribute.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax syntax
            ? syntax.GetLocation()
            : null;

    private static bool HasRuntimeSurface(Compilation compilation, RuntimeBackend runtime)
    {
        string[] probeTypes = runtime == RuntimeBackend.Il2Cpp
            ? DefaultIl2CppRuntimeProbeTypeNames
            : DefaultMonoRuntimeProbeTypeNames;
        return probeTypes.Any(typeName => compilation.GetTypeByMetadataName(typeName) is not null);
    }

    private static void ReportMissingMemberDiagnostic(
        SourceProductionContext context,
        Compilation compilation,
        S1InteropMemberDiagnosticTarget target,
        string runtimeTypeName,
        RuntimeBackend runtime,
        IReadOnlyDictionary<string, S1InteropTypeEntry> entriesByAlias,
        string runtimeName)
    {
        INamedTypeSymbol? ownerType = compilation.GetTypeByMetadataName(runtimeTypeName);
        if (ownerType is null || HasMember(ownerType, target.Entry, runtime, entriesByAlias))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            MemberNotFoundDiagnostic,
            target.Location,
            target.Entry.MemberName,
            target.Entry.OwnerAlias,
            runtimeTypeName,
            runtimeName));
    }

    private static bool HasMember(
        INamedTypeSymbol ownerType,
        S1InteropMemberEntry entry,
        RuntimeBackend runtime,
        IReadOnlyDictionary<string, S1InteropTypeEntry> entriesByAlias)
    {
        IEnumerable<ISymbol> members = ownerType.GetMembers(entry.MemberName);
        if (entry.IsStatic)
        {
            members = members.Where(member => member.IsStatic);
        }

        return entry.Kind switch
        {
            S1InteropMemberKind.Field => members.Any(member => member.Kind == SymbolKind.Field),
            S1InteropMemberKind.Property => members.Any(member => member.Kind == SymbolKind.Property),
            S1InteropMemberKind.Method => members.OfType<IMethodSymbol>().Any(method => ParameterTypesMatch(method, entry, runtime, entriesByAlias)),
            _ => members.Any(member =>
                member.Kind == SymbolKind.Field ||
                member.Kind == SymbolKind.Property ||
                (member is IMethodSymbol method && ParameterTypesMatch(method, entry, runtime, entriesByAlias)))
        };
    }

    private static bool ParameterTypesMatch(
        IMethodSymbol method,
        S1InteropMemberEntry entry,
        RuntimeBackend runtime,
        IReadOnlyDictionary<string, S1InteropTypeEntry> entriesByAlias)
    {
        if (entry.ParameterTypeNames.IsDefaultOrEmpty)
        {
            return true;
        }

        if (method.Parameters.Length != entry.ParameterTypeNames.Length)
        {
            return false;
        }

        for (int index = 0; index < method.Parameters.Length; index++)
        {
            if (!ParameterTypeMatches(method.Parameters[index], entry.ParameterTypeNames[index], runtime, entriesByAlias))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ParameterTypeMatches(
        IParameterSymbol parameter,
        string declaredTypeName,
        RuntimeBackend runtime,
        IReadOnlyDictionary<string, S1InteropTypeEntry> entriesByAlias)
    {
        bool declaredByRef = declaredTypeName.EndsWith("&", StringComparison.Ordinal);
        bool actualByRef = parameter.RefKind != RefKind.None;
        if (declaredByRef != actualByRef)
        {
            return false;
        }

        string expected = NormalizeComparableTypeName(ResolveDeclaredParameterTypeName(declaredTypeName, runtime, entriesByAlias));
        string actual = NormalizeComparableTypeName(GetComparableTypeName(parameter.Type));
        return string.Equals(expected, actual, StringComparison.Ordinal);
    }

    private static string ResolveDeclaredParameterTypeName(
        string declaredTypeName,
        RuntimeBackend runtime,
        IReadOnlyDictionary<string, S1InteropTypeEntry> entriesByAlias)
    {
        bool byRef = declaredTypeName.EndsWith("&", StringComparison.Ordinal);
        string normalized = byRef ? declaredTypeName.Substring(0, declaredTypeName.Length - 1) : declaredTypeName;
        string sanitized = SanitizeIdentifier(normalized);
        if (entriesByAlias.TryGetValue(sanitized, out S1InteropTypeEntry entry))
        {
            return runtime == RuntimeBackend.Il2Cpp ? entry.Il2CppTypeName : entry.MonoTypeName;
        }

        string runtimeTypeName = runtime == RuntimeBackend.Il2Cpp
            ? ToIl2CppTypeName(normalized)
            : normalized;
        return runtimeTypeName;
    }

    private static string GetComparableTypeName(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arrayType)
        {
            return GetComparableTypeName(arrayType.ElementType) + "[]";
        }

        if (type is not INamedTypeSymbol namedType)
        {
            return type.Name;
        }

        string? specialTypeName = GetSpecialTypeName(namedType.SpecialType);
        if (specialTypeName is not null)
        {
            return specialTypeName;
        }

        string fullName = GetFullMetadataName(namedType);
        if (!namedType.IsGenericType)
        {
            return fullName;
        }

        string[] arguments = namedType.TypeArguments
            .Select(GetComparableTypeName)
            .ToArray();
        return fullName + "<" + string.Join(", ", arguments) + ">";
    }

    private static string GetFullMetadataName(INamedTypeSymbol namedType)
    {
        string name = namedType.Name;
        if (namedType.ContainingType is not null)
        {
            return GetFullMetadataName(namedType.ContainingType) + "." + name;
        }

        string namespaceName = namedType.ContainingNamespace?.IsGlobalNamespace == false
            ? namedType.ContainingNamespace.ToDisplayString()
            : string.Empty;
        return string.IsNullOrWhiteSpace(namespaceName) ? name : namespaceName + "." + name;
    }

    private static string? GetSpecialTypeName(SpecialType specialType) =>
        specialType switch
        {
            SpecialType.System_Boolean => "bool",
            SpecialType.System_Byte => "byte",
            SpecialType.System_Char => "char",
            SpecialType.System_Decimal => "decimal",
            SpecialType.System_Double => "double",
            SpecialType.System_Int16 => "short",
            SpecialType.System_Int32 => "int",
            SpecialType.System_Int64 => "long",
            SpecialType.System_Object => "object",
            SpecialType.System_SByte => "sbyte",
            SpecialType.System_Single => "float",
            SpecialType.System_String => "string",
            SpecialType.System_UInt16 => "ushort",
            SpecialType.System_UInt32 => "uint",
            SpecialType.System_UInt64 => "ulong",
            _ => null
        };

    private static string NormalizeComparableTypeName(string typeName) =>
        typeName
            .Replace("global::", string.Empty)
            .Replace("System.Boolean", "bool")
            .Replace("System.Byte", "byte")
            .Replace("System.Char", "char")
            .Replace("System.Decimal", "decimal")
            .Replace("System.Double", "double")
            .Replace("System.Int16", "short")
            .Replace("System.Int32", "int")
            .Replace("System.Int64", "long")
            .Replace("System.Object", "object")
            .Replace("System.SByte", "sbyte")
            .Replace("System.Single", "float")
            .Replace("System.String", "string")
            .Replace("System.UInt16", "ushort")
            .Replace("System.UInt32", "uint")
            .Replace("System.UInt64", "ulong")
            .Replace(" ", string.Empty)
            .Trim();

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
