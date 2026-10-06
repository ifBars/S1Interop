using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace S1Interop.Compiler;

/// <summary>
/// Emits the live <c>List&lt;T&gt;</c> bridge used for native objects and scalar lists connected to native storage. The bridge
/// never copies: every operation reads or writes the wrapped IL2CPP list. Written in C# 7.3 so it compiles under
/// any language version a mod targets.
/// </summary>
internal static class NativeListSource
{
    public const string TypeName = "global::S1Interop.Compiler.Generated.S1InteropList";
    public const string FilePath = "S1Interop.Compiler/S1InteropList.g.cs";

    private const string Body = """
        namespace S1Interop.Compiler.Generated
        {
            using SCG = global::System.Collections.Generic;
            using NL = global::Il2CppSystem.Collections.Generic;

            public sealed class S1InteropList<T> : SCG.IList<T>, SCG.IReadOnlyList<T>, global::System.Collections.IList, IS1InteropNativeView
            {
                private const int SweepInterval = 64;
                private static readonly SCG.Dictionary<global::System.IntPtr, global::System.WeakReference<S1InteropList<T>>> Cache =
                    new SCG.Dictionary<global::System.IntPtr, global::System.WeakReference<S1InteropList<T>>>();
                private static int insertsSinceSweep;

                // Strong field: keeps the native wrapper (and its GC handle) alive for as long as the bridge is reachable,
                // which also keeps the native pointer from being reused while it is a cache key.
                private readonly NL.List<T> native;
                private readonly object syncRoot = new object();
                global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase IS1InteropNativeView.NativeObject => native;

                public S1InteropList() : this(new NL.List<T>(), true) { }

                public S1InteropList(int capacity)
                {
                    if (capacity < 0) throw new global::System.ArgumentOutOfRangeException(nameof(capacity));
                    native = new NL.List<T>(capacity);
                    Register(this);
                }

                public S1InteropList(SCG.IEnumerable<T> collection)
                {
                    if (collection is null) throw new global::System.ArgumentNullException(nameof(collection));
                    native = new NL.List<T>();
                    Register(this);
                    AddRange(collection);
                }

                private S1InteropList(NL.List<T> native, bool register)
                {
                    this.native = native;
                    if (register) Register(this);
                }

                /// <summary>Returns the stable bridge for <paramref name="native"/>, or <c>null</c> when it is <c>null</c>.</summary>
                /// <param name="native">The IL2CPP list to bridge without copying.</param>
                /// <returns>The same bridge instance for every call with the same live native list.</returns>
                public static S1InteropList<T> FromNative(NL.List<T> native)
                {
                    if (native is null) return null;
                    global::System.IntPtr pointer = native.Pointer;
                    lock (Cache)
                    {
                        global::System.WeakReference<S1InteropList<T>> weak;
                        S1InteropList<T> existing;
                        if (Cache.TryGetValue(pointer, out weak) && weak.TryGetTarget(out existing)) return existing;
                        var created = new S1InteropList<T>(native, false);
                        AddLocked(pointer, created);
                        return created;
                    }
                }

                /// <summary>Returns the IL2CPP list behind <paramref name="list"/>, or <c>null</c> when it is <c>null</c>.</summary>
                /// <param name="list">The bridge to unwrap.</param>
                /// <returns>The live native list; mutations through either side are shared.</returns>
                public static NL.List<T> ToNative(S1InteropList<T> list) => list is null ? null : list.native;

                public static implicit operator S1InteropList<T>(NL.List<T> native) => FromNative(native);

                public static implicit operator NL.List<T>(S1InteropList<T> list) => ToNative(list);

                public static S1InteropList<T> TryCast(object value)
                {
                    if (value is S1InteropList<T> list) return list;
                    var nativeValue = value as global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase ?? (value as IS1InteropNativeView)?.NativeObject;
                    return nativeValue is null ? null : FromNative(nativeValue.TryCast<NL.List<T>>());
                }

                public static S1InteropList<T> Cast(object value)
                {
                    if (value is null) return null;
                    return TryCast(value) ?? throw new global::System.InvalidCastException();
                }

                public static bool Is(object value) => !(TryCast(value) is null);

                public global::System.Collections.ObjectModel.ReadOnlyCollection<T> AsReadOnly() =>
                    new global::System.Collections.ObjectModel.ReadOnlyCollection<T>(this);

                private static void Register(S1InteropList<T> list)
                {
                    lock (Cache) AddLocked(list.native.Pointer, list);
                }

                private static void AddLocked(global::System.IntPtr pointer, S1InteropList<T> list)
                {
                    Cache[pointer] = new global::System.WeakReference<S1InteropList<T>>(list);
                    if (++insertsSinceSweep < SweepInterval) return;
                    insertsSinceSweep = 0;
                    var dead = new SCG.List<global::System.IntPtr>();
                    foreach (var pair in Cache)
                    {
                        S1InteropList<T> ignored;
                        if (!pair.Value.TryGetTarget(out ignored)) dead.Add(pair.Key);
                    }

                    foreach (var key in dead) Cache.Remove(key);
                }

                private int Version => native._version;

                public int Count => native.Count;

                public int Capacity
                {
                    get { return native.Capacity; }
                    set
                    {
                        if (value < Count) throw new global::System.ArgumentOutOfRangeException(nameof(value));
                        native.Capacity = value;
                    }
                }

                public T this[int index]
                {
                    get
                    {
                        if ((uint)index >= (uint)native.Count) throw new global::System.ArgumentOutOfRangeException(nameof(index));
                        return native[index];
                    }
                    set
                    {
                        if ((uint)index >= (uint)native.Count) throw new global::System.ArgumentOutOfRangeException(nameof(index));
                        native[index] = value;
                    }
                }

                bool SCG.ICollection<T>.IsReadOnly => false;
                bool global::System.Collections.IList.IsReadOnly => false;
                bool global::System.Collections.IList.IsFixedSize => false;
                bool global::System.Collections.ICollection.IsSynchronized => false;
                object global::System.Collections.ICollection.SyncRoot => syncRoot;

                object global::System.Collections.IList.this[int index]
                {
                    get { return this[index]; }
                    set { this[index] = CastItem(value, nameof(value)); }
                }

                public void Add(T item) => native.Add(item);

                public void Clear() => native.Clear();

                public bool Contains(T item) => native.Contains(item);

                public int IndexOf(T item) => native.IndexOf(item);

                public int IndexOf(T item, int index) => IndexOf(item, index, Count - index);

                public int IndexOf(T item, int index, int count)
                {
                    CheckRange(index, count);
                    if (count == 0) return -1;
                    // Searching a native slice keeps native equality semantics for the element type.
                    int found = native.GetRange(index, count).IndexOf(item);
                    return found < 0 ? -1 : found + index;
                }

                public int LastIndexOf(T item) => Count == 0 ? -1 : LastIndexOf(item, Count - 1, Count);

                public int LastIndexOf(T item, int index) => LastIndexOf(item, index, index + 1);

                public int LastIndexOf(T item, int index, int count)
                {
                    int size = Count;
                    if (size == 0) return -1;
                    if ((uint)index >= (uint)size) throw new global::System.ArgumentOutOfRangeException(nameof(index));
                    if (count < 0 || index - count + 1 < 0) throw new global::System.ArgumentOutOfRangeException(nameof(count));
                    int start = index - count + 1;
                    var slice = native.GetRange(start, count);
                    slice.Reverse();
                    int found = slice.IndexOf(item);
                    return found < 0 ? -1 : index - found;
                }

                public void Insert(int index, T item)
                {
                    if ((uint)index > (uint)native.Count) throw new global::System.ArgumentOutOfRangeException(nameof(index));
                    native.Insert(index, item);
                }

                public bool Remove(T item)
                {
                    int index = native.IndexOf(item);
                    if (index < 0) return false;
                    native.RemoveAt(index);
                    return true;
                }

                public void RemoveAt(int index)
                {
                    if ((uint)index >= (uint)native.Count) throw new global::System.ArgumentOutOfRangeException(nameof(index));
                    native.RemoveAt(index);
                }

                public void RemoveRange(int index, int count)
                {
                    CheckRange(index, count);
                    if (count > 0) native.RemoveRange(index, count);
                }

                public S1InteropList<T> GetRange(int index, int count)
                {
                    CheckRange(index, count);
                    return FromNative(native.GetRange(index, count));
                }

                public void Reverse() => native.Reverse();

                public void Reverse(int index, int count)
                {
                    CheckRange(index, count);
                    native.Reverse(index, count);
                }

                public void TrimExcess() => native.TrimExcess();

                public void AddRange(SCG.IEnumerable<T> collection) => InsertRange(Count, collection);

                public void InsertRange(int index, SCG.IEnumerable<T> collection)
                {
                    if (collection is null) throw new global::System.ArgumentNullException(nameof(collection));
                    if ((uint)index > (uint)native.Count) throw new global::System.ArgumentOutOfRangeException(nameof(index));
                    var known = collection as SCG.ICollection<T>;
                    if (known != null)
                    {
                        // Snapshot first like the BCL, which makes self-insertion well defined.
                        var snapshot = new T[known.Count];
                        known.CopyTo(snapshot, 0);
                        for (int i = 0; i < snapshot.Length; i++) native.Insert(index + i, snapshot[i]);
                        return;
                    }

                    // Lazy sources are pulled one item at a time so iteration and exception timing match the BCL;
                    // enumerating this list while inserting into it throws through the version check.
                    using (var enumerator = collection.GetEnumerator())
                    {
                        while (enumerator.MoveNext()) native.Insert(index++, enumerator.Current);
                    }
                }

                public T[] ToArray()
                {
                    var array = new T[Count];
                    for (int i = 0; i < array.Length; i++) array[i] = native[i];
                    return array;
                }

                public void CopyTo(T[] array) => CopyTo(0, array, 0, Count);

                public void CopyTo(T[] array, int arrayIndex)
                {
                    if (array is null) throw new global::System.ArgumentNullException(nameof(array));
                    CopyTo(0, array, arrayIndex, Count);
                }

                public void CopyTo(int index, T[] array, int arrayIndex, int count)
                {
                    if (array is null) throw new global::System.ArgumentNullException(nameof(array));
                    CheckRange(index, count);
                    if (arrayIndex < 0) throw new global::System.ArgumentOutOfRangeException(nameof(arrayIndex));
                    if (array.Length - arrayIndex < count) throw new global::System.ArgumentException("Destination array is not long enough.");
                    for (int i = 0; i < count; i++) array[arrayIndex + i] = native[index + i];
                }

                void global::System.Collections.ICollection.CopyTo(global::System.Array array, int arrayIndex)
                {
                    if (array is null) throw new global::System.ArgumentNullException(nameof(array));
                    if (array.Rank != 1) throw new global::System.ArgumentException("Multi-dimensional arrays are not supported.");
                    if (array.GetLowerBound(0) != 0) throw new global::System.ArgumentException("Non-zero array lower bounds are not supported.");
                    int count = Count;
                    if (arrayIndex < 0) throw new global::System.ArgumentOutOfRangeException(nameof(arrayIndex));
                    if (array.Length - arrayIndex < count) throw new global::System.ArgumentException("Destination array is not long enough.");
                    try
                    {
                        for (int i = 0; i < count; i++) array.SetValue(native[i], arrayIndex + i);
                    }
                    catch (global::System.InvalidCastException)
                    {
                        throw new global::System.ArgumentException("Target array type is not compatible with the list element type.");
                    }
                }

                public bool Exists(global::System.Predicate<T> match) => FindIndex(0, Count, match) != -1;

                public bool TrueForAll(global::System.Predicate<T> match)
                {
                    if (match is null) throw new global::System.ArgumentNullException(nameof(match));
                    for (int i = 0; i < native.Count; i++)
                    {
                        if (!match(native[i])) return false;
                    }

                    return true;
                }

                public T Find(global::System.Predicate<T> match)
                {
                    int index = FindIndex(0, Count, match);
                    return index < 0 ? default(T) : native[index];
                }

                public T FindLast(global::System.Predicate<T> match)
                {
                    int index = FindLastIndex(match);
                    return index < 0 ? default(T) : native[index];
                }

                public S1InteropList<T> FindAll(global::System.Predicate<T> match)
                {
                    if (match is null) throw new global::System.ArgumentNullException(nameof(match));
                    var result = new S1InteropList<T>();
                    for (int i = 0; i < native.Count; i++)
                    {
                        T item = native[i];
                        if (match(item)) result.native.Add(item);
                    }

                    return result;
                }

                public int FindIndex(global::System.Predicate<T> match) => FindIndex(0, Count, match);

                public int FindIndex(int startIndex, global::System.Predicate<T> match) => FindIndex(startIndex, Count - startIndex, match);

                public int FindIndex(int startIndex, int count, global::System.Predicate<T> match)
                {
                    if ((uint)startIndex > (uint)Count) throw new global::System.ArgumentOutOfRangeException(nameof(startIndex));
                    if (count < 0 || startIndex > Count - count) throw new global::System.ArgumentOutOfRangeException(nameof(count));
                    if (match is null) throw new global::System.ArgumentNullException(nameof(match));
                    int end = startIndex + count;
                    for (int i = startIndex; i < end; i++)
                    {
                        if (match(native[i])) return i;
                    }

                    return -1;
                }

                public int FindLastIndex(global::System.Predicate<T> match) => FindLastIndex(Count - 1, Count, match);

                public int FindLastIndex(int startIndex, global::System.Predicate<T> match) => FindLastIndex(startIndex, startIndex + 1, match);

                public int FindLastIndex(int startIndex, int count, global::System.Predicate<T> match)
                {
                    if (match is null) throw new global::System.ArgumentNullException(nameof(match));
                    int size = Count;
                    if (size == 0)
                    {
                        if (startIndex != -1) throw new global::System.ArgumentOutOfRangeException(nameof(startIndex));
                    }
                    else if ((uint)startIndex >= (uint)size)
                    {
                        throw new global::System.ArgumentOutOfRangeException(nameof(startIndex));
                    }

                    if (count < 0 || startIndex - count + 1 < 0) throw new global::System.ArgumentOutOfRangeException(nameof(count));
                    int end = startIndex - count;
                    for (int i = startIndex; i > end; i--)
                    {
                        if (match(native[i])) return i;
                    }

                    return -1;
                }

                public int RemoveAll(global::System.Predicate<T> match)
                {
                    if (match is null) throw new global::System.ArgumentNullException(nameof(match));
                    int size = native.Count;
                    int free = 0;
                    while (free < size && !match(native[free])) free++;
                    if (free >= size) return 0;
                    int current = free + 1;
                    while (current < size)
                    {
                        while (current < size && match(native[current])) current++;
                        if (current < size) native[free++] = native[current++];
                    }

                    int removed = size - free;
                    native.RemoveRange(free, removed);
                    return removed;
                }

                public void ForEach(global::System.Action<T> action)
                {
                    if (action is null) throw new global::System.ArgumentNullException(nameof(action));
                    int version = Version;
                    for (int i = 0; i < native.Count; i++)
                    {
                        if (version != Version) break;
                        action(native[i]);
                    }

                    if (version != Version) throw VersionChanged();
                }

                public SCG.List<TOutput> ConvertAll<TOutput>(global::System.Converter<T, TOutput> converter)
                {
                    if (converter is null) throw new global::System.ArgumentNullException(nameof(converter));
                    int size = Count;
                    var result = new SCG.List<TOutput>(size);
                    for (int i = 0; i < size; i++) result.Add(converter(native[i]));
                    return result;
                }

                public void Sort() => Sort(0, Count, null);

                public void Sort(SCG.IComparer<T> comparer) => Sort(0, Count, comparer);

                public void Sort(int index, int count, SCG.IComparer<T> comparer)
                {
                    CheckRange(index, count);
                    if (comparer is null) { native.Sort(index, count, null); return; }
                    var buffer = new T[count];
                    CopyTo(index, buffer, 0, count);
                    global::System.Array.Sort(buffer, comparer);
                    WriteBack(index, buffer);
                }

                public void Sort(global::System.Comparison<T> comparison)
                {
                    if (comparison is null) throw new global::System.ArgumentNullException(nameof(comparison));
                    int count = Count;
                    var buffer = ToArray();
                    global::System.Array.Sort(buffer, comparison);
                    WriteBack(0, buffer);
                }

                public int BinarySearch(T item) => BinarySearch(0, Count, item, null);

                public int BinarySearch(T item, SCG.IComparer<T> comparer) => BinarySearch(0, Count, item, comparer);

                public int BinarySearch(int index, int count, T item, SCG.IComparer<T> comparer)
                {
                    CheckRange(index, count);
                    if (comparer is null) return native.BinarySearch(index, count, item, null);
                    int low = index;
                    int high = index + count - 1;
                    while (low <= high)
                    {
                        int middle = low + ((high - low) >> 1);
                        int order = comparer.Compare(native[middle], item);
                        if (order == 0) return middle;
                        if (order < 0) low = middle + 1;
                        else high = middle - 1;
                    }

                    return ~low;
                }

                public Enumerator GetEnumerator() => new Enumerator(this);

                SCG.IEnumerator<T> SCG.IEnumerable<T>.GetEnumerator() => new Enumerator(this);

                global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => new Enumerator(this);

                int global::System.Collections.IList.Add(object value)
                {
                    native.Add(CastItem(value, nameof(value)));
                    return native.Count - 1;
                }

                bool global::System.Collections.IList.Contains(object value) => S1InteropNativeCast.TryValue<T>(value, out var item) && Contains(item);

                int global::System.Collections.IList.IndexOf(object value) => S1InteropNativeCast.TryValue<T>(value, out var item) ? IndexOf(item) : -1;

                void global::System.Collections.IList.Insert(int index, object value) => Insert(index, CastItem(value, nameof(value)));

                void global::System.Collections.IList.Remove(object value)
                {
                    if (S1InteropNativeCast.TryValue<T>(value, out var item)) Remove(item);
                }

                private void WriteBack(int index, T[] buffer)
                {
                    int version = native._version;
                    for (int i = 0; i < buffer.Length; i++) native[index + i] = buffer[i];
                    native._version = unchecked(version + 1);
                }

                private void CheckRange(int index, int count)
                {
                    if (index < 0) throw new global::System.ArgumentOutOfRangeException(nameof(index));
                    if (count < 0) throw new global::System.ArgumentOutOfRangeException(nameof(count));
                    if (Count - index < count) throw new global::System.ArgumentException("Offset and length were out of bounds for the list.");
                }

                private static T CastItem(object value, string name)
                {
                    if (value is null && !(default(T) is null)) throw new global::System.ArgumentNullException(name);
                    if (!S1InteropNativeCast.TryValue<T>(value, out var item)) throw new global::System.ArgumentException("Value is not of the list element type.", name);
                    return item;
                }

                private static global::System.InvalidOperationException VersionChanged() =>
                    new global::System.InvalidOperationException("Collection was modified; enumeration operation may not execute.");

                public struct Enumerator : SCG.IEnumerator<T>
                {
                    private readonly S1InteropList<T> list;
                    private readonly int version;
                    private int index;
                    private T current;

                    internal Enumerator(S1InteropList<T> list)
                    {
                        this.list = list;
                        version = list.Version;
                        index = 0;
                        current = default(T);
                    }

                    public T Current => current;

                    object global::System.Collections.IEnumerator.Current
                    {
                        get
                        {
                            if (index == 0 || index == list.Count + 1) throw new global::System.InvalidOperationException("Enumeration has either not started or has already finished.");
                            return current;
                        }
                    }

                    public bool MoveNext()
                    {
                        // Version is read live from the native list so mutations made by game code also invalidate us.
                        if (version != list.Version) throw VersionChanged();
                        int count = list.Count;
                        if ((uint)index < (uint)count)
                        {
                            current = list.native[index];
                            index++;
                            return true;
                        }

                        index = count + 1;
                        current = default(T);
                        return false;
                    }

                    public void Reset()
                    {
                        if (version != list.Version) throw VersionChanged();
                        index = 0;
                        current = default(T);
                    }

                    public void Dispose() { }
                }
            }
        }
        """;

    public static SyntaxTree CreateTree(CSharpParseOptions options, CancellationToken token)
    {
        string nullable = options.LanguageVersion >= LanguageVersion.CSharp8 ? "#nullable disable\n" : string.Empty;
        return CSharpSyntaxTree.ParseText(
            "// <auto-generated/>\n" + nullable + Body,
            options,
            FilePath,
            System.Text.Encoding.UTF8,
            token);
    }
}
