using System.Security.Cryptography;
using System.Text.Json;

internal static class InstallationVerificationTests
{
    public static void Run(string scenario)
    {
        string root = Path.Combine(Path.GetTempPath(), "S1Interop.Compiler.VersionTests", Guid.NewGuid().ToString("N"));
        string mono = Path.Combine(root, "Mono"), native = Path.Combine(root, "Il2Cpp");
        try
        {
            Write(mono, "Schedule I_Data/globalgamemanagers", System.Text.Encoding.UTF8.GetBytes("Unity\0version 0.4.7f9 Alternate\0"));
            Write(native, "Schedule I_Data/globalgamemanagers", System.Text.Encoding.UTF8.GetBytes("Unity\0version 0.4.7f9\0"));
            byte[] assembly = CompilationSupport.Emit("Assembly-CSharp", "public class GameType { }");
            Write(mono, "Schedule I_Data/Managed/Assembly-CSharp.dll", assembly);
            Write(native, "MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll", assembly);
            byte[] image = [1, 2, 3, 4];
            Write(native, "GameAssembly.dll", image);
            string record = "[Il2CppAssemblyGenerator]\nGameAssemblyHash = \"" + Convert.ToHexString(SHA512.HashData(image)) + "\"\n";
            string config = Path.Combine(native, "MelonLoader/Dependencies/Il2CppAssemblyGenerator/Config.cfg");
            Write(native, "MelonLoader/Dependencies/Il2CppAssemblyGenerator/Config.cfg", System.Text.Encoding.UTF8.GetBytes(record));
            string? expected = scenario switch
            {
                "match" => null,
                "version" => "S1C905",
                _ => "S1C907"
            };
            switch (scenario)
            {
                case "version": File.WriteAllText(Path.Combine(mono, "Schedule I_Data/globalgamemanagers"), "0.4.6f13"); break;
                case "stale": File.WriteAllBytes(Path.Combine(native, "GameAssembly.dll"), [4, 3, 2, 1]); break;
                case "missing": File.Delete(config); break;
                case "duplicate": File.AppendAllText(config, record); break;
            }
            using var output = new StringWriter();
            TextWriter previousOut = Console.Out, previousError = Console.Error;
            int exit;
            try
            {
                Console.SetOut(output);
                Console.SetError(output);
                exit = CompilerCommand.Run(["verify-installations", "--mono-game-path", mono, "--il2cpp-game-path", native,
                    "--report", Path.Combine(root, "versions.json")]);
            }
            finally { Console.SetOut(previousOut); Console.SetError(previousError); }
            if (expected is null ? exit != 0 : exit != 1 || !output.ToString().Contains(expected))
                throw new InvalidOperationException(output.ToString());
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "versions.json")));
            bool generationMatches = report.RootElement.GetProperty("Generation").GetProperty("Match").GetBoolean();
            if (generationMatches != (scenario is "match" or "version"))
                throw new InvalidOperationException("Generation identity report disagrees with evidence.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static void Write(string root, string relative, byte[] bytes)
    {
        string file = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, bytes);
    }
}
