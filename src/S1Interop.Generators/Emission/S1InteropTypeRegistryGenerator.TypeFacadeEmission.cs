using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;

namespace S1Interop.Generators;

public sealed partial class S1InteropTypeRegistryGenerator
{
    private static void GenerateTypeFacades(
        StringBuilder builder,
        ImmutableArray<S1InteropTypeEntry> entries,
        ImmutableArray<S1InteropMemberEntry> members,
        ImmutableArray<S1InteropConstructorEntry> constructors,
        ImmutableArray<S1InteropEnumEntry> enums)
    {
        ILookup<string, S1InteropMemberEntry> membersByOwnerAlias = members.ToLookup(member => member.OwnerAlias, StringComparer.Ordinal);
        ILookup<string, S1InteropConstructorEntry> constructorsByOwnerAlias = constructors.ToLookup(constructor => constructor.OwnerAlias, StringComparer.Ordinal);
        IReadOnlyDictionary<string, S1InteropEnumEntry> enumsByOwnerAlias = enums.ToDictionary(entry => entry.OwnerAlias, StringComparer.Ordinal);
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes = CreateFacadeHandleTypeLookup(entries, enumsByOwnerAlias);
        IReadOnlyDictionary<string, string> facadeEnumTypes = CreateFacadeEnumTypeLookup(enums);

        foreach (S1InteropTypeEntry entry in entries.OrderBy(entry => entry.MonoTypeName, StringComparer.Ordinal))
        {
            TypeFacadeName facadeName = GetTypeFacadeName(entry);
            if (IsReservedFacadeTypeName(facadeName.TypeName))
            {
                continue;
            }

            if (enumsByOwnerAlias.TryGetValue(entry.Alias, out S1InteropEnumEntry enumEntry))
            {
                GenerateEnumFacade(builder, enumEntry, facadeName);
                continue;
            }

            GenerateTypeFacade(builder, entry, membersByOwnerAlias[entry.Alias], constructorsByOwnerAlias[entry.Alias], facadeName, facadeHandleTypes, facadeEnumTypes);
        }
    }

    private static void GenerateEnumFacade(StringBuilder builder, S1InteropEnumEntry entry, TypeFacadeName facadeName)
    {
        builder.AppendLine();
        builder.AppendLine($"namespace {facadeName.NamespaceName}");
        builder.AppendLine("{");
        string underlyingType = string.Equals(entry.UnderlyingTypeName, "int", StringComparison.Ordinal)
            ? string.Empty
            : " : " + entry.UnderlyingTypeName;
        builder.AppendLine($"    internal enum {facadeName.TypeName}{underlyingType}");
        builder.AppendLine("    {");
        foreach (S1InteropEnumValueEntry value in entry.Values)
        {
            builder.AppendLine($"        {EscapeCSharpIdentifier(SanitizeIdentifier(value.Name))} = {value.ValueLiteral},");
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
    }

    private static void GenerateTypeFacade(
        StringBuilder builder,
        S1InteropTypeEntry entry,
        IEnumerable<S1InteropMemberEntry> members,
        IEnumerable<S1InteropConstructorEntry> constructors,
        TypeFacadeName facadeName,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes,
        IReadOnlyDictionary<string, string> facadeEnumTypes)
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
        foreach (S1InteropMemberEntry member in members
                     .OrderBy(member => member.Kind == S1InteropMemberKind.Method ? 1 : 0)
                     .ThenBy(member => member.MemberName, StringComparer.Ordinal))
        {
            if (ShouldGenerateHandleMember(member, facadeHandleTypes, facadeEnumTypes) &&
                TryReserveHandleMemberNames(member, emittedHandleMembers))
            {
                GenerateTypeHandleMember(builder, member, facadeHandleTypes, facadeEnumTypes);
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
        builder.AppendLine("        public static Handle CreateHandle(params object?[] args) => As(Create(args));");
        builder.AppendLine("        public static bool TryCreate(out Handle value, params object?[] args)");
        builder.AppendLine("        {");
        builder.AppendLine("            value = CreateHandle(args);");
        builder.AppendLine("            return value.HasValue;");
        builder.AppendLine("        }");
        GenerateTypedConstructorMembers(builder, constructors, facadeHandleTypes, facadeEnumTypes);
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
                GenerateTypeFacadeMember(builder, member, facadeHandleTypes, facadeEnumTypes);
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
            "CreateHandle",
            "TryCreate",
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

    private static bool ShouldGenerateHandleMember(
        S1InteropMemberEntry member,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes,
        IReadOnlyDictionary<string, string> facadeEnumTypes)
    {
        if (member.IsStatic)
        {
            return false;
        }

        return member.Kind == S1InteropMemberKind.Method
            ? CanGenerateTypedMethodFacadeMember(member, facadeHandleTypes, facadeEnumTypes)
            : true;
    }

    private static bool TryReserveHandleMemberNames(S1InteropMemberEntry member, HashSet<string> reservedNames)
    {
        string memberName = ToPascalIdentifier(member.MemberName);
        List<string> generatedNames = member.Kind == S1InteropMemberKind.Method
            ? [memberName]
            :
            [
                memberName,
                $"Get{memberName}",
                $"Get{memberName}Value"
            ];
        if (member.Kind != S1InteropMemberKind.Method && member.CanWrite)
        {
            generatedNames.Add($"TrySet{memberName}");
        }

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
        List<string> generatedNames = member.Kind == S1InteropMemberKind.Method
            ? [memberName]
            :
            [
                $"Get{memberName}",
                $"Get{memberName}Value"
            ];
        if (member.Kind != S1InteropMemberKind.Method && member.CanWrite)
        {
            generatedNames.Add($"TrySet{memberName}");
        }

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

    private static void GenerateTypeHandleMember(
        StringBuilder builder,
        S1InteropMemberEntry member,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes,
        IReadOnlyDictionary<string, string> facadeEnumTypes)
    {
        string memberName = ToPascalIdentifier(member.MemberName);
        if (member.Kind == S1InteropMemberKind.Method)
        {
            GenerateTypedHandleMethodFacadeMember(builder, member, memberName, facadeHandleTypes, facadeEnumTypes);
            return;
        }

        FacadeMemberType memberType = GetFacadeMemberType(member.ValueTypeName, facadeHandleTypes, facadeEnumTypes);
        builder.AppendLine();
        builder.AppendLine($"            public {GetFacadeReturnTypeName(memberType)} {memberName} => {GenerateFacadeGetExpression(member, "this.value.Instance", memberType)};");
        builder.AppendLine($"            public T? Get{memberName}<T>() where T : class => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}<T>(this.value.Instance);");
        builder.AppendLine($"            public T? Get{memberName}Value<T>() where T : struct => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}Value<T>(this.value.Instance);");
        if (member.CanWrite)
        {
            GenerateTypedTrySetHandleMember(builder, member, memberName, memberType);
            builder.AppendLine($"            public bool TrySet{memberName}(object? memberValue) => S1Interop.Generated.S1InteropMemberRegistry.TrySet{member.Alias}(this.value.Instance, memberValue);");
        }
    }

    private static void GenerateTypeFacadeMember(
        StringBuilder builder,
        S1InteropMemberEntry member,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes,
        IReadOnlyDictionary<string, string> facadeEnumTypes)
    {
        string memberName = ToPascalIdentifier(member.MemberName);
        if (member.Kind == S1InteropMemberKind.Method)
        {
            if (member.IsStatic)
            {
                builder.AppendLine($"        public static object? {memberName}(params object?[] args) => S1Interop.Generated.S1InteropMemberRegistry.Invoke{member.Alias}(args);");
                builder.AppendLine($"        public static T? {memberName}<T>(params object?[] args) => S1Interop.Generated.S1InteropMemberRegistry.Invoke{member.Alias}<T>(args);");
                GenerateTypedStaticMethodFacadeMember(builder, member, memberName, facadeHandleTypes, facadeEnumTypes);
            }
            else
            {
                builder.AppendLine($"        public static object? {memberName}(Handle instance, params object?[] args) => S1Interop.Generated.S1InteropMemberRegistry.Invoke{member.Alias}(instance.Value.Instance, args);");
                builder.AppendLine($"        public static object? {memberName}(object? instance, params object?[] args) => S1Interop.Generated.S1InteropMemberRegistry.Invoke{member.Alias}(instance, args);");
                builder.AppendLine($"        public static T? {memberName}<T>(Handle instance, params object?[] args) => S1Interop.Generated.S1InteropMemberRegistry.Invoke{member.Alias}<T>(instance.Value.Instance, args);");
                builder.AppendLine($"        public static T? {memberName}<T>(object? instance, params object?[] args) => S1Interop.Generated.S1InteropMemberRegistry.Invoke{member.Alias}<T>(instance, args);");
                GenerateTypedInstanceMethodFacadeMember(builder, member, memberName, facadeHandleTypes, facadeEnumTypes);
            }

            return;
        }

        FacadeMemberType memberType = GetFacadeMemberType(member.ValueTypeName, facadeHandleTypes, facadeEnumTypes);
        if (member.IsStatic)
        {
            builder.AppendLine($"        public static {GetFacadeReturnTypeName(memberType)} Get{memberName}() => {GenerateFacadeGetExpression(member, instanceExpression: null, memberType)};");
            builder.AppendLine($"        public static T? Get{memberName}<T>() where T : class => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}<T>();");
            builder.AppendLine($"        public static T? Get{memberName}Value<T>() where T : struct => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}Value<T>();");
            if (member.CanWrite)
            {
                GenerateTypedTrySetStaticFacadeMember(builder, member, memberName, memberType);
                builder.AppendLine($"        public static bool TrySet{memberName}(object? value) => S1Interop.Generated.S1InteropMemberRegistry.TrySet{member.Alias}(value);");
            }
        }
        else
        {
            builder.AppendLine($"        public static {GetFacadeReturnTypeName(memberType)} Get{memberName}(Handle instance) => {GenerateFacadeGetExpression(member, "instance.Value.Instance", memberType)};");
            builder.AppendLine($"        public static {GetFacadeReturnTypeName(memberType)} Get{memberName}(object instance) => {GenerateFacadeGetExpression(member, "instance", memberType)};");
            builder.AppendLine($"        public static T? Get{memberName}<T>(Handle instance) where T : class => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}<T>(instance.Value.Instance);");
            builder.AppendLine($"        public static T? Get{memberName}<T>(object instance) where T : class => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}<T>(instance);");
            builder.AppendLine($"        public static T? Get{memberName}Value<T>(Handle instance) where T : struct => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}Value<T>(instance.Value.Instance);");
            builder.AppendLine($"        public static T? Get{memberName}Value<T>(object instance) where T : struct => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}Value<T>(instance);");
            if (member.CanWrite)
            {
                GenerateTypedTrySetInstanceFacadeMember(builder, member, memberName, memberType);
                builder.AppendLine($"        public static bool TrySet{memberName}(Handle instance, object? value) => S1Interop.Generated.S1InteropMemberRegistry.TrySet{member.Alias}(instance.Value.Instance, value);");
                builder.AppendLine($"        public static bool TrySet{memberName}(object instance, object? value) => S1Interop.Generated.S1InteropMemberRegistry.TrySet{member.Alias}(instance, value);");
            }
        }
    }

    private static void GenerateTypedTrySetHandleMember(
        StringBuilder builder,
        S1InteropMemberEntry member,
        string memberName,
        FacadeMemberType memberType)
    {
        if (!ShouldEmitTypedSetter(memberType))
        {
            return;
        }

        builder.AppendLine($"            public bool TrySet{memberName}({GetFacadeParameterTypeName(memberType)} memberValue) => S1Interop.Generated.S1InteropMemberRegistry.TrySet{member.Alias}(this.value.Instance, {GetFacadeArgumentExpression("memberValue", memberType)});");
    }

    private static void GenerateTypedTrySetStaticFacadeMember(
        StringBuilder builder,
        S1InteropMemberEntry member,
        string memberName,
        FacadeMemberType memberType)
    {
        if (!ShouldEmitTypedSetter(memberType))
        {
            return;
        }

        builder.AppendLine($"        public static bool TrySet{memberName}({GetFacadeParameterTypeName(memberType)} value) => S1Interop.Generated.S1InteropMemberRegistry.TrySet{member.Alias}({GetFacadeArgumentExpression("value", memberType)});");
    }

    private static void GenerateTypedTrySetInstanceFacadeMember(
        StringBuilder builder,
        S1InteropMemberEntry member,
        string memberName,
        FacadeMemberType memberType)
    {
        if (!ShouldEmitTypedSetter(memberType))
        {
            return;
        }

        builder.AppendLine($"        public static bool TrySet{memberName}(Handle instance, {GetFacadeParameterTypeName(memberType)} value) => S1Interop.Generated.S1InteropMemberRegistry.TrySet{member.Alias}(instance.Value.Instance, {GetFacadeArgumentExpression("value", memberType)});");
        builder.AppendLine($"        public static bool TrySet{memberName}(object instance, {GetFacadeParameterTypeName(memberType)} value) => S1Interop.Generated.S1InteropMemberRegistry.TrySet{member.Alias}(instance, {GetFacadeArgumentExpression("value", memberType)});");
    }

    private static void GenerateTypedStaticMethodFacadeMember(
        StringBuilder builder,
        S1InteropMemberEntry member,
        string memberName,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes,
        IReadOnlyDictionary<string, string> facadeEnumTypes)
    {
        if (!TryGetTypedMethodReturnType(member, facadeHandleTypes, facadeEnumTypes, out FacadeMemberType returnType) ||
            !TryGetTypedMethodParameters(member, facadeHandleTypes, facadeEnumTypes, out string? parameters))
        {
            return;
        }

        builder.AppendLine($"        public static {GetFacadeReturnTypeName(returnType)} {memberName}({parameters}){GenerateTypedMethodInvocation(member, receiverExpression: null, returnType, facadeHandleTypes, facadeEnumTypes)}");
    }

    private static void GenerateTypedInstanceMethodFacadeMember(
        StringBuilder builder,
        S1InteropMemberEntry member,
        string memberName,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes,
        IReadOnlyDictionary<string, string> facadeEnumTypes)
    {
        if (!TryGetTypedMethodReturnType(member, facadeHandleTypes, facadeEnumTypes, out FacadeMemberType returnType) ||
            !TryGetTypedMethodParameters(member, facadeHandleTypes, facadeEnumTypes, out string? parameters))
        {
            return;
        }

        string parameterPrefix = string.IsNullOrEmpty(parameters) ? string.Empty : ", " + parameters;
        builder.AppendLine($"        public static {GetFacadeReturnTypeName(returnType)} {memberName}(Handle instance{parameterPrefix}){GenerateTypedMethodInvocation(member, "instance.Value.Instance", returnType, facadeHandleTypes, facadeEnumTypes)}");
        builder.AppendLine($"        public static {GetFacadeReturnTypeName(returnType)} {memberName}(object? instance{parameterPrefix}){GenerateTypedMethodInvocation(member, "instance", returnType, facadeHandleTypes, facadeEnumTypes)}");
    }

    private static void GenerateTypedHandleMethodFacadeMember(
        StringBuilder builder,
        S1InteropMemberEntry member,
        string memberName,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes,
        IReadOnlyDictionary<string, string> facadeEnumTypes)
    {
        if (!TryGetTypedMethodReturnType(member, facadeHandleTypes, facadeEnumTypes, out FacadeMemberType returnType) ||
            !TryGetTypedMethodParameters(member, facadeHandleTypes, facadeEnumTypes, out string? parameters))
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine($"            public {GetFacadeReturnTypeName(returnType)} {memberName}({parameters}){GenerateTypedMethodInvocation(member, "this.value.Instance", returnType, facadeHandleTypes, facadeEnumTypes)}");
    }

    private static bool CanGenerateTypedMethodFacadeMember(
        S1InteropMemberEntry member,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes,
        IReadOnlyDictionary<string, string> facadeEnumTypes) =>
        TryGetTypedMethodReturnType(member, facadeHandleTypes, facadeEnumTypes, out _) &&
        TryGetTypedMethodParameters(member, facadeHandleTypes, facadeEnumTypes, out _);

    private static void GenerateTypedConstructorMembers(
        StringBuilder builder,
        IEnumerable<S1InteropConstructorEntry> constructors,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes,
        IReadOnlyDictionary<string, string> facadeEnumTypes)
    {
        var emittedConstructorSignatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (S1InteropConstructorEntry constructor in constructors
            .OrderBy(constructor => constructor.ParameterTypeNames.Length)
            .ThenBy(GetConstructorSignatureKey, StringComparer.Ordinal))
        {
            if (!emittedConstructorSignatures.Add(GetConstructorSignatureKey(constructor)))
            {
                continue;
            }

            if (!TryGetTypedCallableParameters(constructor.ParameterTypeNames, constructor.ParameterNames, facadeHandleTypes, facadeEnumTypes, out string? parameters))
            {
                continue;
            }

            string argumentList = GenerateFacadeArgumentList(constructor.ParameterTypeNames, constructor.ParameterNames, facadeHandleTypes, facadeEnumTypes);
            builder.AppendLine($"        public static Handle CreateHandle({parameters}) => As(Create({argumentList}));");
            builder.AppendLine($"        public static bool TryCreate(out Handle value{(string.IsNullOrEmpty(parameters) ? string.Empty : ", " + parameters)})");
            builder.AppendLine("        {");
            builder.AppendLine($"            value = CreateHandle({argumentList});");
            builder.AppendLine("            return value.HasValue;");
            builder.AppendLine("        }");
        }
    }

    private static string GetConstructorSignatureKey(S1InteropConstructorEntry constructor) =>
        string.Join("|", constructor.ParameterTypeNames);

    private static bool TryGetTypedMethodReturnType(
        S1InteropMemberEntry member,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes,
        IReadOnlyDictionary<string, string> facadeEnumTypes,
        out FacadeMemberType returnType)
    {
        returnType = GetFacadeMemberType(member.ValueTypeName, facadeHandleTypes, facadeEnumTypes);
        return returnType.HasConcreteType;
    }

    private static bool TryGetTypedMethodParameters(
        S1InteropMemberEntry member,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes,
        IReadOnlyDictionary<string, string> facadeEnumTypes,
        out string? parameters)
    {
        parameters = null;
        return TryGetTypedCallableParameters(member.ParameterTypeNames, member.ParameterNames, facadeHandleTypes, facadeEnumTypes, out parameters);
    }

    private static bool TryGetTypedCallableParameters(
        ImmutableArray<string> parameterTypeNames,
        ImmutableArray<string> parameterNames,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes,
        IReadOnlyDictionary<string, string> facadeEnumTypes,
        out string? parameters)
    {
        parameters = null;
        if (parameterTypeNames.Length != parameterNames.Length)
        {
            return false;
        }

        var generatedParameters = new List<string>(parameterTypeNames.Length);
        string[] generatedParameterNames = GetGeneratedParameterNames(parameterNames);
        for (int index = 0; index < parameterTypeNames.Length; index++)
        {
            FacadeMemberType type = GetFacadeParameterType(parameterTypeNames[index], facadeHandleTypes, facadeEnumTypes);
            if (!type.HasConcreteType)
            {
                return false;
            }

            generatedParameters.Add($"{GetFacadeParameterTypeName(type)} {generatedParameterNames[index]}");
        }

        parameters = string.Join(", ", generatedParameters);
        return true;
    }

    private static string GenerateTypedMethodInvocation(
        S1InteropMemberEntry member,
        string? receiverExpression,
        FacadeMemberType returnType,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes,
        IReadOnlyDictionary<string, string> facadeEnumTypes)
    {
        string argumentList = GenerateFacadeArgumentList(member.ParameterTypeNames, member.ParameterNames, facadeHandleTypes, facadeEnumTypes);
        string receiverPrefix = receiverExpression is null ? string.Empty : receiverExpression + (argumentList.Length == 0 ? string.Empty : ", ");
        string invocation = returnType.IsVoid
            ? $"S1Interop.Generated.S1InteropMemberRegistry.Invoke{member.Alias}({receiverPrefix}{argumentList})"
            : $"S1Interop.Generated.S1InteropMemberRegistry.Invoke{member.Alias}{GetGenericInvocationSuffix(returnType)}({receiverPrefix}{argumentList})";
        return returnType.IsHandle
            ? $" => {returnType.HandleType.GetValueOrDefault().QualifiedFacadeName}.As({invocation});"
            : $" => {invocation};";
    }

    private static string GenerateFacadeArgumentList(
        ImmutableArray<string> parameterTypeNames,
        ImmutableArray<string> parameterNames,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes,
        IReadOnlyDictionary<string, string> facadeEnumTypes)
    {
        if (parameterNames.IsDefaultOrEmpty)
        {
            return string.Empty;
        }

        var arguments = new List<string>(parameterNames.Length);
        string[] generatedParameterNames = GetGeneratedParameterNames(parameterNames);
        for (int index = 0; index < parameterTypeNames.Length; index++)
        {
            arguments.Add(GenerateFacadeArgumentExpression(generatedParameterNames[index], parameterTypeNames[index], facadeHandleTypes, facadeEnumTypes));
        }

        return string.Join(", ", arguments);
    }

    private static string GenerateFacadeArgumentExpression(
        string parameterName,
        string parameterTypeName,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes,
        IReadOnlyDictionary<string, string> facadeEnumTypes) =>
        GetFacadeParameterType(parameterTypeName, facadeHandleTypes, facadeEnumTypes).IsHandle
            ? parameterName + ".Value.Instance"
            : parameterName;

    private static string[] GetGeneratedParameterNames(ImmutableArray<string> parameterNames)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        string[] generatedNames = new string[parameterNames.Length];
        for (int index = 0; index < parameterNames.Length; index++)
        {
            string baseName = SanitizeParameterName(parameterNames[index], index);
            string name = baseName;
            int suffix = 2;
            while (!used.Add(name))
            {
                name = baseName + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            generatedNames[index] = name;
        }

        return generatedNames;
    }

    private static string SanitizeParameterName(string parameterName, int index)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            return "arg" + index.ToString(CultureInfo.InvariantCulture);
        }

        string candidate = parameterName;
        if (candidate is "instance" or "value" or "args" or "result" or "this" or "base")
        {
            candidate = "arg" + char.ToUpperInvariant(candidate[0]) + candidate.Substring(1);
        }

        return IsCSharpKeyword(candidate) ? "arg" + index.ToString(CultureInfo.InvariantCulture) : candidate;
    }

    private static string GenerateFacadeGetExpression(S1InteropMemberEntry member, string? instanceExpression, FacadeMemberType memberType)
    {
        string callArguments = instanceExpression is null ? string.Empty : instanceExpression;
        if (!memberType.HasConcreteType || memberType.IsObject)
        {
            return $"S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}({callArguments})";
        }

        if (memberType.IsHandle)
        {
            return $"{memberType.HandleType.GetValueOrDefault().QualifiedFacadeName}.As(S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}({callArguments}))";
        }

        return $"S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}{GetGenericAccessSuffix(memberType)}({callArguments})";
    }

    private static FacadeMemberType GetFacadeMemberType(
        string? typeName,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes,
        IReadOnlyDictionary<string, string> facadeEnumTypes)
    {
        FacadeMemberType parameterType = GetFacadeParameterType(typeName, facadeHandleTypes, facadeEnumTypes);
        return parameterType.HasConcreteType
            ? parameterType
            : string.Equals(StripNullableAnnotation(typeName), "void", StringComparison.Ordinal)
                ? FacadeMemberType.Void()
                : default;
    }

    private static FacadeMemberType GetFacadeParameterType(
        string? typeName,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes,
        IReadOnlyDictionary<string, string> facadeEnumTypes)
    {
        string? normalized = StripNullableAnnotation(typeName);
        if (normalized is null)
        {
            return default;
        }

        if (TryGetScalarFacadeTypeName(normalized, out string? scalarTypeName))
        {
            return FacadeMemberType.Scalar(scalarTypeName!);
        }

        if (facadeHandleTypes.TryGetValue(NormalizeBackendNeutralTypeName(normalized), out FacadeHandleType handleType))
        {
            return FacadeMemberType.Handle(handleType);
        }

        if (facadeEnumTypes.TryGetValue(NormalizeBackendNeutralTypeName(normalized), out string? enumTypeName))
        {
            return FacadeMemberType.Enum(enumTypeName);
        }

        return default;
    }

    private static bool TryGetScalarFacadeTypeName(string typeName, out string? scalarTypeName)
    {
        scalarTypeName = NormalizeComparableTypeName(typeName);
        return scalarTypeName is "bool" or "byte" or "char" or "decimal" or "double" or "float" or "int" or "long" or "object" or "sbyte" or "short" or "string" or "uint" or "ulong" or "ushort";
    }

    private static string? StripNullableAnnotation(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        string normalized = typeName!;
        if (normalized.EndsWith("&", StringComparison.Ordinal))
        {
            return null;
        }

        return normalized.EndsWith("?", StringComparison.Ordinal)
            ? normalized.Substring(0, normalized.Length - 1)
            : normalized;
    }

    private static bool ShouldEmitTypedSetter(FacadeMemberType memberType) =>
        memberType.HasConcreteType &&
        !memberType.IsVoid &&
        !memberType.IsObject;

    private static string GetFacadeReturnTypeName(FacadeMemberType memberType)
    {
        if (!memberType.HasConcreteType || memberType.IsObject)
        {
            return "object?";
        }

        if (memberType.IsVoid)
        {
            return "void";
        }

        return memberType.IsHandle
            ? memberType.HandleType.GetValueOrDefault().QualifiedHandleName
            : memberType.IsEnum
                ? memberType.EnumTypeName + "?"
            : memberType.ScalarTypeName + "?";
    }

    private static string GetFacadeParameterTypeName(FacadeMemberType memberType)
    {
        if (memberType.IsHandle)
        {
            return memberType.HandleType.GetValueOrDefault().QualifiedHandleName;
        }

        if (memberType.IsEnum)
        {
            return memberType.EnumTypeName!;
        }

        return IsReferenceFacadeType(memberType.ScalarTypeName!)
            ? memberType.ScalarTypeName + "?"
            : memberType.ScalarTypeName!;
    }

    private static bool IsReferenceFacadeType(string memberTypeName) =>
        memberTypeName is "object" or "string";

    private static string GetGenericAccessSuffix(FacadeMemberType memberType) =>
        memberType.IsEnum
            ? $"Value<{memberType.EnumTypeName}>"
            :
        IsReferenceFacadeType(memberType.ScalarTypeName!)
            ? $"<{memberType.ScalarTypeName}>"
            : $"Value<{memberType.ScalarTypeName}>";

    private static string GetGenericInvocationSuffix(FacadeMemberType memberType) =>
        memberType.IsHandle || memberType.IsObject
            ? string.Empty
            : memberType.IsEnum
                ? $"<{memberType.EnumTypeName}>"
                : $"<{memberType.ScalarTypeName}>";

    private static string GetFacadeArgumentExpression(string argumentName, FacadeMemberType argumentType) =>
        argumentType.IsHandle ? argumentName + ".Value.Instance" : argumentName;

    private static IReadOnlyDictionary<string, FacadeHandleType> CreateFacadeHandleTypeLookup(
        ImmutableArray<S1InteropTypeEntry> entries,
        IReadOnlyDictionary<string, S1InteropEnumEntry> enumsByOwnerAlias)
    {
        var lookup = new Dictionary<string, FacadeHandleType>(StringComparer.Ordinal);
        foreach (S1InteropTypeEntry entry in entries)
        {
            if (enumsByOwnerAlias.ContainsKey(entry.Alias))
            {
                continue;
            }

            TypeFacadeName facadeName = GetTypeFacadeName(entry);
            if (IsReservedFacadeTypeName(facadeName.TypeName))
            {
                continue;
            }

            var handleType = new FacadeHandleType(facadeName);
            lookup[NormalizeBackendNeutralTypeName(entry.MonoTypeName)] = handleType;
            lookup[NormalizeBackendNeutralTypeName(entry.Il2CppTypeName)] = handleType;
        }

        return lookup;
    }

    private static IReadOnlyDictionary<string, string> CreateFacadeEnumTypeLookup(ImmutableArray<S1InteropEnumEntry> enums)
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (S1InteropEnumEntry entry in enums)
        {
            var typeEntry = new S1InteropTypeEntry(entry.OwnerAlias, entry.MonoTypeName, entry.Il2CppTypeName);
            TypeFacadeName facadeName = GetTypeFacadeName(typeEntry);
            if (IsReservedFacadeTypeName(facadeName.TypeName))
            {
                continue;
            }

            string qualifiedName = facadeName.NamespaceName + "." + facadeName.TypeName;
            lookup[NormalizeBackendNeutralTypeName(entry.MonoTypeName)] = qualifiedName;
            lookup[NormalizeBackendNeutralTypeName(entry.Il2CppTypeName)] = qualifiedName;
        }

        return lookup;
    }

    private static string EscapeCSharpIdentifier(string identifier) =>
        IsCSharpKeyword(identifier) ? "@" + identifier : identifier;

    private static bool IsCSharpKeyword(string name) =>
        name is "abstract" or "as" or "base" or "bool" or "break" or "byte" or "case" or "catch" or "char" or
            "checked" or "class" or "const" or "continue" or "decimal" or "default" or "delegate" or "do" or
            "double" or "else" or "enum" or "event" or "explicit" or "extern" or "false" or "finally" or
            "fixed" or "float" or "for" or "foreach" or "goto" or "if" or "implicit" or "in" or "int" or
            "interface" or "internal" or "is" or "lock" or "long" or "namespace" or "new" or "null" or
            "object" or "operator" or "out" or "override" or "params" or "private" or "protected" or
            "public" or "readonly" or "ref" or "return" or "sbyte" or "sealed" or "short" or "sizeof" or
            "stackalloc" or "static" or "string" or "struct" or "switch" or "this" or "throw" or "true" or
            "try" or "typeof" or "uint" or "ulong" or "unchecked" or "unsafe" or "ushort" or "using" or
            "virtual" or "void" or "volatile" or "while";

    private static string NormalizeBackendNeutralTypeName(string typeName) =>
        typeName
            .Replace("Il2CppScheduleOne.", "ScheduleOne.")
            .Replace("Il2CppSteamworks.", "Steamworks.")
            .Replace("Il2CppSystem.", "System.");

    private readonly struct FacadeHandleType
    {
        public FacadeHandleType(TypeFacadeName facadeName)
        {
            QualifiedFacadeName = facadeName.NamespaceName + "." + facadeName.TypeName;
            QualifiedHandleName = QualifiedFacadeName + ".Handle";
        }

        public string QualifiedFacadeName { get; }

        public string QualifiedHandleName { get; }
    }

    private readonly struct FacadeMemberType
    {
        private FacadeMemberType(string? scalarTypeName, FacadeHandleType? handleType, string? enumTypeName, bool isVoid)
        {
            ScalarTypeName = scalarTypeName;
            HandleType = handleType;
            EnumTypeName = enumTypeName;
            IsVoid = isVoid;
        }

        public string? ScalarTypeName { get; }

        public FacadeHandleType? HandleType { get; }

        public string? EnumTypeName { get; }

        public bool IsVoid { get; }

        public bool HasConcreteType => ScalarTypeName is not null || HandleType.HasValue || EnumTypeName is not null || IsVoid;

        public bool IsHandle => HandleType.HasValue;

        public bool IsEnum => EnumTypeName is not null;

        public bool IsObject => string.Equals(ScalarTypeName, "object", StringComparison.Ordinal);

        public static FacadeMemberType Scalar(string typeName) => new(typeName, null, null, isVoid: false);

        public static FacadeMemberType Handle(FacadeHandleType handleType) => new(null, handleType, null, isVoid: false);

        public static FacadeMemberType Enum(string enumTypeName) => new(null, null, enumTypeName, isVoid: false);

        public static FacadeMemberType Void() => new(null, null, null, isVoid: true);
    }
}
