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
    private const string UnityEventBridgeAttributeMetadataName = "S1Interop.S1InteropGenerateUnityEventBridgeAttribute";
    private const string DelegateEventBridgeAttributeMetadataName = "S1Interop.S1InteropGenerateDelegateEventBridgeAttribute";

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
        IncrementalValueProvider<S1InteropBridgeRequests> bridgeRequests = context.CompilationProvider
            .Select(static (compilation, _) => GetBridgeRequests(compilation));

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
        context.RegisterSourceOutput(bridgeRequests, static (sourceContext, requests) =>
        {
            if (requests.GenerateUnityEventBridge)
            {
                sourceContext.AddSource(
                    "S1Interop.UnityEventBridge.g.cs",
                    SourceText.From(GenerateUnityEventBridgeSource(), Encoding.UTF8));
            }

            if (requests.GenerateDelegateEventBridge)
            {
                sourceContext.AddSource(
                    "S1Interop.DelegateEventBridge.g.cs",
                    SourceText.From(GenerateDelegateEventBridgeSource(), Encoding.UTF8));
            }
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
        if (runtime == RuntimeBackend.Unknown)
        {
            builder.AppendLine("        private static S1InteropRuntimeBackend? cachedBackend;");
            builder.AppendLine("        public static S1InteropRuntimeBackend Backend => cachedBackend ??= DetectBackend();");
            builder.AppendLine("        public static bool IsMono => Backend == S1InteropRuntimeBackend.Mono;");
            builder.AppendLine("        public static bool IsIl2Cpp => Backend == S1InteropRuntimeBackend.Il2Cpp;");
            builder.AppendLine();
            builder.AppendLine("        private static S1InteropRuntimeBackend DetectBackend()");
            builder.AppendLine("        {");
            foreach (S1InteropTypeEntry entry in entries.OrderBy(entry => entry.Alias, StringComparer.Ordinal))
            {
                builder.AppendLine($"            if (S1InteropTypeRegistry.Resolve(\"{Escape(entry.Il2CppTypeName)}\") is not null)");
                builder.AppendLine("            {");
                builder.AppendLine("                return S1InteropRuntimeBackend.Il2Cpp;");
                builder.AppendLine("            }");
            }

            foreach (S1InteropTypeEntry entry in entries.OrderBy(entry => entry.Alias, StringComparer.Ordinal))
            {
                builder.AppendLine($"            if (S1InteropTypeRegistry.Resolve(\"{Escape(entry.MonoTypeName)}\") is not null)");
                builder.AppendLine("            {");
                builder.AppendLine("                return S1InteropRuntimeBackend.Mono;");
                builder.AppendLine("            }");
            }

            builder.AppendLine();
            builder.AppendLine("            return S1InteropRuntimeBackend.Unknown;");
            builder.AppendLine("        }");
        }
        else
        {
            builder.AppendLine($"        public const S1InteropRuntimeBackend Backend = S1InteropRuntimeBackend.{runtime};");
            builder.AppendLine($"        public const bool IsMono = {ToCSharpBoolean(runtime == RuntimeBackend.Mono)};");
            builder.AppendLine($"        public const bool IsIl2Cpp = {ToCSharpBoolean(runtime == RuntimeBackend.Il2Cpp)};");
        }

        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    internal static class S1InteropTypeRegistry");
        builder.AppendLine("    {");
        builder.AppendLine("        private static readonly System.Collections.Generic.Dictionary<string, System.Type?> Cache = new System.Collections.Generic.Dictionary<string, System.Type?>(System.StringComparer.Ordinal);");
        builder.AppendLine();

        foreach (S1InteropTypeEntry entry in entries.OrderBy(entry => entry.Alias, StringComparer.Ordinal))
        {
            if (runtime == RuntimeBackend.Unknown)
            {
                builder.AppendLine($"        public const string {entry.Alias}MonoName = \"{Escape(entry.MonoTypeName)}\";");
                builder.AppendLine($"        public const string {entry.Alias}Il2CppName = \"{Escape(entry.Il2CppTypeName)}\";");
                builder.AppendLine($"        public static string {entry.Alias}Name => GetRuntimeTypeName({entry.Alias}MonoName, {entry.Alias}Il2CppName);");
            }
            else
            {
                string runtimeName = runtime == RuntimeBackend.Il2Cpp ? entry.Il2CppTypeName : entry.MonoTypeName;
                builder.AppendLine($"        public const string {entry.Alias}Name = \"{Escape(runtimeName)}\";");
            }

            builder.AppendLine($"        public static System.Type? {entry.Alias} => Resolve({entry.Alias}Name);");
            builder.AppendLine($"        public static object? Create{entry.Alias}(params object?[] args) => Create({entry.Alias}Name, args);");
            builder.AppendLine($"        public static object? Get{entry.Alias}Static(string memberName) => S1InteropMemberRegistry.GetValue({entry.Alias}Name, memberName, null);");
            builder.AppendLine($"        public static bool TrySet{entry.Alias}Static(string memberName, object? value) => S1InteropMemberRegistry.TrySetValue({entry.Alias}Name, memberName, null, value);");
            builder.AppendLine($"        public static object? Invoke{entry.Alias}Static(string methodName, params object?[] args) => S1InteropMemberRegistry.Invoke({entry.Alias}Name, methodName, parameterTypeNames: null, null, args);");
            builder.AppendLine($"        public static object? Invoke{entry.Alias}Static(string methodName, string[]? parameterTypeNames, params object?[] args) => S1InteropMemberRegistry.Invoke({entry.Alias}Name, methodName, parameterTypeNames, null, args);");
            builder.AppendLine($"        public static bool Is{entry.Alias}(object? instance) => IsInstance(instance, {entry.Alias}Name);");
            builder.AppendLine($"        public static object? Get{entry.Alias}(object? instance, string memberName) => S1InteropMemberRegistry.GetInstanceValue(instance, memberName);");
            builder.AppendLine($"        public static bool TrySet{entry.Alias}(object? instance, string memberName, object? value) => S1InteropMemberRegistry.TrySetInstanceValue(instance, memberName, value);");
            builder.AppendLine($"        public static object? Invoke{entry.Alias}(object? instance, string methodName, params object?[] args) => S1InteropMemberRegistry.InvokeInstance(instance, methodName, args);");
            builder.AppendLine($"        public static object? Invoke{entry.Alias}(object? instance, string methodName, string[]? parameterTypeNames, params object?[] args) => S1InteropMemberRegistry.InvokeInstance(instance, methodName, parameterTypeNames, args);");
            builder.AppendLine();
        }

        builder.AppendLine("        public static string GetRuntimeTypeName(string monoTypeName, string il2CppTypeName)");
        builder.AppendLine("        {");
        builder.AppendLine("            return S1InteropRuntime.Backend == S1InteropRuntimeBackend.Il2Cpp ? il2CppTypeName : monoTypeName;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static System.Type? Resolve(string runtimeTypeName)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (!Cache.TryGetValue(runtimeTypeName, out System.Type? type))");
        builder.AppendLine("            {");
        builder.AppendLine("                type = System.Type.GetType(runtimeTypeName, throwOnError: false);");
        builder.AppendLine("                if (type is null)");
        builder.AppendLine("                {");
        builder.AppendLine("                    type = ResolveFromLoadedAssemblies(runtimeTypeName);");
        builder.AppendLine("                }");
        builder.AppendLine("                Cache[runtimeTypeName] = type;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return type;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static object? Create(string runtimeTypeName, params object?[] args)");
        builder.AppendLine("        {");
        builder.AppendLine("            System.Type? type = Resolve(runtimeTypeName);");
        builder.AppendLine("            if (type is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            foreach (System.Reflection.ConstructorInfo constructor in type.GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))");
        builder.AppendLine("            {");
        builder.AppendLine("                System.Reflection.ParameterInfo[] parameters = constructor.GetParameters();");
        builder.AppendLine("                if (parameters.Length != args.Length)");
        builder.AppendLine("                {");
        builder.AppendLine("                    continue;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                object?[] converted = new object?[args.Length];");
        builder.AppendLine("                bool compatible = true;");
        builder.AppendLine("                for (int index = 0; index < args.Length; index++)");
        builder.AppendLine("                {");
        builder.AppendLine("                    if (!S1InteropMemberRegistry.TryConvertValue(args[index], parameters[index].ParameterType, out object? convertedValue))");
        builder.AppendLine("                    {");
        builder.AppendLine("                        compatible = false;");
        builder.AppendLine("                        break;");
        builder.AppendLine("                    }");
        builder.AppendLine();
        builder.AppendLine("                    converted[index] = convertedValue;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                if (!compatible)");
        builder.AppendLine("                {");
        builder.AppendLine("                    continue;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                return constructor.Invoke(converted);");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static bool IsInstance(object? instance, string runtimeTypeName)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (instance is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Type? runtimeType = Resolve(runtimeTypeName);");
        builder.AppendLine("            return runtimeType is not null && runtimeType.IsInstanceOfType(instance);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static System.Type? ResolveFromLoadedAssemblies(string runtimeTypeName)");
        builder.AppendLine("        {");
        builder.AppendLine("            foreach (System.Reflection.Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())");
        builder.AppendLine("            {");
        builder.AppendLine("                System.Type? type = assembly.GetType(runtimeTypeName, throwOnError: false);");
        builder.AppendLine("                if (type is not null)");
        builder.AppendLine("                {");
        builder.AppendLine("                    return type;");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (runtimeTypeName.IndexOf('.') >= 0)");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            foreach (System.Reflection.Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())");
        builder.AppendLine("            {");
        builder.AppendLine("                foreach (System.Type type in GetLoadableTypes(assembly))");
        builder.AppendLine("                {");
        builder.AppendLine("                    if (string.Equals(type.Name, runtimeTypeName, System.StringComparison.Ordinal))");
        builder.AppendLine("                    {");
        builder.AppendLine("                        return type;");
        builder.AppendLine("                    }");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static System.Collections.Generic.IEnumerable<System.Type> GetLoadableTypes(System.Reflection.Assembly assembly)");
        builder.AppendLine("        {");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine("                return assembly.GetTypes();");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (System.Reflection.ReflectionTypeLoadException ex)");
        builder.AppendLine("            {");
        builder.AppendLine("                System.Collections.Generic.List<System.Type> types = new System.Collections.Generic.List<System.Type>();");
        builder.AppendLine("                foreach (System.Type? type in ex.Types)");
        builder.AppendLine("                {");
        builder.AppendLine("                    if (type is not null)");
        builder.AppendLine("                    {");
        builder.AppendLine("                        types.Add(type);");
        builder.AppendLine("                    }");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                return types;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        GenerateObjectCastRegistry(builder);
        GenerateDelegateBridge(builder);
        GenerateMemberRegistry(builder, runtime, entries, members);
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void GenerateObjectCastRegistry(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine("    internal static class S1InteropObjectCast");
        builder.AppendLine("    {");
        builder.AppendLine("        private static readonly System.Collections.Generic.Dictionary<string, System.Reflection.MethodInfo?> TryCastCache = new System.Collections.Generic.Dictionary<string, System.Reflection.MethodInfo?>(System.StringComparer.Ordinal);");
        builder.AppendLine();
        builder.AppendLine("        public static bool Is<T>(object? value, out T? result) where T : class");
        builder.AppendLine("        {");
        builder.AppendLine("            result = As<T>(value);");
        builder.AppendLine("            return result is not null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static T? As<T>(object? value) where T : class");
        builder.AppendLine("        {");
        builder.AppendLine("            if (value is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (value is T typed)");
        builder.AppendLine("            {");
        builder.AppendLine("                return typed;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Type valueType = value.GetType();");
        builder.AppendLine("            if (!IsIl2CppObjectBase(valueType))");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Reflection.MethodInfo? tryCast = ResolveTryCastMethod(valueType, typeof(T));");
        builder.AppendLine("            if (tryCast is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine("                return tryCast.Invoke(value, null) as T;");
        builder.AppendLine("            }");
        builder.AppendLine("            catch");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static System.Reflection.MethodInfo? ResolveTryCastMethod(System.Type valueType, System.Type targetType)");
        builder.AppendLine("        {");
        builder.AppendLine("            string cacheKey = (valueType.AssemblyQualifiedName ?? valueType.FullName ?? valueType.Name) + \"|\" + (targetType.AssemblyQualifiedName ?? targetType.FullName ?? targetType.Name);");
        builder.AppendLine("            if (!TryCastCache.TryGetValue(cacheKey, out System.Reflection.MethodInfo? method))");
        builder.AppendLine("            {");
        builder.AppendLine("                method = FindTryCastMethod(valueType, targetType);");
        builder.AppendLine("                TryCastCache[cacheKey] = method;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return method;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static System.Reflection.MethodInfo? FindTryCastMethod(System.Type valueType, System.Type targetType)");
        builder.AppendLine("        {");
        builder.AppendLine("            for (System.Type? current = valueType; current is not null; current = current.BaseType)");
        builder.AppendLine("            {");
        builder.AppendLine("                foreach (System.Reflection.MethodInfo method in current.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))");
        builder.AppendLine("                {");
        builder.AppendLine("                    if (!method.Name.Equals(\"TryCast\", System.StringComparison.Ordinal) ||");
        builder.AppendLine("                        !method.IsGenericMethodDefinition ||");
        builder.AppendLine("                        method.GetGenericArguments().Length != 1 ||");
        builder.AppendLine("                        method.GetParameters().Length != 0)");
        builder.AppendLine("                    {");
        builder.AppendLine("                        continue;");
        builder.AppendLine("                    }");
        builder.AppendLine();
        builder.AppendLine("                    return method.MakeGenericMethod(targetType);");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool IsIl2CppObjectBase(System.Type valueType)");
        builder.AppendLine("        {");
        builder.AppendLine("            for (System.Type? current = valueType; current is not null; current = current.BaseType)");
        builder.AppendLine("            {");
        builder.AppendLine("                if (string.Equals(current.FullName, \"Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase\", System.StringComparison.Ordinal))");
        builder.AppendLine("                {");
        builder.AppendLine("                    return true;");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    private static void GenerateDelegateBridge(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine("    internal static class S1InteropDelegateBridge");
        builder.AppendLine("    {");
        builder.AppendLine("        private static readonly System.Collections.Generic.Dictionary<string, System.Reflection.MethodInfo?> ConvertDelegateCache = new System.Collections.Generic.Dictionary<string, System.Reflection.MethodInfo?>(System.StringComparer.Ordinal);");
        builder.AppendLine();
        builder.AppendLine("        public static TDelegate Convert<TDelegate>(TDelegate listener) where TDelegate : class");
        builder.AppendLine("        {");
        builder.AppendLine("            if (listener is null || !S1InteropRuntime.IsIl2Cpp || listener is not System.Delegate delegateValue)");
        builder.AppendLine("            {");
        builder.AppendLine("                return listener;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Reflection.MethodInfo? convertDelegate = ResolveConvertDelegate(typeof(TDelegate));");
        builder.AppendLine("            if (convertDelegate is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return listener;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine("                return convertDelegate.Invoke(null, new object[] { delegateValue }) as TDelegate ?? listener;");
        builder.AppendLine("            }");
        builder.AppendLine("            catch");
        builder.AppendLine("            {");
        builder.AppendLine("                return listener;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static System.Reflection.MethodInfo? ResolveConvertDelegate(System.Type delegateType)");
        builder.AppendLine("        {");
        builder.AppendLine("            string cacheKey = delegateType.AssemblyQualifiedName ?? delegateType.FullName ?? delegateType.Name;");
        builder.AppendLine("            if (!ConvertDelegateCache.TryGetValue(cacheKey, out System.Reflection.MethodInfo? method))");
        builder.AppendLine("            {");
        builder.AppendLine("                method = FindConvertDelegateMethod(delegateType);");
        builder.AppendLine("                ConvertDelegateCache[cacheKey] = method;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return method;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static System.Reflection.MethodInfo? FindConvertDelegateMethod(System.Type delegateType)");
        builder.AppendLine("        {");
        builder.AppendLine("            System.Type? delegateSupport = ResolveType(\"Il2CppInterop.Runtime.DelegateSupport\");");
        builder.AppendLine("            if (delegateSupport is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            foreach (System.Reflection.MethodInfo method in delegateSupport.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))");
        builder.AppendLine("            {");
        builder.AppendLine("                if (!method.Name.Equals(\"ConvertDelegate\", System.StringComparison.Ordinal) ||");
        builder.AppendLine("                    !method.IsGenericMethodDefinition ||");
        builder.AppendLine("                    method.GetGenericArguments().Length != 1 ||");
        builder.AppendLine("                    method.GetParameters().Length != 1)");
        builder.AppendLine("                {");
        builder.AppendLine("                    continue;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                return method.MakeGenericMethod(delegateType);");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static System.Type? ResolveType(string typeName)");
        builder.AppendLine("        {");
        builder.AppendLine("            System.Type? type = System.Type.GetType(typeName, throwOnError: false);");
        builder.AppendLine("            if (type is not null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return type;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            foreach (System.Reflection.Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())");
        builder.AppendLine("            {");
        builder.AppendLine("                type = assembly.GetType(typeName, throwOnError: false);");
        builder.AppendLine("                if (type is not null)");
        builder.AppendLine("                {");
        builder.AppendLine("                    return type;");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
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
                builder.AppendLine($"        public static System.Reflection.MethodInfo? {member.Alias}Method => ResolveMethod(S1InteropTypeRegistry.{member.OwnerAlias}Name, {member.Alias}Name, {GenerateParameterTypeNamesExpression(runtime, entries, member)});");
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
                string memberKind = $"S1InteropMemberKind.{member.Kind}";
                if (member.Kind is S1InteropMemberKind.Field or S1InteropMemberKind.FieldOrProperty)
                {
                    builder.AppendLine($"        public static System.Reflection.FieldInfo? {member.Alias}FieldInfo => ResolveMember(S1InteropTypeRegistry.{member.OwnerAlias}Name, {member.Alias}Name, parameterTypeNames: null, S1InteropMemberKind.Field) as System.Reflection.FieldInfo;");
                }

                if (member.Kind is S1InteropMemberKind.Property or S1InteropMemberKind.FieldOrProperty)
                {
                    builder.AppendLine($"        public static System.Reflection.PropertyInfo? {member.Alias}PropertyInfo => ResolveMember(S1InteropTypeRegistry.{member.OwnerAlias}Name, {member.Alias}Name, parameterTypeNames: null, S1InteropMemberKind.Property) as System.Reflection.PropertyInfo;");
                }

                if (member.IsStatic)
                {
                    builder.AppendLine($"        public static object? Get{member.Alias}() => GetValue(S1InteropTypeRegistry.{member.OwnerAlias}Name, {member.Alias}Name, null, {memberKind});");
                    builder.AppendLine($"        public static T? Get{member.Alias}<T>() where T : class => Get{member.Alias}() as T;");
                    builder.AppendLine($"        public static T? Get{member.Alias}Value<T>() where T : struct => Get{member.Alias}() is T value ? value : (T?)null;");
                    builder.AppendLine($"        public static bool TrySet{member.Alias}(object? value) => TrySetValue(S1InteropTypeRegistry.{member.OwnerAlias}Name, {member.Alias}Name, null, value, {memberKind});");
                }
                else
                {
                    builder.AppendLine($"        public static object? Get{member.Alias}(object instance) => GetValue(S1InteropTypeRegistry.{member.OwnerAlias}Name, {member.Alias}Name, instance, {memberKind});");
                    builder.AppendLine($"        public static T? Get{member.Alias}<T>(object instance) where T : class => Get{member.Alias}(instance) as T;");
                    builder.AppendLine($"        public static T? Get{member.Alias}Value<T>(object instance) where T : struct => Get{member.Alias}(instance) is T value ? value : (T?)null;");
                    builder.AppendLine($"        public static bool TrySet{member.Alias}(object instance, object? value) => TrySetValue(S1InteropTypeRegistry.{member.OwnerAlias}Name, {member.Alias}Name, instance, value, {memberKind});");
                }
            }

            builder.AppendLine();
        }

        builder.AppendLine("        public static object? GetValue(string ownerTypeName, string memberName, object? instance)");
        builder.AppendLine("        {");
        builder.AppendLine("            return GetValue(ownerTypeName, memberName, instance, S1InteropMemberKind.FieldOrProperty);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static object? GetInstanceValue(object? instance, string memberName)");
        builder.AppendLine("        {");
        builder.AppendLine("            return GetInstanceValue(instance, memberName, S1InteropMemberKind.FieldOrProperty);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static object? GetInstanceValue(object? instance, string memberName, S1InteropMemberKind kind)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (instance is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Reflection.MemberInfo? member = ResolveMemberCached(instance.GetType(), memberName, parameterTypeNames: null, kind);");
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
        builder.AppendLine("        public static object? GetValue(string ownerTypeName, string memberName, object? instance, S1InteropMemberKind kind)");
        builder.AppendLine("        {");
        builder.AppendLine("            System.Reflection.MemberInfo? member = ResolveMember(ownerTypeName, memberName, parameterTypeNames: null, kind);");
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
        builder.AppendLine("            return TrySetValue(ownerTypeName, memberName, instance, value, S1InteropMemberKind.FieldOrProperty);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static bool TrySetInstanceValue(object? instance, string memberName, object? value)");
        builder.AppendLine("        {");
        builder.AppendLine("            return TrySetInstanceValue(instance, memberName, value, S1InteropMemberKind.FieldOrProperty);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static bool TrySetInstanceValue(object? instance, string memberName, object? value, S1InteropMemberKind kind)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (instance is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return TrySetValue(ResolveMemberCached(instance.GetType(), memberName, parameterTypeNames: null, kind), instance, value);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static bool TrySetValue(string ownerTypeName, string memberName, object? instance, object? value, S1InteropMemberKind kind)");
        builder.AppendLine("        {");
        builder.AppendLine("            System.Reflection.MemberInfo? member = ResolveMember(ownerTypeName, memberName, parameterTypeNames: null, kind);");
        builder.AppendLine("            return TrySetValue(member, instance, value);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool TrySetValue(System.Reflection.MemberInfo? member, object? instance, object? value)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (member is System.Reflection.PropertyInfo property && property.CanWrite)");
        builder.AppendLine("            {");
        builder.AppendLine("                if (!TryConvertValue(value, property.PropertyType, out object? converted))");
        builder.AppendLine("                {");
        builder.AppendLine("                    return false;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                property.SetValue(instance, converted, null);");
        builder.AppendLine("                return true;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (member is System.Reflection.FieldInfo field)");
        builder.AppendLine("            {");
        builder.AppendLine("                if (!TryConvertValue(value, field.FieldType, out object? converted))");
        builder.AppendLine("                {");
        builder.AppendLine("                    return false;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                field.SetValue(instance, converted);");
        builder.AppendLine("                return true;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static bool TryConvertValue(object? value, System.Type targetType, out object? converted)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (value is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                converted = targetType.IsValueType && System.Nullable.GetUnderlyingType(targetType) is null ? System.Activator.CreateInstance(targetType) : null;");
        builder.AppendLine("                return true;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Type conversionType = System.Nullable.GetUnderlyingType(targetType) ?? targetType;");
        builder.AppendLine("            if (conversionType.IsInstanceOfType(value))");
        builder.AppendLine("            {");
        builder.AppendLine("                converted = value;");
        builder.AppendLine("                return true;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine("                if (TryConvertIl2CppGuid(value, conversionType, out converted))");
        builder.AppendLine("                {");
        builder.AppendLine("                    return true;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                if (TryConvertIl2CppList(value, conversionType, out converted))");
        builder.AppendLine("                {");
        builder.AppendLine("                    return true;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                if (TryConvertIl2CppHashSet(value, conversionType, out converted))");
        builder.AppendLine("                {");
        builder.AppendLine("                    return true;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                if (TryConvertIl2CppDictionary(value, conversionType, out converted))");
        builder.AppendLine("                {");
        builder.AppendLine("                    return true;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                if (TryConvertIl2CppArray(value, conversionType, out converted))");
        builder.AppendLine("                {");
        builder.AppendLine("                    return true;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                if (conversionType.IsEnum)");
        builder.AppendLine("                {");
        builder.AppendLine("                    converted = value is string text ? System.Enum.Parse(conversionType, text, ignoreCase: true) : System.Enum.ToObject(conversionType, value);");
        builder.AppendLine("                    return true;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                converted = System.Convert.ChangeType(value, conversionType, System.Globalization.CultureInfo.InvariantCulture);");
        builder.AppendLine("                return true;");
        builder.AppendLine("            }");
        builder.AppendLine("            catch");
        builder.AppendLine("            {");
        builder.AppendLine("                converted = null;");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool TryConvertIl2CppGuid(object value, System.Type targetType, out object? converted)");
        builder.AppendLine("        {");
        builder.AppendLine("            converted = null;");
        builder.AppendLine("            if (!string.Equals(targetType.FullName, \"Il2CppSystem.Guid\", System.StringComparison.Ordinal) || value is not System.Guid guid)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine("                System.Reflection.ConstructorInfo? stringConstructor = targetType.GetConstructor(new[] { typeof(string) });");
        builder.AppendLine("                if (stringConstructor is not null)");
        builder.AppendLine("                {");
        builder.AppendLine("                    converted = stringConstructor.Invoke(new object[] { guid.ToString() });");
        builder.AppendLine("                    return true;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                System.Reflection.ConstructorInfo? byteArrayConstructor = targetType.GetConstructor(new[] { typeof(byte[]) });");
        builder.AppendLine("                if (byteArrayConstructor is not null)");
        builder.AppendLine("                {");
        builder.AppendLine("                    converted = byteArrayConstructor.Invoke(new object[] { guid.ToByteArray() });");
        builder.AppendLine("                    return true;");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine("            catch");
        builder.AppendLine("            {");
        builder.AppendLine("                converted = null;");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool TryConvertIl2CppList(object value, System.Type targetType, out object? converted)");
        builder.AppendLine("        {");
        builder.AppendLine("            converted = null;");
        builder.AppendLine("            if (targetType.FullName is null || !targetType.FullName.StartsWith(\"Il2CppSystem.Collections.Generic.List`1\", System.StringComparison.Ordinal))");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (value is string || value is not System.Collections.IEnumerable enumerable)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Type[] genericArguments = targetType.GetGenericArguments();");
        builder.AppendLine("            if (genericArguments.Length != 1)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Reflection.MethodInfo? addMethod = targetType.GetMethod(\"Add\", new[] { genericArguments[0] });");
        builder.AppendLine("            if (addMethod is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine("                object? list = System.Activator.CreateInstance(targetType);");
        builder.AppendLine("                if (list is null)");
        builder.AppendLine("                {");
        builder.AppendLine("                    return false;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                foreach (object? item in enumerable)");
        builder.AppendLine("                {");
        builder.AppendLine("                    if (!TryConvertValue(item, genericArguments[0], out object? convertedItem))");
        builder.AppendLine("                    {");
        builder.AppendLine("                        return false;");
        builder.AppendLine("                    }");
        builder.AppendLine();
        builder.AppendLine("                    addMethod.Invoke(list, new object?[] { convertedItem });");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                converted = list;");
        builder.AppendLine("                return true;");
        builder.AppendLine("            }");
        builder.AppendLine("            catch");
        builder.AppendLine("            {");
        builder.AppendLine("                converted = null;");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool TryConvertIl2CppHashSet(object value, System.Type targetType, out object? converted)");
        builder.AppendLine("        {");
        builder.AppendLine("            converted = null;");
        builder.AppendLine("            if (targetType.FullName is null || !targetType.FullName.StartsWith(\"Il2CppSystem.Collections.Generic.HashSet`1\", System.StringComparison.Ordinal))");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (value is string || value is not System.Collections.IEnumerable enumerable)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Type[] genericArguments = targetType.GetGenericArguments();");
        builder.AppendLine("            if (genericArguments.Length != 1)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Reflection.MethodInfo? addMethod = targetType.GetMethod(\"Add\", new[] { genericArguments[0] });");
        builder.AppendLine("            if (addMethod is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine("                object? set = System.Activator.CreateInstance(targetType);");
        builder.AppendLine("                if (set is null)");
        builder.AppendLine("                {");
        builder.AppendLine("                    return false;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                foreach (object? item in enumerable)");
        builder.AppendLine("                {");
        builder.AppendLine("                    if (!TryConvertValue(item, genericArguments[0], out object? convertedItem))");
        builder.AppendLine("                    {");
        builder.AppendLine("                        return false;");
        builder.AppendLine("                    }");
        builder.AppendLine();
        builder.AppendLine("                    addMethod.Invoke(set, new object?[] { convertedItem });");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                converted = set;");
        builder.AppendLine("                return true;");
        builder.AppendLine("            }");
        builder.AppendLine("            catch");
        builder.AppendLine("            {");
        builder.AppendLine("                converted = null;");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool TryConvertIl2CppDictionary(object value, System.Type targetType, out object? converted)");
        builder.AppendLine("        {");
        builder.AppendLine("            converted = null;");
        builder.AppendLine("            if (targetType.FullName is null || !targetType.FullName.StartsWith(\"Il2CppSystem.Collections.Generic.Dictionary`2\", System.StringComparison.Ordinal))");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (value is not System.Collections.IEnumerable enumerable)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Type[] genericArguments = targetType.GetGenericArguments();");
        builder.AppendLine("            if (genericArguments.Length != 2)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Reflection.MethodInfo? addMethod = targetType.GetMethod(\"Add\", new[] { genericArguments[0], genericArguments[1] });");
        builder.AppendLine("            if (addMethod is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine("                object? il2CppDictionary = System.Activator.CreateInstance(targetType);");
        builder.AppendLine("                if (il2CppDictionary is null)");
        builder.AppendLine("                {");
        builder.AppendLine("                    return false;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                foreach (object? entry in enumerable)");
        builder.AppendLine("                {");
        builder.AppendLine("                    if (!TryGetDictionaryEntry(entry, out object? key, out object? entryValue) ||");
        builder.AppendLine("                        !TryConvertValue(key, genericArguments[0], out object? convertedKey) ||");
        builder.AppendLine("                        !TryConvertValue(entryValue, genericArguments[1], out object? convertedValue))");
        builder.AppendLine("                    {");
        builder.AppendLine("                        return false;");
        builder.AppendLine("                    }");
        builder.AppendLine();
        builder.AppendLine("                    addMethod.Invoke(il2CppDictionary, new object?[] { convertedKey, convertedValue });");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                converted = il2CppDictionary;");
        builder.AppendLine("                return true;");
        builder.AppendLine("            }");
        builder.AppendLine("            catch");
        builder.AppendLine("            {");
        builder.AppendLine("                converted = null;");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool TryGetDictionaryEntry(object? entry, out object? key, out object? value)");
        builder.AppendLine("        {");
        builder.AppendLine("            key = null;");
        builder.AppendLine("            value = null;");
        builder.AppendLine("            if (entry is System.Collections.DictionaryEntry dictionaryEntry)");
        builder.AppendLine("            {");
        builder.AppendLine("                key = dictionaryEntry.Key;");
        builder.AppendLine("                value = dictionaryEntry.Value;");
        builder.AppendLine("                return true;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (entry is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Type entryType = entry.GetType();");
        builder.AppendLine("            System.Reflection.PropertyInfo? keyProperty = entryType.GetProperty(\"Key\");");
        builder.AppendLine("            System.Reflection.PropertyInfo? valueProperty = entryType.GetProperty(\"Value\");");
        builder.AppendLine("            if (keyProperty is null || valueProperty is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            key = keyProperty.GetValue(entry, null);");
        builder.AppendLine("            value = valueProperty.GetValue(entry, null);");
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool TryConvertIl2CppArray(object value, System.Type targetType, out object? converted)");
        builder.AppendLine("        {");
        builder.AppendLine("            converted = null;");
        builder.AppendLine("            if (targetType.FullName is null ||");
        builder.AppendLine("                (!targetType.FullName.StartsWith(\"Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1\", System.StringComparison.Ordinal) &&");
        builder.AppendLine("                 !targetType.FullName.StartsWith(\"Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1\", System.StringComparison.Ordinal)))");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (value is string || value is not System.Collections.IEnumerable enumerable)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Type[] genericArguments = targetType.GetGenericArguments();");
        builder.AppendLine("            if (genericArguments.Length != 1)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine("                System.Collections.Generic.List<object?> items = new System.Collections.Generic.List<object?>();");
        builder.AppendLine("                foreach (object? item in enumerable)");
        builder.AppendLine("                {");
        builder.AppendLine("                    if (!TryConvertValue(item, genericArguments[0], out object? convertedItem))");
        builder.AppendLine("                    {");
        builder.AppendLine("                        return false;");
        builder.AppendLine("                    }");
        builder.AppendLine();
        builder.AppendLine("                    items.Add(convertedItem);");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                object? array = CreateIl2CppArray(targetType, items.Count);");
        builder.AppendLine("                if (array is null)");
        builder.AppendLine("                {");
        builder.AppendLine("                    return false;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                System.Reflection.PropertyInfo? indexer = targetType.GetProperty(\"Item\", new[] { typeof(int) });");
        builder.AppendLine("                System.Reflection.MethodInfo? setItem = targetType.GetMethod(\"set_Item\", new[] { typeof(int), genericArguments[0] });");
        builder.AppendLine("                if (indexer is null && setItem is null)");
        builder.AppendLine("                {");
        builder.AppendLine("                    return false;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                for (int index = 0; index < items.Count; index++)");
        builder.AppendLine("                {");
        builder.AppendLine("                    if (indexer is not null)");
        builder.AppendLine("                    {");
        builder.AppendLine("                        indexer.SetValue(array, items[index], new object[] { index });");
        builder.AppendLine("                    }");
        builder.AppendLine("                    else");
        builder.AppendLine("                    {");
        builder.AppendLine("                        setItem!.Invoke(array, new object?[] { index, items[index] });");
        builder.AppendLine("                    }");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                converted = array;");
        builder.AppendLine("                return true;");
        builder.AppendLine("            }");
        builder.AppendLine("            catch");
        builder.AppendLine("            {");
        builder.AppendLine("                converted = null;");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static object? CreateIl2CppArray(System.Type targetType, int count)");
        builder.AppendLine("        {");
        builder.AppendLine("            System.Reflection.ConstructorInfo? intConstructor = targetType.GetConstructor(new[] { typeof(int) });");
        builder.AppendLine("            if (intConstructor is not null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return intConstructor.Invoke(new object[] { count });");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Reflection.ConstructorInfo? longConstructor = targetType.GetConstructor(new[] { typeof(long) });");
        builder.AppendLine("            return longConstructor?.Invoke(new object[] { (long)count });");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static object? Invoke(string ownerTypeName, string memberName, string[]? parameterTypeNames, object? instance, params object?[] args)");
        builder.AppendLine("        {");
        builder.AppendLine("            return Invoke(ResolveMethod(ownerTypeName, memberName, parameterTypeNames), instance, args);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static object? InvokeInstance(object? instance, string memberName, params object?[] args)");
        builder.AppendLine("        {");
        builder.AppendLine("            return InvokeInstance(instance, memberName, null, args);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static object? InvokeInstance(object? instance, string memberName, string[]? parameterTypeNames, params object?[] args)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (instance is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return Invoke(ResolveMemberCached(instance.GetType(), memberName, parameterTypeNames, S1InteropMemberKind.Method) as System.Reflection.MethodInfo, instance, args);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static object? Invoke(System.Reflection.MethodInfo? method, object? instance, params object?[] args)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (method is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (!TryConvertArguments(method.GetParameters(), args, out object?[] converted))");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            object? result = method.Invoke(instance, converted);");
        builder.AppendLine("            CopyByRefArguments(method.GetParameters(), converted, args);");
        builder.AppendLine("            return result;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool TryConvertArguments(System.Reflection.ParameterInfo[] parameters, object?[] args, out object?[] converted)");
        builder.AppendLine("        {");
        builder.AppendLine("            converted = System.Array.Empty<object?>();");
        builder.AppendLine("            if (parameters.Length != args.Length)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            converted = new object?[args.Length];");
        builder.AppendLine("            for (int index = 0; index < args.Length; index++)");
        builder.AppendLine("            {");
        builder.AppendLine("                System.Type parameterType = parameters[index].ParameterType;");
        builder.AppendLine("                System.Type conversionType = parameterType.IsByRef && parameterType.GetElementType() is System.Type elementType");
        builder.AppendLine("                    ? elementType");
        builder.AppendLine("                    : parameterType;");
        builder.AppendLine("                if (!TryConvertValue(args[index], conversionType, out object? convertedValue))");
        builder.AppendLine("                {");
        builder.AppendLine("                    return false;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                converted[index] = convertedValue;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static void CopyByRefArguments(System.Reflection.ParameterInfo[] parameters, object?[] converted, object?[] args)");
        builder.AppendLine("        {");
        builder.AppendLine("            int count = System.Math.Min(System.Math.Min(parameters.Length, converted.Length), args.Length);");
        builder.AppendLine("            for (int index = 0; index < count; index++)");
        builder.AppendLine("            {");
        builder.AppendLine("                if (parameters[index].ParameterType.IsByRef)");
        builder.AppendLine("                {");
        builder.AppendLine("                    args[index] = ConvertBackValue(args[index], converted[index]);");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static object? ConvertBackValue(object? original, object? converted)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (converted is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (original is System.Guid && TryConvertBackGuid(converted, out System.Guid guid))");
        builder.AppendLine("            {");
        builder.AppendLine("                return guid;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (original is System.Array originalArray && TryConvertBackArray(originalArray, converted, out System.Array? managedArray))");
        builder.AppendLine("            {");
        builder.AppendLine("                return managedArray;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (original is System.Collections.IList originalList && TryConvertBackList(originalList, converted, out System.Collections.IList? managedList))");
        builder.AppendLine("            {");
        builder.AppendLine("                return managedList;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return converted;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool TryConvertBackGuid(object converted, out System.Guid guid)");
        builder.AppendLine("        {");
        builder.AppendLine("            guid = default;");
        builder.AppendLine("            if (!string.Equals(converted.GetType().FullName, \"Il2CppSystem.Guid\", System.StringComparison.Ordinal))");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            object? value = converted.GetType().GetProperty(\"Value\")?.GetValue(converted, null);");
        builder.AppendLine("            string? text = value as string ?? converted.ToString();");
        builder.AppendLine("            return text is not null && System.Guid.TryParse(text, out guid);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool TryConvertBackArray(System.Array original, object converted, out System.Array? managedArray)");
        builder.AppendLine("        {");
        builder.AppendLine("            managedArray = null;");
        builder.AppendLine("            System.Type convertedType = converted.GetType();");
        builder.AppendLine("            if (convertedType.FullName is null ||");
        builder.AppendLine("                (!convertedType.FullName.StartsWith(\"Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1\", System.StringComparison.Ordinal) &&");
        builder.AppendLine("                 !convertedType.FullName.StartsWith(\"Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1\", System.StringComparison.Ordinal)))");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Reflection.PropertyInfo? lengthProperty = convertedType.GetProperty(\"Length\");");
        builder.AppendLine("            System.Reflection.PropertyInfo? indexer = convertedType.GetProperty(\"Item\", new[] { typeof(int) });");
        builder.AppendLine("            if (lengthProperty?.GetValue(converted, null) is not int length || indexer is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Type elementType = original.GetType().GetElementType() ?? typeof(object);");
        builder.AppendLine("            System.Array result = System.Array.CreateInstance(elementType, length);");
        builder.AppendLine("            for (int index = 0; index < length; index++)");
        builder.AppendLine("            {");
        builder.AppendLine("                object? item = indexer.GetValue(converted, new object[] { index });");
        builder.AppendLine("                if (!TryConvertValue(item, elementType, out object? managedItem))");
        builder.AppendLine("                {");
        builder.AppendLine("                    return false;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                result.SetValue(managedItem, index);");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            managedArray = result;");
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool TryConvertBackList(System.Collections.IList original, object converted, out System.Collections.IList? managedList)");
        builder.AppendLine("        {");
        builder.AppendLine("            managedList = null;");
        builder.AppendLine("            System.Type convertedType = converted.GetType();");
        builder.AppendLine("            if (convertedType.FullName is null || !convertedType.FullName.StartsWith(\"Il2CppSystem.Collections.Generic.List`1\", System.StringComparison.Ordinal))");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Reflection.PropertyInfo? countProperty = convertedType.GetProperty(\"Count\");");
        builder.AppendLine("            System.Reflection.PropertyInfo? indexer = convertedType.GetProperty(\"Item\", new[] { typeof(int) });");
        builder.AppendLine("            if (countProperty?.GetValue(converted, null) is not int count || indexer is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Collections.IList? result = CreateManagedList(original);");
        builder.AppendLine("            if (result is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Type elementType = GetListElementType(result.GetType()) ?? typeof(object);");
        builder.AppendLine("            for (int index = 0; index < count; index++)");
        builder.AppendLine("            {");
        builder.AppendLine("                object? item = indexer.GetValue(converted, new object[] { index });");
        builder.AppendLine("                if (!TryConvertValue(item, elementType, out object? managedItem))");
        builder.AppendLine("                {");
        builder.AppendLine("                    return false;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                result.Add(managedItem);");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            managedList = result;");
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static System.Collections.IList? CreateManagedList(System.Collections.IList original)");
        builder.AppendLine("        {");
        builder.AppendLine("            System.Type originalType = original.GetType();");
        builder.AppendLine("            if (!original.IsFixedSize && !original.IsReadOnly)");
        builder.AppendLine("            {");
        builder.AppendLine("                System.Collections.IList? recreated = System.Activator.CreateInstance(originalType) as System.Collections.IList;");
        builder.AppendLine("                if (recreated is not null)");
        builder.AppendLine("                {");
        builder.AppendLine("                    return recreated;");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Type elementType = GetListElementType(originalType) ?? typeof(object);");
        builder.AppendLine("            System.Type listType = typeof(System.Collections.Generic.List<>).MakeGenericType(elementType);");
        builder.AppendLine("            return System.Activator.CreateInstance(listType) as System.Collections.IList;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static System.Type? GetListElementType(System.Type listType)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (listType.IsGenericType)");
        builder.AppendLine("            {");
        builder.AppendLine("                System.Type[] arguments = listType.GetGenericArguments();");
        builder.AppendLine("                if (arguments.Length == 1)");
        builder.AppendLine("                {");
        builder.AppendLine("                    return arguments[0];");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            foreach (System.Type interfaceType in listType.GetInterfaces())");
        builder.AppendLine("            {");
        builder.AppendLine("                if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IList<>))");
        builder.AppendLine("                {");
        builder.AppendLine("                    return interfaceType.GetGenericArguments()[0];");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static System.Reflection.MethodInfo? ResolveMethod(string ownerTypeName, string memberName, string[]? parameterTypeNames)");
        builder.AppendLine("        {");
        builder.AppendLine("            return ResolveMember(ownerTypeName, memberName, parameterTypeNames, S1InteropMemberKind.Method) as System.Reflection.MethodInfo;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static System.Reflection.MemberInfo? ResolveMember(string ownerTypeName, string memberName, string[]? parameterTypeNames, S1InteropMemberKind kind)");
        builder.AppendLine("        {");
        builder.AppendLine("            string cacheKey = ownerTypeName + \"::\" + memberName + \"::\" + ((int)kind).ToString() + \"::\" + (parameterTypeNames is null ? string.Empty : string.Join(\"|\", parameterTypeNames));");
        builder.AppendLine("            if (!Cache.TryGetValue(cacheKey, out System.Reflection.MemberInfo? member))");
        builder.AppendLine("            {");
        builder.AppendLine("                System.Type? ownerType = S1InteropTypeRegistry.Resolve(ownerTypeName);");
        builder.AppendLine("                member = ownerType is null");
        builder.AppendLine("                    ? null");
        builder.AppendLine("                    : ResolveMember(ownerType, memberName, parameterTypeNames, kind);");
        builder.AppendLine("                Cache[cacheKey] = member;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return member;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static System.Reflection.MemberInfo? ResolveMemberCached(System.Type ownerType, string memberName, string[]? parameterTypeNames, S1InteropMemberKind kind)");
        builder.AppendLine("        {");
        builder.AppendLine("            string ownerKey = ownerType.AssemblyQualifiedName ?? ownerType.FullName ?? ownerType.Name;");
        builder.AppendLine("            string cacheKey = ownerKey + \"::\" + memberName + \"::\" + ((int)kind).ToString() + \"::\" + (parameterTypeNames is null ? string.Empty : string.Join(\"|\", parameterTypeNames));");
        builder.AppendLine("            if (!Cache.TryGetValue(cacheKey, out System.Reflection.MemberInfo? member))");
        builder.AppendLine("            {");
        builder.AppendLine("                member = ResolveMember(ownerType, memberName, parameterTypeNames, kind);");
        builder.AppendLine("                Cache[cacheKey] = member;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return member;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static System.Reflection.MemberInfo? ResolveMember(System.Type ownerType, string memberName, string[]? parameterTypeNames, S1InteropMemberKind kind)");
        builder.AppendLine("        {");
        builder.AppendLine("            switch (kind)");
        builder.AppendLine("            {");
        builder.AppendLine("                case S1InteropMemberKind.Method:");
        builder.AppendLine("                    return ResolveMethod(ownerType, memberName, parameterTypeNames);");
        builder.AppendLine("                case S1InteropMemberKind.Field:");
        builder.AppendLine("                    return ownerType.GetField(memberName, AllBindings);");
        builder.AppendLine("                case S1InteropMemberKind.Property:");
        builder.AppendLine("                    return ownerType.GetProperty(memberName, AllBindings);");
        builder.AppendLine("                default:");
        builder.AppendLine("                    return ownerType.GetProperty(memberName, AllBindings) ?? (System.Reflection.MemberInfo?)ownerType.GetField(memberName, AllBindings);");
        builder.AppendLine("            }");
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
        builder.AppendLine("            if (TryResolveArrayType(typeName, out System.Type? arrayType))");
        builder.AppendLine("            {");
        builder.AppendLine("                return arrayType;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (TryResolveGenericListType(typeName, out System.Type? genericListType))");
        builder.AppendLine("            {");
        builder.AppendLine("                return genericListType;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (TryResolveGenericHashSetType(typeName, out System.Type? genericHashSetType))");
        builder.AppendLine("            {");
        builder.AppendLine("                return genericHashSetType;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (TryResolveGenericDictionaryType(typeName, out System.Type? genericDictionaryType))");
        builder.AppendLine("            {");
        builder.AppendLine("                return genericDictionaryType;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            switch (typeName)");
        builder.AppendLine("            {");
        builder.AppendLine("                case \"System.Guid\": return typeof(System.Guid);");
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
        builder.AppendLine();
        builder.AppendLine("        private static bool TryResolveArrayType(string typeName, out System.Type? resolvedType)");
        builder.AppendLine("        {");
        builder.AppendLine("            resolvedType = null;");
        builder.AppendLine("            const string structArrayPrefix = \"Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<\";");
        builder.AppendLine("            const string referenceArrayPrefix = \"Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<\";");
        builder.AppendLine("            if (typeName.EndsWith(\"[]\", System.StringComparison.Ordinal))");
        builder.AppendLine("            {");
        builder.AppendLine("                System.Type? elementType = ResolveKnownType(typeName.Substring(0, typeName.Length - 2));");
        builder.AppendLine("                if (elementType is null)");
        builder.AppendLine("                {");
        builder.AppendLine("                    return false;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                resolvedType = elementType.MakeArrayType();");
        builder.AppendLine("                return true;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            string genericDefinitionName;");
        builder.AppendLine("            string elementTypeName;");
        builder.AppendLine("            if (typeName.StartsWith(structArrayPrefix, System.StringComparison.Ordinal) && typeName.EndsWith(\">\", System.StringComparison.Ordinal))");
        builder.AppendLine("            {");
        builder.AppendLine("                genericDefinitionName = \"Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1\";");
        builder.AppendLine("                elementTypeName = typeName.Substring(structArrayPrefix.Length, typeName.Length - structArrayPrefix.Length - 1);");
        builder.AppendLine("            }");
        builder.AppendLine("            else if (typeName.StartsWith(referenceArrayPrefix, System.StringComparison.Ordinal) && typeName.EndsWith(\">\", System.StringComparison.Ordinal))");
        builder.AppendLine("            {");
        builder.AppendLine("                genericDefinitionName = \"Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1\";");
        builder.AppendLine("                elementTypeName = typeName.Substring(referenceArrayPrefix.Length, typeName.Length - referenceArrayPrefix.Length - 1);");
        builder.AppendLine("            }");
        builder.AppendLine("            else");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Type? genericDefinition = S1InteropTypeRegistry.Resolve(genericDefinitionName);");
        builder.AppendLine("            System.Type? genericElementType = ResolveKnownType(elementTypeName);");
        builder.AppendLine("            if (genericDefinition is null || genericElementType is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            resolvedType = genericDefinition.MakeGenericType(genericElementType);");
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool TryResolveGenericHashSetType(string typeName, out System.Type? resolvedType)");
        builder.AppendLine("        {");
        builder.AppendLine("            resolvedType = null;");
        builder.AppendLine("            const string monoPrefix = \"System.Collections.Generic.HashSet<\";");
        builder.AppendLine("            const string il2CppPrefix = \"Il2CppSystem.Collections.Generic.HashSet<\";");
        builder.AppendLine("            string genericDefinitionName;");
        builder.AppendLine("            string elementTypeName;");
        builder.AppendLine("            if (typeName.StartsWith(monoPrefix, System.StringComparison.Ordinal) && typeName.EndsWith(\">\", System.StringComparison.Ordinal))");
        builder.AppendLine("            {");
        builder.AppendLine("                genericDefinitionName = \"System.Collections.Generic.HashSet`1\";");
        builder.AppendLine("                elementTypeName = typeName.Substring(monoPrefix.Length, typeName.Length - monoPrefix.Length - 1);");
        builder.AppendLine("            }");
        builder.AppendLine("            else if (typeName.StartsWith(il2CppPrefix, System.StringComparison.Ordinal) && typeName.EndsWith(\">\", System.StringComparison.Ordinal))");
        builder.AppendLine("            {");
        builder.AppendLine("                genericDefinitionName = \"Il2CppSystem.Collections.Generic.HashSet`1\";");
        builder.AppendLine("                elementTypeName = typeName.Substring(il2CppPrefix.Length, typeName.Length - il2CppPrefix.Length - 1);");
        builder.AppendLine("            }");
        builder.AppendLine("            else");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Type? genericDefinition = S1InteropTypeRegistry.Resolve(genericDefinitionName);");
        builder.AppendLine("            System.Type? elementType = ResolveKnownType(elementTypeName);");
        builder.AppendLine("            if (genericDefinition is null || elementType is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            resolvedType = genericDefinition.MakeGenericType(elementType);");
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool TryResolveGenericDictionaryType(string typeName, out System.Type? resolvedType)");
        builder.AppendLine("        {");
        builder.AppendLine("            resolvedType = null;");
        builder.AppendLine("            const string monoPrefix = \"System.Collections.Generic.Dictionary<\";");
        builder.AppendLine("            const string il2CppPrefix = \"Il2CppSystem.Collections.Generic.Dictionary<\";");
        builder.AppendLine("            string genericDefinitionName;");
        builder.AppendLine("            string argumentsText;");
        builder.AppendLine("            if (typeName.StartsWith(monoPrefix, System.StringComparison.Ordinal) && typeName.EndsWith(\">\", System.StringComparison.Ordinal))");
        builder.AppendLine("            {");
        builder.AppendLine("                genericDefinitionName = \"System.Collections.Generic.Dictionary`2\";");
        builder.AppendLine("                argumentsText = typeName.Substring(monoPrefix.Length, typeName.Length - monoPrefix.Length - 1);");
        builder.AppendLine("            }");
        builder.AppendLine("            else if (typeName.StartsWith(il2CppPrefix, System.StringComparison.Ordinal) && typeName.EndsWith(\">\", System.StringComparison.Ordinal))");
        builder.AppendLine("            {");
        builder.AppendLine("                genericDefinitionName = \"Il2CppSystem.Collections.Generic.Dictionary`2\";");
        builder.AppendLine("                argumentsText = typeName.Substring(il2CppPrefix.Length, typeName.Length - il2CppPrefix.Length - 1);");
        builder.AppendLine("            }");
        builder.AppendLine("            else");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            string[] genericArguments = SplitGenericArguments(argumentsText);");
        builder.AppendLine("            if (genericArguments.Length != 2)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Type? genericDefinition = S1InteropTypeRegistry.Resolve(genericDefinitionName);");
        builder.AppendLine("            System.Type? keyType = ResolveKnownType(genericArguments[0]);");
        builder.AppendLine("            System.Type? valueType = ResolveKnownType(genericArguments[1]);");
        builder.AppendLine("            if (genericDefinition is null || keyType is null || valueType is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            resolvedType = genericDefinition.MakeGenericType(keyType, valueType);");
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static string[] SplitGenericArguments(string argumentsText)");
        builder.AppendLine("        {");
        builder.AppendLine("            System.Collections.Generic.List<string> arguments = new System.Collections.Generic.List<string>();");
        builder.AppendLine("            int depth = 0;");
        builder.AppendLine("            int start = 0;");
        builder.AppendLine("            for (int index = 0; index < argumentsText.Length; index++)");
        builder.AppendLine("            {");
        builder.AppendLine("                char character = argumentsText[index];");
        builder.AppendLine("                if (character == '<')");
        builder.AppendLine("                {");
        builder.AppendLine("                    depth++;");
        builder.AppendLine("                }");
        builder.AppendLine("                else if (character == '>')");
        builder.AppendLine("                {");
        builder.AppendLine("                    depth--;");
        builder.AppendLine("                }");
        builder.AppendLine("                else if (character == ',' && depth == 0)");
        builder.AppendLine("                {");
        builder.AppendLine("                    arguments.Add(argumentsText.Substring(start, index - start).Trim());");
        builder.AppendLine("                    start = index + 1;");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            arguments.Add(argumentsText.Substring(start).Trim());");
        builder.AppendLine("            return arguments.ToArray();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool TryResolveGenericListType(string typeName, out System.Type? resolvedType)");
        builder.AppendLine("        {");
        builder.AppendLine("            resolvedType = null;");
        builder.AppendLine("            const string monoPrefix = \"System.Collections.Generic.List<\";");
        builder.AppendLine("            const string il2CppPrefix = \"Il2CppSystem.Collections.Generic.List<\";");
        builder.AppendLine("            string genericDefinitionName;");
        builder.AppendLine("            string elementTypeName;");
        builder.AppendLine("            if (typeName.StartsWith(monoPrefix, System.StringComparison.Ordinal) && typeName.EndsWith(\">\", System.StringComparison.Ordinal))");
        builder.AppendLine("            {");
        builder.AppendLine("                genericDefinitionName = \"System.Collections.Generic.List`1\";");
        builder.AppendLine("                elementTypeName = typeName.Substring(monoPrefix.Length, typeName.Length - monoPrefix.Length - 1);");
        builder.AppendLine("            }");
        builder.AppendLine("            else if (typeName.StartsWith(il2CppPrefix, System.StringComparison.Ordinal) && typeName.EndsWith(\">\", System.StringComparison.Ordinal))");
        builder.AppendLine("            {");
        builder.AppendLine("                genericDefinitionName = \"Il2CppSystem.Collections.Generic.List`1\";");
        builder.AppendLine("                elementTypeName = typeName.Substring(il2CppPrefix.Length, typeName.Length - il2CppPrefix.Length - 1);");
        builder.AppendLine("            }");
        builder.AppendLine("            else");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Type? genericDefinition = S1InteropTypeRegistry.Resolve(genericDefinitionName);");
        builder.AppendLine("            System.Type? elementType = ResolveKnownType(elementTypeName);");
        builder.AppendLine("            if (genericDefinition is null || elementType is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            resolvedType = genericDefinition.MakeGenericType(elementType);");
        builder.AppendLine("            return true;");
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

        if (runtime == RuntimeBackend.Unknown)
        {
            string monoTypeName = normalized;
            string il2CppTypeName = ToIl2CppTypeName(normalized);
            if (string.Equals(monoTypeName, il2CppTypeName, StringComparison.Ordinal))
            {
                return $"\"{Escape(monoTypeName + (byRef ? "&" : string.Empty))}\"";
            }

            string expression = $"S1InteropTypeRegistry.GetRuntimeTypeName(\"{Escape(monoTypeName)}\", \"{Escape(il2CppTypeName)}\")";
            return byRef ? expression + " + \"&\"" : expression;
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
                Method = 1,
                Field = 2,
                Property = 3
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

            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = false)]
            internal sealed class S1InteropGenerateUnityEventBridgeAttribute : System.Attribute
            {
            }

            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = false)]
            internal sealed class S1InteropGenerateDelegateEventBridgeAttribute : System.Attribute
            {
            }
        }
        """;

    private static string GenerateUnityEventBridgeSource() =>
        """
        // <auto-generated />
        // Generated by S1Interop.Generators. Do not edit by hand.

        namespace S1Interop.Generated
        {
        internal static class S1InteropUnityEventBridge
        {
            private static readonly System.Collections.Generic.Dictionary<object, System.Collections.Generic.Dictionary<System.Delegate, System.Delegate>> WrappedListeners =
                new System.Collections.Generic.Dictionary<object, System.Collections.Generic.Dictionary<System.Delegate, System.Delegate>>();

            public static void Add(UnityEngine.Events.UnityEvent unityEvent, System.Action listener)
            {
                if (object.ReferenceEquals(unityEvent, null) || object.ReferenceEquals(listener, null) || HasWrapper(unityEvent, listener))
                {
                    return;
                }

        #if IL2CPP
                System.Action wrapped = new System.Action(listener);
                unityEvent.AddListener(wrapped);
        #else
                UnityEngine.Events.UnityAction wrapped = new UnityEngine.Events.UnityAction(listener);
                unityEvent.AddListener(wrapped);
        #endif
                StoreWrapper(unityEvent, listener, wrapped);
            }

            public static void Remove(UnityEngine.Events.UnityEvent unityEvent, System.Action listener)
            {
                System.Delegate wrapped;
                if (object.ReferenceEquals(unityEvent, null) || object.ReferenceEquals(listener, null) || !TryRemoveWrapper(unityEvent, listener, out wrapped))
                {
                    return;
                }

        #if IL2CPP
                if (wrapped is System.Action action)
                {
                    unityEvent.RemoveListener(action);
                }
        #else
                if (wrapped is UnityEngine.Events.UnityAction action)
                {
                    unityEvent.RemoveListener(action);
                }
        #endif
            }

            public static void Add<T0>(UnityEngine.Events.UnityEvent<T0> unityEvent, System.Action<T0> listener)
            {
                if (object.ReferenceEquals(unityEvent, null) || object.ReferenceEquals(listener, null) || HasWrapper(unityEvent, listener))
                {
                    return;
                }

        #if IL2CPP
                System.Action<T0> wrapped = new System.Action<T0>(listener);
                unityEvent.AddListener(wrapped);
        #else
                UnityEngine.Events.UnityAction<T0> wrapped = new UnityEngine.Events.UnityAction<T0>(listener);
                unityEvent.AddListener(wrapped);
        #endif
                StoreWrapper(unityEvent, listener, wrapped);
            }

            public static void Remove<T0>(UnityEngine.Events.UnityEvent<T0> unityEvent, System.Action<T0> listener)
            {
                System.Delegate wrapped;
                if (object.ReferenceEquals(unityEvent, null) || object.ReferenceEquals(listener, null) || !TryRemoveWrapper(unityEvent, listener, out wrapped))
                {
                    return;
                }

        #if IL2CPP
                if (wrapped is System.Action<T0> action)
                {
                    unityEvent.RemoveListener(action);
                }
        #else
                if (wrapped is UnityEngine.Events.UnityAction<T0> action)
                {
                    unityEvent.RemoveListener(action);
                }
        #endif
            }

            private static bool HasWrapper(object unityEvent, System.Delegate listener) =>
                WrappedListeners.TryGetValue(unityEvent, out System.Collections.Generic.Dictionary<System.Delegate, System.Delegate> listeners) &&
                listeners.ContainsKey(listener);

            private static void StoreWrapper(object unityEvent, System.Delegate listener, System.Delegate wrapped)
            {
                System.Collections.Generic.Dictionary<System.Delegate, System.Delegate> listeners;
                if (!WrappedListeners.TryGetValue(unityEvent, out listeners))
                {
                    listeners = new System.Collections.Generic.Dictionary<System.Delegate, System.Delegate>();
                    WrappedListeners[unityEvent] = listeners;
                }

                listeners[listener] = wrapped;
            }

            private static bool TryRemoveWrapper(object unityEvent, System.Delegate listener, out System.Delegate wrapped)
            {
                wrapped = null;
                System.Collections.Generic.Dictionary<System.Delegate, System.Delegate> listeners;
                if (!WrappedListeners.TryGetValue(unityEvent, out listeners) ||
                    !listeners.TryGetValue(listener, out wrapped))
                {
                    return false;
                }

                listeners.Remove(listener);
                if (listeners.Count == 0)
                {
                    WrappedListeners.Remove(unityEvent);
                }

                return true;
            }
        }
        }
        """;

    private static string GenerateDelegateEventBridgeSource() =>
        """
        // <auto-generated />
        // Generated by S1Interop.Generators. Do not edit by hand.

        namespace S1Interop.Generated
        {
        internal static class S1InteropDelegateEventBridge
        {
            public static TDelegate Combine<TDelegate>(TDelegate current, System.Delegate listener) where TDelegate : class
            {
                return (TDelegate)(object)System.Delegate.Combine(current as System.Delegate, listener);
            }

            public static TDelegate Remove<TDelegate>(TDelegate current, System.Delegate listener) where TDelegate : class
            {
                return (TDelegate)(object)System.Delegate.Remove(current as System.Delegate, listener);
            }
        }
        }
        """;

    private static string ToIl2CppTypeName(string monoTypeName)
    {
        if (string.Equals(monoTypeName, "System.Guid", StringComparison.Ordinal) ||
            string.Equals(monoTypeName, "Guid", StringComparison.Ordinal))
        {
            return "Il2CppSystem.Guid";
        }

        const string listPrefix = "System.Collections.Generic.List<";
        if (monoTypeName.StartsWith(listPrefix, StringComparison.Ordinal) &&
            monoTypeName.EndsWith(">", StringComparison.Ordinal))
        {
            string elementTypeName = monoTypeName.Substring(listPrefix.Length, monoTypeName.Length - listPrefix.Length - 1);
            return $"Il2CppSystem.Collections.Generic.List<{ToIl2CppTypeName(elementTypeName)}>";
        }

        const string hashSetPrefix = "System.Collections.Generic.HashSet<";
        if (monoTypeName.StartsWith(hashSetPrefix, StringComparison.Ordinal) &&
            monoTypeName.EndsWith(">", StringComparison.Ordinal))
        {
            string elementTypeName = monoTypeName.Substring(hashSetPrefix.Length, monoTypeName.Length - hashSetPrefix.Length - 1);
            return $"Il2CppSystem.Collections.Generic.HashSet<{ToIl2CppTypeName(elementTypeName)}>";
        }

        const string dictionaryPrefix = "System.Collections.Generic.Dictionary<";
        if (monoTypeName.StartsWith(dictionaryPrefix, StringComparison.Ordinal) &&
            monoTypeName.EndsWith(">", StringComparison.Ordinal))
        {
            string argumentsText = monoTypeName.Substring(dictionaryPrefix.Length, monoTypeName.Length - dictionaryPrefix.Length - 1);
            string[] arguments = SplitTopLevelGenericArguments(argumentsText);
            if (arguments.Length == 2)
            {
                return $"Il2CppSystem.Collections.Generic.Dictionary<{ToIl2CppTypeName(arguments[0])}, {ToIl2CppTypeName(arguments[1])}>";
            }
        }

        if (monoTypeName.EndsWith("[]", StringComparison.Ordinal))
        {
            string elementTypeName = monoTypeName.Substring(0, monoTypeName.Length - 2);
            string arrayTypeName = IsKnownValueTypeName(elementTypeName)
                ? "Il2CppStructArray"
                : "Il2CppReferenceArray";
            return $"Il2CppInterop.Runtime.InteropTypes.Arrays.{arrayTypeName}<{ToIl2CppTypeName(elementTypeName)}>";
        }

        if (monoTypeName.StartsWith("ScheduleOne.", StringComparison.Ordinal))
        {
            return "Il2Cpp" + monoTypeName;
        }

        return monoTypeName;
    }

    private static bool IsKnownValueTypeName(string typeName) =>
        typeName is "bool" or "byte" or "char" or "double" or "float" or "int" or "long" or "short" or "uint" or "ulong" or "System.Guid" or "Guid";

    private static string[] SplitTopLevelGenericArguments(string argumentsText)
    {
        var arguments = new List<string>();
        int depth = 0;
        int start = 0;
        for (int index = 0; index < argumentsText.Length; index++)
        {
            char character = argumentsText[index];
            if (character == '<')
            {
                depth++;
            }
            else if (character == '>')
            {
                depth--;
            }
            else if (character == ',' && depth == 0)
            {
                arguments.Add(argumentsText.Substring(start, index - start).Trim());
                start = index + 1;
            }
        }

        arguments.Add(argumentsText.Substring(start).Trim());
        return arguments.ToArray();
    }

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
        Method,
        Field,
        Property
    }

    private readonly struct S1InteropBridgeRequests
    {
        public S1InteropBridgeRequests(bool generateUnityEventBridge, bool generateDelegateEventBridge)
        {
            GenerateUnityEventBridge = generateUnityEventBridge;
            GenerateDelegateEventBridge = generateDelegateEventBridge;
        }

        public bool GenerateUnityEventBridge { get; }

        public bool GenerateDelegateEventBridge { get; }
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
