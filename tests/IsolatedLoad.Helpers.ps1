# Pure ownership predicates shared by the runner and its no-game regression tests.
function Test-IsolatedLoadChildPath {
    param([string]$Path, [string]$WorkspaceRoot)

    $root = [System.IO.Path]::GetFullPath($WorkspaceRoot).TrimEnd('\', '/')
    $candidate = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    return $candidate.StartsWith($root + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-IsolatedLoadSamePath {
    param([string]$Left, [string]$Right)

    if ([string]::IsNullOrWhiteSpace($Left) -or [string]::IsNullOrWhiteSpace($Right)) {
        return $false
    }
    try {
        # A different process can have a different working directory. Relative
        # command-line paths cannot prove it is using this isolated profile.
        if (-not [System.IO.Path]::IsPathFullyQualified($Left) -or
            -not [System.IO.Path]::IsPathFullyQualified($Right)) { return $false }
        return [System.IO.Path]::GetFullPath($Left).TrimEnd('\', '/').Equals(
            [System.IO.Path]::GetFullPath($Right).TrimEnd('\', '/'),
            [System.StringComparison]::OrdinalIgnoreCase)
    }
    catch {
        return $false
    }
}

function Get-IsolatedLoadCommandArguments {
    param([string]$CommandLine)

    # Accept only whole unquoted or whole quoted arguments, which is how this
    # runner launches tML. Unrecognized quoting fails closed instead of guessing.
    $arguments = [System.Collections.Generic.List[string]]::new()
    $offset = 0
    while ($offset -lt $CommandLine.Length) {
        if ([string]::IsNullOrWhiteSpace($CommandLine.Substring($offset))) { break }
        $match = [regex]::Match($CommandLine.Substring($offset), '^\s*(?:"([^"\r\n]*)"|([^\s"]+))(?=\s|$)')
        if (-not $match.Success) { return @() }
        $arguments.Add($(if ($match.Groups[1].Success) { $match.Groups[1].Value } else { $match.Groups[2].Value }))
        $offset += $match.Length
    }
    return $arguments.ToArray()
}

function Test-IsolatedLoadHost {
    param(
        [object]$ProcessInfo,
        [string]$DotnetPath,
        [string]$TModLoaderDll,
        [string]$SavePath
    )

    if (-not (Test-IsolatedLoadSamePath $ProcessInfo.ExecutablePath $DotnetPath)) { return $false }
    $arguments = @(Get-IsolatedLoadCommandArguments $ProcessInfo.CommandLine)
    if ($arguments.Count -lt 5 -or
        -not (Test-IsolatedLoadSamePath $arguments[0] $DotnetPath) -or
        -not (Test-IsolatedLoadSamePath $arguments[1] $TModLoaderDll) -or
        '-server' -notin $arguments) { return $false }

    $saveIndexes = @(for ($index = 2; $index -lt $arguments.Count; $index++) {
        if ($arguments[$index] -ieq '-tmlsavedirectory') { $index }
    })
    if ($saveIndexes.Count -ne 1 -or $saveIndexes[0] + 1 -ge $arguments.Count) { return $false }
    return Test-IsolatedLoadSamePath $arguments[$saveIndexes[0] + 1] $SavePath
}

function Get-IsolatedLoadProcessIdentity {
    param([object]$ProcessInfo)

    if ($null -eq $ProcessInfo.CreationDate) { return $null }
    return '{0}:{1}' -f $ProcessInfo.ProcessId, $ProcessInfo.CreationDate.ToUniversalTime().Ticks
}

function Test-IsolatedLoadOwnedHost {
    param(
        [object]$ProcessInfo,
        [string]$DotnetPath,
        [string]$TModLoaderDll,
        [string]$SavePath,
        [datetime]$LaunchStartedAt,
        [string[]]$PreExistingIdentities
    )

    $identity = Get-IsolatedLoadProcessIdentity $ProcessInfo
    return $null -ne $identity -and
        $identity -notin $PreExistingIdentities -and
        $ProcessInfo.CreationDate.ToUniversalTime() -ge $LaunchStartedAt.ToUniversalTime() -and
        (Test-IsolatedLoadHost $ProcessInfo $DotnetPath $TModLoaderDll $SavePath)
}
