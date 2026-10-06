using System.Collections.Immutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace S1Interop.Compiler.Tests;

// Each case runs the same author program against the authored Mono contract and, after lowering, against the
// authored IL2CPP contract, so the live dictionary view is held to the behavior of the CLR collection it replaces.
internal static class DictionaryTests
{
    public static void LiveAliasesObserveSourceAndGameMutation() => Verify("""
        using System.Collections.Generic;
        using ScheduleOne.Testing;
        public static class Probe {
            public static int Run() {
                Dictionary<string, int> alias = DictionaryStore.Scores;
                alias["gamma"] = 3;
                alias.Add("delta", 4);
                if (DictionaryStore.Scores.Count != 4 || DictionaryStore.Scores["gamma"] != 3) return -1;
                DictionaryStore.AddScore("epsilon", 5);
                if (!alias.ContainsKey("epsilon") || alias.Count != 5) return -2;
                if (!object.ReferenceEquals(alias, DictionaryStore.ReadScores())) return -3;
                alias["alpha"] = 100;
                if (DictionaryStore.Scores["alpha"] != 100 || alias.Count != 5) return -4;
                if (!alias.Remove("beta") || alias.Remove("beta") || DictionaryStore.Scores.ContainsKey("beta")) return -5;
                if (!alias.Remove("gamma", out int removed) || removed != 3 || alias.Count != 3) return -6;
                alias.Clear();
                return DictionaryStore.ReadScores().Count == 0 && DictionaryStore.Scores.TryAdd("again", 1) ? 1 : -7;
            }
        }
        """, 1);

    public static void ClrExceptionsForNullDuplicateAndMissingKeys() => Verify("""
        using System;
        using System.Collections.Generic;
        using ScheduleOne.Testing;
        public static class Probe {
            public static int Run() {
                var scores = DictionaryStore.Scores;
                var ranks = DictionaryStore.Ranks;
                int mask = 0;
                try { scores.Add("alpha", 9); } catch (ArgumentException e) when (e.GetType() == typeof(ArgumentException)) { mask |= 1; }
                try { var missing = scores["missing"]; } catch (KeyNotFoundException) { mask |= 2; }
                try { scores.Add(null, 1); } catch (ArgumentNullException) { mask |= 4; }
                try { var nothing = scores[null]; } catch (ArgumentNullException) { mask |= 8; }
                try { scores[null] = 1; } catch (ArgumentNullException) { mask |= 16; }
                try { scores.ContainsKey(null); } catch (ArgumentNullException) { mask |= 32; }
                try { scores.TryGetValue(null, out _); } catch (ArgumentNullException) { mask |= 64; }
                try { scores.Remove(null); } catch (ArgumentNullException) { mask |= 128; }
                try { ranks.Add(null, 1); } catch (ArgumentNullException) { mask |= 256; }
                try { scores.TryAdd(null, 1); } catch (ArgumentNullException) { mask |= 512; }
                // Failed calls must not have touched storage, and absence is a result rather than an error.
                if (scores.Count != 2 || scores["alpha"] != 1 || ranks.Count != 1) return -1;
                if (scores.Remove("missing") || scores.ContainsKey("missing")) return -2;
                if (scores.TryGetValue("missing", out int absent) || absent != 0) return -3;
                if (scores.TryAdd("alpha", 5) || scores["alpha"] != 1) return -4;
                return mask;
            }
        }
        """, 1023);

    public static void EnumerationInvalidatesAndCursorCopiesAreIndependent() => Verify("""
        using System;
        using System.Collections.Generic;
        using ScheduleOne.Testing;
        public static class Probe {
            public static int Run() {
                var scores = DictionaryStore.Scores;
                var cursor = scores.GetEnumerator();
                if (!cursor.MoveNext()) return -1;
                var copy = cursor;
                if (!cursor.MoveNext()) return -2;
                if (cursor.Current.Key != "beta" || copy.Current.Key != "alpha") return -3;
                if (!copy.MoveNext() || copy.Current.Key != "beta" || copy.MoveNext()) return -4;
                DictionaryStore.AddScore("gamma", 3);
                try { cursor.MoveNext(); return -5; } catch (InvalidOperationException) { }
                try { copy.MoveNext(); return -6; } catch (InvalidOperationException) { }
                int sum = 0;
                foreach (var pair in scores) sum += pair.Value;
                try { foreach (var pair in scores) scores.Add(pair.Key + "!", 0); return -7; }
                catch (InvalidOperationException) { }
                return sum * 10 + scores.Count;
            }
        }
        """, 64);

    public static void KeysAndValuesAreLiveReadOnlyViews() => Verify("""
        using System;
        using System.Collections.Generic;
        using ScheduleOne.Testing;
        public static class Probe {
            public static int Run() {
                var scores = DictionaryStore.Scores;
                var keys = scores.Keys;
                var values = scores.Values;
                if (!object.ReferenceEquals(keys, scores.Keys) || !object.ReferenceEquals(values, scores.Values)) return -1;
                scores["gamma"] = 3;
                DictionaryStore.AddScore("delta", 4);
                ICollection<string> keyView = keys;
                ICollection<int> valueView = values;
                if (keys.Count != 4 || values.Count != 4 || !keyView.Contains("delta") || !valueView.Contains(4) || valueView.Contains(5)) return -2;
                if (!keyView.IsReadOnly || !valueView.IsReadOnly) return -3;
                int refused = 0;
                try { keyView.Add("x"); } catch (NotSupportedException) { refused |= 1; }
                try { keyView.Remove("alpha"); } catch (NotSupportedException) { refused |= 2; }
                try { keyView.Clear(); } catch (NotSupportedException) { refused |= 4; }
                try { valueView.Add(9); } catch (NotSupportedException) { refused |= 8; }
                try { valueView.Remove(1); } catch (NotSupportedException) { refused |= 16; }
                try { valueView.Clear(); } catch (NotSupportedException) { refused |= 32; }
                if (refused != 63 || scores.Count != 4) return -4;
                var copied = new string[6];
                keys.CopyTo(copied, 1);
                if (copied[0] != null || copied[1] != "alpha" || copied[4] != "delta" || copied[5] != null) return -5;
                try { values.CopyTo(new int[3], 0); return -6; } catch (ArgumentException) { }
                try { keys.CopyTo(null, 0); return -7; } catch (ArgumentNullException) { }
                try { keys.CopyTo(copied, -1); return -8; } catch (ArgumentOutOfRangeException) { }
                IReadOnlyDictionary<string, int> readOnly = scores;
                int names = 0;
                foreach (var key in readOnly.Keys) names += readOnly[key];
                int sum = 0;
                foreach (var value in readOnly.Values) sum += value;
                if (names != 10 || sum != 10 || readOnly.Count != 4 || !readOnly.ContainsKey("beta")) return -9;
                var cursor = keys.GetEnumerator();
                if (!cursor.MoveNext()) return -10;
                scores.Add("later", 5);
                try { cursor.MoveNext(); return -11; } catch (InvalidOperationException) { }
                scores.Clear();
                if (keys.Count != 0 || values.Count != 0) return -12;
                return sum;
            }
        }
        """, 10);

    public static void NongenericDictionaryAndCollectionContracts() => Verify("""
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using ScheduleOne.Testing;
        public static class Probe {
            public static int Run() {
                IDictionary opaque = DictionaryStore.Scores;
                ICollection collection = DictionaryStore.Scores;
                if (opaque.Count != 2 || opaque.IsReadOnly || opaque.IsFixedSize) return -1;
                if (collection.IsSynchronized || collection.SyncRoot == null) return -2;
                if (!opaque.Contains("alpha") || opaque.Contains(7) || opaque.Contains("ALPHA")) return -3;
                if ((int)opaque["beta"] != 2 || opaque["missing"] != null || opaque[7] != null) return -4;
                opaque.Add("gamma", 3);
                opaque["delta"] = 4;
                opaque["alpha"] = 10;
                opaque.Remove("beta");
                opaque.Remove(7);
                if (DictionaryStore.Scores.Count != 3 || DictionaryStore.Scores["alpha"] != 10 || DictionaryStore.Scores.ContainsKey("beta")) return -5;

                int errors = 0;
                try { opaque.Add(7, 1); } catch (ArgumentException e) when (e.GetType() == typeof(ArgumentException)) { errors |= 1; }
                try { opaque.Add("eps", "text"); } catch (ArgumentException e) when (e.GetType() == typeof(ArgumentException)) { errors |= 2; }
                try { opaque.Add("eps", null); } catch (ArgumentNullException) { errors |= 4; }
                try { opaque.Add(null, 1); } catch (ArgumentNullException) { errors |= 8; }
                try { opaque["eps"] = "text"; } catch (ArgumentException e) when (e.GetType() == typeof(ArgumentException)) { errors |= 16; }
                try { opaque[7] = 1; } catch (ArgumentException e) when (e.GetType() == typeof(ArgumentException)) { errors |= 32; }
                try { opaque.Add("gamma", 9); } catch (ArgumentException e) when (e.GetType() == typeof(ArgumentException)) { errors |= 64; }
                try { opaque.Contains(null); } catch (ArgumentNullException) { errors |= 128; }
                if (errors != 255 || DictionaryStore.Scores.Count != 3) return -6;

                var enumerator = opaque.GetEnumerator();
                try { var early = enumerator.Key; return -7; } catch (InvalidOperationException) { }
                int total = 0;
                while (enumerator.MoveNext()) {
                    DictionaryEntry entry = enumerator.Entry;
                    if (!(enumerator.Current is DictionaryEntry) || !Equals(entry.Key, enumerator.Key) || !Equals(entry.Value, enumerator.Value)) return -8;
                    total += (int)entry.Value;
                }
                try { var late = enumerator.Value; return -9; } catch (InvalidOperationException) { }
                int boxedPairs = 0;
                foreach (object item in (IEnumerable)DictionaryStore.Scores) if (item is KeyValuePair<string, int>) boxedPairs++;
                if (boxedPairs != 3) return -10;

                // ICollection.CopyTo accepts pair, DictionaryEntry and object arrays and rejects everything else.
                var pairs = new KeyValuePair<string, int>[4];
                collection.CopyTo(pairs, 1);
                if (pairs[0].Key != null || pairs[1].Key == null || pairs[3].Key == null) return -11;
                var entries = new DictionaryEntry[3];
                collection.CopyTo(entries, 0);
                var boxed = new object[3];
                collection.CopyTo(boxed, 0);
                if (!(boxed[0] is KeyValuePair<string, int> first) || first.Key != (string)entries[0].Key || first.Key != pairs[1].Key) return -12;
                try { collection.CopyTo(new int[3], 0); return -13; } catch (ArgumentException) { }
                try { collection.CopyTo(new object[2], 0); return -14; } catch (ArgumentException) { }
                try { collection.CopyTo(null, 0); return -15; } catch (ArgumentNullException) { }
                try { collection.CopyTo(new object[3], 4); return -16; } catch (ArgumentException) { }

                ICollection keyCollection = opaque.Keys;
                ICollection valueCollection = opaque.Values;
                var keyArray = new string[3];
                keyCollection.CopyTo(keyArray, 0);
                var valueObjects = new object[4];
                valueCollection.CopyTo(valueObjects, 1);
                if (keyArray[0] == null || valueObjects[0] != null || !(valueObjects[1] is int)) return -17;
                try { keyCollection.CopyTo(new int[3], 0); return -18; } catch (ArgumentException) { }
                try { valueCollection.CopyTo(new long[3], 0); return -19; } catch (ArgumentException) { }
                try { valueCollection.CopyTo(new int[2], 0); return -20; } catch (ArgumentException) { }
                if (keyCollection.Count != 3 || keyCollection.IsSynchronized || !ReferenceEquals(keyCollection.SyncRoot, collection.SyncRoot)) return -21;
                return total;
            }
        }
        """, 17);

    public static void UnrelatedManagedDictionaryStaysManaged() => Verify("""
        using System;
        using System.Collections.Generic;
        using ScheduleOne.Testing;
        public static class Probe {
            public static int Run() {
                var bridged = DictionaryStore.Scores;
                bridged["gamma"] = 3;
                var managed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "ALPHA", 10 } };
                if (managed.GetType() != typeof(Dictionary<string, int>)) return -1;
                if (managed["alpha"] != 10 || !managed.ContainsKey("Alpha") || !managed.Comparer.Equals("a", "A")) return -2;
                // The game's dictionary keeps its own default comparer and is unaffected by the managed one.
                if (bridged.ContainsKey("ALPHA") || bridged.Comparer.Equals("a", "A")) return -3;
                foreach (var pair in bridged) managed[pair.Key.ToUpperInvariant()] = pair.Value;
                if (managed.Count != 3 || managed["alpha"] != 1) return -4;
                managed.Add("delta", 4);
                if (DictionaryStore.Scores.ContainsKey("delta") || bridged.Count != 3) return -5;
                // A copy that is never handed to the game remains an ordinary CLR dictionary.
                var plain = new Dictionary<string, int>(bridged);
                plain.Add("x", 1);
                if (plain.GetType() != typeof(Dictionary<string, int>) || bridged.ContainsKey("x")) return -6;
                return managed.Count * 10 + bridged.Count;
            }
        }
        """, 43);

    public static void NativeKeysUseGameIdentityAndEquality() => Verify("""
        using System;
        using System.Collections.Generic;
        using ScheduleOne.Testing;
        public static class Probe {
            public static int Run() {
                var ranks = DictionaryStore.Ranks;
                var employee = (Employee)Actor.Current;
                if (!ranks.ContainsKey(employee) || ranks[employee] != 7) return -1;
                if (!ranks.TryGetValue(Actor.Current, out int rank) || rank != 7) return -2;
                var stranger = new Actor();
                if (ranks.ContainsKey(stranger)) return -3;
                ranks[stranger] = 9;
                ranks.Add(new KeyActor { Key = 3 }, 3);
                if (!ranks.TryGetValue(new KeyActor { Key = 3 }, out int keyed) || keyed != 3) return -4;
                if (ranks.TryAdd(new KeyActor { Key = 3 }, 4) || ranks.Count != 3) return -5;
                try { ranks.Add(new KeyActor { Key = 3 }, 5); return -6; } catch (ArgumentException) { }
                if (!ranks.ContainsValue(9) || ranks.ContainsValue(8)) return -7;
                int salary = 0, keyActors = 0;
                foreach (var pair in ranks) {
                    if (pair.Key is Employee found) salary += found.Salary;
                    if (pair.Key is KeyActor) keyActors++;
                }
                if (!ranks.Remove(new KeyActor { Key = 3 }) || !ranks.Remove(employee)) return -8;
                if (ranks.ContainsKey(Actor.Current) || ranks.Count != 1 || !ranks.ContainsKey(stranger)) return -9;
                return salary * 10 + keyActors;
            }
        }
        """, 71);

    public static void SourceCreatedDictionaryFlowsIntoNativeSlot() => Verify("""
        using System.Collections.Generic;
        using ScheduleOne.Testing;
        public static class Probe {
            public static int Run() {
                var created = new Dictionary<string, int> { { "x", 1 } };
                DictionaryStore.ReplaceScores(created);
                created["y"] = 2;
                if (DictionaryStore.Scores.Count != 2 || DictionaryStore.Scores["y"] != 2) return -1;
                if (!object.ReferenceEquals(created, DictionaryStore.ReadScores())) return -2;
                DictionaryStore.AddScore("z", 3);
                if (created.Count != 3 || created["z"] != 3) return -3;
                var copy = new Dictionary<string, int>(created);
                copy.Add("w", 4);
                if (created.ContainsKey("w") || DictionaryStore.Scores.Count != 3) return -4;
                return created.Count * 10 + copy.Count;
            }
        }
        """, 34);

    public static void SourceCreatedNativeKeyDictionaryFlowsIntoNativeSlot() => Verify("""
        using System.Collections.Generic;
        using ScheduleOne.Testing;
        public static class Probe {
            public static int Run() {
                var created = new Dictionary<Actor, int>();
                created.Add(new KeyActor { Key = 5 }, 1);
                created.Add(Actor.Current, 2);
                DictionaryStore.ReplaceRanks(created);
                created.Add(new Actor(), 3);
                var read = DictionaryStore.ReadRanks();
                if (!object.ReferenceEquals(created, read) || read.Count != 3) return -1;
                if (!read.ContainsKey(new KeyActor { Key = 5 }) || !read.ContainsKey((Employee)Actor.Current)) return -2;
                DictionaryStore.AddRank(new KeyActor { Key = 6 }, 4);
                if (created.Count != 4 || !created.ContainsKey(new KeyActor { Key = 6 })) return -3;
                return created[(Employee)Actor.Current] + created.Count * 10;
            }
        }
        """, 42);

    private static void Verify(string source, int expected)
    {
        var mono = CompilationSupport.Create("Probe", source, CompilationSupport.PlatformReferences.Add(Contracts.MonoReference));
        int original = Execute(mono, Contracts.MonoBytes, default);
        Require(original == expected, $"Original Mono-contract program returned {original}, expected {expected}.");
        var lowered = new InteropCompiler().Lower(mono, CompilationSupport.PlatformReferences.Add(Contracts.NativeReference));
        Require(lowered.Success, string.Join(Environment.NewLine, lowered.Diagnostics));
        int native = Execute(lowered.Compilation, Contracts.NativeBytes, lowered.RuntimeAssembly);
        Require(native == original, $"Lowered IL2CPP-contract program returned {native}, but the original returned {original}.");
    }

    private static int Execute(CSharpCompilation compilation, byte[] contract, ImmutableArray<byte> runtime)
    {
        using var emitted = new MemoryStream();
        var result = compilation.Emit(emitted);
        Require(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var context = new AssemblyLoadContext("DictionaryContract", isCollectible: true);
        try
        {
            using (var reference = new MemoryStream(contract)) context.LoadFromStream(reference);
            if (!runtime.IsDefaultOrEmpty)
            {
                using var support = new MemoryStream(runtime.ToArray());
                context.LoadFromStream(support);
            }
            emitted.Position = 0;
            var assembly = context.LoadFromStream(emitted);
            return (int)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!;
        }
        catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
        finally { context.Unload(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
