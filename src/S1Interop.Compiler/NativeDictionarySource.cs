using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace S1Interop.Compiler;

/// <summary>Emits live dictionary views for storage that crosses a native boundary.</summary>
internal static class NativeDictionarySource
{
    public const string TypeName = "global::S1Interop.Compiler.Generated.S1InteropDictionary";

    private const string Body = """
        namespace S1Interop.Compiler.Generated
        {
            using SC = global::System.Collections;
            using SCG = global::System.Collections.Generic;
            using NC = global::Il2CppSystem.Collections.Generic;

            public sealed class S1InteropDictionaryComparer<T> : SCG.IEqualityComparer<T>
            {
                internal readonly NC.IEqualityComparer<T> Native;
                internal S1InteropDictionaryComparer(NC.IEqualityComparer<T> native) { Native = native; }
                public bool Equals(T left, T right) => Native is null
                    ? NC.EqualityComparer<T>.Default.Equals(left, right) : Native.Equals(left, right);
                public int GetHashCode(T value) => Native is null
                    ? NC.EqualityComparer<T>.Default.GetHashCode(value) : Native.GetHashCode(value);

                // Adapter registry for this key type. Mods register delegates only (no native calls); the native adapter
                // class is injected lazily on first Create. The first registered factory serves every mod because an
                // adapter is stateless apart from the CLR comparer it forwards to. Every registered recoverer is asked to
                // unwrap a native comparer, since each mod injects its own adapter class.
                private static readonly object FactoryGate = new object();
                private static global::System.Func<SCG.IEqualityComparer<T>, NC.IEqualityComparer<T>> factory;
                private static global::System.Func<NC.IEqualityComparer<T>, SCG.IEqualityComparer<T>>[] recoverers =
                    new global::System.Func<NC.IEqualityComparer<T>, SCG.IEqualityComparer<T>>[0];

                public static void RegisterFactory(
                    global::System.Func<SCG.IEqualityComparer<T>, NC.IEqualityComparer<T>> create,
                    global::System.Func<NC.IEqualityComparer<T>, SCG.IEqualityComparer<T>> recover)
                {
                    if (create is null) throw new global::System.ArgumentNullException(nameof(create));
                    if (recover is null) throw new global::System.ArgumentNullException(nameof(recover));
                    lock (FactoryGate)
                    {
                        var current = recoverers;
                        if (global::System.Array.IndexOf(current, recover) >= 0) return;
                        var next = new global::System.Func<NC.IEqualityComparer<T>, SCG.IEqualityComparer<T>>[current.Length + 1];
                        global::System.Array.Copy(current, next, current.Length);
                        next[current.Length] = recover;
                        global::System.Threading.Volatile.Write(ref recoverers, next);
                        if (factory is null) global::System.Threading.Volatile.Write(ref factory, create);
                    }
                }

                // Returns the CLR comparer a native comparer was created from when this bridge made it, otherwise a view of
                // the native comparer (a null native comparer means the native default).
                internal static SCG.IEqualityComparer<T> FromNative(NC.IEqualityComparer<T> native)
                {
                    if (!(native is null))
                    {
                        var candidates = global::System.Threading.Volatile.Read(ref recoverers);
                        for (int i = 0; i < candidates.Length; i++)
                        {
                            var original = candidates[i](native);
                            if (!(original is null)) return original;
                        }
                    }
                    return new S1InteropDictionaryComparer<T>(native);
                }

                internal static NC.IEqualityComparer<T> Resolve(SCG.IEqualityComparer<T> comparer)
                {
                    if (comparer is null || global::System.Object.ReferenceEquals(comparer, SCG.EqualityComparer<T>.Default) ||
                        comparer is IS1InteropDefaultEqualityComparer) return null;
                    if (comparer is S1InteropDictionaryComparer<T> native) return native.Native;
                    __STRING_COMPARERS__
                    var create = global::System.Threading.Volatile.Read(ref factory);
                    if (!(create is null))
                    {
                        var adapted = create(comparer);
                        if (adapted is null) throw new global::System.InvalidOperationException("The native dictionary comparer adapter returned no comparer.");
                        return adapted;
                    }
                    throw new global::System.NotSupportedException("This managed comparer has no native dictionary adapter.");
                }
            }

            public sealed class S1InteropDictionary<TKey, TValue> : SCG.IDictionary<TKey, TValue>,
                SCG.IReadOnlyDictionary<TKey, TValue>, SC.IDictionary, IS1InteropNativeView
            {
                private static readonly SCG.Dictionary<global::System.IntPtr, global::System.WeakReference<S1InteropDictionary<TKey, TValue>>> Cache =
                    new SCG.Dictionary<global::System.IntPtr, global::System.WeakReference<S1InteropDictionary<TKey, TValue>>>();
                private static int inserts;
                private readonly NC.Dictionary<TKey, TValue> native;
                private readonly object syncRoot = new object();
                private KeyCollection keys;
                private ValueCollection values;
                private SCG.IEqualityComparer<TKey> comparer;
                global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase IS1InteropNativeView.NativeObject => native;

                public S1InteropDictionary() : this(0, null) { }
                public S1InteropDictionary(int capacity) : this(capacity, null) { }
                public S1InteropDictionary(SCG.IEqualityComparer<TKey> comparer) : this(0, comparer) { }
                public S1InteropDictionary(int capacity, SCG.IEqualityComparer<TKey> comparer)
                {
                    if (capacity < 0) throw new global::System.ArgumentOutOfRangeException(nameof(capacity));
                    native = new NC.Dictionary<TKey, TValue>(capacity, S1InteropDictionaryComparer<TKey>.Resolve(comparer));
                    lock (Cache) Register(this);
                }
                public S1InteropDictionary(SCG.IDictionary<TKey, TValue> dictionary) : this(dictionary, null) { }
                public S1InteropDictionary(SCG.IDictionary<TKey, TValue> dictionary, SCG.IEqualityComparer<TKey> comparer)
                    : this(dictionary is null ? throw new global::System.ArgumentNullException(nameof(dictionary)) : dictionary.Count, comparer)
                { foreach (var pair in dictionary) Add(pair.Key, pair.Value); }
                public S1InteropDictionary(SCG.IEnumerable<SCG.KeyValuePair<TKey, TValue>> collection) : this(collection, null) { }
                public S1InteropDictionary(SCG.IEnumerable<SCG.KeyValuePair<TKey, TValue>> collection, SCG.IEqualityComparer<TKey> comparer) : this(0, comparer)
                {
                    if (collection is null) throw new global::System.ArgumentNullException(nameof(collection));
                    foreach (var pair in collection) Add(pair.Key, pair.Value);
                }
                private S1InteropDictionary(NC.Dictionary<TKey, TValue> native) { this.native = native; }

                public static S1InteropDictionary<TKey, TValue> CopyManaged(SCG.Dictionary<TKey, TValue> dictionary) =>
                    new S1InteropDictionary<TKey, TValue>(dictionary, dictionary.Comparer);

                public static S1InteropDictionary<TKey, TValue> FromNative(NC.Dictionary<TKey, TValue> native)
                {
                    if (native is null) return null;
                    lock (Cache)
                    {
                        if (Cache.TryGetValue(native.Pointer, out var weak) && weak.TryGetTarget(out var existing)) return existing;
                        var created = new S1InteropDictionary<TKey, TValue>(native);
                        Register(created);
                        return created;
                    }
                }
                private static void Register(S1InteropDictionary<TKey, TValue> value)
                {
                    Cache[value.native.Pointer] = new global::System.WeakReference<S1InteropDictionary<TKey, TValue>>(value);
                    if (++inserts < 64) return;
                    inserts = 0;
                    var dead = new SCG.List<global::System.IntPtr>();
                    foreach (var pair in Cache) if (!pair.Value.TryGetTarget(out _)) dead.Add(pair.Key);
                    foreach (var key in dead) Cache.Remove(key);
                }
                public static NC.Dictionary<TKey, TValue> ToNative(S1InteropDictionary<TKey, TValue> value) => value is null ? null : value.native;
                public static implicit operator S1InteropDictionary<TKey, TValue>(NC.Dictionary<TKey, TValue> value) => FromNative(value);
                public static implicit operator NC.Dictionary<TKey, TValue>(S1InteropDictionary<TKey, TValue> value) => ToNative(value);
                public static S1InteropDictionary<TKey, TValue> TryCast(object value)
                {
                    if (value is S1InteropDictionary<TKey, TValue> view) return view;
                    var proxy = value as global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase ?? (value as IS1InteropNativeView)?.NativeObject;
                    return proxy is null ? null : FromNative(proxy.TryCast<NC.Dictionary<TKey, TValue>>());
                }
                public static S1InteropDictionary<TKey, TValue> Cast(object value) => value is null ? null :
                    TryCast(value) ?? throw new global::System.InvalidCastException();
                public static bool Is(object value) => !(TryCast(value) is null);

                public int Count => native.Count;
                public SCG.IEqualityComparer<TKey> Comparer => comparer ?? (comparer = S1InteropDictionaryComparer<TKey>.FromNative(native._comparer));
                public KeyCollection Keys => keys ?? (keys = new KeyCollection(this));
                public ValueCollection Values => values ?? (values = new ValueCollection(this));
                SCG.ICollection<TKey> SCG.IDictionary<TKey, TValue>.Keys => Keys;
                SCG.ICollection<TValue> SCG.IDictionary<TKey, TValue>.Values => Values;
                SCG.IEnumerable<TKey> SCG.IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
                SCG.IEnumerable<TValue> SCG.IReadOnlyDictionary<TKey, TValue>.Values => Values;
                SC.ICollection SC.IDictionary.Keys => Keys;
                SC.ICollection SC.IDictionary.Values => Values;
                bool SCG.ICollection<SCG.KeyValuePair<TKey, TValue>>.IsReadOnly => false;
                bool SC.IDictionary.IsReadOnly => false;
                bool SC.IDictionary.IsFixedSize => false;
                bool SC.ICollection.IsSynchronized => false;
                object SC.ICollection.SyncRoot => syncRoot;

                private static void CheckKey(TKey key) { if (key is null) throw new global::System.ArgumentNullException(nameof(key)); }
                public TValue this[TKey key]
                {
                    get { if (!TryGetValue(key, out var value)) throw new SCG.KeyNotFoundException(); return value; }
                    set { CheckKey(key); native[key] = value; }
                }
                public void Add(TKey key, TValue value)
                {
                    CheckKey(key);
                    if (native.ContainsKey(key)) throw new global::System.ArgumentException("An item with the same key has already been added.", nameof(key));
                    native.Add(key, value);
                }
                public bool TryAdd(TKey key, TValue value) { CheckKey(key); return native.TryAdd(key, value); }
                public bool ContainsKey(TKey key) { CheckKey(key); return native.ContainsKey(key); }
                public bool ContainsValue(TValue value) => native.ContainsValue(value);
                public bool TryGetValue(TKey key, out TValue value) { CheckKey(key); return native.TryGetValue(key, out value); }
                public bool Remove(TKey key) { CheckKey(key); return native.Remove(key); }
                public bool Remove(TKey key, out TValue value) { if (!TryGetValue(key, out value)) return false; return native.Remove(key); }
                public void Clear() => native.Clear();
                void SCG.ICollection<SCG.KeyValuePair<TKey, TValue>>.Add(SCG.KeyValuePair<TKey, TValue> pair) => Add(pair.Key, pair.Value);
                bool SCG.ICollection<SCG.KeyValuePair<TKey, TValue>>.Contains(SCG.KeyValuePair<TKey, TValue> pair) =>
                    TryGetValue(pair.Key, out var value) && NC.EqualityComparer<TValue>.Default.Equals(value, pair.Value);
                bool SCG.ICollection<SCG.KeyValuePair<TKey, TValue>>.Remove(SCG.KeyValuePair<TKey, TValue> pair) =>
                    ((SCG.ICollection<SCG.KeyValuePair<TKey, TValue>>)this).Contains(pair) && Remove(pair.Key);
                void SCG.ICollection<SCG.KeyValuePair<TKey, TValue>>.CopyTo(SCG.KeyValuePair<TKey, TValue>[] array, int index) => Copy(this, Count, array, index);

                private static bool CompatibleKey(object key, out TKey typed)
                {
                    if (key is null) throw new global::System.ArgumentNullException(nameof(key));
                    return S1InteropNativeCast.TryValue<TKey>(key, out typed);
                }
                private static T CastValue<T>(object value, string name)
                {
                    if (value is null && !(default(T) is null)) throw new global::System.ArgumentNullException(name);
                    if (!S1InteropNativeCast.TryValue<T>(value, out var typed)) throw new global::System.ArgumentException("Incorrect dictionary element type.", name);
                    return typed;
                }
                object SC.IDictionary.this[object key]
                {
                    get => CompatibleKey(key, out var typed) && TryGetValue(typed, out var value) ? (object)value : null;
                    set { if (key is null) throw new global::System.ArgumentNullException(nameof(key)); this[CastValue<TKey>(key, nameof(key))] = CastValue<TValue>(value, nameof(value)); }
                }
                void SC.IDictionary.Add(object key, object value)
                { if (key is null) throw new global::System.ArgumentNullException(nameof(key)); Add(CastValue<TKey>(key, nameof(key)), CastValue<TValue>(value, nameof(value))); }
                bool SC.IDictionary.Contains(object key) => CompatibleKey(key, out var typed) && ContainsKey(typed);
                void SC.IDictionary.Remove(object key) { if (CompatibleKey(key, out var typed)) Remove(typed); }
                void SC.ICollection.CopyTo(global::System.Array array, int index)
                {
                    CheckArray(array, index, Count);
                    if (array is SCG.KeyValuePair<TKey, TValue>[] pairs) { Copy(this, Count, pairs, index); return; }
                    if (array is SC.DictionaryEntry[] entries) { foreach (var pair in this) entries[index++] = new SC.DictionaryEntry(pair.Key, pair.Value); return; }
                    if (array is object[] objects)
                    {
                        try { foreach (var pair in this) objects[index++] = pair; }
                        catch (global::System.ArrayTypeMismatchException) { throw new global::System.ArgumentException("Incorrect array element type.", nameof(array)); }
                        return;
                    }
                    throw new global::System.ArgumentException("Incorrect array element type.", nameof(array));
                }
                private static void CheckArray(global::System.Array array, int index, int count)
                {
                    if (array is null) throw new global::System.ArgumentNullException(nameof(array));
                    if (array.Rank != 1 || array.GetLowerBound(0) != 0) throw new global::System.ArgumentException("Array must be one dimensional and zero based.", nameof(array));
                    if (index < 0 || index > array.Length) throw new global::System.ArgumentOutOfRangeException(nameof(index));
                    if (array.Length - index < count) throw new global::System.ArgumentException("Destination array is too small.", nameof(array));
                }
                private static void Copy<T>(SCG.IEnumerable<T> source, int count, T[] array, int index)
                { CheckArray(array, index, count); foreach (var value in source) array[index++] = value; }
                private static void CopyObjects<T>(SCG.IEnumerable<T> source, int count, global::System.Array array, int index)
                {
                    CheckArray(array, index, count);
                    if (array is T[] typed) { Copy(source, count, typed, index); return; }
                    if (!(array is object[] objects)) throw new global::System.ArgumentException("Incorrect array element type.", nameof(array));
                    try { foreach (var value in source) objects[index++] = value; }
                    catch (global::System.ArrayTypeMismatchException) { throw new global::System.ArgumentException("Incorrect array element type.", nameof(array)); }
                }

                public Enumerator GetEnumerator() => new Enumerator(this, false);
                SCG.IEnumerator<SCG.KeyValuePair<TKey, TValue>> SCG.IEnumerable<SCG.KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
                SC.IEnumerator SC.IEnumerable.GetEnumerator() => GetEnumerator();
                SC.IDictionaryEnumerator SC.IDictionary.GetEnumerator() => new Enumerator(this, true);
                public struct Enumerator : SCG.IEnumerator<SCG.KeyValuePair<TKey, TValue>>, SC.IDictionaryEnumerator
                {
                    private readonly S1InteropDictionary<TKey, TValue> owner;
                    private readonly int version;
                    private readonly bool dictionaryEntry;
                    private int index;
                    private bool valid;
                    private SCG.KeyValuePair<TKey, TValue> current;
                    internal Enumerator(S1InteropDictionary<TKey, TValue> owner, bool dictionaryEntry)
                    { this.owner = owner; this.dictionaryEntry = dictionaryEntry; version = owner.native._version; index = 0; valid = false; current = default; }
                    public SCG.KeyValuePair<TKey, TValue> Current => current;
                    object SC.IEnumerator.Current { get { CheckCurrent(); return dictionaryEntry ? (object)new SC.DictionaryEntry(current.Key, current.Value) : current; } }
                    public SC.DictionaryEntry Entry { get { CheckCurrent(); return new SC.DictionaryEntry(current.Key, current.Value); } }
                    public object Key { get { CheckCurrent(); return current.Key; } }
                    public object Value { get { CheckCurrent(); return current.Value; } }
                    private void CheckCurrent() { if (!valid) throw new global::System.InvalidOperationException("Enumeration has not started or has finished."); }
                    private void CheckVersion() { if (version != owner.native._version) throw new global::System.InvalidOperationException("Collection was modified; enumeration operation may not execute."); }
                    public bool MoveNext()
                    {
                        CheckVersion();
                        while (index < owner.native._count)
                        {
                            var entry = owner.native._entries[index++];
                            if (entry.hashCode < 0) continue;
                            current = new SCG.KeyValuePair<TKey, TValue>(entry.key, entry.value);
                            valid = true;
                            return true;
                        }
                        current = default;
                        valid = false;
                        return false;
                    }
                    public void Reset() { CheckVersion(); index = 0; valid = false; current = default; }
                    public void Dispose() { }
                }

                public sealed class KeyCollection : SCG.ICollection<TKey>, SCG.IReadOnlyCollection<TKey>, SC.ICollection
                {
                    private readonly S1InteropDictionary<TKey, TValue> owner;
                    public KeyCollection(S1InteropDictionary<TKey, TValue> dictionary) { owner = dictionary ?? throw new global::System.ArgumentNullException(nameof(dictionary)); }
                    public int Count => owner.Count;
                    bool SCG.ICollection<TKey>.IsReadOnly => true;
                    bool SC.ICollection.IsSynchronized => false;
                    object SC.ICollection.SyncRoot => owner.syncRoot;
                    public bool Contains(TKey key) => owner.ContainsKey(key);
                    public void CopyTo(TKey[] array, int index) => Copy(this, Count, array, index);
                    void SC.ICollection.CopyTo(global::System.Array array, int index) => CopyObjects(this, Count, array, index);
                    void SCG.ICollection<TKey>.Add(TKey key) => throw new global::System.NotSupportedException();
                    void SCG.ICollection<TKey>.Clear() => throw new global::System.NotSupportedException();
                    bool SCG.ICollection<TKey>.Remove(TKey key) => throw new global::System.NotSupportedException();
                    public Enumerator GetEnumerator() => new Enumerator(owner.GetEnumerator());
                    SCG.IEnumerator<TKey> SCG.IEnumerable<TKey>.GetEnumerator() => GetEnumerator();
                    SC.IEnumerator SC.IEnumerable.GetEnumerator() => GetEnumerator();
                    public struct Enumerator : SCG.IEnumerator<TKey>
                    {
                        private S1InteropDictionary<TKey, TValue>.Enumerator inner;
                        internal Enumerator(S1InteropDictionary<TKey, TValue>.Enumerator inner) { this.inner = inner; }
                        public TKey Current => inner.Current.Key;
                        object SC.IEnumerator.Current { get { var ignored = inner.Entry; return Current; } }
                        public bool MoveNext() => inner.MoveNext();
                        public void Reset() => inner.Reset();
                        public void Dispose() => inner.Dispose();
                    }
                }
                public sealed class ValueCollection : SCG.ICollection<TValue>, SCG.IReadOnlyCollection<TValue>, SC.ICollection
                {
                    private readonly S1InteropDictionary<TKey, TValue> owner;
                    public ValueCollection(S1InteropDictionary<TKey, TValue> dictionary) { owner = dictionary ?? throw new global::System.ArgumentNullException(nameof(dictionary)); }
                    public int Count => owner.Count;
                    bool SCG.ICollection<TValue>.IsReadOnly => true;
                    bool SC.ICollection.IsSynchronized => false;
                    object SC.ICollection.SyncRoot => owner.syncRoot;
                    public bool Contains(TValue value) => owner.ContainsValue(value);
                    public void CopyTo(TValue[] array, int index) => Copy(this, Count, array, index);
                    void SC.ICollection.CopyTo(global::System.Array array, int index) => CopyObjects(this, Count, array, index);
                    void SCG.ICollection<TValue>.Add(TValue value) => throw new global::System.NotSupportedException();
                    void SCG.ICollection<TValue>.Clear() => throw new global::System.NotSupportedException();
                    bool SCG.ICollection<TValue>.Remove(TValue value) => throw new global::System.NotSupportedException();
                    public Enumerator GetEnumerator() => new Enumerator(owner.GetEnumerator());
                    SCG.IEnumerator<TValue> SCG.IEnumerable<TValue>.GetEnumerator() => GetEnumerator();
                    SC.IEnumerator SC.IEnumerable.GetEnumerator() => GetEnumerator();
                    public struct Enumerator : SCG.IEnumerator<TValue>
                    {
                        private S1InteropDictionary<TKey, TValue>.Enumerator inner;
                        internal Enumerator(S1InteropDictionary<TKey, TValue>.Enumerator inner) { this.inner = inner; }
                        public TValue Current => inner.Current.Value;
                        object SC.IEnumerator.Current { get { var ignored = inner.Entry; return Current; } }
                        public bool MoveNext() => inner.MoveNext();
                        public void Reset() => inner.Reset();
                        public void Dispose() => inner.Dispose();
                    }
                }
            }
        }
        """;

    public static SyntaxTree CreateTree(CSharpParseOptions options, MetadataSymbolMap map, CancellationToken token)
    {
        string stringComparers = map.FindTargetType("Il2CppSystem.StringComparer") is null ? "" : """
            if (typeof(T) == typeof(string))
            {
                if (global::System.Object.ReferenceEquals(comparer, global::System.StringComparer.Ordinal))
                    return global::Il2CppSystem.StringComparer.Ordinal.Cast<NC.IEqualityComparer<T>>();
                if (global::System.Object.ReferenceEquals(comparer, global::System.StringComparer.OrdinalIgnoreCase))
                    return global::Il2CppSystem.StringComparer.OrdinalIgnoreCase.Cast<NC.IEqualityComparer<T>>();
            }
            """;
        return CSharpSyntaxTree.ParseText("// <auto-generated/>\n#nullable disable\n" + Body.Replace("__STRING_COMPARERS__", stringComparers), options,
            "S1Interop.Compiler/S1InteropDictionary.g.cs", System.Text.Encoding.UTF8, token);
    }
}
