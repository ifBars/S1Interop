[CmdletBinding()]
param(
    [string]$PackageDirectory = (Join-Path $PSScriptRoot '..\artifacts\packages'),
    [string]$SdkVersion = '8.0.100'
)

$ErrorActionPreference = 'Stop'
$packageRoot = (Resolve-Path -LiteralPath $PackageDirectory).Path
$repoRoot = Split-Path $PSScriptRoot -Parent
$cliProject = [xml](Get-Content -Raw (Join-Path $repoRoot 'src\S1Interop.Cli\S1Interop.Cli.csproj'))
$generatorProject = [xml](Get-Content -Raw (Join-Path $repoRoot 'src\S1Interop.Generators\S1Interop.Generators.csproj'))
$version = [string]$cliProject.Project.PropertyGroup.Version
if ($version -ne [string]$generatorProject.Project.PropertyGroup.Version) {
    throw 'CLI and generator package versions must match.'
}
foreach ($id in @('S1Interop', 'S1Interop.Generators')) {
    if (!(Test-Path -LiteralPath (Join-Path $packageRoot "$id.$version.nupkg"))) {
        throw "Pack $id $version before running this check."
    }
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$smokeRoot = Join-Path $tempRoot ('S1Interop-package-smoke-' + [guid]::NewGuid().ToString('N'))
$previousPackages = $env:NUGET_PACKAGES

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable failed with exit code $LASTEXITCODE."
    }
}

New-Item -ItemType Directory -Path $smokeRoot | Out-Null
Push-Location $smokeRoot
try {
    # Pin the consumer compiler too: a newer local SDK can hide an unloadable generator.
    @{ sdk = @{ version = $SdkVersion; rollForward = 'latestFeature'; allowPrerelease = $false } } |
        ConvertTo-Json -Depth 3 | Set-Content global.json
    $env:NUGET_PACKAGES = Join-Path $smokeRoot 'packages'
    $escapedSource = [System.Security.SecurityElement]::Escape($packageRoot)
    @"
<configuration>
  <packageSources><clear /><add key="candidate" value="$escapedSource" /></packageSources>
</configuration>
"@ | Set-Content NuGet.Config

    Invoke-Checked dotnet @('--version')
    Invoke-Checked dotnet @('tool', 'install', 'S1Interop', '--tool-path', '.tools', '--configfile', 'NuGet.Config', '--version', $version)
    $tool = Join-Path $smokeRoot '.tools\s1interop.exe'
    $versionOutput = & $tool --version
    if ($LASTEXITCODE -ne 0 -or "$versionOutput" -notlike "S1Interop $version*") {
        throw "Packaged CLI reported an unexpected version: $versionOutput"
    }
    Write-Output $versionOutput
    Invoke-Checked $tool @('--help')
    Invoke-Checked $tool @('new', '--legacy-generator', 'MyFirstMod')
    if (Test-Path MyFirstMod) { throw 'new without --apply wrote files.' }
    Invoke-Checked $tool @('new', '--legacy-generator', 'MyFirstMod', '--apply')

    # Compile the documented starter with a tiny loader contract. This checks the actual
    # packaged generator and sample together without redistributing any game assemblies.
    New-Item -ItemType Directory -Path Consumer | Out-Null
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>10.0</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="S1Interop.Generators" Version="$version" PrivateAssets="all" />
    <!-- Exercises the packaged build targets: the starter imports NPCManager without a using directive. -->
    <S1InteropUsing Include="ScheduleOne.NPCs" />
  </ItemGroup>
</Project>
"@ | Set-Content Consumer/Consumer.csproj
    @'
namespace MelonLoader
{
    public sealed class MelonInfoAttribute : System.Attribute
    {
        public MelonInfoAttribute(System.Type type, string name, string version, string author) { }
    }
    public sealed class MelonGameAttribute : System.Attribute
    {
        public MelonGameAttribute(string developer, string name) { }
    }
    public abstract class MelonMod
    {
        public Logger LoggerInstance { get; } = new Logger();
        public virtual void OnInitializeMelon() { }
        public virtual void OnUpdate() { }
    }
    public sealed class Logger { public void Msg(string message) { } }
}

namespace UnityEngine
{
    public enum KeyCode { F8 = 289 }
    public static class Input { public static bool GetKeyDown(KeyCode key) => false; }
}

namespace ScheduleOne.NPCs
{
    public static class NPCManager
    {
        public static System.Collections.Generic.List<object> NPCRegistry { get; } = new System.Collections.Generic.List<object>();
    }
}
'@ | Set-Content Consumer/MelonLoader.cs
    Copy-Item -LiteralPath (Join-Path $smokeRoot 'MyFirstMod\ModCore.cs') -Destination Consumer/ModCore.cs
    Invoke-Checked dotnet @('build', 'Consumer/Consumer.csproj', '-c', 'Release', '--nologo', '--configfile', 'NuGet.Config', '-p:UseSharedCompilation=false')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\docfx\samples\first-mod\ModCore.cs') -Destination Consumer/ModCore.cs
    Invoke-Checked dotnet @('build', 'Consumer/Consumer.csproj', '-c', 'Release', '--no-restore', '--nologo', '-p:UseSharedCompilation=false')
    Write-Output 'Package smoke passed: isolated CLI install, dry-run, scaffold, and documented starter compilation.'
}
finally {
    Pop-Location
    $env:NUGET_PACKAGES = $previousPackages
    $resolvedSmokeRoot = [IO.Path]::GetFullPath($smokeRoot)
    if (!$resolvedSmokeRoot.StartsWith($tempRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path $resolvedSmokeRoot -Leaf) -notlike 'S1Interop-package-smoke-*') {
        throw "Refusing to clean unexpected temporary path: $resolvedSmokeRoot"
    }
    Remove-Item -LiteralPath $resolvedSmokeRoot -Recurse -Force
}
