using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace S1Interop.Compiler;

/// <summary>
/// Emits the helper that lowered managed-to-native delegate conversions call. Equivalent callbacks (by
/// <see cref="System.Delegate.Equals(object)"/>, so recreated method groups match) map to the same native delegate
/// while it is alive, which native <c>RemoveListener</c>/<c>-=</c> needs because IL2CPP compares delegate targets
/// by reference and every <c>DelegateSupport.ConvertDelegate</c> call allocates a fresh target.
/// </summary>
/// <remarks>
/// Ownership: <c>ConvertDelegate</c> makes the native delegate's target an injected
/// <c>Il2CppToMonoDelegateReference</c> whose <c>ReferencedDelegate</c> field holds the managed callback. The injected
/// native object keeps its managed peer alive through a strong GC handle until the native object is finalized, so a
/// native event that owns the converted delegate roots the callback. The cache holds only a managed weak reference to
/// the callback and a native weak (non-resurrection-tracking) GC handle to the converted delegate, so it roots neither.
/// The bridge's own native handle is weakened after conversion to break its mutually strong ownership cycle.
/// <para>
/// Residual race: between <c>il2cpp_gchandle_get_target</c> returning a pointer and <c>Il2CppObjectPool.Get</c>
/// creating a wrapper (which takes a strong handle), the only reference is the pointer on this thread's stack. That is
/// safe only because the IL2CPP Boehm GC scans attached thread stacks conservatively; callers must run on an
/// IL2CPP-attached thread, which every Il2CppInterop call already requires. Once the weak handle reads zero the native
/// delegate is unreachable and a new conversion is correct: no native event can still contain the old one.
/// </para>
/// </remarks>
internal static class NativeDelegateCacheSource
{
    public const string TypeName = "global::S1Interop.Compiler.Generated.S1InteropNativeDelegate";
    public const string FilePath = "S1Interop.Compiler/S1InteropNativeDelegate.g.cs";

    private const string Il2CppTypeName = "Il2CppInterop.Runtime.IL2CPP";
    private const string HandlePlaceholder = "__S1_GCHANDLE__";

    private const string Body = """
        namespace S1Interop.Compiler.Generated
        {
            [global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)]
            public sealed class S1InteropEventAccessorAttribute : global::System.Attribute
            {
                public S1InteropEventAccessorAttribute(string eventName, bool add) { }
            }
            public static class S1InteropNativeDelegate
            {
                private static class EventGate<T> { internal static readonly object Value = new object(); }

                private static class BridgeOwnership
                {
                    private static readonly global::System.Type BridgeType = typeof(global::Il2CppInterop.Runtime.DelegateSupport)
                        .GetNestedType("Il2CppToMonoDelegateReference", global::System.Reflection.BindingFlags.NonPublic);
                    private static readonly global::System.Reflection.FieldInfo CallbackField = BridgeType == null ? null :
                        BridgeType.GetField("ReferencedDelegate", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Public);

                    internal static void Validate()
                    {
                        if (BridgeType == null || !typeof(global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).IsAssignableFrom(BridgeType) ||
                            CallbackField == null || CallbackField.FieldType != typeof(global::System.Delegate))
                            throw new global::System.NotSupportedException("The installed Il2CppInterop delegate bridge layout cannot support callback ownership.");
                        S1InteropInjection.ValidateWeakOwnership();
                    }

                    internal static void ReleaseCycle(global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase converted, global::System.Delegate callback)
                    {
                        var target = converted.Cast<global::Il2CppSystem.Delegate>().m_target;
                        if (target is null)
                            throw new global::System.NotSupportedException("The converted delegate has no injected callback target.");
                        var bridge = global::Il2CppInterop.Runtime.Runtime.ClassInjectorBase.GetMonoObjectFromIl2CppPointer(target.Pointer)
                            as global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
                        if (bridge == null || bridge.GetType() != BridgeType)
                            throw new global::System.NotSupportedException("The converted delegate does not own the expected managed callback bridge.");
                        var owner = new global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase(target.Pointer);
                        S1InteropInjection.WeakenNativeReference(bridge, owner);
                        // Once its known ownership cycle is released, reject an unexpected callback
                        // without permanently retaining the failed conversion's bridge.
                        if (!global::System.Object.ReferenceEquals(CallbackField.GetValue(bridge), callback))
                            throw new global::System.NotSupportedException("The converted delegate bridge owns a different managed callback.");
                        global::System.GC.KeepAlive(converted);
                    }
                }

                public static void Add<T>(ref T location, T callback) where T : global::Il2CppSystem.Delegate
                {
                    lock (EventGate<T>.Value)
                    {
                        var combined = global::Il2CppSystem.Delegate.Combine(location, callback);
                        location = combined is null ? null : combined.Cast<T>();
                    }
                }

                public static void Remove<T>(ref T location, T callback) where T : global::Il2CppSystem.Delegate
                {
                    lock (EventGate<T>.Value)
                    {
                        var remaining = global::Il2CppSystem.Delegate.Remove(location, callback);
                        location = remaining is null ? null : remaining.Cast<T>();
                    }
                }

                public static T Convert<T>(global::System.Delegate callback) where T : global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
                {
                    if (callback is null) return null;
                    return Cache<T>.Convert(callback);
                }

                private sealed class Entry
                {
                    public global::System.WeakReference Callback;
                    public __S1_GCHANDLE__ Handle;
                }

                private static class Cache<T> where T : global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
                {
                    // Periodically reclaim dead weak handles without rooting event targets.
                    private const int SweepInterval = 64;
                    private static readonly object Gate = new object();
                    private static readonly global::System.Collections.Generic.Dictionary<int, global::System.Collections.Generic.List<Entry>> Buckets =
                        new global::System.Collections.Generic.Dictionary<int, global::System.Collections.Generic.List<Entry>>();
                    private static int insertsSinceSweep;

                    public static T Convert(global::System.Delegate callback)
                    {
                        int hash = callback.GetHashCode();
                        lock (Gate)
                        {
                            T cached = Find(hash, callback);
                            if (!(cached is null)) return cached;
                        }

                        // Convert outside the lock: class injection and native allocation can be slow or re-enter.
                        BridgeOwnership.Validate();
                        T converted = global::Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<T>(callback);
                        if (converted is null) return null;
                        BridgeOwnership.ReleaseCycle(converted, callback);

                        lock (Gate)
                        {
                            // Another thread may have converted an equal callback meanwhile; prefer the published one.
                            T raced = Find(hash, callback);
                            if (!(raced is null)) return raced;

                            global::System.Collections.Generic.List<Entry> bucket;
                            if (!Buckets.TryGetValue(hash, out bucket))
                            {
                                bucket = new global::System.Collections.Generic.List<Entry>(1);
                                Buckets.Add(hash, bucket);
                            }

                            var handle = global::Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_new_weakref(converted.Pointer, false);
                            if (handle == default(__S1_GCHANDLE__))
                                throw new global::System.InvalidOperationException("Native delegate cache weak handle allocation failed.");
                            try { bucket.Add(new Entry
                            {
                                Callback = new global::System.WeakReference(callback),
                                Handle = handle,
                            }); }
                            catch { global::Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_free(handle); throw; }

                            if (++insertsSinceSweep >= SweepInterval)
                            {
                                insertsSinceSweep = 0;
                                SweepAll();
                            }
                        }

                        return converted;
                    }

                    // Caller holds Gate. Also prunes dead entries in the probed bucket.
                    private static T Find(int hash, global::System.Delegate callback)
                    {
                        global::System.Collections.Generic.List<Entry> bucket;
                        if (!Buckets.TryGetValue(hash, out bucket)) return null;

                        T found = null;
                        for (int i = bucket.Count - 1; i >= 0; i--)
                        {
                            Entry entry = bucket[i];
                            global::System.IntPtr pointer;
                            object target = Resolve(entry, out pointer);
                            if (target is null)
                            {
                                Release(bucket, i);
                                continue;
                            }

                            if (found is null && callback.Equals(target))
                            {
                                found = global::Il2CppInterop.Runtime.Runtime.Il2CppObjectPool.Get<T>(pointer);
                            }
                        }

                        if (bucket.Count == 0) Buckets.Remove(hash);
                        return found;
                    }

                    private static void SweepAll()
                    {
                        var emptied = new global::System.Collections.Generic.List<int>();
                        foreach (var pair in Buckets)
                        {
                            var bucket = pair.Value;
                            for (int i = bucket.Count - 1; i >= 0; i--)
                            {
                                global::System.IntPtr pointer;
                                if (Resolve(bucket[i], out pointer) is null) Release(bucket, i);
                            }

                            if (bucket.Count == 0) emptied.Add(pair.Key);
                        }

                        foreach (int key in emptied) Buckets.Remove(key);
                    }

                    // Returns the live callback when both the callback and its native delegate are alive, else null.
                    private static object Resolve(Entry entry, out global::System.IntPtr pointer)
                    {
                        pointer = global::System.IntPtr.Zero;
                        object target = entry.Callback.Target;
                        if (target is null) return null;
                        pointer = global::Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_get_target(entry.Handle);
                        return pointer == global::System.IntPtr.Zero ? null : target;
                    }

                    private static void Release(global::System.Collections.Generic.List<Entry> bucket, int index)
                    {
                        global::Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_free(bucket[index].Handle);
                        bucket.RemoveAt(index);
                    }
                }
            }
        }
        """;

    public static SyntaxTree CreateTree(CSharpParseOptions options, MetadataSymbolMap map, CancellationToken token = default)
    {
        string handleType = ResolveHandleType(map);
        string nullable = options.LanguageVersion >= LanguageVersion.CSharp8 ? "#nullable disable\n" : string.Empty;
        return CSharpSyntaxTree.ParseText(
            "// <auto-generated/>\n" + nullable + Body.Replace(HandlePlaceholder, handleType),
            options,
            FilePath,
            System.Text.Encoding.UTF8,
            token);
    }

    // The GC handle is nint in current Il2CppInterop but uint in older loaders; emit whatever the referenced build uses.
    internal static string ResolveHandleType(MetadataSymbolMap map)
    {
        INamedTypeSymbol il2cpp = map.FindTargetType(Il2CppTypeName)
            ?? throw new InvalidOperationException($"Native delegate helper requires '{Il2CppTypeName}', which is not referenced.");

        IMethodSymbol newWeakRef = FindStatic(il2cpp, "il2cpp_gchandle_new_weakref", 2);
        ITypeSymbol handle = newWeakRef.ReturnType;
        if (newWeakRef.Parameters[0].Type.SpecialType != SpecialType.System_IntPtr
            || newWeakRef.Parameters[1].Type.SpecialType != SpecialType.System_Boolean)
        {
            throw Unsupported("il2cpp_gchandle_new_weakref(IntPtr, bool)");
        }

        IMethodSymbol getTarget = FindStatic(il2cpp, "il2cpp_gchandle_get_target", 1);
        if (getTarget.ReturnType.SpecialType != SpecialType.System_IntPtr
            || !SymbolEqualityComparer.Default.Equals(getTarget.Parameters[0].Type, handle))
        {
            throw Unsupported("IntPtr il2cpp_gchandle_get_target(handle)");
        }

        IMethodSymbol free = FindStatic(il2cpp, "il2cpp_gchandle_free", 1);
        if (!SymbolEqualityComparer.Default.Equals(free.Parameters[0].Type, handle))
            throw Unsupported("il2cpp_gchandle_free(handle)");

        RequireType(map, "Il2CppInterop.Runtime.DelegateSupport", "ConvertDelegate");
        RequireType(map, "Il2CppInterop.Runtime.Runtime.Il2CppObjectPool", "Get");

        return handle.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static IMethodSymbol FindStatic(INamedTypeSymbol type, string name, int arity)
    {
        IMethodSymbol? match = null;
        foreach (IMethodSymbol method in type.GetMembers(name).OfType<IMethodSymbol>())
        {
            if (!method.IsStatic || method.DeclaredAccessibility != Accessibility.Public || method.Parameters.Length != arity)
                continue;
            if (match is not null)
                throw Unsupported($"a unique {name} with {arity} parameter(s)");
            match = method;
        }

        return match ?? throw Unsupported($"public static {name} with {arity} parameter(s)");
    }

    private static void RequireType(MetadataSymbolMap map, string metadataName, string method)
    {
        INamedTypeSymbol? type = map.FindTargetType(metadataName);
        if (type is null || !type.GetMembers(method).OfType<IMethodSymbol>().Any(m => m.IsStatic && m.IsGenericMethod))
            throw Unsupported($"{metadataName}.{method}<T>");
    }

    private static InvalidOperationException Unsupported(string member) =>
        new($"Native delegate helper requires Il2CppInterop member {member}; the referenced Il2CppInterop.Runtime does not expose it.");
}
