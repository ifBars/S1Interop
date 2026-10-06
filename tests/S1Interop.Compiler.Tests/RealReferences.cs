using Microsoft.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using S1Interop.Compiler;

internal static class RealReferences
{
    public static int Verify(string monoInstall, string nativeInstall)
    {
        const string source = """
            using ScheduleOne.PlayerScripts;
            using ScheduleOne.NPCs;
            using UnityEngine;
            using System.Linq;
            public static class Probe
            {
                public static int RegistryCounts() => Player.PlayerList.Count + NPCManager.NPCRegistry.Count;
                public static Player Retype(Object value) => value as Player;
                public static bool IsPlayer(Object value) => value is Player player && player != null;
                public static System.Type PatchType() => typeof(Player);
                public static Coroutine Begin(MonoBehaviour owner, System.Collections.IEnumerator routine) => owner.StartCoroutine(routine);
                public static System.Collections.Generic.IEnumerable<ScheduleOne.Employees.Employee> Employees() =>
                    NPCManager.NPCRegistry.OfType<ScheduleOne.Employees.Employee>();
            }
            """;
        var mono = References(monoInstall, native: false);
        var native = References(nativeInstall, native: true);
        var author = CompilationSupport.Create("RealGameCompilerProbe", source, mono);
        var authorErrors = author.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (authorErrors.Length != 0)
        {
            foreach (var diagnostic in authorErrors) Console.Error.WriteLine(diagnostic);
            return 1;
        }
        var result = new InteropCompiler().Lower(author, native);
        foreach (var diagnostic in result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning))
            Console.WriteLine(diagnostic);
        if (!result.Success) return 1;
        using (var runtime = new MemoryStream(result.RuntimeAssembly.ToArray()))
        using (var image = new PEReader(runtime))
        {
            var metadata = image.GetMetadataReader();
            var names = metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToArray();
            if (names.Any(name => name == "Assembly-CSharp" || name == "MelonLoader" || name.StartsWith("UnityEngine", StringComparison.Ordinal)))
                throw new InvalidOperationException("Shared support unexpectedly depends on the game or Unity API.");
            Console.WriteLine("PASS shared support references only framework and IL2CPP infrastructure: " + string.Join(", ", names));
        }
        using var stream = new MemoryStream();
        var emitted = result.Compilation.Emit(stream);
        if (!emitted.Success)
        {
            foreach (var diagnostic in emitted.Diagnostics) Console.Error.WriteLine(diagnostic);
            return 1;
        }
        Console.WriteLine($"PASS real Mono binding and IL2CPP emission; {result.RewrittenNodes} transformations. Compile evidence only.");
        foreach (string arraySource in new[] {
            "public static class Probe { public static UnityEngine.Vector3[] Read(UnityEngine.Mesh mesh) => mesh.vertices; }"
        })
        {
            var arrays = new InteropCompiler().Lower(CompilationSupport.Create("RealArrayProbe", arraySource, mono), native);
            if (arrays.Success || !arrays.Diagnostics.Any(diagnostic => diagnostic.Id == "S1IC032"))
                throw new InvalidOperationException("Real native array conversion was not diagnosed: " +
                    string.Join(Environment.NewLine, arrays.Diagnostics));
        }
        Console.WriteLine("PASS real Unity struct array snapshot conversion rejected. Compile evidence only.");
        const string referenceArraySource = """
            using System;
            using ScheduleOne.Persistence.Datas;
            using UnityEngine;
            public static class Probe {
                public static Transform[] Components(GameObject owner) => owner.GetComponents<Transform>();
                public static DynamicSaveData[] Read(NPCCollectionData data) => data.NPCs;
                public static void Replace(NPCCollectionData data, DynamicSaveData[] values) => data.NPCs = values;
                public static SaveData[] Run(NPCCollectionData data) {
                    DynamicSaveData[] values = { new DynamicSaveData(new NPCData("probe")) };
                    Replace(data, values);
                    SaveData[] covariant = Read(data);
                    covariant[0] = null;
                    SaveData[] clone = (SaveData[])covariant.Clone();
                    Array.Resize(ref clone, 2);
                    clone[1] = new NPCData("resized");
                    return clone;
                }
            }
            """;
        var referenceArrays = new InteropCompiler().Lower(CompilationSupport.Create("RealReferenceArrayProbe", referenceArraySource, mono), native);
        if (!referenceArrays.Success) throw new InvalidOperationException(string.Join(Environment.NewLine, referenceArrays.Diagnostics));
        using var referenceArrayImage = new MemoryStream();
        var referenceArrayEmit = referenceArrays.Compilation.Emit(referenceArrayImage);
        if (!referenceArrayEmit.Success) throw new InvalidOperationException(string.Join(Environment.NewLine, referenceArrayEmit.Diagnostics));
        Console.WriteLine("PASS real game reference arrays compile through source helpers, covariance, clone and resize. Compile evidence only.");
        const string scalarArraySource = """
            using System;
            using ScheduleOne.Persistence;
            public static class Probe {
                public static int[] Read(TrashContentData data) => data.TrashQuantities;
                public static void Replace(TrashContentData data, int[] values) => data.TrashQuantities = values;
                public static int Run(TrashContentData data) {
                    int[] values = { 1, 2, 3 }; Replace(data, values);
                    values[0] = 7; Array.Copy(values, 0, values, 1, 2);
                    int[] clone = (int[])values.Clone(); Replace(data, clone);
                    Array.Resize(ref clone, 4);
                    return Read(data)[1];
                }
            }
            """;
        var scalarArrays = new InteropCompiler().Lower(CompilationSupport.Create("RealScalarArrayProbe", scalarArraySource, mono), native);
        if (!scalarArrays.Success) throw new InvalidOperationException(string.Join(Environment.NewLine, scalarArrays.Diagnostics));
        using var scalarArrayImage = new MemoryStream();
        var scalarArrayEmit = scalarArrays.Compilation.Emit(scalarArrayImage);
        if (!scalarArrayEmit.Success) throw new InvalidOperationException(string.Join(Environment.NewLine, scalarArrayEmit.Diagnostics));
        Console.WriteLine("PASS real game scalar array storage compiles through author helpers, copy, clone and resize. Compile evidence only.");
        return 0;
    }

    private static IEnumerable<MetadataReference> References(string install, bool native)
    {
        string managed = Path.Combine(install, native ? "MelonLoader/Il2CppAssemblies" : "Schedule I_Data/Managed");
        string loader = Path.Combine(install, native ? "MelonLoader/net6" : "MelonLoader/net35");
        if (!Directory.Exists(managed)) throw new DirectoryNotFoundException(managed);
        var files = Directory.EnumerateFiles(managed, "*.dll")
            .Where(path => native || !IsFramework(Path.GetFileName(path))).ToList();
        files.Add(Path.Combine(loader, "MelonLoader.dll"));
        files.Add(Path.Combine(loader, "0Harmony.dll"));
        if (native)
        {
            files.Add(Path.Combine(loader, "Il2CppInterop.Runtime.dll"));
            files.Add(Path.Combine(loader, "Il2CppInterop.Common.dll"));
            files.Add(Path.Combine(install, "MelonLoader/Dependencies/SupportModules/Il2Cpp.dll"));
        }
        return CompilationSupport.PlatformReferences.Concat(files.Select(path => MetadataReference.CreateFromFile(path)));
    }

    private static bool IsFramework(string name) => name is "mscorlib.dll" or "netstandard.dll" or "System.dll" or
        "Accessibility.dll" or "Edgegap.dll" || name.StartsWith("System.", StringComparison.Ordinal) ||
        name.StartsWith("Mono.", StringComparison.Ordinal) || name.StartsWith("Novell.", StringComparison.Ordinal);
}
