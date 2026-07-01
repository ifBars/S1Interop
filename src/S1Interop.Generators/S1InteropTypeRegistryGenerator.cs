using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace S1Interop.Generators;

[Generator]
public sealed class S1InteropTypeRegistryGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName = "S1Interop.S1InteropTypeAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static output =>
            output.AddSource("S1InteropTypeAttribute.g.cs", SourceText.From(GenerateAttributeSource(), Encoding.UTF8)));

        IncrementalValueProvider<RuntimeBackend> runtimeProvider = context.AnalyzerConfigOptionsProvider
            .Combine(context.ParseOptionsProvider)
            .Select(static (input, _) => ResolveRuntime(input.Left, input.Right));

        IncrementalValueProvider<ImmutableArray<S1InteropTypeEntry>> assemblyEntries = context.CompilationProvider
            .Select(static (compilation, _) => GetAssemblyEntries(compilation));

        IncrementalValuesProvider<S1InteropTypeEntry> attributedTypeEntries = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (context, _) => GetTypeEntry(context))
            .Where(static entry => entry is not null)
            .Select(static (entry, _) => entry!.Value);

        IncrementalValueProvider<ImmutableArray<S1InteropTypeEntry>> allEntries = assemblyEntries
            .Combine(attributedTypeEntries.Collect())
            .Select(static (input, _) => input.Left.AddRange(input.Right).Distinct(S1InteropTypeEntryComparer.Instance).ToImmutableArray());

        context.RegisterSourceOutput(runtimeProvider.Combine(allEntries), static (sourceContext, input) =>
        {
            sourceContext.AddSource(
                "S1Interop.TypeRegistry.g.cs",
                SourceText.From(GenerateRegistrySource(input.Left, input.Right), Encoding.UTF8));
        });
    }

    private static RuntimeBackend ResolveRuntime(AnalyzerConfigOptionsProvider optionsProvider, ParseOptions parseOptions)
    {
        if (optionsProvider.GlobalOptions.TryGetValue("build_property.S1InteropTargetRuntime", out string? propertyValue) &&
            TryParseRuntime(propertyValue, out RuntimeBackend propertyRuntime))
        {
            return propertyRuntime;
        }

        if (parseOptions is CSharpParseOptions csharpOptions)
        {
            if (csharpOptions.PreprocessorSymbolNames.Contains("IL2CPP", StringComparer.OrdinalIgnoreCase))
            {
                return RuntimeBackend.Il2Cpp;
            }

            if (csharpOptions.PreprocessorSymbolNames.Contains("MONO", StringComparer.OrdinalIgnoreCase))
            {
                return RuntimeBackend.Mono;
            }
        }

        return RuntimeBackend.Unknown;
    }

    private static bool TryParseRuntime(string? value, out RuntimeBackend runtime)
    {
        if (string.Equals(value, "Mono", StringComparison.OrdinalIgnoreCase))
        {
            runtime = RuntimeBackend.Mono;
            return true;
        }

        if (string.Equals(value, "Il2Cpp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "IL2CPP", StringComparison.OrdinalIgnoreCase))
        {
            runtime = RuntimeBackend.Il2Cpp;
            return true;
        }

        runtime = RuntimeBackend.Unknown;
        return false;
    }

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

    private static string GenerateRegistrySource(RuntimeBackend runtime, ImmutableArray<S1InteropTypeEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("// Generated by S1Interop.Generators. Do not edit by hand.");
        builder.AppendLine();
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.AppendLine("namespace S1Interop.Generated");
        builder.AppendLine("{");
        builder.AppendLine("    internal enum S1InteropRuntimeBackend");
        builder.AppendLine("    {");
        builder.AppendLine("        Unknown = 0,");
        builder.AppendLine("        Mono = 1,");
        builder.AppendLine("        Il2Cpp = 2");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    internal static class S1InteropRuntime");
        builder.AppendLine("    {");
        builder.AppendLine($"        public const S1InteropRuntimeBackend Backend = S1InteropRuntimeBackend.{runtime};");
        builder.AppendLine($"        public const bool IsMono = {ToCSharpBoolean(runtime == RuntimeBackend.Mono)};");
        builder.AppendLine($"        public const bool IsIl2Cpp = {ToCSharpBoolean(runtime == RuntimeBackend.Il2Cpp)};");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    internal static class S1InteropTypeRegistry");
        builder.AppendLine("    {");
        builder.AppendLine("        private static readonly System.Collections.Generic.Dictionary<string, System.Type?> Cache = new System.Collections.Generic.Dictionary<string, System.Type?>(System.StringComparer.Ordinal);");
        builder.AppendLine();

        foreach (S1InteropTypeEntry entry in entries.OrderBy(entry => entry.Alias, StringComparer.Ordinal))
        {
            string runtimeName = runtime == RuntimeBackend.Il2Cpp ? entry.Il2CppTypeName : entry.MonoTypeName;
            builder.AppendLine($"        public const string {entry.Alias}Name = \"{Escape(runtimeName)}\";");
            builder.AppendLine($"        public static System.Type? {entry.Alias} => Resolve({entry.Alias}Name);");
            builder.AppendLine();
        }

        builder.AppendLine("        public static System.Type? Resolve(string runtimeTypeName)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (!Cache.TryGetValue(runtimeTypeName, out System.Type? type))");
        builder.AppendLine("            {");
        builder.AppendLine("                type = System.Type.GetType(runtimeTypeName, throwOnError: false);");
        builder.AppendLine("                Cache[runtimeTypeName] = type;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return type;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string GenerateAttributeSource() =>
        """
        // <auto-generated />
        // Generated by S1Interop.Generators. Do not edit by hand.

        #nullable enable

        namespace S1Interop
        {
            [System.AttributeUsage(System.AttributeTargets.Assembly | System.AttributeTargets.Class | System.AttributeTargets.Struct, AllowMultiple = true)]
            internal sealed class S1InteropTypeAttribute : System.Attribute
            {
                public S1InteropTypeAttribute(string monoTypeName)
                {
                    MonoTypeName = monoTypeName;
                }

                public string MonoTypeName { get; }

                public string? Il2CppTypeName { get; set; }

                public string? Alias { get; set; }
            }
        }
        """;

    private static string ToIl2CppTypeName(string monoTypeName) =>
        monoTypeName.StartsWith("ScheduleOne.", StringComparison.Ordinal)
            ? "Il2Cpp" + monoTypeName
            : monoTypeName;

    private static string GetSimpleName(string typeName)
    {
        int separator = typeName.LastIndexOf('.');
        return separator < 0 ? typeName : typeName.Substring(separator + 1);
    }

    private static string SanitizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "RuntimeType";
        }

        var builder = new StringBuilder();
        foreach (char character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        }

        if (builder.Length == 0 || char.IsDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string ToCSharpBoolean(bool value) => value ? "true" : "false";

    private readonly struct S1InteropTypeEntry
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

    private enum RuntimeBackend
    {
        Unknown,
        Mono,
        Il2Cpp
    }

    private sealed class S1InteropTypeEntryComparer : IEqualityComparer<S1InteropTypeEntry>
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
}
