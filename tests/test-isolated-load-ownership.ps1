$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'IsolatedLoad.Helpers.ps1')

$workspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$save = Join-Path $workspace 'isolated tests\profile'
$dotnet = Join-Path $workspace 'runtime\dotnet.exe'
$dll = Join-Path $workspace 'runtime\tModLoader.dll'
$launch = [datetime]'2026-01-01T00:00:00Z'
$cases = 0

function Assert-OwnershipCase {
    param([string]$Name, [bool]$Actual, [bool]$Expected)
    if ($Actual -ne $Expected) { throw "FAIL: $Name (actual=$Actual, expected=$Expected)" }
    $script:cases++
}

function New-TestHost {
    param([string]$CommandLine, [datetime]$Created = $launch.AddSeconds(1), [int]$HostId = 123)
    return [pscustomobject]@{
        ProcessId = $HostId
        ExecutablePath = $dotnet
        CommandLine = $CommandLine
        CreationDate = $Created
    }
}

$command = '"{0}" "{1}" -server -nosteam -tmlsavedirectory "{2}"' -f $dotnet, $dll, $save
$own = New-TestHost $command
Assert-OwnershipCase 'child with spaces' (Test-IsolatedLoadChildPath $save $workspace) $true
Assert-OwnershipCase 'workspace itself' (Test-IsolatedLoadChildPath $workspace $workspace) $false
Assert-OwnershipCase 'sibling sharing prefix' (Test-IsolatedLoadChildPath ($workspace + '-other\profile') $workspace) $false
Assert-OwnershipCase 'parent traversal' (Test-IsolatedLoadChildPath (Join-Path $workspace '..\outside') $workspace) $false
Assert-OwnershipCase 'quoted exact profile' (Test-IsolatedLoadOwnedHost $own $dotnet $dll $save $launch @()) $true
Assert-OwnershipCase 'restarted host with new PID' (Test-IsolatedLoadOwnedHost (New-TestHost $command -HostId 456) $dotnet $dll $save $launch @()) $true
Assert-OwnershipCase 'pre-existing matching profile' (Test-IsolatedLoadOwnedHost $own $dotnet $dll $save $launch @((Get-IsolatedLoadProcessIdentity $own))) $false
Assert-OwnershipCase 'old host absent from initial list' (Test-IsolatedLoadOwnedHost (New-TestHost $command -Created $launch.AddSeconds(-1)) $dotnet $dll $save $launch @()) $false
$normalGame = New-TestHost ('"{0}" "{1}"' -f $dotnet, $dll)
Assert-OwnershipCase 'ordinary game launched during test' (Test-IsolatedLoadOwnedHost $normalGame $dotnet $dll $save $launch @()) $false
$otherProfile = New-TestHost ($command.Replace($save, $save + '-other'))
Assert-OwnershipCase 'concurrent isolated profile' (Test-IsolatedLoadOwnedHost $otherProfile $dotnet $dll $save $launch @()) $false
$wrongDll = New-TestHost ($command.Replace($dll, (Join-Path $workspace 'other.dll')))
Assert-OwnershipCase 'other dotnet application' (Test-IsolatedLoadOwnedHost $wrongDll $dotnet $dll $save $launch @()) $false
$duplicateSave = New-TestHost ($command + ' -tmlsavedirectory "' + $save + '"')
Assert-OwnershipCase 'ambiguous duplicate save argument' (Test-IsolatedLoadOwnedHost $duplicateSave $dotnet $dll $save $launch @()) $false
$unterminatedQuote = New-TestHost ($command.TrimEnd('"'))
Assert-OwnershipCase 'malformed quoting' (Test-IsolatedLoadOwnedHost $unterminatedQuote $dotnet $dll $save $launch @()) $false
$missingCreation = New-TestHost $command
$missingCreation.CreationDate = $null
Assert-OwnershipCase 'unknown creation time' (Test-IsolatedLoadOwnedHost $missingCreation $dotnet $dll $save $launch @()) $false
$reused = New-TestHost $command -Created $launch.AddSeconds(2)
Assert-OwnershipCase 'PID reuse changes identity' ((Get-IsolatedLoadProcessIdentity $own) -eq (Get-IsolatedLoadProcessIdentity $reused)) $false
$relativeSave = New-TestHost ($command.Replace($save, '.\isolated tests\profile'))
Assert-OwnershipCase 'relative profile cannot prove identity' (Test-IsolatedLoadOwnedHost $relativeSave $dotnet $dll $save $launch @()) $false
$wrongExecutable = New-TestHost $command
$wrongExecutable.ExecutablePath = Join-Path $workspace 'unrelated\dotnet.exe'
Assert-OwnershipCase 'different dotnet executable' (Test-IsolatedLoadOwnedHost $wrongExecutable $dotnet $dll $save $launch @()) $false
$missingCommand = New-TestHost ''
Assert-OwnershipCase 'unavailable command line' (Test-IsolatedLoadOwnedHost $missingCommand $dotnet $dll $save $launch @()) $false
$canonicalSave = New-TestHost ($command.Replace($save, ($save.ToUpperInvariant() + '\')))
Assert-OwnershipCase 'absolute path case and trailing separator' (Test-IsolatedLoadOwnedHost $canonicalSave $dotnet $dll $save $launch @()) $true

Write-Output "Passed $cases isolated-load ownership checks; no processes were started or stopped."
