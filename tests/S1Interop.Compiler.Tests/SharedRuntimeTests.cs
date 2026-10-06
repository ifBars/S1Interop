using System.Collections.Immutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace S1Interop.Compiler.Tests;

internal static class SharedRuntimeTests
{
    public static void RejectMismatchedRuntime()
    {
        var author = CompilationSupport.Create("Probe", "public static class Probe { public static int Run() => 1; }",
            CompilationSupport.PlatformReferences.Add(Contracts.MonoReference));
        var compiler = new InteropCompiler();
        var original = compiler.Lower(author, CompilationSupport.PlatformReferences.Add(Contracts.NativeReference));
        byte[] otherContract = CompilationSupport.Emit("NativeContract", RuntimeContracts.Il2Cpp.Replace("uint", "ulong", StringComparison.Ordinal));
        var other = compiler.Lower(author, CompilationSupport.PlatformReferences.Add(MetadataReference.CreateFromImage(otherContract)));
        RequireSuccess(original);
        RequireSuccess(other);
        Require(!original.RuntimeAssembly.SequenceEqual(other.RuntimeAssembly), "Changed handle ABI did not change support output.");
        try { Execute(Emit(original.Compilation), otherContract, [], other.RuntimeAssembly); }
        catch (Exception exception) when (exception.GetBaseException() is InvalidOperationException failure &&
            failure.Message.StartsWith("S1Interop runtime mismatch for Probe.", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("A mismatched shared runtime was accepted.");
    }

    public static void OlderAuthorLanguage()
    {
        var author = CompilationSupport.Create("Probe", "public static class Probe { public static int Run() => 1; }",
            CompilationSupport.PlatformReferences.Add(Contracts.MonoReference));
        author = author.RemoveAllSyntaxTrees().AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "public static class Probe { public static int Run() => 1; }", new CSharpParseOptions(LanguageVersion.CSharp7_3)));
        var result = new InteropCompiler().Lower(author, CompilationSupport.PlatformReferences.Add(Contracts.NativeReference));
        RequireSuccess(result);
        Require(Execute(Emit(result.Compilation), Contracts.NativeBytes, [], result.RuntimeAssembly) == 1,
            "Generated initialization failed for an older author language version.");
    }

    public static void Run()
    {
        const string librarySource = """
            using System;
            using System.Collections.Generic;
            using ScheduleOne.Testing;
            using UnityEngine.Events;
            [assembly: System.Reflection.AssemblyMetadata("S1Interop.AuthoringSha256", "fixture")]
            public abstract class LibraryBase : UnityEngine.MonoBehaviour {
                [NonSerialized] protected int value = 2;
                public int Score = 13;
                public UnityEngine.ObjectSequence Objects = new();
                private int __S1InteropField_Score;
                protected LibraryBase() { value += 3; }
                public abstract int Read();
            }
            public sealed class LibraryComponent : UnityEngine.MonoBehaviour {
                public System.Collections.IEnumerator Start() { yield return 7; }
                private int __S1InteropManagedStart;
            }
            public static class ModLibrary {
                public static int[] ArrayValues() => ArrayStore.Read(null);
                public static void StoreArray(int[] values) => ArrayStore.Replace(values);
                public static void UseArray(Action<int[]> callback) => callback(ArrayStore.Values);
                public static Func<int[]> ArrayFactory() => () => ArrayStore.Values;
                public static Actor[] Actors() => ArrayStore.ReadActors();
                public static void StoreActors(Actor[] values) => ArrayStore.ReplaceActors(values);
                public static void StoreScores(Dictionary<string, int> scores) => DictionaryStore.ReplaceScores(scores);
                public static IEqualityComparer<string> ScoreComparer => DictionaryStore.Scores.Comparer;
                public static List<int> Integers() => ScalarStore.Numbers;
                public static void StoreIntegers(List<int> values) => ScalarStore.Replace(values);
                public static List<string> ManagedWords() => new List<string> { "managed" };
                public static List<Actor> Read() => Actor.All;
                public static void Add(List<Actor> list) => list.Add(new Employee());
                public static void AddAction(UnityEvent signal, Action callback) => signal.AddListener(callback.Invoke);
                public static void RemoveAction(UnityEvent signal, Action callback) => signal.RemoveListener(callback.Invoke);
                public static void AddListener(UnityEvent signal, UnityAction callback) => signal.AddListener(callback);
                public static void RemoveListener(UnityEvent signal, UnityAction callback) => signal.RemoveListener(callback);
            }
            """;
        const string consumerSource = """
            using System;
            using System.Linq;
            using System.Collections.Generic;
            using ScheduleOne.Testing;
            using UnityEngine.Events;
            public sealed class ConsumerComponent : LibraryBase {
                public ConsumerComponent() { value += 2; }
                public override int Read() => value;
            }
            public sealed class LengthComparer : IEqualityComparer<string> {
                public bool Equals(string left, string right) => left.Length == right.Length;
                public int GetHashCode(string value) => value.Length;
            }
            public static class Probe {
                static int calls;
                static void Hit() { calls++; }
                public static int Run() {
                    int[] array = { 2, 3 };
                    ModLibrary.StoreArray(array);
                    array[0] = 9;
                    var nativeArray = ModLibrary.ArrayValues();
                    nativeArray[1]++;
                    if (ArrayStore.Values[0] != 9 || array[1] != 4 || !object.ReferenceEquals(nativeArray, array)) return -11;
                    Action<int[]> update = values => values[0] = 23;
                    ModLibrary.UseArray(update);
                    Func<int[]> factory = ModLibrary.ArrayFactory();
                    if (array[0] != 23 || !object.ReferenceEquals(factory(), array)) return -13;
                    Actor[] actors = { new Employee() };
                    ModLibrary.StoreActors(actors);
                    Actor[] actorAlias = ModLibrary.Actors();
                    actorAlias[0] = new Customer();
                    if (!object.ReferenceEquals(actors, actorAlias) || !(actors[0] is Customer)) return -12;
                    var comparer = new LengthComparer();
                    var scores = new Dictionary<string, int>(comparer) { ["one"] = 3 };
                    ModLibrary.StoreScores(scores);
                    if (DictionaryStore.Scores["two"] != 3 || !object.ReferenceEquals(comparer, ModLibrary.ScoreComparer)) return -8;
                    LibraryBase inherited = new UnityEngine.GameObject().AddComponent<ConsumerComponent>();
                    if (inherited.Read() != 7) return -7;
                    inherited.Score++;
                    if (inherited.Score != 14) return -9;
                    inherited.Objects.Add(new UnityEngine.GameObject());
                    if (inherited.Objects.Select(item => item).Count() != 1) return -10;
                    List<int> numbers = ModLibrary.Integers();
                    numbers.Add(9);
                    if (ScalarStore.Numbers.Count != 4) return -4;
                    var replacements = new List<int> { 7 };
                    ModLibrary.StoreIntegers(replacements);
                    replacements.Add(8);
                    if (ModLibrary.Integers().Count != 2) return -5;
                    List<string> nativeWords = ScalarStore.Words;
                    var managedWords = ModLibrary.ManagedWords();
                    if (managedWords.GetType() != typeof(List<string>)) return -6;
                    var component = new UnityEngine.GameObject().AddComponent<LibraryComponent>();
                    Func<System.Collections.IEnumerator> start = component.Start;
                    var iterator = start();
                    if (nameof(component.Start) != "Start" || !iterator.MoveNext() || (int)iterator.Current != 7) return -2;
                    System.Collections.IEnumerator direct = component.Start();
                    if (!direct.MoveNext() || (int)direct.Current != 7) return -3;
                    List<Actor> view = ModLibrary.Read();
                    ModLibrary.Add(view);
                    if (!object.ReferenceEquals(view, Actor.All) || view.Count != 2) return -1;
                    var signal = new UnityEvent();
                    ModLibrary.AddListener(signal, Hit);
                    signal.Invoke();
                    signal.RemoveListener(Hit);
                    signal.Invoke();
                    signal.AddListener(Hit);
                    ModLibrary.RemoveListener(signal, Hit);
                    signal.Invoke();
                    Action callback = Hit;
                    ModLibrary.AddAction(signal, callback);
                    signal.Invoke();
                    signal.RemoveListener(callback.Invoke);
                    signal.Invoke();
                    signal.AddListener(callback.Invoke);
                    ModLibrary.RemoveAction(signal, callback);
                    signal.Invoke();
                    return calls;
                }
            }
            """;
        var monoReferences = CompilationSupport.PlatformReferences.Add(Contracts.MonoReference);
        var nativeReferences = CompilationSupport.PlatformReferences.Add(Contracts.NativeReference);
        var library = CompilationSupport.Create("ModLibrary", librarySource, monoReferences);
        var monoLibrary = Emit(library);
        var loweredLibrary = new InteropCompiler().Lower(library, nativeReferences);
        RequireSuccess(loweredLibrary);
        var nativeLibrary = Emit(loweredLibrary.Compilation);
        var consumer = CompilationSupport.Create("Probe", consumerSource,
            monoReferences.Add(MetadataReference.CreateFromImage(monoLibrary)));
        Require(Execute(Emit(consumer), Contracts.MonoBytes, monoLibrary, []) == 2, "Mono fixture failed.");
        var loweredConsumer = new InteropCompiler().Lower(consumer,
            nativeReferences.Add(MetadataReference.CreateFromImage(nativeLibrary))
                .Add(MetadataReference.CreateFromImage(loweredLibrary.RuntimeAssembly)));
        RequireSuccess(loweredConsumer);
        Require(loweredLibrary.RuntimeAssembly.SequenceEqual(loweredConsumer.RuntimeAssembly),
            "Unrelated mod references changed the generated runtime image.");
        Require(Execute(Emit(loweredConsumer.Compilation), Contracts.NativeBytes, nativeLibrary,
            loweredConsumer.RuntimeAssembly) == 2, "Cross-mod list identity or delegate removal failed.");
    }

    private static int Execute(byte[] consumer, byte[] contract, byte[] library, ImmutableArray<byte> runtime)
    {
        var context = new AssemblyLoadContext("SharedCompilerRuntime", isCollectible: true);
        try
        {
            foreach (var image in new[] { contract, runtime.ToArray(), library, consumer })
            {
                if (image.Length == 0) continue;
                using var stream = new MemoryStream(image);
                var assembly = context.LoadFromStream(stream);
                if (ReferenceEquals(image, consumer))
                {
                    int result = (int)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!;
                    if (library.Length != 0 && !runtime.IsEmpty)
                    {
                        var libraryType = context.Assemblies.Single(value => value.GetName().Name == "ModLibrary").GetType("LibraryComponent")!;
                        var injector = context.Assemblies.Select(value => value.GetType("Il2CppInterop.Runtime.Injection.ClassInjector"))
                            .Single(value => value != null)!;
                        var registered = (HashSet<Type>)injector.GetField("Registered")!.GetValue(null)!;
                        Require(registered.Contains(libraryType), "Compiler-built dependency component was not registered.");
                    }
                    return result;
                }
            }
            throw new InvalidOperationException("Consumer assembly was not executed.");
        }
        finally { context.Unload(); }
    }

    private static byte[] Emit(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Require(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return stream.ToArray();
    }

    private static void RequireSuccess(LoweringResult result) =>
        Require(result.Success, string.Join(Environment.NewLine, result.Diagnostics));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
