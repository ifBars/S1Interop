using S1Interop.Core.Setup;
using S1Interop.Core.Scaffolding;

internal sealed partial class S1InteropFixtureTests
{
    private void SetupSupportsEitherRuntimeAndPreservesBackendNeutralRequirements()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "S1Interop.Tests", Guid.NewGuid().ToString("N"));
        string projectDirectory = Path.Combine(tempRoot, "FirstMod");
        string monoPath = Path.Combine(tempRoot, "missing-mono");
        string il2CppPath = Path.Combine(tempRoot, "il2cpp");
        var scaffolder = new BackendNeutralProjectScaffolder();
        var service = new DeveloperSetupService();
        NewProjectPlan plan = scaffolder.CreatePlan(projectDirectory);
        try
        {
            scaffolder.Apply(plan);
            CreateFiles(il2CppPath,
                "Schedule I.exe",
                Path.Combine("MelonLoader", "Il2CppAssemblies", "Assembly-CSharp.dll"),
                Path.Combine("MelonLoader", "Il2CppAssemblies", "Il2CppScheduleOne.Core.dll"),
                Path.Combine("MelonLoader", "net6", "MelonLoader.dll"));

            DeveloperSetupReport il2CppOnly = service.Inspect(projectDirectory, monoPath, il2CppPath);
            Assert(il2CppOnly.Ready && il2CppOnly.CanApply, "An IL2CPP-only default scaffold must be ready to configure.");
            Assert(il2CppOnly.Checks.Single(check => check.Id == "mono").Status == "optional", "Mono is optional when the default scaffold has usable IL2CPP references.");
            Assert(!File.Exists(il2CppOnly.LocalPropsPath), "Inspect must stay read-only.");
            service.Apply(il2CppOnly);
            Assert(XDocument.Load(il2CppOnly.LocalPropsPath).Descendants("Il2CppGamePath").Single().Value == il2CppPath,
                "Setup must persist the usable IL2CPP path.");
            Assert(!service.Inspect(projectDirectory, monoPath, il2CppPath).CanApply, "Setup must never overwrite existing local paths.");

            DeveloperSetupReport neither = service.Inspect(projectDirectory, monoPath, Path.Combine(tempRoot, "missing-il2cpp"));
            Assert(!neither.Ready && !neither.CanApply, "Two missing runtimes cannot pass setup.");

            string experimentalDirectory = Path.Combine(tempRoot, "ExperimentalMod");
            scaffolder.Apply(scaffolder.CreatePlan(experimentalDirectory, experimentalBackendNeutral: true));
            DeveloperSetupReport experimental = service.Inspect(experimentalDirectory, monoPath, il2CppPath);
            Assert(!experimental.Ready && !experimental.CanApply, "The experimental shipping build still requires Mono references.");

            File.WriteAllText(Path.Combine(projectDirectory, ".gitignore"), "bin/\nobj/\n");
            Assert(!service.Inspect(projectDirectory, monoPath, il2CppPath).Ready, "A usable IL2CPP install must not bypass ignore safety.");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }
}
