internal sealed partial class S1InteropFixtureTests
{
    private void S1InteropMemberDiagnosticsRejectKindAndStaticShapeMismatches()
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

            namespace ScheduleOne.UI
            {
                public class HudBase
                {
                    public static int InheritedStaticCount;

                    public int InheritedCount { get; set; }

                    public void InheritedRefresh()
                    {
                    }
                }

                public sealed class Hud : HudBase
                {
                    public static int StaticCount;

                    public int InstanceCount { get; set; }

                    public void Refresh()
                    {
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

            namespace Il2CppScheduleOne.UI
            {
                public class HudBase
                {
                    public static int InheritedStaticCount;

                    public int InheritedCount { get; set; }

                    public void InheritedRefresh()
                    {
                    }
                }

                public sealed class Hud : HudBase
                {
                    public static int StaticCount;

                    public int InstanceCount { get; set; }

                    public void Refresh()
                    {
                    }
                }
            }
            """);
        const string source =
            """
            [assembly: S1Interop.S1InteropType("ScheduleOne.UI.Hud", Alias = "Hud")]

            [assembly: S1Interop.S1InteropMember("Hud", "StaticCount", Alias = "StaticAsInstance", Kind = S1Interop.S1InteropMemberKind.Field)]
            [assembly: S1Interop.S1InteropMember("Hud", "InstanceCount", Alias = "InstanceAsStatic", Kind = S1Interop.S1InteropMemberKind.Property, IsStatic = true)]
            [assembly: S1Interop.S1InteropMember("Hud", "Refresh", Alias = "MethodAsValue")]

            [assembly: S1Interop.S1InteropMember("Hud", "StaticCount", Alias = "ValidStatic", Kind = S1Interop.S1InteropMemberKind.Field, IsStatic = true)]
            [assembly: S1Interop.S1InteropMember("Hud", "InstanceCount", Alias = "ValidInstance", Kind = S1Interop.S1InteropMemberKind.Property)]
            [assembly: S1Interop.S1InteropMember("Hud", "Refresh", Alias = "ValidMethod", Kind = S1Interop.S1InteropMemberKind.Method)]
            [assembly: S1Interop.S1InteropMember("Hud", "InheritedStaticCount", Alias = "ValidInheritedStatic", Kind = S1Interop.S1InteropMemberKind.Field, IsStatic = true)]
            [assembly: S1Interop.S1InteropMember("Hud", "InheritedCount", Alias = "ValidInheritedInstance", Kind = S1Interop.S1InteropMemberKind.Property)]
            [assembly: S1Interop.S1InteropMember("Hud", "InheritedRefresh", Alias = "ValidInheritedMethod", Kind = S1Interop.S1InteropMemberKind.Method)]

            namespace SyntheticMod;
            """;

        ImmutableArray<Diagnostic> diagnostics = RunS1InteropGeneratorDiagnostics(
            source,
            [monoGameReference, il2CppGameReference]);
        Diagnostic[] missingMemberDiagnostics = diagnostics
            .Where(diagnostic => diagnostic.Id == "S1I003")
            .ToArray();

        Assert(
            missingMemberDiagnostics.Length == 6,
            $"Each invalid member kind/static shape should report once for Mono and IL2CPP while valid declared and inherited counterparts stay accepted. Diagnostics: {string.Join(Environment.NewLine, diagnostics)}");
        Assert(
            missingMemberDiagnostics.Count(diagnostic => diagnostic.GetMessage().Contains("StaticCount", StringComparison.Ordinal)) == 2 &&
            missingMemberDiagnostics.Count(diagnostic => diagnostic.GetMessage().Contains("InstanceCount", StringComparison.Ordinal)) == 2 &&
            missingMemberDiagnostics.Count(diagnostic => diagnostic.GetMessage().Contains("Refresh", StringComparison.Ordinal)) == 2,
            $"S1I003 diagnostics should identify the three mismatched declarations on both runtime surfaces. Diagnostics: {string.Join(Environment.NewLine, missingMemberDiagnostics.Select(diagnostic => diagnostic.ToString()))}");
    }
}
