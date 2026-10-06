using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace S1Interop.Compiler;

/// <summary>Uses the installed loader's registered iterator adapter while preserving coroutine identity.</summary>
internal static class CoroutineSupportSource
{
    public const string AdapterType = "MelonLoader.Support.MonoEnumeratorWrapper";
    public const string NativeEnumerator = "Il2CppSystem.Collections.IEnumerator";
    public const string HelperType = "global::S1Interop.Compiler.Generated.S1InteropCoroutine";

    public static SyntaxTree CreateTree(CSharpParseOptions options, MetadataSymbolMap map, CancellationToken token) =>
        CSharpSyntaxTree.ParseText("""
            namespace S1Interop.Compiler.Generated {
                public static class S1InteropCoroutine {
                    private sealed class Entry {
                        public global::System.WeakReference Routine;
                        public __HANDLE__ Handle;
                    }
                    private static readonly object Gate = new object();
                    private static readonly global::System.Collections.Generic.Dictionary<int,
                        global::System.Collections.Generic.List<Entry>> Buckets =
                        new global::System.Collections.Generic.Dictionary<int, global::System.Collections.Generic.List<Entry>>();
                    private static int inserts;
                    private class NativeView : global::System.Collections.IEnumerator, IS1InteropNativeView {
                        internal readonly global::Il2CppSystem.Collections.IEnumerator Native;
                        internal NativeView(global::Il2CppSystem.Collections.IEnumerator native) { Native = native; }
                        public global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase NativeObject => Native;
                        public object Current => Native.Current;
                        public bool MoveNext() => Native.MoveNext();
                        public void Reset() => Native.Reset();
                    }
                    __DISPOSABLE_VIEW__
                    private static readonly global::System.Collections.Generic.Dictionary<global::System.IntPtr,
                        global::System.WeakReference> Views =
                        new global::System.Collections.Generic.Dictionary<global::System.IntPtr, global::System.WeakReference>();

                    public static global::System.Collections.IEnumerator FromNative(global::Il2CppSystem.Collections.IEnumerator routine) {
                        if (routine is null) return null;
                        lock (Gate) {
                            // Returning an adapter that originated in managed code must recover the original iterator.
                            foreach (var bucket in Buckets.Values)
                                foreach (var entry in bucket)
                                    if (global::Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_get_target(entry.Handle) == routine.Pointer &&
                                        entry.Routine.Target is global::System.Collections.IEnumerator original) return original;
                            global::System.WeakReference weak;
                            if (Views.TryGetValue(routine.Pointer, out weak) && weak.Target is NativeView existing) return existing;
                            __CREATE_VIEW__
                            Views[routine.Pointer] = new global::System.WeakReference(view);
                            if (++inserts >= 64) { inserts = 0; Sweep(); }
                            return view;
                        }
                    }

                    public static global::Il2CppSystem.Collections.IEnumerator Wrap(global::System.Collections.IEnumerator routine) {
                        if (routine is null) return null;
                        if (routine is NativeView view) return view.Native;
                        int hash = global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(routine);
                        lock (Gate) {
                            var existing = Find(hash, routine);
                            if (!(existing is null)) return existing;
                        }
                        var created = new global::MelonLoader.Support.MonoEnumeratorWrapper(routine)
                            .Cast<global::Il2CppSystem.Collections.IEnumerator>();
                        lock (Gate) {
                            var existing = Find(hash, routine);
                            if (!(existing is null)) return existing;
                            global::System.Collections.Generic.List<Entry> bucket;
                            if (!Buckets.TryGetValue(hash, out bucket)) {
                                bucket = new global::System.Collections.Generic.List<Entry>();
                                Buckets.Add(hash, bucket);
                            }
                            bucket.Add(new Entry {
                                Routine = new global::System.WeakReference(routine),
                                Handle = global::Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_new_weakref(created.Pointer, false)
                            });
                            if (++inserts >= 64) { inserts = 0; Sweep(); }
                        }
                        return created;
                    }

                    private static global::Il2CppSystem.Collections.IEnumerator Find(int hash, object routine) {
                        global::System.Collections.Generic.List<Entry> bucket;
                        if (!Buckets.TryGetValue(hash, out bucket)) return null;
                        global::Il2CppSystem.Collections.IEnumerator found = null;
                        for (int i = bucket.Count - 1; i >= 0; i--) {
                            object target = bucket[i].Routine.Target;
                            var pointer = global::Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_get_target(bucket[i].Handle);
                            if (target is null || pointer == global::System.IntPtr.Zero) Release(bucket, i);
                            else if (global::System.Object.ReferenceEquals(target, routine))
                                found = global::Il2CppInterop.Runtime.Runtime.Il2CppObjectPool.Get<global::Il2CppSystem.Collections.IEnumerator>(pointer);
                        }
                        if (bucket.Count == 0) Buckets.Remove(hash);
                        return found;
                    }

                    private static void Sweep() {
                        var emptied = new global::System.Collections.Generic.List<int>();
                        foreach (var pair in Buckets) {
                            var bucket = pair.Value;
                            for (int i = bucket.Count - 1; i >= 0; i--) {
                                if (bucket[i].Routine.Target is null ||
                                    global::Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_get_target(bucket[i].Handle) == global::System.IntPtr.Zero)
                                    Release(bucket, i);
                            }
                            if (bucket.Count == 0) emptied.Add(pair.Key);
                        }
                        foreach (int key in emptied) Buckets.Remove(key);
                        var deadViews = new global::System.Collections.Generic.List<global::System.IntPtr>();
                        foreach (var pair in Views) if (!pair.Value.IsAlive) deadViews.Add(pair.Key);
                        foreach (var key in deadViews) Views.Remove(key);
                    }

                    private static void Release(global::System.Collections.Generic.List<Entry> bucket, int index) {
                        global::Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_free(bucket[index].Handle);
                        bucket.RemoveAt(index);
                    }
                }
            }
            """.Replace("__HANDLE__", NativeDelegateCacheSource.ResolveHandleType(map))
            .Replace("__DISPOSABLE_VIEW__", map.FindTargetType("Il2CppSystem.IDisposable") is null ? "" : """
                private sealed class DisposableNativeView : NativeView, global::System.IDisposable {
                    private readonly global::Il2CppSystem.IDisposable disposable;
                    internal DisposableNativeView(global::Il2CppSystem.Collections.IEnumerator native,
                        global::Il2CppSystem.IDisposable disposable) : base(native) { this.disposable = disposable; }
                    public void Dispose() => disposable.Dispose();
                }
                """)
            .Replace("__CREATE_VIEW__", map.FindTargetType("Il2CppSystem.IDisposable") is null
                ? "var view = new NativeView(routine);" : """
                var disposable = routine.TryCast<global::Il2CppSystem.IDisposable>();
                NativeView view = disposable is null ? new NativeView(routine) : new DisposableNativeView(routine, disposable);
                """),
            options, "S1Interop.Compiler.Coroutine.g.cs", System.Text.Encoding.UTF8, token);
}
