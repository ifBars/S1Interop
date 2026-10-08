internal static class EventRepairPreparationTests
{
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "S1Interop.EventRepairPreparation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] author = CompilationSupport.Emit("RepairFixture", "public class Events { private event System.Action Changed; }");
            byte[] target = CompilationSupport.Emit("RepairFixture", """
                public class Events {
                    public System.Action Changed { get; set; }
                    public void add_Changed(System.Action callback) { throw new System.NotSupportedException("Method unstripping failed"); }
                    public void remove_Changed(System.Action callback) { throw new System.NotSupportedException("Method unstripping failed"); }
                }
                """);
            string Prepare(string name, byte[] image)
            {
                string original = Path.Combine(root, name + ".dll"), list = Path.Combine(root, name + ".list"), output = Path.Combine(root, name);
                File.WriteAllBytes(original, image);
                File.WriteAllText(list, original);
                ReferencePreparation.Run(["--references", list, "--output", output]);
                return File.ReadAllLines(Path.Combine(output, "references.list")).Single();
            }
            string a = Prepare("author", author), t = Prepare("target", target);
            var plans = EventRepairPreparation.Create([a], [t]);
            Require(plans.Count == 1 && plans[0].Accessors.Count == 2, "Original IL must remain available through publicized provenance.");
            string originalAuthor = Path.Combine(root, "author.dll"), originalTarget = Path.Combine(root, "target.dll");
            var raw = EventRepairPreparation.Create([originalAuthor], [originalTarget]);
            Require(raw.Count == 1 && raw[0].TargetSha256 == plans[0].TargetSha256 &&
                raw[0].Accessors.SequenceEqual(plans[0].Accessors), "Raw assembly inputs must produce the same repairs as prepared references.");
            Require(EventRepairPreparation.Create([originalAuthor, a], [originalTarget, t]).Count == 1,
                "Identical raw/prepared images should deduplicate.");
            Reject(() => EventRepairPreparation.Create([originalAuthor, originalTarget], [t]), "ambiguous raw image identity");
            Reject(() => S1Interop.Compiler.GameReferencePublicizer.CreateReference(File.ReadAllBytes(a)), "re-publicized metadata without original bodies");
            string provenance = t + ".s1interop-origin.json";
            string savedProvenance = File.ReadAllText(provenance);
            File.Move(provenance, provenance + ".saved");
            Reject(() => EventRepairPreparation.Create([a], [t]), "missing prepared-reference provenance");
            File.Move(provenance + ".saved", provenance);
            File.WriteAllText(provenance, System.Text.Json.JsonSerializer.Serialize(new ReferencePreparation.ReferenceOrigin(
                originalAuthor, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(author)),
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(t))))));
            Reject(() => EventRepairPreparation.Create([a], [t]), "sidecar pointing at a different original image");
            File.WriteAllText(provenance, savedProvenance);
            Require(EventRepairPreparation.Initializer(plans, new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions())
                .ToString().Contains(plans[0].TargetSha256), "Startup source must bind the target image.");
            File.WriteAllBytes(Path.Combine(root, "author.dll"), target);
            Reject(() => EventRepairPreparation.Create([a], [t]), "changed original");
            File.WriteAllBytes(Path.Combine(root, "author.dll"), author);
            byte[] reference = File.ReadAllBytes(t);
            File.WriteAllBytes(t, target);
            Reject(() => EventRepairPreparation.Create([a], [t]), "changed publicized reference");
            File.WriteAllBytes(t, reference);
            File.WriteAllText(t + ".s1interop-origin.json", "{");
            Reject(() => EventRepairPreparation.Create([a], [t]), "corrupt provenance JSON");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Reject(Action action, string scenario)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new InvalidOperationException("Provenance accepted " + scenario + ".");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
