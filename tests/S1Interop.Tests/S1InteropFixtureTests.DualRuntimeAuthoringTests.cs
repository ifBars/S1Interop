using System.Text.Json;
using Microsoft.CodeAnalysis.Diagnostics;

internal sealed partial class S1InteropFixtureTests
{
    // Shared by both runtime builds below. It must compile without #if blocks: that is the point of the helpers.
    private const string DualRuntimeHelperUsings =
        """
        using System.Linq;
        using S1Interop;

        """;

    private const string DualRuntimeHelperUsageSource =
        """
        namespace SyntheticMod
        {
            public static class Usage
            {
                public static string Run()
                {
                    var results = new System.Collections.Generic.List<string>();
                    Game.Npc plain = new Game.Npc();
                    Game.Npc employeeAsNpc = Game.Factory.EmployeeTypedAsNpc();

                    results.Add(plain.TryCast<Game.Employee>() is null ? "plain:null" : "plain:employee");
                    results.Add(employeeAsNpc.TryCast<Game.Employee>() is null ? "proxy:null" : "proxy:employee");
                    results.Add(((object)"text").TryCast<Game.Npc>() is null ? "string:null" : "string:npc");
                    results.Add(((object)employeeAsNpc).Is(out Game.Employee employee) && employee is not null ? "is:true" : "is:false");
                    try
                    {
                        plain.Cast<Game.Employee>();
                        results.Add("cast:succeeded");
                    }
                    catch (System.InvalidCastException)
                    {
                        results.Add("cast:invalid");
                    }

                    results.Add($"linq:{Game.Registry.Npcs.AsEnumerable().Count()}");
                    results.Add($"managed:{Game.Registry.Npcs.ToManagedList().Count}");
                    results.Add($"native:{new[] { plain, plain }.ToNativeList().Count}");
                    results.Add($"prices:{Game.Registry.Prices.AsEnumerable().Sum(pair => pair.Value)}");
                    results.Add($"dictionary:{Game.Registry.Prices.ToManagedDictionary().Count}");

                    var clicked = new UnityEngine.Events.UnityEvent();
                    int clicks = 0;
                    clicked.AddListener(() => clicks++);
                    System.IDisposable subscription = clicked.Subscribe(() => clicks += 10);
                    clicked.Invoke();
                    subscription.Dispose();
                    subscription.Dispose();
                    clicked.Invoke();
                    results.Add($"clicks:{clicks}:{clicked.ListenerCount}");

                    var slider = new UnityEngine.Events.UnityEvent<int>();
                    int total = 0;
                    slider.AddListener(value => total += value);
                    slider.Invoke(5);
                    results.Add($"slider:{total}");
                    return string.Join("|", results);
                }
            }
        }
        """;

    // Minimal stand-ins for the shapes the helpers rely on. The IL2CPP half mirrors Il2CppInterop: proxies are typed by
    // their declared wrapper, collections are not IEnumerable<T>, and UnityAction is a class with an implicit conversion.
    private const string DualRuntimeHelperSurfaceSource =
        """
        #if IL2CPP
        namespace Il2CppInterop.Runtime.InteropTypes
        {
            public class Il2CppObjectBase
            {
                public System.Type NativeClass { get; set; }

                public T TryCast<T>() where T : Il2CppObjectBase =>
                    NativeClass is not null && typeof(T).IsAssignableFrom(NativeClass)
                        ? (T)System.Activator.CreateInstance(typeof(T))
                        : null;
            }
        }

        namespace Il2CppSystem.Collections.Generic
        {
            public class List<T> : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
            {
                private readonly System.Collections.Generic.List<T> items = new System.Collections.Generic.List<T>();
                public int Count => items.Count;
                public T this[int index] => items[index];
                public void Add(T item) => items.Add(item);
            }

            public class Dictionary<TKey, TValue> : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
            {
                private readonly System.Collections.Generic.Dictionary<TKey, TValue> items = new System.Collections.Generic.Dictionary<TKey, TValue>();
                public void Add(TKey key, TValue value) => items.Add(key, value);
                public System.Collections.Generic.Dictionary<TKey, TValue>.Enumerator GetEnumerator() => items.GetEnumerator();
            }
        }

        namespace UnityEngine.Events
        {
            public sealed class UnityAction
            {
                private readonly System.Action action;
                private UnityAction(System.Action action) => this.action = action;
                public static implicit operator UnityAction(System.Action action) => new UnityAction(action);
                public void Invoke() => action();
            }

            public sealed class UnityAction<T0>
            {
                private readonly System.Action<T0> action;
                private UnityAction(System.Action<T0> action) => this.action = action;
                public static implicit operator UnityAction<T0>(System.Action<T0> action) => new UnityAction<T0>(action);
                public void Invoke(T0 value) => action(value);
            }
        }

        namespace Game
        {
            public class Npc : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
            {
            }

            public class Employee : Npc
            {
            }

            public static class Factory
            {
                public static Npc EmployeeTypedAsNpc() => new Npc { NativeClass = typeof(Employee) };
            }

            public static class Registry
            {
                public static Il2CppSystem.Collections.Generic.List<Npc> Npcs { get; } = CreateNpcs();
                public static Il2CppSystem.Collections.Generic.Dictionary<string, int> Prices { get; } = CreatePrices();

                private static Il2CppSystem.Collections.Generic.List<Npc> CreateNpcs()
                {
                    var list = new Il2CppSystem.Collections.Generic.List<Npc>();
                    list.Add(new Npc());
                    list.Add(new Npc());
                    list.Add(new Npc());
                    return list;
                }

                private static Il2CppSystem.Collections.Generic.Dictionary<string, int> CreatePrices()
                {
                    var prices = new Il2CppSystem.Collections.Generic.Dictionary<string, int>();
                    prices.Add("a", 2);
                    prices.Add("b", 4);
                    return prices;
                }
            }
        }
        #else
        namespace UnityEngine.Events
        {
            public delegate void UnityAction();
            public delegate void UnityAction<T0>(T0 value);
        }

        namespace Game
        {
            public class Npc
            {
            }

            public class Employee : Npc
            {
            }

            public static class Factory
            {
                public static Npc EmployeeTypedAsNpc() => new Employee();
            }

            public static class Registry
            {
                public static System.Collections.Generic.List<Npc> Npcs { get; } = new System.Collections.Generic.List<Npc> { new Npc(), new Npc(), new Npc() };
                public static System.Collections.Generic.Dictionary<string, int> Prices { get; } = new System.Collections.Generic.Dictionary<string, int> { ["a"] = 2, ["b"] = 4 };
            }
        }
        #endif

        namespace UnityEngine.Events
        {
            // Removal matches by reference, as native IL2CPP delegates do; a re-converted listener is never removed.
            public sealed class UnityEvent
            {
                private readonly System.Collections.Generic.List<UnityAction> listeners = new System.Collections.Generic.List<UnityAction>();
                public int ListenerCount => listeners.Count;
                public void AddListener(UnityAction listener) => listeners.Add(listener);
                public void RemoveListener(UnityAction listener) => listeners.RemoveAll(existing => ReferenceEquals(existing, listener));

                public void Invoke()
                {
                    foreach (UnityAction listener in listeners.ToArray())
                    {
                        listener.Invoke();
                    }
                }
            }

            public sealed class UnityEvent<T0>
            {
                private readonly System.Collections.Generic.List<UnityAction<T0>> listeners = new System.Collections.Generic.List<UnityAction<T0>>();
                public void AddListener(UnityAction<T0> listener) => listeners.Add(listener);
                public void RemoveListener(UnityAction<T0> listener) => listeners.RemoveAll(existing => ReferenceEquals(existing, listener));

                public void Invoke(T0 value)
                {
                    foreach (UnityAction<T0> listener in listeners.ToArray())
                    {
                        listener.Invoke(value);
                    }
                }
            }
        }
        """;

    private void RuntimeHelpersRunTheSameSourceOnMonoAndIl2Cpp()
    {
        const string expected =
            "plain:null|proxy:employee|string:null|is:true|cast:invalid|linq:3|managed:3|native:2|prices:6|dictionary:2|clicks:12:1|slider:5";
        string source = DualRuntimeHelperUsings + DualRuntimeHelperSurfaceSource + Environment.NewLine + DualRuntimeHelperUsageSource;

        foreach (string runtime in new[] { "MONO", "IL2CPP" })
        {
            IReadOnlyDictionary<string, string> generated = RunS1InteropGenerator(source, runtime);
            string helpers = generated.GetValueOrDefault("S1Interop.RuntimeHelpers.g.cs") ?? string.Empty;
            Assert(
                helpers.Contains("internal static class S1InteropCastExtensions", StringComparison.Ordinal) &&
                helpers.Contains("internal static class S1InteropUnityEventExtensions", StringComparison.Ordinal) &&
                helpers.Contains("Il2CppSystem.Collections.Generic.List", StringComparison.Ordinal) == (runtime == "IL2CPP"),
                $"{runtime} builds should emit typed helpers for the referenced surface. Generated:{Environment.NewLine}{helpers}");

            System.Reflection.Assembly assembly = CompileAndLoadS1InteropGeneratedAssembly(source, assemblyName: null, runtime);
            string actual = (string)assembly.GetType("SyntheticMod.Usage", throwOnError: true)!
                .GetMethod("Run")!
                .Invoke(null, null)!;
            Assert(actual == expected, $"{runtime} helper behavior differed.{Environment.NewLine}Expected: {expected}{Environment.NewLine}Actual:   {actual}");
        }

        IReadOnlyDictionary<string, string> unknown = RunS1InteropGenerator(DualRuntimeHelperSurfaceSource);
        Assert(
            !unknown.ContainsKey("S1Interop.RuntimeHelpers.g.cs"),
            "Builds without a known runtime must not get runtime-specific helpers; backend-neutral code uses facades instead.");
    }

    private void RuntimeHelpersSkipApisWhoseBaseAssembliesAreNotReferenced()
    {
        // IL2CPP UnityAction derives from Il2Cppmscorlib types. A project that references UnityEngine but not
        // Il2Cppmscorlib (the experimental single-assembly scaffold's IL2CPP check) must not get helpers that touch it.
        MetadataReference il2CppMscorlib = CreateMetadataReferenceFromSource(
            "Il2Cppmscorlib",
            "namespace Il2CppSystem { public class Object { } public class MulticastDelegate : Object { } }");
        CSharpCompilation unityCompilation = CSharpCompilation.Create(
            "UnityEngine.CoreModule",
            [CSharpSyntaxTree.ParseText(
                """
                namespace UnityEngine.Events
                {
                    public sealed class UnityAction : Il2CppSystem.MulticastDelegate
                    {
                        public static implicit operator UnityAction(System.Action action) => new UnityAction();
                    }

                    public sealed class UnityAction<T0> : Il2CppSystem.MulticastDelegate
                    {
                        public static implicit operator UnityAction<T0>(System.Action<T0> action) => new UnityAction<T0>();
                    }

                    public class UnityEvent
                    {
                        public void AddListener(UnityAction listener) { }
                        public void RemoveListener(UnityAction listener) { }
                    }

                    public class UnityEvent<T0>
                    {
                        public void AddListener(UnityAction<T0> listener) { }
                        public void RemoveListener(UnityAction<T0> listener) { }
                    }
                }
                """,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest))],
            GetTrustedPlatformReferences().Append(il2CppMscorlib),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var unityImage = new MemoryStream();
        Assert(unityCompilation.Emit(unityImage).Success, "The synthetic IL2CPP UnityEngine reference should compile.");

        Compilation output = RunS1InteropGeneratorCompilation(
            "namespace SyntheticMod { internal static class Core { } }",
            ["IL2CPP"],
            assemblyName: null,
            [MetadataReference.CreateFromImage(unityImage.ToArray())]);
        string helpers = output.SyntaxTrees
            .SingleOrDefault(tree => (tree.FilePath ?? string.Empty).EndsWith("S1Interop.RuntimeHelpers.g.cs", StringComparison.Ordinal))?
            .GetText()
            .ToString() ?? string.Empty;
        Assert(
            !helpers.Contains("S1InteropUnityEventExtensions", StringComparison.Ordinal),
            "UnityEvent helpers need Il2Cppmscorlib on IL2CPP and should be skipped when it is not referenced.");
    }

    private void Il2CppInjectedTypesGetGeneratedConstructorsOnlyForIl2Cpp()
    {
        const string source =
            """
            namespace MelonLoader
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class RegisterTypeInIl2Cpp : System.Attribute
                {
                }
            }

            namespace Il2CppInterop.Runtime.Injection
            {
                public static class ClassInjector
                {
                    public static System.IntPtr DerivedConstructorPointer<T>() => System.IntPtr.Zero;
                    public static void DerivedConstructorBody(object instance) { }
                }
            }

            namespace UnityEngine
            {
            #if IL2CPP
                // Il2CppInterop wrappers expose both a native-pointer constructor and a parameterless one.
                public class Object { public Object() { } public Object(System.IntPtr pointer) { } }
                public class Component : Object { public Component() { } public Component(System.IntPtr pointer) : base(pointer) { } }
                public class MonoBehaviour : Component { public MonoBehaviour() { } public MonoBehaviour(System.IntPtr pointer) : base(pointer) { } }
            #else
                public class Object { }
                public class Component : Object { }
                public class MonoBehaviour : Component { }
            #endif
            }

            namespace Game
            {
                public abstract class ConsoleCommand
                {
            #if IL2CPP
                    protected ConsoleCommand(System.IntPtr pointer) { }
            #endif
                    public abstract string CommandWord { get; }
                }
            }

            namespace SyntheticMod
            {
                [MelonLoader.RegisterTypeInIl2Cpp]
                public partial class Spinner : UnityEngine.MonoBehaviour
                {
                }

                [MelonLoader.RegisterTypeInIl2Cpp]
                public partial class HelloCommand : Game.ConsoleCommand
                {
                    public override string CommandWord => "hello";
                    public static HelloCommand Create() => new HelloCommand();
                }

                [MelonLoader.RegisterTypeInIl2Cpp]
                public partial class DerivedSpinner : Spinner
                {
                }

                [MelonLoader.RegisterTypeInIl2Cpp]
                public class NotPartial : UnityEngine.MonoBehaviour
                {
                }

                public partial class Outer
                {
                    [MelonLoader.RegisterTypeInIl2Cpp]
                    public partial class Nested : UnityEngine.MonoBehaviour
                    {
                    }
                }
            }
            """;

        ImmutableArray<Diagnostic> il2CppDiagnostics = RunS1InteropGeneratorDiagnostics(source, [], "IL2CPP");
        Assert(
            il2CppDiagnostics.All(diagnostic => diagnostic.Severity != RoslynDiagnosticSeverity.Error),
            $"Generated IL2CPP constructors should compile. Diagnostics:{Environment.NewLine}{string.Join(Environment.NewLine, il2CppDiagnostics)}");
        Diagnostic[] missingConstructor = il2CppDiagnostics.Where(diagnostic => diagnostic.Id == "S1I009").ToArray();
        Assert(
            missingConstructor.Length == 1 && missingConstructor[0].GetMessage().Contains("'NotPartial'", StringComparison.Ordinal),
            $"Only the non-partial injected type should report S1I009. Diagnostics:{Environment.NewLine}{string.Join(Environment.NewLine, il2CppDiagnostics)}");

        System.Reflection.Assembly il2Cpp = CompileAndLoadS1InteropGeneratedAssembly(source, assemblyName: null, "IL2CPP");
        Assert(HasIntPtrConstructor(il2Cpp, "SyntheticMod.Spinner") &&
            HasIntPtrConstructor(il2Cpp, "SyntheticMod.DerivedSpinner") &&
            HasIntPtrConstructor(il2Cpp, "SyntheticMod.HelloCommand") &&
            HasIntPtrConstructor(il2Cpp, "SyntheticMod.Outer+Nested"),
            "IL2CPP builds should give every partial injected type, including nested and derived ones, an IntPtr constructor.");
        Assert(
            il2Cpp.GetType("SyntheticMod.HelloCommand")!.GetConstructor(Type.EmptyTypes) is not null &&
            il2Cpp.GetType("SyntheticMod.Spinner")!.GetConstructor(Type.EmptyTypes) is null,
            "Non-component injected types keep a managed constructor; Unity components must not get one.");

        ImmutableArray<Diagnostic> monoDiagnostics = RunS1InteropGeneratorDiagnostics(source, [], "MONO");
        System.Reflection.Assembly mono = CompileAndLoadS1InteropGeneratedAssembly(source, assemblyName: null, "MONO");
        Assert(
            monoDiagnostics.All(diagnostic => diagnostic.Id != "S1I009") && !HasIntPtrConstructor(mono, "SyntheticMod.Spinner"),
            "Mono builds must not require or generate IL2CPP injection constructors.");

        static bool HasIntPtrConstructor(System.Reflection.Assembly assembly, string typeName) =>
            assembly.GetType(typeName, throwOnError: true)!.GetConstructor([typeof(IntPtr)]) is not null;
    }

    private void GeneratorReadsRuntimeAndEmissionSwitchesFromBuildProperties()
    {
        const string melonLoader =
            """
            namespace MelonLoader
            {
                [System.AttributeUsage(System.AttributeTargets.Assembly)]
                public sealed class MelonInfoAttribute : System.Attribute
                {
                    public MelonInfoAttribute(System.Type type, string name, string version, string author) { }
                }

                [System.AttributeUsage(System.AttributeTargets.Assembly)]
                public sealed class MelonPlatformDomainAttribute : System.Attribute
                {
                    public enum CompatibleDomains { UNIVERSAL, MONO, IL2CPP }
                    public MelonPlatformDomainAttribute(CompatibleDomains domain) { }
                }
            }

            namespace SyntheticMod
            {
                public sealed class Core
                {
                }
            }
            """;
        const string melon = "[assembly: MelonLoader.MelonInfo(typeof(SyntheticMod.Core), \"Synthetic\", \"1.0.0\", \"Tests\")]\n" + melonLoader;

        IReadOnlyDictionary<string, string> il2Cpp = RunS1InteropGeneratorWithBuildProperties(
            melon,
            new Dictionary<string, string>
            {
                ["S1InteropTargetRuntime"] = "Il2Cpp",
                ["S1InteropEmitPlatformDomain"] = "true"
            });
        Assert(
            il2Cpp["S1Interop.TypeRegistry.g.cs"].Contains("Backend = S1InteropRuntimeBackend.Il2Cpp;", StringComparison.Ordinal),
            "S1InteropTargetRuntime should select the runtime without MONO/IL2CPP preprocessor symbols.");
        Assert(
            il2Cpp.GetValueOrDefault("S1Interop.PlatformDomain.g.cs")?.Contains("CompatibleDomains.IL2CPP", StringComparison.Ordinal) == true,
            "A melon with an explicit runtime should be pinned to that MelonLoader platform domain.");

        IReadOnlyDictionary<string, string> monoWithoutSwitch = RunS1InteropGeneratorWithBuildProperties(
            melon,
            new Dictionary<string, string> { ["S1InteropTargetRuntime"] = "Mono" });
        Assert(
            !monoWithoutSwitch.ContainsKey("S1Interop.PlatformDomain.g.cs") &&
            monoWithoutSwitch.ContainsKey("S1Interop.RuntimeHelpers.g.cs"),
            "Platform domains are opt-in (the package targets opt in for explicit runtimes); runtime helpers are on by default.");

        IReadOnlyDictionary<string, string> declared = RunS1InteropGeneratorWithBuildProperties(
            "[assembly: MelonLoader.MelonPlatformDomain(MelonLoader.MelonPlatformDomainAttribute.CompatibleDomains.UNIVERSAL)]\n" + melon,
            new Dictionary<string, string>
            {
                ["S1InteropTargetRuntime"] = "Mono",
                ["S1InteropEmitPlatformDomain"] = "true"
            });
        IReadOnlyDictionary<string, string> library = RunS1InteropGeneratorWithBuildProperties(
            melonLoader,
            new Dictionary<string, string>
            {
                ["S1InteropTargetRuntime"] = "Mono",
                ["S1InteropEmitPlatformDomain"] = "true",
                ["S1InteropEmitRuntimeHelpers"] = "false"
            });
        Assert(
            !declared.ContainsKey("S1Interop.PlatformDomain.g.cs") &&
            !library.ContainsKey("S1Interop.PlatformDomain.g.cs") &&
            !library.ContainsKey("S1Interop.RuntimeHelpers.g.cs"),
            "An author-declared domain wins, non-melon libraries get no domain, and runtime helpers can be switched off.");
    }

    private void GeneratorBuildTargetsMapUsingsReferencesRunAndDeploy()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            string monoGame = Path.Combine(tempRoot, "Mono Game");
            string il2CppGame = Path.Combine(tempRoot, "Il2Cpp Game");
            CreateFiles(monoGame,
                Path.Combine("Schedule I_Data", "Managed", "Assembly-CSharp.dll"),
                Path.Combine("Schedule I_Data", "Managed", "UnityEngine.CoreModule.dll"),
                Path.Combine("Schedule I_Data", "Managed", "mscorlib.dll"),
                Path.Combine("Schedule I_Data", "Managed", "System.Core.dll"),
                Path.Combine("Schedule I_Data", "Managed", "Mono.Security.dll"),
                Path.Combine("Schedule I_Data", "Managed", "Edgegap.dll"),
                Path.Combine("MelonLoader", "net35", "MelonLoader.dll"),
                Path.Combine("MelonLoader", "net35", "0Harmony.dll"));
            CreateFiles(il2CppGame,
                Path.Combine("MelonLoader", "Il2CppAssemblies", "Assembly-CSharp.dll"),
                Path.Combine("MelonLoader", "Il2CppAssemblies", "Il2Cppmscorlib.dll"),
                Path.Combine("MelonLoader", "net6", "MelonLoader.dll"),
                Path.Combine("MelonLoader", "net6", "0Harmony.dll"),
                Path.Combine("MelonLoader", "net6", "Il2CppInterop.Runtime.dll"),
                Path.Combine("MelonLoader", "net6", "Il2CppInterop.Common.dll"));

            string buildDirectory = Path.Combine(RepositoryRoot, "src", "S1Interop.Generators", "build");
            string modsPath = Path.Combine(tempRoot, "Mods");
            string projectPath = Path.Combine(tempRoot, "Project", "TargetsMod.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(projectPath)!, "Core.cs"), "namespace TargetsMod { public sealed class Core { } }");
            File.WriteAllText(
                projectPath,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="{Path.Combine(buildDirectory, "S1Interop.Generators.props")}" />
                  <PropertyGroup>
                    <TargetFramework>netstandard2.1</TargetFramework>
                    <LangVersion>10.0</LangVersion>
                    <Configurations>Mono;Il2Cpp</Configurations>
                    <MonoGamePath>{monoGame}</MonoGamePath>
                    <Il2CppGamePath>{il2CppGame}</Il2CppGamePath>
                    <S1InteropModsPath>{modsPath}</S1InteropModsPath>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)'=='Mono'">
                    <S1InteropTargetRuntime>Mono</S1InteropTargetRuntime>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)'=='Il2Cpp'">
                    <S1InteropTargetRuntime>Il2Cpp</S1InteropTargetRuntime>
                  </PropertyGroup>
                  <ItemGroup Condition="'$(S1InteropGameReferences)'=='true'">
                    <S1InteropUsing Include="ScheduleOne.NPCs" />
                    <S1InteropUsing Include="System.Collections.Generic.List&lt;string&gt;" Alias="NativeStringList" />
                  </ItemGroup>
                  <Import Project="{Path.Combine(buildDirectory, "S1Interop.Generators.targets")}" />
                </Project>
                """);

            JsonElement mono = EvaluateProject(projectPath, "Mono", "-p:S1InteropGameReferences=true");
            string[] monoReferences = GetItemIdentities(mono, "Reference");
            Assert(
                GetItemIdentities(mono, "Using").Contains("ScheduleOne.NPCs") &&
                GetItems(mono, "Using").Any(item =>
                    item.GetProperty("Identity").GetString() == "System.Collections.Generic.List<string>" &&
                    item.GetProperty("Alias").GetString() == "NativeStringList"),
                "Mono builds should import game namespaces and aliases unchanged.");
            Assert(
                monoReferences.Any(path => path.EndsWith("Assembly-CSharp.dll", StringComparison.Ordinal)) &&
                monoReferences.Any(path => path.EndsWith("UnityEngine.CoreModule.dll", StringComparison.Ordinal)) &&
                monoReferences.Any(path => path.EndsWith(Path.Combine("net35", "MelonLoader.dll"), StringComparison.Ordinal)) &&
                !monoReferences.Any(path => Path.GetFileName(path) is "mscorlib.dll" or "System.Core.dll" or "Mono.Security.dll" or "Edgegap.dll"),
                $"Mono game references should exclude the game's BCL copies. References:{Environment.NewLine}{string.Join(Environment.NewLine, monoReferences)}");
            Assert(
                mono.GetProperty("Properties").GetProperty("RunCommand").GetString() == Path.Combine(monoGame, "Schedule I.exe") &&
                mono.GetProperty("Properties").GetProperty("S1InteropEmitPlatformDomain").GetString() == "true",
                "dotnet run should start the selected install, and explicit runtimes should opt into a platform domain.");

            JsonElement il2Cpp = EvaluateProject(projectPath, "Il2Cpp", "-p:S1InteropGameReferences=true");
            string[] il2CppReferences = GetItemIdentities(il2Cpp, "Reference");
            Assert(
                GetItemIdentities(il2Cpp, "Using").Contains("Il2CppScheduleOne.NPCs") &&
                GetItemIdentities(il2Cpp, "Using").Contains("Il2CppSystem.Collections.Generic.List<string>"),
                "IL2CPP builds should import the Il2Cpp-prefixed namespaces and alias targets.");
            Assert(
                il2CppReferences.Any(path => path.EndsWith("Il2Cppmscorlib.dll", StringComparison.Ordinal)) &&
                il2CppReferences.Any(path => path.EndsWith("Il2CppInterop.Runtime.dll", StringComparison.Ordinal)) &&
                il2CppReferences.Any(path => path.EndsWith(Path.Combine("net6", "MelonLoader.dll"), StringComparison.Ordinal)),
                $"IL2CPP game references should include generated assemblies and Il2CppInterop. References:{Environment.NewLine}{string.Join(Environment.NewLine, il2CppReferences)}");

            JsonElement diagnosticsOnly = EvaluateProject(projectPath, "Mono");
            string diagnosticsOnlyRunCommand = diagnosticsOnly.GetProperty("Properties").GetProperty("RunCommand").GetString() ?? string.Empty;
            Assert(
                !GetItemIdentities(diagnosticsOnly, "Reference").Any(path => path.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)) &&
                !diagnosticsOnlyRunCommand.Contains("Schedule I.exe", StringComparison.Ordinal),
                "Without S1InteropGameReferences the package must not add references or a run command.");

            ProcessResult deploy = RunDotNet("build", projectPath, "-c", "Mono", "-p:S1InteropDeployToGame=true", "--nologo", "-v:minimal");
            Assert(
                deploy.ExitCode == 0 &&
                File.Exists(Path.Combine(modsPath, "TargetsMod.dll")) &&
                File.Exists(Path.Combine(modsPath, "TargetsMod.pdb")),
                $"Deploy should copy the built DLL and symbols into the Mods folder. Output:{Environment.NewLine}{deploy.Output}");

            // A running game holds the deployed DLL open; that must warn, not fail the build.
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(projectPath)!, "Core.cs"), "namespace TargetsMod { public sealed class Core { public int Changed; } }");
            using (new FileStream(Path.Combine(modsPath, "TargetsMod.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                ProcessResult locked = RunDotNet("build", projectPath, "-c", "Mono", "-p:S1InteropDeployToGame=true", "--nologo", "-v:minimal");
                Assert(
                    locked.ExitCode == 0 && locked.Output.Contains("S1I107", StringComparison.Ordinal),
                    $"A locked Mods DLL should produce warning S1I107 without failing the build. Output:{Environment.NewLine}{locked.Output}");
            }
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }

        static JsonElement EvaluateProject(string projectPath, string configuration, params string[] properties)
        {
            ProcessResult evaluation = RunDotNet(
                [
                    "msbuild",
                    projectPath,
                    $"-p:Configuration={configuration}",
                    .. properties,
                    "-getItem:Using",
                    "-getItem:Reference",
                    "-getProperty:RunCommand",
                    "-getProperty:S1InteropEmitPlatformDomain"
                ]);
            Assert(evaluation.ExitCode == 0, $"Evaluating the targets fixture failed. Output:{Environment.NewLine}{evaluation.Output}");
            return JsonDocument.Parse(evaluation.Output).RootElement;
        }

        static IEnumerable<JsonElement> GetItems(JsonElement evaluation, string itemType) =>
            evaluation.GetProperty("Items").TryGetProperty(itemType, out JsonElement items)
                ? items.EnumerateArray()
                : [];

        static string[] GetItemIdentities(JsonElement evaluation, string itemType) =>
            GetItems(evaluation, itemType)
                .Select(item => item.GetProperty("Identity").GetString() ?? string.Empty)
                .ToArray();
    }

    private void GeneratorDegradesToDiagnosticsBelowCSharp9()
    {
        // netstandard2.1 defaults to C# 8, so this is what a Mono mod gets when it only adds the package for diagnostics.
        const string source =
            """
            [assembly: S1Interop.S1InteropType("System.String", Alias = "StringType")]

            namespace SyntheticMod
            {
                internal static class Core
                {
                }
            }
            """;
        CSharpParseOptions parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.CSharp8)
            .WithPreprocessorSymbols("MONO");
        CSharpCompilation compilation = CSharpCompilation.Create(
            "SyntheticMod." + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        CSharpGeneratorDriver.Create([new S1InteropTypeRegistryGenerator().AsSourceGenerator()], parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> generatorDiagnostics);

        Diagnostic[] diagnostics = generatorDiagnostics.Concat(outputCompilation.GetDiagnostics()).ToArray();
        Assert(
            diagnostics.All(diagnostic => diagnostic.Severity != RoslynDiagnosticSeverity.Error) &&
            diagnostics.Count(diagnostic => diagnostic.Id == "S1I010") == 1 &&
            outputCompilation.SyntaxTrees.All(tree => !(tree.FilePath ?? string.Empty).EndsWith("S1Interop.TypeRegistry.g.cs", StringComparison.Ordinal)),
            $"C# 8 projects should compile with one S1I010 warning instead of errors in generated code. Diagnostics:{Environment.NewLine}{string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()))}");
    }

    private static IReadOnlyDictionary<string, string> RunS1InteropGeneratorWithBuildProperties(
        string source,
        IReadOnlyDictionary<string, string> buildProperties)
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "SyntheticMod." + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new S1InteropTypeRegistryGenerator().AsSourceGenerator()],
            parseOptions: parseOptions,
            optionsProvider: new BuildPropertyOptionsProvider(buildProperties));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> diagnostics);
        Assert(
            diagnostics.Concat(outputCompilation.GetDiagnostics()).All(diagnostic => diagnostic.Severity != RoslynDiagnosticSeverity.Error),
            $"Generated compilation reported errors: {string.Join(Environment.NewLine, diagnostics.Concat(outputCompilation.GetDiagnostics()))}");

        return outputCompilation.SyntaxTrees
            .Where(tree => (tree.FilePath ?? string.Empty).Contains("S1Interop.Generators", StringComparison.Ordinal))
            .ToDictionary(tree => Path.GetFileName(tree.FilePath), tree => tree.GetText().ToString(), StringComparer.Ordinal);
    }

    private sealed class BuildPropertyOptionsProvider(IReadOnlyDictionary<string, string> buildProperties) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions globalOptions = new DictionaryOptions(
            buildProperties.ToDictionary(pair => $"build_property.{pair.Key}", pair => pair.Value, StringComparer.OrdinalIgnoreCase));

        public override AnalyzerConfigOptions GlobalOptions => globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => DictionaryOptions.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => DictionaryOptions.Empty;

        private sealed class DictionaryOptions(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
        {
            public static readonly DictionaryOptions Empty = new(new Dictionary<string, string>());

            public override bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value) =>
                values.TryGetValue(key, out value);
        }
    }
}
