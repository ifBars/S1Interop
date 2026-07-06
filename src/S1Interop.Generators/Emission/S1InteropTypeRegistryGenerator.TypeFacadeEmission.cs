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
        ImmutableArray<S1InteropMemberEntry> members,
        ImmutableArray<S1InteropConstructorEntry> constructors)
    {
        ILookup<string, S1InteropMemberEntry> membersByOwnerAlias = members.ToLookup(member => member.OwnerAlias, StringComparer.Ordinal);
        ILookup<string, S1InteropConstructorEntry> constructorsByOwnerAlias = constructors.ToLookup(constructor => constructor.OwnerAlias, StringComparer.Ordinal);
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes = CreateFacadeHandleTypeLookup(entries);

        foreach (S1InteropTypeEntry entry in entries.OrderBy(entry => entry.MonoTypeName, StringComparer.Ordinal))
        {
            TypeFacadeName facadeName = GetTypeFacadeName(entry);
            if (IsReservedFacadeTypeName(facadeName.TypeName))
            {
                continue;
            }

            GenerateTypeFacade(builder, entry, membersByOwnerAlias[entry.Alias], constructorsByOwnerAlias[entry.Alias], facadeName, facadeHandleTypes);
        }
    }

    private static void GenerateTypeFacade(
        StringBuilder builder,
        S1InteropTypeEntry entry,
        IEnumerable<S1InteropMemberEntry> members,
        IEnumerable<S1InteropConstructorEntry> constructors,
        TypeFacadeName facadeName,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes)
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
                GenerateTypeHandleMember(builder, member, facadeHandleTypes);
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
        GenerateTypedConstructorMembers(builder, constructors, facadeHandleTypes);
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
                GenerateTypeFacadeMember(builder, member, facadeHandleTypes);
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

    private static void GenerateTypeHandleMember(
        StringBuilder builder,
        S1InteropMemberEntry member,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes)
    {
        string memberName = ToPascalIdentifier(member.MemberName);
        FacadeMemberType memberType = GetFacadeMemberType(member.ValueTypeName, facadeHandleTypes);
        builder.AppendLine();
        builder.AppendLine($"            public {GetFacadeReturnTypeName(memberType)} {memberName} => {GenerateFacadeGetExpression(member, "value.Instance", memberType)};");
        builder.AppendLine($"            public T? Get{memberName}<T>() where T : class => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}<T>(value.Instance);");
        builder.AppendLine($"            public T? Get{memberName}Value<T>() where T : struct => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}Value<T>(value.Instance);");
        GenerateTypedTrySetHandleMember(builder, member, memberName, memberType);
        builder.AppendLine($"            public bool TrySet{memberName}(object? memberValue) => S1Interop.Generated.S1InteropMemberRegistry.TrySet{member.Alias}(value.Instance, memberValue);");
    }

    private static void GenerateTypeFacadeMember(
        StringBuilder builder,
        S1InteropMemberEntry member,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes)
    {
        string memberName = ToPascalIdentifier(member.MemberName);
        if (member.Kind == S1InteropMemberKind.Method)
        {
            if (member.IsStatic)
            {
                builder.AppendLine($"        public static object? {memberName}(params object?[] args) => S1Interop.Generated.S1InteropMemberRegistry.Invoke{member.Alias}(args);");
                builder.AppendLine($"        public static T? {memberName}<T>(params object?[] args) => S1Interop.Generated.S1InteropMemberRegistry.Invoke{member.Alias}<T>(args);");
                GenerateTypedStaticMethodFacadeMember(builder, member, memberName, facadeHandleTypes);
            }
            else
            {
                builder.AppendLine($"        public static object? {memberName}(Handle instance, params object?[] args) => S1Interop.Generated.S1InteropMemberRegistry.Invoke{member.Alias}(instance.Value.Instance, args);");
                builder.AppendLine($"        public static object? {memberName}(object? instance, params object?[] args) => S1Interop.Generated.S1InteropMemberRegistry.Invoke{member.Alias}(instance, args);");
                builder.AppendLine($"        public static T? {memberName}<T>(Handle instance, params object?[] args) => S1Interop.Generated.S1InteropMemberRegistry.Invoke{member.Alias}<T>(instance.Value.Instance, args);");
                builder.AppendLine($"        public static T? {memberName}<T>(object? instance, params object?[] args) => S1Interop.Generated.S1InteropMemberRegistry.Invoke{member.Alias}<T>(instance, args);");
                GenerateTypedInstanceMethodFacadeMember(builder, member, memberName, facadeHandleTypes);
            }

            return;
        }

        FacadeMemberType memberType = GetFacadeMemberType(member.ValueTypeName, facadeHandleTypes);
        if (member.IsStatic)
        {
            builder.AppendLine($"        public static {GetFacadeReturnTypeName(memberType)} Get{memberName}() => {GenerateFacadeGetExpression(member, instanceExpression: null, memberType)};");
            builder.AppendLine($"        public static T? Get{memberName}<T>() where T : class => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}<T>();");
            builder.AppendLine($"        public static T? Get{memberName}Value<T>() where T : struct => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}Value<T>();");
            GenerateTypedTrySetStaticFacadeMember(builder, member, memberName, memberType);
            builder.AppendLine($"        public static bool TrySet{memberName}(object? value) => S1Interop.Generated.S1InteropMemberRegistry.TrySet{member.Alias}(value);");
        }
        else
        {
            builder.AppendLine($"        public static {GetFacadeReturnTypeName(memberType)} Get{memberName}(Handle instance) => {GenerateFacadeGetExpression(member, "instance.Value.Instance", memberType)};");
            builder.AppendLine($"        public static {GetFacadeReturnTypeName(memberType)} Get{memberName}(object instance) => {GenerateFacadeGetExpression(member, "instance", memberType)};");
            builder.AppendLine($"        public static T? Get{memberName}<T>(Handle instance) where T : class => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}<T>(instance.Value.Instance);");
            builder.AppendLine($"        public static T? Get{memberName}<T>(object instance) where T : class => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}<T>(instance);");
            builder.AppendLine($"        public static T? Get{memberName}Value<T>(Handle instance) where T : struct => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}Value<T>(instance.Value.Instance);");
            builder.AppendLine($"        public static T? Get{memberName}Value<T>(object instance) where T : struct => S1Interop.Generated.S1InteropMemberRegistry.Get{member.Alias}Value<T>(instance);");
            GenerateTypedTrySetInstanceFacadeMember(builder, member, memberName, memberType);
            builder.AppendLine($"        public static bool TrySet{memberName}(Handle instance, object? value) => S1Interop.Generated.S1InteropMemberRegistry.TrySet{member.Alias}(instance.Value.Instance, value);");
            builder.AppendLine($"        public static bool TrySet{memberName}(object instance, object? value) => S1Interop.Generated.S1InteropMemberRegistry.TrySet{member.Alias}(instance, value);");
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

        builder.AppendLine($"            public bool TrySet{memberName}({GetFacadeParameterTypeName(memberType)} memberValue) => S1Interop.Generated.S1InteropMemberRegistry.TrySet{member.Alias}(value.Instance, {GetFacadeArgumentExpression("memberValue", memberType)});");
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
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes)
    {
        if (!TryGetTypedMethodReturnType(member, facadeHandleTypes, out FacadeMemberType returnType) ||
            !TryGetTypedMethodParameters(member, facadeHandleTypes, out string? parameters))
        {
            return;
        }

        builder.AppendLine($"        public static {GetFacadeReturnTypeName(returnType)} {memberName}({parameters}){GenerateTypedMethodInvocation(member, receiverExpression: null, returnType, facadeHandleTypes)}");
    }

    private static void GenerateTypedInstanceMethodFacadeMember(
        StringBuilder builder,
        S1InteropMemberEntry member,
        string memberName,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes)
    {
        if (!TryGetTypedMethodReturnType(member, facadeHandleTypes, out FacadeMemberType returnType) ||
            !TryGetTypedMethodParameters(member, facadeHandleTypes, out string? parameters))
        {
            return;
        }

        string parameterPrefix = string.IsNullOrEmpty(parameters) ? string.Empty : ", " + parameters;
        builder.AppendLine($"        public static {GetFacadeReturnTypeName(returnType)} {memberName}(Handle instance{parameterPrefix}){GenerateTypedMethodInvocation(member, "instance.Value.Instance", returnType, facadeHandleTypes)}");
        builder.AppendLine($"        public static {GetFacadeReturnTypeName(returnType)} {memberName}(object? instance{parameterPrefix}){GenerateTypedMethodInvocation(member, "instance", returnType, facadeHandleTypes)}");
    }

    private static void GenerateTypedConstructorMembers(
        StringBuilder builder,
        IEnumerable<S1InteropConstructorEntry> constructors,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes)
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

            if (!TryGetTypedCallableParameters(constructor.ParameterTypeNames, constructor.ParameterNames, facadeHandleTypes, out string? parameters))
            {
                continue;
            }

            string argumentList = GenerateFacadeArgumentList(constructor.ParameterTypeNames, constructor.ParameterNames, facadeHandleTypes);
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
        out FacadeMemberType returnType)
    {
        returnType = GetFacadeMemberType(member.ValueTypeName, facadeHandleTypes);
        return returnType.HasConcreteType;
    }

    private static bool TryGetTypedMethodParameters(
        S1InteropMemberEntry member,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes,
        out string? parameters)
    {
        parameters = null;
        return TryGetTypedCallableParameters(member.ParameterTypeNames, member.ParameterNames, facadeHandleTypes, out parameters);
    }

    private static bool TryGetTypedCallableParameters(
        ImmutableArray<string> parameterTypeNames,
        ImmutableArray<string> parameterNames,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes,
        out string? parameters)
    {
        parameters = null;
        if (parameterTypeNames.Length != parameterNames.Length)
        {
            return false;
        }

        var generatedParameters = new List<string>(parameterTypeNames.Length);
        for (int index = 0; index < parameterTypeNames.Length; index++)
        {
            FacadeMemberType type = GetFacadeParameterType(parameterTypeNames[index], facadeHandleTypes);
            if (!type.HasConcreteType)
            {
                return false;
            }

            generatedParameters.Add($"{GetFacadeParameterTypeName(type)} {parameterNames[index]}");
        }

        parameters = string.Join(", ", generatedParameters);
        return true;
    }

    private static string GenerateTypedMethodInvocation(
        S1InteropMemberEntry member,
        string? receiverExpression,
        FacadeMemberType returnType,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes)
    {
        string argumentList = GenerateFacadeArgumentList(member.ParameterTypeNames, member.ParameterNames, facadeHandleTypes);
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
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes)
    {
        if (parameterNames.IsDefaultOrEmpty)
        {
            return string.Empty;
        }

        var arguments = new List<string>(parameterNames.Length);
        for (int index = 0; index < parameterTypeNames.Length; index++)
        {
            arguments.Add(GenerateFacadeArgumentExpression(parameterNames[index], parameterTypeNames[index], facadeHandleTypes));
        }

        return string.Join(", ", arguments);
    }

    private static string GenerateFacadeArgumentExpression(
        string parameterName,
        string parameterTypeName,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes) =>
        GetFacadeParameterType(parameterTypeName, facadeHandleTypes).IsHandle
            ? parameterName + ".Value.Instance"
            : parameterName;

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
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes)
    {
        FacadeMemberType parameterType = GetFacadeParameterType(typeName, facadeHandleTypes);
        return parameterType.HasConcreteType
            ? parameterType
            : string.Equals(StripNullableAnnotation(typeName), "void", StringComparison.Ordinal)
                ? FacadeMemberType.Void()
                : default;
    }

    private static FacadeMemberType GetFacadeParameterType(
        string? typeName,
        IReadOnlyDictionary<string, FacadeHandleType> facadeHandleTypes)
    {
        string? normalized = StripNullableAnnotation(typeName);
        if (normalized is null)
        {
            return default;
        }

        if (facadeHandleTypes.TryGetValue(NormalizeBackendNeutralTypeName(normalized), out FacadeHandleType handleType))
        {
            return FacadeMemberType.Handle(handleType);
        }

        return normalized is "bool" or "byte" or "char" or "decimal" or "double" or "float" or "int" or "long" or "object" or "sbyte" or "short" or "string" or "uint" or "ulong" or "ushort"
            ? FacadeMemberType.Scalar(normalized)
            : default;
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
            : memberType.ScalarTypeName + "?";
    }

    private static string GetFacadeParameterTypeName(FacadeMemberType memberType)
    {
        if (memberType.IsHandle)
        {
            return memberType.HandleType.GetValueOrDefault().QualifiedHandleName;
        }

        return IsReferenceFacadeType(memberType.ScalarTypeName!)
            ? memberType.ScalarTypeName + "?"
            : memberType.ScalarTypeName!;
    }

    private static bool IsReferenceFacadeType(string memberTypeName) =>
        memberTypeName is "object" or "string";

    private static string GetGenericAccessSuffix(FacadeMemberType memberType) =>
        IsReferenceFacadeType(memberType.ScalarTypeName!)
            ? $"<{memberType.ScalarTypeName}>"
            : $"Value<{memberType.ScalarTypeName}>";

    private static string GetGenericInvocationSuffix(FacadeMemberType memberType) =>
        memberType.IsHandle || memberType.IsObject
            ? string.Empty
            : $"<{memberType.ScalarTypeName}>";

    private static string GetFacadeArgumentExpression(string argumentName, FacadeMemberType argumentType) =>
        argumentType.IsHandle ? argumentName + ".Value.Instance" : argumentName;

    private static IReadOnlyDictionary<string, FacadeHandleType> CreateFacadeHandleTypeLookup(ImmutableArray<S1InteropTypeEntry> entries)
    {
        var lookup = new Dictionary<string, FacadeHandleType>(StringComparer.Ordinal);
        foreach (S1InteropTypeEntry entry in entries)
        {
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

    private static string NormalizeBackendNeutralTypeName(string typeName) =>
        typeName
            .Replace("Il2CppScheduleOne.", "ScheduleOne.")
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
        private FacadeMemberType(string? scalarTypeName, FacadeHandleType? handleType, bool isVoid)
        {
            ScalarTypeName = scalarTypeName;
            HandleType = handleType;
            IsVoid = isVoid;
        }

        public string? ScalarTypeName { get; }

        public FacadeHandleType? HandleType { get; }

        public bool IsVoid { get; }

        public bool HasConcreteType => ScalarTypeName is not null || HandleType.HasValue || IsVoid;

        public bool IsHandle => HandleType.HasValue;

        public bool IsObject => string.Equals(ScalarTypeName, "object", StringComparison.Ordinal);

        public static FacadeMemberType Scalar(string typeName) => new(typeName, null, isVoid: false);

        public static FacadeMemberType Handle(FacadeHandleType handleType) => new(null, handleType, isVoid: false);

        public static FacadeMemberType Void() => new(null, null, isVoid: true);
    }
}
