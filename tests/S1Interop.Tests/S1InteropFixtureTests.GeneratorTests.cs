internal sealed partial class S1InteropFixtureTests
{
    private void ScheduleOneUsingRewriterGroupsAdjacentUsings()
    {
        string source =
            """
            using HarmonyLib;
            using ScheduleOne.DevUtilities;
            using ScheduleOne.Equipping;
            using PlayerAlias = ScheduleOne.PlayerScripts;
            using static ScheduleOne.UI.HUD;
            using UnityEngine;

            namespace SyntheticMod;
            """;

        string rewritten = ScheduleOneUsingRewriter.RewriteSource(source);

        Assert(CountOccurrences(rewritten, "#if MONO") == 1, "Adjacent ScheduleOne usings should share one MONO guard.");
        Assert(CountOccurrences(rewritten, "#elif IL2CPP") == 1, "Adjacent ScheduleOne usings should share one IL2CPP guard.");
        Assert(CountOccurrences(rewritten, "#endif") == 1, "Adjacent ScheduleOne usings should share one closing guard.");
        Assert(
            rewritten.Contains(
                """
                #if MONO
                using ScheduleOne.DevUtilities;
                using ScheduleOne.Equipping;
                using PlayerAlias = ScheduleOne.PlayerScripts;
                using static ScheduleOne.UI.HUD;
                #elif IL2CPP
                using Il2CppScheduleOne.DevUtilities;
                using Il2CppScheduleOne.Equipping;
                using PlayerAlias = Il2CppScheduleOne.PlayerScripts;
                using static Il2CppScheduleOne.UI.HUD;
                #endif
                """,
                StringComparison.Ordinal),
            $"Grouped ScheduleOne using block was not emitted as expected. Rewritten source:{Environment.NewLine}{rewritten}");
        Assert(
            rewritten.Contains("using HarmonyLib;", StringComparison.Ordinal) &&
            rewritten.Contains("using UnityEngine;", StringComparison.Ordinal),
            "Non-ScheduleOne usings should remain outside the generated runtime guard.");
    }

    private void ScheduleOneUsingRewriterCanPreferGlobalFacade()
    {
        string source =
            """
            using HarmonyLib;
            using ScheduleOne.DevUtilities;
            using ScheduleOne.Equipping;
            using PlayerAlias = ScheduleOne.PlayerScripts;
            using static ScheduleOne.UI.HUD;
            using UnityEngine;

            namespace SyntheticMod;
            """;

        string rewritten = ScheduleOneUsingRewriter.RewriteSource(
            source,
            ScheduleOneUsingRewriter.RewriteMode.PreferGlobalFacade);

        Assert(
            !rewritten.Contains("using ScheduleOne.DevUtilities;", StringComparison.Ordinal) &&
            !rewritten.Contains("using Il2CppScheduleOne.DevUtilities;", StringComparison.Ordinal) &&
            !rewritten.Contains("using ScheduleOne.Equipping;", StringComparison.Ordinal) &&
            !rewritten.Contains("using Il2CppScheduleOne.Equipping;", StringComparison.Ordinal),
            $"Normal ScheduleOne namespace usings should be removed when the global facade owns them. Rewritten source:{Environment.NewLine}{rewritten}");
        Assert(CountOccurrences(rewritten, "#if MONO") == 1, "Alias/static ScheduleOne usings should still share one MONO guard.");
        Assert(
            rewritten.Contains("using PlayerAlias = ScheduleOne.PlayerScripts;", StringComparison.Ordinal) &&
            rewritten.Contains("using PlayerAlias = Il2CppScheduleOne.PlayerScripts;", StringComparison.Ordinal) &&
            rewritten.Contains("using static ScheduleOne.UI.HUD;", StringComparison.Ordinal) &&
            rewritten.Contains("using static Il2CppScheduleOne.UI.HUD;", StringComparison.Ordinal),
            "Alias and static ScheduleOne usings should remain source-level guarded because global namespace imports cannot replace them safely.");
    }

    private void S1InteropTypeRegistryGeneratorProducesBackendSpecificReflectionCache()
    {
        const string source =
            """
            [assembly: S1Interop.S1InteropType("ScheduleOne.PlayerScripts.PlayerCamera", Alias = "PlayerCamera")]
            [assembly: S1Interop.S1InteropType("ScheduleOne.UI.Phone.Phone", Alias = "Phone", Il2CppTypeName = "Il2CppScheduleOne.UI.Phone.Phone")]
            [assembly: S1Interop.S1InteropType("ScheduleOne.NPCs.Behaviour.MoveItemBehaviour", Alias = "MoveItemBehaviour")]
            [assembly: S1Interop.S1InteropType("ScheduleOne.Management.TransitRoute", Alias = "TransitRoute")]
            [assembly: S1Interop.S1InteropType("ScheduleOne.ItemFramework.ItemInstance", Alias = "ItemInstance")]
            [assembly: S1Interop.S1InteropMember("PlayerCamera", "container", Alias = "NoticeContainer")]
            [assembly: S1Interop.S1InteropMember("PlayerCamera", "Instance", Alias = "PlayerCameraInstance", IsStatic = true)]
            [assembly: S1Interop.S1InteropMember("PlayerCamera", "_homeScreenInstance", Alias = "HomeScreenField", Kind = S1Interop.S1InteropMemberKind.Field)]
            [assembly: S1Interop.S1InteropMember("Phone", "StartUpdateVolume", Alias = "StartUpdateVolume", Kind = S1Interop.S1InteropMemberKind.Method)]
            [assembly: S1Interop.S1InteropMember("Phone", "Open", Alias = "OpenPhone", Kind = S1Interop.S1InteropMemberKind.Method, IsStatic = true)]
            [assembly: S1Interop.S1InteropMember("Phone", "deviceUniqueIdentifier", Alias = "DeviceIdProperty", Kind = S1Interop.S1InteropMemberKind.Property, IsStatic = true)]
            [assembly: S1Interop.S1InteropMember("MoveItemBehaviour", "IsDestinationValid", Alias = "IsDestinationValid", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "TransitRoute", "ItemInstance", "string&" })]
            [assembly: S1Interop.S1InteropMember("Phone", "SetPacket", Alias = "SetPacket", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "byte[]" })]
            [assembly: S1Interop.S1InteropMember("Phone", "SetLabels", Alias = "SetLabels", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "string[]" })]
            [assembly: S1Interop.S1InteropMember("Phone", "SetScores", Alias = "SetScores", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "System.Collections.Generic.Dictionary<string, int>" })]
            [assembly: S1Interop.S1InteropMember("Phone", "SetTags", Alias = "SetTags", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "System.Collections.Generic.HashSet<string>" })]

            namespace SyntheticMod
            {
                internal static class Core
                {
                }
            }
            """;

        string monoGenerated = RunTypeRegistryGenerator(source, "MONO");
        string il2CppGenerated = RunTypeRegistryGenerator(source, "IL2CPP");
        string runtimeGenerated = RunTypeRegistryGenerator(source);

        Assert(
            monoGenerated.Contains("public const S1InteropRuntimeBackend Backend = S1InteropRuntimeBackend.Mono;", StringComparison.Ordinal) &&
            monoGenerated.Contains("public const bool IsMono = true;", StringComparison.Ordinal),
            $"Mono generator output should expose Mono runtime constants. Generated source:{Environment.NewLine}{monoGenerated}");
        Assert(
            monoGenerated.Contains("public const string PlayerCameraName = \"ScheduleOne.PlayerScripts.PlayerCamera\";", StringComparison.Ordinal),
            $"Mono generator output should keep Mono ScheduleOne type names. Generated source:{Environment.NewLine}{monoGenerated}");
        Assert(
            il2CppGenerated.Contains("public const S1InteropRuntimeBackend Backend = S1InteropRuntimeBackend.Il2Cpp;", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public const bool IsIl2Cpp = true;", StringComparison.Ordinal),
            $"IL2CPP generator output should expose IL2CPP runtime constants. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("public const string PlayerCameraName = \"Il2CppScheduleOne.PlayerScripts.PlayerCamera\";", StringComparison.Ordinal),
            $"IL2CPP generator output should rewrite ScheduleOne type names to Il2CppScheduleOne. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("public const string PhoneName = \"Il2CppScheduleOne.UI.Phone.Phone\";", StringComparison.Ordinal),
            $"IL2CPP generator output should respect explicit Il2CppTypeName overrides. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("private static readonly global::System.Collections.Generic.Dictionary<string, global::System.Type?> Cache", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("global::System.Type.GetType(runtimeTypeName, throwOnError: false)", StringComparison.Ordinal),
            "Generated type registry should include a compile-time generated reflection cache.");
        Assert(
            runtimeGenerated.Contains("ResolveFromKnownGameAssemblies(runtimeTypeName)", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("global::System.Reflection.Assembly.Load(assemblyName)", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("ResolveFromAssembly(\"Assembly-CSharp\", runtimeTypeName)", StringComparison.Ordinal),
            $"Backend-neutral type registry should try known game assemblies when a type is not already loaded. Generated source:{Environment.NewLine}{runtimeGenerated}");
        Assert(
            il2CppGenerated.Contains("internal static class S1InteropObjectCast", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static bool Is<T>(object? value, out T? result) where T : class", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static T? As<T>(object? value) where T : class", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("method.MakeGenericMethod(targetType)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase", StringComparison.Ordinal),
            $"Generated type registry should include a backend-neutral object cast helper for IL2CPP TryCast<T> proxy unwrapping. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("internal static class S1InteropDelegateBridge", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static TDelegate? Convert<TDelegate>(TDelegate? listener) where TDelegate : class", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("Il2CppInterop.Runtime.DelegateSupport", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("method.MakeGenericMethod(delegateType)", StringComparison.Ordinal),
            $"Generated type registry should include a backend-neutral delegate bridge for reflected IL2CPP DelegateSupport conversion. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("ResolveFromLoadedAssemblies(runtimeTypeName)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("assembly.GetType(runtimeTypeName, throwOnError: false)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("string.Equals(type.Name, runtimeTypeName, global::System.StringComparison.Ordinal)", StringComparison.Ordinal),
            "Generated type registry should fall back to cached loaded-assembly lookup for simple generated migration type names.");
        Assert(
            il2CppGenerated.Contains("public const string NoticeContainerName = \"container\";", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static global::System.Reflection.FieldInfo? NoticeContainerFieldInfo => ResolveMember(S1InteropTypeRegistry.PlayerCameraName, NoticeContainerName, parameterTypeNames: null, S1InteropMemberKind.Field) as global::System.Reflection.FieldInfo;", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static global::System.Reflection.PropertyInfo? NoticeContainerPropertyInfo => ResolveMember(S1InteropTypeRegistry.PlayerCameraName, NoticeContainerName, parameterTypeNames: null, S1InteropMemberKind.Property) as global::System.Reflection.PropertyInfo;", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static object? GetNoticeContainer(object instance) => GetValue(S1InteropTypeRegistry.PlayerCameraName, NoticeContainerName, instance, S1InteropMemberKind.FieldOrProperty);", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static T? GetNoticeContainer<T>(object instance) where T : class => GetNoticeContainer(instance) as T;", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static T? GetNoticeContainerValue<T>(object instance) where T : struct => GetNoticeContainer(instance) is T value ? value : (T?)null;", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static bool TrySetNoticeContainer(object instance, object? value) => TrySetValue(S1InteropTypeRegistry.PlayerCameraName, NoticeContainerName, instance, value, S1InteropMemberKind.FieldOrProperty);", StringComparison.Ordinal),
            $"Generated member registry should include field/property bridge helpers. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("public const string PlayerCameraInstanceName = \"Instance\";", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static object? GetPlayerCameraInstance() => GetValue(S1InteropTypeRegistry.PlayerCameraName, PlayerCameraInstanceName, null, S1InteropMemberKind.FieldOrProperty);", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static T? GetPlayerCameraInstance<T>() where T : class => GetPlayerCameraInstance() as T;", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static T? GetPlayerCameraInstanceValue<T>() where T : struct => GetPlayerCameraInstance() is T value ? value : (T?)null;", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static bool TrySetPlayerCameraInstance(object? value) => TrySetValue(S1InteropTypeRegistry.PlayerCameraName, PlayerCameraInstanceName, null, value, S1InteropMemberKind.FieldOrProperty);", StringComparison.Ordinal),
            $"Generated member registry should include static field/property bridge helpers. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("public static object? GetHomeScreenField(object instance) => GetValue(S1InteropTypeRegistry.PlayerCameraName, HomeScreenFieldName, instance, S1InteropMemberKind.Field);", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static object? GetDeviceIdProperty() => GetValue(S1InteropTypeRegistry.PhoneName, DeviceIdPropertyName, null, S1InteropMemberKind.Property);", StringComparison.Ordinal),
            $"Generated member registry should honor exact field/property member kinds. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("public static global::System.Reflection.FieldInfo? HomeScreenFieldFieldInfo => ResolveMember(S1InteropTypeRegistry.PlayerCameraName, HomeScreenFieldName, parameterTypeNames: null, S1InteropMemberKind.Field) as global::System.Reflection.FieldInfo;", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static global::System.Reflection.PropertyInfo? DeviceIdPropertyPropertyInfo => ResolveMember(S1InteropTypeRegistry.PhoneName, DeviceIdPropertyName, parameterTypeNames: null, S1InteropMemberKind.Property) as global::System.Reflection.PropertyInfo;", StringComparison.Ordinal),
            $"Generated member registry should expose exact typed member metadata accessors. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("public const string StartUpdateVolumeName = \"StartUpdateVolume\";", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static global::System.Reflection.MethodInfo? StartUpdateVolumeMethod => ResolveMethod(S1InteropTypeRegistry.PhoneName, StartUpdateVolumeName, null);", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static object? InvokeStartUpdateVolume(object? instance, params object?[] args) => Invoke(S1InteropTypeRegistry.PhoneName, StartUpdateVolumeName, null, instance, args);", StringComparison.Ordinal),
            $"Generated member registry should include method invoker helpers. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("public const string OpenPhoneName = \"Open\";", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static object? InvokeOpenPhone(params object?[] args) => Invoke(S1InteropTypeRegistry.PhoneName, OpenPhoneName, null, null, args);", StringComparison.Ordinal),
            $"Generated member registry should include static method invoker helpers. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("public const string MoveItemBehaviourName = \"Il2CppScheduleOne.NPCs.Behaviour.MoveItemBehaviour\";", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static global::System.Reflection.MethodInfo? IsDestinationValidMethod => ResolveMethod(S1InteropTypeRegistry.MoveItemBehaviourName, IsDestinationValidName, new string[] { S1InteropTypeRegistry.TransitRouteName, S1InteropTypeRegistry.ItemInstanceName, \"string&\" });", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static object? InvokeIsDestinationValid(object? instance, params object?[] args) => Invoke(S1InteropTypeRegistry.MoveItemBehaviourName, IsDestinationValidName, new string[] { S1InteropTypeRegistry.TransitRouteName, S1InteropTypeRegistry.ItemInstanceName, \"string&\" }, instance, args);", StringComparison.Ordinal),
            $"Generated member registry should include overload-specific method invoker helpers. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("case S1InteropMemberKind.Field:", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("case S1InteropMemberKind.Property:", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("ownerType.GetProperty(memberName, AllBindings)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("ownerType.GetField(memberName, AllBindings)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("ownerType.GetMethod(memberName, AllBindings, binder: null, types: parameterTypes, modifiers: null)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("parameterType.MakeByRefType()", StringComparison.Ordinal),
            "Generated member registry should cache property, field, method overload, and by-ref lookup paths.");
        Assert(
            il2CppGenerated.Contains("public static object? GetInstanceValue(object? instance, string memberName)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("global::System.Reflection.MemberInfo? member = ResolveMemberCached(instance.GetType(), memberName, parameterTypeNames: null, kind);", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static bool TrySetInstanceValue(object? instance, string memberName, object? value, S1InteropMemberKind kind)", StringComparison.Ordinal),
            $"Generated member registry should include cached instance-type helpers for generic reflection code. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("public static bool TryConvertValue(object? value, global::System.Type targetType, out object? converted)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("TryConvertIl2CppGuid(value, conversionType, out converted)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("TryConvertIl2CppList(value, conversionType, out converted)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("TryConvertIl2CppHashSet(value, conversionType, out converted)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("TryConvertIl2CppDictionary(value, conversionType, out converted)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("TryGetDictionaryEntry(entry, out object? key, out object? entryValue)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("TryConvertIl2CppArray(value, conversionType, out converted)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("Il2CppSystem.Collections.Generic.List`1", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("Il2CppSystem.Collections.Generic.HashSet`1", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("Il2CppSystem.Collections.Generic.Dictionary`2", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("property.SetValue(instance, converted, null);", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("field.SetValue(instance, converted);", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("converted = global::System.Convert.ChangeType(value, conversionType, global::System.Globalization.CultureInfo.InvariantCulture)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("global::System.Enum.Parse(conversionType, text, ignoreCase: true)", StringComparison.Ordinal),
            $"Generated member registry should centralize value conversion before field/property writes. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("if (!TryConvertArguments(method.GetParameters(), args, out object?[] converted))", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("object? result = method.Invoke(instance, converted);", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("CopyByRefArguments(method.GetParameters(), converted, args);", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("global::System.Type conversionType = parameterType.IsByRef && parameterType.GetElementType() is global::System.Type elementType", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("args[index] = ConvertBackValue(args[index], converted[index]);", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("private static bool TryConvertBackGuid(object converted, out global::System.Guid guid)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("private static bool TryConvertBackArray(global::System.Array original, object converted, out global::System.Array? managedArray)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("private static bool TryConvertBackList(global::System.Collections.IList original, object converted, out global::System.Collections.IList? managedList)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("private static bool TryConvertBackDictionary(object? original, object converted, out object? managedDictionary)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("private static bool TryConvertBackHashSet(object? original, object converted, out object? managedHashSet)", StringComparison.Ordinal),
            $"Generated member registry should convert method invocation arguments and copy by-ref values back after reflection Invoke. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            il2CppGenerated.Contains("public static object? InvokeInstance(object? instance, string memberName, params object?[] args)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("public static object? InvokeInstance(object? instance, string memberName, string[]? parameterTypeNames, params object?[] args)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("ResolveMemberCached(instance.GetType(), memberName, parameterTypeNames, S1InteropMemberKind.Method) as global::System.Reflection.MethodInfo", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("private static global::System.Reflection.MemberInfo? ResolveMemberCached(global::System.Type ownerType, string memberName, string[]? parameterTypeNames, S1InteropMemberKind kind)", StringComparison.Ordinal) &&
            il2CppGenerated.Contains("string ownerKey = ownerType.AssemblyQualifiedName ?? ownerType.FullName ?? ownerType.Name;", StringComparison.Ordinal),
            $"Generated member registry should include cached dynamic instance method invocation helpers for backend-neutral reflection wrappers. Generated source:{Environment.NewLine}{il2CppGenerated}");
        Assert(
            runtimeGenerated.Contains("public static object? CreatePlayerCamera(params object?[] args) => Create(PlayerCameraName, args);", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static T? CreatePlayerCamera<T>(params object?[] args) where T : class => CreatePlayerCamera(args) as T;", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static object? GetPlayerCameraStatic(string memberName) => S1InteropMemberRegistry.GetValue(PlayerCameraName, memberName, null);", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static bool TrySetPlayerCameraStatic(string memberName, object? value) => S1InteropMemberRegistry.TrySetValue(PlayerCameraName, memberName, null, value);", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static object? InvokePlayerCameraStatic(string methodName, params object?[] args) => S1InteropMemberRegistry.Invoke(PlayerCameraName, methodName, parameterTypeNames: null, null, args);", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static T? InvokePlayerCameraStatic<T>(string methodName, params object?[] args) => S1InteropMemberRegistry.CastResult<T>(InvokePlayerCameraStatic(methodName, args));", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static object? InvokePlayerCameraStatic(string methodName, string[]? parameterTypeNames, params object?[] args) => S1InteropMemberRegistry.Invoke(PlayerCameraName, methodName, parameterTypeNames, null, args);", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static T? InvokePlayerCameraStatic<T>(string methodName, string[]? parameterTypeNames, params object?[] args) => S1InteropMemberRegistry.CastResult<T>(InvokePlayerCameraStatic(methodName, parameterTypeNames, args));", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static bool IsPlayerCamera(object? instance) => IsInstance(instance, PlayerCameraName);", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static object? GetPlayerCamera(object? instance, string memberName) => S1InteropMemberRegistry.GetInstanceValue(instance, memberName);", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static bool TrySetPlayerCamera(object? instance, string memberName, object? value) => S1InteropMemberRegistry.TrySetInstanceValue(instance, memberName, value);", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static object? InvokePlayerCamera(object? instance, string methodName, params object?[] args) => S1InteropMemberRegistry.InvokeInstance(instance, methodName, args);", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static T? InvokePlayerCamera<T>(object? instance, string methodName, params object?[] args) => S1InteropMemberRegistry.CastResult<T>(InvokePlayerCamera(instance, methodName, args));", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static object? InvokePlayerCamera(object? instance, string methodName, string[]? parameterTypeNames, params object?[] args) => S1InteropMemberRegistry.InvokeInstance(instance, methodName, parameterTypeNames, args);", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static T? InvokePlayerCamera<T>(object? instance, string methodName, string[]? parameterTypeNames, params object?[] args) => S1InteropMemberRegistry.CastResult<T>(InvokePlayerCamera(instance, methodName, parameterTypeNames, args));", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static object? Create(string runtimeTypeName, params object?[] args)", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static bool IsInstance(object? instance, string runtimeTypeName)", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("constructor.Invoke(converted)", StringComparison.Ordinal),
            $"Backend-neutral type registry should emit object-based type facade helpers that do not require compiling against backend-specific types. Generated source:{Environment.NewLine}{runtimeGenerated}");
        Assert(
            runtimeGenerated.Contains("public static S1InteropRuntimeBackend Backend", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("cachedBackend is null || cachedBackend == S1InteropRuntimeBackend.Unknown", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static bool IsMono => Backend == S1InteropRuntimeBackend.Mono;", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static bool IsIl2Cpp => Backend == S1InteropRuntimeBackend.Il2Cpp;", StringComparison.Ordinal),
            $"Backend-neutral generator output should detect and cache the runtime backend. Generated source:{Environment.NewLine}{runtimeGenerated}");
        Assert(
            runtimeGenerated.Contains("public const string PlayerCameraMonoName = \"ScheduleOne.PlayerScripts.PlayerCamera\";", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public const string PlayerCameraIl2CppName = \"Il2CppScheduleOne.PlayerScripts.PlayerCamera\";", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static string PlayerCameraName => GetRuntimeTypeName(PlayerCameraMonoName, PlayerCameraIl2CppName);", StringComparison.Ordinal),
            $"Backend-neutral generator output should keep both backend type names and resolve the active one at runtime. Generated source:{Environment.NewLine}{runtimeGenerated}");
        Assert(
            runtimeGenerated.Contains("if (S1InteropTypeRegistry.Resolve(\"Il2CppScheduleOne.PlayerScripts.PlayerCamera\") is not null)", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("if (S1InteropTypeRegistry.Resolve(\"ScheduleOne.PlayerScripts.PlayerCamera\") is not null)", StringComparison.Ordinal),
            $"Backend-neutral generator output should probe known IL2CPP and Mono types. Generated source:{Environment.NewLine}{runtimeGenerated}");
        Assert(
            runtimeGenerated.Contains("public static string GetRuntimeTypeName(string monoTypeName, string il2CppTypeName)", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("S1InteropRuntime.Backend == S1InteropRuntimeBackend.Il2Cpp ? il2CppTypeName : monoTypeName", StringComparison.Ordinal),
            $"Backend-neutral generator output should expose runtime type-name selection for method parameter caches. Generated source:{Environment.NewLine}{runtimeGenerated}");
        Assert(
            runtimeGenerated.Contains("public static global::System.Reflection.MethodInfo? IsDestinationValidMethod => ResolveMethod(S1InteropTypeRegistry.MoveItemBehaviourName, IsDestinationValidName, new string[] { S1InteropTypeRegistry.TransitRouteName, S1InteropTypeRegistry.ItemInstanceName, \"string&\" });", StringComparison.Ordinal),
            $"Backend-neutral member registry should route alias parameter types through runtime-resolved names. Generated source:{Environment.NewLine}{runtimeGenerated}");
        Assert(
            runtimeGenerated.Contains("public static T? InvokeIsDestinationValid<T>(object? instance, params object?[] args) => CastResult<T>(InvokeIsDestinationValid(instance, args));", StringComparison.Ordinal) &&
            runtimeGenerated.Contains("public static T? CastResult<T>(object? value)", StringComparison.Ordinal),
            $"Backend-neutral member registry should expose typed method invocation helpers for new backend-neutral projects. Generated source:{Environment.NewLine}{runtimeGenerated}");
        Assert(
            runtimeGenerated.Contains("public static global::System.Reflection.MethodInfo? SetPacketMethod => ResolveMethod(S1InteropTypeRegistry.PhoneName, SetPacketName, new string[] { S1InteropTypeRegistry.GetRuntimeTypeName(\"byte[]\", \"Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>\") });", StringComparison.Ordinal),
            $"Backend-neutral member registry should route managed array parameter names to IL2CPP array wrappers at runtime. Generated source:{Environment.NewLine}{runtimeGenerated}");
        Assert(
            runtimeGenerated.Contains("public static global::System.Reflection.MethodInfo? SetLabelsMethod => ResolveMethod(S1InteropTypeRegistry.PhoneName, SetLabelsName, new string[] { S1InteropTypeRegistry.GetRuntimeTypeName(\"string[]\", \"Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<string>\") });", StringComparison.Ordinal),
            $"Backend-neutral member registry should route managed reference-array parameter names to IL2CPP reference array wrappers at runtime. Generated source:{Environment.NewLine}{runtimeGenerated}");
        Assert(
            runtimeGenerated.Contains("public static global::System.Reflection.MethodInfo? SetScoresMethod => ResolveMethod(S1InteropTypeRegistry.PhoneName, SetScoresName, new string[] { S1InteropTypeRegistry.GetRuntimeTypeName(\"System.Collections.Generic.Dictionary<string, int>\", \"Il2CppSystem.Collections.Generic.Dictionary<string, int>\") });", StringComparison.Ordinal),
            $"Backend-neutral member registry should route managed dictionary parameter names to IL2CPP dictionary wrappers at runtime. Generated source:{Environment.NewLine}{runtimeGenerated}");
        Assert(
            runtimeGenerated.Contains("public static global::System.Reflection.MethodInfo? SetTagsMethod => ResolveMethod(S1InteropTypeRegistry.PhoneName, SetTagsName, new string[] { S1InteropTypeRegistry.GetRuntimeTypeName(\"System.Collections.Generic.HashSet<string>\", \"Il2CppSystem.Collections.Generic.HashSet<string>\") });", StringComparison.Ordinal),
            $"Backend-neutral member registry should route managed hash set parameter names to IL2CPP hash set wrappers at runtime. Generated source:{Environment.NewLine}{runtimeGenerated}");
    }

    private void S1InteropTypeRegistryGeneratorDiscoversPublicTypeMembers()
    {
        MetadataReference monoGameReference = CreateMetadataReferenceFromSource(
            "Assembly-CSharp",
            """
            namespace ScheduleOne.Vehicles
            {
                public sealed class LandVehicle
                {
                    public string vehicleName = "";
                    public float CurrentThrottle { get; set; }
                    public int HealthBackingField;
                    public static LandVehicle? Instance { get; set; }
                    internal string InternalName = "";
                    public string this[int index] => index.ToString();
                    public const string StableKind = "vehicle";
                    public string StartEngine()
                    {
                        return "started";
                    }

                    public string AssignDriver(ScheduleOne.PlayerScripts.Player player)
                    {
                        return player.Name;
                    }

                    public string Rename(string name)
                    {
                        return name;
                    }

                    public void StopEngine()
                    {
                    }

                    public static int ClampSpeed(int value)
                    {
                        return value;
                    }

                    public void Overloaded()
                    {
                    }

                    public void Overloaded(int value)
                    {
                    }

                    public void GenericMethod<T>()
                    {
                    }
                }
            }

            namespace ScheduleOne.PlayerScripts
            {
                public sealed class Player
                {
                    public string Name = "";
                }
            }
            """);
        MetadataReference il2CppGameReference = CreateMetadataReferenceFromSource(
            "Il2CppAssembly-CSharp",
            """
            namespace Il2CppScheduleOne.Vehicles
            {
                public sealed class LandVehicle
                {
                    public string vehicleName { get; set; } = "";
                    public float CurrentThrottle;
                    public int HealthBackingField;
                    public static LandVehicle? Instance { get; set; }
                    public string Il2CppOnly { get; set; } = "";
                    public string StartEngine()
                    {
                        return "started";
                    }

                    public string AssignDriver(Il2CppScheduleOne.PlayerScripts.Player player)
                    {
                        return player.Name;
                    }

                    public string Rename(string name)
                    {
                        return name;
                    }

                    public void StopEngine()
                    {
                    }

                    public static int ClampSpeed(int value)
                    {
                        return value;
                    }

                    public void Overloaded()
                    {
                    }

                    public void Overloaded(int value)
                    {
                    }

                    public void GenericMethod<T>()
                    {
                    }

                    public void Il2CppOnlyMethod()
                    {
                    }
                }
            }

            namespace Il2CppScheduleOne.PlayerScripts
            {
                public sealed class Player
                {
                    public string Name = "";
                }
            }
            """);
        const string source =
            """
            [assembly: S1Interop.S1InteropType("ScheduleOne.Vehicles.LandVehicle", Alias = "LandVehicle")]
            [assembly: S1Interop.S1InteropType("FishNet.Runtime.NetworkManager", Alias = "NetworkManager")]

            namespace SyntheticMod
            {
                internal static class Core
                {
                }
            }
            """;

        string generated = RunTypeRegistryGenerator(
            source,
            [monoGameReference, il2CppGameReference],
            "IL2CPP");

        Assert(
            generated.Contains("public const string LandVehicleVehicleNameName = \"vehicleName\";", StringComparison.Ordinal) &&
            generated.Contains("public const string LandVehicleCurrentThrottleName = \"CurrentThrottle\";", StringComparison.Ordinal) &&
            generated.Contains("public const string LandVehicleInstanceName = \"Instance\";", StringComparison.Ordinal),
            $"Declaring an interop type should discover compatible public fields and properties. Generated source:{Environment.NewLine}{generated}");
        Assert(
            generated.Contains("public static string? GetVehicleName(Handle instance)", StringComparison.Ordinal) &&
            generated.Contains("public static float? GetCurrentThrottle(Handle instance)", StringComparison.Ordinal) &&
            generated.Contains("public static object? GetInstance()", StringComparison.Ordinal) &&
            generated.Contains("public static object? StartEngine(Handle instance, params object?[] args)", StringComparison.Ordinal) &&
            generated.Contains("public static object? AssignDriver(Handle instance, params object?[] args)", StringComparison.Ordinal),
            $"Type facades should expose discovered members and unambiguous public methods without explicit S1InteropMember declarations. Generated source:{Environment.NewLine}{generated}");
        Assert(
            generated.Contains("namespace S1Interop.ScheduleOne.Vehicles", StringComparison.Ordinal) &&
            !generated.Contains("namespace S1Interop.Vehicles", StringComparison.Ordinal),
            $"ScheduleOne type facades should emit only the canonical root-preserving namespace. Generated source:{Environment.NewLine}{generated}");
        Assert(
            generated.Contains("namespace S1Interop.FishNet.Runtime", StringComparison.Ordinal) &&
            !generated.Contains("namespace S1Interop.Types.FishNet.Runtime", StringComparison.Ordinal),
            $"Non-ScheduleOne type facades should also preserve their original root under S1Interop. Generated source:{Environment.NewLine}{generated}");
        Assert(
            generated.Contains("public const string LandVehicleAssignDriverName = \"AssignDriver\";", StringComparison.Ordinal) &&
            generated.Contains("public static global::System.Reflection.MethodInfo? LandVehicleAssignDriverMethod => ResolveMethod(S1InteropTypeRegistry.LandVehicleName, LandVehicleAssignDriverName, new string[] { \"Il2CppScheduleOne.PlayerScripts.Player\" });", StringComparison.Ordinal),
            $"Discovered methods should preserve parameter-specific lookup and runtime type-name conversion. Generated source:{Environment.NewLine}{generated}");
        Assert(
            generated.Contains("public string? VehicleName => S1Interop.Generated.S1InteropMemberRegistry.GetLandVehicleVehicleName<string>(value.Instance);", StringComparison.Ordinal) &&
            generated.Contains("public float? CurrentThrottle => S1Interop.Generated.S1InteropMemberRegistry.GetLandVehicleCurrentThrottleValue<float>(value.Instance);", StringComparison.Ordinal) &&
            generated.Contains("public T? GetVehicleName<T>() where T : class => S1Interop.Generated.S1InteropMemberRegistry.GetLandVehicleVehicleName<T>(value.Instance);", StringComparison.Ordinal) &&
            generated.Contains("public T? GetCurrentThrottleValue<T>() where T : struct => S1Interop.Generated.S1InteropMemberRegistry.GetLandVehicleCurrentThrottleValue<T>(value.Instance);", StringComparison.Ordinal) &&
            generated.Contains("public bool TrySetVehicleName(string? memberValue) => S1Interop.Generated.S1InteropMemberRegistry.TrySetLandVehicleVehicleName(value.Instance, memberValue);", StringComparison.Ordinal) &&
            generated.Contains("public bool TrySetCurrentThrottle(float memberValue) => S1Interop.Generated.S1InteropMemberRegistry.TrySetLandVehicleCurrentThrottle(value.Instance, memberValue);", StringComparison.Ordinal) &&
            generated.Contains("public bool TrySetCurrentThrottle(object? memberValue) => S1Interop.Generated.S1InteropMemberRegistry.TrySetLandVehicleCurrentThrottle(value.Instance, memberValue);", StringComparison.Ordinal),
            $"Type facade handles should expose native-like instance accessors for discovered field/property members. Generated source:{Environment.NewLine}{generated}");
        Assert(
            generated.Contains("public static string? StartEngine(Handle instance) => S1Interop.Generated.S1InteropMemberRegistry.InvokeLandVehicleStartEngine<string>(instance.Value.Instance);", StringComparison.Ordinal) &&
            generated.Contains("public static string? Rename(Handle instance, string? arg0) => S1Interop.Generated.S1InteropMemberRegistry.InvokeLandVehicleRename<string>(instance.Value.Instance, arg0);", StringComparison.Ordinal) &&
            generated.Contains("public static void StopEngine(Handle instance) => S1Interop.Generated.S1InteropMemberRegistry.InvokeLandVehicleStopEngine(instance.Value.Instance);", StringComparison.Ordinal) &&
            generated.Contains("public static int? ClampSpeed(int arg0) => S1Interop.Generated.S1InteropMemberRegistry.InvokeLandVehicleClampSpeed<int>(arg0);", StringComparison.Ordinal),
            $"Type facades should add typed method overloads when return and parameter metadata are backend-neutral. Generated source:{Environment.NewLine}{generated}");
        Assert(
            !generated.Contains("public object? Instance => S1Interop.Generated.S1InteropMemberRegistry.GetInstance(value.Instance);", StringComparison.Ordinal),
            $"Type facade handles should not generate accessors that collide with built-in handle members. Generated source:{Environment.NewLine}{generated}");
        Assert(
            !generated.Contains("Il2CppOnlyName", StringComparison.Ordinal) &&
            !generated.Contains("HealthBackingFieldName", StringComparison.Ordinal) &&
            !generated.Contains("InternalNameName", StringComparison.Ordinal) &&
            !generated.Contains("StableKindName", StringComparison.Ordinal) &&
            !generated.Contains("ItemName", StringComparison.Ordinal) &&
            !generated.Contains("OverloadedName", StringComparison.Ordinal) &&
            !generated.Contains("GenericMethodName", StringComparison.Ordinal) &&
            !generated.Contains("Il2CppOnlyMethodName", StringComparison.Ordinal),
            $"Discovered member facades should skip one-sided, non-public, const, indexer, overloaded, and generic members. Generated source:{Environment.NewLine}{generated}");
    }

    private void S1InteropTypeRegistryGeneratorExpandsNamespaceDeclarations()
    {
        MetadataReference monoGameReference = CreateMetadataReferenceFromSource(
            "NamespaceExpansionMonoGame",
            """
            namespace ScheduleOne.Vehicles
            {
                public sealed class LandVehicle
                {
                    public string vehicleName = "";
                }

                public sealed class Wheel
                {
                }
            }

            namespace ScheduleOne.GameTime
            {
                public sealed class TimeManager
                {
                }
            }

            namespace ScheduleOne.Internal
            {
                internal sealed class HiddenType
                {
                }
            }
            """);
        MetadataReference il2CppGameReference = CreateMetadataReferenceFromSource(
            "NamespaceExpansionIl2CppGame",
            """
            namespace Il2CppScheduleOne.Vehicles
            {
                public sealed class LandVehicle
                {
                    public string vehicleName = "";
                }

                public sealed class Wheel
                {
                }
            }

            namespace Il2CppScheduleOne.GameTime
            {
                public sealed class TimeManager
                {
                }
            }
            """);
        const string source =
            """
            [assembly: S1Interop.S1InteropNamespace("ScheduleOne", IncludeSubnamespaces = true)]

            namespace SyntheticMod
            {
                internal static class Core
                {
                }
            }
            """;

        string generated = RunTypeRegistryGenerator(
            source,
            [monoGameReference, il2CppGameReference],
            "IL2CPP");

        Assert(
            generated.Contains("public const string LandVehicleName = \"Il2CppScheduleOne.Vehicles.LandVehicle\";", StringComparison.Ordinal) &&
            generated.Contains("public const string TimeManagerName = \"Il2CppScheduleOne.GameTime.TimeManager\";", StringComparison.Ordinal) &&
            generated.Contains("public const string WheelName = \"Il2CppScheduleOne.Vehicles.Wheel\";", StringComparison.Ordinal),
            $"Namespace declarations should expand public referenced ScheduleOne types into registry entries. Generated source:{Environment.NewLine}{generated}");
        Assert(
            generated.Contains("namespace S1Interop.ScheduleOne.Vehicles", StringComparison.Ordinal) &&
            generated.Contains("public readonly struct Handle", StringComparison.Ordinal),
            $"Namespace-expanded types should still emit root-preserving facades. Generated source:{Environment.NewLine}{generated}");
        Assert(
            !generated.Contains("HiddenType", StringComparison.Ordinal),
            $"Namespace declarations should skip non-public reference types. Generated source:{Environment.NewLine}{generated}");
    }

    private void S1InteropTypeRegistryGeneratorOwnerQualifiesDiscoveredMemberAliases()
    {
        MetadataReference monoGameReference = CreateMetadataReferenceFromSource(
            "Assembly-CSharp",
            """
            namespace ScheduleOne.First
            {
                public sealed class Thing
                {
                    public int Shared;
                    public void Update()
                    {
                    }
                }
            }

            namespace ScheduleOne.Second
            {
                public sealed class Thing
                {
                    public int Shared;
                    public void Update()
                    {
                    }
                }
            }
            """);
        MetadataReference il2CppGameReference = CreateMetadataReferenceFromSource(
            "Il2CppAssembly-CSharp",
            """
            namespace Il2CppScheduleOne.First
            {
                public sealed class Thing
                {
                    public int Shared;
                    public void Update()
                    {
                    }
                }
            }

            namespace Il2CppScheduleOne.Second
            {
                public sealed class Thing
                {
                    public int Shared;
                    public void Update()
                    {
                    }
                }
            }
            """);
        const string source =
            """
            [assembly: S1Interop.S1InteropType("ScheduleOne.First.Thing", Alias = "FirstThing")]
            [assembly: S1Interop.S1InteropType("ScheduleOne.Second.Thing", Alias = "SecondThing")]

            namespace SyntheticMod;
            """;

        string generated = RunTypeRegistryGenerator(
            source,
            [monoGameReference, il2CppGameReference],
            "IL2CPP");

        Assert(
            generated.Contains("public const string FirstThingSharedName = \"Shared\";", StringComparison.Ordinal) &&
            generated.Contains("public const string SecondThingSharedName = \"Shared\";", StringComparison.Ordinal) &&
            generated.Contains("public const string FirstThingUpdateName = \"Update\";", StringComparison.Ordinal) &&
            generated.Contains("public const string SecondThingUpdateName = \"Update\";", StringComparison.Ordinal),
            $"Discovered member aliases should be owner-qualified so common game member names do not collide. Generated source:{Environment.NewLine}{generated}");
    }

    private void S1InteropTypeRegistryGeneratorMergesDuplicateAliases()
    {
        const string source =
            """
            [assembly: S1Interop.S1InteropType("ScheduleOne.Law.GameOffenceNotice", Alias = "GameOffenceNotice")]
            [assembly: S1Interop.S1InteropType("ScheduleOne.Law.GameOffenceNotice", Alias = "GameOffenceNotice", Il2CppTypeName = "Il2CppScheduleOne.Law.GameOffenceNotice")]

            namespace SyntheticMod
            {
                internal static class Core
                {
                }
            }
            """;

        string generated = RunTypeRegistryGenerator(source, "IL2CPP");

        Assert(
            CountOccurrences(generated, "public readonly struct GameOffenceNoticeTag") == 1 &&
            CountOccurrences(generated, "public const string GameOffenceNoticeName") == 1,
            $"Duplicate S1InteropType aliases should emit one registry helper set. Generated source:{Environment.NewLine}{generated}");
        Assert(
            generated.Contains("public const string GameOffenceNoticeName = \"Il2CppScheduleOne.Law.GameOffenceNotice\";", StringComparison.Ordinal),
            $"Merged duplicate aliases should prefer explicit Il2CppTypeName metadata. Generated source:{Environment.NewLine}{generated}");
    }

    private void S1InteropTypeRegistryGeneratorValidatesDeclaredTypesAgainstReferencedGameAssemblies()
    {
        MetadataReference monoGameReference = CreateMetadataReferenceFromSource(
            "Assembly-CSharp",
            """
            namespace ScheduleOne
            {
                public sealed class GameManager
                {
                }
            }

            namespace ScheduleOne.PlayerScripts
            {
                public sealed class PlayerCamera
                {
                    public static PlayerCamera? Instance { get; set; }
                    public object? container;
                }
            }
            """);
        const string source =
            """
            [assembly: S1Interop.S1InteropType("ScheduleOne.PlayerScripts.PlayerCamera", Alias = "PlayerCamera")]
            [assembly: S1Interop.S1InteropMember("PlayerCamera", "Instance", Alias = "PlayerCameraInstance", IsStatic = true)]

            namespace SyntheticMod;
            """;

        ImmutableArray<Diagnostic> diagnostics = RunS1InteropGeneratorDiagnostics(source, [monoGameReference]);

        Assert(
            diagnostics.All(diagnostic => diagnostic.Id != "S1I001" && diagnostic.Id != "S1I002" && diagnostic.Id != "S1I003"),
            $"Valid S1InteropType and S1InteropMember declarations should not report reference validation diagnostics. Diagnostics: {string.Join(Environment.NewLine, diagnostics)}");
    }

    private void S1InteropTypeRegistryGeneratorReportsMissingDeclaredTypesWhenGameReferencesExist()
    {
        MetadataReference monoGameReference = CreateMetadataReferenceFromSource(
            "Assembly-CSharp",
            """
            namespace ScheduleOne
            {
                public sealed class GameManager
                {
                }
            }
            """);
        const string source =
            """
            [assembly: S1Interop.S1InteropType("ScheduleOne.PlayerScripts.DoesNotExist", Alias = "MissingCamera")]

            namespace SyntheticMod;
            """;

        ImmutableArray<Diagnostic> diagnostics = RunS1InteropGeneratorDiagnostics(source, [monoGameReference]);

        Assert(
            diagnostics.Any(diagnostic =>
                diagnostic.Id == "S1I001" &&
                diagnostic.GetMessage().Contains("ScheduleOne.PlayerScripts.DoesNotExist", StringComparison.Ordinal) &&
                diagnostic.GetMessage().Contains("MissingCamera", StringComparison.Ordinal)),
            $"Missing S1InteropType declarations should report S1I001 when Mono game references are available. Diagnostics: {string.Join(Environment.NewLine, diagnostics)}");
    }

    private void S1InteropTypeRegistryGeneratorReportsMissingIl2CppDeclaredTypesWhenGameReferencesExist()
    {
        MetadataReference il2CppGameReference = CreateMetadataReferenceFromSource(
            "Il2CppAssembly-CSharp",
            """
            namespace Il2CppScheduleOne
            {
                public sealed class GameManager
                {
                }
            }
            """);
        const string source =
            """
            [assembly: S1Interop.S1InteropType("ScheduleOne.PlayerScripts.DoesNotExist", Alias = "MissingCamera")]

            namespace SyntheticMod;
            """;

        ImmutableArray<Diagnostic> diagnostics = RunS1InteropGeneratorDiagnostics(source, [il2CppGameReference]);

        Assert(
            diagnostics.Any(diagnostic =>
                diagnostic.Id == "S1I001" &&
                diagnostic.GetMessage().Contains("Il2CppScheduleOne.PlayerScripts.DoesNotExist", StringComparison.Ordinal) &&
                diagnostic.GetMessage().Contains("MissingCamera", StringComparison.Ordinal)),
            $"Missing S1InteropType declarations should report S1I001 when IL2CPP game references are available. Diagnostics: {string.Join(Environment.NewLine, diagnostics)}");
    }

    private void S1InteropTypeRegistryGeneratorSkipsReferenceValidationWhenGameReferencesAreAbsent()
    {
        const string source =
            """
            [assembly: S1Interop.S1InteropType("ScheduleOne.PlayerScripts.DoesNotExist", Alias = "MissingCamera")]

            namespace SyntheticMod;
            """;

        ImmutableArray<Diagnostic> diagnostics = RunS1InteropGeneratorDiagnostics(source, []);

        Assert(
            diagnostics.All(diagnostic => diagnostic.Id != "S1I001"),
            $"Missing S1InteropType declarations should not report S1I001 when no game reference surface is available. Diagnostics: {string.Join(Environment.NewLine, diagnostics)}");
    }

    private void S1InteropTypeRegistryGeneratorReportsMissingDeclaredMembersWhenGameReferencesExist()
    {
        MetadataReference monoGameReference = CreateMetadataReferenceFromSource(
            "Assembly-CSharp",
            """
            namespace ScheduleOne
            {
                public sealed class GameManager
                {
                }
            }

            namespace ScheduleOne.PlayerScripts
            {
                public sealed class PlayerCamera
                {
                    public static PlayerCamera? Instance { get; set; }
                }
            }
            """);
        const string source =
            """
            [assembly: S1Interop.S1InteropType("ScheduleOne.PlayerScripts.PlayerCamera", Alias = "PlayerCamera")]
            [assembly: S1Interop.S1InteropMember("PlayerCamera", "container", Alias = "NoticeContainer")]
            [assembly: S1Interop.S1InteropMember("MissingOwner", "Instance", Alias = "MissingOwnerInstance", IsStatic = true)]

            namespace SyntheticMod;
            """;

        ImmutableArray<Diagnostic> diagnostics = RunS1InteropGeneratorDiagnostics(source, [monoGameReference]);

        Assert(
            diagnostics.Any(diagnostic =>
                diagnostic.Id == "S1I002" &&
                diagnostic.GetMessage().Contains("MissingOwner", StringComparison.Ordinal)),
            $"S1InteropMember declarations should report S1I002 when their owner alias is not declared. Diagnostics: {string.Join(Environment.NewLine, diagnostics)}");
        Assert(
            diagnostics.Any(diagnostic =>
                diagnostic.Id == "S1I003" &&
                diagnostic.GetMessage().Contains("container", StringComparison.Ordinal) &&
                diagnostic.GetMessage().Contains("PlayerCamera", StringComparison.Ordinal)),
            $"S1InteropMember declarations should report S1I003 when the member name is absent from the referenced owner type. Diagnostics: {string.Join(Environment.NewLine, diagnostics)}");
    }

    private void S1InteropTypeRegistryGeneratorValidatesMethodParameterAliasesAgainstReferencedGameAssemblies()
    {
        MetadataReference monoGameReference = CreateMetadataReferenceFromSource(
            "Assembly-CSharp",
            """
            namespace ScheduleOne
            {
                public sealed class GameManager
                {
                }
            }

            namespace ScheduleOne.Management
            {
                public sealed class TransitRoute
                {
                }
            }

            namespace ScheduleOne.ItemFramework
            {
                public sealed class ItemInstance
                {
                }
            }

            namespace ScheduleOne.NPCs.Behaviour
            {
                public sealed class MoveItemBehaviour
                {
                    public bool IsDestinationValid(ScheduleOne.Management.TransitRoute route, ScheduleOne.ItemFramework.ItemInstance item, ref string reason)
                    {
                        return true;
                    }
                }
            }
            """);
        MetadataReference il2CppGameReference = CreateMetadataReferenceFromSource(
            "Il2CppAssembly-CSharp",
            """
            namespace Il2CppScheduleOne
            {
                public sealed class GameManager
                {
                }
            }

            namespace Il2CppScheduleOne.Management
            {
                public sealed class TransitRoute
                {
                }
            }

            namespace Il2CppScheduleOne.ItemFramework
            {
                public sealed class ItemInstance
                {
                }
            }

            namespace Il2CppScheduleOne.NPCs.Behaviour
            {
                public sealed class MoveItemBehaviour
                {
                    public bool IsDestinationValid(Il2CppScheduleOne.Management.TransitRoute route, Il2CppScheduleOne.ItemFramework.ItemInstance item, ref string reason)
                    {
                        return true;
                    }
                }
            }
            """);
        const string source =
            """
            [assembly: S1Interop.S1InteropType("ScheduleOne.NPCs.Behaviour.MoveItemBehaviour", Alias = "MoveItemBehaviour")]
            [assembly: S1Interop.S1InteropType("ScheduleOne.Management.TransitRoute", Alias = "TransitRoute")]
            [assembly: S1Interop.S1InteropType("ScheduleOne.ItemFramework.ItemInstance", Alias = "ItemInstance")]
            [assembly: S1Interop.S1InteropMember("MoveItemBehaviour", "IsDestinationValid", Alias = "IsDestinationValid", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "TransitRoute", "ItemInstance", "string&" })]

            namespace SyntheticMod;
            """;

        ImmutableArray<Diagnostic> diagnostics = RunS1InteropGeneratorDiagnostics(source, [monoGameReference, il2CppGameReference]);

        Assert(
            diagnostics.All(diagnostic => diagnostic.Id != "S1I001" && diagnostic.Id != "S1I002" && diagnostic.Id != "S1I003"),
            $"Alias-based S1InteropMember method parameter declarations should validate against both Mono and IL2CPP referenced signatures. Diagnostics: {string.Join(Environment.NewLine, diagnostics)}");
    }

    private void S1InteropTypeRegistryGeneratorReportsWrongMethodParameterTypesWhenGameReferencesExist()
    {
        MetadataReference monoGameReference = CreateMetadataReferenceFromSource(
            "Assembly-CSharp",
            """
            namespace ScheduleOne
            {
                public sealed class GameManager
                {
                }
            }

            namespace ScheduleOne.Management
            {
                public sealed class TransitRoute
                {
                }
            }

            namespace ScheduleOne.ItemFramework
            {
                public sealed class ItemInstance
                {
                }
            }

            namespace ScheduleOne.PlayerScripts
            {
                public sealed class PlayerCamera
                {
                }
            }

            namespace ScheduleOne.NPCs.Behaviour
            {
                public sealed class MoveItemBehaviour
                {
                    public bool IsDestinationValid(ScheduleOne.Management.TransitRoute route, ScheduleOne.ItemFramework.ItemInstance item, ref string reason)
                    {
                        return true;
                    }
                }
            }
            """);
        const string source =
            """
            [assembly: S1Interop.S1InteropType("ScheduleOne.NPCs.Behaviour.MoveItemBehaviour", Alias = "MoveItemBehaviour")]
            [assembly: S1Interop.S1InteropType("ScheduleOne.Management.TransitRoute", Alias = "TransitRoute")]
            [assembly: S1Interop.S1InteropType("ScheduleOne.PlayerScripts.PlayerCamera", Alias = "PlayerCamera")]
            [assembly: S1Interop.S1InteropMember("MoveItemBehaviour", "IsDestinationValid", Alias = "IsDestinationValid", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "TransitRoute", "PlayerCamera", "string&" })]

            namespace SyntheticMod;
            """;

        ImmutableArray<Diagnostic> diagnostics = RunS1InteropGeneratorDiagnostics(source, [monoGameReference]);

        Assert(
            diagnostics.Any(diagnostic =>
                diagnostic.Id == "S1I003" &&
                diagnostic.GetMessage().Contains("IsDestinationValid", StringComparison.Ordinal) &&
                diagnostic.GetMessage().Contains("MoveItemBehaviour", StringComparison.Ordinal)),
            $"S1InteropMember method declarations should report S1I003 when the named overload has the right arity but wrong parameter types. Diagnostics: {string.Join(Environment.NewLine, diagnostics)}");
    }

    private void S1InteropTypeRegistryGeneratorReportsIl2CppSourceBoundaryDiagnostics()
    {
        const string source =
            """
            using System;
            using System.Collections.Generic;
            using HarmonyLib;

            namespace SyntheticMod;

            public static class RiskyPatch
            {
                [HarmonyTranspiler]
                public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => instructions;

                public static void BindInternalPostfix(object __instance, List<EntityConfiguration> configs)
                {
                }

                public static void Receive()
                {
                    byte[] data = new byte[1024];
                    Steamworks.SteamNetworking.ReadP2PPacket(data, 1024, out uint bytesRead, out object remoteId, 0);
                }

                public static int Inspect(object pipelineAsset)
                {
                    return pipelineAsset is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset typedAsset
                        ? typedAsset.GetHashCode()
                        : 0;
                }
            }

            public sealed class EntityConfiguration
            {
            }

            namespace HarmonyLib
            {
                [AttributeUsage(AttributeTargets.Method)]
                public sealed class HarmonyTranspilerAttribute : Attribute
                {
                }

                public sealed class CodeInstruction
                {
                }
            }

            namespace Steamworks
            {
                public static class SteamNetworking
                {
                    public static bool ReadP2PPacket(byte[] data, uint dataSize, out uint bytesRead, out object remoteId, int channel)
                    {
                        bytesRead = 0;
                        remoteId = new object();
                        return true;
                    }
                }
            }

            namespace UnityEngine.Rendering.Universal
            {
                public sealed class UniversalRenderPipelineAsset
                {
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = RunS1InteropGeneratorDiagnostics(source, [], "IL2CPP");

        Assert(
            diagnostics.Any(diagnostic => diagnostic.Id == "S1I004" && diagnostic.GetMessage().Contains("Transpiler", StringComparison.Ordinal)),
            $"IL2CPP compiler diagnostics should reject Harmony transpilers. Diagnostics: {string.Join(Environment.NewLine, diagnostics)}");
        Assert(
            diagnostics.Any(diagnostic => diagnostic.Id == "S1I005" && diagnostic.GetMessage().Contains("configs", StringComparison.Ordinal)),
            $"IL2CPP compiler diagnostics should reject managed collection parameters in game-facing signatures. Diagnostics: {string.Join(Environment.NewLine, diagnostics)}");
        Assert(
            diagnostics.Any(diagnostic => diagnostic.Id == "S1I006" && diagnostic.GetMessage().Contains("data", StringComparison.Ordinal)),
            $"IL2CPP compiler diagnostics should reject managed byte[] at native/game buffer boundaries. Diagnostics: {string.Join(Environment.NewLine, diagnostics)}");
        Assert(
            diagnostics.Any(diagnostic =>
                diagnostic.Id == "S1I007" &&
                diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning &&
                diagnostic.GetMessage().Contains("pipelineAsset", StringComparison.Ordinal)),
            $"IL2CPP compiler diagnostics should warn on plain object/proxy casts to Unity object types. Diagnostics: {string.Join(Environment.NewLine, diagnostics)}");
    }

    private void BackendNeutralRuntimeDetectsDefaultBackendMarkersWithoutTypeAliases()
    {
        const string il2CppSource =
            """
            namespace SyntheticMod
            {
                internal static class Core
                {
                }
            }

            namespace Il2CppInterop.Runtime.InteropTypes
            {
                public class Il2CppObjectBase
                {
                }
            }
            """;
        const string monoSource =
            """
            namespace SyntheticMod
            {
                internal static class Core
                {
                }
            }

            namespace ScheduleOne
            {
                public sealed class GameManager
                {
                }
            }
            """;

        string generated = RunTypeRegistryGenerator(il2CppSource);
        Assert(
            generated.Contains("S1InteropTypeRegistry.Resolve(\"Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase\")", StringComparison.Ordinal) &&
            generated.Contains("S1InteropTypeRegistry.Resolve(\"ScheduleOne.GameManager\")", StringComparison.Ordinal) &&
            generated.Contains("S1InteropTypeRegistry.IsAssemblyLoaded(\"Il2CppInterop.Runtime\")", StringComparison.Ordinal) &&
            generated.Contains("S1InteropTypeRegistry.IsAssemblyLoaded(\"Assembly-CSharp\")", StringComparison.Ordinal),
            $"Backend-neutral runtime detection should emit default backend probes even when a new mod has no registered type aliases. Generated source:{Environment.NewLine}{generated}");

        System.Reflection.Assembly monoAssembly = CompileAndLoadS1InteropGeneratedAssembly(monoSource);
        Type monoRuntimeType = monoAssembly.GetType("S1Interop.Generated.S1InteropRuntime", throwOnError: true)!;
        object? monoBackend = monoRuntimeType.GetProperty("Backend")?.GetValue(null);
        object? isMono = monoRuntimeType.GetProperty("IsMono")?.GetValue(null);
        Assert(string.Equals(monoBackend?.ToString(), "Mono", StringComparison.Ordinal), $"Backend-neutral runtime detection should select Mono from the default ScheduleOne marker. Backend={monoBackend}");
        Assert(isMono is true, "Backend-neutral runtime IsMono should be true when the default Mono marker is loadable.");

        System.Reflection.Assembly il2CppAssembly = CompileAndLoadS1InteropGeneratedAssembly(il2CppSource);
        Type il2CppRuntimeType = il2CppAssembly.GetType("S1Interop.Generated.S1InteropRuntime", throwOnError: true)!;
        object? il2CppBackend = il2CppRuntimeType.GetProperty("Backend")?.GetValue(null);
        object? isIl2Cpp = il2CppRuntimeType.GetProperty("IsIl2Cpp")?.GetValue(null);
        Assert(string.Equals(il2CppBackend?.ToString(), "Il2Cpp", StringComparison.Ordinal), $"Backend-neutral runtime detection should select Il2Cpp from the default Il2CppInterop marker. Backend={il2CppBackend}");
        Assert(isIl2Cpp is true, "Backend-neutral runtime IsIl2Cpp should be true when the default Il2Cpp marker is loadable.");

        System.Reflection.Assembly assemblyNameOnly = CompileAndLoadS1InteropGeneratedAssembly(
            "namespace SyntheticMod { internal static class Core { } }",
            assemblyName: "Il2CppInterop.Runtime");
        Type assemblyNameOnlyRuntimeType = assemblyNameOnly.GetType("S1Interop.Generated.S1InteropRuntime", throwOnError: true)!;
        object? assemblyNameOnlyBackend = assemblyNameOnlyRuntimeType.GetProperty("Backend")?.GetValue(null);
        Assert(
            string.Equals(assemblyNameOnlyBackend?.ToString(), "Il2Cpp", StringComparison.Ordinal),
            $"Backend-neutral runtime detection should select IL2CPP from loaded assembly names when marker types are not available. Backend={assemblyNameOnlyBackend}");
    }

    private void BackendNeutralTypeRegistryExecutesAgainstIl2CppLikeTypes()
    {
        const string source =
            """
            [assembly: S1Interop.S1InteropType("ScheduleOne.UI.HUD", Alias = "Hud", Il2CppTypeName = "Il2CppScheduleOne.UI.HUD")]
            [assembly: S1Interop.S1InteropMember("Hud", "Instance", Alias = "HudInstance", IsStatic = true)]
            [assembly: S1Interop.S1InteropMember("Hud", "Scale", Alias = "HudScale", Kind = S1Interop.S1InteropMemberKind.Field)]
            [assembly: S1Interop.S1InteropMember("Hud", "SetLevel", Alias = "HudSetLevel", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "int", "string&" })]
            [assembly: S1Interop.S1InteropMember("Hud", "GetScaleText", Alias = "HudGetScaleText", Kind = S1Interop.S1InteropMemberKind.Method)]
            [assembly: S1Interop.S1InteropMember("Hud", "RewriteGuid", Alias = "HudRewriteGuid", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "System.Guid&" })]
            [assembly: S1Interop.S1InteropMember("Hud", "RewriteNames", Alias = "HudRewriteNames", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "System.Collections.Generic.List<string>&" })]
            [assembly: S1Interop.S1InteropMember("Hud", "SetData", Alias = "HudSetData", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "System.Guid", "System.Collections.Generic.List<string>" })]
            [assembly: S1Interop.S1InteropMember("Hud", "RewriteBytes", Alias = "HudRewriteBytes", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "byte[]&" })]
            [assembly: S1Interop.S1InteropMember("Hud", "SetBytes", Alias = "HudSetBytes", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "byte[]" })]
            [assembly: S1Interop.S1InteropMember("Hud", "SetLabels", Alias = "HudSetLabels", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "string[]" })]
            [assembly: S1Interop.S1InteropMember("Hud", "RewriteScores", Alias = "HudRewriteScores", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "System.Collections.Generic.Dictionary<string, int>&" })]
            [assembly: S1Interop.S1InteropMember("Hud", "RewriteTags", Alias = "HudRewriteTags", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "System.Collections.Generic.HashSet<string>&" })]
            [assembly: S1Interop.S1InteropMember("Hud", "SetScores", Alias = "HudSetScores", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "System.Collections.Generic.Dictionary<string, int>" })]
            [assembly: S1Interop.S1InteropMember("Hud", "SetTags", Alias = "HudSetTags", Kind = S1Interop.S1InteropMemberKind.Method, ParameterTypeNames = new[] { "System.Collections.Generic.HashSet<string>" })]

            namespace SyntheticMod
            {
                internal static class Core
                {
                }
            }

            namespace Il2CppScheduleOne.UI
            {
                public sealed class HUD
                {
                    public static HUD Instance { get; } = new HUD();
                    public int Scale;
                    public string? LastName { get; private set; }
                    public string? LastGuid { get; private set; }
                    public string? LastRewriteNames { get; private set; }
                    public string? LastData { get; private set; }
                    public string? LastRewriteBytes { get; private set; }
                    public string? LastBytes { get; private set; }
                    public string? LastLabels { get; private set; }
                    public string? LastScores { get; private set; }
                    public string? LastTags { get; private set; }

                    public string SetLevel(int level, ref string name)
                    {
                        Scale = level;
                        name = "il2cpp:" + name;
                        LastName = name;
                        return "done";
                    }

                    public string GetScaleText()
                    {
                        return Scale.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }

                    public string SetData(Il2CppSystem.Guid guid, Il2CppSystem.Collections.Generic.List<string> names)
                    {
                        LastData = guid.Value + ":" + names.Count + ":" + names[0];
                        return LastData;
                    }

                    public string RewriteGuid(ref Il2CppSystem.Guid guid)
                    {
                        guid = new Il2CppSystem.Guid("22222222-3333-4444-5555-666666666666");
                        LastGuid = guid.Value;
                        return LastGuid;
                    }

                    public string RewriteNames(ref Il2CppSystem.Collections.Generic.List<string> names)
                    {
                        names = new Il2CppSystem.Collections.Generic.List<string>();
                        names.Add("delta");
                        names.Add("echo");
                        LastRewriteNames = names.Count + ":" + names[0] + ":" + names[1];
                        return LastRewriteNames;
                    }

                    public string RewriteBytes(ref Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> bytes)
                    {
                        bytes = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>(3);
                        bytes[0] = 4;
                        bytes[1] = 5;
                        bytes[2] = 6;
                        LastRewriteBytes = bytes.Length + ":" + bytes[0] + ":" + bytes[1] + ":" + bytes[2];
                        return LastRewriteBytes;
                    }

                    public string SetBytes(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> bytes)
                    {
                        LastBytes = bytes.Length + ":" + bytes[0] + ":" + bytes[1];
                        return LastBytes;
                    }

                    public string SetLabels(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<string> labels)
                    {
                        LastLabels = labels.Length + ":" + labels[0] + ":" + labels[1];
                        return LastLabels;
                    }

                    public string SetScores(Il2CppSystem.Collections.Generic.Dictionary<string, int> scores)
                    {
                        LastScores = scores.Count + ":" + scores["north"] + ":" + scores["south"];
                        return LastScores;
                    }

                    public string RewriteScores(ref Il2CppSystem.Collections.Generic.Dictionary<string, int> scores)
                    {
                        scores = new Il2CppSystem.Collections.Generic.Dictionary<string, int>();
                        scores.Add("east", 12);
                        scores.Add("west", 15);
                        LastScores = scores.Count + ":" + scores["east"] + ":" + scores["west"];
                        return LastScores;
                    }

                    public string SetTags(Il2CppSystem.Collections.Generic.HashSet<string> tags)
                    {
                        LastTags = tags.Count + ":" + tags.Contains("north") + ":" + tags.Contains("south");
                        return LastTags;
                    }

                    public string RewriteTags(ref Il2CppSystem.Collections.Generic.HashSet<string> tags)
                    {
                        tags = new Il2CppSystem.Collections.Generic.HashSet<string>();
                        tags.Add("east");
                        tags.Add("west");
                        LastTags = tags.Count + ":" + tags.Contains("east") + ":" + tags.Contains("west");
                        return LastTags;
                    }
                }
            }

            namespace Il2CppSystem
            {
                public sealed class Guid
                {
                    public Guid(string value)
                    {
                        Value = value;
                    }

                    public string Value { get; }
                }

                namespace Collections.Generic
                {
                    public sealed class List<T>
                    {
                        private readonly System.Collections.Generic.List<T> inner = new System.Collections.Generic.List<T>();

                        public int Count => inner.Count;

                        public T this[int index] => inner[index];

                        public void Add(T value)
                        {
                            inner.Add(value);
                        }
                    }

                    public sealed class HashSet<T>
                    {
                        private readonly System.Collections.Generic.HashSet<T> inner = new System.Collections.Generic.HashSet<T>();

                        public int Count => inner.Count;

                        public bool Add(T value) => inner.Add(value);

                        public bool Contains(T value) => inner.Contains(value);

                        public System.Collections.Generic.HashSet<T>.Enumerator GetEnumerator() => inner.GetEnumerator();
                    }

                    public sealed class Dictionary<TKey, TValue> where TKey : notnull
                    {
                        private readonly System.Collections.Generic.Dictionary<TKey, TValue> inner = new System.Collections.Generic.Dictionary<TKey, TValue>();

                        public int Count => inner.Count;

                        public TValue this[TKey key] => inner[key];

                        public void Add(TKey key, TValue value)
                        {
                            inner.Add(key, value);
                        }

                        public System.Collections.Generic.Dictionary<TKey, TValue>.Enumerator GetEnumerator() => inner.GetEnumerator();
                    }
                }
            }

            namespace Il2CppInterop.Runtime.InteropTypes
            {
                public class Il2CppObjectBase
                {
                    private readonly object? target;

                    public Il2CppObjectBase(object? target)
                    {
                        this.target = target;
                    }

                    public T? TryCast<T>() where T : class
                    {
                        return target as T;
                    }
                }

                namespace Arrays
                {
                    public sealed class Il2CppStructArray<T> where T : struct
                    {
                        private readonly T[] inner;

                        public Il2CppStructArray(int length)
                        {
                            inner = new T[length];
                        }

                        public int Length => inner.Length;

                        public T this[int index]
                        {
                            get => inner[index];
                            set => inner[index] = value;
                        }
                    }

                    public sealed class Il2CppReferenceArray<T> where T : class
                    {
                        private readonly T?[] inner;

                        public Il2CppReferenceArray(int length)
                        {
                            inner = new T?[length];
                        }

                        public int Length => inner.Length;

                        public T? this[int index]
                        {
                            get => inner[index];
                            set => inner[index] = value;
                        }
                    }
                }
            }

            namespace Il2CppInterop.Runtime
            {
                public static class DelegateSupport
                {
                    public static bool WasCalled;

                    public static T? ConvertDelegate<T>(System.Delegate listener) where T : class
                    {
                        WasCalled = true;
                        return listener as T;
                    }
                }
            }
            """;

        System.Reflection.Assembly assembly = CompileAndLoadS1InteropGeneratedAssembly(source);
        Type runtimeType = assembly.GetType("S1Interop.Generated.S1InteropRuntime", throwOnError: true)!;
        Type typeRegistryType = assembly.GetType("S1Interop.Generated.S1InteropTypeRegistry", throwOnError: true)!;
        Type memberRegistryType = assembly.GetType("S1Interop.Generated.S1InteropMemberRegistry", throwOnError: true)!;
        Type objectCastType = assembly.GetType("S1Interop.Generated.S1InteropObjectCast", throwOnError: true)!;
        Type delegateBridgeType = assembly.GetType("S1Interop.Generated.S1InteropDelegateBridge", throwOnError: true)!;
        Type hudFacadeType = assembly.GetType("S1Interop.ScheduleOne.UI.HUD", throwOnError: true)!;
        Type memberKindType = assembly.GetTypes().Single(type => type.Name == "S1InteropMemberKind");

        object? backend = runtimeType.GetProperty("Backend")?.GetValue(null);
        object? isIl2Cpp = runtimeType.GetProperty("IsIl2Cpp")?.GetValue(null);
        object? hudName = typeRegistryType.GetProperty("HudName")?.GetValue(null);
        object? hudType = typeRegistryType.GetProperty("Hud")?.GetValue(null);
        MethodInfo? getHudInstance = memberRegistryType.GetMethods()
            .FirstOrDefault(method => method.Name == "GetHudInstance" && !method.IsGenericMethod && method.GetParameters().Length == 0);
        object? hudInstance = getHudInstance?.Invoke(null, null);

        Assert(string.Equals(backend?.ToString(), "Il2Cpp", StringComparison.Ordinal), $"Backend-neutral runtime detection should select Il2Cpp when only Il2Cpp types are loadable. Backend={backend}");
        Assert(isIl2Cpp is true, "Backend-neutral runtime IsIl2Cpp should be true for the fake Il2Cpp assembly.");
        Assert(string.Equals(hudName as string, "Il2CppScheduleOne.UI.HUD", StringComparison.Ordinal), $"Backend-neutral HudName should resolve to Il2Cpp type name. HudName={hudName}");
        Assert(hudType is Type resolvedHudType && resolvedHudType.FullName == "Il2CppScheduleOne.UI.HUD", "Backend-neutral type registry should resolve the fake Il2Cpp HUD type.");
        Assert(hudInstance is not null && hudInstance.GetType().FullName == "Il2CppScheduleOne.UI.HUD", "Generated static member helper should return the fake Il2Cpp HUD instance.");
        object hud = hudInstance!;

        MethodInfo? isHud = typeRegistryType.GetMethod("IsHud", [typeof(object)]);
        MethodInfo? getHud = typeRegistryType.GetMethod("GetHud", [typeof(object), typeof(string)]);
        MethodInfo? trySetHud = typeRegistryType.GetMethod("TrySetHud", [typeof(object), typeof(string), typeof(object)]);
        MethodInfo? invokeHud = typeRegistryType.GetMethods()
            .FirstOrDefault(method => method.Name == "InvokeHud" && !method.IsGenericMethod && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { typeof(object), typeof(string), typeof(object[]) }));
        MethodInfo? invokeHudOverload = typeRegistryType.GetMethods()
            .FirstOrDefault(method => method.Name == "InvokeHud" && !method.IsGenericMethod && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { typeof(object), typeof(string), typeof(string[]), typeof(object[]) }));
        Assert(isHud is not null, "Generated type registry should expose an alias-level IsHud helper.");
        Assert(
            assembly.GetType("S1Interop.UI.HUD", throwOnError: false) is null,
            "Generated ScheduleOne facades should not emit shortened duplicate namespaces.");
        Assert(getHud is not null, "Generated type registry should expose an alias-level GetHud helper.");
        Assert(trySetHud is not null, "Generated type registry should expose an alias-level TrySetHud helper.");
        Assert(invokeHud is not null, "Generated type registry should expose an alias-level InvokeHud helper.");
        Assert(invokeHudOverload is not null, "Generated type registry should expose an overload-specific alias-level InvokeHud helper.");
        Assert(isHud!.Invoke(null, [hud]) is true, "Generated alias-level type checker should recognize the fake Il2Cpp HUD instance.");
        Assert(getHud!.Invoke(null, [hud, "Scale"]) is 0, "Generated alias-level instance getter should route through the member registry.");
        Assert(trySetHud!.Invoke(null, [hud, "Scale", "18"]) is true, "Generated alias-level instance setter should convert and write values.");
        Assert(getHud.Invoke(null, [hud, "Scale"]) is 18, "Generated alias-level instance getter should read values written through the alias setter.");
        object?[] facadeArgs = ["21", "facade"];
        Assert(string.Equals(invokeHud!.Invoke(null, [hud, "SetLevel", facadeArgs]) as string, "done", StringComparison.Ordinal), "Generated alias-level method invoker should route through the member registry.");
        Assert(facadeArgs[1] is "il2cpp:facade", "Generated alias-level method invoker should preserve by-ref copy-back behavior.");
        object?[] facadeOverloadArgs = ["22", "facade-overload"];
        Assert(string.Equals(invokeHudOverload!.Invoke(null, [hud, "SetLevel", new[] { "int", "string&" }, facadeOverloadArgs]) as string, "done", StringComparison.Ordinal), "Generated alias-level overload invoker should route through cached parameter-specific member lookup.");
        Assert(facadeOverloadArgs[1] is "il2cpp:facade-overload", "Generated alias-level overload invoker should preserve by-ref copy-back behavior.");

        Type hudTagType = typeRegistryType.GetNestedType("HudTag") ?? throw new InvalidOperationException("Generated type registry should expose HudTag.");
        Type handleType = assembly.GetType("S1Interop.Generated.S1InteropObject`1", throwOnError: true)!.MakeGenericType(hudTagType);
        MethodInfo? tryAsHud = typeRegistryType.GetMethod("TryAsHud");
        MethodInfo? asHud = typeRegistryType.GetMethod("AsHud", [typeof(object)]);
        Assert(tryAsHud is not null, "Generated type registry should expose TryAsHud.");
        Assert(asHud is not null, "Generated type registry should expose AsHud.");
        object?[] typedHandleArgs = [hud, null];
        Assert(tryAsHud!.Invoke(null, typedHandleArgs) is true, "Generated typed handle helper should accept the matching backend object.");
        object typedHud = typedHandleArgs[1] ?? throw new InvalidOperationException("TryAsHud should return a typed handle.");
        Assert(handleType.IsInstanceOfType(typedHud), "Generated typed handle should use the alias-specific tag type.");
        Assert(handleType.GetProperty("HasValue")?.GetValue(typedHud) is true, "Generated typed handle should report a non-null value.");
        Assert(ReferenceEquals(handleType.GetProperty("Instance")?.GetValue(typedHud), hud), "Generated typed handle should retain the original backend object.");
        object?[] wrongHandleArgs = [new object(), null];
        Assert(tryAsHud.Invoke(null, wrongHandleArgs) is false, "Generated typed handle helper should reject unrelated objects.");
        object? missingHandle = asHud!.Invoke(null, [new object()]);
        Assert(missingHandle is not null && handleType.GetProperty("HasValue")?.GetValue(missingHandle) is false, "Generated AsHud should return an empty handle for unrelated objects.");

        MethodInfo? getHudTyped = typeRegistryType.GetMethod("GetHud", [handleType, typeof(string)]);
        MethodInfo? trySetHudTyped = typeRegistryType.GetMethod("TrySetHud", [handleType, typeof(string), typeof(object)]);
        MethodInfo? invokeHudTyped = typeRegistryType.GetMethods()
            .FirstOrDefault(method => method.Name == "InvokeHud" && !method.IsGenericMethod && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { handleType, typeof(string), typeof(object[]) }));
        Assert(getHudTyped is not null, "Generated type registry should expose a typed-handle GetHud helper.");
        Assert(trySetHudTyped is not null, "Generated type registry should expose a typed-handle TrySetHud helper.");
        Assert(invokeHudTyped is not null, "Generated type registry should expose a typed-handle InvokeHud helper.");
        Assert(trySetHudTyped!.Invoke(null, [typedHud, "Scale", "33"]) is true, "Generated typed-handle setter should write values through the member registry.");
        Assert(getHudTyped!.Invoke(null, [typedHud, "Scale"]) is 33, "Generated typed-handle getter should read values through the member registry.");
        object?[] typedFacadeArgs = ["34", "typed-facade"];
        Assert(string.Equals(invokeHudTyped!.Invoke(null, [typedHud, "SetLevel", typedFacadeArgs]) as string, "done", StringComparison.Ordinal), "Generated typed-handle invoker should route through the member registry.");
        Assert(typedFacadeArgs[1] is "il2cpp:typed-facade", "Generated typed-handle invoker should preserve by-ref copy-back behavior.");

        MethodInfo? facadeAs = hudFacadeType.GetMethod("As", [typeof(object)]);
        Type facadeHandleType = hudFacadeType.GetNestedType("Handle") ?? throw new InvalidOperationException("Generated type-scoped facade should expose a nested Handle type.");
        MethodInfo? facadeGetScale = hudFacadeType.GetMethods()
            .FirstOrDefault(method => method.Name == "GetScale" && !method.IsGenericMethod && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { facadeHandleType }));
        MethodInfo? facadeGetScaleValue = hudFacadeType.GetMethods()
            .FirstOrDefault(method => method.Name == "GetScaleValue" && method.IsGenericMethodDefinition && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { facadeHandleType }));
        MethodInfo? facadeTrySetScale = hudFacadeType.GetMethod("TrySetScale", [facadeHandleType, typeof(object)]);
        MethodInfo? facadeSetLevel = hudFacadeType.GetMethods()
            .FirstOrDefault(method => method.Name == "SetLevel" && !method.IsGenericMethod && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { facadeHandleType, typeof(object[]) }));
        PropertyInfo? facadeHandleScale = facadeHandleType.GetProperty("Scale");
        MethodInfo? facadeHandleGetScaleValue = facadeHandleType.GetMethods()
            .FirstOrDefault(method => method.Name == "GetScaleValue" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0);
        MethodInfo? facadeHandleTrySetScale = facadeHandleType.GetMethod("TrySetScale", [typeof(object)]);
        Assert(facadeAs is not null, "Generated type-scoped facade should expose As.");
        Assert(facadeHandleType.GetProperty("HasValue") is not null, "Generated type-scoped facade Handle should expose HasValue.");
        Assert(facadeHandleType.GetProperty("Instance") is not null, "Generated type-scoped facade Handle should expose Instance.");
        Assert(facadeHandleScale is not null, "Generated type-scoped facade Handle should expose native-like member properties.");
        Assert(facadeHandleGetScaleValue is not null, "Generated type-scoped facade Handle should expose typed value getter methods.");
        Assert(facadeHandleTrySetScale is not null, "Generated type-scoped facade Handle should expose instance setter helpers.");
        Assert(facadeGetScale is not null, "Generated type-scoped facade should expose member-name getter methods.");
        Assert(facadeGetScaleValue is not null, "Generated type-scoped facade should expose typed value getters.");
        Assert(facadeTrySetScale is not null, "Generated type-scoped facade should expose member-name setters.");
        Assert(facadeSetLevel is not null, "Generated type-scoped facade should expose method-name invokers.");
        object facadeHandle = facadeAs!.Invoke(null, [hud]) ?? throw new InvalidOperationException("HUD facade As should return a handle.");
        Assert(facadeHandleType.IsInstanceOfType(facadeHandle), "Generated type-scoped facade As should return the nested facade Handle type.");
        Assert(facadeHandleType.GetProperty("HasValue")?.GetValue(facadeHandle) is true, "Generated type-scoped facade Handle should report a non-empty value.");
        Assert(ReferenceEquals(facadeHandleType.GetProperty("Instance")?.GetValue(facadeHandle), hud), "Generated type-scoped facade Handle should retain the backend instance.");
        Assert(facadeTrySetScale!.Invoke(null, [facadeHandle, "55"]) is true, "Generated type-scoped facade setter should write values.");
        Assert(facadeGetScale!.Invoke(null, [facadeHandle]) is 55, "Generated type-scoped facade getter should read values.");
        Assert(facadeGetScaleValue!.MakeGenericMethod(typeof(int)).Invoke(null, [facadeHandle]) is 55, "Generated type-scoped facade value getter should preserve typed convenience.");
        Assert(facadeHandleTrySetScale!.Invoke(facadeHandle, ["57"]) is true, "Generated type-scoped facade Handle setter should write values.");
        Assert(facadeHandleScale!.GetValue(facadeHandle) is 57, "Generated type-scoped facade Handle property should read values.");
        Assert(facadeHandleGetScaleValue!.MakeGenericMethod(typeof(int)).Invoke(facadeHandle, []) is 57, "Generated type-scoped facade Handle typed value getter should preserve typed convenience.");
        object?[] facadeTypeArgs = ["56", "typed-facade-class"];
        Assert(string.Equals(facadeSetLevel!.Invoke(null, [facadeHandle, facadeTypeArgs]) as string, "done", StringComparison.Ordinal), "Generated type-scoped facade method should route through member registry.");
        Assert(facadeTypeArgs[1] is "il2cpp:typed-facade-class", "Generated type-scoped facade method should preserve by-ref copy-back behavior.");

        MethodInfo? trySetScale = memberRegistryType.GetMethod("TrySetHudScale", [typeof(object), typeof(object)]);
        Assert(trySetScale is not null, "Generated member registry should expose TrySetHudScale.");
        object? setResult = trySetScale!.Invoke(null, [hud, "42"]);
        Assert(setResult is true, "Generated field setter should convert string values to the reflected integer field type.");

        MethodInfo? getScaleObject = memberRegistryType.GetMethods()
            .FirstOrDefault(method => method.Name == "GetHudScale" && !method.IsGenericMethod && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { typeof(object) }));
        MethodInfo? getScaleValueObject = memberRegistryType.GetMethods()
            .FirstOrDefault(method => method.Name == "GetHudScaleValue" && method.IsGenericMethodDefinition && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { typeof(object) }));
        Assert(getScaleObject is not null, "Generated member registry should expose an object GetHudScale helper.");
        Assert(getScaleValueObject is not null, "Generated member registry should expose an object GetHudScaleValue helper.");
        Assert(trySetScale!.Invoke(null, [hud, "44"]) is true, "Generated object member setter should write values.");
        Assert(getScaleObject!.Invoke(null, [hud]) is 44, "Generated object member getter should read values.");
        object? typedScaleValue = getScaleValueObject!.MakeGenericMethod(typeof(int)).Invoke(null, [hud]);
        Assert(typedScaleValue is 44, $"Generated object value getter should preserve value-type convenience. Result={typedScaleValue}");

        object? scale = hud.GetType().GetField("Scale")?.GetValue(hud);
        Assert(scale is 44, $"Generated field setter should update the fake Il2Cpp field. Scale={scale}");

        object fieldKind = Enum.Parse(memberKindType, "Field");
        MethodInfo? getInstanceValue = memberRegistryType.GetMethods()
            .FirstOrDefault(method => method.Name == "GetInstanceValue" && method.GetParameters().Length == 3);
        MethodInfo? trySetInstanceValue = memberRegistryType.GetMethods()
            .FirstOrDefault(method => method.Name == "TrySetInstanceValue" && method.GetParameters().Length == 4);
        Assert(getInstanceValue is not null, "Generated member registry should expose typed GetInstanceValue.");
        Assert(trySetInstanceValue is not null, "Generated member registry should expose typed TrySetInstanceValue.");
        object? dynamicScale = getInstanceValue!.Invoke(null, [hud, "Scale", fieldKind]);
        Assert(dynamicScale is 44, $"Generated dynamic instance getter should read from the fake Il2Cpp object. Scale={dynamicScale}");
        object? dynamicSetResult = trySetInstanceValue!.Invoke(null, [hud, "Scale", "99", fieldKind]);
        Assert(dynamicSetResult is true, "Generated dynamic instance setter should convert values and write through cached instance member lookup.");
        Assert(hud.GetType().GetField("Scale")?.GetValue(hud) is 99, "Generated dynamic instance setter should update the fake Il2Cpp field.");

        MethodInfo? invokeHudGeneric = typeRegistryType.GetMethods()
            .FirstOrDefault(method => method.Name == "InvokeHud" && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1 && method.GetParameters().Length == 3);
        Assert(invokeHudGeneric is not null, "Generated alias-level invoker should expose a typed generic overload.");
        object? typedFacadeResult = invokeHudGeneric!.MakeGenericMethod(typeof(int)).Invoke(null, [hud, "GetScaleText", Array.Empty<object?>()]);
        Assert(typedFacadeResult is 99, $"Generated alias-level typed invoker should convert simple reflected return values. Result={typedFacadeResult}");

        MethodInfo? invokeGetScaleTextGeneric = memberRegistryType.GetMethods()
            .FirstOrDefault(method => method.Name == "InvokeHudGetScaleText" && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1);
        Assert(invokeGetScaleTextGeneric is not null, "Generated member invoker should expose a typed generic overload.");
        object? typedMemberResult = invokeGetScaleTextGeneric!.MakeGenericMethod(typeof(int)).Invoke(null, [hud, Array.Empty<object?>()]);
        Assert(typedMemberResult is 99, $"Generated member typed invoker should convert simple reflected return values. Result={typedMemberResult}");

        MethodInfo? invokeSetLevel = GetNonGenericMethod(memberRegistryType, "InvokeHudSetLevel", typeof(object), typeof(object[]));
        Assert(invokeSetLevel is not null, "Generated member registry should expose InvokeHudSetLevel.");
        object?[] args = ["7", "fps"];
        object? invokeResult = invokeSetLevel!.Invoke(null, [hud, args]);

        Assert(string.Equals(invokeResult as string, "done", StringComparison.Ordinal), $"Generated method invoker should return reflected method result. Result={invokeResult}");
        Assert(hud.GetType().GetField("Scale")?.GetValue(hud) is 7, "Generated method invoker should convert string numeric arguments before invocation.");
        Assert(string.Equals(args[1] as string, "il2cpp:fps", StringComparison.Ordinal), $"Generated method invoker should copy by-ref argument values back to caller args. Arg={args[1]}");

        MethodInfo? invokeInstance = memberRegistryType.GetMethod("InvokeInstance", [typeof(object), typeof(string), typeof(string[]), typeof(object[])]);
        Assert(invokeInstance is not null, "Generated member registry should expose overload-specific InvokeInstance.");
        object?[] dynamicArgs = ["11", "hud"];
        object? dynamicInvokeResult = invokeInstance!.Invoke(null, [hud, "SetLevel", new[] { "int", "string&" }, dynamicArgs]);
        Assert(string.Equals(dynamicInvokeResult as string, "done", StringComparison.Ordinal), $"Generated dynamic instance invoker should return reflected method result. Result={dynamicInvokeResult}");
        Assert(hud.GetType().GetField("Scale")?.GetValue(hud) is 11, "Generated dynamic instance invoker should convert arguments before invocation.");
        Assert(string.Equals(dynamicArgs[1] as string, "il2cpp:hud", StringComparison.Ordinal), $"Generated dynamic instance invoker should copy by-ref argument values back to caller args. Arg={dynamicArgs[1]}");

        MethodInfo? invokeRewriteGuid = GetNonGenericMethod(memberRegistryType, "InvokeHudRewriteGuid", typeof(object), typeof(object[]));
        Assert(invokeRewriteGuid is not null, "Generated member registry should expose InvokeHudRewriteGuid.");
        object?[] guidArgs = [Guid.Parse("11111111-2222-3333-4444-555555555555")];
        object? rewriteGuidResult = invokeRewriteGuid!.Invoke(null, [hud, guidArgs]);
        Assert(string.Equals(rewriteGuidResult as string, "22222222-3333-4444-5555-666666666666", StringComparison.Ordinal), $"Generated method invoker should return the fake IL2CPP ref Guid result. Result={rewriteGuidResult}");
        Assert(guidArgs[0] is Guid copiedGuid && copiedGuid == Guid.Parse("22222222-3333-4444-5555-666666666666"), $"Generated method invoker should copy IL2CPP Guid ref values back as System.Guid. Arg={guidArgs[0]}");

        MethodInfo? invokeRewriteNames = GetNonGenericMethod(memberRegistryType, "InvokeHudRewriteNames", typeof(object), typeof(object[]));
        Assert(invokeRewriteNames is not null, "Generated member registry should expose InvokeHudRewriteNames.");
        object?[] nameRefArgs = [new List<string> { "alpha", "beta" }];
        object? rewriteNamesResult = invokeRewriteNames!.Invoke(null, [hud, nameRefArgs]);
        Assert(string.Equals(rewriteNamesResult as string, "2:delta:echo", StringComparison.Ordinal), $"Generated method invoker should return the fake IL2CPP ref list result. Result={rewriteNamesResult}");
        Assert(nameRefArgs[0] is List<string> copiedNames && copiedNames.SequenceEqual(new[] { "delta", "echo" }), $"Generated method invoker should copy IL2CPP list ref values back as managed lists. Arg={nameRefArgs[0]}");

        MethodInfo? invokeRewriteScores = GetNonGenericMethod(memberRegistryType, "InvokeHudRewriteScores", typeof(object), typeof(object[]));
        Assert(invokeRewriteScores is not null, "Generated member registry should expose InvokeHudRewriteScores.");
        object?[] scoreRefArgs = [new Dictionary<string, int> { ["north"] = 4 }];
        object? rewriteScoresResult = invokeRewriteScores!.Invoke(null, [hud, scoreRefArgs]);
        Assert(string.Equals(rewriteScoresResult as string, "2:12:15", StringComparison.Ordinal), $"Generated method invoker should return the fake IL2CPP ref dictionary result. Result={rewriteScoresResult}");
        Assert(scoreRefArgs[0] is Dictionary<string, int> copiedScores && copiedScores.Count == 2 && copiedScores["east"] == 12 && copiedScores["west"] == 15, $"Generated method invoker should copy IL2CPP dictionary ref values back as managed dictionaries. Arg={scoreRefArgs[0]}");

        MethodInfo? invokeRewriteTags = GetNonGenericMethod(memberRegistryType, "InvokeHudRewriteTags", typeof(object), typeof(object[]));
        Assert(invokeRewriteTags is not null, "Generated member registry should expose InvokeHudRewriteTags.");
        object?[] tagRefArgs = [new HashSet<string> { "north" }];
        object? rewriteTagsResult = invokeRewriteTags!.Invoke(null, [hud, tagRefArgs]);
        Assert(string.Equals(rewriteTagsResult as string, "2:True:True", StringComparison.Ordinal), $"Generated method invoker should return the fake IL2CPP ref hash set result. Result={rewriteTagsResult}");
        Assert(tagRefArgs[0] is HashSet<string> copiedTags && copiedTags.SetEquals(new[] { "east", "west" }), $"Generated method invoker should copy IL2CPP hash set ref values back as managed hash sets. Arg={tagRefArgs[0]}");

        MethodInfo? invokeRewriteBytes = GetNonGenericMethod(memberRegistryType, "InvokeHudRewriteBytes", typeof(object), typeof(object[]));
        Assert(invokeRewriteBytes is not null, "Generated member registry should expose InvokeHudRewriteBytes.");
        object?[] byteRefArgs = [new byte[] { 1, 2 }];
        object? rewriteBytesResult = invokeRewriteBytes!.Invoke(null, [hud, byteRefArgs]);
        Assert(string.Equals(rewriteBytesResult as string, "3:4:5:6", StringComparison.Ordinal), $"Generated method invoker should return the fake IL2CPP ref byte-array result. Result={rewriteBytesResult}");
        Assert(byteRefArgs[0] is byte[] copiedBytes && copiedBytes.SequenceEqual(new byte[] { 4, 5, 6 }), $"Generated method invoker should copy IL2CPP byte-array ref values back as managed byte arrays. Arg={byteRefArgs[0]}");

        MethodInfo? invokeSetData = GetNonGenericMethod(memberRegistryType, "InvokeHudSetData", typeof(object), typeof(object[]));
        Assert(invokeSetData is not null, "Generated member registry should expose InvokeHudSetData.");
        Guid guid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        object? setDataResult = invokeSetData!.Invoke(null, [hud, new object?[] { guid, new[] { "alpha", "beta" } }]);
        Assert(
            string.Equals(setDataResult as string, "11111111-2222-3333-4444-555555555555:2:alpha", StringComparison.Ordinal),
            $"Generated method invoker should convert System.Guid and managed arrays to fake IL2CPP Guid/List parameter types. Result={setDataResult}");

        MethodInfo? invokeSetBytes = GetNonGenericMethod(memberRegistryType, "InvokeHudSetBytes", typeof(object), typeof(object[]));
        Assert(invokeSetBytes is not null, "Generated member registry should expose InvokeHudSetBytes.");
        object? setBytesResult = invokeSetBytes!.Invoke(null, [hud, new object?[] { new byte[] { 7, 9 } }]);
        Assert(
            string.Equals(setBytesResult as string, "2:7:9", StringComparison.Ordinal),
            $"Generated method invoker should convert managed byte arrays to fake IL2CPP struct arrays. Result={setBytesResult}");

        MethodInfo? invokeSetLabels = GetNonGenericMethod(memberRegistryType, "InvokeHudSetLabels", typeof(object), typeof(object[]));
        Assert(invokeSetLabels is not null, "Generated member registry should expose InvokeHudSetLabels.");
        object? setLabelsResult = invokeSetLabels!.Invoke(null, [hud, new object?[] { new[] { "north", "south" } }]);
        Assert(
            string.Equals(setLabelsResult as string, "2:north:south", StringComparison.Ordinal),
            $"Generated method invoker should convert managed string arrays to fake IL2CPP reference arrays. Result={setLabelsResult}");

        MethodInfo? invokeSetScores = GetNonGenericMethod(memberRegistryType, "InvokeHudSetScores", typeof(object), typeof(object[]));
        Assert(invokeSetScores is not null, "Generated member registry should expose InvokeHudSetScores.");
        var scores = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(new Dictionary<string, int>
        {
            ["north"] = 4,
            ["south"] = 8
        });
        object? setScoresResult = invokeSetScores!.Invoke(null, [hud, new object?[] { scores }]);
        Assert(
            string.Equals(setScoresResult as string, "2:4:8", StringComparison.Ordinal),
            $"Generated method invoker should convert managed read-only dictionaries to fake IL2CPP dictionaries. Result={setScoresResult}");

        MethodInfo? invokeSetTags = GetNonGenericMethod(memberRegistryType, "InvokeHudSetTags", typeof(object), typeof(object[]));
        Assert(invokeSetTags is not null, "Generated member registry should expose InvokeHudSetTags.");
        var tags = new HashSet<string> { "north", "south", "north" };
        object? setTagsResult = invokeSetTags!.Invoke(null, [hud, new object?[] { tags }]);
        Assert(
            string.Equals(setTagsResult as string, "2:True:True", StringComparison.Ordinal),
            $"Generated method invoker should convert managed hash sets to fake IL2CPP hash sets. Result={setTagsResult}");

        Type objectBaseType = assembly.GetType("Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase", throwOnError: true)!;
        object proxy = Activator.CreateInstance(objectBaseType, [hud])!;
        MethodInfo? objectCastAs = objectCastType.GetMethod("As")?.MakeGenericMethod(hud.GetType());
        Assert(objectCastAs is not null, "Generated object cast helper should expose As<T>.");
        object? castResult = objectCastAs!.Invoke(null, [proxy]);
        Assert(ReferenceEquals(castResult, hud), "Generated object cast helper should unwrap fake IL2CPP proxies through reflected TryCast<T>.");

        Type delegateSupportType = assembly.GetType("Il2CppInterop.Runtime.DelegateSupport", throwOnError: true)!;
        MethodInfo? convertDelegate = delegateBridgeType.GetMethod("Convert")?.MakeGenericMethod(typeof(Action));
        Assert(convertDelegate is not null, "Generated delegate bridge should expose Convert<TDelegate>.");
        Action action = static () => { };
        object? convertedDelegate = convertDelegate!.Invoke(null, [action]);
        object? delegateSupportCalled = delegateSupportType.GetField("WasCalled")?.GetValue(null);
        Assert(ReferenceEquals(convertedDelegate, action), "Generated delegate bridge should return the converted delegate instance.");
        Assert(delegateSupportCalled is true, "Generated delegate bridge should route IL2CPP delegate conversion through reflected DelegateSupport.ConvertDelegate<T>.");
    }

    private void S1InteropGeneratorProducesCompileTimeEventBridges()
    {
        const string source =
            """
            [assembly: S1Interop.S1InteropGenerateUnityEventBridge]
            [assembly: S1Interop.S1InteropGenerateDelegateEventBridge]

            namespace UnityEngine.Events
            {
                public delegate void UnityAction();
                public delegate void UnityAction<T0>(T0 value);

                public sealed class UnityEvent
                {
                    public void AddListener(UnityAction listener) { }
                    public void AddListener(System.Action listener) { }
                    public void RemoveListener(UnityAction listener) { }
                    public void RemoveListener(System.Action listener) { }
                }

                public sealed class UnityEvent<T0>
                {
                    public void AddListener(UnityAction<T0> listener) { }
                    public void AddListener(System.Action<T0> listener) { }
                    public void RemoveListener(UnityAction<T0> listener) { }
                    public void RemoveListener(System.Action<T0> listener) { }
                }
            }

            namespace SyntheticMod
            {
                internal static class Core
                {
                }
            }
            """;

        IReadOnlyDictionary<string, string> monoGenerated = RunS1InteropGenerator(source, "MONO");
        IReadOnlyDictionary<string, string> il2CppGenerated = RunS1InteropGenerator(source, "IL2CPP");

        bool hasMonoUnityBridge = monoGenerated.TryGetValue("S1Interop.UnityEventBridge.g.cs", out string? monoUnityBridge);
        bool hasMonoDelegateBridge = monoGenerated.TryGetValue("S1Interop.DelegateEventBridge.g.cs", out string? monoDelegateBridge);
        bool hasIl2CppUnityBridge = il2CppGenerated.TryGetValue("S1Interop.UnityEventBridge.g.cs", out string? il2CppUnityBridge);
        bool hasIl2CppDelegateBridge = il2CppGenerated.TryGetValue("S1Interop.DelegateEventBridge.g.cs", out string? il2CppDelegateBridge);

        Assert(
            hasMonoUnityBridge && hasMonoDelegateBridge,
            "Generator should emit requested event bridges for Mono builds.");
        Assert(
            hasIl2CppUnityBridge && hasIl2CppDelegateBridge,
            "Generator should emit requested event bridges for IL2CPP builds.");

        monoUnityBridge ??= string.Empty;
        monoDelegateBridge ??= string.Empty;
        il2CppUnityBridge ??= string.Empty;
        il2CppDelegateBridge ??= string.Empty;

        Assert(
            monoUnityBridge.Contains("S1InteropUnityEventBridge", StringComparison.Ordinal) &&
            monoUnityBridge.Contains("UnityEngine.Events.UnityAction wrapped = new UnityEngine.Events.UnityAction(listener);", StringComparison.Ordinal),
            "Compile-time UnityEvent bridge should include Mono UnityAction wrapping.");
        Assert(
            il2CppUnityBridge.Contains("#if IL2CPP", StringComparison.Ordinal) &&
            il2CppUnityBridge.Contains("global::System.Action wrapped = new global::System.Action(listener);", StringComparison.Ordinal),
            "Compile-time UnityEvent bridge should include IL2CPP System.Action wrapping.");
        Assert(
            monoDelegateBridge.Contains("S1InteropDelegateEventBridge", StringComparison.Ordinal) &&
            il2CppDelegateBridge.Contains("global::System.Delegate.Combine", StringComparison.Ordinal) &&
            il2CppDelegateBridge.Contains("global::System.Delegate.Remove", StringComparison.Ordinal),
            "Compile-time delegate bridge should include Combine and Remove helpers.");
    }

    private void SdkFacadeAliasesFullyQualifiedScheduleOneTypes()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "AliasFacadeMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "Core.cs"),
                """
                using GameHud = Il2CppScheduleOne.UI.HUD;
                using GameWeed = ScheduleOne.Product.WeedDefinition;

                namespace AliasFacadeMod
                {
                    public static class Core
                    {
                        private static GameHud? hud;
                        private static GameWeed? aliasedWeed;
                        private static ScheduleOne.Product.WeedDefinition? weed;
                        private static ScheduleOne.Persistence.Datas.QuestEntryData? questEntry;
                        private static ScheduleOne.Other.QuestEntryData? collidingQuestEntry;
                        private static Type? npcInventoryType = AccessTools.TypeByName("Il2CppScheduleOne.NPCs.NPCInventory");
                    }
                }
                """);

            ProjectAnalysis project = analyzer.Analyze(tempProject).Projects.Single();
            var generator = new SdkFacadeGenerator();
            SdkFacadePlan plan = generator.Plan(project);
            string facadeSource = generator.GenerateSource(plan);

            Assert(
                plan.TypeAliases.Any(alias =>
                    alias.Alias == "WeedDefinition" &&
                    alias.MonoType == "ScheduleOne.Product.WeedDefinition" &&
                    alias.Il2CppType == "Il2CppScheduleOne.Product.WeedDefinition" &&
                    alias.GenerateGlobalUsing),
                "Facade plan should alias unique fully-qualified ScheduleOne type references.");
            Assert(
                plan.TypeAliases.Any(alias =>
                    alias.Alias == "GameHud" &&
                    alias.MonoType == "ScheduleOne.UI.HUD" &&
                    alias.Il2CppType == "Il2CppScheduleOne.UI.HUD" &&
                    !alias.GenerateGlobalUsing),
                "Facade plan should normalize explicit Il2Cpp ScheduleOne alias directives into backend-neutral registry aliases without duplicating local aliases.");
            Assert(
                plan.TypeAliases.Any(alias =>
                    alias.Alias == "GameWeed" &&
                    alias.MonoType == "ScheduleOne.Product.WeedDefinition" &&
                    alias.Il2CppType == "Il2CppScheduleOne.Product.WeedDefinition" &&
                    !alias.GenerateGlobalUsing),
                "Facade plan should preserve explicit Mono ScheduleOne alias directives as backend-neutral registry aliases without duplicating local aliases.");
            Assert(
                plan.TypeAliases.All(alias => alias.Alias != "QuestEntryData"),
                "Facade plan should skip aliases when multiple fully-qualified types share the same simple name.");
            Assert(
                plan.TypeAliases.Any(alias =>
                    alias.Alias == "NPCInventory" &&
                    alias.MonoType == "ScheduleOne.NPCs.NPCInventory" &&
                    alias.Il2CppType == "Il2CppScheduleOne.NPCs.NPCInventory" &&
                    !alias.GenerateGlobalUsing),
                "Facade plan should infer registry aliases from string-held ScheduleOne type lookups without adding broad global aliases.");
            Assert(
                facadeSource.Contains("global using WeedDefinition = ScheduleOne.Product.WeedDefinition;", StringComparison.Ordinal) &&
                facadeSource.Contains("global using WeedDefinition = Il2CppScheduleOne.Product.WeedDefinition;", StringComparison.Ordinal),
                "Generated facade should include conditional aliases for unique fully-qualified type references.");
            Assert(
                !facadeSource.Contains("global using GameHud =", StringComparison.Ordinal) &&
                !facadeSource.Contains("global using GameWeed =", StringComparison.Ordinal),
                "Generated facade should not duplicate aliases already declared by source files.");
            Assert(
                facadeSource.Contains("[assembly: S1Interop.S1InteropType(\"ScheduleOne.UI.HUD\", Alias = \"GameHud\", Il2CppTypeName = \"Il2CppScheduleOne.UI.HUD\")]", StringComparison.Ordinal) &&
                facadeSource.Contains("[assembly: S1Interop.S1InteropType(\"ScheduleOne.Product.WeedDefinition\", Alias = \"GameWeed\", Il2CppTypeName = \"Il2CppScheduleOne.Product.WeedDefinition\")]", StringComparison.Ordinal),
                "Generated facade should emit registry attributes so explicit aliases can feed backend-neutral Roslyn caches.");
            Assert(
                !facadeSource.Contains("S1InteropRuntime", StringComparison.Ordinal),
                "File-based SDK facade should not emit runtime helpers; the Roslyn generator owns backend-neutral runtime detection and caches.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MigrationApplyAndRollbackRewritesFullyQualifiedScheduleOneTypes()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "AliasRewriteMod.csproj");
            string tempSource = Path.Combine(tempRoot, "Core.cs");
            string generatedFacade = Path.Combine(tempRoot, "S1Interop.Generated", "S1Interop.GlobalUsings.g.cs");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                tempSource,
                """
                using System;

                namespace AliasRewriteMod
                {
                    public static class Core
                    {
                        public const string TypeName = "ScheduleOne.Product.WeedDefinition";
                        public const string VerbatimTypeName = @"ScheduleOne.Product.WeedDefinition";
                        public const string CommandSnippet = "new Il2CppScheduleOne.Console.SetWeather().Execute(null)";
                        public const string ListSnippet = "new Il2CppSystem.Collections.Generic.List<string>()";
                        // ScheduleOne.Product.WeedDefinition should remain readable in comments.
                        // new Il2CppScheduleOne.Console.SetWeather().Execute(null) should remain readable in comments.
                        /*
                         * var ignoredCommand = new Il2CppScheduleOne.Console.ClearTrash();
                         */
                        public static Type WeedType => typeof(ScheduleOne.Product.WeedDefinition);
                        public static Type HudType => typeof(Il2CppScheduleOne.UI.HUD);
                        public static Type? WeedTypeByName => Type.GetType("ScheduleOne.Product.WeedDefinition", false);
                        public static Type? MoneyManagerType => AccessTools.TypeByName("Il2CppScheduleOne.Money.MoneyManager");
                        public static ScheduleOne.Product.WeedDefinition? Find() => null;

                        public static void RunConsoleCommand()
                        {
                            var commandList = new Il2CppSystem.Collections.Generic.List<string>();
                            commandList.Add("rain");
                            Il2CppSystem.Collections.Generic.List<string> nativeList = new Il2CppSystem.Collections.Generic.List<string>();
                            nativeList.Add("storm");

                            var command = new Il2CppScheduleOne.Console.SetWeather();
                            command.Execute(commandList);
                            new Il2CppScheduleOne.Console.ClearTrash().Execute(null);
                        }
                    }
                }
                """);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            MigrationPlan plan = new MigrationPlanner().Plan(before);
            ProjectMigrationPlan projectPlan = plan.Projects.Single();
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "generate_sdk_facade"),
                "Fully-qualified ScheduleOne types should trigger SDK facade generation.");
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "install_s1interop_generator_package"),
                "Generated SDK facade registry attributes should install the S1Interop Roslyn generator package.");
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "rewrite_fully_qualified_scheduleone_types"),
                "Fully-qualified ScheduleOne types should plan a source rewrite when the alias is unique.");
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "rewrite_scheduleone_string_type_lookups"),
                "ScheduleOne string type lookup calls should plan a backend-neutral registry rewrite.");
            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "rewrite_scheduleone_type_facade_invocations"),
                "Simple ScheduleOne object creation and instance calls should plan a backend-neutral type facade rewrite.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "rewrite_fully_qualified_scheduleone_types"),
                "Migration apply should rewrite fully-qualified ScheduleOne type references.");
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "rewrite_scheduleone_string_type_lookups"),
                "Migration apply should rewrite ScheduleOne string type lookup calls.");
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "rewrite_scheduleone_type_facade_invocations"),
                "Migration apply should rewrite simple ScheduleOne object creation and instance calls.");
            Assert(File.Exists(generatedFacade), "Migration apply should generate the SDK facade for type aliases.");

            string migratedSource = File.ReadAllText(tempSource);
            string facadeSource = File.ReadAllText(generatedFacade);
            Assert(
                migratedSource.Contains("Type WeedType => S1Interop.Generated.S1InteropTypeRegistry.WeedDefinition;", StringComparison.Ordinal) &&
                migratedSource.Contains("Type HudType => S1Interop.Generated.S1InteropTypeRegistry.HUD;", StringComparison.Ordinal) &&
                migratedSource.Contains("WeedDefinition? Find()", StringComparison.Ordinal),
                "Migration should route typeof game type lookups through the generated registry while keeping ordinary type positions on generated aliases.");
            Assert(
                migratedSource.Contains("Type? WeedTypeByName => S1Interop.Generated.S1InteropTypeRegistry.WeedDefinition;", StringComparison.Ordinal) &&
                migratedSource.Contains("Type? MoneyManagerType => S1Interop.Generated.S1InteropTypeRegistry.MoneyManager;", StringComparison.Ordinal),
                "Migration should rewrite obvious string-based game type lookups to generated backend-neutral registry properties.");
            Assert(
                migratedSource.Contains("var commandList = new System.Collections.Generic.List<string>();", StringComparison.Ordinal) &&
                migratedSource.Contains("Il2CppSystem.Collections.Generic.List<string> nativeList = new Il2CppSystem.Collections.Generic.List<string>();", StringComparison.Ordinal) &&
                migratedSource.Contains("var command = S1Interop.ScheduleOne.Console.SetWeather.Create();", StringComparison.Ordinal) &&
                migratedSource.Contains("S1Interop.ScheduleOne.Console.SetWeather.Invoke(command, \"Execute\", commandList);", StringComparison.Ordinal) &&
                migratedSource.Contains("S1Interop.ScheduleOne.Console.ClearTrash.Invoke(S1Interop.ScheduleOne.Console.ClearTrash.Create(), \"Execute\", (object?)null);", StringComparison.Ordinal),
                $"Migration should route simple ScheduleOne command construction and invocation through generated type facades. Migrated source:{Environment.NewLine}{migratedSource}");
            Assert(
                !migratedSource.Contains("typeof(ScheduleOne.Product.WeedDefinition)", StringComparison.Ordinal) &&
                !migratedSource.Contains("typeof(Il2CppScheduleOne.UI.HUD)", StringComparison.Ordinal) &&
                !migratedSource.Contains("typeof(WeedDefinition)", StringComparison.Ordinal) &&
                !migratedSource.Contains("var commandList = new Il2CppSystem.Collections.Generic.List<string>();", StringComparison.Ordinal) &&
                !migratedSource.Contains("var command = new Il2CppScheduleOne.Console.SetWeather();", StringComparison.Ordinal) &&
                !migratedSource.Contains("ScheduleOne.Product.WeedDefinition? Find()", StringComparison.Ordinal),
                "Migration should remove fully-qualified ScheduleOne type tokens from code when the alias is unique.");
            Assert(
                migratedSource.Contains("public const string TypeName = \"ScheduleOne.Product.WeedDefinition\";", StringComparison.Ordinal) &&
                migratedSource.Contains("public const string VerbatimTypeName = @\"ScheduleOne.Product.WeedDefinition\";", StringComparison.Ordinal) &&
                migratedSource.Contains("public const string CommandSnippet = \"new Il2CppScheduleOne.Console.SetWeather().Execute(null)\";", StringComparison.Ordinal) &&
                migratedSource.Contains("public const string ListSnippet = \"new Il2CppSystem.Collections.Generic.List<string>()\";", StringComparison.Ordinal) &&
                migratedSource.Contains("// ScheduleOne.Product.WeedDefinition should remain readable in comments.", StringComparison.Ordinal) &&
                migratedSource.Contains("// new Il2CppScheduleOne.Console.SetWeather().Execute(null) should remain readable in comments.", StringComparison.Ordinal) &&
                migratedSource.Contains("* var ignoredCommand = new Il2CppScheduleOne.Console.ClearTrash();", StringComparison.Ordinal),
                "Migration should not rewrite fully-qualified ScheduleOne type names or invocation snippets inside strings or comments.");
            Assert(
                facadeSource.Contains("global using WeedDefinition = ScheduleOne.Product.WeedDefinition;", StringComparison.Ordinal) &&
                facadeSource.Contains("global using WeedDefinition = Il2CppScheduleOne.Product.WeedDefinition;", StringComparison.Ordinal),
                "Generated facade should provide Mono and IL2CPP aliases for the rewritten type.");
            Assert(
                facadeSource.Contains("[assembly: S1Interop.S1InteropType(\"ScheduleOne.Product.WeedDefinition\", Alias = \"WeedDefinition\", Il2CppTypeName = \"Il2CppScheduleOne.Product.WeedDefinition\")]", StringComparison.Ordinal),
                "Generated facade should register rewritten aliases for backend-neutral reflection cache generation.");
            Assert(
                facadeSource.Contains("[assembly: S1Interop.S1InteropType(\"ScheduleOne.Money.MoneyManager\", Alias = \"MoneyManager\", Il2CppTypeName = \"Il2CppScheduleOne.Money.MoneyManager\")]", StringComparison.Ordinal),
                "Generated facade should register string-discovered type lookup aliases for backend-neutral reflection cache generation.");
            Assert(
                facadeSource.Contains("[assembly: S1Interop.S1InteropType(\"ScheduleOne.Console.SetWeather\", Alias = \"SetWeather\", Il2CppTypeName = \"Il2CppScheduleOne.Console.SetWeather\")]", StringComparison.Ordinal) &&
                facadeSource.Contains("[assembly: S1Interop.S1InteropType(\"ScheduleOne.Console.ClearTrash\", Alias = \"ClearTrash\", Il2CppTypeName = \"Il2CppScheduleOne.Console.ClearTrash\")]", StringComparison.Ordinal),
                "Generated facade should register type-facade invocation targets for backend-neutral command construction.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempSource), "Rollback should restore the rewritten source file.");
            Assert(!File.Exists(generatedFacade), "Rollback should remove the generated alias facade.");
            Assert(
                File.ReadAllText(tempSource).Contains("ScheduleOne.Product.WeedDefinition", StringComparison.Ordinal),
                "Rollback should restore the original fully-qualified ScheduleOne type references.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void HideFromIl2CppMigrationHandlesMultipleTargetsAndOverloads()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "SyntheticMod.csproj");
            string tempSource = Path.Combine(tempRoot, "InjectedComponent.cs");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>netstandard2.1</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                tempSource,
                """
                namespace SyntheticMod;

                public class HelperA
                {
                }

                public class HelperB
                {
                }

                #if !MONO
                [MelonLoader.RegisterTypeInIl2Cpp]
                #endif
                public class InjectedComponent
                {
                #if !MONO
                    public InjectedComponent(IntPtr ptr) : base(ptr) { }
                #endif

                    public void Convert(int value)
                    {
                    }

                    public HelperA Convert(HelperA value)
                    {
                        return value;
                    }

                    public HelperB ConvertB(HelperB value)
                    {
                        return value;
                    }
                }
                """);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            ProjectMigrationPlan projectPlan = new MigrationPlanner().Plan(before).Projects.Single();
            MigrationOperation[] hideOperations = projectPlan.Operations
                .Where(operation => operation.RuleId == "injected_member_requires_hidefromil2cpp")
                .ToArray();
            Assert(hideOperations.Length == 2, "Synthetic fixture should report two managed-surface migration operations.");
            Assert(
                hideOperations.Any(operation => operation.Evidence?.Contains("Convert(HelperA value)", StringComparison.Ordinal) == true),
                "HideFromIl2Cpp migration evidence should preserve overload parameter text.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(new MigrationPlan(tempProject, [projectPlan]));
            Assert(
                applyResult.Operations.Count(operation => operation.RuleId == "injected_member_requires_hidefromil2cpp") == 2,
                "Migration apply should apply both same-file HideFromIl2Cpp operations.");

            string migratedSource = File.ReadAllText(tempSource);
            Assert(
                CountOccurrences(migratedSource, "Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp") == 2,
                "Migration should add exactly two HideFromIl2Cpp attributes.");

            int primitiveOverloadIndex = migratedSource.IndexOf("public void Convert(int value)", StringComparison.Ordinal);
            int helperOverloadIndex = migratedSource.IndexOf("public HelperA Convert(HelperA value)", StringComparison.Ordinal);
            int firstAttributeIndex = migratedSource.IndexOf("[Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]", StringComparison.Ordinal);
            Assert(
                primitiveOverloadIndex >= 0 &&
                helperOverloadIndex > primitiveOverloadIndex &&
                firstAttributeIndex > primitiveOverloadIndex &&
                firstAttributeIndex < helperOverloadIndex,
                "Migration should hide the HelperA overload without hiding the primitive overload.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            ProjectMigrationPlan idempotentPlan = new MigrationPlanner().Plan(after).Projects.Single();
            Assert(
                idempotentPlan.Operations.All(operation => operation.RuleId != "injected_member_requires_hidefromil2cpp"),
                "A second migration plan should not duplicate HideFromIl2Cpp operations after apply.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MigrationPlannerCreatesOperationsForBrokenFixture()
    {
        string path = Path.Combine(WorkspaceRoot, @"JackpotEveryTime\JackpotEveryTime.csproj");
        WorkspaceAnalysis analysis = analyzer.Analyze(path);
        MigrationPlan plan = new MigrationPlanner().Plan(analysis);
        ProjectMigrationPlan projectPlan = plan.Projects.Single();

        Assert(
            projectPlan.Operations.Any(operation => operation.RuleId == "wrong_target_framework" && operation.Automatic),
            "Expected automatic TargetFramework migration operation for JackpotEveryTime.");
        Assert(
            projectPlan.Operations.Any(operation => operation.RuleId == "wrong_il2cpp_reference_surface" && operation.Risk == "medium"),
            "Expected IL2CPP reference-surface migration operation for JackpotEveryTime.");
        Assert(
            projectPlan.Operations.Any(operation => operation.RuleId == "generate_sdk_facade" && operation.Automatic),
            "Expected SDK facade generation operation for JackpotEveryTime.");
    }

    private void SdkFacadeGeneratorDetectsGunsAlwaysAccurateNamespaces()
    {
        ProjectAnalysis project = AnalyzeProject(@"GunsAlwaysAccurate\GunsAlwaysAccurate.csproj");
        var generator = new SdkFacadeGenerator();
        SdkFacadePlan facadePlan = generator.Plan(project);
        string source = generator.GenerateSource(facadePlan);

        Assert(facadePlan.HasContent, "GunsAlwaysAccurate should produce a facade generation plan.");
        Assert(
            facadePlan.ScheduleOneNamespaces.Contains("ScheduleOne.DevUtilities"),
            "Expected facade plan to include ScheduleOne.DevUtilities.");
        Assert(
            facadePlan.ScheduleOneNamespaces.Contains("ScheduleOne.UI"),
            "Expected facade plan to include ScheduleOne.UI.");
        Assert(
            source.Contains("global using ScheduleOne.UI;", StringComparison.Ordinal),
            "Expected generated source to include Mono ScheduleOne.UI global using.");
        Assert(
            source.Contains("global using Il2CppScheduleOne.UI;", StringComparison.Ordinal),
            "Expected generated source to include IL2CPP ScheduleOne.UI global using.");
        Assert(
            !source.Contains("Il2CppIl2Cpp", StringComparison.Ordinal),
            "Generated source should not double-prefix Il2Cpp namespaces.");
    }

    private void SdkFacadeMigrationRequiresCSharp10ForDefaultLangVersionProjects()
    {
        ProjectAnalysis project = AnalyzeProject(@"DedicatedServerAddons\S1DS-PlayerList\S1DS-PlayerList.csproj");

        AssertHasDiagnostic(project, "global_usings_require_langversion", null);

        MigrationPlan plan = new MigrationPlanner().Plan(
            new WorkspaceAnalysis(project.ProjectPath, [project]),
            new MigrationPlannerOptions(DualRuntime: true));
        Assert(
            plan.Projects.Single().Operations.Any(operation =>
                operation.RuleId == "global_usings_require_langversion" &&
                operation.Automatic),
            "Default LangVersion projects with generated S1Interop facades should plan a C# 10 migration.");
    }

    private void DuplicateLangVersionUsesEffectiveLastValueForSdkFacadeSupport()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string tempProject = Path.Combine(tempRoot, "DuplicateLangVersionMod.csproj");
            File.WriteAllText(
                tempProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>netstandard2.1</TargetFramework>
                    <LangVersion>default</LangVersion>
                    <LangVersion>latest</LangVersion>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(tempRoot, "Core.cs"),
                """
                using ScheduleOne.DevUtilities;

                namespace DuplicateLangVersionMod;

                public static class Core
                {
                    public static void Touch() => PlayerSingleton<PlayerCamera>.InstanceExists.ToString();
                }
                """);

            ProjectAnalysis project = analyzer.Analyze(tempProject).Projects.Single();

            Assert(
                new SdkFacadeGenerator().Plan(project).HasContent,
                "Fixture should require an SDK facade so LangVersion support is evaluated.");
            Assert(
                project.Diagnostics.All(diagnostic => diagnostic.RuleId != "global_usings_require_langversion"),
                "Duplicate LangVersion properties should use the effective last value.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void DuplicateLangVersionRealModsDoNotRequireCSharp10Migration()
    {
        ProjectAnalysis hoverboard = AnalyzeProject(@"Hoverboard\Hoverboard.csproj");
        Assert(
            hoverboard.Diagnostics.All(diagnostic => diagnostic.RuleId != "global_usings_require_langversion"),
            "Hoverboard's later LangVersion=latest should satisfy generated facade support.");

        ProjectAnalysis modernCheatMenu = AnalyzeProject(@"Modern-Cheat-Menu\Cheat Menu\Modern Cheat Menu.csproj");
        Assert(
            modernCheatMenu.Diagnostics.All(diagnostic => diagnostic.RuleId != "global_usings_require_langversion"),
            "Modern-Cheat-Menu's later LangVersion=latest should satisfy generated facade support.");
    }

    private void MigrationApplyAndRollbackWorkOnCopiedFixture()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string sourceDirectory = Path.Combine(WorkspaceRoot, "JackpotEveryTime");
            CopyFixtureDirectory(sourceDirectory, tempRoot);
            string tempProject = Path.Combine(tempRoot, "JackpotEveryTime.csproj");
            string generatedFacade = Path.Combine(tempRoot, "S1Interop.Generated", "S1Interop.GlobalUsings.g.cs");

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            MigrationPlan plan = new MigrationPlanner().Plan(before);
            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);

            Assert(File.Exists(applyResult.ManifestPath), "Migration manifest was not written.");
            Assert(File.Exists(Path.Combine(tempRoot, "local.build.props")), "local.build.props was not created.");
            Assert(File.Exists(Path.Combine(tempRoot, "local.build.props.example")), "local.build.props.example was not created.");
            Assert(File.Exists(generatedFacade), "SDK facade was not generated.");

            string facadeSource = File.ReadAllText(generatedFacade);
            Assert(
                facadeSource.Contains("global using ScheduleOne.Casino;", StringComparison.Ordinal),
                "Generated facade should include Mono casino namespace.");
            Assert(
                facadeSource.Contains("global using Il2CppScheduleOne.Casino;", StringComparison.Ordinal),
                "Generated facade should include IL2CPP casino namespace.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            Assert(
                !after.Diagnostics.Any(diagnostic => diagnostic.Severity == CoreDiagnosticSeverity.Error),
                "Copied JackpotEveryTime fixture should have no error diagnostics after migration apply.");
            Assert(
                after.Diagnostics.All(diagnostic => diagnostic.RuleId != "local_path_in_project"),
                "Copied JackpotEveryTime fixture should not retain committed local path diagnostics after migration apply.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempProject), "Rollback did not restore the copied project file.");
            Assert(!File.Exists(Path.Combine(tempRoot, "local.build.props")), "Rollback did not remove generated local.build.props.");
            Assert(rollbackResult.RemovedFiles.Contains(generatedFacade), "Rollback did not report removing the generated SDK facade.");
            Assert(!File.Exists(generatedFacade), "Rollback did not remove the generated SDK facade.");

            WorkspaceAnalysis rolledBack = analyzer.Analyze(tempProject);
            Assert(
                rolledBack.Diagnostics.Any(diagnostic => diagnostic.RuleId == "wrong_target_framework"),
                "Rollback should restore the original wrong_target_framework diagnostic.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void MigrationApplyAndRollbackFixRuntimeDefinesOnCopiedFixture()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string sourceProject = Path.Combine(WorkspaceRoot, @"BetterJukebox\BetterJukebox.csproj");
            string sourceCore = Path.Combine(WorkspaceRoot, @"BetterJukebox\Core.cs");
            string tempProject = Path.Combine(tempRoot, "BetterJukebox.csproj");
            string tempCore = Path.Combine(tempRoot, "Core.cs");
            File.Copy(sourceProject, tempProject);
            File.Copy(sourceCore, tempCore);

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            ProjectAnalysis beforeProject = before.Projects.Single();
            AssertHasDiagnostic(beforeProject, "missing_runtime_define", "Mono");
            AssertHasDiagnostic(beforeProject, "missing_runtime_define", "IL2CPP");

            MigrationPlan plan = new MigrationPlanner().Plan(before);
            Assert(
                plan.Projects.Single().Operations.Any(operation => operation.RuleId == "missing_runtime_define"),
                "Expected missing_runtime_define migration operations for BetterJukebox.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "missing_runtime_define"),
                "Migration apply did not apply missing_runtime_define operations.");
            Assert(
                ConfigurationDefines(tempProject, "Mono").Contains("MONO"),
                "Mono configuration should define MONO after migration.");
            Assert(
                ConfigurationDefines(tempProject, "IL2CPP").Contains("IL2CPP"),
                "IL2CPP configuration should define IL2CPP after migration.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            Assert(
                after.Diagnostics.All(diagnostic => diagnostic.RuleId != "missing_runtime_define"),
                "Copied BetterJukebox fixture should not retain missing runtime define diagnostics after migration apply.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempProject), "Rollback did not restore the BetterJukebox project file.");
            Assert(
                !ConfigurationDefines(tempProject, "Mono").Contains("MONO"),
                "Rollback should remove the migrated MONO define.");
            Assert(
                !ConfigurationDefines(tempProject, "IL2CPP").Contains("IL2CPP"),
                "Rollback should remove the migrated IL2CPP define.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void DualRuntimeMigrationScaffoldsS1DsPlayerListFixture()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string sourceDirectory = Path.Combine(WorkspaceRoot, @"DedicatedServerAddons\S1DS-PlayerList");
            CopyFixtureDirectory(sourceDirectory, tempRoot);
            string tempProject = Path.Combine(tempRoot, "S1DS-PlayerList.csproj");
            string tempClientSource = Path.Combine(tempRoot, "S1DSPlayerListClientMod.cs");
            string generatedFacade = Path.Combine(tempRoot, "S1Interop.Generated", "S1Interop.GlobalUsings.g.cs");

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            MigrationPlan plan = new MigrationPlanner().Plan(before, new MigrationPlannerOptions(DualRuntime: true));
            ProjectMigrationPlan projectPlan = plan.Projects.Single();

            Assert(
                projectPlan.Operations.Any(operation => operation.RuleId == "add_il2cpp_configuration"),
                "Expected dual-runtime migration to add IL2CPP configurations.");
            Assert(
                projectPlan.Operations.Any(operation =>
                    operation.RuleId == "conditionalize_scheduleone_usings" &&
                    string.Equals(operation.FilePath, tempClientSource, StringComparison.OrdinalIgnoreCase)),
                "Expected dual-runtime migration to conditionalize S1DS client ScheduleOne usings.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "add_il2cpp_configuration"),
                "Dual-runtime apply did not scaffold IL2CPP configurations.");
            Assert(
                applyResult.Operations.Any(operation => operation.RuleId == "conditionalize_scheduleone_usings"),
                "Dual-runtime apply did not conditionalize source usings.");

            string projectText = File.ReadAllText(tempProject);
            Assert(projectText.Contains("Il2cpp_Client", StringComparison.Ordinal), "Scaffolded project should include Il2cpp_Client.");
            Assert(projectText.Contains("Il2cpp_Server", StringComparison.Ordinal), "Scaffolded project should include Il2cpp_Server.");
            Assert(projectText.Contains("<TargetFramework>net6.0</TargetFramework>", StringComparison.Ordinal), "IL2CPP configs should target net6.0.");
            Assert(projectText.Contains("<DefineConstants>IL2CPP;CLIENT</DefineConstants>", StringComparison.Ordinal), "IL2CPP client config should define IL2CPP;CLIENT.");
            Assert(projectText.Contains("<DefineConstants>IL2CPP;SERVER</DefineConstants>", StringComparison.Ordinal), "IL2CPP server config should define IL2CPP;SERVER.");
            Assert(projectText.Contains("DedicatedServerMod_Il2cpp_Client", StringComparison.Ordinal), "Client references should target the IL2CPP DedicatedServerMod assembly.");
            Assert(projectText.Contains("Il2CppFishNet.Runtime", StringComparison.Ordinal), "FishNet reference should be rewritten to the IL2CPP wrapper assembly.");
            Assert(projectText.Contains("Il2CppInterop.Runtime", StringComparison.Ordinal), "IL2CPP configs should reference Il2CppInterop.Runtime.");
            Assert(projectText.Contains("S1DSModSearchPath", StringComparison.Ordinal), "IL2CPP configs should retain the S1DS mod search path fallback.");

            string clientSource = File.ReadAllText(tempClientSource);
            Assert(
                !clientSource.Contains("using ScheduleOne.PlayerScripts;", StringComparison.Ordinal) &&
                !clientSource.Contains("using Il2CppScheduleOne.PlayerScripts;", StringComparison.Ordinal),
                "Client source should let the generated facade own normal ScheduleOne namespace imports.");
            Assert(File.Exists(generatedFacade), "Dual-runtime migration should generate the SDK facade.");
            string facadeSource = File.ReadAllText(generatedFacade);
            Assert(
                facadeSource.Contains("global using ScheduleOne.PlayerScripts;", StringComparison.Ordinal) &&
                facadeSource.Contains("global using Il2CppScheduleOne.PlayerScripts;", StringComparison.Ordinal),
                "Generated facade should provide Mono and IL2CPP ScheduleOne.PlayerScripts imports.");
            Assert(
                projectText.Contains(@"<Compile Include=""S1Interop.Generated\S1Interop.GlobalUsings.g.cs""", StringComparison.Ordinal),
                "Projects with EnableDefaultCompileItems=false should explicitly compile the generated facade.");
            AssertHasUnconditionedCompileInclude(tempProject, @"S1Interop.Generated\S1Interop.GlobalUsings.g.cs");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempProject), "Rollback did not restore the S1DS project file.");
            Assert(rollbackResult.RestoredFiles.Contains(tempClientSource), "Rollback did not restore the S1DS client source file.");

            string rolledBackProject = File.ReadAllText(tempProject);
            string rolledBackClientSource = File.ReadAllText(tempClientSource);
            Assert(!rolledBackProject.Contains("Il2cpp_Client", StringComparison.Ordinal), "Rollback should remove scaffolded Il2cpp_Client config.");
            Assert(!rolledBackProject.Contains("S1Interop.GlobalUsings.g.cs", StringComparison.Ordinal), "Rollback should remove the generated facade Compile include.");
            Assert(rolledBackClientSource.Contains("using ScheduleOne.PlayerScripts;", StringComparison.Ordinal), "Rollback should restore the unconditional ScheduleOne using.");
            Assert(!rolledBackClientSource.Contains("using Il2CppScheduleOne.PlayerScripts;", StringComparison.Ordinal), "Rollback should remove the generated IL2CPP using.");
            Assert(!File.Exists(generatedFacade), "Rollback should remove the generated SDK facade.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private void DualRuntimeMigrationAddsGeneratedMonoGuardDefines()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string sourceDirectory = Path.Combine(WorkspaceRoot, @"DedicatedServerAddons\SeparateOrganisations.POC");
            CopyFixtureDirectory(sourceDirectory, tempRoot);
            string tempProject = Path.Combine(tempRoot, "SeparateOrganisations.csproj");
            string tempModelSource = Path.Combine(tempRoot, "SeparateOrgs.Models.cs");
            string generatedFacade = Path.Combine(tempRoot, "S1Interop.Generated", "S1Interop.GlobalUsings.g.cs");

            WorkspaceAnalysis before = analyzer.Analyze(tempProject);
            MigrationPlan plan = new MigrationPlanner().Plan(before, new MigrationPlannerOptions(DualRuntime: true));
            ProjectMigrationPlan projectPlan = plan.Projects.Single();

            Assert(
                projectPlan.Operations.Any(operation =>
                    operation.RuleId == "missing_runtime_define" &&
                    operation.Configuration == "Mono_Client"),
                "Dual-runtime source guard generation should plan MONO for Mono_Client.");
            Assert(
                projectPlan.Operations.Any(operation =>
                    operation.RuleId == "missing_runtime_define" &&
                    operation.Configuration == "Mono_Server"),
                "Dual-runtime source guard generation should plan MONO for Mono_Server.");

            MigrationApplyResult applyResult = new MigrationApplier().Apply(plan);
            Assert(
                applyResult.Operations.Count(operation => operation.RuleId == "missing_runtime_define") == 2,
                "Dual-runtime apply should add MONO to both existing Mono configurations.");
            string projectText = File.ReadAllText(tempProject);
            Assert(
                projectText.Contains(@"<Compile Include=""S1Interop.Generated\S1Interop.GlobalUsings.g.cs""", StringComparison.Ordinal),
                "SeparateOrganisations should explicitly compile the generated facade because default compile items are disabled.");
            AssertHasUnconditionedCompileInclude(tempProject, @"S1Interop.Generated\S1Interop.GlobalUsings.g.cs");

            IReadOnlyList<string> clientDefines = ConfigurationDefines(tempProject, "Mono_Client");
            IReadOnlyList<string> serverDefines = ConfigurationDefines(tempProject, "Mono_Server");
            Assert(clientDefines.Contains("CLIENT"), "Mono_Client should retain CLIENT.");
            Assert(clientDefines.Contains("MONO"), "Mono_Client should define MONO after migration.");
            Assert(serverDefines.Contains("SERVER"), "Mono_Server should retain SERVER.");
            Assert(serverDefines.Contains("MONO"), "Mono_Server should define MONO after migration.");

            string modelSource = File.ReadAllText(tempModelSource);
            Assert(
                !modelSource.Contains("using ScheduleOne.DevUtilities;", StringComparison.Ordinal) &&
                !modelSource.Contains("using ScheduleOne.Persistence;", StringComparison.Ordinal),
                "Ordinary ScheduleOne usings should move out of SeparateOrgs.Models.cs when the facade owns them.");
            Assert(File.Exists(generatedFacade), "Dual-runtime migration should generate a facade for SeparateOrganisations.");
            string facadeSource = File.ReadAllText(generatedFacade);
            Assert(
                facadeSource.Contains("global using ScheduleOne.DevUtilities;", StringComparison.Ordinal) &&
                facadeSource.Contains("global using Il2CppScheduleOne.DevUtilities;", StringComparison.Ordinal) &&
                facadeSource.Contains("global using ScheduleOne.Persistence;", StringComparison.Ordinal) &&
                facadeSource.Contains("global using Il2CppScheduleOne.Persistence;", StringComparison.Ordinal),
                "Generated facade should contain Mono and IL2CPP imports for SeparateOrganisations ScheduleOne namespaces.");

            WorkspaceAnalysis after = analyzer.Analyze(tempProject);
            Assert(
                after.Diagnostics.All(diagnostic => diagnostic.RuleId != "missing_runtime_define"),
                "Migrated SeparateOrganisations fixture should not retain missing runtime define diagnostics.");

            MigrationRollbackResult rollbackResult = new MigrationApplier().Rollback(applyResult.ManifestPath);
            Assert(rollbackResult.RestoredFiles.Contains(tempProject), "Rollback did not restore the SeparateOrganisations project file.");
            Assert(rollbackResult.RestoredFiles.Contains(tempModelSource), "Rollback did not restore the SeparateOrganisations model source file.");
            string rolledBackProjectText = File.ReadAllText(tempProject);
            Assert(
                !rolledBackProjectText.Contains("S1Interop.GlobalUsings.g.cs", StringComparison.Ordinal),
                "Rollback should remove the SeparateOrganisations generated facade Compile include.");
            Assert(
                !ConfigurationDefines(tempProject, "Mono_Client").Contains("MONO"),
                "Rollback should remove generated MONO from Mono_Client.");
            Assert(
                !ConfigurationDefines(tempProject, "Mono_Server").Contains("MONO"),
                "Rollback should remove generated MONO from Mono_Server.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private ProjectAnalysis AnalyzeProject(string relativePath)
    {
        string path = Path.Combine(WorkspaceRoot, relativePath);
        WorkspaceAnalysis analysis = analyzer.Analyze(path);
        Assert(analysis.Projects.Count == 1, $"Expected one project for {relativePath}, found {analysis.Projects.Count}.");
        return analysis.Projects[0];
    }
}
