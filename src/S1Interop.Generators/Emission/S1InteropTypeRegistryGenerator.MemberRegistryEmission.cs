using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;

namespace S1Interop.Generators;

public sealed partial class S1InteropTypeRegistryGenerator
{
    private static void GenerateMemberRegistry(
        StringBuilder builder,
        RuntimeBackend runtime,
        ImmutableArray<S1InteropTypeEntry> entries,
        ImmutableArray<S1InteropMemberEntry> members)
    {
        builder.AppendLine();
        builder.AppendLine("    internal enum S1InteropMemberResolutionStatus");
        builder.AppendLine("    {");
        builder.AppendLine("        MissingOwnerType,");
        builder.AppendLine("        MissingMember,");
        builder.AppendLine("        MissingParameterType,");
        builder.AppendLine("        AmbiguousMember,");
        builder.AppendLine("        ArgumentConversionFailed,");
        builder.AppendLine("        InvocationFailed,");
        builder.AppendLine("        ValueAccessFailed,");
        builder.AppendLine("        ValueSetFailed,");
        builder.AppendLine("        ResolutionFailed");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    internal readonly struct S1InteropMemberResolutionReport");
        builder.AppendLine("    {");
        builder.AppendLine("        public S1InteropMemberResolutionReport(S1InteropMemberResolutionStatus status, string ownerTypeName, string memberName, S1InteropMemberKind kind, string? parameterTypeNames, string? message, string? exceptionTypeName)");
        builder.AppendLine("        {");
        builder.AppendLine("            Status = status;");
        builder.AppendLine("            OwnerTypeName = ownerTypeName;");
        builder.AppendLine("            MemberName = memberName;");
        builder.AppendLine("            Kind = kind;");
        builder.AppendLine("            ParameterTypeNames = parameterTypeNames;");
        builder.AppendLine("            Message = message;");
        builder.AppendLine("            ExceptionTypeName = exceptionTypeName;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public S1InteropMemberResolutionStatus Status { get; }");
        builder.AppendLine();
        builder.AppendLine("        public string OwnerTypeName { get; }");
        builder.AppendLine();
        builder.AppendLine("        public string MemberName { get; }");
        builder.AppendLine();
        builder.AppendLine("        public S1InteropMemberKind Kind { get; }");
        builder.AppendLine();
        builder.AppendLine("        public string? ParameterTypeNames { get; }");
        builder.AppendLine();
        builder.AppendLine("        public string? Message { get; }");
        builder.AppendLine();
        builder.AppendLine("        public string? ExceptionTypeName { get; }");
        builder.AppendLine();
        builder.AppendLine("        public override string ToString()");
        builder.AppendLine("        {");
        builder.AppendLine("            string value = Status + \": \" + OwnerTypeName + \".\" + MemberName + \" (\" + Kind + \")\";");
        builder.AppendLine("            if (!string.IsNullOrWhiteSpace(ParameterTypeNames))");
        builder.AppendLine("            {");
        builder.AppendLine("                value += \" [\" + ParameterTypeNames + \"]\";");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (!string.IsNullOrWhiteSpace(Message))");
        builder.AppendLine("            {");
        builder.AppendLine("                value += \" - \" + Message;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (!string.IsNullOrWhiteSpace(ExceptionTypeName))");
        builder.AppendLine("            {");
        builder.AppendLine("                value += \" [\" + ExceptionTypeName + \"]\";");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return value;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    internal static class S1InteropMemberRegistry");
        builder.AppendLine("    {");
        builder.AppendLine("        private const System.Reflection.BindingFlags AllBindings = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;");
        builder.AppendLine("        private static readonly System.Collections.Generic.Dictionary<string, System.Reflection.MemberInfo?> Cache = new System.Collections.Generic.Dictionary<string, System.Reflection.MemberInfo?>(System.StringComparer.Ordinal);");
        builder.AppendLine("        private static readonly System.Collections.Generic.List<S1InteropMemberResolutionReport> MutableReports = new System.Collections.Generic.List<S1InteropMemberResolutionReport>();");
        builder.AppendLine();
        builder.AppendLine("        internal static System.Collections.Generic.IReadOnlyList<S1InteropMemberResolutionReport> Reports => MutableReports;");
        builder.AppendLine();
        builder.AppendLine("        internal static S1InteropMemberResolutionReport? LastReport => MutableReports.Count == 0 ? (S1InteropMemberResolutionReport?)null : MutableReports[MutableReports.Count - 1];");
        builder.AppendLine();
        builder.AppendLine("        internal static void ClearReports() => MutableReports.Clear();");
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
                    builder.AppendLine($"        public static T? Invoke{member.Alias}<T>(params object?[] args) => CastResult<T>(Invoke{member.Alias}(args));");
                }
                else
                {
                    builder.AppendLine($"        public static object? Invoke{member.Alias}(object? instance, params object?[] args) => Invoke(S1InteropTypeRegistry.{member.OwnerAlias}Name, {member.Alias}Name, {GenerateParameterTypeNamesExpression(runtime, entries, member)}, instance, args);");
                    builder.AppendLine($"        public static T? Invoke{member.Alias}<T>(object? instance, params object?[] args) => CastResult<T>(Invoke{member.Alias}(instance, args));");
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
                    if (member.CanWrite)
                    {
                        builder.AppendLine($"        public static bool TrySet{member.Alias}(object? value) => TrySetValue(S1InteropTypeRegistry.{member.OwnerAlias}Name, {member.Alias}Name, null, value, {memberKind});");
                    }
                }
                else
                {
                    builder.AppendLine($"        public static object? Get{member.Alias}(object instance) => GetValue(S1InteropTypeRegistry.{member.OwnerAlias}Name, {member.Alias}Name, instance, {memberKind});");
                    builder.AppendLine($"        public static T? Get{member.Alias}<T>(object instance) where T : class => Get{member.Alias}(instance) as T;");
                    builder.AppendLine($"        public static T? Get{member.Alias}Value<T>(object instance) where T : struct => Get{member.Alias}(instance) is T value ? value : (T?)null;");
                    if (member.CanWrite)
                    {
                        builder.AppendLine($"        public static bool TrySet{member.Alias}(object instance, object? value) => TrySetValue(S1InteropTypeRegistry.{member.OwnerAlias}Name, {member.Alias}Name, instance, value, {memberKind});");
                    }
                }
            }

            builder.AppendLine();
        }

        GenerateMemberValueAccessHelpers(builder);
        GenerateMemberForwardConversionHelpers(builder);
        GenerateMemberInvocationHelpers(builder);
        GenerateMemberBackConversionHelpers(builder);
        GenerateMemberResolutionHelpers(builder);
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
}
