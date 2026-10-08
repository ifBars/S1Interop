namespace S1Interop.Compiler.Tests;

// These deliberately small, executable contracts are authored test doubles, not game code.
internal static class RuntimeContracts
{
    public const string Mono = """
        namespace ScheduleOne.Testing {
            public enum WideKind : ulong { High = ulong.MaxValue }
            public static class ArrayStore {
                public static int[] Values = new[] { 1 };
                public static WideKind[] WideKinds = new[] { WideKind.High };
                public static byte[] Bytes = new byte[] { 1 };
                public static bool[] Flags = new[] { true };
                public static Actor.Kind[] Kinds = new[] { Actor.Kind.Customer };
                public static int[] Read(int[] unused) => Values;
                public static void Replace(int[] values) { Values = values; }
                public static void Replace(string unrelated) { }
                public static Actor[] Actors = new Actor[] { Actor.Current };
                public static Employee[] Employees = new Employee[] { new Employee() };
                public static Actor[] ReadActors() => Actors;
                public static T[] ReadGeneric<T>() where T : Actor => (T[])(object)Actors;
                public static void ReplaceActors(Actor[] values) { Actors = values; }
            }
        }
        namespace ScheduleOne.Testing {
            public static class ScalarStore {
                public static System.Collections.Generic.List<int> Numbers = new() { 3, 1, 2 };
                public static System.Collections.Generic.List<string> Words = new() { "first" };
                public static void Replace(System.Collections.Generic.List<int> values) { Numbers = values; }
            }
            public static class RoutineHost {
                private static System.Collections.IEnumerator current;
                public static bool Running => current != null;
                public static System.Collections.IEnumerator Active { get => current; set => current = value; }
                public static System.Collections.IEnumerator Read() => current;
                public static int Disposals;
                public static System.Collections.IEnumerator CreatePlain() => new PlainRoutine();
                private sealed class PlainRoutine : System.Collections.IEnumerator {
                    private int step;
                    public object Current => null;
                    public bool MoveNext() => ++step == 1;
                    public void Reset() { step = 0; }
                }
                public static System.Collections.IEnumerator Create() {
                    try { yield return Actor.Current; yield return null; } finally { Disposals++; }
                }
                public static void Start(System.Collections.IEnumerator routine) { current = routine; routine?.MoveNext(); }
                public static void Stop(System.Collections.IEnumerator routine) { if (object.ReferenceEquals(current, routine)) current = null; }
            }
        }
        namespace ScheduleOne.Testing
        {
            public interface IActor { int ReadValue(); }
            public class Actor : IActor
            {
                public System.Collections.IEnumerator Routine() => RoutineHost.Create();
                public int ReadValue() => 5;
                public string Name = "actor";
                public Employee ReflectionChild;
                public static int ReflectionCount;
                public static System.Collections.Generic.Dictionary<string, int> ReflectionScores = new() { ["one"] = 1 };
                public static int reflectionScores;
                public static System.Collections.Generic.List<int> ReflectionNumbers = new() { 1 };
                private static int ReflectionPrivate;
                protected static int ReflectionProtected;
                internal static int ReflectionInternal;
                public static event System.Action ReflectionEvent;
                public event System.Action<int> ReflectionInstanceEvent;
                public static event System.Action ReflectionWithoutStorage { add { } remove { } }
                public static int Calls;
                public static Actor Current = new Employee();
                public static Actor Next() { Calls++; return Current; }
                public static System.Collections.Generic.List<Actor> All = new() { Current };
                public static System.Collections.Generic.List<Actor> GetAll() => All;
                public static object OpaqueAll() => All;
                public static void SetAll(System.Collections.Generic.List<Actor> value) { All = value; }
                public static void AddActor() { All.Add(new Actor()); }
                public virtual int Score(int value) => value + 1;
                public int Score(string value) => value.Length;
                public T Echo<T>(T value) => value;
                public System.Action Changed;
                public static string RenamedOnlyInMono() => "unavailable";
                public class Settings { public int Value; }
                public enum Kind { Customer, Employee }
            }
            public class Employee : Actor { public int Salary = 7; }
            public class PropertyShadowActor : Actor { public new Employee ReflectionChild => null; }
            public class FieldShadowActor : Actor { public new Employee ReflectionChild = new Employee { Salary = 21 }; }
            public class Customer : Actor { }
            public class KeyActor : Actor
            {
                public int Key;
                public override bool Equals(object value) => value is KeyActor other && other.Key == Key;
                public override int GetHashCode() => Key;
            }
        }
        namespace ScheduleOne.Testing
        {
            public static class DictionaryStore
            {
                public static System.Collections.Generic.Dictionary<string, int> Scores = new() { { "alpha", 1 }, { "beta", 2 } };
                public static System.Collections.Generic.Dictionary<Actor, int> Ranks = new() { { Actor.Current, 7 } };
                public static System.Collections.Generic.Dictionary<string, int> ReadScores() => Scores;
                public static void ReplaceScores(System.Collections.Generic.Dictionary<string, int> values) { Scores = values; }
                public static System.Collections.Generic.Dictionary<Actor, int> ReadRanks() => Ranks;
                public static void ReplaceRanks(System.Collections.Generic.Dictionary<Actor, int> values) { Ranks = values; }
                public static void AddScore(string key, int value) { Scores.Add(key, value); }
                public static void AddRank(Actor key, int value) { Ranks.Add(key, value); }
            }
        }
        namespace Unrelated.Library
        {
            public class Tool { public static int Twice(int value) => value * 2; }
        }
        namespace UnityEngine
        {
            [System.AttributeUsage(System.AttributeTargets.Field)]
            public sealed class SerializeField : System.Attribute { }
            public class Object { }
            public class AudioClip
            {
                public delegate void PCMReaderCallback(float[] data);
                public delegate float[] SampleProvider();
                public static float[] Provide(SampleProvider callback) => callback();
                public static void Read(PCMReaderCallback callback, float[] data) => callback(data);
            }
            public class ObjectSequence : Object, System.Collections.Generic.IEnumerable<Object>
            {
                private readonly System.Collections.Generic.List<Object> values = new();
                public void Add(Object value) => values.Add(value);
                public System.Collections.Generic.IEnumerator<Object> GetEnumerator() => values.GetEnumerator();
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }
            public class Component : Object { }
            public class MonoBehaviour : Component { }
            public class GameObject : Object
            {
                public T AddComponent<T>() where T : Component, new() => new T();
            }
        }
        namespace UnityEngine.Events
        {
            public delegate void UnityAction();
            public class UnityEvent
            {
                private readonly System.Collections.Generic.List<UnityAction> listeners = new();
                public void AddListener(UnityAction call) => listeners.Add(call);
                // Managed removal uses delegate equality, so a recreated method group matches.
                public void RemoveListener(UnityAction call) => listeners.Remove(call);
                public void Invoke() { foreach (var listener in listeners.ToArray()) listener(); }
            }
        }
        """;

    public const string Il2Cpp = """
        namespace Il2CppInterop.Runtime.InteropTypes.Arrays {
            public abstract class Il2CppArrayBase<T> : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase, System.Collections.Generic.IEnumerable<T> {
                public abstract int Length { get; }
                public abstract T this[int index] { get; set; }
                public abstract System.Collections.Generic.IEnumerator<T> GetEnumerator();
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
                public static implicit operator T[](Il2CppArrayBase<T> value) {
                    if (value == null) return null;
                    var copy = new T[value.Length]; for (int i = 0; i < copy.Length; i++) copy[i] = value[i]; return copy;
                }
            }
            internal sealed class ReferenceStorage {
                public readonly System.Array Values;
                public ReferenceStorage(System.Type element, int length) { Values = System.Array.CreateInstance(element, length); }
                public ReferenceStorage(System.Array values) { Values = values; }
            }
            public class Il2CppReferenceArray<T> : Il2CppArrayBase<T>
                where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase {
                private ReferenceStorage Storage => (ReferenceStorage)Native;
                public Il2CppReferenceArray(long length) { Native = new ReferenceStorage(typeof(T), checked((int)length)); }
                public Il2CppReferenceArray(System.IntPtr pointer) { Native = Il2CppInterop.Runtime.FakeNativeHeap.Resolve(pointer); }
                public Il2CppReferenceArray(T[] values) : this((long)values.Length) { for (int i = 0; i < values.Length; i++) this[i] = values[i]; }
                public override int Length => Storage.Values.Length;
                public override T this[int index] {
                    get {
                        object value = Storage.Values.GetValue(index);
                        if (typeof(T) == typeof(Il2CppScheduleOne.Testing.Actor) && value is Il2CppScheduleOne.Testing.Actor)
                            return (T)(object)new Il2CppScheduleOne.Testing.Actor { Native = value };
                        return (T)value;
                    }
                    set => Storage.Values.SetValue(value?.Native, index);
                }
                public override System.Collections.Generic.IEnumerator<T> GetEnumerator() { for (int i = 0; i < Length; i++) yield return this[i]; }
                public static implicit operator Il2CppReferenceArray<T>(T[] value) => value == null ? null : new(value);
                public static implicit operator T[](Il2CppReferenceArray<T> value) {
                    if (value == null) return null;
                    var copy = new T[value.Length]; for (int i = 0; i < copy.Length; i++) copy[i] = value[i]; return copy;
                }
            }
            public class Il2CppStructArray<T> : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase, System.Collections.Generic.IEnumerable<T> where T : unmanaged {
                private readonly T[] values;
                public Il2CppStructArray(T[] values) { this.values = (T[])values.Clone(); }
                public Il2CppStructArray(long length) { values = new T[checked((int)length)]; }
                public Il2CppStructArray(Il2CppStructArray<T> other) { values = other.values; Native = other.Native; }
                public int Length => values.Length;
                public T this[int index] { get => values[index]; set => values[index] = value; }
                public System.Span<T> AsSpan() => values;
                public System.Collections.Generic.IEnumerator<T> GetEnumerator() => ((System.Collections.Generic.IEnumerable<T>)values).GetEnumerator();
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
                public static implicit operator Il2CppStructArray<T>(T[] value) => value == null ? null : new(value);
                public static implicit operator T[](Il2CppStructArray<T> value) => value == null ? null : (T[])value.values.Clone();
            }
        }
        namespace Il2CppScheduleOne.Testing {
            public enum WideKind : ulong { High = ulong.MaxValue }
            public static class ArrayStore {
                public static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> Values = new int[] { 1 };
                public static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<WideKind> WideKinds = new WideKind[] { WideKind.High };
                public static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> Bytes = new byte[] { 1 };
                public static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<bool> Flags = new bool[] { true };
                public static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Actor.Kind> Kinds = new Actor.Kind[] { Actor.Kind.Customer };
                public static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> Read(
                    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> unused) => Values == null ? null : new(Values);
                public static void Replace(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> values) { Values = values; }
                public static void Replace(string unrelated) { }
                public static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Actor> Actors = new Actor[] { Actor.Current };
                public static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Employee> Employees = new Employee[] { new Employee() };
                public static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Actor> ReadActors() => new(Actors.Pointer);
                public static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<T> ReadGeneric<T>() where T : Actor =>
                    new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<T>(Actors.Pointer);
                public static void ReplaceActors(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Actor> values) { Actors = values; }
            }
        }
        namespace Il2CppSystem.Collections {
            public class IEnumerator : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase {
                protected readonly System.Collections.IEnumerator source;
                public IEnumerator(System.Collections.IEnumerator source) { this.source = source; }
                public bool MoveNext() => source.MoveNext();
                public object Current => source.Current;
                public void Reset() => source.Reset();
                public bool IsDisposable => source is System.IDisposable;
                public void Dispose() => (source as System.IDisposable)?.Dispose();
            }
        }
        namespace Il2CppSystem {
            public class Object : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase {
                public Object() { }
                public Object(System.IntPtr pointer) : base(pointer) { Native = Il2CppInterop.Runtime.FakeNativeHeap.Resolve(pointer) ?? this; }
            }
            public class Array : Object {
                public Array(System.IntPtr pointer) : base(pointer) { }
                public int Length => ((Il2CppInterop.Runtime.InteropTypes.Arrays.ReferenceStorage)Native).Values.Length;
                public void SetValue(Object value, int index) {
                    ((Il2CppInterop.Runtime.InteropTypes.Arrays.ReferenceStorage)Native).Values.SetValue(value?.Native, index);
                }
                public Object Clone() {
                    var storage = (Il2CppInterop.Runtime.InteropTypes.Arrays.ReferenceStorage)Native;
                    return new Object { Native = new Il2CppInterop.Runtime.InteropTypes.Arrays.ReferenceStorage((System.Array)storage.Values.Clone()) };
                }
            }
            public class IDisposable : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase {
                public void Dispose() => ((Collections.IEnumerator)Native).Dispose();
            }
        }
        namespace MelonLoader.Support {
            public class MonoEnumeratorWrapper : Il2CppSystem.Collections.IEnumerator {
                public MonoEnumeratorWrapper(System.Collections.IEnumerator source) : base(source) { }
            }
        }
        namespace Il2CppScheduleOne.Testing {
            public static class ScalarStore {
                public static Il2CppSystem.Collections.Generic.List<int> Numbers = new();
                public static Il2CppSystem.Collections.Generic.List<string> Words = new();
                static ScalarStore() { Numbers.Add(3); Numbers.Add(1); Numbers.Add(2); Words.Add("first"); }
                public static void Replace(Il2CppSystem.Collections.Generic.List<int> values) { Numbers = values; }
            }
            public static class RoutineHost {
                private static Il2CppSystem.Collections.IEnumerator current;
                public static bool Running => current != null;
                public static Il2CppSystem.Collections.IEnumerator Active { get => current; set => current = value; }
                public static Il2CppSystem.Collections.IEnumerator Read() => current;
                public static Il2CppSystem.Collections.IEnumerator Create() => new(Values());
                public static int Disposals;
                public static Il2CppSystem.Collections.IEnumerator CreatePlain() => new(new PlainRoutine());
                private sealed class PlainRoutine : System.Collections.IEnumerator {
                    private int step;
                    public object Current => null;
                    public bool MoveNext() => ++step == 1;
                    public void Reset() { step = 0; }
                }
                private static System.Collections.IEnumerator Values() {
                    try { yield return Actor.Current; yield return null; } finally { Disposals++; }
                }
                public static void Start(Il2CppSystem.Collections.IEnumerator routine) { current = routine; routine?.MoveNext(); }
                public static void Stop(Il2CppSystem.Collections.IEnumerator routine) { if (current?.Pointer == routine?.Pointer) current = null; }
            }
        }
        namespace Il2CppInterop.Runtime.InteropTypes
        {
            public class Il2CppObjectBase
            {
                protected internal object Native;
                private uint myGcHandle = 0;
                public Il2CppObjectBase() { Native = this; }
                public Il2CppObjectBase(System.IntPtr pointer) { Native = Il2CppInterop.Runtime.FakeNativeHeap.Resolve(pointer) ?? this; }
                public System.IntPtr Pointer => Il2CppInterop.Runtime.FakeNativeHeap.PointerOf(Native);
                public bool WasCollected => false;
                public T TryCast<T>() where T : Il2CppObjectBase =>
                    Native is Il2CppInterop.Runtime.InteropTypes.Arrays.ReferenceStorage storage && typeof(T).IsGenericType &&
                        typeof(T).GetGenericTypeDefinition() == typeof(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<>)
                        ? (typeof(T).GetGenericArguments()[0].IsAssignableFrom(storage.Values.GetType().GetElementType())
                            ? (T)System.Activator.CreateInstance(typeof(T), new object[] { Pointer }) : null) :
                    typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(Il2CppSystem.Collections.Generic.IEqualityComparer<>) &&
                        Native is not T && Il2CppInterop.Runtime.Injection.ClassInjector.Registered.Contains(Native.GetType())
                        ? WrapInterface<T>() :
                    typeof(T) == typeof(Il2CppSystem.IDisposable) && Native is Il2CppSystem.Collections.IEnumerator iterator && iterator.IsDisposable
                        ? new Il2CppSystem.IDisposable { Native = Native } as T :
                    typeof(T) == typeof(Il2CppScheduleOne.Testing.IActor) && Native is Il2CppScheduleOne.Testing.Actor
                        ? new Il2CppScheduleOne.Testing.IActor { Native = Native } as T : Native as T;
                private T WrapInterface<T>() where T : Il2CppObjectBase {
                    var proxy = (T)System.Activator.CreateInstance(typeof(T));
                    proxy.Native = Native;
                    return proxy;
                }
                public T Cast<T>() where T : Il2CppObjectBase =>
                    TryCast<T>() ?? throw new System.InvalidCastException();
            }
        }
        namespace Il2CppScheduleOne.Testing
        {
            public class IActor : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
            {
                public int ReadValue() => ((Actor)Native).ReadValue();
            }
            public class Actor : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
            {
                public Il2CppSystem.Collections.IEnumerator Routine() => RoutineHost.Create();
                public int ReadValue() => 5;
                public Actor() { }
                public Actor(System.IntPtr pointer) : base(pointer) { }
                public string Name { get; set; } = "actor";
                public Employee ReflectionChild { get; set; }
                public static int ReflectionCount { get; set; }
                public static Il2CppSystem.Collections.Generic.Dictionary<string, int> ReflectionScores { get; set; } = CreateReflectionScores();
                public static int reflectionScores { get; set; }
                private static Il2CppSystem.Collections.Generic.Dictionary<string, int> CreateReflectionScores() { var values = new Il2CppSystem.Collections.Generic.Dictionary<string, int>(); values.Add("one", 1); return values; }
                public static Il2CppSystem.Collections.Generic.List<int> ReflectionNumbers { get; set; } = new();
                public static int ReflectionPrivate { get; set; }
                public static int ReflectionProtected { get; set; }
                public static int ReflectionInternal { get; set; }
                public static System.Action ReflectionEvent { get; set; }
                public System.Action<int> ReflectionInstanceEvent { get; set; }
                public static System.Action ReflectionWithoutStorage { get; set; }
                public static int Calls;
                // CLR type is Actor, but its native identity is Employee: ordinary CLR casts fail.
                public static Actor Current = Wrap(new Employee());
                private static Actor Wrap(Actor value) => new Actor { Native = value };
                public static Actor Next() { Calls++; return Current; }
                public static Il2CppSystem.Collections.Generic.List<Actor> All { get; set; } = new();
                public static Il2CppSystem.Collections.Generic.List<Actor> GetAll() => All;
                public static Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase OpaqueAll() => new Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase { Native = All };
                public static void SetAll(Il2CppSystem.Collections.Generic.List<Actor> value) { All = value; }
                public static void AddActor() { All.Add(new Actor()); }
                static Actor() { All.Add(Current); }
                public virtual int Score(int value) => value + 1;
                public int Score(string value) => value.Length;
                public T Echo<T>(T value) => value;
                public Il2CppSystem.Action Changed { get; set; }
                public class Settings { public int Value { get; set; } }
                public enum Kind { Customer, Employee }
            }
            public class Employee : Actor { public int Salary { get; set; } = 7; }
            public class PropertyShadowActor : Actor {
                [Il2CppInterop.Runtime.OriginalProperty] public new Employee ReflectionChild => null;
            }
            public class FieldShadowActor : Actor { public new Employee ReflectionChild { get; set; } = new Employee { Salary = 21 }; }
            public class Customer : Actor { }
            public class KeyActor : Actor { public int Key { get; set; } }
        }
        namespace Il2CppScheduleOne.Testing
        {
            public static class DictionaryStore
            {
                public static Il2CppSystem.Collections.Generic.Dictionary<string, int> Scores = new();
                public static Il2CppSystem.Collections.Generic.Dictionary<Actor, int> Ranks = new();
                static DictionaryStore() { Scores.Add("alpha", 1); Scores.Add("beta", 2); Ranks.Add(Actor.Current, 7); }
                public static Il2CppSystem.Collections.Generic.Dictionary<string, int> ReadScores() => Scores;
                public static void ReplaceScores(Il2CppSystem.Collections.Generic.Dictionary<string, int> values) { Scores = values; }
                public static Il2CppSystem.Collections.Generic.Dictionary<Actor, int> ReadRanks() => Ranks;
                public static void ReplaceRanks(Il2CppSystem.Collections.Generic.Dictionary<Actor, int> values) { Ranks = values; }
                public static void AddScore(string key, int value) { Scores.Add(key, value); }
                public static void AddRank(Actor key, int value) { Ranks.Add(key, value); }
            }
        }
        namespace Il2CppUnrelated.Library
        {
            public class Tool { public static int Twice(int value) => value * 2; }
        }
        namespace Il2CppSystem
        {
            // Native delegates own their managed callback, as Il2CppToMonoDelegateReference does.
            public class Delegate : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
            {
                protected internal readonly System.Delegate Callback;
                public Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase m_target;
                protected Delegate(System.Delegate callback) { Callback = callback; }
                public static Delegate Combine(Delegate left, Delegate right) {
                    if (left is null) return right;
                    if (right is null) return left;
                    return Recreate(left, System.Delegate.Combine(left.Callback, right.Callback));
                }
                public static Delegate Remove(Delegate left, Delegate right) {
                    if (left is null || right is null) return left;
                    var remaining = System.Delegate.Remove(left.Callback, right.Callback);
                    return remaining is null ? null : object.ReferenceEquals(remaining, left.Callback) ? left : Recreate(left, remaining);
                }
                private static Delegate Recreate(Delegate source, System.Delegate callback) => (Delegate)
                    System.Activator.CreateInstance(source.GetType(), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic, null, new object[] { callback }, null);
                protected void InvokeCallback()
                {
                    try { Callback?.DynamicInvoke(); }
                    catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is not null)
                    {
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                    }
                }
            }
            public class Action : Delegate
            {
                protected Action(System.Delegate callback) : base(callback) { }
                public void Invoke() => InvokeCallback();
                public static implicit operator Action(System.Action callback) => new(callback);
                public static Action operator +(Action left, Action right) =>
                    new(System.Delegate.Combine(left?.Callback, right?.Callback));
                public static Action operator -(Action left, Action right) =>
                    new(System.Delegate.Remove(left?.Callback, right?.Callback));
            }
        }
        namespace UnityEngine.Events
        {
            public class UnityAction : Il2CppSystem.Delegate
            {
                protected UnityAction(System.Delegate callback) : base(callback) { }
                public void Invoke() => InvokeCallback();
            }
            public class UnityEvent : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
            {
                private readonly System.Collections.Generic.List<UnityAction> listeners = new();
                public void AddListener(UnityAction call) => listeners.Add(call);
                // IL2CPP compares native delegate identity, so a freshly converted equal callback is not removed.
                public void RemoveListener(UnityAction call)
                {
                    if (call is null) return;
                    int index = listeners.FindLastIndex(listener => listener.Pointer == call.Pointer);
                    if (index >= 0) listeners.RemoveAt(index);
                }
                public void Invoke() { foreach (var listener in listeners.ToArray()) listener.Invoke(); }
            }
        }
        namespace Il2CppInterop.Runtime
        {
            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class OriginalPropertyAttribute : System.Attribute { }
            public static class Il2CppClassPointerStore {
                public static System.IntPtr GetNativeClassPointer(System.Type type) => FakeNativeHeap.PointerOf(type);
            }
            // Purely managed stand-in for native object identity; holds objects weakly like a native weak GC handle.
            internal static class FakeNativeHeap
            {
                private sealed class Box { public System.IntPtr Value; }
                private static readonly object Gate = new();
                private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Box> Pointers = new();
                private static readonly System.Collections.Generic.Dictionary<System.IntPtr, System.WeakReference> Objects = new();
                private static long next;

                public static System.IntPtr PointerOf(object value)
                {
                    lock (Gate)
                    {
                        if (Pointers.TryGetValue(value, out Box existing)) return existing.Value;
                        var pointer = new System.IntPtr(++next * 16);
                        Pointers.Add(value, new Box { Value = pointer });
                        Objects[pointer] = new System.WeakReference(value);
                        return pointer;
                    }
                }

                public static object Resolve(System.IntPtr pointer)
                {
                    lock (Gate)
                    {
                        if (!Objects.TryGetValue(pointer, out var reference)) return null;
                        object target = reference.Target;
                        if (target is null) Objects.Remove(pointer);
                        return target;
                    }
                }
            }
            public static class IL2CPP
            {
                public static System.IntPtr il2cpp_class_get_field_from_name(System.IntPtr klass, string name) {
                    for (var type = (System.Type)FakeNativeHeap.Resolve(klass); type != null; type = type.BaseType) {
                        var property = type.GetProperty(name, System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance);
                        if (property != null && property.GetCustomAttributes(typeof(OriginalPropertyAttribute), false).Length == 0)
                            return FakeNativeHeap.PointerOf(property);
                    }
                    return System.IntPtr.Zero;
                }
                public static System.IntPtr il2cpp_field_get_parent(System.IntPtr field) =>
                    FakeNativeHeap.PointerOf(((System.Reflection.PropertyInfo)FakeNativeHeap.Resolve(field)).DeclaringType);
                public static uint il2cpp_field_get_flags(System.IntPtr field) =>
                    (uint)(System.Reflection.FieldAttributes.Public | (((System.Reflection.PropertyInfo)FakeNativeHeap.Resolve(field)).GetMethod.IsStatic
                        ? System.Reflection.FieldAttributes.Static : 0));
                public static System.IntPtr il2cpp_object_get_class(System.IntPtr pointer) {
                    object value = FakeNativeHeap.Resolve(pointer);
                    var type = value is InteropTypes.Arrays.ReferenceStorage array ? array.Values.GetType() : value.GetType();
                    return FakeNativeHeap.PointerOf(type);
                }
                public static System.IntPtr il2cpp_class_get_element_class(System.IntPtr klass) =>
                    FakeNativeHeap.PointerOf(((System.Type)FakeNativeHeap.Resolve(klass)).GetElementType());
                public static bool il2cpp_class_is_assignable_from(System.IntPtr klass, System.IntPtr other) =>
                    ((System.Type)FakeNativeHeap.Resolve(klass)).IsAssignableFrom((System.Type)FakeNativeHeap.Resolve(other));
                public static bool il2cpp_class_is_valuetype(System.IntPtr klass) => ((System.Type)FakeNativeHeap.Resolve(klass)).IsValueType;
                public static System.IntPtr il2cpp_array_new(System.IntPtr element, ulong length) =>
                    FakeNativeHeap.PointerOf(new InteropTypes.Arrays.ReferenceStorage((System.Type)FakeNativeHeap.Resolve(element), checked((int)length)));
                private static readonly object Gate = new();
                private static readonly System.Collections.Generic.Dictionary<uint, System.IntPtr> Handles = new();
                private static uint next;
                public static uint il2cpp_gchandle_new_weakref(System.IntPtr obj, bool trackResurrection)
                {
                    lock (Gate) { Handles[++next] = obj; return next; }
                }
                public static System.IntPtr il2cpp_gchandle_get_target(uint gchandle)
                {
                    System.IntPtr pointer;
                    lock (Gate) { if (!Handles.TryGetValue(gchandle, out pointer)) return System.IntPtr.Zero; }
                    return FakeNativeHeap.Resolve(pointer) is null ? System.IntPtr.Zero : pointer;
                }
                public static void il2cpp_gchandle_free(uint gchandle) { lock (Gate) Handles.Remove(gchandle); }
            }
            public static class DelegateSupport
            {
                private sealed class Il2CppToMonoDelegateReference : InteropTypes.Il2CppObjectBase
                {
                    public System.Delegate ReferencedDelegate;
                    public Il2CppToMonoDelegateReference(System.Delegate callback) { ReferencedDelegate = callback; }
                }
                // Like the real runtime, every conversion allocates a new native delegate.
                public static T ConvertDelegate<T>(System.Delegate @delegate) where T : InteropTypes.Il2CppObjectBase {
                    if (@delegate is null) return null;
                    var converted = (T)System.Activator.CreateInstance(typeof(T),
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic, null, new object[] { @delegate }, null);
                    ((Il2CppSystem.Delegate)(object)converted).m_target = new Il2CppToMonoDelegateReference(@delegate);
                    return converted;
                }
            }
        }
        namespace Il2CppInterop.Runtime.Runtime
        {
            public static class ClassInjectorBase
            {
                public static object GetMonoObjectFromIl2CppPointer(System.IntPtr pointer) =>
                    Il2CppInterop.Runtime.FakeNativeHeap.Resolve(pointer);
            }
            public static class Il2CppObjectPool
            {
                public static T Get<T>(System.IntPtr ptr) where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase =>
                    Il2CppInterop.Runtime.FakeNativeHeap.Resolve(ptr) as T;
            }
        }
        namespace Il2CppSystem.Collections.Generic
        {
            // IL2CPP interop exposes the game's comparer interface as a proxy class, not a CLR interface.
            public class IEqualityComparer<T> : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
            {
                public virtual bool Equals(T left, T right) => (bool)Native.GetType().GetMethod("Equals", new[] { typeof(T), typeof(T) }).Invoke(Native, new object[] { left, right });
                public virtual int GetHashCode(T value) => (int)Native.GetType().GetMethod("GetHashCode", new[] { typeof(T) }).Invoke(Native, new object[] { value });
            }
            public class EqualityComparer<T> : IEqualityComparer<T>
            {
                public static EqualityComparer<T> Default { get; } = new();
                private static object Unwrap(object value) => value is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase native ? native.Native : value;
                public override bool Equals(T left, T right) {
                    object a = Unwrap(left), b = Unwrap(right);
                    if (a is Il2CppScheduleOne.Testing.KeyActor key) return b is Il2CppScheduleOne.Testing.KeyActor other && key.Key == other.Key;
                    return object.Equals(a, b);
                }
                public override int GetHashCode(T value) {
                    object native = Unwrap(value);
                    return native is Il2CppScheduleOne.Testing.KeyActor key ? key.Key : native?.GetHashCode() ?? 0;
                }
            }
            public class List<T> : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
            {
                private readonly System.Collections.Generic.List<T> values = new();
                public List() { }
                public List(int capacity) { values.Capacity = capacity; }
                public int _version;
                public int Capacity { get => values.Capacity; set => values.Capacity = value; }
                public int Count => values.Count;
                public T this[int index] { get => values[index]; set { values[index] = value; _version++; } }
                public void Add(T value) { values.Add(value); _version++; }
                public void Insert(int index, T value) { values.Insert(index, value); _version++; }
                public void RemoveAt(int index) { values.RemoveAt(index); _version++; }
                public void RemoveRange(int index, int count) { values.RemoveRange(index, count); if (count > 0) _version++; }
                public bool Remove(T value) { int index = IndexOf(value); if (index < 0) return false; RemoveAt(index); return true; }
                public bool Contains(T value) => IndexOf(value) >= 0;
                public int IndexOf(T value) {
                    for (int i = 0; i < values.Count; i++) {
                        if (EqualityComparer<T>.Default.Equals(values[i], value)) return i;
                    }
                    return -1;
                }
                public List<T> GetRange(int index, int count) { var result = new List<T>(); foreach(var item in values.GetRange(index, count)) result.Add(item); return result; }
                public void Reverse() { values.Reverse(); _version++; }
                public void Reverse(int index, int count) { values.Reverse(index, count); _version++; }
                public void Sort(int index, int count, System.Collections.Generic.IComparer<T> comparer) { values.Sort(index, count, comparer); _version++; }
                public int BinarySearch(int index, int count, T item, System.Collections.Generic.IComparer<T> comparer) => values.BinarySearch(index,count,item,comparer);
                public void TrimExcess() => values.TrimExcess();
                public void Clear() { values.Clear(); _version++; }
                public System.Collections.Generic.List<T>.Enumerator GetEnumerator() => values.GetEnumerator();
            }
            // Follows the Unity-era mscorlib layout the generated view reads: tombstoned entries carry a negative
            // hash code, freed slots are reused, and every mutation (including overwrite, Remove and Clear) bumps _version.
            public sealed class NativeDictionaryException : System.Exception { public NativeDictionaryException(string message) : base(message) { } }
            public class Dictionary<TKey, TValue> : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
            {
                public struct Entry { public int hashCode; public int next; public TKey key; public TValue value; }
                public Entry[] _entries = new Entry[0];
                public int _count;
                public int _version;
                public IEqualityComparer<TKey> _comparer;
                private int freeList = -1;
                private int freeCount;
                public Dictionary() : this(0, null) { }
                public Dictionary(int capacity) : this(capacity, null) { }
                public Dictionary(IEqualityComparer<TKey> comparer) : this(0, comparer) { }
                public Dictionary(int capacity, IEqualityComparer<TKey> comparer)
                {
                    if (capacity < 0) throw new NativeDictionaryException("capacity");
                    _entries = new Entry[capacity];
                    // Like the real collection, a null comparer is replaced by the default, never left null.
                    _comparer = comparer ?? EqualityComparer<TKey>.Default;
                }
                public int Count => _count - freeCount;
                private int Find(TKey key, int hash)
                {
                    for (int i = 0; i < _count; i++)
                        if (_entries[i].hashCode == hash && _comparer.Equals(_entries[i].key, key)) return i;
                    return -1;
                }
                private int Hash(TKey key)
                {
                    if (key is null) throw new NativeDictionaryException("null key");
                    return _comparer.GetHashCode(key) & 0x7FFFFFFF;
                }
                private bool Insert(TKey key, TValue value, bool overwrite, bool throwOnExisting)
                {
                    int hash = Hash(key);
                    int existing = Find(key, hash);
                    if (existing >= 0)
                    {
                        if (throwOnExisting) throw new NativeDictionaryException("duplicate key");
                        if (!overwrite) return false;
                        _entries[existing].value = value;
                        _version++;
                        return true;
                    }
                    int slot;
                    if (freeCount > 0) { slot = freeList; freeList = _entries[slot].next; freeCount--; }
                    else
                    {
                        if (_count == _entries.Length) System.Array.Resize(ref _entries, _count == 0 ? 4 : _count * 2);
                        slot = _count++;
                    }
                    _entries[slot] = new Entry { hashCode = hash, key = key, value = value };
                    _version++;
                    return true;
                }
                public TValue this[TKey key]
                {
                    get { int i = Find(key, Hash(key)); if (i < 0) throw new NativeDictionaryException("missing key"); return _entries[i].value; }
                    set { Insert(key, value, true, false); }
                }
                public void Add(TKey key, TValue value) => Insert(key, value, false, true);
                public bool TryAdd(TKey key, TValue value) => Insert(key, value, false, false);
                public bool ContainsKey(TKey key) => Find(key, Hash(key)) >= 0;
                public bool ContainsValue(TValue value)
                {
                    for (int i = 0; i < _count; i++)
                        if (_entries[i].hashCode >= 0 && EqualityComparer<TValue>.Default.Equals(_entries[i].value, value)) return true;
                    return false;
                }
                public bool TryGetValue(TKey key, out TValue value)
                {
                    int i = Find(key, Hash(key));
                    value = i < 0 ? default : _entries[i].value;
                    return i >= 0;
                }
                public bool Remove(TKey key)
                {
                    int i = Find(key, Hash(key));
                    if (i < 0) return false;
                    _entries[i] = new Entry { hashCode = -1, next = freeList };
                    freeList = i;
                    freeCount++;
                    _version++;
                    return true;
                }
                public void Clear()
                {
                    if (_count > 0) System.Array.Clear(_entries, 0, _count);
                    _count = 0;
                    freeList = -1;
                    freeCount = 0;
                    _version++;
                }
            }
        }
        namespace UnityEngine
        {
            [System.AttributeUsage(System.AttributeTargets.Field)]
            public sealed class SerializeField : System.Attribute { }
            public class Object : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
            {
                public Object() { }
                public Object(System.IntPtr pointer) : base(pointer) { }
            }
            public class AudioClip : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
            {
                public class SampleProvider : Il2CppSystem.Delegate
                {
                    protected SampleProvider(System.Delegate callback) : base(callback) { }
                    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float> Invoke() =>
                        ((System.Func<Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float>>)Callback)();
                }
                public static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float> Provide(SampleProvider callback) => callback.Invoke();
                public class PCMReaderCallback : Il2CppSystem.Delegate
                {
                    protected PCMReaderCallback(System.Delegate callback) : base(callback) { }
                    public void Invoke(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float> data) =>
                        ((System.Action<Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float>>)Callback)(data);
                }
                public static void Read(PCMReaderCallback callback, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float> data) => callback.Invoke(data);
            }
            public class ObjectSequence : Object
            {
                private readonly System.Collections.Generic.List<Object> values = new();
                public void Add(Object value) => values.Add(value);
                public Cursor GetEnumerator() => new Cursor(values);
                public sealed class Cursor
                {
                    private readonly System.Collections.Generic.List<Object> values;
                    private int index = -1;
                    public Cursor(System.Collections.Generic.List<Object> values) { this.values = values; }
                    public Object Current => values[index];
                    public bool MoveNext() => ++index < values.Count;
                    public void Dispose() { }
                }
            }
            public class Component : Object
            {
                public Component() { }
                public Component(System.IntPtr pointer) : base(pointer) { }
            }
            public class MonoBehaviour : Component
            {
                public MonoBehaviour() { }
                public MonoBehaviour(System.IntPtr pointer) : base(pointer) { }
            }
            public class GameObject : Object
            {
                public T AddComponent<T>() where T : Component, new() => new T();
            }
        }
        namespace Il2CppInterop.Runtime.InteropTypes.Fields
        {
            internal static class FieldStorage
            {
                private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Il2CppObjectBase,
                    System.Collections.Generic.Dictionary<string, object>> Values = new();
                internal static T Read<T>(Il2CppObjectBase owner, string name) =>
                    Values.GetOrCreateValue(owner).TryGetValue(name, out var value) ? (T)value : default;
                internal static void Write<T>(Il2CppObjectBase owner, string name, T value) =>
                    Values.GetOrCreateValue(owner)[name] = value;
            }
            public sealed class Il2CppValueField<T> where T : unmanaged
            {
                private readonly Il2CppObjectBase owner;
                private readonly string name;
                internal Il2CppValueField(Il2CppObjectBase owner, string name) { this.owner = owner; this.name = name; }
                public T Value { get => FieldStorage.Read<T>(owner, name); set => FieldStorage.Write(owner, name, value); }
            }
            public sealed class Il2CppStringField
            {
                private readonly Il2CppObjectBase owner;
                private readonly string name;
                internal Il2CppStringField(Il2CppObjectBase owner, string name) { this.owner = owner; this.name = name; }
                public string Value { get => FieldStorage.Read<string>(owner, name); set => FieldStorage.Write(owner, name, value); }
            }
            public sealed class Il2CppReferenceField<T> where T : Il2CppObjectBase
            {
                private readonly Il2CppObjectBase owner;
                private readonly string name;
                internal Il2CppReferenceField(Il2CppObjectBase owner, string name) { this.owner = owner; this.name = name; }
                public T Value { get => FieldStorage.Read<T>(owner, name); set => FieldStorage.Write(owner, name, value); }
            }
        }
        namespace Il2CppInterop.Runtime.Injection
        {
            public class Il2CppInterfaceCollection : System.Collections.Generic.List<System.Type> {
                public Il2CppInterfaceCollection(System.Type[] types) : base(types) { }
                public static implicit operator Il2CppInterfaceCollection(System.Type[] types) => new(types);
            }
            public class RegisterTypeOptions { public Il2CppInterfaceCollection Interfaces { get; set; } }
            public static class ClassInjector
            {
                public static readonly System.Collections.Generic.HashSet<System.Type> Registered = new();
                public static void RegisterTypeInIl2Cpp<T>() => Registered.Add(typeof(T));
                public static void RegisterTypeInIl2Cpp<T>(RegisterTypeOptions options) {
                    if (options.Interfaces == null || options.Interfaces.Count != 1) throw new System.InvalidOperationException("Missing native interface contract.");
                    Registered.Add(typeof(T));
                }
                public static System.IntPtr DerivedConstructorPointer<T>() => new(1);
                public static void DerivedConstructorBody(object value) { }
            }
        }
        namespace MelonLoader
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class RegisterTypeInIl2Cpp : System.Attribute {
                public static void RegisterAssembly(System.Reflection.Assembly assembly) {
                    foreach (var type in assembly.GetTypes())
                        if (System.Attribute.IsDefined(type, typeof(RegisterTypeInIl2Cpp)))
                            Il2CppInterop.Runtime.Injection.ClassInjector.Registered.Add(type);
                }
            }
        }
        namespace Il2CppInterop.Runtime.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.All)]
            public sealed class HideFromIl2CppAttribute : System.Attribute { }
        }
        """;
}
