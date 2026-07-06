using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace S1Interop.Generators.Discovery;

internal static class PublicEnumCatalog
{
    public static ImmutableArray<S1InteropEnumEntry> DiscoverEnumEntries(
        Compilation compilation,
        ImmutableArray<S1InteropTypeEntry> entries)
    {
        if (entries.IsDefaultOrEmpty)
        {
            return ImmutableArray<S1InteropEnumEntry>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<S1InteropEnumEntry>();
        foreach (S1InteropTypeEntry entry in entries)
        {
            DiscoveredEnum? monoEnum = DiscoverEnum(compilation.GetTypeByMetadataName(entry.MonoTypeName));
            DiscoveredEnum? il2CppEnum = DiscoverEnum(compilation.GetTypeByMetadataName(entry.Il2CppTypeName));
            if (monoEnum.HasValue && il2CppEnum.HasValue)
            {
                if (!AreEnumsCompatible(monoEnum.Value, il2CppEnum.Value))
                {
                    continue;
                }

                builder.Add(CreateEnumEntry(entry, monoEnum.Value));
                continue;
            }

            DiscoveredEnum? availableEnum = monoEnum ?? il2CppEnum;
            if (availableEnum.HasValue)
            {
                builder.Add(CreateEnumEntry(entry, availableEnum.Value));
            }
        }

        return builder.ToImmutable();
    }

    private static S1InteropEnumEntry CreateEnumEntry(S1InteropTypeEntry entry, DiscoveredEnum discovered) =>
        new(
            entry.Alias,
            entry.MonoTypeName,
            entry.Il2CppTypeName,
            discovered.UnderlyingTypeName,
            discovered.Values);

    private static DiscoveredEnum? DiscoverEnum(INamedTypeSymbol? type)
    {
        if (type is null || type.TypeKind != TypeKind.Enum)
        {
            return null;
        }

        string underlyingTypeName = type.EnumUnderlyingType is null
            ? "int"
            : NormalizeComparableTypeName(type.EnumUnderlyingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        var values = type.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(field => field.DeclaredAccessibility == Accessibility.Public && field.IsStatic && field.HasConstantValue)
            .OrderBy(field => field.MetadataName, StringComparer.Ordinal)
            .Select(field => new S1InteropEnumValueEntry(field.MetadataName, CreateEnumValueLiteral(field.ConstantValue)))
            .ToImmutableArray();
        return new DiscoveredEnum(underlyingTypeName, values);
    }

    private static bool AreEnumsCompatible(DiscoveredEnum monoEnum, DiscoveredEnum il2CppEnum)
    {
        if (!string.Equals(monoEnum.UnderlyingTypeName, il2CppEnum.UnderlyingTypeName, StringComparison.Ordinal) ||
            monoEnum.Values.Length != il2CppEnum.Values.Length)
        {
            return false;
        }

        for (int index = 0; index < monoEnum.Values.Length; index++)
        {
            if (!string.Equals(monoEnum.Values[index].Name, il2CppEnum.Values[index].Name, StringComparison.Ordinal) ||
                !string.Equals(monoEnum.Values[index].ValueLiteral, il2CppEnum.Values[index].ValueLiteral, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string CreateEnumValueLiteral(object? value) =>
        value switch
        {
            byte typed => typed.ToString(CultureInfo.InvariantCulture),
            sbyte typed => typed.ToString(CultureInfo.InvariantCulture),
            short typed => typed.ToString(CultureInfo.InvariantCulture),
            ushort typed => typed.ToString(CultureInfo.InvariantCulture),
            int typed => typed.ToString(CultureInfo.InvariantCulture),
            uint typed => typed.ToString(CultureInfo.InvariantCulture) + "u",
            long typed => typed.ToString(CultureInfo.InvariantCulture) + "L",
            ulong typed => typed.ToString(CultureInfo.InvariantCulture) + "UL",
            _ => "0"
        };

    private readonly struct DiscoveredEnum
    {
        public DiscoveredEnum(string underlyingTypeName, ImmutableArray<S1InteropEnumValueEntry> values)
        {
            UnderlyingTypeName = underlyingTypeName;
            Values = values;
        }

        public string UnderlyingTypeName { get; }

        public ImmutableArray<S1InteropEnumValueEntry> Values { get; }
    }
}
