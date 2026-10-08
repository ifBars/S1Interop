internal sealed partial class S1InteropFixtureTests
{
    private void S1InteropTypeRegistryGeneratorDiscoversInheritedPublicMembers()
    {
        MetadataReference monoGameReference = CreateMetadataReferenceFromSource(
            "InheritedMembersMonoGame",
            """
            namespace ScheduleOne.Inheritance
            {
                public class BaseThing
                {
                    public int InheritedValue;
                    public static int InheritedStaticValue;
                    public int HiddenValue;
                    public int MonoOnlyBase;
                    public int IncompatibleBase;

                    public string InheritedMethod(string text) => text;
                    public static string InheritedStaticMethod() => "mono";
                    public string HiddenMethod() => "base";

                    public void Overloaded(int value)
                    {
                    }
                }

                public sealed class DerivedThing : BaseThing
                {
                    public new string HiddenValue = "";
                    public new string HiddenMethod() => "derived";

                    public void Overloaded()
                    {
                    }
                }
            }
            """);
        MetadataReference il2CppGameReference = CreateMetadataReferenceFromSource(
            "InheritedMembersIl2CppGame",
            """
            namespace Il2CppScheduleOne.Inheritance
            {
                public class BaseThing
                {
                    public int InheritedValue;
                    public static int InheritedStaticValue;
                    public int HiddenValue;
                    public string IncompatibleBase = "";

                    public string InheritedMethod(string text) => text;
                    public static string InheritedStaticMethod() => "il2cpp";
                    public string HiddenMethod() => "base";

                    public void Overloaded(int value)
                    {
                    }
                }

                public sealed class DerivedThing : BaseThing
                {
                    public new string HiddenValue = "";
                    public new string HiddenMethod() => "derived";

                    public void Overloaded()
                    {
                    }
                }
            }
            """);
        const string source =
            """
            [assembly: S1Interop.S1InteropType("ScheduleOne.Inheritance.DerivedThing", Alias = "DerivedThing")]

            namespace SyntheticMod;
            """;

        string generated = RunTypeRegistryGenerator(
            source,
            [monoGameReference, il2CppGameReference],
            "IL2CPP");

        Assert(
            generated.Contains("public const string DerivedThingInheritedValueName = \"InheritedValue\";", StringComparison.Ordinal) &&
            generated.Contains("public const string DerivedThingInheritedStaticValueName = \"InheritedStaticValue\";", StringComparison.Ordinal) &&
            generated.Contains("public const string DerivedThingInheritedMethodName = \"InheritedMethod\";", StringComparison.Ordinal) &&
            generated.Contains("public const string DerivedThingInheritedStaticMethodName = \"InheritedStaticMethod\";", StringComparison.Ordinal) &&
            generated.Contains("public static int? GetInheritedValue(Handle instance)", StringComparison.Ordinal) &&
            generated.Contains("public static int? GetInheritedStaticValue()", StringComparison.Ordinal) &&
            generated.Contains("public string? InheritedMethod(string? text)", StringComparison.Ordinal) &&
            generated.Contains("public static string? InheritedStaticMethod()", StringComparison.Ordinal) &&
            generated.Contains("BindingFlags.Static | global::System.Reflection.BindingFlags.FlattenHierarchy", StringComparison.Ordinal),
            $"Derived type facades should expose compatible inherited public fields and methods. Generated source:{Environment.NewLine}{generated}");
        Assert(
            generated.Contains("public static string? GetHiddenValue(Handle instance)", StringComparison.Ordinal) &&
            !generated.Contains("public static int? GetHiddenValue(Handle instance)", StringComparison.Ordinal) &&
            generated.Contains("public const string DerivedThingHiddenMethodName = \"HiddenMethod\";", StringComparison.Ordinal),
            $"Derived hidden value and method members should take precedence over same-named inherited members. Generated source:{Environment.NewLine}{generated}");
        Assert(
            !generated.Contains("DerivedThingOverloadedName", StringComparison.Ordinal) &&
            !generated.Contains("DerivedThingMonoOnlyBaseName", StringComparison.Ordinal) &&
            !generated.Contains("DerivedThingIncompatibleBaseName", StringComparison.Ordinal),
            $"Inherited overloads and members missing or incompatible on one backend should remain undiscovered. Generated source:{Environment.NewLine}{generated}");
    }
}
