using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace S1Interop.Generators;

public sealed partial class S1InteropTypeRegistryGenerator
{
    private static void GenerateTypeFacades(
        StringBuilder builder,
        ImmutableArray<S1InteropTypeEntry> entries,
        ImmutableArray<S1InteropMemberEntry> members)
    {
        ILookup<string, S1InteropMemberEntry> membersByOwnerAlias = members.ToLookup(member => member.OwnerAlias, StringComparer.Ordinal);

        foreach (S1InteropTypeEntry entry in entries.OrderBy(entry => entry.MonoTypeName, StringComparer.Ordinal))
        {
            TypeFacadeName facadeName = GetTypeFacadeName(entry);
            if (IsReservedFacadeTypeName(facadeName.TypeName))
            {
                continue;
            }

            GenerateTypeFacade(builder, entry, membersByOwnerAlias[entry.Alias], facadeName);
        }
    }

    private static void GenerateTypeFacade(
        StringBuilder builder,
        S1InteropTypeEntry entry,
        IEnumerable<S1InteropMemberEntry> members,
        TypeFacadeName facadeName)
    {
        string handleType = GetHandleTypeName(entry);
        builder.AppendLine();
        builder.AppendLine($"namespace {facadeName.NamespaceName}");
        builder.AppendLine("{");
        builder.AppendLine($"    internal static class {facadeName.TypeName}");
        builder.AppendLine("    {");
        builder.AppendLine("        public readonly struct Handle");
        builder.AppendLine("        {");
        builder.AppendLine($"            private readonly {handleType} value;");
        builder.AppendLine();
        builder.AppendLine($"            internal Handle({handleType} value)");
        builder.AppendLine("            {");
        builder.AppendLine("                this.value = value;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine($"            internal {handleType} Value => value;");
        builder.AppendLine("            public object? Instance => value.Instance;");
        builder.AppendLine("            public bool HasValue => value.HasValue;");

        var emittedHandleMembers = CreateReservedHandleMemberNames();
        foreach (S1InteropMemberEntry member in members.OrderBy(member => member.MemberName, StringComparer.Ordinal))
        {
            if (ShouldGenerateHandleMember(member) &&
                TryReserveHandleMemberNames(member, emittedHandleMembers))
            {
                GenerateTypeHandleMember(builder, member);
            }
        }

        builder.AppendLine("            public override string ToString() => value.ToString();");
        builder.AppendLine($"            public static implicit operator {handleType}(Handle handle) => handle.value;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine($"        public static global::System.Type? Type => S1Interop.Generated.S1InteropTypeRegistry.{entry.Alias};");
        builder.AppendLine($"        public static string TypeName => S1Interop.Generated.S1InteropTypeRegistry.{entry.Alias}Name;");
        builder.AppendLine($"        public static object? Create(params object?[] args) => S1Interop.Generated.S1InteropTypeRegistry.Create{entry.Alias}(args);");
        builder.AppendLine($"        public static T? Create<T>(params object?[] args) where T : class => S1Interop.Generated.S1InteropTypeRegistry.Create{entry.Alias}<T>(args);");
        builder.AppendLine($"        public static bool Is(object? instance) => S1Interop.Generated.S1InteropTypeRegistry.Is{entry.Alias}(instance);");
        builder.AppendLine("        public static bool TryAs(object? instance, out Handle value)");
        builder.AppendLine("        {");
        builder.AppendLine($"            if (!S1Interop.Generated.S1InteropTypeRegistry.TryAs{entry.Alias}(instance, out {handleType} generatedValue))");
        builder.AppendLine("            {");
        builder.AppendLine("                value = default;");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            value = new Handle(generatedValue);");
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine($"        public static Handle As(object? instance) => new Handle(S1Interop.Generated.S1InteropTypeRegistry.As{entry.Alias}(instance));");
        builder.AppendLine($"        public static object? Get(Handle instance, string memberName) => S1Interop.Generated.S1InteropTypeRegistry.Get{entry.Alias}(instance.Value, memberName);");
        builder.AppendLine($"        public static object? Get(object? instance, string memberName) => S1Interop.Generated.S1InteropTypeRegistry.Get{entry.Alias}(instance, memberName);");
        builder.AppendLine($"        public static bool TrySet(Handle instance, string memberName, object? value) => S1Interop.Generated.S1InteropTypeRegistry.TrySet{entry.Alias}(instance.Value, memberName, value);");
        builder.AppendLine($"        public static bool TrySet(object? instance, string memberName, object? value) => S1Interop.Generated.S1InteropTypeRegistry.TrySet{entry.Alias}(instance, memberName, value);");
        builder.AppendLine($"        public static object? Invoke(Handle instance, string methodName, params object?[] args) => S1Interop.Generated.S1InteropTypeRegistry.Invoke{entry.Alias}(instance.Value, methodName, args);");
        builder.AppendLine($"        public static object? Invoke(object? instance, string methodName, params object?[] args) => S1Interop.Generated.S1InteropTypeRegistry.Invoke{entry.Alias}(instance, methodName, args);");

        var emittedFacadeMembers = CreateReservedFacadeMemberNames();
        foreach (S1InteropMemberEntry member in members.OrderBy(member => member.MemberName, StringComparer.Ordinal))
        {
            if (TryReserveFacadeMemberNames(member, emittedFacadeMembers))
            {
                GenerateTypeFacadeMember(builder, member);
            }
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
    }

    private static HashSet<string> CreateReservedHandleMemberNames() =>
        new(StringComparer.Ordinal)
        {
            "Value",
            "Instance",
            "HasValue",
            "Handle",
            "GetHandle",
            "GetHandleValue",
            "TrySetHandle",
            "ToString",
            "Equals",
            "GetHashCode",
            "GetType"
        };

    private static HashSet<string> CreateReservedFacadeMemberNames() =>
        new(StringComparer.Ordinal)
        {
            "Type",
            "TypeName",
            "Create",
            "Is",
            "TryAs",
            "As",
            "Get",
            "TrySet",
            "Invoke",
            "ToString",
            "Equals",
            "GetHashCode",
            "GetType"
        };

    private static bool IsReservedFacadeTypeName(string typeName) =>
        CreateReservedFacadeMemberNames().Contains(typeName) ||
        string.Equals(typeName, "Handle", StringComparison.Ordinal);

    private static bool ShouldGenerateHandleMember(S1InteropMemberEntry member) =>
        !member.IsStatic && member.Kind != S1InteropMemberKind.Method;

    private static bool TryReserveHandleMemberNames(S1InteropMemberEntry member, HashSet<string> reservedNames)
    {
        string memberName = ToPascalIdentifier(member.MemberName);
        string[] generatedNames =
        [
            memberName,
            $"Get{memberName}",
            $"Get{memberName}Value",
            $"TrySet{memberName}"
        ];

        if (generatedNames.Any(name => reservedNames.Contains(name)))
        {
            return false;
        }

        foreach (string generatedName in generatedNames)
        {
            reservedNames.Add(generatedName);
        }

        return true;
    }

    private static bool TryReserveFacadeMemberNames(S1InteropMemberEntry member, HashSet<string> reservedNames)
    {
        string memberName = ToPascalIdentifier(member.MemberName);
        string[] generatedNames = member.Kind == S1InteropMemberKind.Method
            ? [memberName]
            :
            [
                $"Get{memberName}",
                $"Get{memberName}Value",
                $"TrySet{memberName}"
            ];

        if (generatedNames.Any(name => reservedNames.Contains(name)))
        {
            return false;
        }

        foreach (string generatedName in generatedNames)
        {
            reservedNames.Add(generatedName);
        }

        return true;
    }

    private static void GenerateTypeHandleMember(StringBuilder builder, S1InteropMemberEntry member)
    {
        string memberName = ToPascalIdentifier(member.MemberName);
        builder.AppendLine();
        builder.AppendLine($"            public object? {memberName} => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}(value.Instance);");
        builder.AppendLine($"            public T? Get{memberName}<T>() where T : class => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}<T>(value.Instance);");
        builder.AppendLine($"            public T? Get{memberName}Value<T>() where T : struct => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}Value<T>(value.Instance);");
        builder.AppendLine($"            public bool TrySet{memberName}(object? memberValue) => S1Interop.Generated.S1InteropMemberRegistry.TrySet{member.Alias}(value.Instance, memberValue);");
    }

    private static void GenerateTypeFacadeMember(StringBuilder builder, S1InteropMemberEntry member)
    {
        string memberName = ToPascalIdentifier(member.MemberName);
        if (member.Kind == S1InteropMemberKind.Method)
        {
            if (member.IsStatic)
            {
                builder.AppendLine($"        public static object? {memberName}(params object?[] args) => S1Interop.Generated.S1InteropMemberRegistry.Invoke{member.Alias}(args);");
                builder.AppendLine($"        public static T? {memberName}<T>(params object?[] args) => S1Interop.Generated.S1InteropMemberRegistry.Invoke{member.Alias}<T>(args);");
            }
            else
            {
                builder.AppendLine($"        public static object? {memberName}(Handle instance, params object?[] args) => S1Interop.Generated.S1InteropMemberRegistry.Invoke{member.Alias}(instance.Value.Instance, args);");
                builder.AppendLine($"        public static object? {memberName}(object? instance, params object?[] args) => S1Interop.Generated.S1InteropMemberRegistry.Invoke{member.Alias}(instance, args);");
                builder.AppendLine($"        public static T? {memberName}<T>(Handle instance, params object?[] args) => S1Interop.Generated.S1InteropMemberRegistry.Invoke{member.Alias}<T>(instance.Value.Instance, args);");
                builder.AppendLine($"        public static T? {memberName}<T>(object? instance, params object?[] args) => S1Interop.Generated.S1InteropMemberRegistry.Invoke{member.Alias}<T>(instance, args);");
            }

            return;
        }

        if (member.IsStatic)
        {
            builder.AppendLine($"        public static object? Get{memberName}() => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}();");
            builder.AppendLine($"        public static T? Get{memberName}<T>() where T : class => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}<T>();");
            builder.AppendLine($"        public static T? Get{memberName}Value<T>() where T : struct => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}Value<T>();");
            builder.AppendLine($"        public static bool TrySet{memberName}(object? value) => S1Interop.Generated.S1InteropMemberRegistry.TrySet{member.Alias}(value);");
        }
        else
        {
            builder.AppendLine($"        public static object? Get{memberName}(Handle instance) => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}(instance.Value.Instance);");
            builder.AppendLine($"        public static object? Get{memberName}(object instance) => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}(instance);");
            builder.AppendLine($"        public static T? Get{memberName}<T>(Handle instance) where T : class => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}<T>(instance.Value.Instance);");
            builder.AppendLine($"        public static T? Get{memberName}<T>(object instance) where T : class => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}<T>(instance);");
            builder.AppendLine($"        public static T? Get{memberName}Value<T>(Handle instance) where T : struct => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}Value<T>(instance.Value.Instance);");
            builder.AppendLine($"        public static T? Get{memberName}Value<T>(object instance) where T : struct => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}Value<T>(instance);");
            builder.AppendLine($"        public static bool TrySet{memberName}(Handle instance, object? value) => S1Interop.Generated.S1InteropMemberRegistry.TrySet{member.Alias}(instance.Value.Instance, value);");
            builder.AppendLine($"        public static bool TrySet{memberName}(object instance, object? value) => S1Interop.Generated.S1InteropMemberRegistry.TrySet{member.Alias}(instance, value);");
        }
    }
}
