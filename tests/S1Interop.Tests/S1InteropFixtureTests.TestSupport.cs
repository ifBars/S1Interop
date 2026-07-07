internal sealed partial class S1InteropFixtureTests
{
    private static void AssertHasRuntime(ProjectAnalysis project, string configName, RuntimeKind runtime)
    {
        ConfigurationAnalysis resolved = GetConfiguration(project, configName);
        Assert(resolved.Runtime == runtime, $"Expected {configName} to infer {runtime}, got {resolved.Runtime}.");
    }

    private static void AssertHasTargetFramework(ProjectAnalysis project, string configName, string targetFramework)
    {
        ConfigurationAnalysis configuration = GetConfiguration(project, configName);
        Assert(
            string.Equals(configuration.TargetFramework, targetFramework, StringComparison.OrdinalIgnoreCase),
            $"Expected {configName} to target {targetFramework}, got {configuration.TargetFramework ?? "<missing>"}.");
    }

    private static ConfigurationAnalysis GetConfiguration(ProjectAnalysis project, string configName)
    {
        ConfigurationAnalysis? configuration = project.Configurations.FirstOrDefault(config =>
            string.Equals(config.Name, configName, StringComparison.OrdinalIgnoreCase));
        Assert(configuration is not null, $"Missing configuration {configName} in {project.ProjectPath}.");
        return configuration!;
    }

    private static void AssertHasDiagnostic(ProjectAnalysis project, string ruleId, string? configuration)
    {
        bool found = project.Diagnostics.Any(diagnostic =>
            diagnostic.RuleId == ruleId &&
            string.Equals(diagnostic.Configuration, configuration, StringComparison.OrdinalIgnoreCase));
        Assert(found, $"Expected diagnostic {ruleId} for {configuration} in {project.ProjectPath}.");
    }

    private static void AssertHasUnconditionedCompileInclude(string projectPath, string include)
    {
        XDocument document = XDocument.Load(projectPath);
        bool found = document.Descendants()
            .Where(element => element.Name.LocalName == "Compile")
            .Any(element =>
                element.Parent?.Attribute("Condition") is null &&
                string.Equals(element.Attribute("Include")?.Value, include, StringComparison.OrdinalIgnoreCase));
        Assert(found, $"Expected unconditioned Compile Include={include} in {projectPath}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static MethodInfo? GetNonGenericMethod(Type type, string name, params Type[] parameterTypes)
    {
        return type.GetMethods()
            .FirstOrDefault(method =>
                string.Equals(method.Name, name, StringComparison.Ordinal) &&
                !method.IsGenericMethod &&
                method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes));
    }

    private static IReadOnlyList<object> GetGeneratedPatchReports(Type patcherType)
    {
        object reportsValue = patcherType
            .GetProperty("Reports", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null)
            ?? throw new InvalidOperationException("Generated patcher did not expose Reports.");
        if (reportsValue is not System.Collections.IEnumerable reports)
        {
            throw new InvalidOperationException("Generated patch reports are not enumerable.");
        }

        return reports.Cast<object>().ToArray();
    }

    private static IReadOnlyList<object> GetGeneratedMemberReports(Type memberRegistryType)
    {
        object reportsValue = memberRegistryType
            .GetProperty("Reports", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null)
            ?? throw new InvalidOperationException("Generated member registry did not expose Reports.");
        if (reportsValue is not System.Collections.IEnumerable reports)
        {
            throw new InvalidOperationException("Generated member reports are not enumerable.");
        }

        return reports.Cast<object>().ToArray();
    }

    private static object? GetReportValue(object report, string propertyName) =>
        report.GetType().GetProperty(propertyName)?.GetValue(report);

    private static string RunTypeRegistryGenerator(string source, params string[] symbols)
    {
        IReadOnlyDictionary<string, string> generatedSources = RunS1InteropGenerator(source, symbols);
        return generatedSources.Single(pair => pair.Key.Contains("S1Interop.TypeRegistry.g.cs", StringComparison.Ordinal)).Value;
    }

    private static string RunTypeRegistryGenerator(
        string source,
        IReadOnlyList<MetadataReference> additionalReferences,
        params string[] symbols)
    {
        Compilation outputCompilation = RunS1InteropGeneratorCompilation(source, symbols, assemblyName: null, additionalReferences);
        return outputCompilation.SyntaxTrees
            .Where(tree => (tree.FilePath ?? string.Empty).Contains("S1Interop.Generators", StringComparison.Ordinal))
            .ToDictionary(
                tree => Path.GetFileName(tree.FilePath),
                tree => tree.GetText().ToString(),
                StringComparer.Ordinal)
            .Single(pair => pair.Key.Contains("S1Interop.TypeRegistry.g.cs", StringComparison.Ordinal))
            .Value;
    }

    private static IReadOnlyDictionary<string, string> RunS1InteropGenerator(string source, params string[] symbols)
    {
        Compilation outputCompilation = RunS1InteropGeneratorCompilation(source, symbols);
        return outputCompilation.SyntaxTrees
            .Where(tree => (tree.FilePath ?? string.Empty).Contains("S1Interop.Generators", StringComparison.Ordinal))
            .ToDictionary(
                tree => Path.GetFileName(tree.FilePath),
                tree => tree.GetText().ToString(),
                StringComparer.Ordinal);
    }

    private static ImmutableArray<Diagnostic> RunS1InteropGeneratorDiagnostics(
        string source,
        IReadOnlyList<MetadataReference> additionalReferences,
        params string[] symbols)
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest)
            .WithPreprocessorSymbols(symbols);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "SyntheticMod." + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            GetTrustedPlatformReferences().Concat(additionalReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new S1InteropTypeRegistryGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> generatorDiagnostics);

        return generatorDiagnostics
            .Concat(outputCompilation.GetDiagnostics())
            .ToImmutableArray();
    }

    private static MetadataReference CreateMetadataReferenceFromSource(string assemblyName, string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest))],
            GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitResult = compilation.Emit(stream);
        Assert(
            emitResult.Success,
            $"Synthetic metadata reference {assemblyName} failed to compile: {string.Join(Environment.NewLine, emitResult.Diagnostics)}");

        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static void WriteAssemblyFromSource(string outputPath, string assemblyName, string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest))],
            GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        Microsoft.CodeAnalysis.Emit.EmitResult emitResult = compilation.Emit(outputPath);
        Assert(
            emitResult.Success,
            $"Synthetic assembly {assemblyName} failed to compile to {outputPath}: {string.Join(Environment.NewLine, emitResult.Diagnostics)}");
    }

    private static System.Reflection.Assembly CompileAndLoadS1InteropGeneratedAssembly(string source, params string[] symbols)
    {
        return CompileAndLoadS1InteropGeneratedAssembly(source, assemblyName: null, symbols);
    }

    private static System.Reflection.Assembly CompileAndLoadS1InteropGeneratedAssembly(string source, string? assemblyName, params string[] symbols)
    {
        Compilation outputCompilation = RunS1InteropGeneratorCompilation(source, symbols, assemblyName);
        using var assemblyStream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitResult = outputCompilation.Emit(assemblyStream);
        Assert(
            emitResult.Success,
            $"S1Interop generated compilation emit failed: {string.Join(Environment.NewLine, emitResult.Diagnostics)}");

        assemblyStream.Position = 0;
        return System.Reflection.Assembly.Load(assemblyStream.ToArray());
    }

    private static Compilation RunS1InteropGeneratorCompilation(string source, params string[] symbols)
    {
        return RunS1InteropGeneratorCompilation(source, symbols, assemblyName: null);
    }

    private static Compilation RunS1InteropGeneratorCompilation(string source, IReadOnlyList<string> symbols, string? assemblyName)
    {
        return RunS1InteropGeneratorCompilation(source, symbols, assemblyName, additionalReferences: []);
    }

    private static Compilation RunS1InteropGeneratorCompilation(
        string source,
        IReadOnlyList<string> symbols,
        string? assemblyName,
        IReadOnlyList<MetadataReference> additionalReferences)
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest)
            .WithPreprocessorSymbols(symbols);
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName ?? "SyntheticMod." + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            GetTrustedPlatformReferences().Concat(additionalReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new S1InteropTypeRegistryGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert(
            generatorDiagnostics.All(diagnostic => diagnostic.Severity != RoslynDiagnosticSeverity.Error),
            $"S1Interop type registry generator reported errors: {string.Join(Environment.NewLine, generatorDiagnostics)}");
        Assert(
            outputCompilation.GetDiagnostics().All(diagnostic => diagnostic.Severity != RoslynDiagnosticSeverity.Error),
            $"S1Interop generated compilation reported errors: {string.Join(Environment.NewLine, outputCompilation.GetDiagnostics())}");

        return outputCompilation;
    }

    private static IReadOnlyList<MetadataReference> GetTrustedPlatformReferences()
    {
        string trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES is not available.");
        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static string BuildBackendNeutralRegistrySource(
        IEnumerable<SdkTypeAlias> aliases,
        IEnumerable<S1InteropMemberDeclaration>? members = null)
    {
        var builder = new StringBuilder();
        SdkTypeAlias[] aliasDeclarations = aliases.ToArray();
        foreach (SdkTypeAlias alias in aliasDeclarations)
        {
            builder.AppendLine($"[assembly: S1Interop.S1InteropType(\"{EscapeCSharpString(alias.MonoType)}\", Alias = \"{EscapeCSharpString(alias.Alias)}\", Il2CppTypeName = \"{EscapeCSharpString(alias.Il2CppType)}\")]");
        }

        S1InteropMemberDeclaration[] memberDeclarations = members?.ToArray() ?? Array.Empty<S1InteropMemberDeclaration>();
        foreach (S1InteropMemberDeclaration member in memberDeclarations)
        {
            builder.Append($"[assembly: S1Interop.S1InteropMember(\"{EscapeCSharpString(member.OwnerAlias)}\", \"{EscapeCSharpString(member.MemberName)}\", Alias = \"{EscapeCSharpString(member.Alias)}\"");
            if (!string.Equals(member.Kind, "S1Interop.S1InteropMemberKind.FieldOrProperty", StringComparison.Ordinal))
            {
                builder.Append($", Kind = {member.Kind}");
            }

            if (member.IsStatic)
            {
                builder.Append(", IsStatic = true");
            }

            builder.AppendLine(")]");
        }

        builder.AppendLine();
        builder.AppendLine("namespace SyntheticRealMod");
        builder.AppendLine("{");
        builder.AppendLine("    internal static class BackendNeutralRegistryProbe");
        builder.AppendLine("    {");
        if (aliasDeclarations.Length > 0)
        {
            SdkTypeAlias alias = aliasDeclarations[0];
            builder.AppendLine($"        public static object? Read{alias.Alias}Dynamically(object instance, string memberName) => S1Interop.Generated.S1InteropTypeRegistry.Get{alias.Alias}(instance, memberName);");
        }

        foreach (S1InteropMemberDeclaration member in memberDeclarations)
        {
            if (member.IsStatic)
            {
                builder.AppendLine($"        public static object? Read{member.Alias}() => {GetTypeFacadeName(member)}.Get{ToPascalIdentifier(member.MemberName)}();");
            }
            else
            {
                string parameterName = member.OwnerAlias.Equals("LandVehicle", StringComparison.Ordinal) ? "vehicle" : "instance";
                string facadeName = GetTypeFacadeName(member);
                builder.AppendLine($"        public static object? Read{member.Alias}(object {parameterName}) => {facadeName}.As({parameterName}).{ToPascalIdentifier(member.MemberName)};");
                builder.AppendLine($"        public static object? Read{member.Alias}Dynamically(object {parameterName}) => {facadeName}.Get({facadeName}.As({parameterName}), \"{EscapeCSharpString(member.MemberName)}\");");
            }
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private sealed record S1InteropMemberDeclaration(
        string OwnerAlias,
        string OwnerTypeName,
        string MemberName,
        string Alias,
        string Kind,
        bool IsStatic);

    private static string GetTypeFacadeName(S1InteropMemberDeclaration member)
    {
        string[] parts = member.OwnerTypeName
            .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeIdentifier)
            .ToArray();
        if (parts.Length == 0)
        {
            return $"S1Interop.{SanitizeIdentifier(member.OwnerAlias)}";
        }

        string typeName = parts[^1];
        IEnumerable<string> namespaceParts = parts.Take(parts.Length - 1);
        if (!parts[0].Equals("ScheduleOne", StringComparison.Ordinal))
        {
            namespaceParts = new[] { "Types" }.Concat(namespaceParts);
        }

        string namespaceSuffix = string.Join(".", namespaceParts);
        return string.IsNullOrWhiteSpace(namespaceSuffix)
            ? $"S1Interop.{typeName}"
            : $"S1Interop.{namespaceSuffix}.{typeName}";
    }

    private static string ToPascalIdentifier(string value)
    {
        string sanitized = SanitizeIdentifier(value);
        if (sanitized.Length == 1)
        {
            return sanitized.ToUpperInvariant();
        }

        return char.ToUpperInvariant(sanitized[0]) + sanitized[1..];
    }

    private static string SanitizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "RuntimeType";
        }

        var chars = value.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray();
        string sanitized = new(chars);
        return char.IsDigit(sanitized[0]) ? "_" + sanitized : sanitized;
    }

    private static string EscapeCSharpString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static int CountProjectImports(string projectPath, string importPath)
    {
        return XDocument.Load(projectPath).Root!.Elements()
            .Count(element =>
                string.Equals(element.Name.LocalName, "Import", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(element.Attribute("Project")?.Value, importPath, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsPath(IEnumerable<string> paths, string expectedPath)
    {
        string fullExpectedPath = Path.GetFullPath(expectedPath);
        return paths.Any(path => string.Equals(Path.GetFullPath(path), fullExpectedPath, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatDiagnostics(IEnumerable<InteropDiagnostic> diagnostics)
    {
        return string.Join(
            "; ",
            diagnostics.Select(diagnostic =>
                $"{diagnostic.RuleId}/{diagnostic.Configuration ?? "project"}: {diagnostic.Evidence ?? diagnostic.Message}"));
    }

    private static string FormatBuildResults(IEnumerable<MigrationBuildResult>? buildResults)
    {
        if (buildResults is null)
        {
            return "<none>";
        }

        return string.Join(
            "; ",
            buildResults.Select(result =>
                $"{result.Configuration}/{result.Runtime}: exit={result.ExitCode}, success={result.Success}, timedOut={result.TimedOut}, readiness={result.ReadinessStatus}, attribution={result.Attribution}, kind={result.FailureKind}, summary={result.Summary}, issues={string.Join("|", result.Issues.Select(issue => $"{issue.Kind}:{issue.Include}:{issue.Path}"))}, command={result.Command}, output={result.Output}"));
    }

    private static bool IsDependencyNotReadyBuildGate(MigrationVerificationResult result)
    {
        return !result.Success &&
               result.BuildResults is { Count: > 0 } &&
               result.BuildResults.Any(build => !build.Success) &&
               result.BuildResults.Where(build => !build.Success).All(build =>
                   build.Attribution.Equals("DependencyNotReady", StringComparison.OrdinalIgnoreCase) ||
                   build.ReadinessStatus.StartsWith("BlockedBy", StringComparison.OrdinalIgnoreCase));
    }

    private static string ExtractManifestPath(string output)
    {
        foreach (string line in output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            const string prefix = "Manifest:";
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return line[prefix.Length..].Trim();
            }
        }

        throw new InvalidOperationException($"Could not find manifest path in output: {output}");
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static ProcessResult RunDotNet(params string[] arguments)
    {
        return RunProcess("dotnet", arguments);
    }

    private static ProcessResult RunCli(params string[] arguments)
    {
        string cliAssembly = Path.Combine(AppContext.BaseDirectory, "S1Interop.Cli.dll");
        if (!File.Exists(cliAssembly))
        {
            throw new FileNotFoundException("Could not locate the built S1Interop CLI assembly next to the test host.", cliAssembly);
        }

        return RunDotNet([cliAssembly, .. arguments]);
    }

    private static ProcessResult RunProcess(string fileName, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo.FileName = fileName;
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.Start();

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(milliseconds: 120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{fileName} {string.Join(' ', arguments)} timed out.");
        }

        return new ProcessResult(process.ExitCode, output + error);
    }

    private string CreateLocalGeneratorPackageSource(string tempRoot)
    {
        string packageSource = Path.Combine(tempRoot, "NuGet");
        Directory.CreateDirectory(packageSource);
        string generatorProject = Path.Combine(RepositoryRoot, "src", "S1Interop.Generators", "S1Interop.Generators.csproj");
        ProcessResult pack = RunDotNet("pack", generatorProject, "-c", "Debug", "-o", packageSource, "--nologo", "-v:minimal");
        Assert(pack.ExitCode == 0, $"Packing local S1Interop.Generators feed should succeed. Output: {pack.Output}");
        return packageSource;
    }

    private static IReadOnlyList<string> ConfigurationDefines(string projectPath, string configuration)
    {
        XElement propertyGroup = GetConfigurationPropertyGroup(projectPath, configuration);
        XElement defineConstants = propertyGroup.Elements()
            .First(element => string.Equals(element.Name.LocalName, "DefineConstants", StringComparison.OrdinalIgnoreCase));
        return defineConstants.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string GetConditionedDefineConstants(XDocument document, string configurationPlatform)
    {
        XElement propertyGroup = document.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "PropertyGroup", StringComparison.OrdinalIgnoreCase))
            .First(element => (element.Attribute("Condition")?.Value ?? string.Empty).Contains(configurationPlatform, StringComparison.OrdinalIgnoreCase));
        return propertyGroup.Elements()
            .First(element => string.Equals(element.Name.LocalName, "DefineConstants", StringComparison.OrdinalIgnoreCase))
            .Value;
    }

    private static void ReduceBotanistFixCopyToMonoOnly(string projectPath)
    {
        XDocument document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        foreach (XElement element in document.Root!.Elements().ToArray())
        {
            string condition = element.Attribute("Condition")?.Value ?? string.Empty;
            if ((element.Name.LocalName.Equals("PropertyGroup", StringComparison.OrdinalIgnoreCase) ||
                 element.Name.LocalName.Equals("ItemGroup", StringComparison.OrdinalIgnoreCase)) &&
                condition.Contains("Il2cpp", StringComparison.OrdinalIgnoreCase))
            {
                element.Remove();
            }
        }
        XElement configurations = document.Descendants()
            .First(element => element.Name.LocalName.Equals("Configurations", StringComparison.OrdinalIgnoreCase));
        configurations.Value = "Mono;MonoRelease";

        const string monoReferenceCondition = "'$(Configuration)'=='Mono' Or '$(Configuration)'=='MonoRelease'";
        foreach (XElement itemGroup in document.Root.Elements()
                     .Where(element =>
                         element.Name.LocalName.Equals("ItemGroup", StringComparison.OrdinalIgnoreCase) &&
                         element.Attribute("Condition") is null &&
                         element.Elements().Any(child => child.Name.LocalName.Equals("Reference", StringComparison.OrdinalIgnoreCase))))
        {
            itemGroup.SetAttributeValue("Condition", monoReferenceCondition);
        }

        document.Save(projectPath);
    }

    private static void ReduceS1VoiceChatCopyToMonoOnly(string projectPath)
    {
        XDocument document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        foreach (XElement element in document.Root!.Elements().ToArray())
        {
            string condition = element.Attribute("Condition")?.Value ?? string.Empty;
            if ((element.Name.LocalName.Equals("PropertyGroup", StringComparison.OrdinalIgnoreCase) ||
                 element.Name.LocalName.Equals("ItemGroup", StringComparison.OrdinalIgnoreCase)) &&
                (condition.Contains("Il2CppMelon", StringComparison.OrdinalIgnoreCase) ||
                 condition.Contains("IL2CPPMELON", StringComparison.OrdinalIgnoreCase)))
            {
                element.Remove();
            }
        }
        foreach (XElement element in document.Descendants().ToArray())
        {
            string condition = element.Attribute("Condition")?.Value ?? string.Empty;
            if (condition.Contains("Il2CppMelon", StringComparison.OrdinalIgnoreCase) ||
                condition.Contains("IL2CPPMELON", StringComparison.OrdinalIgnoreCase))
            {
                element.Remove();
            }
        }

        XElement configurations = document.Descendants()
            .First(element => element.Name.LocalName.Equals("Configurations", StringComparison.OrdinalIgnoreCase));
        configurations.Value = "MonoMelon";

        XElement? targetFrameworks = document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName.Equals("TargetFrameworks", StringComparison.OrdinalIgnoreCase));
        if (targetFrameworks is not null)
        {
            targetFrameworks.Value = "netstandard2.1";
        }

        document.Save(projectPath);
    }

    private static void ReduceS1FuelModCopyToMonoOnly(string projectPath)
    {
        XDocument document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        foreach (XElement element in document.Root!.Elements().ToArray())
        {
            string condition = element.Attribute("Condition")?.Value ?? string.Empty;
            if ((element.Name.LocalName.Equals("PropertyGroup", StringComparison.OrdinalIgnoreCase) ||
                 element.Name.LocalName.Equals("ItemGroup", StringComparison.OrdinalIgnoreCase)) &&
                condition.Contains("IL2CPP", StringComparison.OrdinalIgnoreCase))
            {
                element.Remove();
            }
        }

        foreach (XElement import in document.Root.Elements()
                     .Where(element =>
                         element.Name.LocalName.Equals("Import", StringComparison.OrdinalIgnoreCase) &&
                         (element.Attribute("Project")?.Value ?? string.Empty).Contains("MelonIL2CPP.targets", StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            import.Remove();
        }

        XElement configurations = document.Descendants()
            .First(element => element.Name.LocalName.Equals("Configurations", StringComparison.OrdinalIgnoreCase));
        configurations.Value = "Debug Mono;Release Mono";

        document.Save(projectPath);

        string conditionsPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "build", "conditions.props");
        if (!File.Exists(conditionsPath))
        {
            return;
        }

        XDocument conditionsDocument = XDocument.Load(conditionsPath, LoadOptions.PreserveWhitespace);
        foreach (XElement element in conditionsDocument.Descendants().ToArray())
        {
            string condition = element.Attribute("Condition")?.Value ?? string.Empty;
            if (condition.Contains("IsMono", StringComparison.OrdinalIgnoreCase) &&
                condition.Contains("!= 'true'", StringComparison.OrdinalIgnoreCase))
            {
                element.Remove();
            }
        }

        conditionsDocument.Save(conditionsPath);
    }

    private static void RewriteHoverboardGamePaths(string projectPath, string monoGamePath, string il2CppGamePath)
    {
        XDocument document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        foreach (XElement propertyGroup in document.Root!.Elements()
                     .Where(element => element.Name.LocalName.Equals("PropertyGroup", StringComparison.OrdinalIgnoreCase)))
        {
            string condition = propertyGroup.Attribute("Condition")?.Value ?? string.Empty;
            XElement? s1Dir = propertyGroup.Elements()
                .FirstOrDefault(element => element.Name.LocalName.Equals("S1Dir", StringComparison.OrdinalIgnoreCase));
            if (s1Dir is null)
            {
                continue;
            }

            if (condition.Contains("MONO", StringComparison.OrdinalIgnoreCase))
            {
                s1Dir.Value = monoGamePath;
            }
            else if (condition.Contains("IL2CPP", StringComparison.OrdinalIgnoreCase))
            {
                s1Dir.Value = il2CppGamePath;
            }
        }

        document.Save(projectPath);
    }

    private static XElement GetConfigurationPropertyGroup(string projectPath, string configuration)
    {
        XDocument document = XDocument.Load(projectPath);
        return document.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "PropertyGroup", StringComparison.OrdinalIgnoreCase))
            .First(element => (element.Attribute("Condition")?.Value ?? string.Empty).Contains(configuration, StringComparison.OrdinalIgnoreCase));
    }

    private bool TryGetWorkspaceRoot(out string? root)
    {
        if (workspaceRoot is not null)
        {
            root = workspaceRoot;
            return true;
        }

        workspaceRoot = TryFindWorkspaceRoot();
        root = workspaceRoot;
        return root is not null;
    }

    private static string? TryFindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "S1Interop.sln")) &&
                Directory.Exists(Path.Combine(current.FullName, "src", "S1Interop.Cli")) &&
                Directory.Exists(Path.Combine(current.FullName, "src", "S1Interop.Generators")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string? TryFindWorkspaceRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "AlwaysJackpot")) &&
                Directory.Exists(Path.Combine(current.FullName, "GunsAlwaysAccurate")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static void CopyFixtureDirectory(string sourceDirectory, string targetDirectory)
    {
        foreach (string directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            if (ShouldSkipFixturePath(sourceDirectory, directory))
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
        }

        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            if (ShouldSkipFixturePath(sourceDirectory, file))
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(sourceDirectory, file);
            string destination = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    private static void DeleteDirectoryIfExists(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        foreach (string childDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(childDirectory, FileAttributes.Normal);
        }

        File.SetAttributes(directory, FileAttributes.Normal);
        Directory.Delete(directory, recursive: true);
    }

    private static bool ShouldSkipFixturePath(string sourceDirectory, string path)
    {
        string relativePath = Path.GetRelativePath(sourceDirectory, path);
        string[] parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part =>
            part.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".agent", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("s1interop-runs", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("S1Interop.Generated", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("target", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("Il2CppAssemblies", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("cpp2il_out", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("AssetRipper", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("AssetRipperExport", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("UnityExplorer", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("UniverseLib", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("Cpp2IL", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("Il2CppInterop", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("MelonLoader", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
