using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;

// Local diagnostic consumer of a compiler-created plan. No installation files are rewritten.
internal sealed class PlannedEventRepair : IDisposable
{
    private readonly HarmonyLib.Harmony harmony = new("S1Interop.AtomicEventDiagnostic." + Guid.NewGuid().ToString("N"));
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<MethodBase, Accessor> installed = new();
    public int Count { get; }

    public sealed record Accessor(int MetadataToken, string DeclaringType, string FieldName, string CallbackType, bool Add, bool IsStatic = false);
    public sealed record Plan(string AuthorSha256, string TargetSha256, string TargetAssembly, Guid TargetModuleMvid, Accessor[] Accessors);

    public PlannedEventRepair(string path)
    {
        var plan = JsonSerializer.Deserialize<Plan>(File.ReadAllText(path)) ?? throw new InvalidOperationException("Missing repair plan.");
        var assembly = Assembly.Load(new AssemblyName(plan.TargetAssembly));
        if (assembly.ManifestModule.ModuleVersionId != plan.TargetModuleMvid ||
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) != plan.TargetSha256)
            throw new InvalidOperationException("Repair plan does not match the loaded wrapper image.");
        var methods = new List<(MethodInfo Method, Accessor Repair)>();
        // This diagnostic retains its instance-field probe. Static repairs are exercised
        // through the actual generated startup runtime in the dual-runtime core smoke.
        foreach (var repair in plan.Accessors.Where(a => !a.IsStatic))
        {
            var method = assembly.ManifestModule.ResolveMethod(repair.MetadataToken) as MethodInfo;
            if (method is null || method.IsStatic || method.ContainsGenericParameters || method.ReturnType != typeof(void) ||
                method.DeclaringType?.FullName != repair.DeclaringType ||
                method.Name != (repair.Add ? "add_" : "remove_") + repair.FieldName ||
                method.GetParameters() is not [var parameter] || parameter.ParameterType.FullName != repair.CallbackType ||
                !typeof(Il2CppSystem.Delegate).IsAssignableFrom(parameter.ParameterType) ||
                !typeof(Il2CppObjectBase).IsAssignableFrom(method.DeclaringType))
                throw new InvalidOperationException("Repair target does not match its planned signature.");
            methods.Add((method, repair));
        }
        try
        {
            foreach (var (method, repair) in methods)
            {
                if (!installed.TryAdd(method, repair)) throw new InvalidOperationException("A diagnostic repair already owns this method.");
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(PlannedEventRepair).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic)!));
            }
            Count = methods.Count;
        }
        catch { Dispose(); throw; }
    }

    private static bool Prefix(Il2CppObjectBase __instance, Il2CppSystem.Delegate __0, MethodBase __originalMethod)
    {
        var repair = installed[__originalMethod];
        new NativeEventField(__instance, repair.FieldName).Update(__0, repair.Add);
        return false;
    }

    public void Dispose()
    {
        harmony.UnpatchSelf();
        // Keep immutable descriptors for prefixes already in flight when unpatching.
        // They hold no game instances and this diagnostic runs once per owned process.
    }
}
