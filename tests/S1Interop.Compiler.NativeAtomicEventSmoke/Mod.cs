using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using MelonLoader;
using System.Runtime.CompilerServices;
using UnityEngine;
using NativeDelegate = Il2CppSystem.Delegate;

[assembly: MelonInfo(typeof(AtomicEventProbe), "S1Interop Native Atomic Event Smoke", "0.0.1", "S1Interop contributors")]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonPlatformDomain(MelonPlatformDomainAttribute.CompatibleDomains.IL2CPP)]

// Native-only instrumentation, not compiler-transformed source or an installed game patch.
public sealed class AtomicEventProbe : MelonMod
{
    private static readonly bool weakBridge = Environment.GetEnvironmentVariable("S1INTEROP_TEST_WEAK_CALLBACK_BRIDGE") == "1";
    private static readonly bool compilerBridge = Environment.GetEnvironmentVariable("S1INTEROP_TEST_COMPILER_CALLBACK_BRIDGE") == "1";
    private bool completed;
    private static bool workerCleanupComplete = true;
    private AudioClip? lifetimeClip;
    private NativeEventField? lifetimeField;
    private nint retainedHandle, controlHandle;
    private WeakReference? retainedState, controlState;
    private int lifetimeFrames, completedChecks, plannedAccessors;
    private string runToken = "missing";
    private bool lifetimeActive, callbackRemoved, acquisitionPending;
    private readonly System.Diagnostics.Stopwatch phaseTimer = new();

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        if (completed || sceneName != "Menu") return;
        completed = true;
        string token = Environment.GetEnvironmentVariable("S1INTEROP_SMOKE_TOKEN") ?? "missing";
        AudioClip? clip = null;
        int checks = 0;
        void Require(bool ok, string description)
        {
            if (!ok) throw new InvalidOperationException(description);
            checks++;
        }
        try
        {
            if (weakBridge && compilerBridge) throw new InvalidOperationException("Choose only one callback ownership intervention.");
            string? repairPlan = Environment.GetEnvironmentVariable("S1INTEROP_TEST_EVENT_REPAIR_PLAN");
            using var repairs = repairPlan == null ? null : new PlannedEventRepair(repairPlan);
            if (repairs != null) { plannedAccessors = repairs.Count; ProbeReconstructedAccessors(Require); }
            clip = AudioClip.Create("S1Interop atomic field contract", 64, 1, 8000, false);
            var field = new NativeEventField(clip, "m_PCMReaderCallback");
            int firstCalls = 0, secondCalls = 0;
            AudioClip.PCMReaderCallback first = (Action<Il2CppStructArray<float>>)(samples => { Interlocked.Increment(ref firstCalls); samples[0] = 0.25f; });
            AudioClip.PCMReaderCallback second = (Action<Il2CppStructArray<float>>)(samples => { Interlocked.Increment(ref secondCalls); samples[0] = 0.75f; });
            Require(field.Read() == IntPtr.Zero, "new owned callback field starts empty");
            Require(field.CompareExchange(first, null) == IntPtr.Zero, "CAS returns previous null");
            Require(clip.m_PCMReaderCallback.Pointer == first.Pointer, "CAS updates actual native storage");
            Require(field.CompareExchange(second, null) == first.Pointer, "failed CAS returns observed callback");
            Require(clip.m_PCMReaderCallback.Pointer == first.Pointer, "failed CAS does not overwrite callback");
            var samples = new Il2CppStructArray<float>(2);
            clip.InvokePCMReaderCallback_Internal(samples);
            Require(firstCalls == 1 && samples[0] == 0.25f, "native dispatcher observes stored callback");
            Require(field.CompareExchange(null, first) == first.Pointer, "CAS removes expected callback");
            Require(field.Read() == IntPtr.Zero, "native field cleared");
            field.Update(first, add: true);
            field.Update(second, add: true);
            clip.InvokePCMReaderCallback_Internal(samples);
            Require(firstCalls == 2 && secondCalls == 1 && samples[0] == 0.75f, "multicast add preserves invocation order");
            field.Update(first, add: false);
            clip.InvokePCMReaderCallback_Internal(samples);
            Require(firstCalls == 2 && secondCalls == 2, "remove preserves remaining handler");
            field.Update(second, add: false);
            Require(field.Read() == IntPtr.Zero, "last removal clears field");

            const int workers = 4, iterations = 16;
            RunWorkers(workers, (cancellation, collision) =>
                {
                    for (int i = 0; i < iterations; i++)
                        field.Update(first, add: true, i == 0 ? () =>
                        {
                            if (!collision!.SignalAndWait(TimeSpan.FromSeconds(2), cancellation)) throw new TimeoutException("CAS collision barrier timed out.");
                        } : null, cancellation);
                }, forceCollision: true);
            Require(field.RetryCount >= workers - 1, "forced competing updates exercise CAS retries");
            clip.InvokePCMReaderCallback_Internal(samples);
            Require(firstCalls == 2 + workers * iterations, "concurrent adds do not lose updates");
            RunWorkers(workers, (cancellation, _) => { for (int i = 0; i < iterations; i++) field.Update(first, add: false, cancellation: cancellation); });
            Require(field.Read() == IntPtr.Zero, "concurrent removals drain exactly the installed callbacks");

            field.Update(second, add: true);
            GC.Collect(); GC.WaitForPendingFinalizers(); IL2CPP.il2cpp_gc_collect(0);
            clip.InvokePCMReaderCallback_Internal(samples);
            Require(secondCalls == 3, "callback remains callable after managed and native collection");
            field.Update(second, add: false);
            lifetimeClip = clip;
            clip = null; // Transfer ownership before any lifetime setup can throw.
            lifetimeField = field;
            runToken = token;
            completedChecks = checks;
            (retainedHandle, retainedState) = CreateTrackedCallback(field);
            (controlHandle, controlState) = CreateTrackedCallback(null);
            lifetimeActive = true;
            phaseTimer.Restart();
        }
        catch (Exception exception)
        {
            LoggerInstance.Error($"S1Compiler|FAIL|Token={token}|{exception}");
            CleanupLifetime();
        }
        finally { if (clip != null && workerCleanupComplete) UnityEngine.Object.Destroy(clip); }
    }

    public override void OnUpdate()
    {
        if (!lifetimeActive) return;
        try
        {
            if (lifetimeClip == null) throw new InvalidOperationException("Lifetime clip was unexpectedly destroyed.");
            ScrubStack();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            IL2CPP.il2cpp_gc_collect(0);
            if (++lifetimeFrames < 8) return;
            if (acquisitionPending)
            {
                ProbeAcquisitionWindow();
                acquisitionPending = false;
                callbackRemoved = true;
                lifetimeFrames = 0;
                phaseTimer.Restart();
                return;
            }
            if (!callbackRemoved)
            {
                if (IL2CPP.il2cpp_gchandle_get_target(controlHandle) != IntPtr.Zero)
                {
                    if (phaseTimer.Elapsed < TimeSpan.FromSeconds(10)) return;
                    throw new InvalidOperationException($"Unrooted callback reclamation inconclusive after {lifetimeFrames} frames: nativeAlive={IL2CPP.il2cpp_gchandle_get_target(controlHandle) != IntPtr.Zero}, managedAlive={controlState!.IsAlive}.");
                }
                CheckLifetime(IL2CPP.il2cpp_gchandle_get_target(controlHandle) == IntPtr.Zero,
                    "unrooted native callback must be reclaimed");
                CheckLifetime(IL2CPP.il2cpp_gchandle_get_target(retainedHandle) != IntPtr.Zero,
                    "native field alone retains installed callback");
                CheckLifetime(retainedState!.IsAlive, "installed callback retains managed state");
                CheckLifetime(InvokeRetained(lifetimeClip, retainedState), "field-owned callback remains callable");
                LoggerInstance.Msg($"S1Compiler|OBSERVATION|Token={runToken}|FieldOnlyCallbackAlive=True|ControlNativeAlive=False|ControlManagedAlive={controlState!.IsAlive}");
                acquisitionPending = true;
                return;
            }
            if (IL2CPP.il2cpp_gchandle_get_target(retainedHandle) != IntPtr.Zero || retainedState!.IsAlive || controlState!.IsAlive)
            {
                if (phaseTimer.Elapsed < TimeSpan.FromSeconds(10)) return;
                LoggerInstance.Msg($"S1Compiler|OBSERVATION|Token={runToken}|RemovedNativeAlive={IL2CPP.il2cpp_gchandle_get_target(retainedHandle) != IntPtr.Zero}|RemovedManagedAlive={retainedState!.IsAlive}|ControlManagedAlive={controlState!.IsAlive}");
                throw new InvalidOperationException($"Removed callback reclamation inconclusive after {lifetimeFrames} frames: nativeAlive={IL2CPP.il2cpp_gchandle_get_target(retainedHandle) != IntPtr.Zero}, managedAlive={retainedState!.IsAlive}.");
            }
            CheckLifetime(IL2CPP.il2cpp_gchandle_get_target(retainedHandle) == IntPtr.Zero,
                "removed native callback must be reclaimed");
            CheckLifetime(!retainedState!.IsAlive, "removed managed callback state must be reclaimed");
            CheckLifetime(!controlState!.IsAlive, "unrooted managed callback state must be reclaimed");
            LoggerInstance.Msg($"S1Compiler|PASS|Token={runToken}|Scene=Menu|Checks={completedChecks}|Case=NativeAtomicEventField|WeakBridge={weakBridge}|CompilerBridge={compilerBridge}|PlannedAccessors={plannedAccessors}|LifetimeFrames={lifetimeFrames}");
        }
        catch (Exception exception) { LoggerInstance.Error($"S1Compiler|FAIL|Token={runToken}|{exception}"); }
        CleanupLifetime();
    }

    private void CheckLifetime(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        completedChecks++;
    }

    private sealed class CallbackState { public int Calls; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ProbeReconstructedAccessors(Action<bool, string> require)
    {
        int reads = 0, position = -1;
        var reader = S1Interop.Compiler.Generated.S1InteropNativeDelegate.Convert<AudioClip.PCMReaderCallback>(
            (Action<Il2CppStructArray<float>>)(samples => { reads++; samples[0] = 0.375f; }));
        var seek = S1Interop.Compiler.Generated.S1InteropNativeDelegate.Convert<AudioClip.PCMSetPositionCallback>(
            (Action<int>)(value => position = value));
        var clip = AudioClip.Create("S1Interop reconstructed event", 64, 1, 8000, false, reader, seek);
        try
        {
            require(clip.m_PCMReaderCallback != null && clip.m_PCMSetPositionCallback != null,
                "AudioClip.Create reaches repaired accessors internally");
            reads = 0;
            var samples = new Il2CppStructArray<float>(1);
            clip.InvokePCMReaderCallback_Internal(samples);
            clip.InvokePCMSetPositionCallback_Internal(17);
            require(reads == 1 && samples[0] == 0.375f && position == 17, "repaired accessor callbacks reach native dispatch");
            clip.remove_m_PCMReaderCallback(reader);
            clip.remove_m_PCMSetPositionCallback(seek);
            require(clip.m_PCMReaderCallback == null && clip.m_PCMSetPositionCallback == null, "planned removal clears both fields");
            clip.add_m_PCMReaderCallback(reader);
            clip.add_m_PCMSetPositionCallback(seek);
            clip.InvokePCMReaderCallback_Internal(samples);
            clip.InvokePCMSetPositionCallback_Internal(29);
            require(reads == 2 && position == 29, "planned re-add restores both native callbacks");
        }
        finally { UnityEngine.Object.Destroy(clip); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProbeAcquisitionWindow()
    {
        var field = lifetimeField!;
        int calls = 0;
        AudioClip.PCMReaderCallback replacement = S1Interop.Compiler.Generated.S1InteropNativeDelegate.Convert<AudioClip.PCMReaderCallback>(
            (Action<Il2CppStructArray<float>>)(samples => { calls++; samples[0] = 0.875f; }));
        int retries = field.RetryCount;
        field.Update(replacement, add: true, beforeFirstAcquire: () =>
        {
            // The worker's wrapper must be gone and its native thread detached before collection.
            RunWorkers(1, (_, _) => RemoveRetained(field));
            GC.Collect();
            GC.WaitForPendingFinalizers();
            IL2CPP.il2cpp_gc_collect(0);
            CheckLifetime(IL2CPP.il2cpp_gchandle_get_target(retainedHandle) != IntPtr.Zero,
                "observed pointer survives collection after another thread removes field ownership");
        });
        CheckLifetime(field.RetryCount == retries + 1, "removed observation forces exactly one CAS retry");
        var samples = new Il2CppStructArray<float>(1);
        lifetimeClip!.InvokePCMReaderCallback_Internal(samples);
        CheckLifetime(calls == 1 && samples[0] == 0.875f && RetainedCalls(retainedState!) == 1,
            "retry installs only the replacement callback");
        RemoveRetained(field);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RetainedCalls(WeakReference reference) => (reference.Target as CallbackState)?.Calls ?? -1;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (nint Handle, WeakReference State) CreateTrackedCallback(NativeEventField? field)
    {
        var state = new CallbackState();
        Action<Il2CppStructArray<float>> managedCallback = samples =>
        {
            state.Calls++;
            samples[0] = 0.625f;
        };
        AudioClip.PCMReaderCallback callback = compilerBridge
            ? S1Interop.Compiler.Generated.S1InteropNativeDelegate.Convert<AudioClip.PCMReaderCallback>(managedCallback)
            : managedCallback;
        if (weakBridge)
            WeakenOwnedBridge(callback);
        field?.Update(callback, add: true);
        if (field != null && field.Read() != callback.Pointer)
            throw new InvalidOperationException("Installing into an empty field changed delegate identity.");
        nint handle = IL2CPP.il2cpp_gchandle_new_weakref(callback.Pointer, false);
        if (handle == 0) throw new InvalidOperationException("Native weak handle allocation failed.");
        if (IL2CPP.il2cpp_gchandle_get_target(handle) != callback.Pointer)
        {
            IL2CPP.il2cpp_gchandle_free(handle);
            throw new InvalidOperationException("Native weak handle did not initially reference its callback.");
        }
        GC.KeepAlive(callback);
        return (handle, new WeakReference(state));
    }

    // Diagnostic intervention only: change the bridge created for this callback,
    // while the callback's own wrapper strongly retains its native target.
    private static void WeakenOwnedBridge(AudioClip.PCMReaderCallback callback)
    {
        var target = callback.m_target;
        var bridge = ClassInjectorBase.GetMonoObjectFromIl2CppPointer(target.Pointer) as Il2CppObjectBase;
        if (bridge == null || bridge.GetType().FullName != "Il2CppInterop.Runtime.DelegateSupport+Il2CppToMonoDelegateReference" ||
            bridge.GetType().Assembly != typeof(DelegateSupport).Assembly)
            throw new NotSupportedException("The created callback does not have the expected injected delegate bridge.");
        var field = typeof(Il2CppObjectBase).GetField("myGcHandle",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field?.FieldType != typeof(nint)) throw new NotSupportedException("Unexpected bridge handle layout.");
        nint previous = (nint)field.GetValue(bridge)!;
        nint weak = IL2CPP.il2cpp_gchandle_new_weakref(bridge.Pointer, false);
        if (weak == 0) throw new InvalidOperationException("Bridge weak handle allocation failed.");
        try { field.SetValue(bridge, weak); }
        catch { IL2CPP.il2cpp_gchandle_free(weak); throw; }
        IL2CPP.il2cpp_gchandle_free(previous);
        GC.KeepAlive(callback);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InvokeRetained(AudioClip clip, WeakReference reference)
    {
        var state = reference.Target as CallbackState;
        var samples = new Il2CppStructArray<float>(1);
        clip.InvokePCMReaderCallback_Internal(samples);
        return state?.Calls == 1 && samples[0] == 0.625f;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RemoveRetained(NativeEventField field)
    {
        var callback = Il2CppObjectPool.Get<NativeDelegate>(field.Read());
        field.Update(callback, add: false);
        if (field.Read() != IntPtr.Zero) throw new InvalidOperationException("Lifetime callback removal did not clear storage.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static unsafe void ScrubStack()
    {
        byte* bytes = stackalloc byte[32768];
        new Span<byte>(bytes, 32768).Clear();
    }

    private void CleanupLifetime()
    {
        lifetimeActive = false;
        if (!workerCleanupComplete) return; // A timed-out worker can still be using the owned field.
        if (retainedHandle != 0) IL2CPP.il2cpp_gchandle_free(retainedHandle);
        if (controlHandle != 0) IL2CPP.il2cpp_gchandle_free(controlHandle);
        retainedHandle = controlHandle = 0;
        lifetimeField = null;
        retainedState = controlState = null;
        if (lifetimeClip != null) UnityEngine.Object.Destroy(lifetimeClip);
        lifetimeClip = null;
    }

    private static void RunWorkers(int count, Action<CancellationToken, Barrier?> action, bool forceCollision = false)
    {
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var start = new ManualResetEventSlim(false);
        var stop = new CancellationTokenSource();
        var collision = forceCollision ? new Barrier(count) : null;
        var started = new List<Thread>();
        workerCleanupComplete = false;
        var threads = Enumerable.Range(0, count).Select(_ => new Thread(() =>
        {
            IntPtr attached = IntPtr.Zero;
            try
            {
                if (IL2CPP.il2cpp_thread_current() == IntPtr.Zero) attached = IL2CPP.il2cpp_thread_attach(IL2CPP.il2cpp_domain_get());
                start.Wait(stop.Token);
                action(stop.Token, collision);
            }
            catch (Exception exception) { errors.Enqueue(exception); }
            finally { if (attached != IntPtr.Zero) IL2CPP.il2cpp_thread_detach(attached); }
        }) { IsBackground = true }).ToArray();
        try
        {
            foreach (var thread in threads) { thread.Start(); started.Add(thread); }
            start.Set();
            if (!JoinAll(started, TimeSpan.FromSeconds(5))) throw new TimeoutException("Native CAS worker did not finish.");
            if (!errors.IsEmpty) throw new AggregateException(errors);
        }
        finally
        {
            stop.Cancel();
            start.Set();
            workerCleanupComplete = JoinAll(started, TimeSpan.FromSeconds(2));
            // A stuck native call cannot be cancelled safely. Keep its owner and wait
            // primitives alive until the test runner stops this failed game process.
            if (workerCleanupComplete) { collision?.Dispose(); start.Dispose(); stop.Dispose(); }
        }
    }

    private static bool JoinAll(IEnumerable<Thread> threads, TimeSpan timeout)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        foreach (var thread in threads)
            if (!thread.Join(TimeSpan.FromMilliseconds(Math.Max(0, (timeout - timer.Elapsed).TotalMilliseconds)))) return false;
        return true;
    }
}

internal sealed unsafe class NativeEventField
{
    private readonly Il2CppObjectBase owner;
    private readonly IntPtr address;
    private readonly IntPtr compareExchange;
    private int retryCount;
    public int RetryCount => Volatile.Read(ref retryCount);

    public NativeEventField(Il2CppObjectBase owner, string name)
    {
        this.owner = owner;
        IntPtr pointer = IL2CPP.Il2CppObjectBaseToPtrNotNull(owner);
        IntPtr field = IL2CPP.il2cpp_class_get_field_from_name(IL2CPP.il2cpp_object_get_class(pointer), name);
        if (field == IntPtr.Zero) throw new MissingFieldException(name);
        IntPtr fieldClass = IL2CPP.il2cpp_class_from_type(IL2CPP.il2cpp_field_get_type(field));
        IntPtr delegateClass = Il2CppClassPointerStore<NativeDelegate>.NativeClassPtr;
        if ((IL2CPP.il2cpp_field_get_flags(field) & 0x70) != 0 || fieldClass == IntPtr.Zero || delegateClass == IntPtr.Zero ||
            !IL2CPP.il2cpp_class_is_assignable_from(delegateClass, fieldClass))
            throw new NotSupportedException("The probe requires a writable instance delegate field.");
        address = pointer + checked((int)IL2CPP.il2cpp_field_get_offset(field));
        compareExchange = IL2CPP.GetIl2CppMethod(Il2CppClassPointerStore.GetNativeClassPointer(typeof(Il2CppSystem.Threading.Interlocked)),
            false, "CompareExchange", "System.Object", "System.Object&", "System.Object", "System.Object");
        string[] parameters = { "System.Object&", "System.Object", "System.Object" };
        if (compareExchange == IntPtr.Zero || IL2CPP.il2cpp_method_get_param_count(compareExchange) != 3 ||
            IL2CPP.il2cpp_type_get_name_(IL2CPP.il2cpp_method_get_return_type(compareExchange)) != "System.Object" ||
            parameters.Where((p, i) => IL2CPP.il2cpp_type_get_name_(IL2CPP.il2cpp_method_get_param(compareExchange, (uint)i)) != p).Any())
            throw new NotSupportedException("Exact native object CompareExchange overload is unavailable.");
    }

    public IntPtr Read()
    {
        try { return Volatile.Read(ref *(IntPtr*)address); }
        finally { GC.KeepAlive(owner); }
    }

    public IntPtr CompareExchange(NativeDelegate? value, NativeDelegate? expected)
    {
        try
        {
            IntPtr* arguments = stackalloc IntPtr[3];
            arguments[0] = address;
            arguments[1] = value is null ? IntPtr.Zero : IL2CPP.Il2CppObjectBaseToPtr(value);
            arguments[2] = expected is null ? IntPtr.Zero : IL2CPP.Il2CppObjectBaseToPtr(expected);
            IntPtr exception = IntPtr.Zero;
            IntPtr observed = IL2CPP.il2cpp_runtime_invoke(compareExchange, IntPtr.Zero, (void**)arguments, ref exception);
            Il2CppInterop.Runtime.Il2CppException.RaiseExceptionIfNecessary(exception);
            return observed;
        }
        finally { GC.KeepAlive(owner); GC.KeepAlive(value); GC.KeepAlive(expected); }
    }

    public void Update(NativeDelegate value, bool add, Action? beforeFirstCompare = null, CancellationToken cancellation = default,
        Action? beforeFirstAcquire = null)
    {
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            IntPtr observed = Read();
            beforeFirstAcquire?.Invoke();
            beforeFirstAcquire = null;
            var current = observed == IntPtr.Zero ? null : Il2CppObjectPool.Get<NativeDelegate>(observed);
            var next = add ? NativeDelegate.Combine(current, value) : NativeDelegate.Remove(current, value);
            beforeFirstCompare?.Invoke();
            beforeFirstCompare = null;
            if (CompareExchange(next, current) == observed) return;
            Interlocked.Increment(ref retryCount);
        }
    }
}
