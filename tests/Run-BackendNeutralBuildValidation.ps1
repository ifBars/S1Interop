param(
    [Parameter(Mandatory = $true)]
    [string] $ProjectPath,

    [string] $MonoGamePath = $env:S1_MONO_GAME_PATH,

    [string] $Il2CppGamePath = $env:S1_IL2CPP_GAME_PATH,

    [string] $Configuration = "Debug",

    [string] $GeneratorPackageSource,

    [switch] $ClearNuGetCache
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath([string] $PathValue) {
    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $null
    }

    return [System.IO.Path]::GetFullPath($PathValue)
}

function Assert-File([string] $PathValue, [string] $Description) {
    if (-not (Test-Path -LiteralPath $PathValue -PathType Leaf)) {
        throw "Missing $Description at $PathValue"
    }
}

function Invoke-DotNetBuild([string[]] $BuildArgs) {
    Write-Host "dotnet $($BuildArgs -join ' ')"
    & dotnet @BuildArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE"
    }
}

$project = Resolve-FullPath $ProjectPath
$monoRoot = Resolve-FullPath $MonoGamePath
$il2CppRoot = Resolve-FullPath $Il2CppGamePath
$packageSource = Resolve-FullPath $GeneratorPackageSource

Assert-File $project "project or solution"

if ([string]::IsNullOrWhiteSpace($monoRoot)) {
    throw "MonoGamePath was not provided. Pass -MonoGamePath or set S1_MONO_GAME_PATH."
}

if ([string]::IsNullOrWhiteSpace($il2CppRoot)) {
    throw "Il2CppGamePath was not provided. Pass -Il2CppGamePath or set S1_IL2CPP_GAME_PATH."
}

Assert-File (Join-Path $monoRoot "MelonLoader\net35\MelonLoader.dll") "Mono MelonLoader"
Assert-File (Join-Path $monoRoot "Schedule I_Data\Managed\UnityEngine.CoreModule.dll") "Mono UnityEngine.CoreModule"
Assert-File (Join-Path $il2CppRoot "MelonLoader\net6\MelonLoader.dll") "IL2CPP MelonLoader"
Assert-File (Join-Path $il2CppRoot "MelonLoader\Il2CppAssemblies\UnityEngine.CoreModule.dll") "IL2CPP UnityEngine.CoreModule"

if ($ClearNuGetCache) {
    & dotnet nuget locals global-packages --clear
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet nuget locals failed with exit code $LASTEXITCODE"
    }
}

$commonArgs = @(
    "build",
    $project,
    "--nologo",
    "-v:minimal",
    "-c",
    $Configuration
)

if (-not [string]::IsNullOrWhiteSpace($packageSource)) {
    $commonArgs += "-p:RestoreAdditionalProjectSources=$packageSource"
}

Invoke-DotNetBuild ($commonArgs + @("-p:MonoGamePath=$monoRoot"))
Invoke-DotNetBuild ($commonArgs + @("-p:S1InteropReferenceRuntime=Il2Cpp", "-p:Il2CppGamePath=$il2CppRoot"))

Write-Host "Backend-neutral build validation passed for Mono and IL2CPP reference surfaces."
