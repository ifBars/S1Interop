using S1Interop.Compiler;

internal static class UnstrippedMethodAnalysisTests
{
    public static void Run()
    {
        byte[] author = CompilationSupport.Emit("AuthorEvents", """
            using System;
            public class Events {
                private event Action Changed;
                public void Subscribe(Action callback) { Changed += callback; }
                public void StaticMismatch() { }
            }
            public class Box<T> { public void Broken(T value) { } }
            public class Il2CppEvents { public void Original() { } }
            """);
        byte[] target = CompilationSupport.Emit("NativeEvents", """
            using System;
            public class Events {
                static Events() { throw new Exception("metadata analysis must not execute this"); }
                public Action Changed { get; set; }
                public void add_Changed(Action callback) { throw new NotSupportedException("Method unstripping failed"); }
                public void remove_Changed(Action callback) { throw new NotSupportedException("Method unstripping failed"); }
                public void Subscribe(Action callback) { add_Changed(callback); }
                public void CycleA(Action callback) { CycleB(callback); }
                public void CycleB(Action callback) { CycleA(callback); Subscribe(callback); }
                public void OtherThrow() { throw new NotSupportedException("another reason"); }
                public void HasSideEffect() { Console.WriteLine("effect"); throw new NotSupportedException("Method unstripping failed"); }
                public void MissingOriginal() { throw new NotSupportedException("Method unstripping failed"); }
                public static void StaticMismatch() { throw new NotSupportedException("Method unstripping failed"); }
                public void GenericCall() { new Box<int>().Broken(1); }
                public Action<int> MakeCallback() => new Box<int>().Broken;
            }
            public class Box<T> { public void Broken(T value) { throw new NotSupportedException("Method unstripping failed"); } }
            public class Il2CppEvents { public void Original() { throw new NotSupportedException("Method unstripping failed"); } }
            """);
        var findings = UnstrippedMethodAnalysis.Inspect(author, target);
        byte[] staticAuthor = CompilationSupport.Emit("StaticAuthor", """
            public static class Signals {
                public static event System.Action Changed;
                [field: System.ThreadStatic] public static event System.Action PerThread;
            }
            """);
        byte[] staticTarget = CompilationSupport.Emit("StaticTarget", """
            public static class Signals {
                public static System.Action Changed { get; set; }
                public static System.Action PerThread { get; set; }
                public static void add_Changed(System.Action value) { throw new System.NotSupportedException("Method unstripping failed"); }
                public static void remove_Changed(System.Action value) { throw new System.NotSupportedException("Method unstripping failed"); }
                public static void add_PerThread(System.Action value) { throw new System.NotSupportedException("Method unstripping failed"); }
                public static void remove_PerThread(System.Action value) { throw new System.NotSupportedException("Method unstripping failed"); }
            }
            """);
        var staticPlan = AtomicEventRepairPlan.Create(staticAuthor, staticTarget);
        Require(staticPlan.Accessors.Count == 2 && staticPlan.Accessors.All(a => a.IsStatic && a.FieldName == "Changed") &&
            staticPlan.Accessors.Count(a => a.Add) == 1, "Static CAS loops must be recognized without accepting thread-local storage.");
        var plan = AtomicEventRepairPlan.Create(author, target);
        Require(plan.Accessors.Count == 2 && plan.Accessors.Count(a => a.Add) == 1 &&
            plan.Accessors.All(a => a.FieldName == "Changed" && a.DeclaringType == "Events" && a.CallbackType == "System.Action"),
            "Only the recognized add/remove loops should be planned with exact storage and callback identity.");
        Require(plan.TargetSha256 == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(target)) &&
            plan.AuthorSha256 == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(author)) &&
            findings.All(f => f.TargetModuleMvid == plan.TargetModuleMvid), "Plan must bind both input images and the target module.");
        using (var input = new MemoryStream(target))
        using (var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(input))
        using (var output = new MemoryStream())
        {
            var accessor = assembly.MainModule.Types.Single(t => t.Name == "Events").Methods.Single(m => m.Name == "add_Changed");
            accessor.Body.Instructions.Add(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ret));
            assembly.Write(output);
            Require(UnstrippedMethodAnalysis.Inspect(author, output.ToArray()).Count == findings.Count,
                "The generated unreachable ret prevented stub recognition.");
            var duplicate = new Mono.Cecil.MethodDefinition(accessor.Name, accessor.Attributes, accessor.ReturnType);
            duplicate.Parameters.Add(new Mono.Cecil.ParameterDefinition(accessor.Parameters[0].ParameterType));
            duplicate.Body.Instructions.Add(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ldstr, "Method unstripping failed"));
            duplicate.Body.Instructions.Add(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Newobj,
                (Mono.Cecil.MethodReference)accessor.Body.Instructions.Single(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Newobj).Operand));
            duplicate.Body.Instructions.Add(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Throw));
            accessor.DeclaringType.Methods.Add(duplicate);
            using var duplicateOutput = new MemoryStream();
            assembly.Write(duplicateOutput);
            var duplicateFindings = UnstrippedMethodAnalysis.Inspect(author, duplicateOutput.ToArray())
                .Where(f => f.TargetMethod.Contains("::add_Changed(")).ToArray();
            Require(duplicateFindings.Length == 2 && duplicateFindings.Select(f => f.TargetMetadataToken).Distinct().Count() == 2 &&
                duplicateFindings.Sum(f => f.DirectCallers.Count) == 1,
                "Duplicate display signatures must retain distinct token identities and exact call targets.");
        }
        Require(findings.Count == 6, "Detection must match the exact failure body, not arbitrary exceptions.");
        var add = findings.Single(f => f.TargetMethod.Contains("::add_Changed("));
        Require(add.AuthorHasBody && add.AuthorMethod!.Contains("::add_Changed("), "Original accessor was not matched.");
        Require(add.EventName == "Changed" && add.StorageProperty?.Contains("::Changed()") == true,
            "Event backing storage was not identified from source metadata and field access.");
        Require(add.UsesCompareExchange, "Original atomic update requirement was lost.");
        Require(add.HasCanonicalAtomicEventBody, "Complete default event accessor pattern was not recognized.");
        Require(add.DirectCallers.Count == 1 && add.DirectCallers[0].Contains("::Subscribe("), "Managed caller edge missing.");
        Require(add.PotentialCallers.Count == 3 && add.PotentialCallers.Any(m => m.Contains("::CycleA(")),
            "Transitive callers must include cycle members once and terminate.");
        Require(findings.Single(f => f.TargetMethod.Contains("::MissingOriginal(")).AuthorMethod is null,
            "Missing source counterpart was invented.");
        Require(findings.Single(f => f.TargetMethod.Contains("::StaticMismatch(")).AuthorMethod is null,
            "Static and instance methods must not be paired.");
        var generic = findings.Single(f => f.TargetMethod.Contains("::Broken("));
        Require(generic.AuthorHasBody && generic.DirectCallers.Count == 2 && generic.DirectCallers.Any(m => m.Contains("::MakeCallback(")),
            "Constructed generic and delegate-function references were lost.");
        Require(findings.Single(f => f.TargetMethod.Contains("::Original(")).AuthorMethod!.Contains("Il2CppEvents::Original"),
            "A legitimate author Il2Cpp prefix was stripped.");
        Require(findings.Select(f => f.TargetMethod).SequenceEqual(UnstrippedMethodAnalysis.Inspect(author, target).Select(f => f.TargetMethod)),
            "Analysis ordering is unstable.");
        foreach (bool changeField in new[] { false, true })
        {
            using var input = new MemoryStream(author);
            using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(input);
            var owner = assembly.MainModule.Types.Single(t => t.Name == "Events");
            var accessor = owner.Methods.Single(m => m.Name == "add_Changed");
            if (changeField)
            {
                var other = new Mono.Cecil.FieldDefinition("Other", Mono.Cecil.FieldAttributes.Private, owner.Fields.Single(f => f.Name == "Changed").FieldType);
                owner.Fields.Add(other);
                accessor.Body.Instructions.Single(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldflda).Operand = other;
            }
            else
                accessor.Body.Instructions.Single(i => i.OpCode.Code is Mono.Cecil.Cil.Code.Bne_Un or Mono.Cecil.Cil.Code.Bne_Un_S).Operand = accessor.Body.Instructions[0];
            using var output = new MemoryStream();
            assembly.Write(output);
            var altered = UnstrippedMethodAnalysis.Inspect(output.ToArray(), target).Single(f => f.TargetMethod.Contains("::add_Changed("));
            Require(altered.UsesCompareExchange && !altered.HasCanonicalAtomicEventBody,
                "A CompareExchange call is not enough: the exact backing field and retry target must match.");
            Require(AtomicEventRepairPlan.Create(output.ToArray(), target).Accessors is [var remaining] && !remaining.Add,
                "A changed backing field or retry branch must exclude only the altered accessor from repair.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
