param(
    [Parameter(Mandatory = $true)]
    [string] $ProjectPath,

    [Parameter(Mandatory = $true)]
    [ValidateSet("Mono", "Il2Cpp")]
    [string] $Runtime,

    [string] $GamePath,

    [string] $Configuration = "Debug",

    [string] $GeneratorPackageSource,

    [string] $ModName,

    [string] $ExpectedMarker,

    [int] $TimeoutSeconds = 90,

    [string] $OutputRoot,

    [switch] $AllowExtraMods,

    [switch] $AllowExistingProcess,

    [switch] $KeepDeployed,

    [switch] $NoLaunch
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

function Assert-Directory([string] $PathValue, [string] $Description) {
    if (-not (Test-Path -LiteralPath $PathValue -PathType Container)) {
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

function Get-ProjectFile([string] $InputPath) {
    if ([System.IO.Path]::GetExtension($InputPath).Equals(".csproj", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $InputPath
    }

    if (-not [System.IO.Path]::GetExtension($InputPath).Equals(".sln", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "ProjectPath must point to a .csproj or single-project .sln file: $InputPath"
    }

    $solutionRoot = Split-Path -Parent $InputPath
    $projectEntries = @(Get-Content -LiteralPath $InputPath | Where-Object { $_ -match 'Project\(".*"\)\s+=\s+".*",\s+"([^"]+\.csproj)"' })
    if ($projectEntries.Count -ne 1) {
        throw "Runtime smoke expects a single-project solution. Found $($projectEntries.Count) projects in $InputPath."
    }

    $relativeProject = [regex]::Match($projectEntries[0], 'Project\(".*"\)\s+=\s+".*",\s+"([^"]+\.csproj)"').Groups[1].Value
    return [System.IO.Path]::GetFullPath((Join-Path $solutionRoot $relativeProject))
}

function Get-ProjectProperty([string] $ProjectFile, [string] $PropertyName, [string] $DefaultValue) {
    [xml] $projectXml = Get-Content -LiteralPath $ProjectFile -Raw
    foreach ($propertyGroup in $projectXml.Project.PropertyGroup) {
        $node = $propertyGroup.SelectSingleNode($PropertyName)
        if ($node -ne $null -and -not [string]::IsNullOrWhiteSpace($node.InnerText)) {
            return $node.InnerText.Trim()
        }
    }

    return $DefaultValue
}

function Get-OwnedGameProcesses([string] $ExePath) {
    @(Get-CimInstance Win32_Process -Filter "Name = 'Schedule I.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -eq $ExePath })
}

function Get-LogPaths([string] $RootPath) {
    $paths = @()
    $latest = Join-Path $RootPath "MelonLoader\Latest.log"
    if (Test-Path -LiteralPath $latest -PathType Leaf) {
        $paths += $latest
    }

    $logDirectory = Join-Path $RootPath "MelonLoader\Logs"
    if (Test-Path -LiteralPath $logDirectory -PathType Container) {
        $paths += @(Get-ChildItem -LiteralPath $logDirectory -Filter "*.log" -File | Select-Object -ExpandProperty FullName)
    }

    return @($paths | Select-Object -Unique)
}

function Read-NewLogText([string[]] $LogPaths, [datetime] $StartedAt) {
    $builder = [System.Text.StringBuilder]::new()
    foreach ($path in $LogPaths) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            continue
        }

        $item = Get-Item -LiteralPath $path
        if ($item.LastWriteTime -lt $StartedAt.AddSeconds(-2)) {
            continue
        }

        [void] $builder.AppendLine((Get-Content -LiteralPath $path -Raw -ErrorAction SilentlyContinue))
    }

    return $builder.ToString()
}

function Copy-Logs([string] $RootPath, [string] $Destination) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($path in Get-LogPaths $RootPath) {
        $targetName = Split-Path -Leaf $path
        $parentName = Split-Path -Leaf (Split-Path -Parent $path)
        Copy-Item -LiteralPath $path -Destination (Join-Path $Destination "$parentName-$targetName") -Force
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$inputProject = Resolve-FullPath $ProjectPath
$projectFile = Get-ProjectFile $inputProject
$gameRoot = Resolve-FullPath $(if ([string]::IsNullOrWhiteSpace($GamePath)) {
        if ($Runtime -eq "Mono") { $env:S1_MONO_GAME_PATH } else { $env:S1_IL2CPP_GAME_PATH }
    } else {
        $GamePath
    })
$packageSource = Resolve-FullPath $GeneratorPackageSource

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\runtime-smoke"
}

$runId = "{0}-{1}-{2}" -f (Get-Date -Format "yyyyMMdd-HHmmss"), $Runtime, ([System.Guid]::NewGuid().ToString("N").Substring(0, 8))
$outputDirectory = Join-Path (Resolve-FullPath $OutputRoot) $runId
$logOutputDirectory = Join-Path $outputDirectory "logs"
$backupDirectory = Join-Path $outputDirectory "backup"

Assert-File $inputProject "project or solution"
Assert-File $projectFile "project file"
Assert-Directory $gameRoot "game path"

$exePath = Join-Path $gameRoot "Schedule I.exe"
$modsPath = Join-Path $gameRoot "Mods"
$melonLoaderPath = if ($Runtime -eq "Mono") {
    Join-Path $gameRoot "MelonLoader\net35\MelonLoader.dll"
} else {
    Join-Path $gameRoot "MelonLoader\net6\MelonLoader.dll"
}
$unityPath = if ($Runtime -eq "Mono") {
    Join-Path $gameRoot "Schedule I_Data\Managed\UnityEngine.CoreModule.dll"
} else {
    Join-Path $gameRoot "MelonLoader\Il2CppAssemblies\UnityEngine.CoreModule.dll"
}

Assert-File $exePath "Schedule I executable"
Assert-Directory $modsPath "Mods directory"
Assert-File $melonLoaderPath "$Runtime MelonLoader"
Assert-File $unityPath "$Runtime UnityEngine.CoreModule"

$assemblyName = Get-ProjectProperty $projectFile "AssemblyName" ([System.IO.Path]::GetFileNameWithoutExtension($projectFile))
$targetFramework = Get-ProjectProperty $projectFile "TargetFramework" "netstandard2.1"
if ([string]::IsNullOrWhiteSpace($ModName)) {
    $ModName = $assemblyName
}

if ([string]::IsNullOrWhiteSpace($ExpectedMarker)) {
    $ExpectedMarker = "S1InteropSmoke|PASS|Backend=$Runtime"
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

Write-Host "S1Interop backend-neutral runtime smoke" -ForegroundColor Cyan
Write-Host "Project: $inputProject" -ForegroundColor Gray
Write-Host "Runtime: $Runtime" -ForegroundColor Gray
Write-Host "GamePath: $gameRoot" -ForegroundColor Gray
Write-Host "Output: $outputDirectory" -ForegroundColor Gray

$buildArgs = @(
    "build",
    $inputProject,
    "--nologo",
    "-v:minimal",
    "-c",
    $Configuration
)

if ($Runtime -eq "Mono") {
    $buildArgs += "-p:MonoGamePath=$gameRoot"
} else {
    $buildArgs += @("-p:S1InteropReferenceRuntime=Il2Cpp", "-p:Il2CppGamePath=$gameRoot")
}

if (-not [string]::IsNullOrWhiteSpace($packageSource)) {
    $buildArgs += "-p:RestoreAdditionalProjectSources=$packageSource"
}

Invoke-DotNetBuild $buildArgs

$builtDll = Join-Path (Split-Path -Parent $projectFile) "bin\$Configuration\$targetFramework\$assemblyName.dll"
Assert-File $builtDll "built mod DLL"

$existingMods = @(Get-ChildItem -LiteralPath $modsPath -Filter "*.dll" -File | Select-Object -ExpandProperty Name)
$targetDll = Join-Path $modsPath "$assemblyName.dll"
$extraMods = @($existingMods | Where-Object { $_ -ne "$assemblyName.dll" })
if (-not $AllowExtraMods -and $extraMods.Count -gt 0) {
    throw "Mods contains extra DLLs that can interfere with smoke validation: $($extraMods -join ', '). Pass -AllowExtraMods for a dirty local install."
}

if ($NoLaunch) {
    Write-Host "NoLaunch requested. Build and install audit passed; game was not launched." -ForegroundColor Green
    exit 0
}

$preExistingProcesses = Get-OwnedGameProcesses $exePath
if (-not $AllowExistingProcess -and $preExistingProcesses.Count -gt 0) {
    throw "Schedule I is already running from this game path. Close it first, or pass -AllowExistingProcess if log mixing is acceptable."
}

$hadExistingTarget = Test-Path -LiteralPath $targetDll -PathType Leaf
if ($hadExistingTarget) {
    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
    Copy-Item -LiteralPath $targetDll -Destination (Join-Path $backupDirectory "$assemblyName.dll") -Force
}

$process = $null
$startedAt = Get-Date
$exitCode = 1
$completed = $false

try {
    Copy-Item -LiteralPath $builtDll -Destination $targetDll -Force

    Write-Host "Launching Schedule I..." -ForegroundColor Yellow
    $process = Start-Process -FilePath $exePath -WorkingDirectory $gameRoot -PassThru -WindowStyle Normal
    Write-Host "PID: $($process.Id)" -ForegroundColor Green

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $combinedLogText = ""
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        $combinedLogText = Read-NewLogText (Get-LogPaths $gameRoot) $startedAt

        if ($combinedLogText.Contains($ExpectedMarker)) {
            Copy-Logs $gameRoot $logOutputDirectory
            Write-Host "Runtime smoke passed. Found marker: $ExpectedMarker" -ForegroundColor Green
            Write-Host "Logs: $logOutputDirectory" -ForegroundColor Green
            $exitCode = 0
            $completed = $true
            break
        }

        if ($combinedLogText -match "S1InteropSmoke\|FAIL" -or $combinedLogText -match "Backend=Unknown" -or $combinedLogText -match "=missing") {
            Copy-Logs $gameRoot $logOutputDirectory
            Write-Host "Runtime smoke failed. Failure marker found in MelonLoader logs." -ForegroundColor Red
            Write-Host "Logs: $logOutputDirectory" -ForegroundColor Yellow
            $exitCode = 1
            $completed = $true
            break
        }

        if ($process.HasExited) {
            break
        }
    }

    if (-not $completed) {
        Copy-Logs $gameRoot $logOutputDirectory
        Write-Host "Runtime smoke timed out before marker was observed: $ExpectedMarker" -ForegroundColor Red
        Write-Host "Logs: $logOutputDirectory" -ForegroundColor Yellow
        $exitCode = 1
    }
}
finally {
    if ($process -ne $null -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }

    if (-not $KeepDeployed) {
        if ($hadExistingTarget) {
            Copy-Item -LiteralPath (Join-Path $backupDirectory "$assemblyName.dll") -Destination $targetDll -Force
        } elseif (Test-Path -LiteralPath $targetDll -PathType Leaf) {
            Remove-Item -LiteralPath $targetDll -Force
        }
    }
}

exit $exitCode
