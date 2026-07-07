namespace S1Interop.Generators;

internal static class GeneratorConstants
{
    public const string AttributeMetadataName = "S1Interop.S1InteropTypeAttribute";
    public const string NamespaceAttributeMetadataName = "S1Interop.S1InteropNamespaceAttribute";
    public const string MemberAttributeMetadataName = "S1Interop.S1InteropMemberAttribute";
    public const string PatchAttributeMetadataName = "S1Interop.S1InteropPatchAttribute";
    public const string PatchPrefixAttributeMetadataName = "S1Interop.S1InteropPrefixAttribute";
    public const string PatchPostfixAttributeMetadataName = "S1Interop.S1InteropPostfixAttribute";
    public const string PatchFinalizerAttributeMetadataName = "S1Interop.S1InteropFinalizerAttribute";
    public const string UnityEventBridgeAttributeMetadataName = "S1Interop.S1InteropGenerateUnityEventBridgeAttribute";
    public const string DelegateEventBridgeAttributeMetadataName = "S1Interop.S1InteropGenerateDelegateEventBridgeAttribute";

    public static readonly string[] DefaultIl2CppRuntimeProbeTypeNames =
    [
        "Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase",
        "Il2CppSystem.Object",
        "Il2CppScheduleOne.GameManager"
    ];

    public static readonly string[] DefaultIl2CppRuntimeProbeAssemblyNames =
    [
        "Il2CppInterop.Runtime",
        "Il2Cppmscorlib",
        "Il2CppAssembly-CSharp"
    ];

    public static readonly string[] DefaultMonoRuntimeProbeTypeNames =
    [
        "ScheduleOne.GameManager",
        "ScheduleOne.PlayerScripts.PlayerCamera"
    ];

    public static readonly string[] DefaultMonoRuntimeProbeAssemblyNames =
    [
        "Assembly-CSharp"
    ];

    public static readonly string[] DefaultGameAssemblyProbeNames =
    [
        "Assembly-CSharp",
        "Il2CppAssembly-CSharp",
        "Il2CppScheduleOne.Core",
        "com.rlabrecque.steamworks.net",
        "Il2Cppcom.rlabrecque.steamworks.net"
    ];
}
