param(
    [string] $PackageDirectory = 'artifacts/compiler-packages',
    [string] $MonoGamePath,
    [string] $Il2CppGamePath,
    [string] $SdkVersion = '8.0.100'
)

$ErrorActionPreference = 'Stop'
if ([bool]$MonoGamePath -ne [bool]$Il2CppGamePath) { throw 'Supply both game paths or neither.' }
$repoRoot = Split-Path -Parent $PSScriptRoot
$feed = [IO.Path]::GetFullPath($PackageDirectory)
$version = ([xml](Get-Content (Join-Path $repoRoot 'src/S1Interop.Cli/S1Interop.Cli.csproj') -Raw)).Project.PropertyGroup.Version
$package = Join-Path $feed "S1Interop.$version.nupkg"
if (!(Test-Path -LiteralPath $package)) { throw "Pack the candidate first: $package" }
$root = Join-Path ([IO.Path]::GetTempPath()) ('S1Interop-compiler-package-' + [guid]::NewGuid().ToString('N'))
$previousPackages = $env:NUGET_PACKAGES
$previousHome = $env:DOTNET_CLI_HOME

function Invoke-Checked {
    param([string] $Command, [string[]] $Arguments)
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}

New-Item -ItemType Directory -Path $root | Out-Null
Push-Location -LiteralPath $root
try {
    $env:NUGET_PACKAGES = Join-Path $root 'cache'
    $env:DOTNET_CLI_HOME = Join-Path $root 'home'
    @{ sdk = @{ version = $SdkVersion; rollForward = 'latestFeature'; allowPrerelease = $false } } |
        ConvertTo-Json -Depth 3 | Set-Content global.json
    $escapedFeed = [Security.SecurityElement]::Escape($feed)
    Set-Content NuGet.Config "<configuration><packageSources><clear/><add key=`"candidate`" value=`"$escapedFeed`"/></packageSources></configuration>"
    Invoke-Checked dotnet @('tool', 'install', 'S1Interop', '--version', $version, '--tool-path', '.tools', '--configfile', 'NuGet.Config')
    $tool = Join-Path $root '.tools/s1interop.exe'
    Invoke-Checked $tool @('new', 'Fresh Mod', '--format', 'json')
    if (Test-Path -LiteralPath 'Fresh Mod') { throw 'Preview wrote files.' }
    Invoke-Checked $tool @('new', 'Fresh Mod', '--apply')
    $project = Join-Path $root 'Fresh Mod'
    $manifest = Get-Content (Join-Path $project '.config/dotnet-tools.json') -Raw | ConvertFrom-Json
    if ($manifest.tools.s1interop.version -ne $version) { throw 'Scaffold pinned a different tool version.' }
    foreach ($asset in @('props', 'targets')) {
        $actual = (Get-FileHash -LiteralPath (Join-Path $project ".s1interop/S1Interop.Compiler.$asset")).Hash
        $expected = (Get-FileHash -LiteralPath (Join-Path $repoRoot "build/S1Interop.Compiler.$asset")).Hash
        if ($actual -ne $expected) { throw "Packaged $asset asset differs from the source being validated." }
    }
    Push-Location -LiteralPath $project
    try {
        Invoke-Checked dotnet @('tool', 'restore', '--configfile', (Join-Path $root 'NuGet.Config'))
        Invoke-Checked dotnet @('tool', 'run', 's1interop', '--', 'compiler', '--help')
        # Reference preparation exercises Roslyn/Cecil dependencies from the installed
        # package without requiring redistributable game assemblies in CI.
        $core = Get-ChildItem -LiteralPath (Join-Path $root '.tools') -Recurse -File -Filter S1Interop.Core.dll | Select-Object -First 1
        if (!$core) { throw 'Packaged Core assembly missing.' }
        Set-Content (Join-Path $root 'references.list') $core.FullName
        Invoke-Checked dotnet @('tool', 'run', 's1interop', '--', 'compiler', 'prepare-references', '--references',
            (Join-Path $root 'references.list'), '--output', (Join-Path $root 'prepared'))
        if (!(Test-Path -LiteralPath (Join-Path $root 'prepared/references.list'))) { throw 'Reference preparation output missing.' }
        if ($MonoGamePath) {
            Invoke-Checked dotnet @('tool', 'run', 's1interop', '--', 'setup', '.', '--mono-game-path', $MonoGamePath,
                '--il2cpp-game-path', $Il2CppGamePath, '--apply')
            Invoke-Checked dotnet @('tool', 'run', 's1interop', '--', 'doctor', '.')
            $localProperties = ([xml](Get-Content local.build.props -Raw)).Project.PropertyGroup
            if ($localProperties.MonoGamePath -ne [IO.Path]::GetFullPath($MonoGamePath) -or
                $localProperties.Il2CppGamePath -ne [IO.Path]::GetFullPath($Il2CppGamePath)) {
                throw 'Setup did not persist the selected installations.'
            }
            foreach ($runtime in @('Mono', 'Il2Cpp')) {
                Invoke-Checked dotnet @('build', '-c', 'Release', "-p:S1InteropCompilerRuntime=$runtime",
                    '-v', 'quiet')
                $framework = if ($runtime -eq 'Mono') { 'netstandard2.1' } else { 'net6.0' }
                $output = Join-Path $project "bin/Release/$runtime/$framework/FreshMod.dll"
                if (!(Test-Path -LiteralPath $output)) { throw "Expected output missing: $output" }
                Write-Output "BUILD-PASS runtime=$runtime hash=$((Get-FileHash -LiteralPath $output).Hash); compile evidence only."
            }
        }
    }
    finally { Pop-Location }
    $scope = if ($MonoGamePath) { 'setup, doctor, and both real-reference builds from saved configuration' } else { 'without game builds' }
    Write-Output "COMPILER-PACKAGE-PASS version=$version packageHash=$((Get-FileHash -LiteralPath $package).Hash); isolated install, scaffold, local restore, reference preparation $scope."
}
finally {
    Pop-Location
    $env:NUGET_PACKAGES = $previousPackages
    $env:DOTNET_CLI_HOME = $previousHome
    $resolved = [IO.Path]::GetFullPath($root)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path $resolved -Leaf) -notlike 'S1Interop-compiler-package-*') { throw "Unexpected cleanup path: $resolved" }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
