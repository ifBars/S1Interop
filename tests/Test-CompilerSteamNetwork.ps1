#requires -Version 7
param(
    [Parameter(Mandatory)][string] $SourceRoot,
    [Parameter(Mandatory)][string] $SourcesList,
    [Parameter(Mandatory)][string] $MonoGamePath,
    [Parameter(Mandatory)][string] $Il2CppGamePath,
    [ValidatePattern('^[A-Za-z_][A-Za-z0-9_]*$')][string[]] $AuthoringDefines = @(),
    [ValidateRange(1, 10000)][int] $ExpectedChecks = 27,
    [switch] $RunRuntime
)

$ErrorActionPreference = 'Stop'
if (@($AuthoringDefines | Where-Object { $_ -in @('MONO', 'IL2CPP') }).Count) {
    throw 'Runtime symbols are selected by the compiler; AuthoringDefines is for feature symbols only.'
}
$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceDirectory = [IO.Path]::GetFullPath($SourceRoot)
$mono = (Get-Item -LiteralPath $MonoGamePath).FullName
$native = (Get-Item -LiteralPath $Il2CppGamePath).FullName
$sources = @(Get-Content -LiteralPath $SourcesList | Where-Object { ![string]::IsNullOrWhiteSpace($_) })
if (!$sources.Count) { throw 'Supply a nonempty audited source manifest.' }
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$fingerprints = @($sources | ForEach-Object {
    if (![IO.Path]::IsPathFullyQualified($_)) { throw "Source manifest entries must be absolute: $_" }
    $path = [IO.Path]::GetFullPath($_)
    $relative = [IO.Path]::GetRelativePath($sourceDirectory, $path)
    if ($relative -eq '..' -or $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar) -or
        [IO.Path]::IsPathRooted($relative) -or [IO.Path]::GetExtension($path) -ne '.cs') {
        throw "Source must be a C# file within SourceRoot: $path"
    }
    if (!$seen.Add($path)) { throw "Duplicate source manifest entry: $path" }
    [ordered]@{ Path=$path; RelativePath=$relative; Hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
})

$id = [guid]::NewGuid().ToString('N')
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$work = Join-Path $temporaryParent "S1Interop-steam-$id"
$evidence = Join-Path $repoRoot "artifacts/compiler-steam/$id"
New-Item -ItemType Directory -Path $evidence | Out-Null
$report = [ordered]@{
    SourceCount=$fingerprints.Count; Sources=$fingerprints
    AuthoringDefines=$AuthoringDefines
    ExpectedChecks=$ExpectedChecks
    ManifestSha256=(Get-FileHash -LiteralPath $SourcesList -Algorithm SHA256).Hash
    RuntimeRequested=[bool]$RunRuntime; Builds=@(); RuntimeResults=@()
    EvidenceMode=$(if ($RunRuntime) { 'Compilation and local transport override' } else { 'Compilation only' })
    BuildsPassed=$false; RuntimePassed=$null; CleanupSucceeded=$false
    OriginalsUnchanged=$false; Passed=$false; Failure=$null
}

function Invoke-Build {
    param([string[]] $Arguments, [string] $Log)
    & dotnet @Arguments '-nr:false' '-p:UseSharedCompilation=false' *> $Log
    if ($LASTEXITCODE -ne 0) { throw "Build failed; inspect $Log" }
}

Push-Location -LiteralPath $repoRoot
try {
    New-Item -ItemType Directory -Path $work | Out-Null
    @{ sdk=@{ version='8.0.100'; rollForward='latestFeature'; allowPrerelease=$false } } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $work 'global.json')
    $library = Join-Path $work 'Library'
    $probe = Join-Path $work 'Probe'
    New-Item -ItemType Directory -Path $library, $probe | Out-Null
    foreach ($source in $fingerprints) {
        $destination = Join-Path $library (Join-Path 'Source' $source.RelativePath)
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $source.Path -Destination $destination
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $source.Hash) {
            throw "Source changed while copying: $($source.Path)"
        }
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'S1Interop.Compiler.SteamNetworkSmoke/RuntimeChecks.cs') -Destination $library
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'S1Interop.Compiler.SteamNetworkSmoke/Mod.cs') -Destination $probe
    $props = [Security.SecurityElement]::Escape((Join-Path $repoRoot 'build/S1Interop.Compiler.props'))
    $targets = [Security.SecurityElement]::Escape((Join-Path $repoRoot 'build/S1Interop.Compiler.targets'))
    $defines = [Security.SecurityElement]::Escape(($AuthoringDefines -join ';'))
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="$props" />
  <PropertyGroup>
    <AssemblyName>SteamNetworkLib</AssemblyName>
    <DefineConstants>`$(DefineConstants);$defines</DefineConstants>
    <LangVersion>latest</LangVersion><Nullable>enable</Nullable><IsPackable>false</IsPackable>
  </PropertyGroup>
  <Import Project="$targets" />
</Project>
"@ | Set-Content -LiteralPath (Join-Path $library 'Library.csproj')
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="$props" />
  <PropertyGroup>
    <AssemblyName>S1Interop.SteamNetworkSmoke</AssemblyName>
    <LangVersion>latest</LangVersion><Nullable>enable</Nullable><IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
  <Import Project="$targets" />
</Project>
"@ | Set-Content -LiteralPath (Join-Path $probe 'Probe.csproj')
    Invoke-Build @('build', (Join-Path $repoRoot 'src/S1Interop.Cli'), '-c', 'Release', '-v', 'quiet') (Join-Path $evidence 'compiler-build.log')
    $report.CompilerSha256 = (Get-FileHash -LiteralPath (Join-Path $repoRoot 'src/S1Interop.Cli/bin/Release/net8.0/S1Interop.Cli.dll')).Hash
    foreach ($runtime in @('Mono', 'Il2Cpp')) {
        Write-Output "Building unchanged SteamNetworkLib source: $runtime ($($sources.Count) files)"
        Invoke-Build @('build', (Join-Path $probe 'Probe.csproj'), '-c', $runtime,
            "-p:MonoGamePath=$mono", "-p:Il2CppGamePath=$native", '-v', 'quiet') (Join-Path $evidence "$runtime-build.log")
        $framework = if ($runtime -eq 'Mono') { 'netstandard2.1' } else { 'net6.0' }
        $output = Join-Path $probe "bin/$runtime/$runtime/$framework"
        $mod = Join-Path $output 'S1Interop.SteamNetworkSmoke.dll'
        $report.Builds += [ordered]@{
            Runtime=$runtime; ModSha256=(Get-FileHash -LiteralPath $mod).Hash
            LibrarySha256=(Get-FileHash -LiteralPath (Join-Path $output 'SteamNetworkLib.dll')).Hash
        }
        if ($runtime -eq 'Il2Cpp') {
            Copy-Item -LiteralPath (Join-Path $probe "obj/$runtime/$runtime/$framework/s1interop-lowered/versions.json") -Destination $evidence
            $libraryEvidence = Join-Path $evidence 'library-lowering'
            New-Item -ItemType Directory -Path $libraryEvidence | Out-Null
            foreach ($name in @('author-sources.list', 'versions.json')) {
                Copy-Item -LiteralPath (Join-Path $library "obj/$runtime/$runtime/$framework/s1interop-lowered/$name") -Destination $libraryEvidence
            }
            $boundSources = @(Get-Content -LiteralPath (Join-Path $libraryEvidence 'author-sources.list'))
            foreach ($source in $fingerprints) {
                $expectedSource = Join-Path $library (Join-Path 'Source' $source.RelativePath)
                if ($boundSources -notcontains $expectedSource) { throw "Audited source was absent from compiler inputs: $($source.RelativePath)" }
            }
        }
        if ($RunRuntime) {
            $game = if ($runtime -eq 'Mono') { $mono } else { $native }
            $runtimeLog = Join-Path $evidence "$runtime-runtime.log"
            $runtimeFailure = $null
            try {
                & (Join-Path $PSScriptRoot 'Run-CompilerRuntimeSmoke.ps1') -GamePath $game -ModPath $mod -Runtime $runtime -ProbeLibraryName SteamNetworkLib.dll `
                    -ProbeDisplayName 'S1Interop SteamNetwork Runtime Smoke' -ExpectedChecks $ExpectedChecks | Tee-Object -FilePath $runtimeLog
            } catch { $runtimeFailure = $_.Exception.Message }
            $messages = @(Get-Content -LiteralPath $runtimeLog -ErrorAction SilentlyContinue)
            $resultPath = @($messages | Where-Object { $_ -is [string] -and $_.StartsWith('EVIDENCE ') })
            if ($resultPath.Count -eq 1) {
                $runtimeResult = Get-Content -LiteralPath (Join-Path $resultPath[0].Substring(9) 'result.json') -Raw | ConvertFrom-Json
                $report.RuntimeResults += $runtimeResult
                if (!$runtimeResult.Passed -and !$runtimeFailure) { $runtimeFailure = 'Runtime result did not pass.' }
            } elseif (!$runtimeFailure) { $runtimeFailure = 'Runtime runner did not return one evidence directory.' }
            if ($runtimeFailure) { throw $runtimeFailure }
        }
    }
    $report.Passed = $true
} catch {
    $report.Failure = $_.Exception.Message
} finally {
    try {
        $report.OriginalsUnchanged = @($fingerprints | Where-Object {
            !(Test-Path -LiteralPath $_.Path) -or (Get-FileHash -LiteralPath $_.Path -Algorithm SHA256).Hash -ne $_.Hash
        }).Count -eq 0
        if (!$report.OriginalsUnchanged) { throw 'Original source hashes changed during verification.' }
    } catch { $report.Passed=$false; $report.Failure += " Source verification: $($_.Exception.Message)" }
    try {
        # Only remove the unique workspace this invocation created, never a supplied source path.
        $resolved = [IO.Path]::GetFullPath($work)
        if ($resolved -ne (Join-Path $temporaryParent "S1Interop-steam-$id")) { throw 'Unexpected cleanup path.' }
        if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
        $report.CleanupSucceeded = $true
    } catch { $report.Passed=$false; $report.Failure += " Cleanup: $($_.Exception.Message)" }
    $report.BuildsPassed = $report.Builds.Count -eq 2
    if ($RunRuntime) { $report.RuntimePassed = $report.RuntimeResults.Count -eq 2 -and @($report.RuntimeResults | Where-Object { !$_.Passed }).Count -eq 0 }
    Pop-Location
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'result.json')
    Write-Output "STEAM-COMPILER-EVIDENCE $evidence"
}
if (!$report.Passed) { throw $report.Failure }
