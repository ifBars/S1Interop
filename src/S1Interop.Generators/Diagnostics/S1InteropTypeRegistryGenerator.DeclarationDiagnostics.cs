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

        ReportPatchMissingMemberDiagnostics(context, compilation, patchTargets, entriesByAlias, hasMonoSurface, hasIl2CppSurface);
        ReportPatchTargetRiskDiagnostics(context, compilation, patchTargets, entriesByAlias, hasMonoSurface, hasIl2CppSurface);
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

        IEnumerable<IMethodSymbol> methodMembers = GetPatchMethods(ownerType, entry.MemberName);
        if (entry.IsStatic)
        {
            methodMembers = methodMembers.Where(member => member.IsStatic);
        }

        return entry.Kind switch
        {
            S1InteropMemberKind.Field => members.Any(member => member.Kind == SymbolKind.Field),
            S1InteropMemberKind.Property => members.Any(member => member.Kind == SymbolKind.Property),
            S1InteropMemberKind.Method => methodMembers.Any(method => ParameterTypesMatch(method, entry, runtime, entriesByAlias)),
            _ => members.Any(member =>
                member.Kind == SymbolKind.Field ||
                member.Kind == SymbolKind.Property) ||
                methodMembers.Any(method => ParameterTypesMatch(method, entry, runtime, entriesByAlias))
        };
    }

    private static void ReportPatchMissingMemberDiagnostics(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<S1InteropPatchEntry> patches,
        IReadOnlyDictionary<string, S1InteropTypeEntry> entriesByAlias,
        bool hasMonoSurface,
        bool hasIl2CppSurface)
    {
        foreach (S1InteropPatchEntry patch in patches)
        {
            if (!entriesByAlias.TryGetValue(patch.TargetMemberEntry.OwnerAlias, out S1InteropTypeEntry ownerEntry))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MemberOwnerNotFoundDiagnostic,
                    patch.Location,
                    patch.TargetMemberEntry.MemberName,
                    patch.TargetMemberEntry.OwnerAlias));
                continue;
            }

            bool monoHasMember = hasMonoSurface && TryHasMember(compilation, ownerEntry.MonoTypeName, patch.TargetMemberEntry, RuntimeBackend.Mono, entriesByAlias);
            bool il2CppHasMember = hasIl2CppSurface && TryHasMember(compilation, ownerEntry.Il2CppTypeName, patch.TargetMemberEntry, RuntimeBackend.Il2Cpp, entriesByAlias);
            bool anyVisibleMember = monoHasMember || il2CppHasMember;

            if (hasMonoSurface && !monoHasMember && (patch.Required || anyVisibleMember))
            {
                ReportMissingPatchMemberDiagnostic(context, patch, ownerEntry.MonoTypeName, "Mono");
            }

            if (hasIl2CppSurface && !il2CppHasMember && (patch.Required || anyVisibleMember))
            {
                ReportMissingPatchMemberDiagnostic(context, patch, ownerEntry.Il2CppTypeName, "IL2CPP");
            }
        }
    }

    private static bool TryHasMember(
        Compilation compilation,
        string runtimeTypeName,
        S1InteropMemberEntry entry,
        RuntimeBackend runtime,
        IReadOnlyDictionary<string, S1InteropTypeEntry> entriesByAlias)
    {
        INamedTypeSymbol? ownerType = compilation.GetTypeByMetadataName(runtimeTypeName);
        return ownerType is not null && HasMember(ownerType, entry, runtime, entriesByAlias);
    }

    private static void ReportMissingPatchMemberDiagnostic(
        SourceProductionContext context,
        S1InteropPatchEntry patch,
        string runtimeTypeName,
        string runtimeName)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            MemberNotFoundDiagnostic,
            patch.Location,
            patch.TargetMemberEntry.MemberName,
            patch.TargetMemberEntry.OwnerAlias,
            runtimeTypeName,
            runtimeName));
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

    private static void ReportPatchTargetRiskDiagnostics(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<S1InteropPatchEntry> patches,
        IReadOnlyDictionary<string, S1InteropTypeEntry> entriesByAlias,
        bool hasMonoSurface,
        bool hasIl2CppSurface)
    {
        foreach (S1InteropPatchEntry patch in patches)
        {
            if (!entriesByAlias.TryGetValue(patch.TargetMemberEntry.OwnerAlias, out S1InteropTypeEntry ownerEntry))
            {
                continue;
            }

            var reasons = new List<string>();
            if (hasMonoSurface)
            {
                AddPatchTargetRiskReasons(compilation, patch, ownerEntry.MonoTypeName, RuntimeBackend.Mono, entriesByAlias, "Mono", reasons);
            }

            if (hasIl2CppSurface)
            {
                AddPatchTargetRiskReasons(compilation, patch, ownerEntry.Il2CppTypeName, RuntimeBackend.Il2Cpp, entriesByAlias, "IL2CPP", reasons);
            }

            string[] distinctReasons = reasons
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (distinctReasons.Length == 0)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                PatchTargetRiskDiagnostic,
                patch.Location,
                patch.OwnerEntry.MonoTypeName,
                patch.TargetMemberEntry.MemberName,
                string.Join("; ", distinctReasons)));
        }
    }

    private static void AddPatchTargetRiskReasons(
        Compilation compilation,
        S1InteropPatchEntry patch,
        string runtimeTypeName,
        RuntimeBackend runtime,
        IReadOnlyDictionary<string, S1InteropTypeEntry> entriesByAlias,
        string runtimeName,
        List<string> reasons)
    {
        INamedTypeSymbol? ownerType = compilation.GetTypeByMetadataName(runtimeTypeName);
        if (ownerType is null)
        {
            return;
        }

        IMethodSymbol[] methods = GetMatchingPatchMethods(ownerType, patch.TargetMemberEntry, runtime, entriesByAlias).ToArray();
        if (methods.Length == 0)
        {
            return;
        }

        if (patch.TargetMemberEntry.ParameterTypeNames.IsDefaultOrEmpty && methods.Length > 1)
        {
            reasons.Add($"{runtimeName} target has {methods.Length} overloads and no ParameterTypeNames; set ParameterTypeNames so the generated patcher binds one method");
        }

        if (methods.Any(IsAccessorLikePatchTarget))
        {
            reasons.Add($"{runtimeName} target is an accessor, event accessor, or operator; IL2CPP may inline or redirect callers, so prefer a higher-level patch target when possible");
        }

        if (methods.Any(HasAggressiveInliningOrOptimization))
        {
            reasons.Add($"{runtimeName} target is marked for aggressive inlining or optimization; IL2CPP may inline callers, so verify the handler fires on the IL2CPP branch");
        }
    }

    private static IEnumerable<IMethodSymbol> GetMatchingPatchMethods(
        INamedTypeSymbol ownerType,
        S1InteropMemberEntry entry,
        RuntimeBackend runtime,
        IReadOnlyDictionary<string, S1InteropTypeEntry> entriesByAlias)
    {
        IEnumerable<IMethodSymbol> methods = GetPatchMethods(ownerType, entry.MemberName);
        if (entry.IsStatic)
        {
            methods = methods.Where(method => method.IsStatic);
        }

        return methods.Where(method => ParameterTypesMatch(method, entry, runtime, entriesByAlias));
    }

    private static IEnumerable<IMethodSymbol> GetPatchMethods(INamedTypeSymbol ownerType, string memberName)
    {
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (IMethodSymbol method in ownerType.GetMembers(memberName).OfType<IMethodSymbol>())
        {
            if (seen.Add(method))
            {
                yield return method;
            }
        }

        foreach (IPropertySymbol property in ownerType.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.GetMethod is IMethodSymbol getter &&
                string.Equals(getter.Name, memberName, StringComparison.Ordinal) &&
                seen.Add(getter))
            {
                yield return getter;
            }

            if (property.SetMethod is IMethodSymbol setter &&
                string.Equals(setter.Name, memberName, StringComparison.Ordinal) &&
                seen.Add(setter))
            {
                yield return setter;
            }
        }

        foreach (IEventSymbol eventSymbol in ownerType.GetMembers().OfType<IEventSymbol>())
        {
            if (eventSymbol.AddMethod is IMethodSymbol addMethod &&
                string.Equals(addMethod.Name, memberName, StringComparison.Ordinal) &&
                seen.Add(addMethod))
            {
                yield return addMethod;
            }

            if (eventSymbol.RemoveMethod is IMethodSymbol removeMethod &&
                string.Equals(removeMethod.Name, memberName, StringComparison.Ordinal) &&
                seen.Add(removeMethod))
            {
                yield return removeMethod;
            }
        }
    }

    private static bool IsAccessorLikePatchTarget(IMethodSymbol method) =>
        method.MethodKind is MethodKind.PropertyGet or
            MethodKind.PropertySet or
            MethodKind.EventAdd or
            MethodKind.EventRemove or
            MethodKind.UserDefinedOperator or
            MethodKind.Conversion ||
        method.Name.StartsWith("get_", StringComparison.Ordinal) ||
        method.Name.StartsWith("set_", StringComparison.Ordinal) ||
        method.Name.StartsWith("add_", StringComparison.Ordinal) ||
        method.Name.StartsWith("remove_", StringComparison.Ordinal) ||
        method.Name.StartsWith("op_", StringComparison.Ordinal);

    private static bool HasAggressiveInliningOrOptimization(IMethodSymbol method)
    {
        const int aggressiveInlining = 256;
        const int aggressiveOptimization = 512;
        int implementationFlags = (int)method.MethodImplementationFlags;
        if ((implementationFlags & aggressiveInlining) != 0 ||
            (implementationFlags & aggressiveOptimization) != 0)
        {
            return true;
        }

        return method.GetAttributes().Any(attribute =>
            string.Equals(attribute.AttributeClass?.ToDisplayString(), "System.Runtime.CompilerServices.MethodImplAttribute", StringComparison.Ordinal) &&
            attribute.ConstructorArguments.Any(IsAggressiveMethodImplOption));
    }

    private static bool IsAggressiveMethodImplOption(TypedConstant constant)
    {
        if (constant.Value is int value)
        {
            const int aggressiveInlining = 256;
            const int aggressiveOptimization = 512;
            return (value & aggressiveInlining) != 0 || (value & aggressiveOptimization) != 0;
        }

        string text = constant.Value?.ToString() ?? constant.ToString();
        return text.Contains("AggressiveInlining", StringComparison.Ordinal) ||
               text.Contains("AggressiveOptimization", StringComparison.Ordinal);
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
