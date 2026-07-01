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
    private const string MemberAttributeMetadataName = "S1Interop.S1InteropMemberAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static output =>
            output.AddSource("S1InteropTypeAttribute.g.cs", SourceText.From(GenerateAttributeSource(), Encoding.UTF8)));

        IncrementalValueProvider<RuntimeBackend> runtimeProvider = context.AnalyzerConfigOptionsProvider
            .Combine(context.ParseOptionsProvider)
            .Select(static (input, _) => ResolveRuntime(input.Left, input.Right));

        IncrementalValueProvider<ImmutableArray<S1InteropTypeEntry>> assemblyEntries = context.CompilationProvider
            .Select(static (compilation, _) => GetAssemblyEntries(compilation));
        IncrementalValueProvider<ImmutableArray<S1InteropMemberEntry>> memberEntries = context.CompilationProvider
            .Select(static (compilation, _) => GetAssemblyMemberEntries(compilation));

        IncrementalValuesProvider<S1InteropTypeEntry> attributedTypeEntries = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (context, _) => GetTypeEntry(context))
            .Where(static entry => entry is not null)
            .Select(static (entry, _) => entry!.Value);

        IncrementalValueProvider<ImmutableArray<S1InteropTypeEntry>> allEntries = assemblyEntries
            .Combine(attributedTypeEntries.Collect())
            .Select(static (input, _) => input.Left.AddRange(input.Right).Distinct(S1InteropTypeEntryComparer.Instance).ToImmutableArray());

        context.RegisterSourceOutput(runtimeProvider.Combine(allEntries).Combine(memberEntries), static (sourceContext, input) =>
        {
            sourceContext.AddSource(
                "S1Interop.TypeRegistry.g.cs",
                SourceText.From(GenerateRegistrySource(input.Left.Left, input.Left.Right, input.Right), Encoding.UTF8));
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
                kind = kindValue == (int)S1InteropMemberKind.Method
                    ? S1InteropMemberKind.Method
                    : S1InteropMemberKind.FieldOrProperty;
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

    private static string GenerateRegistrySource(
        RuntimeBackend runtime,
        ImmutableArray<S1InteropTypeEntry> entries,
        ImmutableArray<S1InteropMemberEntry> members)
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
        GenerateMemberRegistry(builder, runtime, entries, members);
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void GenerateMemberRegistry(
        StringBuilder builder,
        RuntimeBackend runtime,
        ImmutableArray<S1InteropTypeEntry> entries,
        ImmutableArray<S1InteropMemberEntry> members)
    {
        builder.AppendLine();
        builder.AppendLine("    internal static class S1InteropMemberRegistry");
        builder.AppendLine("    {");
        builder.AppendLine("        private const System.Reflection.BindingFlags AllBindings = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;");
        builder.AppendLine("        private static readonly System.Collections.Generic.Dictionary<string, System.Reflection.MemberInfo?> Cache = new System.Collections.Generic.Dictionary<string, System.Reflection.MemberInfo?>(System.StringComparer.Ordinal);");
        builder.AppendLine();

        foreach (S1InteropMemberEntry member in members.OrderBy(member => member.Alias, StringComparer.Ordinal))
        {
            builder.AppendLine($"        public const string {member.Alias}Name = \"{Escape(member.MemberName)}\";");
            if (member.Kind == S1InteropMemberKind.Method)
            {
                if (member.IsStatic)
                {
                    builder.AppendLine($"        public static object? Invoke{member.Alias}(params object?[] args) => Invoke(S1InteropTypeRegistry.{member.OwnerAlias}Name, {member.Alias}Name, {GenerateParameterTypeNamesExpression(runtime, entries, member)}, null, args);");
                }
                else
                {
                    builder.AppendLine($"        public static object? Invoke{member.Alias}(object? instance, params object?[] args) => Invoke(S1InteropTypeRegistry.{member.OwnerAlias}Name, {member.Alias}Name, {GenerateParameterTypeNamesExpression(runtime, entries, member)}, instance, args);");
                }
            }
            else
            {
                if (member.IsStatic)
                {
                    builder.AppendLine($"        public static object? Get{member.Alias}() => GetValue(S1InteropTypeRegistry.{member.OwnerAlias}Name, {member.Alias}Name, null);");
                    builder.AppendLine($"        public static bool TrySet{member.Alias}(object? value) => TrySetValue(S1InteropTypeRegistry.{member.OwnerAlias}Name, {member.Alias}Name, null, value);");
                }
                else
                {
                    builder.AppendLine($"        public static object? Get{member.Alias}(object instance) => GetValue(S1InteropTypeRegistry.{member.OwnerAlias}Name, {member.Alias}Name, instance);");
                    builder.AppendLine($"        public static bool TrySet{member.Alias}(object instance, object? value) => TrySetValue(S1InteropTypeRegistry.{member.OwnerAlias}Name, {member.Alias}Name, instance, value);");
                }
            }

            builder.AppendLine();
        }

        builder.AppendLine("        public static object? GetValue(string ownerTypeName, string memberName, object? instance)");
        builder.AppendLine("        {");
        builder.AppendLine("            System.Reflection.MemberInfo? member = ResolveMember(ownerTypeName, memberName, parameterTypeNames: null, preferMethod: false);");
        builder.AppendLine("            if (member is System.Reflection.PropertyInfo property)");
        builder.AppendLine("            {");
        builder.AppendLine("                return property.GetValue(instance, null);");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (member is System.Reflection.FieldInfo field)");
        builder.AppendLine("            {");
        builder.AppendLine("                return field.GetValue(instance);");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static bool TrySetValue(string ownerTypeName, string memberName, object? instance, object? value)");
        builder.AppendLine("        {");
        builder.AppendLine("            System.Reflection.MemberInfo? member = ResolveMember(ownerTypeName, memberName, parameterTypeNames: null, preferMethod: false);");
        builder.AppendLine("            if (member is System.Reflection.PropertyInfo property && property.CanWrite)");
        builder.AppendLine("            {");
        builder.AppendLine("                property.SetValue(instance, value, null);");
        builder.AppendLine("                return true;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (member is System.Reflection.FieldInfo field)");
        builder.AppendLine("            {");
        builder.AppendLine("                field.SetValue(instance, value);");
        builder.AppendLine("                return true;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static object? Invoke(string ownerTypeName, string memberName, string[]? parameterTypeNames, object? instance, params object?[] args)");
        builder.AppendLine("        {");
        builder.AppendLine("            return ResolveMember(ownerTypeName, memberName, parameterTypeNames, preferMethod: true) is System.Reflection.MethodInfo method");
        builder.AppendLine("                ? method.Invoke(instance, args)");
        builder.AppendLine("                : null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static System.Reflection.MemberInfo? ResolveMember(string ownerTypeName, string memberName, string[]? parameterTypeNames, bool preferMethod)");
        builder.AppendLine("        {");
        builder.AppendLine("            string cacheKey = ownerTypeName + \"::\" + memberName + \"::\" + preferMethod.ToString() + \"::\" + (parameterTypeNames is null ? string.Empty : string.Join(\"|\", parameterTypeNames));");
        builder.AppendLine("            if (!Cache.TryGetValue(cacheKey, out System.Reflection.MemberInfo? member))");
        builder.AppendLine("            {");
        builder.AppendLine("                System.Type? ownerType = S1InteropTypeRegistry.Resolve(ownerTypeName);");
        builder.AppendLine("                member = ownerType is null");
        builder.AppendLine("                    ? null");
        builder.AppendLine("                    : preferMethod");
        builder.AppendLine("                        ? (System.Reflection.MemberInfo?)ResolveMethod(ownerType, memberName, parameterTypeNames)");
        builder.AppendLine("                        : ownerType.GetProperty(memberName, AllBindings) ?? (System.Reflection.MemberInfo?)ownerType.GetField(memberName, AllBindings);");
        builder.AppendLine("                Cache[cacheKey] = member;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return member;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static System.Reflection.MethodInfo? ResolveMethod(System.Type ownerType, string memberName, string[]? parameterTypeNames)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (parameterTypeNames is null || parameterTypeNames.Length == 0)");
        builder.AppendLine("            {");
        builder.AppendLine("                return ownerType.GetMethod(memberName, AllBindings);");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Type[]? parameterTypes = ResolveParameterTypes(parameterTypeNames);");
        builder.AppendLine("            return parameterTypes is null ? null : ownerType.GetMethod(memberName, AllBindings, binder: null, types: parameterTypes, modifiers: null);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static System.Type[]? ResolveParameterTypes(string[] parameterTypeNames)");
        builder.AppendLine("        {");
        builder.AppendLine("            var parameterTypes = new System.Type[parameterTypeNames.Length];");
        builder.AppendLine("            for (int index = 0; index < parameterTypeNames.Length; index++)");
        builder.AppendLine("            {");
        builder.AppendLine("                string parameterTypeName = parameterTypeNames[index];");
        builder.AppendLine("                bool byRef = parameterTypeName.EndsWith(\"&\", System.StringComparison.Ordinal);");
        builder.AppendLine("                if (byRef)");
        builder.AppendLine("                {");
        builder.AppendLine("                    parameterTypeName = parameterTypeName.Substring(0, parameterTypeName.Length - 1);");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                System.Type? parameterType = ResolveKnownType(parameterTypeName);");
        builder.AppendLine("                if (parameterType is null)");
        builder.AppendLine("                {");
        builder.AppendLine("                    return null;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                parameterTypes[index] = byRef ? parameterType.MakeByRefType() : parameterType;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return parameterTypes;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static System.Type? ResolveKnownType(string typeName)");
        builder.AppendLine("        {");
        builder.AppendLine("            switch (typeName)");
        builder.AppendLine("            {");
        builder.AppendLine("                case \"bool\": return typeof(bool);");
        builder.AppendLine("                case \"byte\": return typeof(byte);");
        builder.AppendLine("                case \"char\": return typeof(char);");
        builder.AppendLine("                case \"double\": return typeof(double);");
        builder.AppendLine("                case \"float\": return typeof(float);");
        builder.AppendLine("                case \"int\": return typeof(int);");
        builder.AppendLine("                case \"long\": return typeof(long);");
        builder.AppendLine("                case \"object\": return typeof(object);");
        builder.AppendLine("                case \"short\": return typeof(short);");
        builder.AppendLine("                case \"string\": return typeof(string);");
        builder.AppendLine("                case \"uint\": return typeof(uint);");
        builder.AppendLine("                case \"ulong\": return typeof(ulong);");
        builder.AppendLine("                case \"void\": return typeof(void);");
        builder.AppendLine("                default: return S1InteropTypeRegistry.Resolve(typeName);");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    private static string GenerateParameterTypeNamesExpression(
        RuntimeBackend runtime,
        ImmutableArray<S1InteropTypeEntry> entries,
        S1InteropMemberEntry member)
    {
        if (member.ParameterTypeNames.IsDefaultOrEmpty)
        {
            return "null";
        }

        var parts = member.ParameterTypeNames
            .Select(parameter => GenerateParameterTypeNameExpression(runtime, entries, parameter));
        return "new string[] { " + string.Join(", ", parts) + " }";
    }

    private static string GenerateParameterTypeNameExpression(
        RuntimeBackend runtime,
        ImmutableArray<S1InteropTypeEntry> entries,
        string parameterTypeName)
    {
        bool byRef = parameterTypeName.EndsWith("&", StringComparison.Ordinal);
        string normalized = byRef ? parameterTypeName.Substring(0, parameterTypeName.Length - 1) : parameterTypeName;
        string sanitized = SanitizeIdentifier(normalized);
        S1InteropTypeEntry? matchingEntry = FindEntryByAlias(entries, sanitized);
        if (matchingEntry.HasValue)
        {
            return byRef
                ? $"S1InteropTypeRegistry.{matchingEntry.Value.Alias}Name + \"&\""
                : $"S1InteropTypeRegistry.{matchingEntry.Value.Alias}Name";
        }

        string runtimeTypeName = runtime == RuntimeBackend.Il2Cpp
            ? ToIl2CppTypeName(normalized)
            : normalized;
        return $"\"{Escape(runtimeTypeName + (byRef ? "&" : string.Empty))}\"";
    }

    private static S1InteropTypeEntry? FindEntryByAlias(ImmutableArray<S1InteropTypeEntry> entries, string alias)
    {
        foreach (S1InteropTypeEntry entry in entries)
        {
            if (string.Equals(entry.Alias, alias, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        return null;
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

            internal enum S1InteropMemberKind
            {
                FieldOrProperty = 0,
                Method = 1
            }

            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
            internal sealed class S1InteropMemberAttribute : System.Attribute
            {
                public S1InteropMemberAttribute(string ownerAlias, string memberName)
                {
                    OwnerAlias = ownerAlias;
                    MemberName = memberName;
                }

                public string OwnerAlias { get; }

                public string MemberName { get; }

                public string? Alias { get; set; }

                public S1InteropMemberKind Kind { get; set; }

                public bool IsStatic { get; set; }

                public string[] ParameterTypeNames { get; set; } = System.Array.Empty<string>();
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

    private enum S1InteropMemberKind
    {
        FieldOrProperty,
        Method
    }

    private readonly struct S1InteropMemberEntry
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

    private sealed class S1InteropMemberEntryComparer : IEqualityComparer<S1InteropMemberEntry>
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
}
