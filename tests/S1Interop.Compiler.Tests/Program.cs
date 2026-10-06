using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using S1Interop.Compiler;
using S1Interop.Compiler.Tests;

var tests = new (string Name, Action Test)[]
{
    ("ArrayNativeDelegateFactoriesConstructorsAndExtensions", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public sealed class Holder { public Func<int[], int> Callback; }
        public static class Extensions { public static int Change(this Holder owner, int[] values) { values[0] = 67; return values[0]; } }
        public static class Probe {
            private static Func<int[], int> Create(int[] configuration) {
                if (configuration.GetType() != typeof(int[])) throw new Exception("Factory input was incorrectly joined to callback input");
                return input => { ArrayStore.Replace(input); return input[0]; };
            }
            public static int Run() {
                int[] config = { 1 };
                var holder = new Holder { Callback = Create(config) };
                int[] data = { 3 }; ArrayStore.Replace(data);
                if (holder?.Callback(data) != 3) return -1;
                holder.Callback = new Func<int[], int>(holder.Change);
                if (holder.Callback(data) != 67 || ArrayStore.Values[0] != 67) return -2;
                if (config.GetType() != typeof(int[])) return -3;
                return 67;
            }
        }
        """, 67)),
    ("ArrayNativeDelegateReturnsAndConditionalInvokePreserveStorage", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe {
            private static Func<int[]> Make() => () => new int[] { 7 };
            public static int Run() {
                Func<int[]> factory = Make();
                var alias = factory(); ArrayStore.Replace(alias);
                Action<int[]> change = values => values[0] = 53;
                change?.Invoke(alias);
                if (ArrayStore.Values[0] != 53) return -1;
                Func<int[], int[]> identity = values => values;
                int[] retained = identity(alias);
                retained[0] = 57;
                Func<int[]> reader = () => ArrayStore.Read(null);
                if (!ReferenceEquals(reader(), alias)) return -2;
                return ArrayStore.Values[0];
            }
        }
        """, 57)),
    ("ArrayNativeBase64EncodingPreservesValidationAndSlices", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe {
            private static string Encode(byte[] data, int offset, int length, Base64FormattingOptions options) {
                ArrayStore.Bytes = data;
                return Convert.ToBase64String(data, offset, length, options);
            }
            public static int Run() {
                byte[] bytes = { 1, 2, 3 }; ArrayStore.Bytes = bytes;
                if (Convert.ToBase64String(bytes) != "AQID" || Convert.ToBase64String(bytes, 1, 2) != "AgM=") return -1;
                if (Convert.ToBase64String(options: Base64FormattingOptions.None, inArray: bytes) != "AQID") return -2;
                var longBytes = new byte[60]; ArrayStore.Bytes = longBytes;
                if (!Convert.ToBase64String(longBytes, Base64FormattingOptions.InsertLineBreaks).Contains("\r\n")) return -3;
                try { Encode(null, -1, -1, (Base64FormattingOptions)99); return -4; } catch (ArgumentNullException e) { if (e.ParamName != "inArray") return -5; }
                try { Encode(bytes, -1, -1, (Base64FormattingOptions)99); return -6; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "length") return -7; }
                try { Encode(bytes, 4, 0, (Base64FormattingOptions)99); return -8; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "offset") return -9; }
                try { Encode(bytes, 0, 0, (Base64FormattingOptions)99); return -10; } catch (ArgumentException e) { if (e.ParamName != "options") return -11; }
                return Encode(bytes, 3, 0, Base64FormattingOptions.None) == "" ? 61 : -12;
            }
        }
        """, 61)),
    ("ArrayNativeSourceDelegateParametersPreserveAliases", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe {
            private static Func<int[], int> callback;
            private static int[] retained;
            private static void Configure(Func<int[], int> value) { callback = value; }
            private static int Read(int[] values) { retained = values; values[0] = 47; return values[0]; }
            public static int Run() {
                int[] values = { 1 }; ArrayStore.Replace(values);
                Configure(Read);
                if (callback(values) != 47 || !ReferenceEquals(retained, ArrayStore.Values)) return -1;
                Configure((int[] input) => { input[0] = 49; return input[0]; });
                if (callback.Invoke(values) != 49 || retained[0] != 49) return -2;
                Func<int[], int> unrelated = input => input[0];
                var managed = new int[] { 51 };
                if (unrelated(managed) != 51 || managed.GetType() != typeof(int[])) return -3;
                return 49;
            }
        }
        """, 49)),
    ("ArrayNativeCustomEncodingRemainsDiagnosed", () => {
        var result = Lower("""
            using System.Text;
            using ScheduleOne.Testing;
            public static class Probe {
                public static string Read(Encoding encoding) => encoding.GetString(ArrayStore.Bytes);
                public static string ReadLocal() { var encoding = Encoding.UTF8; return encoding.GetString(ArrayStore.Bytes); }
                public static string ReadConditional() => Encoding.UTF8?.GetString(ArrayStore.Bytes);
            }
            """);
        Assert(!result.Success && result.Diagnostics.Count(d => d.Id == "S1IC032") == 3,
            "Unknown encoding receivers must not silently bypass array overloads: " + string.Join(Environment.NewLine, result.Diagnostics));
    }),
    ("ArrayNativeUtf8ReadsPreserveBufferAndValidation", () => Verify("""
        using System;
        using System.Text;
        using static System.Text.Encoding;
        using ScheduleOne.Testing;
        public static class Probe {
            private static int order;
            private static int Count() { order = order * 10 + 1; return 2; }
            private static byte[] Bytes() { order = order * 10 + 2; return ArrayStore.Bytes; }
            private static int Index() { order = order * 10 + 3; return 0; }
            private static string Read(byte[] bytes, int index, int count) {
                ArrayStore.Bytes = bytes;
                return Encoding.UTF8.GetString(bytes, index, count);
            }
            public static int Run() {
                byte[] data = { 65, 66, 67 }; ArrayStore.Bytes = data;
                if (Encoding.UTF8.GetString(count: Count(), bytes: Bytes(), index: Index()) != "AB" || order != 123) return -1;
                data[0] = 90;
                if (Encoding.UTF8.GetString(data) != "ZBC" || Read(data, 3, 0) != "") return -2;
                if (UTF8.GetString(data) != "ZBC" || (Encoding.UTF8).GetString(data) != "ZBC") return -12;
                ArrayStore.Bytes = System.Convert.FromBase64String("");
                if (UTF8.GetString(ArrayStore.Bytes) != "") return -13;
                if (Read(new byte[] { 255 }, 0, 1) != "\uFFFD") return -3;
                try { Read(null, -1, -1); return -4; } catch (ArgumentNullException e) { if (e.ParamName != "bytes") return -5; }
                try { Read(data, -1, -1); return -6; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "index") return -7; }
                try { Read(data, 0, -1); return -8; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "count") return -9; }
                try { Read(data, 3, 1); return -10; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "bytes") return -11; }
                return 43;
            }
        }
        """, 43)),
    ("ArrayNativeBase64OwnedResultPreservesAliasesAndErrors", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe {
            private static int calls;
            private static string Input() { calls++; return " A QI D\r\n"; }
            private static byte[] Decode(string value) { byte[] result = Convert.FromBase64String(value); return result; }
            public static int Run() {
                byte[] data = Decode(Input());
                ArrayStore.Bytes = data;
                byte[] alias = data;
                alias[1] = 39;
                if (calls != 1 || data.Length != 3 || ArrayStore.Bytes[1] != 39) return -1;
                byte[] first = Decode(""); byte[] second = Decode("");
                if (ReferenceEquals(first, second) || first.Length != 0) return -2;
                try { Decode(null); return -3; } catch (ArgumentNullException error) { if (error.ParamName != "s") return -4; }
                try { Decode("bad!"); return -5; } catch (FormatException) { }
                return ArrayStore.Bytes[1];
            }
        }
        """, 39)),
    ("ArrayNativeGenericElementStorageRemainsDiagnosed", () => {
        var result = Lower("""
            using ScheduleOne.Testing;
            public static class Probe {
                private static T[] Echo<T>(T[] values) => values;
                public static void Run() {
                    int[] values = { 1 };
                    ArrayStore.Replace(values);
                    var alias = Echo<int>(values);
                    alias[0] = 9;
                }
            }
            """);
        Assert(!result.Success && result.Diagnostics.Any(d => d.Id == "S1IC032"),
            "Generic element storage must not silently split aliases: " + string.Join(Environment.NewLine, result.Diagnostics));
    }),
    ("ArrayNativeGenericHelpersShareFixedStorage", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Cache<T> { public static int[] Buffer; }
        public static class Probe {
            private static int[] Echo<T>(int[] values) => values;
            private static void Replace<T>(int[] values) { ArrayStore.Replace(values); }
            public static int Run() {
                int[] values = { 4, 5 };
                Replace<string>(values);
                var alias = Echo<int>(values);
                Cache<string>.Buffer = alias;
                Cache<string>.Buffer[0] = 37;
                int[] other = { 8 };
                Cache<int>.Buffer = other;
                if (Cache<int>.Buffer[0] != 8 || !ReferenceEquals(values, ArrayStore.Values)) return -1;
                return ArrayStore.Values[0];
            }
        }
        """, 37)),
    ("ReflectionTrackedFieldReadsWritesAndErrors", () => Verify("""
        using System;
        using System.Reflection;
        using ScheduleOne.Testing;
        namespace HarmonyLib { public static class AccessTools {
            public static FieldInfo Field(Type type, string name) => type.GetField(name);
        } }
        public static class Probe { public static int Run() {
            FieldInfo field = HarmonyLib.AccessTools.Field(name: "Name", type: typeof(Employee));
            var salary = HarmonyLib.AccessTools.Field(typeof(Employee), "Salary");
            var child = HarmonyLib.AccessTools.Field(typeof(Actor), "ReflectionChild");
            var count = HarmonyLib.AccessTools.Field(typeof(Actor), "ReflectionCount");
            if (field == null || salary == null) return -1;
            var actor = new Employee();
            field.SetValue(actor, "changed"); salary.SetValue(actor, 41);
            if ((string)field.GetValue(actor) != "changed" || actor.Name != "changed" || actor.Salary != 41) return -2;
            if ((int)salary.GetValue(Actor.Current) != 7) return -5;
            child.SetValue(actor, Actor.Current);
            if (((Employee)child.GetValue(actor)).Salary != 7) return -6;
            count.SetValue(new object(), 9);
            if ((int)count.GetValue(null) != 9 || Actor.ReflectionCount != 9) return -7;
            try { field.GetValue(null); return -3; } catch (TargetException) { }
            try { field.SetValue(new object(), "wrong"); return -4; } catch (ArgumentException) { }
            return 41;
        } }
        """, 41)),
    ("ReflectionFieldEscapeAndMetadataRemainDiagnosed", () => {
        var result = Lower("""
            using System;
            using System.Reflection;
            using ScheduleOne.Testing;
            namespace HarmonyLib { public static class AccessTools {
                public static FieldInfo Field(Type type, string name) => type.GetField(name);
            } }
            public static class Probe {
                public static FieldInfo Escape() { var field = HarmonyLib.AccessTools.Field(typeof(Actor), "Name"); return field; }
                public static string Metadata() { var field = HarmonyLib.AccessTools.Field(typeof(Actor), "Name"); return field.Name; }
                public static object Alias() { var field = HarmonyLib.AccessTools.Field(typeof(Actor), "Name"); var alias = field; return alias.GetValue(new Actor()); }
            }
            """);
        Assert(!result.Success && result.Diagnostics.Count(d => d.Id == "S1IC034") == 3,
            "Field escapes must not silently expose synthetic reflection metadata: " + string.Join(Environment.NewLine, result.Diagnostics));
    }),
    ("ReflectionNativeFieldPropertyMismatchIsDiagnosed", () => {
        var result = Lower("""
            using ScheduleOne.Testing;
            namespace HarmonyLib { public static class AccessTools {
                public static System.Reflection.FieldInfo Field(System.Type type, string name) => type.GetField(name);
            } }
            public static class Probe {
                public static object Direct() => typeof(Actor).GetField("Name");
                public static object Inherited() => typeof(Employee).GetField("Name");
                public static object Harmony() => HarmonyLib.AccessTools.Field(name: "Name", type: typeof(Actor));
            }
            """);
        Assert(!result.Success && result.Diagnostics.Count(diagnostic => diagnostic.Id == "S1IC034") == 3,
            "Native field-to-property reflection mismatch was not diagnosed at every lookup: " + string.Join(Environment.NewLine, result.Diagnostics));
    }),
    ("ReflectionManagedAndUnchangedNativeFieldsRemainAvailable", () => {
        var result = Lower("""
            using ScheduleOne.Testing;
            public class Local { public string Name; }
            public static class Probe {
                public static object LocalField() => typeof(Local).GetField("Name");
                public static object NativeField() => typeof(Actor).GetField("Calls");
                public static object MissingField() => typeof(Actor).GetField("DoesNotExist");
            }
            """);
        Assert(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }),
    ("SerializedComponentReferenceArraysPreserveAliasesAndPrivateFields", () => Verify("""
        using System;
        using UnityEngine;
        using Object = UnityEngine.Object;
        public sealed class Fields : MonoBehaviour {
            public Object[] Values = { new GameObject(), null };
            [SerializeField] private GameObject[] links = { null };
            public void Set(GameObject value) { links[0] = value; }
            public GameObject Read() => links[0];
            public void Replace(Object[] values) { Values = values; }
        }
        public static class Probe { public static int Run() {
            var fields = new Fields();
            Object[] alias = fields.Values;
            var item = new GameObject();
            fields.Set(item); alias[1] = item;
            if (!ReferenceEquals(fields.Read(), item) || !ReferenceEquals(fields.Values[1], item)) return -1;
            Object[] replacement = { item }; fields.Replace(replacement);
            replacement[0] = null;
            if (fields.Values[0] != null || alias[1] == null) return -2;
            return 59;
        } }
        """, 59)),
    ("SerializedComponentReferenceArrayMetadataUsesNativeStorage", () => {
        var result = Lower("public class Fields : UnityEngine.MonoBehaviour { public UnityEngine.GameObject[] Values = { null }; }");
        Assert(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var field = result.Compilation.GetTypeByMetadataName("Fields")!.GetMembers("Values").OfType<IFieldSymbol>().Single();
        Assert(field.Type is INamedTypeSymbol { Name: "Il2CppReferenceField", TypeArguments: [INamedTypeSymbol { Name: "Il2CppReferenceArray" }] },
            "Reference array field did not receive native storage.");
        Assert(!result.Diagnostics.Any(diagnostic => diagnostic.Id == "S1IC016"), "Supported field reported managed-only storage.");
    }),
    ("ReferenceArraysPreserveSourceAndGameAliases", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe {
            private static Actor[] alias;
            private static Actor[] Read() => ArrayStore.ReadActors();
            private static void Store(Actor[] values) { alias = values; ArrayStore.ReplaceActors(values); }
            public static int Run() {
                Actor[] values = { new Actor(), new Employee() };
                Store(values);
                Actor[] fromGame = Read();
                if (!ReferenceEquals(values, fromGame) || !values.Equals(fromGame) ||
                    values.GetHashCode() != fromGame.GetHashCode()) return -1;
                values[0] = new Customer();
                if (!(fromGame[0] is Customer)) return -2;
                fromGame[1] = null;
                if (alias[1] != null) return -3;
                foreach (Actor actor in fromGame) if (actor is Customer) return 17;
                return -4;
            }
        }
        """, 17)),
    ("ReferenceArraysGenericNativeBaseReturnPreservesStorage", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe {
            private static Actor[] Read() => ArrayStore.ReadGeneric<Actor>();
            public static int Run() {
                Actor[] values = Read();
                if (!ReferenceEquals(values, ArrayStore.Actors)) return -1;
                values[0] = new Customer();
                return ArrayStore.Actors[0] is Customer ? 19 : -2;
            }
        }
        """, 19)),
    ("ReferenceArraysCovariantMemberReturnPreservesSyntaxAndStorage", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe {
            private static Actor[] Read() => ArrayStore.Employees;
            public static int Run() {
                Actor[] values = Read();
                if (!ReferenceEquals(values, ArrayStore.Employees)) return -1;
                try { values[0] = new Customer(); return -2; } catch (ArrayTypeMismatchException) { }
                return 21;
            }
        }
        """, 21)),
    ("ReferenceArraysPreserveAssignmentOrderAndExceptions", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe {
            private static string order = "";
            private static Actor[] Pick(Actor[] array) { order += "a"; return array; }
            private static int Index(int value) { order += "i"; return value; }
            private static Actor Value() { order += "v"; return new Employee(); }
            public static int Run() {
                Actor[] array = new Actor[1]; ArrayStore.ReplaceActors(array);
                Actor assigned = (Pick(array)[Index(0)] = Value());
                if (order != "aiv" || !ReferenceEquals(assigned, array[0])) return -1;
                order = "";
                try { Pick(null)[Index(0)] = Value(); return -2; }
                catch (NullReferenceException) { if (order != "aiv") return -3; }
                order = "";
                try { Pick(array)[Index(2)] = Value(); return -4; }
                catch (IndexOutOfRangeException) { if (order != "aiv") return -5; }
                order = "";
                Pick(array)[Index(0)] ??= Value();
                if (order != "ai") return -6;
                array[0] = null; order = "";
                Pick(array)[Index(0)] ??= Value();
                if (order != "aiv" || !(array[0] is Employee)) return -7;
                order = "";
                try { Pick(array)[Index(-1)] ??= Value(); return -8; }
                catch (IndexOutOfRangeException) { if (order != "ai") return -9; }
                return 23;
            }
        }
        """, 23)),
    ("ReferenceArraysKeepCovariantRuntimeElementType", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            Employee[] employees = new[] { new Employee() };
            Actor[] actors = employees; ArrayStore.ReplaceActors(actors);
            Actor[] fromGame = ArrayStore.ReadActors();
            if (!ReferenceEquals(employees, fromGame)) return -1;
            fromGame[0] = new Employee();
            try { fromGame[0] = new Customer(); return -2; } catch (ArrayTypeMismatchException) { }
            if (!(employees[0] is Employee)) return -3;
            fromGame[0] = null;
            if (employees[0] != null) return -4;
            return 29;
        } }
        """, 29)),
    ("ReferenceArraysCloneAndResizePreserveStorageContracts", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            Employee[] employees = new[] { new Employee() };
            Actor[] actors = employees; ArrayStore.ReplaceActors(actors);
            Actor[] clone = (Actor[])actors.Clone();
            if (ReferenceEquals(clone, actors) || !ReferenceEquals(clone[0], actors[0])) return -1;
            try { clone[0] = new Customer(); return -2; } catch (ArrayTypeMismatchException) { }
            Actor[] old = actors;
            Array.Resize(ref actors, 1);
            if (!ReferenceEquals(old, actors)) return -3;
            Array.Resize(ref actors, 2);
            if (ReferenceEquals(old, actors) || old.Length != 1 || actors.Length != 2 || actors[1] != null) return -4;
            actors[1] = new Customer();
            return actors[1] is Customer ? 31 : -5;
        } }
        """, 31)),
    ("ReferenceArraysInitializersAndUnrelatedManagedStorage", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            Actor[] originals = { new Employee() };
            ArrayStore.ReplaceActors(originals);
            Actor[] expanded = [null, ..originals];
            ArrayStore.ReplaceActors(expanded);
            if (expanded.Length != 2 || expanded[0] != null || !ReferenceEquals(expanded[1], originals[0])) return -1;
            expanded[1] = new Customer();
            if (!(originals[0] is Employee)) return -2;
            Actor[] managed = { new Actor() };
            Actor[] managedAlias = managed;
            managedAlias[0] = null;
            if (managed[0] != null || managed.GetType() != typeof(Actor[])) return -3;
            return 37;
        } }
        """, 37)),
    ("ReferenceArraysRejectEscapedRefsAndHiddenSnapshots", () => {
        foreach (string source in new[] {
            "using ScheduleOne.Testing; public class Probe { public static ref Actor Read() => ref ArrayStore.Actors[0]; }",
            "using ScheduleOne.Testing; public class Probe { public static void Run() { Actor[] x = ArrayStore.ReadActors(); System.Span<Actor> span = x; } }",
            "using ScheduleOne.Testing; public class Probe { public static void Run() { Actor[] x = new System.Collections.Generic.List<Actor>().ToArray(); ArrayStore.ReplaceActors(x); } }"
        }) {
            var result = Lower(source);
            Assert(!result.Success && result.Diagnostics.Any(d => d.Id is "S1IC032" or "S1IC033"),
                "Reference array lifetime/copy boundary did not receive its compiler diagnostic: " + string.Join(Environment.NewLine, result.Diagnostics));
        }
    }),
    ("ReferenceArraysRequireCheckedNativeStoreSurface", () => {
        const string source = "using ScheduleOne.Testing; public static class Probe { public static Actor[] Read() => ArrayStore.ReadActors(); }";
        var author = CompilationSupport.Create("Probe", source, CompilationSupport.PlatformReferences.Add(Contracts.MonoReference));
        foreach (var replacement in new[] {
            ("void SetValue(Object value, int index)", "void MissingSetValue(Object value, int index)"),
            ("IntPtr il2cpp_class_get_element_class(", "IntPtr missing_class_get_element_class(")
        }) {
            byte[] incomplete = CompilationSupport.Emit("IncompleteNativeContract",
                RuntimeContracts.Il2Cpp.Replace(replacement.Item1, replacement.Item2, StringComparison.Ordinal));
            var result = new InteropCompiler().Lower(author,
                CompilationSupport.PlatformReferences.Add(MetadataReference.CreateFromImage(incomplete)));
            Assert(!result.Success && result.Diagnostics.Any(d => d.Id == "S1IC032"),
                "Incomplete native reference store surface was accepted or gave an unrelated error: " + string.Join(Environment.NewLine, result.Diagnostics));
        }
    }),
    ("ReferenceArraysCopyOverlapMixedStorageAndPartialFailure", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            Actor[] source = { new Employee(), new Customer(), null };
            ArrayStore.ReplaceActors(source);
            Actor first = source[0], second = source[1];
            Array.Copy(source, 0, source, 1, 2);
            if (!ReferenceEquals(source[1], first) || !ReferenceEquals(source[2], second)) return -1;
            Actor[] managed = new Actor[3];
            Array.Copy(source, managed, 3);
            if (managed.GetType() != typeof(Actor[]) || !ReferenceEquals(managed[2], second)) return -2;
            managed[0] = null;
            if (source[0] == null) return -3;
            Array.Copy(managed, 0, source, 0, 1);
            if (source[0] != null) return -4;
            Employee[] employees = new Employee[2];
            Actor[] destination = employees; ArrayStore.ReplaceActors(destination);
            bool failed = false;
            try { Array.Copy(source, 1, destination, 0, 2); }
            catch (InvalidCastException) { failed = true; }
            if (!failed || !ReferenceEquals(employees[0], first) || employees[1] != null) return -5;
            Array.Clear(source, 1, 2);
            if (source[1] != null || source[2] != null) return -6;
            return 41;
        } }
        """, 41)),
    ("ReferenceArraysNullConditionalLoadsAndWideIndices", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            Actor[] source = { new Employee() }; ArrayStore.ReplaceActors(source);
            if (!(source?[0] is Employee) || !ReferenceEquals(source[0L], source[^1])) return -1;
            try { var value = source?[2]; return -2; } catch (IndexOutOfRangeException) { }
            try { var value = source[-1L]; return -3; } catch (IndexOutOfRangeException) { }
            source = null;
            int calls = 0;
            var absent = source?[calls++];
            if (absent != null || calls != 0) return -4;
            return 43;
        } }
        """, 43)),
    ("ReferenceArraysNamedCopyArgumentsPreserveRolesAndOrder", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe {
            private static string order = "";
            private static Actor[] Destination(Actor[] value) { order += "d"; return value; }
            private static Employee[] Source(Employee[] value) { order += "s"; return value; }
            public static int Run() {
                Employee[] employees = { new Employee() }; Actor[] source = employees;
                ArrayStore.ReplaceActors(source);
                Actor[] destination = new Actor[1]; ArrayStore.ReplaceActors(destination);
                Array.Copy(destinationArray: Destination(destination), destinationIndex: 0,
                    sourceArray: Source(employees), sourceIndex: 0, length: 1);
                return order == "ds" && ReferenceEquals(destination[0], employees[0]) ? 47 : -1;
            }
        }
        """, 47)),
    ("ReferenceArraysMixedCopyUsesActualElementClasses", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            Actor[] source = { new Employee(), new Customer() }; ArrayStore.ReplaceActors(source);
            Employee[] managed = new Employee[2]; Actor[] view = managed;
            bool partial = false;
            try { Array.Copy(source, view, 2); } catch (InvalidCastException) { partial = true; }
            if (!partial || managed[0] == null || managed[1] != null || !ReferenceEquals(managed[0], source[0])) return -1;
            Customer[] unrelated = { null };
            Actor[] native = new Employee[1]; ArrayStore.ReplaceActors(native);
            try { Array.Copy(unrelated, native, 0); return -2; } catch (ArrayTypeMismatchException) { }
            Employee[] managedEmployees = new Employee[1];
            Actor[] nativeCustomers = new Customer[1]; ArrayStore.ReplaceActors(nativeCustomers);
            try { Array.Copy(nativeCustomers, managedEmployees, 0); return -3; } catch (ArrayTypeMismatchException) { }
            return 53;
        } }
        """, 53)),
    ("SerializedComponentArraysPreserveAliasesAndPrivateInitializers", () => Verify("""
        using System;
        using UnityEngine;
        public sealed class Fields : MonoBehaviour {
            public int[] Numbers = { 1, 2 };
            [SerializeField] private bool[] flags = { true, false };
            [NonSerialized] public byte[] Scratch = { 3 };
            public bool Flag => flags[0];
            public void SetFlag(bool value) { flags[0] = value; }
            public void Replace(int[] values) { Numbers = values; }
        }
        public static class Probe { public static int Run() {
            var fields = new GameObject().AddComponent<Fields>();
            var other = new Fields();
            int[] alias = fields.Numbers;
            alias[0] = 9;
            if (fields.Numbers[0] != 9 || other.Numbers[0] != 1 || !fields.Flag) return -1;
            var replacement = new[] { 4, 5 };
            fields.Replace(replacement);
            replacement[1] = 8;
            fields.SetFlag(false);
            if (fields.Numbers[1] != 8 || fields.Flag || fields.Scratch.GetType() != typeof(byte[])) return -2;
            return fields.Numbers[0] + fields.Numbers[1];
        } }
        """, 12)),
    ("SerializedComponentArrayFieldUsesNativeReferenceStorage", () => {
        var result = Lower("public sealed class Fields : UnityEngine.MonoBehaviour { public int[] Values = { 1 }; }");
        Assert(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var field = result.Compilation.GetTypeByMetadataName("Fields")!.GetMembers("Values").OfType<IFieldSymbol>().Single();
        Assert(field.Type is INamedTypeSymbol { Name: "Il2CppReferenceField", TypeArguments: [INamedTypeSymbol { Name: "Il2CppStructArray" }] },
            "Serialized array field did not receive native reference storage.");
        Assert(!result.Diagnostics.Any(diagnostic => diagnostic.Id == "S1IC016"), "Supported array was still marked managed-only.");
    }),
    ("SerializedComponentArraysHandleNullableSharedAndCollectionInitializers", () => Verify("""
        using System;
        using UnityEngine;
        public class Fields : MonoBehaviour {
            public int[]? First, Second;
            public int[] Values = [1, 2];
            public Fields() { First = [3, ..Values]; Second = First; }
        }
        public static class Probe { public static int Run() {
            var fields = new Fields();
            if (fields.First is null || fields.Second is null || !object.ReferenceEquals(fields.First, fields.Second)) return -1;
            fields.Second[1] = 7;
            return fields.First[0] + fields.First[1] + fields.First[2];
        } }
        """, 12)),
    ("SerializedComponentArrayBoundariesRemainDiagnosed", () => {
        foreach (var (source, code) in new[] {
            ("public class Base : UnityEngine.MonoBehaviour { public int[] Values; } public class Child : Base { public new int[] Values; }", "S1IC018"),
            ("public class Fields : UnityEngine.MonoBehaviour { public int[] Values; public void Change() { System.Array.Resize(ref Values, 3); } }", "S1IC017"),
            ("public class Fields : UnityEngine.MonoBehaviour { public int[] Values = new System.Collections.Generic.List<int>().ToArray(); }", "S1IC032")
        }) {
            var result = Lower(source);
            Assert(!result.Success && result.Diagnostics.Any(diagnostic => diagnostic.Id == code), "Missing array field boundary diagnostic " + code);
        }
    }),
    ("ArrayUnhandledClrBoundariesRejectSilentCopies", () => {
        foreach (string body in new[] {
            "return System.BitConverter.ToInt32(ArrayStore.Bytes, 0);"
        }) {
            var result = Lower("using ScheduleOne.Testing; public static class Probe { public static int Run() { " + body + " } }");
            Assert(!result.Success && result.Diagnostics.Any(diagnostic => diagnostic.Id == "S1IC032"),
                "Unhandled CLR array boundary accepted a silent copy: " + body);
        }
    }),
    ("ArrayNativeRefsAndViewsRequireOwnershipAdaptation", () => {
        foreach (string body in new[] {
            "ref int cell = ref values[0]; return cell;",
            "System.Span<int> span = values; return span[0];",
            "var span = values.AsSpan(); return span[0];",
            "System.Collections.Generic.ICollection<int> view = values; return view.IsReadOnly ? 1 : 0;",
            "return Observe(values[0]);"
        }) {
            var result = Lower("using System; using ScheduleOne.Testing; public static class Probe { " +
                "static int Observe(in int value) => value; public static int Run() { var values = ArrayStore.Values; " + body + " } }");
            Assert(!result.Success && result.Diagnostics.Any(diagnostic => diagnostic.Id == "S1IC033"),
                "An unadapted native memory lifetime or interface contract was accepted: " + body);
        }
    }),
    ("ArrayNativeStorageSharesSourceAndGameMutations", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe {
            private static int[] held;
            private static int[] Pass(int[] values) => values;
            public static int Run() {
                int[] values = { 1, 2, 3 };
                held = Pass(values);
                ArrayStore.Replace(held);
                values[0] = 7;
                if (ArrayStore.Values[0] != 7) return -1;
                ArrayStore.Values[1] += 8;
                int[] read = ArrayStore.Read(null);
                if (held[1] != 10 || !object.ReferenceEquals(read, held)) return -2;
                read[2]++;
                return values[0] + values[1] + values[2];
            }
        }
        """, 21)),
    ("ArrayNativeReadDimensionsEnumerationAndIdentity", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            ArrayStore.Replace(new[] { 2, 3, 4 });
            var values = ArrayStore.Read(null);
            int[] alias = values;
            int total = 0; foreach (int value in alias) total += value;
            if (values.Length != 3 || values.LongLength != 3 || values.Rank != 1 || values.GetLength(0) != 3) return -1;
            if (values != ArrayStore.Read(null) || !object.ReferenceEquals(alias, ArrayStore.Values)) return -2;
            if (!values.Equals(ArrayStore.Read(null)) || values.GetHashCode() != ArrayStore.Read(null).GetHashCode()) return -3;
            object first = values, second = ArrayStore.Read(null);
            if (!object.Equals(first, second) || !first.Equals(second) || first.GetHashCode() != second.GetHashCode()) return -4;
            return total;
        } }
        """, 9)),
    ("ArrayNativePrimitiveElementKinds", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            var bytes = new byte[] { 3, 4 };
            bool[] flags = { true, false };
            ArrayStore.Bytes = bytes;
            ArrayStore.Flags = flags;
            bytes[1]++;
            ArrayStore.Flags[0] = false;
            if (flags[0] || ArrayStore.Bytes[1] != 5) return -1;
            return bytes[0] + bytes[1];
        } }
        """, 8)),
    ("ArrayNativeConnectedCastsAndPatterns", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            int[] values = new[] { 4, 5 }; ArrayStore.Replace(values);
            object boxed = values;
            int[] cast = (int[])boxed;
            var narrowed = boxed as int[];
            if (boxed is not int[] matched || !object.ReferenceEquals(matched, narrowed)) return -1;
            if (boxed is bool[]) return -2;
            cast[0] = 7;
            return boxed switch { int[] items when items.Length == 2 => items[0] + items[1], _ => -3 };
        } }
        """, 12)),
    ("ArrayNativeCopyOverlapAndClear", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            var values = new[] { 1, 2, 3, 4, 5 };
            ArrayStore.Replace(values);
            Array.Copy(values, 0, values, 1, 4);
            if (values[0] != 1 || values[1] != 1 || values[4] != 4) return -1;
            Array.Copy(values, 1, values, 0, 4);
            if (values[0] != 1 || values[1] != 2 || values[3] != 4) return -2;
            Array.Clear(values, 1, 2);
            return ArrayStore.Values[0] + values[1] + values[2] + values[3] + values[4];
        } }
        """, 9)),
    ("ArrayCopyAcrossManagedBoundaryRemainsAnExplicitCopy", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            ArrayStore.Replace(new[] { 1, 2, 3 });
            var native = ArrayStore.Values;
            var managed = new int[3];
            Array.Copy(native, managed, 3);
            managed[0] = 8;
            if (native[0] != 1 || managed.GetType() != typeof(int[])) return -1;
            Array.Copy(managed, native, 3);
            if (ArrayStore.Values[0] != 8) return -2;
            native[1] = 9;
            native.CopyTo(managed, 0);
            if (managed[1] != 9) return -3;
            return managed[0] + managed[1] + managed[2];
        } }
        """, 20)),
    ("ArrayNativeNamedArgumentsPreserveEvaluationOrder", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe {
            private static int order;
            private static int[] Source() { order = order * 10 + 1; return ArrayStore.Values; }
            private static int[] Destination() { order = order * 10 + 2; return ArrayStore.Values; }
            private static int Index() { order = order * 10 + 3; return 1; }
            public static int Run() {
                ArrayStore.Replace(new[] { 1, 2, 3 });
                Array.Copy(destinationArray: Destination(), sourceArray: Source(), length: 2, destinationIndex: Index(), sourceIndex: 0);
                if (ArrayStore.Values[1] != 1 || ArrayStore.Values[2] != 2) return -1;
                return order;
            }
        }
        """, 213)),
    ("ArrayNativeCloneAndResizeOwnIndependentStorage", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            int[] values = new[] { 1, 2 };
            ArrayStore.Replace(values);
            int[] clone = (int[])values.Clone();
            ArrayStore.Replace(clone);
            clone[0] = 8;
            if (values[0] != 1 || object.ReferenceEquals(clone, values)) return -1;
            Array.Resize(ref clone, 4);
            if (clone.Length != 4 || clone[0] != 8 || clone[2] != 0 || ArrayStore.Values.Length != 2) return -2;
            clone[0] = 9;
            if (ArrayStore.Values[0] != 8) return -3;
            var before = clone;
            Array.Resize(ref clone, 4);
            if (!object.ReferenceEquals(before, clone)) return -4;
            return clone[0];
        } }
        """, 9)),
    ("ArrayNativeNullBoundsAndLengthExceptions", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            int[] values = new int[2]; ArrayStore.Replace(values);
            int checks = 0;
            try { values[-1] = 1; } catch (IndexOutOfRangeException) { checks++; }
            try { _ = values[2]; } catch (IndexOutOfRangeException) { checks++; }
            int[] absent = null; ArrayStore.Replace(absent);
            try { _ = absent.Length; } catch (NullReferenceException) { checks++; }
            try { Array.Copy(absent, values, 1); } catch (ArgumentNullException) { checks++; }
            try { Array.Resize(ref values, -1); } catch (ArgumentOutOfRangeException) { checks++; }
            int negative = -1;
            try { values = new int[negative]; } catch (OverflowException) { checks++; }
            return checks;
        } }
        """, 6)),
    ("ArrayNativeAssignmentChecksFollowRightHandSide", () => Verify("""
        using System;
        using ScheduleOne.Testing;
        public static class Probe {
            private static int calls;
            private static int Value() { calls++; return 7; }
            public static int Run() {
                int[] values = new int[1]; ArrayStore.Replace(values);
                try { values[1] = Value(); } catch (IndexOutOfRangeException) { }
                if (calls != 1) return -1;
                values = null;
                try { values[0] = Value(); } catch (NullReferenceException) { }
                if (calls != 2) return -2;
                try { values[0] += Value(); } catch (NullReferenceException) { }
                return calls;
            }
        }
        """, 2)),
    ("ArrayManagedAliasesRemainShared", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            var values = new[] { 1 }; int[] alias = values; alias[0] = 9;
            ArrayStore.Replace("unrelated overload");
            if (nameof(ArrayStore.Values) != "Values") return -1;
            ArrayStore.Replace(new[] { 9 });
            if (ArrayStore.Values[0] != 9) return -2;
            return values[0];
        } }
        """, 9)),
    ("ArrayEqualityHelpersPreserveManagedVirtualDispatch", () => Verify("""
        using System;
        public class ValueEquality {
            public override bool Equals(object other) => other is string text && text == "match";
            public override int GetHashCode() => 37;
        }
        public class BaseIdentity {
            public override bool Equals(object other) => base.Equals(other);
            public override int GetHashCode() => base.GetHashCode();
        }
        public static class Probe { public static int Run() {
            object value = new ValueEquality();
            if (!object.Equals(value, "match") || !value.Equals("match") || value.GetHashCode() != 37) return -1;
            if (object.Equals(value, null) || object.Equals(null, value) || !object.Equals(null, null)) return -2;
            object identity = new BaseIdentity();
            if (!identity.Equals(identity) || identity.Equals(new BaseIdentity()) ||
                identity.GetHashCode() != System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(identity)) return -3;
            return 1;
        } }
        """, 1)),
    ("DictionaryAuthorComparerPreservesSemanticsAndIdentity", () => Verify("""
        using System;
        using System.Collections.Generic;
        using ScheduleOne.Testing;
        public class CustomComparer : IEqualityComparer<string> {
            public int Calls;
            public bool Equals(string left, string right) { Calls++; return left.Length == right.Length; }
            public int GetHashCode(string value) { Calls++; return value.Length; }
        }
        public static class Probe { public static int Run() {
            var comparer = new CustomComparer();
            var values = new Dictionary<string, int>(comparer) { ["one"] = 7 };
            DictionaryStore.ReplaceScores(values);
            GC.Collect(); GC.WaitForPendingFinalizers();
            if (!DictionaryStore.Scores.ContainsKey("two")) return -1;
            DictionaryStore.Scores["two"] = 8;
            if (values.Count != 1 || values["one"] != 8 || comparer.Calls == 0) return -2;
            if (!object.ReferenceEquals(comparer, values.Comparer) ||
                !object.ReferenceEquals(comparer, DictionaryStore.ReadScores().Comparer)) return -3;
            return 8;
        } }
        """, 8)),
    ("DictionaryOpaqueNativeKeyComparerAndLinq", () => Verify("""
        using System.Collections.Generic;
        using System.Linq;
        using ScheduleOne.Testing;
        public class NamedComparer : IEqualityComparer<Actor> {
            public bool Equals(Actor left, Actor right) => left.Name == right.Name;
            public int GetHashCode(Actor value) => value.Name.GetHashCode();
        }
        public static class Probe { public static int Run() {
            IEqualityComparer<Actor> comparer = new NamedComparer();
            var values = new[] { new Actor { Name = "key" } }.ToDictionary(actor => actor, actor => 9, comparer);
            DictionaryStore.ReplaceRanks(values);
            if (!object.ReferenceEquals(comparer, DictionaryStore.Ranks.Comparer)) return -1;
            return DictionaryStore.Ranks[new Actor { Name = "key" }];
        } }
        """, 9)),    ("DictionaryTypedViewsPatternsAndNativeKeyLinq", () => Verify("""
        using System.Collections.Generic;
        using System.Linq;
        using ScheduleOne.Testing;
        public static class Probe {
            public static int Run() {
                Dictionary<string,int>.KeyCollection keys = DictionaryStore.Scores.Keys;
                Dictionary<string,int>.KeyCollection.Enumerator iterator = keys.GetEnumerator();
                Dictionary<string,int>.Enumerator entries = DictionaryStore.Scores.GetEnumerator();
                if (!iterator.MoveNext() || !entries.MoveNext() || iterator.Current != entries.Current.Key) return -1;
                object opaque = DictionaryStore.Scores;
                if (!(opaque is Dictionary<string,int> recovered)) return -2;
                if (!object.ReferenceEquals(recovered, opaque as Dictionary<string,int>)) return -3;
                if (!object.ReferenceEquals(recovered, (Dictionary<string,int>)opaque)) return -4;
                var converted = new[] { Actor.Current }.ToDictionary(actor => actor, actor => 12);
                DictionaryStore.ReplaceRanks(converted);
                return DictionaryStore.Ranks[(Employee)Actor.Current];
            }
        }
        """, 12)),
    ("DictionaryLiveAliasesObserveSourceAndGameMutation", DictionaryTests.LiveAliasesObserveSourceAndGameMutation),
    ("DictionaryClrExceptionsForNullDuplicateAndMissingKeys", DictionaryTests.ClrExceptionsForNullDuplicateAndMissingKeys),
    ("DictionaryEnumerationInvalidatesAndCursorCopiesAreIndependent", DictionaryTests.EnumerationInvalidatesAndCursorCopiesAreIndependent),
    ("DictionaryKeysAndValuesAreLiveReadOnlyViews", DictionaryTests.KeysAndValuesAreLiveReadOnlyViews),
    ("DictionaryNongenericDictionaryAndCollectionContracts", DictionaryTests.NongenericDictionaryAndCollectionContracts),
    ("DictionaryUnrelatedManagedDictionaryStaysManaged", DictionaryTests.UnrelatedManagedDictionaryStaysManaged),
    ("DictionaryNativeKeysUseGameIdentityAndEquality", DictionaryTests.NativeKeysUseGameIdentityAndEquality),
    ("DictionarySourceCreatedDictionaryFlowsIntoNativeSlot", DictionaryTests.SourceCreatedDictionaryFlowsIntoNativeSlot),
    ("DictionarySourceCreatedNativeKeyDictionaryFlowsIntoNativeSlot", DictionaryTests.SourceCreatedNativeKeyDictionaryFlowsIntoNativeSlot),
    ("PublicizerAccessesOriginalPrivateMembers", PublicizerTests.RuntimeAccess),
    ("NativeDelegateEventAttributesArePreservedOrDiagnosed", () => {
        var result = Lower("""
            using System;
            using UnityEngine.Events;
            [AttributeUsage(AttributeTargets.Method)] public sealed class AccessorMarkerAttribute : Attribute { }
            public class Signal {
                [field: NonSerialized]
                [method: AccessorMarker]
                public event UnityAction Changed;
                public event UnityAction Custom {
                    [AccessorMarker] add { Changed += value; }
                    [AccessorMarker] remove { Changed -= value; }
                }
            }
            """);
        Assert(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var members = result.Compilation.GetTypeByMetadataName("Signal")!.GetMembers();
        Assert(members.OfType<IFieldSymbol>().Single(field => field.Name == "Changed").GetAttributes()
            .Any(attribute => attribute.AttributeClass?.Name == "NonSerializedAttribute"), "Field attribute was lost.");
        Assert(members.OfType<IMethodSymbol>().Count(method => method.Name.StartsWith("__S1InteropEvent_") &&
            method.GetAttributes().Any(attribute => attribute.AttributeClass?.Name == "AccessorMarkerAttribute")) == 4,
            "Accessor attributes were lost.");
        var unsupported = Lower("public class Signal { [System.Obsolete] public event UnityEngine.Events.UnityAction Changed; }");
        Assert(!unsupported.Success && unsupported.Diagnostics.Any(diagnostic => diagnostic.Id == "S1IC022"),
            "Event-only metadata must not be silently discarded.");
    }),
    ("NativeDelegateAbstractExplicitAndStructEvents", () => Verify("""
        using UnityEngine.Events;
        public interface ISignal { event UnityAction Changed; }
        public abstract class SignalBase { public abstract event UnityAction Changed; }
        public sealed class Signal : SignalBase, ISignal {
            public override event UnityAction Changed;
            event UnityAction ISignal.Changed { add { Changed += value; } remove { Changed -= value; } }
            public void Raise() => Changed?.Invoke();
        }
        public struct ValueSignal {
            public event UnityAction Changed;
            public static event UnityAction Global;
            public void Raise() => Changed?.Invoke();
            public static void RaiseGlobal() => Global?.Invoke();
        }
        public static class Probe {
            static int calls;
            static void Hit() { calls++; }
            public static int Run() {
                var owner = new Signal();
                SignalBase signal = owner;
                signal.Changed += Hit;
                owner.Raise();
                ((ISignal)owner).Changed -= Hit;
                owner.Raise();
                var value = new ValueSignal();
                value.Changed += Hit;
                var copy = value;
                value.Changed -= Hit;
                value.Raise(); copy.Raise();
                ValueSignal.Global += Hit;
                ValueSignal.RaiseGlobal();
                ValueSignal.Global -= Hit;
                ValueSignal.RaiseGlobal();
                return calls;
            }
        }
        """, 3)),
    ("NativeDelegateAuthoredEventsPreserveMulticastRemoval", () => Verify("""
        using UnityEngine.Events;
        public sealed class Signal {
            public event UnityAction Changed;
            public void Raise() => Changed?.Invoke();
        }
        public static class Probe {
            static int calls;
            static void First() { calls += 1; }
            static void Second() { calls += 10; }
            public static int Run() {
                var signal = new Signal();
                signal.Changed += First;
                signal.Changed += Second;
                signal.Changed += First;
                signal.Raise();
                signal.Changed -= First;
                signal.Raise();
                signal.Changed -= Second;
                signal.Changed -= First;
                signal.Raise();
                return calls;
            }
        }
        """, 23)),
    ("NativeDelegateCustomAndInterfaceEvents", () => Verify("""
        using UnityEngine.Events;
        public interface ISignal { event UnityAction Changed; }
        public sealed class Signal : ISignal {
            private event UnityAction storage;
            public event UnityAction Changed { add { storage += value; } remove { storage -= value; } }
            public void Raise() => storage?.Invoke();
        }
        public static class Probe {
            static int calls;
            static void Hit() { calls++; }
            public static int Run() {
                var owner = new Signal();
                ISignal signal = owner;
                signal.Changed += Hit;
                owner.Raise();
                signal.Changed -= Hit;
                owner.Raise();
                return calls;
            }
        }
        """, 1)),
    ("AbstractComponentContractsDoNotAllocateNativeAbstractSlots", () => {
        var result = Lower("""
            public abstract class CounterBase : UnityEngine.MonoBehaviour {
                public abstract int Read();
                public abstract int Value { get; set; }
                public abstract event UnityEngine.Events.UnityAction Changed;
            }
            """);
        Assert(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var type = result.Compilation.GetTypeByMetadataName("CounterBase")!;
        Assert(type.IsAbstract, "The managed abstract contract must remain abstract.");
        var methods = type.GetMembers().OfType<IMethodSymbol>().Where(method => method.IsAbstract).ToArray();
        Assert(methods.Length == 5 && methods.All(method => method.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == "Il2CppInterop.Runtime.Attributes.HideFromIl2CppAttribute")),
            "An abstract method or accessor would be allocated as an unsafe native vtable slot.");
    }),
    ("CoroutineStartAbstractOverridePreservesManagedDispatch", () => Verify("""
        using System.Collections;
        using UnityEngine;
        public abstract class CoroutineBase : MonoBehaviour {
            protected abstract IEnumerator Start();
            public IEnumerator Run() => Start();
        }
        public sealed class Counter : CoroutineBase {
            [System.NonSerialized] public int Calls;
            protected override IEnumerator Start() { Calls++; yield return null; Calls++; }
        }
        public static class Probe { public static int Run() {
            var counter = new GameObject().AddComponent<Counter>();
            var iterator = counter.Run(); iterator.MoveNext(); iterator.MoveNext();
            return counter.Calls;
        } }
        """, 2)),
    ("ComponentInheritancePreservesBaseConstructionAndVirtualDispatch", () => Verify("""
        using UnityEngine;
        public abstract partial class CounterBase : MonoBehaviour {
            [System.NonSerialized] protected int value = 2;
            public static int Initialization;
            protected CounterBase() { value += 3; }
            public abstract int Read();
        }
        public abstract partial class CounterBase { static CounterBase() { Initialization++; } }
        public class CounterMiddle : CounterBase {
            public CounterMiddle() { value++; }
            public override int Read() => value;
        }
        public sealed class Counter : CounterMiddle {
            public Counter() { value += 2; }
            public override int Read() => base.Read() + 1;
        }
        public static class Probe { public static int Run() {
            CounterBase component = new GameObject().AddComponent<Counter>();
            return CounterBase.Initialization == 1 ? component.Read() : -1;
        } }
        """, 9)),
    ("ScalarListPropertiesAndExplicitCopies", () => Verify("""
        using System.Collections.Generic;
        using System.Linq;
        using ScheduleOne.Testing;
        public static class Probe {
            static List<int> Values { get; set; } = ScalarStore.Numbers;
            public static int Run() {
                List<int> Local() => Values;
                var alias = Local();
                var copy = new List<int>(alias);
                var queryCopy = alias.ToList();
                copy.Add(4); queryCopy.Add(5);
                if (Values.Count != 3 || copy.GetType() != typeof(List<int>)) return -1;
                List<int> slice = Values.GetRange(0, 1);
                slice.Add(6);
                if (slice.Count != 2 || Values.Count != 3) return -2;
                ScalarStore.Replace(alias.Where(value => value > 1).ToList());
                if (ScalarStore.Numbers.Count != 2) return -3;
                ScalarStore.Replace(ScalarStore.Words.ConvertAll(word => word.Length));
                return ScalarStore.Numbers[0];
            }
        }
        """, 5)),
    ("ScalarListStorageFlowPreservesLiveAliasesAndManagedLists", () => Verify("""
        using System;
        using System.Collections.Generic;
        using ScheduleOne.Testing;
        public static class Probe {
            static List<int> field;
            static List<int> Read() => field;
            static void Assign(List<int> values) { field = values; }
            public static int Run() {
                List<int> local = ScalarStore.Numbers;
                Assign(local);
                Read().Sort();
                Read().Add(4);
                if (ScalarStore.Numbers[0] != 1 || !object.ReferenceEquals(local, Read())) return -1;
                List<string> words = ScalarStore.Words;
                words.Add("second");
                if (ScalarStore.Words.Count != 2) return -2;
                var managed = new List<string> { "private" };
                if (managed.GetType() != typeof(List<string>)) return -3;
                var replacement = new List<int> { 8 };
                ScalarStore.Replace(replacement);
                replacement.Add(9);
                return ScalarStore.Numbers.Count == 2 && local.Count == 4 ? 1 : -4;
            }
        }
        """, 1)),
    ("ScalarListNonGenericValuesAndEnumeration", () => Verify("""
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using ScheduleOne.Testing;
        public static class Probe {
            public static int Run() {
                List<int> numbers = ScalarStore.Numbers;
                IList opaque = numbers;
                opaque.Add(7);
                if (!opaque.Contains(7) || opaque.Contains("7") || opaque.Contains(null)) return -1;
                try { opaque.Add(null); return -2; } catch (ArgumentNullException) { }
                var iterator = numbers.GetEnumerator();
                ScalarStore.Numbers.Add(8);
                try { iterator.MoveNext(); return -3; } catch (InvalidOperationException) { }
                return numbers.Count == 5 ? 1 : -4;
            }
        }
        """, 1)),
    ("CoroutineNativeResetPreservesNonDisposableShape", () => Verify("""
        using System;
        using System.Collections;
        using ScheduleOne.Testing;
        public static class Probe {
            public static int Run() {
                IEnumerator routine = RoutineHost.CreatePlain();
                if (routine is IDisposable || !routine.MoveNext() || routine.MoveNext()) return -1;
                routine.Reset();
                return routine.MoveNext() ? 1 : -2;
            }
        }
        """, 1)),
    ("CoroutineNativeDisposalRunsIteratorFinally", () => Verify("""
        using System;
        using System.Collections;
        using ScheduleOne.Testing;
        public static class Probe {
            public static int Run() {
                IEnumerator routine = RoutineHost.Create();
                routine.MoveNext();
                if (!(routine is IDisposable disposable)) return -1;
                disposable.Dispose();
                return RoutineHost.Disposals;
            }
        }
        """, 1)),
    ("CoroutineNativeConditionalAccess", () => Verify("""
        using System.Collections;
        using ScheduleOne.Testing;
        public static class Probe {
            public static int Run() {
                Actor missing = null;
                IEnumerator absent = missing?.Routine();
                var present = new Actor();
                IEnumerator routine = present?.Routine();
                return absent == null && routine.MoveNext() && routine.Current is Employee ? 1 : -1;
            }
        }
        """, 1)),
    ("CoroutineNativeReadPreservesIdentityAndStorage", () => Verify("""
        using System.Collections;
        using ScheduleOne.Testing;
        public static class Probe {
            static IEnumerator Read() => RoutineHost.Read();
            public static int Run() {
                IEnumerator routine = RoutineHost.Create();
                RoutineHost.Active = routine;
                var again = RoutineHost.Read();
                if (!object.ReferenceEquals(routine, again) || !object.ReferenceEquals(Read(), RoutineHost.Active)) return -1;
                if (!routine.MoveNext() || !(routine.Current is Employee)) return -2;
                if (!again.MoveNext() || again.Current != null || routine.MoveNext()) return -3;
                again = RoutineHost.CreatePlain();
                if (!again.MoveNext()) return -5;
                RoutineHost.Active = null;
                return Read() == null ? 1 : -4;
            }
        }
        """, 1)),
    ("CoroutineNativeReadRecoversManagedOriginal", () => Verify("""
        using System.Collections;
        using ScheduleOne.Testing;
        public static class Probe {
            static IEnumerator Routine() { yield return null; }
            public static int Run() {
                var original = Routine();
                RoutineHost.Start(original);
                IEnumerator returned = RoutineHost.Read();
                RoutineHost.Stop(returned);
                return object.ReferenceEquals(original, returned) && !RoutineHost.Running ? 1 : -1;
            }
        }
        """, 1)),
    ("CoroutineStartPreservesManagedCallsAndNameof", () => Verify("""
        using System.Collections;
        using UnityEngine;
        public sealed class Counter : MonoBehaviour {
            [System.NonSerialized] public int Calls;
            public IEnumerator Start() { Calls++; yield return null; Calls++; }
            public string CallbackName => nameof(Start);
            private int __S1InteropManagedStart;
        }
        public static class Probe { public static int Run() {
            var counter = new GameObject().AddComponent<Counter>();
            System.Func<IEnumerator> start = counter.Start;
            var iterator = start(); iterator.MoveNext(); iterator.MoveNext();
            return counter.CallbackName == "Start" ? counter.Calls : -1;
        } }
        """, 2)),
    ("CoroutineConversionPreservesStopIdentity", () => Verify("""
        using System.Collections;
        using ScheduleOne.Testing;
        public static class Probe {
            static int count;
            static IEnumerator Routine() { count++; yield return null; count++; }
            public static int Run() {
                var routine = Routine();
                RoutineHost.Start(routine);
                RoutineHost.Stop(routine);
                return !RoutineHost.Running ? count : -1;
            }
        }
        """, 1)),
    ("CoroutineConversionDistinguishesInstancesAndPreservesNull", () => Verify("""
        using System.Collections;
        using ScheduleOne.Testing;
        public static class Probe {
            static IEnumerator Routine() { yield return null; }
            public static int Run() {
                RoutineHost.Start(Routine());
                RoutineHost.Stop(Routine());
                if (!RoutineHost.Running) return -1;
                RoutineHost.Start(null);
                return RoutineHost.Running ? -1 : 1;
            }
        }
        """, 1)),
    ("PublicizerReferenceShapeIsDeterministicAndNotExecutable", PublicizerTests.ReferenceShape),
    ("SharedRuntimeAcrossMods", SharedRuntimeTests.Run),
    ("SharedRuntimeMismatchRejectedBeforeModCode", SharedRuntimeTests.RejectMismatchedRuntime),
    ("SharedRuntimeSupportsOlderAuthorLanguage", SharedRuntimeTests.OlderAuthorLanguage),
    ("AuthoringReferencePairMatches", () => AuthoringReferenceTests.Run("match")),
    ("AuthoringReferenceMissingRejected", () => AuthoringReferenceTests.Run("missing")),
    ("AuthoringReferenceStaleRejected", () => AuthoringReferenceTests.Run("stale")),
    ("InstallationVersionsAndGenerationMatch", () => InstallationVerificationTests.Run("match")),
    ("InstallationVersionsMustMatch", () => InstallationVerificationTests.Run("version")),
    ("InstallationRejectsStaleGeneration", () => InstallationVerificationTests.Run("stale")),
    ("InstallationRejectsMissingGenerationRecord", () => InstallationVerificationTests.Run("missing")),
    ("InstallationRejectsAmbiguousGenerationRecord", () => InstallationVerificationTests.Run("duplicate")),
    ("DirectMembersOverloadsGenerics", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            var value = new Actor(); value.Name = "hello";
            return value.Score(value.Name) + value.Score(3) + value.Echo(2);
        } }
        """, 11)),
    ("AliasesNestedTypesAndArbitraryLibraries", () => Verify("""
        using A = ScheduleOne.Testing.Actor;
        using static Unrelated.Library.Tool;
        public static class Probe { public static int Run() {
            var settings = new A.Settings { Value = 3 };
            return Twice(settings.Value) + (int)global::ScheduleOne.Testing.Actor.Kind.Employee;
        } }
        """, 7)),
    ("NativeAsAndPatternSingleEvaluation", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            Actor.Calls = 0;
            if (Actor.Next() is Employee employee && employee.Salary == 7) {
                var again = Actor.Next() as Employee;
                return again.Salary + Actor.Calls;
            }
            return -1;
        } }
        """, 9)),
    ("ExplicitCastNativeIdentity", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            return ((Employee)(object)Actor.Current).Salary;
        } }
        """, 7)),
    ("ConstrainedGenericNativeCast", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe {
            static T Convert<T>(object value) where T : Actor => (T)value;
            public static int Run() => Convert<Employee>(Actor.Current).Salary;
        }
        """, 7)),
    ("NativeInterfaceExplicitAndImplicitConversions", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe {
            static int Read(IActor value) => value.ReadValue();
            public static int Run() {
                IActor implicitView = Actor.Current;
                var explicitView = (IActor)(object)Actor.Current;
                return implicitView.ReadValue() + explicitView.ReadValue() + Read(Actor.Current);
            }
        }
        """, 15)),
    ("NativeReferenceIdentitySurvivesRetyping", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            Actor value = Actor.Current;
            var employee = (Employee)(object)value;
            IActor view = value;
            return (object)value == employee && object.ReferenceEquals(value, view) && !((object)value != employee) ? 1 : 0;
        } }
        """, 1)),
    ("ManagedValueAndDelegateEqualityUnchanged", () => Verify("""
        public static class Probe {
            static void Hit() { }
            public static int Run() {
                string a = new string('x', 2), b = new string('x', 2);
                System.Action first = Hit, second = Hit;
                object x = new object(), y = new object();
                return a == b && first == second && x != y && !object.ReferenceEquals(x, y) ? 1 : 0;
            }
        }
        """, 1)),
    ("ReferenceEqualityNamedArgumentsPreserveEvaluationOrder", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe {
            static int order;
            static object First() { order = order * 10 + 1; return Actor.Current; }
            static object Second() { order = order * 10 + 2; return (Employee)(object)Actor.Current; }
            public static int Run() => object.ReferenceEquals(objB: Second(), objA: First()) ? order : -1;
        }
        """, 21)),
    ("UserDefinedConversionIsPreserved", () => Verify("""
        using ScheduleOne.Testing;
        public struct Token {
            public static explicit operator Employee(Token value) => new Employee { Salary = 13 };
        }
        public static class Probe { public static int Run() => ((Employee)new Token()).Salary; }
        """, 13)),
    ("IdentityCastKeepsExistingWrapper", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            var original = Actor.Current;
            return object.ReferenceEquals(original, (Actor)(object)original) ? 1 : 0;
        } }
        """, 1)),
    ("NullCastPreserved", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            object value = null;
            return (Employee)value is null ? 1 : 0;
        } }
        """, 1)),
    ("InvalidCastThrowsOnce", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            Actor.Calls = 0;
            try { var invalid = (Customer)Actor.Next(); return -1; }
            catch (System.InvalidCastException) { return Actor.Calls; }
        } }
        """, 1)),
    ("LiveCollectionMutation", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            var list = Actor.All;
            list.Add(new Employee());
            int count = 0;
            foreach (var value in list) { count++; }
            return Actor.All.Count + count;
        } }
        """, 4)),
    ("NativeListBoundariesPreserveIdentityAndAliasing", () => Verify("""
        using ScheduleOne.Testing;
        using System.Collections.Generic;
        public static class Probe {
            public static List<Actor> Read() => Actor.GetAll();
            static void Write(List<Actor> value) => Actor.SetAll(value);
            public static int Run() {
                var created = new List<Actor> { new Actor() };
                Write(created);
                var alias = Read();
                ((IList<Actor>)alias).Add(new Employee());
                created.Add(new Actor());
                return object.ReferenceEquals(created, Actor.All) && object.ReferenceEquals(created, alias) ? Actor.All.Count : -1;
            }
        }
        """, 3)),
    ("NativeListEnumeratorDetectsGameMutation", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            var enumerator = Actor.All.GetEnumerator();
            enumerator.MoveNext();
            Actor.AddActor();
            try { enumerator.MoveNext(); return -1; }
            catch (System.InvalidOperationException) { return 1; }
        } }
        """, 1)),
    ("NativeListEnumeratorCopiesHaveIndependentCursor", () => Verify("""
        using ScheduleOne.Testing;
        using System.Collections.Generic;
        public static class Probe { public static int Run() {
            var list = new List<Actor> { new Actor { Name = "first" }, new Actor { Name = "second" } };
            List<Actor>.Enumerator first = list.GetEnumerator();
            var second = first;
            first.MoveNext(); first.MoveNext(); second.MoveNext();
            return first.Current.Name == "second" && second.Current.Name == "first" ? 1 : 0;
        } }
        """, 1)),
    ("NativeListSelfInsertionAndManagedPredicates", () => Verify("""
        using ScheduleOne.Testing;
        using System.Collections.Generic;
        public static class Probe { public static int Run() {
            var list = new List<Actor> { new Actor { Name = "b" }, new Actor { Name = "a" } };
            list.InsertRange(1, list);
            list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            int found = list.FindAll(a => a.Name == "a").Count;
            int removed = list.RemoveAll(a => a.Name == "b");
            return found + removed + list.Count;
        } }
        """, 6)),
    ("NativeListToListOwnsNewStorage", () => Verify("""
        using ScheduleOne.Testing;
        using System.Collections.Generic;
        using System.Linq;
        public static class Probe { public static int Run() {
            List<Actor> copy = Actor.All.Where(a => a != null).ToList();
            copy.Clear();
            return Actor.All.Count;
        } }
        """, 1)),
    ("NativeListCastsAndPatternsRecoverOpaqueNativeList", () => Verify("""
        using ScheduleOne.Testing;
        using System.Collections.Generic;
        public static class Probe { public static int Run() {
            object opaque = Actor.OpaqueAll();
            var explicitView = (List<Actor>)opaque;
            var asView = opaque as List<Actor>;
            return opaque is List<Actor> pattern && object.ReferenceEquals(explicitView, asView) &&
                object.ReferenceEquals(pattern, Actor.All) && object.ReferenceEquals(opaque, pattern) ? 1 : 0;
        } }
        """, 1)),
    ("NativeListConvertAllSupportsBothKindsOfResult", () => Verify("""
        using ScheduleOne.Testing;
        using System.Collections.Generic;
        public static class Probe { public static int Run() {
            List<Employee> game = Actor.All.ConvertAll(value => (Employee)value);
            List<string> managed = Actor.All.ConvertAll(value => value.Name);
            return game[0].Salary + managed[0].Length;
        } }
        """, 12)),
    ("NativeListEmptySortInvalidatesEnumerator", () => Verify("""
        using ScheduleOne.Testing;
        using System.Collections.Generic;
        public static class Probe { public static int Run() {
            var list = new List<Actor>();
            var enumerator = list.GetEnumerator();
            list.Sort((a, b) => 0);
            try { enumerator.MoveNext(); return -1; }
            catch (System.InvalidOperationException) { return 1; }
        } }
        """, 1)),
    ("TypedForeachRetypesNativeElements", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            int salary = 0;
            foreach (Employee employee in Actor.All) { salary += employee.Salary; }
            return salary;
        } }
        """, 7)),
    ("NativeLinqIsLazyAndObservesLiveChanges", () => Verify("""
        using ScheduleOne.Testing;
        using System.Linq;
        public static class Probe { public static int Run() {
            var registry = Actor.All;
            var query = registry.Where(actor => actor.Name == "actor").Select(actor => actor.Name.Length);
            registry.Add(new Actor());
            return query.Sum() + Enumerable.Count(Actor.All);
        } }
        """, 12)),
    ("NativeLinqPreservesNullSourceException", () => Verify("""
        using ScheduleOne.Testing;
        using System.Linq;
        public static class Probe { public static int Run() {
            Actor.All = null;
            try { return Actor.All.Count(); }
            catch (System.ArgumentNullException) { return 1; }
        } }
        """, 1)),
    ("NativeLinqTypeFiltersUseNativeIdentity", () => Verify("""
        using ScheduleOne.Testing;
        using System.Linq;
        public static class Probe { public static int Run() {
            object[] managed = { Actor.Current, null, "wrong" };
            return Actor.All.Cast<Employee>().Single().Salary + managed.OfType<Employee>().Single().Salary;
        } }
        """, 14)),
    ("ComponentConstructorsPreserveManagedState", () => Verify("""
        using UnityEngine;
        public sealed class Counter : MonoBehaviour {
            public int Value = 4;
            public Counter() { Value += 3; }
            public int Read() => Value;
        }
        public static class Probe { public static int Run() => new GameObject().AddComponent<Counter>().Read(); }
        """, 7)),
    ("ComponentManagedHelpersRemainCallable", () => Verify("""
        using UnityEngine;
        using System.Collections.Generic;
        public sealed class Counter : MonoBehaviour {
            public List<string> Values => new List<string> { "retained" };
            public List<string> GetValues() => Values;
        }
        public static class Probe { public static int Run() => new GameObject().AddComponent<Counter>().GetValues().Count; }
        """, 1)),
    ("ManagedCollectionsStayManaged", () => Verify("""
        using ScheduleOne.Testing;
        using System.Collections.Generic;
        public static class Probe { public static int Run() {
            var list = new List<Actor> { new Actor() };
            return list.ConvertAll(value => value.Name).Count;
        } }
        """, 1)),
    ("ShadowedNamesAndStringsUntouched", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            var Employee = "ScheduleOne.Testing.Employee";
            return Employee.Length + new Actor().Score(0);
        } }
        """, 29)),
    ("InterpolatedStaticMembers", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() => $"{Actor.Current.Name}".Length; }
        """, 5)),
    ("NameofNamespacePreservesAuthorValue", () => Verify("""
        public static class Probe { public static int Run() => nameof(ScheduleOne).Length; }
        """, 11)),
    ("NativeContractRemovalComparesIdentity", () => {
        // Guards the contract itself: a fresh conversion of an equal callback must not remove the listener,
        // otherwise the method-group removal test below could pass without the conversion cache.
        var native = CompilationSupport.Create("Probe", """
            using Il2CppInterop.Runtime;
            using UnityEngine.Events;
            public static class Probe {
                static int calls;
                static void Hit() { calls++; }
                public static int Run() {
                    calls = 0;
                    var unityEvent = new UnityEvent();
                    unityEvent.AddListener(DelegateSupport.ConvertDelegate<UnityAction>((System.Action)Hit));
                    unityEvent.RemoveListener(DelegateSupport.ConvertDelegate<UnityAction>((System.Action)Hit));
                    unityEvent.Invoke();
                    return calls;
                }
            }
            """, CompilationSupport.PlatformReferences.Add(Contracts.NativeReference));
        Assert(Execute(native, Contracts.NativeBytes) == 1, "Native UnityEvent removal must compare native delegate identity.");
    }),
    ("DelegateMethodGroupRemovalUsesEqualCallback", () => Verify("""
        using UnityEngine.Events;
        public static class Probe {
            static int calls;
            static void Hit() { calls++; }
            public static int Run() {
                calls = 0;
                var unityEvent = new UnityEvent();
                unityEvent.AddListener(Hit);
                unityEvent.Invoke();
                unityEvent.RemoveListener(Hit);
                unityEvent.Invoke();
                return calls;
            }
        }
        """, 1)),
    ("UnityActionVariableAndLambdaInvocation", () => Verify("""
        using UnityEngine.Events;
        public static class Probe {
            static int calls;
            public static int Run() {
                calls = 0;
                UnityAction action = () => calls += 2;
                action();
                action.Invoke();
                var unityEvent = new UnityEvent();
                unityEvent.AddListener(action);
                unityEvent.AddListener(() => calls += 10);
                unityEvent.Invoke();
                return calls;
            }
        }
        """, 16)),
    ("ManagedActionAssignedToNativeSlot", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            int calls = 0;
            System.Action callback = () => calls++;
            var actor = new Actor();
            actor.Changed = callback;
            actor.Changed();
            return calls;
        } }
        """, 1)),
    ("MissingNativeMemberFails", () => Reject("""
        using ScheduleOne.Testing;
        public static class Probe { public static string Run() => Actor.RenamedOnlyInMono(); }
        """)),
    ("NativeSwitchUsesNativeIdentity", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            switch (Actor.Current) { case Employee employee: return employee.Salary; default: return 0; }
        } }
        """, 7)),
    ("NativeSwitchExpressionPreservesGuardsAndEvaluation", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            Actor.Calls = 0;
            var result = Actor.Next() switch {
                Employee { Salary: > 10 } high => high.Salary,
                Employee employee when employee.Salary == 7 => employee.Salary,
                _ => -10
            };
            return result + Actor.Calls;
        } }
        """, 8)),
    ("NativeSwitchNotPatternPreservesNull", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            Actor value = null;
            return value switch { not Employee => 3, _ => -1 };
        } }
        """, 3)),
    ("CollectionBoundaryRetainsLiveStorage", () => Verify("""
        using ScheduleOne.Testing;
        using System.Collections.Generic;
        public static class Probe { public static int Run() {
            List<Actor> list = Actor.All; list.Add(new Actor()); return Actor.All.Count;
        } }
        """, 2)),
    ("FieldByReferenceCannotBecomeProperty", () => Reject("""
        using ScheduleOne.Testing;
        public static class Probe {
            static void Update(ref string value) => value = "updated";
            public static void Run() { var actor = new Actor(); Update(ref actor.Name); }
        }
        """)),
    ("NativeHashEqualityIsAutomatic", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() {
            var set = new System.Collections.Generic.HashSet<Actor> { Actor.Current };
            return set.Contains((Employee)Actor.Current) ? 1 : 0;
        } }
        """, 1)),
    ("NativeListEqualityUsesGameIdentity", () => Verify("""
        using ScheduleOne.Testing;
        public static class Probe { public static int Run() => new System.Collections.Generic.List<Actor> { Actor.Current }.Contains((Employee)Actor.Current) ? 1 : 0; }
        """, 1)),
    ("NativeLinqDistinctUsesNativeIdentity", () => Verify("""
        using ScheduleOne.Testing;
        using System.Linq;
        public static class Probe { public static int Run() => new Actor[] { Actor.Current, (Employee)Actor.Current }.Distinct().Count(); }
        """, 1)),
    ("NativeDictionaryKeysAndExplicitDefaultComparer", () => Verify("""
        using ScheduleOne.Testing;
        using System.Collections.Generic;
        public static class Probe { public static int Run() {
            Dictionary<Actor, int> values = new(comparer: null, capacity: 3);
            values.Add(Actor.Current, 7);
            EqualityComparer<Actor> comparer = EqualityComparer<Actor>.Default;
            return comparer.Equals(Actor.Current, (Employee)Actor.Current) ? values[(Employee)Actor.Current] : -1;
        } }
        """, 7)),
    ("NativeEqualityOverridesArePreserved", () => Verify("""
        using ScheduleOne.Testing;
        using System.Collections.Generic;
        using System.Linq;
        public static class Probe { public static int Run() {
            var left = new KeyActor { Key = 3 }; var right = new KeyActor { Key = 3 };
            var set = new HashSet<KeyActor> { left, right };
            var list = new List<KeyActor> { left };
            return set.Count + (list.Contains(right) ? 1 : 0) + new KeyActor[] { left, right }.GroupBy(a => a).Count();
        } }
        """, 3)),
    ("AuthorBindingErrorsReported", () => Reject("public static class Probe { public static int Run() => Missing.Name; }")),
    ("SerializedComponentScalarsPreserveInitializersAndMutations", () => Verify("""
        using UnityEngine;
        public sealed partial class Door : MonoBehaviour {
            public int Number = 3, Other = 4;
            [SerializeField] private string label = "start";
            [SerializeField] internal int Internal = 11;
            public Door() { Number += Other; label += Number; }
            public string Label => label;
        }
        public sealed partial class Door { public float Speed = 1.5f; }
        public static class Probe {
            private static int calls;
            private static Door door;
            private static Door Next() { calls++; return door; }
            public static int Run() {
                door = new Door { Other = 9 };
                Next().Number++;
                Door absent = null;
                return door.Number == 8 && door.Other == 9 && door.Label == "start7" &&
                    door.Speed == 1.5f && door.Internal == 11 && calls == 1 && absent?.Number == null && nameof(door.Number) == "Number" ? 1 : 0;
            }
        }
        """, 1)),
    ("SerializedComponentObjectReferencesPreserveIdentityAndNull", () => Verify("""
        using UnityEngine;
        using System.Linq;
        public sealed class Fields : MonoBehaviour {
            public Object Target;
            public ObjectSequence Objects = new();
            [SerializeField] private GameObject exact;
            public void Set(GameObject value) { exact = value; }
            public GameObject Read() => exact;
        }
        public static class Probe {
            static int calls;
            static Fields fields;
            static Fields Next() { calls++; return fields; }
            public static int Run() {
                var target = new GameObject();
                fields = new Fields { Target = target };
                fields.Set(target);
                fields.Objects.Add(target);
                if (!object.ReferenceEquals(fields.Objects.Select(item => item).Single(), target)) return -7;
                var projected = new { fields.Target };
                var pair = (fields.Target, count: 1);
                var duplicates = (fields.Target, fields.Target);
                if (!object.ReferenceEquals(duplicates.Item1, duplicates.Item2)) return -6;
                if (!object.ReferenceEquals(projected.Target, pair.Target)) return -5;
                if (!object.ReferenceEquals((GameObject)fields.Target, fields.Read())) return -1;
                if (fields.Target is not GameObject || fields is not { Target: not null }) return -2;
                Next().Target = null;
                if (calls != 1 || fields.Target != null || fields.Read() != target) return -3;
                fields.Set(null);
                return fields.Read() == null ? 1 : -4;
            }
        }
        """, 1)),
    ("SerializedComponentBaseVirtualSeesDerivedInitializer", () => Verify("""
        using UnityEngine;
        public abstract class Base : MonoBehaviour {
            [System.NonSerialized] public int Snapshot;
            protected Base() { Snapshot = Read(); }
            public abstract int Read();
        }
        public sealed class Child : Base {
            public int Value = 7;
            public Child() { Value += 3; }
            public override int Read() => Value;
        }
        public static class Probe { public static int Run() {
            var child = new Child(); return child.Snapshot == 7 && child.Value == 10 ? 1 : 0;
        } }
        """, 1)),
    ("SerializedComponentInitializerEvaluationOrder", () => Verify("""
        using UnityEngine;
        public static class Trace { public static string Log = ""; public static int Mark(string tag) { Log += tag; return 1; } }
        public class Base : MonoBehaviour {
            public int First = Trace.Mark("B");
            public Base() { Trace.Log += "b"; }
        }
        public sealed class Child : Base {
            public int Second = Trace.Mark("D");
            [System.NonSerialized] public int Managed = Trace.Mark("M");
            public Child() { Trace.Log += "d"; }
        }
        public static class Probe { public static int Run() {
            var child = new Child(); return Trace.Log == "DMBbd" && child.First + child.Second + child.Managed == 3 ? 1 : 0;
        } }
        """, 1)),
    ("SerializedComponentScalarsRetainLanguageOperations", () => Verify("""
        using UnityEngine;
        public sealed class Fields : MonoBehaviour {
            public bool Flag = true;
            public byte Small = 251;
            public long Wide = 9007199254740993L;
            public double Fraction = 0.125;
            public char Letter = 'Z';
            public string Text;
            public int Left, Right;
        }
        public static class Probe { public static int Run() {
            var fields = new Fields();
            (fields.Left, fields.Right) = (3, 4);
            fields.Text ??= "filled";
            return fields is { Flag: true, Small: 251, Wide: 9007199254740993L, Fraction: 0.125, Letter: 'Z', Left: 3, Right: 4 }
                && fields.Text == "filled" ? 1 : 0;
        } }
        """, 1)),
    ("SerializedComponentRefFieldIsRejected", () => Reject("""
        public sealed class Fields : UnityEngine.MonoBehaviour { public int Value; }
        public static class Probe {
            static void Increment(ref int value) { value++; }
            public static int Run() { var fields = new Fields(); Increment(ref fields.Value); return fields.Value; }
        }
        """)),
    ("SerializedComponentAuthorReferenceRegistrationIsRejected", () => Reject("""
        public sealed class Fields : UnityEngine.MonoBehaviour {
            [UnityEngine.SerializeField] private Fields next;
        }
        """)),
    ("SerializedComponentHiddenFieldIsRejected", () => Reject("""
        public class Base : UnityEngine.MonoBehaviour { public int Value; }
        public sealed class Child : Base { public new int Value; }
        """)),
    ("UnsupportedVolatileComponentFieldRemainsManaged", () => {
        var result = Lower("public sealed class Fields : UnityEngine.MonoBehaviour { public volatile int Value; }");
        Assert(result.Success && result.Diagnostics.Any(diagnostic => diagnostic.Id == "S1IC016") &&
            result.Compilation.GetTypeByMetadataName("Fields")!.GetMembers("Value").OfType<IFieldSymbol>().Single() is
                { IsVolatile: true, Type.SpecialType: SpecialType.System_Int32 }, "Volatile field semantics or serialization warning were lost.");
    }),
    ("SerializedComponentFieldCannotSilentlyLoseState", () => Reject("""
        public sealed class Door : UnityEngine.MonoBehaviour { [UnityEngine.SerializeField] private System.Collections.Generic.List<int> speeds = new(); }
        """)),
    ("PublicComponentFieldReportsSerializationBoundary", () => {
        var result = Lower("public sealed class Door : UnityEngine.MonoBehaviour { public decimal[] Speeds = new decimal[1]; }");
        Assert(result.Success && result.Diagnostics.Any(diagnostic => diagnostic.Id == "S1IC016"), "Missing public field serialization warning.");
    }),
    ("DeterministicLowering", () => {
        const string source = "using ScheduleOne.Testing; public static class Probe { public static int Run() => new Actor().Score(1); }";
        var first = Lower(source);
        var second = Lower(source);
        Assert(first.Compilation.SyntaxTrees.Select(t => t.ToString()).SequenceEqual(
            second.Compilation.SyntaxTrees.Select(t => t.ToString())), "Lowering output changed across equivalent runs.");
    })
};

if (args is ["--list"])
{
    foreach (var test in tests) Console.WriteLine(test.Name);
    return 0;
}
if (args is ["--real", var monoPath, var nativePath])
    return RealReferences.Verify(monoPath, nativePath);
if (args is ["--mod", var modProject, var authorList, var targetList, var reportPath, var defines])
    return WorkspaceModCorpus.Verify(modProject, authorList, targetList, reportPath, defines);
if (args is ["--mod-sources", var sourceProject, var sources, var sourceAuthorList, var sourceTargetList, var sourceReportPath, var sourceDefines])
    return WorkspaceModCorpus.Verify(sourceProject, sourceAuthorList, sourceTargetList, sourceReportPath, sourceDefines, sources);
if (args.Length != 0 && args is not ["--filter", _])
    throw new ArgumentException("Expected --list, --filter <name>, --real <mono install> <IL2CPP install>, --mod <project> <author refs list> <target refs list> <report JSON> <author defines>, or --mod-sources <project> <sources list> <author refs list> <target refs list> <report JSON> <author defines>.");
var selected = tests.Where(test => args.Length == 0 || test.Name.Contains(args[1], StringComparison.OrdinalIgnoreCase)).ToArray();
if (selected.Length == 0) throw new ArgumentException("No tests matched the filter.");
int failures = 0;
foreach (var test in selected)
{
    try { test.Test(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception exception) { failures++; Console.Error.WriteLine($"FAIL {test.Name}: {exception}"); }
}
Console.WriteLine($"Compiler contracts: {selected.Length - failures} passed; {failures} failed.");
return failures == 0 ? 0 : 1;

static LoweringResult Lower(string source) => new InteropCompiler().Lower(
    CompilationSupport.Create("Probe", source, CompilationSupport.PlatformReferences.Add(Contracts.MonoReference)),
    CompilationSupport.PlatformReferences.Add(Contracts.NativeReference));

static void Verify(string source, int expected)
{
    // Execute the same author program against both authored contract implementations.
    var mono = CompilationSupport.Create("Probe", source, CompilationSupport.PlatformReferences.Add(Contracts.MonoReference));
    Assert(Execute(mono, Contracts.MonoBytes) == expected, "Original Mono-contract behavior differs from expectation.");
    var result = Lower(source);
    Assert(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    Assert(Execute(result.Compilation, Contracts.NativeBytes, result.RuntimeAssembly) == expected,
        "Lowered IL2CPP-contract behavior differs from original source.");
}

static void Reject(string source)
{
    var result = Lower(source);
    Assert(!result.Success && result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error),
        "Unsafe or unavailable source was accepted without an error.");
}

static int Execute(CSharpCompilation compilation, byte[] referenceBytes, ImmutableArray<byte> runtimeAssembly = default)
{
    using var emitted = new MemoryStream();
    var result = compilation.Emit(emitted);
    Assert(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    var context = new AssemblyLoadContext("CompilerContract", isCollectible: true);
    try
    {
        using var reference = new MemoryStream(referenceBytes);
        context.LoadFromStream(reference);
        if (!runtimeAssembly.IsDefaultOrEmpty)
        {
            using var runtime = new MemoryStream(runtimeAssembly.ToArray());
            context.LoadFromStream(runtime);
        }
        emitted.Position = 0;
        Assembly assembly = context.LoadFromStream(emitted);
        return (int)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!;
    }
    finally { context.Unload(); }
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

internal static class Contracts
{
    public static readonly byte[] MonoBytes = CompilationSupport.Emit("MonoContract", RuntimeContracts.Mono);
    public static readonly byte[] NativeBytes = CompilationSupport.Emit("NativeContract", RuntimeContracts.Il2Cpp);
    public static readonly MetadataReference MonoReference = MetadataReference.CreateFromImage(MonoBytes);
    public static readonly MetadataReference NativeReference = MetadataReference.CreateFromImage(NativeBytes);
}

internal static class CompilationSupport
{
    public static readonly ImmutableArray<MetadataReference> PlatformReferences =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToImmutableArray();

    public static CSharpCompilation Create(string name, string source, IEnumerable<MetadataReference> references) =>
        CSharpCompilation.Create(name,
            [CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8), new CSharpParseOptions(LanguageVersion.Latest), "Author.cs")],
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

    public static byte[] Emit(string name, string source)
    {
        using var stream = new MemoryStream();
        var result = Create(name, source, PlatformReferences).Emit(stream);
        if (!result.Success) throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        return stream.ToArray();
    }
}
