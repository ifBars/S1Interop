using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace S1Interop.Generators;

public sealed partial class S1InteropTypeRegistryGenerator
{
    private static void ReportDeclarationDiagnostics(SourceProductionContext context, Compilation compilation)
    {
        ReportIl2CppSourceDiagnostics(context, compilation);

        ImmutableArray<S1InteropPatchEntry> patchTargets = InteropDeclarationReader.GetPatchEntries(compilation);
        ImmutableArray<S1InteropTypeDiagnosticTarget> typeTargets = GetDeclaredTypeDiagnosticTargets(compilation)
            .AddRange(patchTargets.Select(patch => new S1InteropTypeDiagnosticTarget(patch.OwnerEntry, patch.Location)))
            .Distinct(S1InteropTypeDiagnosticTargetComparer.Instance)
            .ToImmutableArray();
        ImmutableArray<S1InteropMemberDiagnosticTarget> memberTargets = GetDeclaredMemberDiagnosticTargets(compilation)
            .AddRange(patchTargets.Select(patch => new S1InteropMemberDiagnosticTarget(patch.TargetMemberEntry, patch.Location)))
            .Distinct(S1InteropMemberDiagnosticTargetComparer.Instance)
            .ToImmutableArray();
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
            S1InteropTypeEntry? entry = InteropDeclarationReader.TryCreateEntry(attribute);
            if (entry is not null)
            {
                targets.Add(new S1InteropTypeDiagnosticTarget(entry.Value, GetAttributeLocation(attribute)));
            }
        }

        foreach (INamedTypeSymbol typeSymbol in GetSourceTypeSymbols(compilation))
        {
            AttributeData? attribute = typeSymbol.GetAttributes()
                .FirstOrDefault(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType));
            S1InteropTypeEntry? entry = attribute is null ? null : InteropDeclarationReader.TryCreateEntry(attribute);
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
            .Select(attribute => (Entry: InteropDeclarationReader.TryCreateMemberEntry(attribute), Location: GetAttributeLocation(attribute)))
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

}
