param(
    [Parameter(Mandatory = $true)]
    [string]$SavePath,
    [string[]]$ExpectedFinalizedMods = @('Daybreak DamageTracker v0.2.1'),
    [int]$TimeoutSeconds = 55
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'IsolatedLoad.Helpers.ps1')

$tModLoaderDirectory = 'D:\steam\steamapps\common\tModLoader'
$dotnetPath = Join-Path $tModLoaderDirectory 'dotnet\dotnet.exe'
$tModLoaderDll = Join-Path $tModLoaderDirectory 'tModLoader.dll'
$resolvedSavePath = [System.IO.Path]::GetFullPath($SavePath).TrimEnd('\', '/')
$workspaceRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))

if (-not (Test-IsolatedLoadChildPath $resolvedSavePath $workspaceRoot)) {
    throw "Isolated SavePath must be a strict child of the workspace: $resolvedSavePath"
}
# Lexical containment alone does not protect against a junction into an active
# save directory. Reject reparse-point ancestors below the workspace as well.
$directoryToCheck = $resolvedSavePath
while (Test-IsolatedLoadChildPath $directoryToCheck $workspaceRoot) {
    $directory = Get-Item -LiteralPath $directoryToCheck -Force
    if (($directory.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Isolated SavePath cannot traverse a reparse point: $directoryToCheck"
    }
    $directoryToCheck = [System.IO.Path]::GetDirectoryName($directoryToCheck)
}
if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    throw "Bundled tModLoader dotnet was not found: $dotnetPath"
}
if (-not (Test-Path -LiteralPath $tModLoaderDll -PathType Leaf)) {
    throw "tModLoader.dll was not found: $tModLoaderDll"
}

$packagePath = Join-Path $resolvedSavePath 'Mods\DaybreakDamageTracker.tmod'
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
    throw "Candidate package was not found: $packagePath"
}

$stdoutPath = Join-Path $resolvedSavePath 'isolated-load.stdout.log'
$stderrPath = Join-Path $resolvedSavePath 'isolated-load.stderr.log'
$arguments = @(
    ('"' + $tModLoaderDll + '"'),
    '-server',
    '-nosteam',
    '-tmlsavedirectory',
    ('"' + $resolvedSavePath + '"')
)
$preExistingHosts = @(Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'")
if (@($preExistingHosts | Where-Object {
    Test-IsolatedLoadHost $_ $dotnetPath $tModLoaderDll $resolvedSavePath
}).Count -gt 0) {
    throw 'The isolated SavePath already belongs to a running tModLoader host.'
}
$preExistingIdentities = @(
    $preExistingHosts | ForEach-Object { Get-IsolatedLoadProcessIdentity $_ }
)

$process = $null
$loadCompleted = $false
$launchStartedAt = [DateTime]::UtcNow
try {
    $process = Start-Process `
        -FilePath $dotnetPath `
        -ArgumentList $arguments `
        -WorkingDirectory $tModLoaderDirectory `
        -WindowStyle Hidden `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -PassThru

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $stdoutPath -PathType Leaf) {
            $logText = Get-Content -LiteralPath $stdoutPath -Raw
            $allExpectedModsFinalized = $true
            foreach ($expectedMod in $ExpectedFinalizedMods) {
                if ($logText -notmatch [regex]::Escape("Finalizing Content: $expectedMod")) {
                    $allExpectedModsFinalized = $false
                    break
                }
            }

            if ($allExpectedModsFinalized -and $logText -match 'Choose World:') {
                $loadCompleted = $true
                break
            }
        }

        # The launcher can exit before a restarted tML host finishes loading.
        # Observe the isolated log until the deadline, not just the first PID.
        Start-Sleep -Milliseconds 250
    }
}
finally {
    # Relaunched hosts are owned only when executable, tML DLL, dedicated-server
    # mode, exact SavePath and creation time all agree. A later ordinary Steam
    # client or a different isolated profile is never selected merely for being new.
    $spawnedTmlHosts = @(
        Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" |
            Where-Object {
                Test-IsolatedLoadOwnedHost $_ $dotnetPath $tModLoaderDll $resolvedSavePath `
                    $launchStartedAt $preExistingIdentities
            }
    )
    foreach ($spawnedTmlHost in $spawnedTmlHosts) {
        # Bind a Process handle before rechecking creation time, so PID reuse between
        # discovery and termination cannot redirect Kill to an unrelated process.
        $candidate = Get-Process -Id $spawnedTmlHost.ProcessId -ErrorAction SilentlyContinue
        if ($null -eq $candidate) { continue }
        try {
            $null = $candidate.Handle
            $fresh = Get-CimInstance Win32_Process -Filter "ProcessId = $($candidate.Id)"
            if ($null -ne $fresh -and
                (Get-IsolatedLoadProcessIdentity $fresh) -eq (Get-IsolatedLoadProcessIdentity $spawnedTmlHost) -and
                (Test-IsolatedLoadOwnedHost $fresh $dotnetPath $tModLoaderDll $resolvedSavePath $launchStartedAt $preExistingIdentities)) {
                $candidate.Kill()
                $candidate.WaitForExit(5000) | Out-Null
            }
        }
        catch [System.InvalidOperationException] {
            # The proven-owned process exited naturally before termination.
        }
        finally {
            $candidate.Dispose()
        }
    }
}

$finalLog = Get-Content -LiteralPath $stdoutPath -Raw
$finalErrorLog = Get-Content -LiteralPath $stderrPath -Raw
$allLoadErrors = @(
    ($finalLog + "`n" + $finalErrorLog) -split "`r?`n" |
        Where-Object {
            $_ -match '^\s*Missing mod:' -or
            $_ -match '^\s*An error occurred while loading' -or
            $_ -match 'dependencies have been automatically disabled' -or
            $_ -match '\[(ERROR|FATAL)\]' -or
            $_ -match '^\s*(Unhandled exception|System\.[A-Za-z0-9_.]+Exception:|Error:)'
        }
)

if (-not $loadCompleted) {
    throw 'Timed out or exited before every expected mod finalized and the isolated server reached world selection.'
}
if ($allLoadErrors.Count -gt 0) {
    throw "The isolated load emitted $($allLoadErrors.Count) dependency or load error line(s)."
}

[pscustomobject]@{
    Package = $packagePath
    Sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
    ModLoadCompleted = $loadCompleted
    LoadErrorLines = $allLoadErrors.Count
    ExpectedFinalizedMods = $ExpectedFinalizedMods -join '; '
    EvidenceLog = $stdoutPath
}
