using System.Text;
using System.Text.Json;
using S1Interop.Core.Generators;

internal static class NewProjectCommand
{
    public static int Run(ParsedCommand command)
    {
        string targetDirectory = Path.GetFullPath(command.Path);
        if (Directory.Exists(targetDirectory) && Directory.EnumerateFileSystemEntries(targetDirectory).Any())
        {
            Console.Error.WriteLine($"s1interop: target directory is not empty: {targetDirectory}");
            return 2;
        }

        string projectName = SanitizeIdentifier(new DirectoryInfo(targetDirectory).Name);
        if (string.IsNullOrWhiteSpace(projectName))
        {
            Console.Error.WriteLine("s1interop: could not infer a valid project name from the target path.");
            return 2;
        }

        string projectPath = Path.Combine(targetDirectory, $"{projectName}.csproj");
        string corePath = Path.Combine(targetDirectory, "ModCore.cs");
        string starterPath = Path.Combine(targetDirectory, "S1Interop.Generated", BackendNeutralStarterGenerator.SourceFileName);
        string readmePath = Path.Combine(targetDirectory, "README.md");
        string[] plannedFiles = [projectPath, corePath, starterPath, readmePath];

        if (!command.Apply)
        {
            PrintDryRun(command, targetDirectory, projectName, plannedFiles);
            return 0;
        }

        Directory.CreateDirectory(targetDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(starterPath)!);
        File.WriteAllText(projectPath, GenerateProject(projectName), Encoding.UTF8);
        File.WriteAllText(corePath, GenerateCore(projectName), Encoding.UTF8);
        File.WriteAllText(starterPath, new BackendNeutralStarterGenerator().GenerateSource(), Encoding.UTF8);
        File.WriteAllText(readmePath, GenerateReadme(projectName), Encoding.UTF8);

        if (command.Format == OutputFormat.Json)
        {
            var result = new
            {
                projectName,
                targetDirectory,
                files = plannedFiles
            };
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 0;
        }

        Console.WriteLine($"S1Interop backend-neutral project created: {projectName}");
        Console.WriteLine($"Directory: {targetDirectory}");
        foreach (string file in plannedFiles)
        {
            Console.WriteLine($"  created {file}");
        }

        return 0;
    }

    private static void PrintDryRun(ParsedCommand command, string targetDirectory, string projectName, IReadOnlyList<string> plannedFiles)
    {
        if (command.Format == OutputFormat.Json)
        {
            var result = new
            {
                projectName,
                targetDirectory,
                apply = false,
                files = plannedFiles
            };
            Console.WriteLine(JsonSerializer.Serialize(result));
            return;
        }

        Console.WriteLine($"S1Interop new project dry-run: {projectName}");
        Console.WriteLine($"Directory: {targetDirectory}");
        foreach (string file in plannedFiles)
        {
            Console.WriteLine($"  create {file}");
        }
        Console.WriteLine("Run again with --apply to write files.");
    }

    private static string GenerateProject(string projectName) =>
        $"""
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <TargetFramework>netstandard2.1</TargetFramework>
            <LangVersion>10.0</LangVersion>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <RootNamespace>{projectName}</RootNamespace>
            <AssemblyName>{projectName}</AssemblyName>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="S1Interop.Generators" Version="0.1.0-alpha.1" PrivateAssets="all" IncludeAssets="runtime; build; native; contentfiles; analyzers; buildtransitive" />
          </ItemGroup>

        </Project>
        """;

    private static string GenerateCore(string projectName) =>
        $$"""
        namespace {{projectName}};

        internal static class ModCore
        {
            public const string ModName = "{{projectName}}";
        }
        """;

    private static string GenerateReadme(string projectName) =>
        $$"""
        # {{projectName}}

        Backend-neutral Schedule One mod scaffold created by S1Interop.

        Add game type declarations in `S1Interop.Generated/S1Interop.BackendNeutral.cs` as your mod touches Schedule One APIs.

        ```csharp
        [assembly: S1Interop.S1InteropType("ScheduleOne.PlayerScripts.PlayerCamera", Alias = "PlayerCamera")]
        [assembly: S1Interop.S1InteropMember("PlayerCamera", "Instance", Alias = "PlayerCameraInstance", IsStatic = true)]
        ```
        """;

    private static string SanitizeIdentifier(string value)
    {
        var builder = new StringBuilder();
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character) || character == '_')
            {
                builder.Append(character);
            }
        }

        if (builder.Length == 0)
        {
            return string.Empty;
        }

        if (!char.IsLetter(builder[0]) && builder[0] != '_')
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }
}
