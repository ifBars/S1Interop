param(
    [Parameter(Mandatory)][string] $GamePath,
    [Parameter(Mandatory)][string] $ModPath,
    [Parameter(Mandatory)][ValidateSet('Mono', 'Il2Cpp')][string] $Runtime,
    [ValidateRange(10, 180)][int] $TimeoutSeconds = 90,
    [switch] $StarterLoad,
    [string] $ProbeLibraryName = 'S1Interop.Compiler.RuntimeLibrary.dll',
    [string] $ProbeDisplayName = 'S1Interop Compiler Runtime Smoke',
    [ValidateRange(0, 10000)][int] $ExpectedChecks = 0
)

$ErrorActionPreference = 'Stop'
if ([IO.Path]::GetFileName($ProbeLibraryName) -ne $ProbeLibraryName -or $ProbeLibraryName -notlike '*.dll') {
    throw 'ProbeLibraryName must be a DLL filename without directory components.'
}
$gameRoot = [IO.Path]::GetFullPath($GamePath)
$modFile = [IO.Path]::GetFullPath($ModPath)
$gameExecutable = Join-Path $gameRoot 'Schedule I.exe'
if (-not (Test-Path -LiteralPath $gameExecutable -PathType Leaf)) { throw "Missing executable: $gameExecutable" }
if (-not (Test-Path -LiteralPath $modFile -PathType Leaf)) { throw "Missing built mod: $modFile" }
if (-not (Test-Path -LiteralPath (Join-Path $gameRoot '.s1interop-compiler-test'))) {
    throw 'The install must be a dedicated compiler test copy, marked with .s1interop-compiler-test.'
}
$existing = @(Get-CimInstance Win32_Process -Filter "Name = 'Schedule I.exe'" |
    Where-Object { $_.ExecutablePath -eq $gameExecutable })
if ($existing.Count -ne 0) { throw 'The test install already has a running game process.' }
foreach ($directory in @('Mods', 'Plugins', 'UserLibs')) {
    $path = Join-Path $gameRoot $directory
    $extra = @(Get-ChildItem -LiteralPath $path -File -Filter '*.dll' -ErrorAction SilentlyContinue)
    if ($extra.Count -ne 0) { throw "Unexpected DLLs in ${path}: $($extra.Name -join ', ')" }
}

$token = [Guid]::NewGuid().ToString('N')
$repoRoot = Split-Path -Parent $PSScriptRoot
$evidence = Join-Path $repoRoot "artifacts\compiler-smoke\$Runtime-$token"
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$mods = Join-Path $gameRoot 'Mods'
New-Item -ItemType Directory -Path $mods -Force | Out-Null
$deployed = Join-Path $mods ([IO.Path]::GetFileName($modFile))
$expectedHash = (Get-FileHash -LiteralPath $modFile -Algorithm SHA256).Hash
$supportFile = Join-Path (Split-Path -Parent $modFile) 'S1Interop.Runtime.dll'
$supportTarget = Join-Path $gameRoot 'UserLibs\S1Interop.Runtime.dll'
$supportHash = $null
$supportInstalled = $false
$libraryFile = Join-Path (Split-Path -Parent $modFile) $ProbeLibraryName
$libraryTarget = Join-Path (Join-Path $gameRoot 'UserLibs') $ProbeLibraryName
if (!$StarterLoad -and -not (Test-Path -LiteralPath $libraryFile -PathType Leaf)) { throw "Missing probe library: $libraryFile" }
$libraryHash = if (!$StarterLoad) { (Get-FileHash -LiteralPath $libraryFile -Algorithm SHA256).Hash } else { $null }
$libraryInstalled = $false
$modInstalled = $false
if ($Runtime -eq 'Il2Cpp') {
    if (-not (Test-Path -LiteralPath $supportFile -PathType Leaf)) { throw "Missing runtime support: $supportFile" }
    $supportHash = (Get-FileHash -LiteralPath $supportFile -Algorithm SHA256).Hash
}
$appId = Join-Path $gameRoot 'steam_appid.txt'
$createdAppId = -not (Test-Path -LiteralPath $appId)
$process = $null
$passed = $false
$failure = $null
$failureStack = $null
$logPath = Join-Path $gameRoot 'MelonLoader\Latest.log'
$playerLog = Join-Path $evidence 'Player.log'
$previousToken = $env:S1INTEROP_SMOKE_TOKEN
try {
    [IO.File]::Copy($modFile, $deployed, $false)
    $modInstalled = $true
    New-Item -ItemType Directory -Path (Split-Path -Parent $libraryTarget) -Force | Out-Null
    if (!$StarterLoad) {
        [IO.File]::Copy($libraryFile, $libraryTarget, $false)
        $libraryInstalled = $true
    }
    if ($Runtime -eq 'Il2Cpp') {
        New-Item -ItemType Directory -Path (Split-Path -Parent $supportTarget) -Force | Out-Null
        [IO.File]::Copy($supportFile, $supportTarget, $false)
        $supportInstalled = $true
    }
    if ($createdAppId) { [IO.File]::WriteAllText($appId, '3164500') }
    $env:S1INTEROP_SMOKE_TOKEN = $token
    # A starter has its ordinary load message, not the probe's token marker. Remove
    # stale-log ambiguity by preserving the previous log before this owned launch.
    if ($StarterLoad -and (Test-Path -LiteralPath $logPath)) {
        Move-Item -LiteralPath $logPath -Destination (Join-Path $evidence 'Previous-MelonLoader.log')
    }
    $started = Get-Date
    $process = Start-Process -FilePath $gameExecutable -WorkingDirectory $gameRoot -PassThru -WindowStyle Hidden `
        -ArgumentList @('-batchmode', '-nographics', '-logFile', ('"' + $playerLog + '"'))
    $env:S1INTEROP_SMOKE_TOKEN = $previousToken
    Write-Output "LAUNCH runtime=$Runtime pid=$($process.Id) token=$token hash=$expectedHash"
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $loaded = $false
    while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        [string]$log = ''
        if ((Test-Path -LiteralPath $logPath) -and (Get-Item -LiteralPath $logPath).LastWriteTime -ge $started.AddSeconds(-1)) {
            $log = '' + (Get-Content -LiteralPath $logPath -Raw -ErrorAction SilentlyContinue)
        }
        if (-not $loaded -and $log.Contains($ProbeDisplayName)) {
            $loaded = $true
            Write-Output "LOADED runtime=$Runtime elapsed=$([Math]::Round($timer.Elapsed.TotalSeconds, 1))s"
        }
        if ($log -match "S1Compiler\|FAIL\|Token=$token\|") { throw 'The runtime probe reported FAIL.' }
        $starterLoaded = $StarterLoad -and $log.Contains(([IO.Path]::GetFileNameWithoutExtension($modFile) + ' loaded.'))
        if ($starterLoaded -or (!$StarterLoad -and $log -match "S1Compiler\|PASS\|Token=$token\|")) {
            $expectedRuntime = if ($Runtime -eq 'Mono') { 'Game Type: Mono' } else { 'Game Type: Il2cpp' }
            if (-not $log.Contains($expectedRuntime)) { throw 'Loader runtime did not match the requested test runtime.' }
            if (!$StarterLoad -and $ExpectedChecks -gt 0 -and
                $log -notmatch "S1Compiler\|PASS\|Token=$token\|[^\r\n]*\|Checks=$ExpectedChecks(?:\||\r|\n|$)") {
                throw "Probe did not report the expected $ExpectedChecks checks."
            }
            $passed = $true
            if ($StarterLoad) { Write-Output "STARTER-LOAD-PASS runtime=$Runtime token=$token; initialization only, no gameplay claim." }
            else { Write-Output (($log -split '\r?\n' | Where-Object { $_ -match "S1Compiler\|PASS\|Token=$token\|" }) -join "`n") }
            break
        }
        if ($process.HasExited) { throw "Game exited before the probe result (exit $($process.ExitCode))." }
        Start-Sleep -Milliseconds 400
    }
    if (-not $passed) { throw "Probe timed out after $TimeoutSeconds seconds; loaded=$loaded." }
} catch {
    $failure = $_.Exception.Message
    $failureStack = $_.ScriptStackTrace
} finally {
    $env:S1INTEROP_SMOKE_TOKEN = $previousToken
    if ($process -and -not $process.HasExited) {
        if (-not $process.WaitForExit(5000)) {
            Stop-Process -Id $process.Id -Force
            if (-not $process.WaitForExit(5000)) { throw 'Owned game process did not exit; deployed files remain for inspection.' }
        }
    }
    if (Test-Path -LiteralPath $logPath) { Copy-Item -LiteralPath $logPath -Destination (Join-Path $evidence 'MelonLoader.log') }
    if ($modInstalled -and (Test-Path -LiteralPath $deployed) -and (Get-FileHash -LiteralPath $deployed -Algorithm SHA256).Hash -eq $expectedHash) {
        Remove-Item -LiteralPath $deployed
    }
    if ($supportInstalled -and (Test-Path -LiteralPath $supportTarget) -and
        (Get-FileHash -LiteralPath $supportTarget -Algorithm SHA256).Hash -eq $supportHash) {
        Remove-Item -LiteralPath $supportTarget
    }
    if ($libraryInstalled -and (Test-Path -LiteralPath $libraryTarget) -and
        (Get-FileHash -LiteralPath $libraryTarget -Algorithm SHA256).Hash -eq $libraryHash) {
        Remove-Item -LiteralPath $libraryTarget
    }
    if ($createdAppId -and (Test-Path -LiteralPath $appId)) { Remove-Item -LiteralPath $appId }
    [ordered]@{ Runtime=$Runtime; Probe=[IO.Path]::GetFileNameWithoutExtension($modFile); EvidenceMode=$(if ($StarterLoad) { 'Starter initialization only' } else { 'Token-tagged runtime contracts' }); Token=$token; Passed=$passed; Failure=$failure; FailureStack=$failureStack; ModSha256=$expectedHash; RuntimeSha256=$supportHash; LibrarySha256=$libraryHash } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'result.json')
    Write-Output "EVIDENCE $evidence"
}
if ($failure) { throw $failure }
