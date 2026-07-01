using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace S1Interop.Core;

public static class DualRuntimeProjectScaffolder
{
    private static readonly Regex ConfigurationConditionRegex = new(
        @"\$\(\s*Configuration\s*\)\s*'?\s*={1,2}\s*'?(?<name>[^'""\)\s]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool NeedsIl2CppConfigurations(ProjectAnalysis project) =>
        project.Configurations.Any(IsSourceMonoConfiguration) &&
        project.Configurations.All(configuration =>
            configuration.Runtime != RuntimeKind.Il2Cpp &&
            !IsIl2CppConfiguration(configuration.Name));

    public static bool IsSourceMonoConfiguration(ConfigurationAnalysis configuration) =>
        configuration.Runtime == RuntimeKind.Mono &&
        !IsIl2CppConfiguration(configuration.Name);

    public static bool Apply(XDocument document) =>
        Apply(document, monoConfigurations: null);

    public static bool Apply(XDocument document, IReadOnlyList<string>? monoConfigurations)
    {
        IReadOnlyList<string> configurations = GetConfigurationNames(document);
        string[] sourceMonoConfigurations = GetSourceMonoConfigurations(configurations, monoConfigurations);
        string[] newIl2CppConfigurations = sourceMonoConfigurations
            .Select(ToIl2CppConfigurationName)
            .Where(name => !configurations.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (newIl2CppConfigurations.Length == 0)
        {
            return false;
        }

        EnsureConfigurationsElement(document, configurations.Concat(newIl2CppConfigurations));
        EnsureIl2CppTargetFrameworks(document);
        EnsureIl2CppPathDefaults(document);
        ConditionUnconditionedReferenceGroups(document, sourceMonoConfigurations);

        foreach (string monoConfiguration in sourceMonoConfigurations)
        {
            string il2CppConfiguration = ToIl2CppConfigurationName(monoConfiguration);
            if (!newIl2CppConfigurations.Contains(il2CppConfiguration, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            AddIl2CppPropertyGroup(document, monoConfiguration, il2CppConfiguration);
            AddIl2CppItemGroups(document, monoConfiguration, il2CppConfiguration);
        }

        EnsureRuntimeGeneratorProperties(document, sourceMonoConfigurations, newIl2CppConfigurations);

        return true;
    }

    private static void EnsureIl2CppTargetFrameworks(XDocument document)
    {
        XElement? targetFrameworks = document.Descendants().FirstOrDefault(IsNamed("TargetFrameworks"));
        if (targetFrameworks is null)
        {
            return;
        }

        string[] frameworks = SplitMsBuildList(targetFrameworks.Value)
            .Append("net6.0")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        targetFrameworks.Value = string.Join(";", frameworks);
    }

    private static void ConditionUnconditionedReferenceGroups(XDocument document, IReadOnlyList<string> monoConfigurations)
    {
        if (monoConfigurations.Count == 0)
        {
            return;
        }

        string monoCondition = string.Join(
            " Or ",
            monoConfigurations.Select(configuration => $"'$(Configuration)'=='{configuration}'"));
        Func<XElement, bool> isReference = IsNamed("Reference");
        foreach (XElement itemGroup in document.Root!.Elements()
                     .Where(IsNamed("ItemGroup"))
                     .Where(group => group.Attribute("Condition") is null)
                     .ToArray())
        {
            XElement[] references = itemGroup.Elements()
                .Where(isReference)
                .ToArray();
            if (references.Length == 0)
            {
                continue;
            }

            if (itemGroup.Elements().All(isReference))
            {
                itemGroup.SetAttributeValue("Condition", monoCondition);
                continue;
            }

            var conditionedReferences = new XElement(
                "ItemGroup",
                new XAttribute("Condition", monoCondition),
                references.Select(reference => new XElement(reference)));
            foreach (XElement reference in references)
            {
                reference.Remove();
            }

            itemGroup.AddAfterSelf(conditionedReferences);
        }
    }

    private static string[] GetSourceMonoConfigurations(
        IReadOnlyList<string> projectConfigurations,
        IReadOnlyList<string>? analyzedMonoConfigurations)
    {
        string[] explicitMonoConfigurations = analyzedMonoConfigurations?
            .Where(configuration => projectConfigurations.Contains(configuration, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        return explicitMonoConfigurations.Length > 0
            ? explicitMonoConfigurations
            : projectConfigurations.Where(IsMonoConfiguration).ToArray();
    }

    private static IReadOnlyList<string> GetConfigurationNames(XDocument document)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (XElement element in document.Descendants().Where(IsNamed("Configurations")))
        {
            foreach (string part in SplitMsBuildList(element.Value))
            {
                names.Add(part);
            }
        }

        foreach (XAttribute condition in document.Descendants().Attributes("Condition"))
        {
            foreach (Match match in ConfigurationConditionRegex.Matches(condition.Value))
            {
                names.Add(match.Groups["name"].Value);
            }
        }

        return names.ToArray();
    }

    private static void EnsureConfigurationsElement(XDocument document, IEnumerable<string> configurations)
    {
        XElement? configurationsElement = document.Descendants().FirstOrDefault(IsNamed("Configurations"));
        if (configurationsElement is null)
        {
            XElement propertyGroup = document.Root!.Elements().FirstOrDefault(IsNamed("PropertyGroup"))
                ?? new XElement("PropertyGroup");
            if (propertyGroup.Parent is null)
            {
                document.Root!.AddFirst(propertyGroup);
            }

            configurationsElement = new XElement("Configurations");
            propertyGroup.Add(configurationsElement);
        }

        configurationsElement.Value = string.Join(";", configurations.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static void EnsureIl2CppPathDefaults(XDocument document)
    {
        XElement propertyGroup = document.Root!.Elements().Where(IsNamed("PropertyGroup"))
            .FirstOrDefault(group => group.Attribute("Condition") is null)
            ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent is null)
        {
            document.Root!.AddFirst(propertyGroup);
        }

        AddPropertyIfMissing(propertyGroup, "Il2CppClientGamePath", "$(Il2CppGamePath)", "'$(Il2CppClientGamePath)' == ''");
        AddPropertyIfMissing(propertyGroup, "Il2CppServerGamePath", "$(Il2CppGamePath)", "'$(Il2CppServerGamePath)' == ''");
    }

    private static void AddIl2CppPropertyGroup(XDocument document, string monoConfiguration, string il2CppConfiguration)
    {
        if (HasConfigurationPropertyGroup(document, il2CppConfiguration))
        {
            return;
        }

        XElement group = new("PropertyGroup", new XAttribute("Condition", $"'$(Configuration)'=='{il2CppConfiguration}'"));
        group.Add(new XElement("TargetFramework", "net6.0"));

        string role = GetRoleSymbol(il2CppConfiguration);
        string gamePathProperty = role switch
        {
            "CLIENT" => "$(Il2CppClientGamePath)",
            "SERVER" => "$(Il2CppServerGamePath)",
            _ => "$(Il2CppGamePath)"
        };

        group.Add(new XElement("GamePath", gamePathProperty));
        group.Add(new XElement("S1Dir", "$(GamePath)"));
        group.Add(new XElement("ManagedPath", @"$(GamePath)\MelonLoader\Il2CppAssemblies"));
        group.Add(new XElement("MelonLoaderPath", @"$(GamePath)\MelonLoader\net6"));
        group.Add(new XElement("ModsPath", @"$(GamePath)\Mods"));
        group.Add(new XElement("S1InteropTargetRuntime", "Il2Cpp"));
        group.Add(new XElement("DefineConstants", role.Length == 0 ? "IL2CPP" : $"IL2CPP;{role}"));
        group.Add(new XElement("AssemblyName", GetIl2CppAssemblyName(document, monoConfiguration, il2CppConfiguration)));
        group.Add(new XElement("OutputPath", @"bin\$(Configuration)\"));
        AddDeploymentPathDefaults(group, role);
        AddS1DsSearchPathDefaults(group, role);

        document.Root!.Add(group);
    }

    private static void EnsureRuntimeGeneratorProperties(
        XDocument document,
        IReadOnlyList<string> monoConfigurations,
        IReadOnlyList<string> il2CppConfigurations)
    {
        foreach (string monoConfiguration in monoConfigurations)
        {
            EnsureConfigurationProperty(document, monoConfiguration, "S1InteropTargetRuntime", "Mono");
        }

        foreach (string il2CppConfiguration in il2CppConfigurations)
        {
            EnsureConfigurationProperty(document, il2CppConfiguration, "S1InteropTargetRuntime", "Il2Cpp");
        }
    }

    private static void EnsureConfigurationProperty(
        XDocument document,
        string configuration,
        string propertyName,
        string value)
    {
        XElement? group = document.Root!.Elements()
            .Where(IsNamed("PropertyGroup"))
            .FirstOrDefault(group => ConditionMentionsConfiguration(group.Attribute("Condition")?.Value, configuration));
        if (group is null)
        {
            group = new XElement("PropertyGroup", new XAttribute("Condition", $"'$(Configuration)'=='{configuration}'"));
            document.Root.Add(group);
        }

        AddPropertyIfMissing(group, propertyName, value);
    }

    private static void AddIl2CppItemGroups(XDocument document, string monoConfiguration, string il2CppConfiguration)
    {
        XElement[] sourceGroups = document.Root!.Elements()
            .Where(IsNamed("ItemGroup"))
            .Where(group => ConditionMentionsConfiguration(group.Attribute("Condition")?.Value, monoConfiguration))
            .ToArray();

        foreach (XElement sourceGroup in sourceGroups)
        {
            XElement clone = new(sourceGroup);
            clone.SetAttributeValue("Condition", $"'$(Configuration)'=='{il2CppConfiguration}'");
            RewriteIl2CppReferences(clone, monoConfiguration, il2CppConfiguration);
            document.Root!.Add(clone);
        }

        if (!sourceGroups.Any(group => group.Descendants().Any(IsNamed("Reference"))))
        {
            XElement itemGroup = new("ItemGroup", new XAttribute("Condition", $"'$(Configuration)'=='{il2CppConfiguration}'"));
            AddCoreIl2CppReferences(itemGroup);
            document.Root!.Add(itemGroup);
            return;
        }

        XElement? firstClonedReferenceGroup = document.Root.Elements()
            .Where(IsNamed("ItemGroup"))
            .LastOrDefault(group =>
                ConditionMentionsConfiguration(group.Attribute("Condition")?.Value, il2CppConfiguration) &&
                group.Elements().Any(IsNamed("Reference")));
        if (firstClonedReferenceGroup is not null)
        {
            AddReferenceIfMissing(firstClonedReferenceGroup, "Il2CppInterop.Runtime", @"$(MelonLoaderPath)\Il2CppInterop.Runtime.dll");
        }
    }

    private static void RewriteIl2CppReferences(XElement itemGroup, string monoConfiguration, string il2CppConfiguration)
    {
        foreach (XElement reference in itemGroup.Elements().Where(IsNamed("Reference")))
        {
            string include = reference.Attribute("Include")?.Value ?? string.Empty;
            string rewrittenInclude = RewriteReferenceInclude(include, monoConfiguration, il2CppConfiguration);
            reference.SetAttributeValue("Include", rewrittenInclude);

            XElement? hintPath = reference.Elements().FirstOrDefault(IsNamed("HintPath"));
            if (hintPath is not null)
            {
                hintPath.Value = RewriteReferenceHintPath(hintPath.Value, include, rewrittenInclude, monoConfiguration, il2CppConfiguration);
            }

            EnsurePrivateFalse(reference);
        }

        AddCoreIl2CppReferences(itemGroup);
    }

    private static string RewriteReferenceInclude(string include, string monoConfiguration, string il2CppConfiguration)
    {
        if (include.StartsWith("DedicatedServerMod_Mono", StringComparison.OrdinalIgnoreCase))
        {
            return include.Replace("Mono", "Il2cpp", StringComparison.OrdinalIgnoreCase);
        }

        if (include.Equals("FishNet.Runtime", StringComparison.OrdinalIgnoreCase))
        {
            return "Il2CppFishNet.Runtime";
        }

        if (include.Equals("com.rlabrecque.steamworks.net", StringComparison.OrdinalIgnoreCase))
        {
            return "Il2Cppcom.rlabrecque.steamworks.net";
        }

        if (include.StartsWith("ScheduleOne", StringComparison.OrdinalIgnoreCase))
        {
            return $"Il2Cpp{include}";
        }

        return include
            .Replace(monoConfiguration, il2CppConfiguration, StringComparison.OrdinalIgnoreCase)
            .Replace("Mono", "Il2cpp", StringComparison.OrdinalIgnoreCase);
    }

    private static string RewriteReferenceHintPath(
        string hintPath,
        string originalInclude,
        string rewrittenInclude,
        string monoConfiguration,
        string il2CppConfiguration)
    {
        string rewritten = hintPath
            .Replace(@"Schedule I_Data\Managed", @"MelonLoader\Il2CppAssemblies", StringComparison.OrdinalIgnoreCase)
            .Replace(@"/Schedule I_Data/Managed", @"/MelonLoader/Il2CppAssemblies", StringComparison.OrdinalIgnoreCase)
            .Replace("$(MonoAssembliesPath)", "$(ManagedPath)", StringComparison.OrdinalIgnoreCase)
            .Replace("$(MonoManagedPath)", "$(ManagedPath)", StringComparison.OrdinalIgnoreCase)
            .Replace("$(MonoManagedDllPath)", "$(ManagedPath)", StringComparison.OrdinalIgnoreCase)
            .Replace("$(SteamworksManagedPath)", "$(ManagedPath)", StringComparison.OrdinalIgnoreCase)
            .Replace("$(MelonLoaderMonoAssembliesPath)", "$(MelonLoaderPath)", StringComparison.OrdinalIgnoreCase)
            .Replace("$(MonoMelonLoaderPath)", "$(MelonLoaderPath)", StringComparison.OrdinalIgnoreCase)
            .Replace("$(MelonLoaderNet35Path)", "$(MelonLoaderPath)", StringComparison.OrdinalIgnoreCase)
            .Replace("net35", "net6", StringComparison.OrdinalIgnoreCase)
            .Replace(monoConfiguration, il2CppConfiguration, StringComparison.OrdinalIgnoreCase)
            .Replace("Mono", "Il2cpp", StringComparison.OrdinalIgnoreCase)
            .Replace("Il2cppAssemblyPath", "Il2CppAssemblyPath", StringComparison.OrdinalIgnoreCase)
            .Replace($"{originalInclude}.dll", $"{rewrittenInclude}.dll", StringComparison.OrdinalIgnoreCase);

        if (originalInclude.Equals("FishNet.Runtime", StringComparison.OrdinalIgnoreCase) &&
            !rewritten.Contains("Il2CppFishNet.Runtime.dll", StringComparison.OrdinalIgnoreCase))
        {
            rewritten = rewritten.Replace("FishNet.Runtime.dll", "Il2CppFishNet.Runtime.dll", StringComparison.OrdinalIgnoreCase);
        }

        return rewritten;
    }

    private static void AddCoreIl2CppReferences(XElement itemGroup)
    {
        AddReferenceIfMissing(itemGroup, "MelonLoader", @"$(MelonLoaderPath)\MelonLoader.dll");
        AddReferenceIfMissing(itemGroup, "0Harmony", @"$(MelonLoaderPath)\0Harmony.dll");
        AddReferenceIfMissing(itemGroup, "Il2CppInterop.Runtime", @"$(MelonLoaderPath)\Il2CppInterop.Runtime.dll");
        AddReferenceIfMissing(itemGroup, "Il2Cppmscorlib", @"$(ManagedPath)\Il2Cppmscorlib.dll");
        AddReferenceIfMissing(itemGroup, "Assembly-CSharp", @"$(ManagedPath)\Assembly-CSharp.dll");
        AddReferenceIfMissing(itemGroup, "UnityEngine.CoreModule", @"$(ManagedPath)\UnityEngine.CoreModule.dll");
    }

    private static void AddReferenceIfMissing(XElement itemGroup, string include, string hintPath)
    {
        bool exists = itemGroup.Elements().Where(IsNamed("Reference")).Any(reference =>
            string.Equals(reference.Attribute("Include")?.Value, include, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            return;
        }

        itemGroup.Add(
            new XElement("Reference",
                new XAttribute("Include", include),
                new XElement("HintPath", hintPath),
                new XElement("Private", "false")));
    }

    private static void AddPropertyIfMissing(XElement propertyGroup, string name, string value, string? condition = null)
    {
        bool exists = propertyGroup.Elements().Any(element => string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            return;
        }

        XElement element = new(name, value);
        if (condition is not null)
        {
            element.SetAttributeValue("Condition", condition);
        }

        propertyGroup.Add(element);
    }

    private static void AddDeploymentPathDefaults(XElement group, string role)
    {
        if (role.Equals("CLIENT", StringComparison.Ordinal))
        {
            group.Add(new XElement("DeploymentPath", new XAttribute("Condition", "'$(Il2CppClientDeploymentPath)' != ''"), "$(Il2CppClientDeploymentPath)"));
            group.Add(new XElement("DeploymentPath", new XAttribute("Condition", "'$(ClientDeploymentPath)' != ''"), "$(ClientDeploymentPath)"));
        }
        else if (role.Equals("SERVER", StringComparison.Ordinal))
        {
            group.Add(new XElement("DeploymentPath", new XAttribute("Condition", "'$(Il2CppServerDeploymentPath)' != ''"), "$(Il2CppServerDeploymentPath)"));
            group.Add(new XElement("DeploymentPath", new XAttribute("Condition", "'$(ServerDeploymentPath)' != ''"), "$(ServerDeploymentPath)"));
        }

        group.Add(new XElement("DeploymentPath", new XAttribute("Condition", "'$(DeploymentPath)' == ''"), "$(ModsPath)"));
    }

    private static void AddS1DsSearchPathDefaults(XElement group, string role)
    {
        if (role.Equals("CLIENT", StringComparison.Ordinal))
        {
            group.Add(new XElement("S1DSModSearchPath", new XAttribute("Condition", "'$(Il2CppClientDeploymentPath)' != ''"), "$(Il2CppClientDeploymentPath)"));
            group.Add(new XElement("S1DSModSearchPath", new XAttribute("Condition", "'$(ClientDeploymentPath)' != ''"), "$(ClientDeploymentPath)"));
        }
        else if (role.Equals("SERVER", StringComparison.Ordinal))
        {
            group.Add(new XElement("S1DSModSearchPath", new XAttribute("Condition", "'$(Il2CppServerDeploymentPath)' != ''"), "$(Il2CppServerDeploymentPath)"));
            group.Add(new XElement("S1DSModSearchPath", new XAttribute("Condition", "'$(ServerDeploymentPath)' != ''"), "$(ServerDeploymentPath)"));
        }

        group.Add(new XElement("S1DSModSearchPath", new XAttribute("Condition", "'$(S1DSModSearchPath)' == ''"), "$(ModsPath)"));
    }

    private static void EnsurePrivateFalse(XElement reference)
    {
        XElement? privateElement = reference.Elements().FirstOrDefault(IsNamed("Private"));
        if (privateElement is null)
        {
            reference.Add(new XElement("Private", "false"));
            return;
        }

        privateElement.Value = "false";
    }

    private static string GetIl2CppAssemblyName(XDocument document, string monoConfiguration, string il2CppConfiguration)
    {
        XElement? monoGroup = document.Descendants()
            .Where(IsNamed("PropertyGroup"))
            .FirstOrDefault(group => ConditionMentionsConfiguration(group.Attribute("Condition")?.Value, monoConfiguration));
        string? monoAssemblyName = monoGroup?.Elements().FirstOrDefault(IsNamed("AssemblyName"))?.Value.Trim();
        if (!string.IsNullOrWhiteSpace(monoAssemblyName))
        {
            return monoAssemblyName.Replace("Mono", "Il2cpp", StringComparison.OrdinalIgnoreCase);
        }

        string rootAssemblyName = document.Root!.Elements().Where(IsNamed("PropertyGroup"))
            .SelectMany(group => group.Elements().Where(IsNamed("AssemblyName")))
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? "$(AssemblyName)";
        string suffix = GetRoleSymbol(il2CppConfiguration) switch
        {
            "CLIENT" => "_Il2cpp_Client",
            "SERVER" => "_Il2cpp_Server",
            _ => "_Il2cpp"
        };

        return rootAssemblyName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? rootAssemblyName
            : $"{rootAssemblyName}{suffix}";
    }

    private static bool HasConfigurationPropertyGroup(XDocument document, string configuration) =>
        document.Descendants()
            .Where(IsNamed("PropertyGroup"))
            .Any(group => ConditionMentionsConfiguration(group.Attribute("Condition")?.Value, configuration));

    private static string ToIl2CppConfigurationName(string monoConfiguration)
    {
        if (monoConfiguration.Contains("Mono", StringComparison.OrdinalIgnoreCase))
        {
            return Regex.Replace(monoConfiguration, "Mono", "Il2cpp", RegexOptions.IgnoreCase);
        }

        return $"Il2cpp_{monoConfiguration}";
    }

    private static bool IsMonoConfiguration(string configuration) =>
        configuration.Contains("mono", StringComparison.OrdinalIgnoreCase);

    private static bool IsIl2CppConfiguration(string configuration) =>
        configuration.Contains("il2cpp", StringComparison.OrdinalIgnoreCase);

    private static string GetRoleSymbol(string configuration)
    {
        if (configuration.Contains("client", StringComparison.OrdinalIgnoreCase))
        {
            return "CLIENT";
        }

        if (configuration.Contains("server", StringComparison.OrdinalIgnoreCase))
        {
            return "SERVER";
        }

        return string.Empty;
    }

    private static bool ConditionMentionsConfiguration(string? condition, string configuration)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return false;
        }

        return ConfigurationConditionRegex.Matches(condition).Any(match =>
            string.Equals(match.Groups["name"].Value, configuration, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> SplitMsBuildList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static Func<XElement, bool> IsNamed(string localName) =>
        element => string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);
}
