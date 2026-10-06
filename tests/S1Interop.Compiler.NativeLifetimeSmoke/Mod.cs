using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Il2CppInterop.Runtime;
using MelonLoader;
using S1Interop.CompilerSmoke.Library;
using UnityEngine;
using BridgeDictionary = S1Interop.Compiler.Generated.S1InteropDictionary<string, string>;
using NativeDictionary = Il2CppSystem.Collections.Generic.Dictionary<string, string>;

[assembly: MelonInfo(typeof(S1Interop.CompilerSmoke.NativeLifetime.Mod), "S1Interop Compiler Runtime Smoke (Native Lifetime)", "0.0.1", "S1Interop contributors")]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonPlatformDomain(MelonPlatformDomainAttribute.CompatibleDomains.IL2CPP)]

namespace S1Interop.CompilerSmoke.NativeLifetime;

/// <summary>
/// Native-only diagnostic for the generated dictionary comparer adapters. It binds to the IL2CPP interop assemblies and the
/// built RuntimeLibrary directly, so no compiler lowering sits between the harness and the lifetimes it observes.
/// </summary>
/// <remarks>
/// Scenario 1 (release): a native dictionary is retained only through a field; managed and IL2CPP collections must keep both
/// lookup semantics and CLR comparer identity alive. After the field is cleared, the native adapter and the CLR comparer must
/// both be reclaimed.
/// Scenario 2 (rooted): the CLR comparer stays strongly rooted by this harness. The old native adapter must still be
/// reclaimed, a new dictionary over the same comparer must recreate it, and it must behave identically and reclaim cleanly.
/// Nothing in this file holds a raw native pointer between frames: only a weak GC handle (nint) survives a helper call.
/// </remarks>
public sealed class Mod : MelonMod
{
    private const string MENU_SCENE = "Menu";
    private const string COMPARER_FIELD = "_comparer";
    private const int RETAINED_FRAMES = 8;
    private const int MAX_DRAIN_FRAMES = 120;
    private const int NATIVE_COLLECT_GENERATION = 0;
    private const int STACK_SCRUB_BYTES = 32 * 1024;
    private const int CONSTANT_HASH = 17;

    private enum Phase { WaitingForMenu, Retained, Draining, RecreatedRetained, FinalDraining, Finished }

    private enum ProbeMode { Length, FirstCharIgnoreCase, LastChar, ConstantHashIgnoreCase }

    private static int stackScrubSink;

    private Phase phase = Phase.WaitingForMenu;
    private string token = "missing";
    private LifetimeCase[] cases = Array.Empty<LifetimeCase>();
    private int phaseFrames;
    private int totalFrames;
    private int checks;
    private int firstDrainFrames;
    private int finalDrainFrames;

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        if (phase != Phase.WaitingForMenu || sceneName != MENU_SCENE) return;
        token = Environment.GetEnvironmentVariable("S1INTEROP_SMOKE_TOKEN") ?? "missing";
        try
        {
            RegisterLibraryFactory();
            cases = new[]
            {
                CreateRetained("release/length", ProbeMode.Length, keepManagedRoot: false),
                CreateRetained("release/first-char", ProbeMode.FirstCharIgnoreCase, keepManagedRoot: false),
                CreateRetained("release/constant-hash", ProbeMode.ConstantHashIgnoreCase, keepManagedRoot: false),
                CreateRetained("rooted/last-char", ProbeMode.LastChar, keepManagedRoot: true),
                CreateRetained("rooted/length", ProbeMode.Length, keepManagedRoot: true)
            };
            ScrubStack();
            EnterPhase(Phase.Retained);
            LogInfo($"armed {cases.Length} comparer cases; retained={RETAINED_FRAMES} frames, drain bound={MAX_DRAIN_FRAMES} frames");
        }
        catch (Exception exception)
        {
            Finish(false, exception.ToString());
        }
    }

    public override void OnUpdate()
    {
        if (phase == Phase.WaitingForMenu || phase == Phase.Finished) return;
        try { Step(); }
        catch (Exception exception) { Finish(false, exception.ToString()); }
    }

    private void Step()
    {
        totalFrames++;
        phaseFrames++;
        // Boehm scans attached thread stacks conservatively; wipe stale frames so a leftover pointer cannot pin an adapter.
        ScrubStack();
        CollectAll();
        switch (phase)
        {
            case Phase.Retained:
                VerifyAllRetained();
                if (phaseFrames >= RETAINED_FRAMES)
                {
                    ReleaseNativeOwners();
                    ScrubStack();
                    EnterPhase(Phase.Draining);
                }
                break;
            case Phase.Draining:
                if (PollDrain(finalDrain: false))
                {
                    firstDrainFrames = phaseFrames;
                    LogInfo($"first drain complete after {firstDrainFrames} frames: {DescribeObservations()}");
                    if (Array.Exists(cases, lifetime => lifetime.KeepManagedRoot))
                    {
                        RecreateRooted();
                        ScrubStack();
                        EnterPhase(Phase.RecreatedRetained);
                    }
                    else Finish(true, null);
                }
                else if (phaseFrames >= MAX_DRAIN_FRAMES)
                    Finish(false, $"native adapters were not reclaimed within {MAX_DRAIN_FRAMES} frames");
                break;
            case Phase.RecreatedRetained:
                VerifyAllRetained();
                if (phaseFrames >= RETAINED_FRAMES)
                {
                    ReleaseNativeOwners();
                    ReleaseManagedRoots();
                    ScrubStack();
                    EnterPhase(Phase.FinalDraining);
                }
                break;
            case Phase.FinalDraining:
                if (PollDrain(finalDrain: true))
                {
                    finalDrainFrames = phaseFrames;
                    LogInfo($"final drain complete after {finalDrainFrames} frames: {DescribeObservations()}");
                    Finish(true, null);
                }
                else if (phaseFrames >= MAX_DRAIN_FRAMES)
                    Finish(false, $"recreated adapters or released comparers were not reclaimed within {MAX_DRAIN_FRAMES} frames");
                break;
        }
    }

    private void EnterPhase(Phase next)
    {
        phase = next;
        phaseFrames = 0;
        LogInfo($"phase {next} at frame {totalFrames}");
    }

    /// <summary>Managed collection first so wrapper finalizers drop native handles, then the IL2CPP collection.</summary>
    private static void CollectAll()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        IL2CPP.il2cpp_gc_collect(NATIVE_COLLECT_GENERATION);
    }

    private bool PollDrain(bool finalDrain)
    {
        bool complete = true;
        foreach (LifetimeCase lifetime in cases)
        {
            if (finalDrain && !lifetime.KeepManagedRoot) continue;
            bool expectManagedCollected = finalDrain || !lifetime.KeepManagedRoot;
            if (!lifetime.Poll(totalFrames, expectManagedCollected)) complete = false;
        }
        return complete;
    }

    // Loading the library assembly runs its module initializer, which registers the generated comparer factories.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterLibraryFactory()
    {
        var report = new Il2CppScheduleOne.Reporting.ReportSubmission();
        _ = SharedProbe.Metadata(report);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static LifetimeCase CreateRetained(string name, ProbeMode mode, bool keepManagedRoot)
    {
        var comparer = new ProbeComparer(mode);
        var lifetime = new LifetimeCase(name, mode, keepManagedRoot, new WeakReference(comparer, false));
        lifetime.OwnerNative = BuildNativeDictionary(comparer, ProbeData.For(mode));
        lifetime.NativeComparerHandle = CaptureNativeComparerWeakHandle(lifetime.OwnerNative);
        if (keepManagedRoot) lifetime.RootedComparer = comparer;
        return lifetime;
    }

    // The bridged view is only a construction vehicle; the returned native wrapper becomes the single strong owner.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static NativeDictionary BuildNativeDictionary(ProbeComparer comparer, ProbeData data)
    {
        var view = new BridgeDictionary(comparer);
        for (int index = 0; index < data.Keys.Length; index++) view.Add(data.Keys[index], data.Values[index]);
        return BridgeDictionary.ToNative(view) ?? throw new InvalidOperationException("The bridged dictionary has no native storage.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static nint CaptureNativeComparerWeakHandle(NativeDictionary native)
    {
        nint handle = IL2CPP.il2cpp_gchandle_new_weakref(ReadNativeComparerPointer(native), false);
        if (handle == 0) throw new InvalidOperationException("IL2CPP returned no weak handle for the native comparer.");
        return handle;
    }

    // The interop wrapper's _comparer property may be inaccessible and would also mint a strong wrapper, so read the field.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IntPtr ReadNativeComparerPointer(NativeDictionary native)
    {
        IntPtr dictionary = native.Pointer;
        IntPtr field = IL2CPP.il2cpp_class_get_field_from_name(IL2CPP.il2cpp_object_get_class(dictionary), COMPARER_FIELD);
        if (field == IntPtr.Zero) throw new MissingFieldException("Il2CppSystem Dictionary", COMPARER_FIELD);
        IntPtr comparer = Marshal.ReadIntPtr(dictionary, checked((int)IL2CPP.il2cpp_field_get_offset(field)));
        if (comparer == IntPtr.Zero) throw new InvalidOperationException("The native dictionary has no comparer.");
        return comparer;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsNativeCollected(nint handle) => IL2CPP.il2cpp_gchandle_get_target(handle) == IntPtr.Zero;

    private void VerifyAllRetained()
    {
        foreach (LifetimeCase lifetime in cases)
            if (lifetime.OwnerNative != null) VerifyRetained(lifetime);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void VerifyRetained(LifetimeCase lifetime)
    {
        NativeDictionary native = lifetime.OwnerNative ?? throw new InvalidOperationException(lifetime.Name + ": owner field is empty");
        ProbeData data = ProbeData.For(lifetime.Mode);
        string name = lifetime.Name;
        ProbeComparer? original = lifetime.ManagedComparer.Target as ProbeComparer;
        Require(original != null, name + ": CLR comparer stays alive while only the native dictionary is retained");
        Require(IL2CPP.il2cpp_gchandle_get_target(lifetime.NativeComparerHandle) == ReadNativeComparerPointer(native),
            name + ": weak handle still tracks the dictionary's native comparer");
        Require(lifetime.RetiredNativeComparerHandle == 0 || IsNativeCollected(lifetime.RetiredNativeComparerHandle),
            name + ": retired adapter stays reclaimed after recreation");

        BridgeDictionary view = BridgeDictionary.FromNative(native) ?? throw new InvalidOperationException(name + ": no bridged view");
        int callsBefore = original!.Calls;
        Require(view.Count == data.Keys.Length, name + ": dictionary count survives collection");
        Require(view.TryGetValue(data.HitKey, out string? hit) && hit == data.HitValue,
            name + ": lookup honors the custom comparer after collection");
        Require(!view.TryGetValue(data.MissKey, out _), name + ": comparer still rejects non-equivalent keys");
        Require(original.Calls > callsBefore, name + ": native adapter forwards to the CLR comparer");
        Require(object.ReferenceEquals(view.Comparer, original), name + ": comparer identity is recovered from the native adapter");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReleaseNativeOwners()
    {
        foreach (LifetimeCase lifetime in cases) lifetime.OwnerNative = null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReleaseManagedRoots()
    {
        foreach (LifetimeCase lifetime in cases) lifetime.RootedComparer = null;
    }

    private void RecreateRooted()
    {
        foreach (LifetimeCase lifetime in cases)
            if (lifetime.KeepManagedRoot) RecreateAdapter(lifetime);
    }

    // Create must notice the collected adapter behind the same comparer and build a new one; a stale cache hit would throw here.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RecreateAdapter(LifetimeCase lifetime)
    {
        ProbeComparer comparer = lifetime.RootedComparer ?? throw new InvalidOperationException(lifetime.Name + ": comparer root is missing");
        Require(IsNativeCollected(lifetime.NativeComparerHandle), lifetime.Name + ": old adapter reclaimed before recreation");
        lifetime.OwnerNative = BuildNativeDictionary(comparer, ProbeData.For(lifetime.Mode));
        lifetime.RetiredNativeComparerHandle = lifetime.NativeComparerHandle;
        lifetime.NativeComparerHandle = CaptureNativeComparerWeakHandle(lifetime.OwnerNative);
        lifetime.ResetObservation();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static unsafe void ScrubStack()
    {
        byte* block = stackalloc byte[STACK_SCRUB_BYTES];
        new Span<byte>(block, STACK_SCRUB_BYTES).Clear();
        stackScrubSink = block[STACK_SCRUB_BYTES / 2];
    }

    private void Require(bool condition, string scenario)
    {
        if (!condition) throw new InvalidOperationException(scenario);
        checks++;
    }

    private void Finish(bool passed, string? failure)
    {
        if (phase == Phase.Finished) return;
        string state = DescribeState();
        phase = Phase.Finished;
        try { foreach (LifetimeCase lifetime in cases) lifetime.Dispose(); }
        catch (Exception exception) { LogInfo("cleanup failed: " + exception.Message); }
        if (passed)
        {
            LoggerInstance.Msg($"S1Compiler|PASS|Token={token}|Version={Application.version}|Scene={MENU_SCENE}|Checks={checks}" +
                $"|Cases={cases.Length}|Frames={totalFrames}|FirstDrain={firstDrainFrames}|FinalDrain={finalDrainFrames}|{DescribeObservations()}");
        }
        else LoggerInstance.Error($"S1Compiler|FAIL|Token={token}|{failure}|{state}");
        Application.Quit();
    }

    private string DescribeState()
    {
        var text = new StringBuilder("Phase=").Append(phase).Append("|Frames=").Append(totalFrames).Append("|PhaseFrames=").Append(phaseFrames)
            .Append("|Checks=").Append(checks);
        try { text.Append("|NativeGcDisabled=").Append(IL2CPP.il2cpp_gc_is_disabled()); }
        catch (Exception exception) { text.Append("|NativeGcDisabled=unknown(").Append(exception.GetType().Name).Append(')'); }
        return text.Append('|').Append(DescribeObservations()).ToString();
    }

    private string DescribeObservations()
    {
        var text = new StringBuilder("Cases=[");
        for (int index = 0; index < cases.Length; index++)
        {
            if (index > 0) text.Append(';');
            cases[index].Describe(text);
        }
        return text.Append(']').ToString();
    }

    private void LogInfo(string message) => LoggerInstance.Msg($"S1Compiler|INFO|Token={token}|{message}");

    private sealed class LifetimeCase
    {
        public LifetimeCase(string name, ProbeMode mode, bool keepManagedRoot, WeakReference managedComparer)
        {
            Name = name;
            Mode = mode;
            KeepManagedRoot = keepManagedRoot;
            ManagedComparer = managedComparer;
        }

        public string Name { get; }
        public ProbeMode Mode { get; }
        public bool KeepManagedRoot { get; }
        public WeakReference ManagedComparer { get; }

        /// <summary>The only strong reference to the native dictionary (and through it the native comparer adapter).</summary>
        public NativeDictionary? OwnerNative;

        /// <summary>Set only for rooted cases: keeps the CLR comparer alive independent of any native object.</summary>
        public ProbeComparer? RootedComparer;

        /// <summary>Weak IL2CPP GC handle to the dictionary's native comparer. Zero target means the adapter was reclaimed.</summary>
        public nint NativeComparerHandle;
        public nint RetiredNativeComparerHandle;

        private int nativeCollectedFrame;
        private int managedCollectedFrame;

        public void ResetObservation()
        {
            nativeCollectedFrame = 0;
            managedCollectedFrame = 0;
        }

        public bool Poll(int frame, bool expectManagedCollected)
        {
            // Native weak handles only ever clear, so reading managed liveness first makes (managed gone, native alive) a sound proof
            // that the CLR comparer was freed while its native adapter could still call it.
            bool managedGone = !ManagedComparer.IsAlive;
            bool nativeGone = IsNativeCollected(NativeComparerHandle);
            if (managedGone && !nativeGone)
                throw new InvalidOperationException(Name + ": CLR comparer was collected while its native adapter was still alive");
            if (managedGone && !expectManagedCollected)
                throw new InvalidOperationException(Name + ": strongly rooted CLR comparer was collected");
            if (nativeGone && nativeCollectedFrame == 0) nativeCollectedFrame = frame;
            if (managedGone && managedCollectedFrame == 0) managedCollectedFrame = frame;
            return nativeGone && (!expectManagedCollected || managedGone);
        }

        public void Describe(StringBuilder text)
        {
            text.Append(Name).Append(":native@").Append(nativeCollectedFrame == 0 ? "-" : nativeCollectedFrame.ToString())
                .Append("/managed@").Append(managedCollectedFrame == 0 ? "-" : managedCollectedFrame.ToString());
        }

        public void Dispose()
        {
            OwnerNative = null;
            RootedComparer = null;
            Free(ref NativeComparerHandle);
            Free(ref RetiredNativeComparerHandle);
        }

        private static void Free(ref nint handle)
        {
            if (handle == 0) return;
            IL2CPP.il2cpp_gchandle_free(handle);
            handle = 0;
        }
    }

    private sealed class ProbeData
    {
        private ProbeData(string[] keys, string[] values, string hitKey, string hitValue, string missKey)
        {
            Keys = keys;
            Values = values;
            HitKey = hitKey;
            HitValue = hitValue;
            MissKey = missKey;
        }

        public string[] Keys { get; }
        public string[] Values { get; }

        /// <summary>A key equal to a stored key only under the custom comparer (ordinal equality would miss).</summary>
        public string HitKey { get; }
        public string HitValue { get; }
        public string MissKey { get; }

        public static ProbeData For(ProbeMode mode) => mode switch
        {
            ProbeMode.Length => new ProbeData(new[] { "aaa", "bb" }, new[] { "three", "two" }, "zzz", "three", "q"),
            ProbeMode.FirstCharIgnoreCase => new ProbeData(new[] { "Alpha", "Beta" }, new[] { "a", "b" }, "apricot", "a", "gamma"),
            ProbeMode.LastChar => new ProbeData(new[] { "xyz", "xyw" }, new[] { "z", "w" }, "abz", "z", "abc"),
            ProbeMode.ConstantHashIgnoreCase => new ProbeData(new[] { "Key", "Other" }, new[] { "k", "o" }, "KEY", "k", "missing"),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    /// <summary>Test-only CLR comparer; each instance counts callbacks so forwarding from the native adapter is observable.</summary>
    private sealed class ProbeComparer : System.Collections.Generic.IEqualityComparer<string>
    {
        private readonly ProbeMode mode;
        private int calls;

        public ProbeComparer(ProbeMode mode) { this.mode = mode; }

        public int Calls => Volatile.Read(ref calls);

        public bool Equals(string? left, string? right)
        {
            Interlocked.Increment(ref calls);
            if (left is null || right is null) return left is null && right is null;
            return mode switch
            {
                ProbeMode.Length => left.Length == right.Length,
                ProbeMode.FirstCharIgnoreCase => FirstChar(left) == FirstChar(right),
                ProbeMode.LastChar => LastChar(left) == LastChar(right),
                _ => string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
            };
        }

        public int GetHashCode(string value)
        {
            Interlocked.Increment(ref calls);
            return mode switch
            {
                ProbeMode.Length => value.Length,
                ProbeMode.FirstCharIgnoreCase => FirstChar(value),
                ProbeMode.LastChar => LastChar(value),
                _ => CONSTANT_HASH
            };
        }

        private static int FirstChar(string value) => value.Length == 0 ? 0 : char.ToUpperInvariant(value[0]);
        private static int LastChar(string value) => value.Length == 0 ? 0 : value[value.Length - 1];
    }
}
