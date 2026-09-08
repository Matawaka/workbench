# Diagnostic v2.1, bounded OID inventory only; not a publication preflight or permit.
# Keep v1/v2 observations unchanged. Same existing MinGit; no installer, PATH/config writes or hydration.
# Production accepts no arguments or target overrides. Fixture rebinding is external to this file.
& {
    $ErrorActionPreference = 'Stop'
    $root = 'K:\Matawaka\Workbench'
    $head = '58b9430fc544998a8e40ba00b6757cc630ba9081'
    $tag = 'refs/tags/workbench-v0.60.1-accepted'
    $parents = "$head ea852feeb0e8d92a8977bb251693e7e977913dca ac083598711caa0c399cc0d2c385b980c083024a"
    try {
        $git = 'K:\Matawaka\Tools\Git\MinGit-2.55.0.4-64-bit\cmd\git.exe'
        if (-not (Test-Path -LiteralPath $git -PathType Leaf)) { throw 'EXACT_SIDE_BY_SIDE_GIT_MISSING' }
        $gitHash = (Get-FileHash -LiteralPath $git -Algorithm SHA256).Hash
        foreach ($relative in @('', '.git', '.git/objects', '.git/config', '.git/index')) {
            $item = Get-Item -LiteralPath (Join-Path $root $relative) -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'REPARSE_PATH_REFUSED' }
        }
        if (-not (Get-Item -LiteralPath "$root\.git" -Force).PSIsContainer) { throw 'DIRECT_GIT_DIRECTORY_REQUIRED' }
        foreach ($relative in @('commondir','shallow','info/grafts','objects/info/alternates','objects/info/http-alternates')) {
            if (Test-Path -LiteralPath "$root\.git\$relative") { throw 'UNQUALIFIED_GIT_LAYOUT' }
        }
        $prefix = '--no-pager --no-lazy-fetch --no-replace-objects --no-optional-locks -C "' + $root + '" -c core.commitGraph=false -c core.fsmonitor=false -c core.untrackedCache=false -c protocol.allow=never -c credential.helper= -c credential.interactive=false '
        function Read-Git([string]$operation) {
            if ((Get-FileHash -LiteralPath $git -Algorithm SHA256).Hash -ne $gitHash) { throw 'GIT_IMAGE_CHANGED' }
            $info = New-Object System.Diagnostics.ProcessStartInfo
            $info.FileName = $git; $info.Arguments = $prefix + $operation
            $info.UseShellExecute = $false; $info.CreateNoWindow = $true
            $info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
            $info.EnvironmentVariables.Clear()
            foreach ($key in @('SystemRoot','WINDIR','PATH','TEMP','TMP','COMSPEC')) {
                $value = [Environment]::GetEnvironmentVariable($key)
                if ($null -ne $value) { $info.EnvironmentVariables[$key] = $value }
            }
            foreach ($pair in @(@('GIT_CONFIG_NOSYSTEM','1'),@('GIT_CONFIG_GLOBAL','NUL'),@('GIT_NO_LAZY_FETCH','1'),@('GIT_NO_REPLACE_OBJECTS','1'),@('GIT_OPTIONAL_LOCKS','0'),@('GIT_ALLOW_PROTOCOL',''),@('GIT_TERMINAL_PROMPT','0'),@('LC_ALL','C'))) {
                $info.EnvironmentVariables[$pair[0]] = $pair[1]
            }
            $p = New-Object System.Diagnostics.Process; $p.StartInfo = $info
            try {
                if (-not $p.Start()) { throw 'GIT_START_FAILED' }
                $p.StandardInput.Close()
                $stdout = $p.StandardOutput.ReadToEndAsync(); $stderr = $p.StandardError.ReadToEndAsync()
                if (-not $p.WaitForExit(60000)) { try { $p.Kill() } catch {}; throw 'GIT_READ_TIMEOUT' }
                if (-not $stdout.Wait(5000) -or -not $stderr.Wait(5000)) { throw 'GIT_OUTPUT_TIMEOUT' }
                if ($p.ExitCode -ne 0) { throw ('GIT_' + (($operation -split ' ')[0].TrimStart('-').ToUpperInvariant() -replace '[^A-Z0-9_]', '_') + '_EXIT_' + $p.ExitCode) }
                if ($stdout.Result.Length -gt 16777216 -or $stderr.Result.Length -gt 131072) { throw 'OUTPUT_TOO_LARGE' }
                return $stdout.Result.TrimEnd([char[]]"`r`n")
            } finally { $p.Dispose() }
        }
        function Count-Objects([string]$text) {
            $present = 0
            $missing = New-Object 'System.Collections.Generic.List[string]'
            $seen = New-Object 'System.Collections.Generic.HashSet[string]'
            foreach ($line in ($text -split '\r?\n')) {
                if ($line -cmatch '^\?[0-9a-f]{40}$') {
                    $oid = $line.Substring(1)
                    if (-not $seen.Add($oid)) { throw 'DUPLICATE_OBJECT_OUTPUT' }
                    $missing.Add($oid)
                    if ($missing.Count -gt 256) { throw 'MISSING_OID_DISCLOSURE_LIMIT' }
                }
                elseif ($line -cmatch '^[0-9a-f]{40}$') {
                    if (-not $seen.Add($line)) { throw 'DUPLICATE_OBJECT_OUTPUT' }
                    $present++
                }
                else { throw 'UNEXPECTED_OBJECT_OUTPUT' }
            }
            return [pscustomobject]@{ Present = $present; Missing = $missing.Count; MissingOids = @($missing | Sort-Object) }
        }
        # Capability probe precedes repository object reads; unsupported option never falls back.
        $version = Read-Git '--version'
        if ($version -cne 'git version 2.55.0.windows.4') { throw 'EXACT_GIT_VERSION_MISMATCH' }
        if ($gitHash -ine 'c470d205517c7a53ceca321df16a6e4549fcd52b576ab4d09536d36f26fda5a9') { throw 'EXACT_GIT_IMAGE_MISMATCH' }
        $configHash = (Get-FileHash -LiteralPath "$root\.git\config" -Algorithm SHA256).Hash
        $indexHash = (Get-FileHash -LiteralPath "$root\.git\index" -Algorithm SHA256).Hash
        $config = Read-Git 'config --local --no-includes --null --list'
        foreach ($entry in $config.Split([char]0)) {
            $key = $entry.Split([char]10)[0]
            if ($key -match '^(include|includeif|filter|extensions)\.') { throw 'OTHER_UNQUALIFIED_CONFIG' }
        }
        $refs = Read-Git 'for-each-ref --format="%(refname) %(objectname)"'
        if ($refs -match '(?m)^refs/replace/') { throw 'REPLACE_REFS_REFUSED' }
        if ((Read-Git 'rev-parse --verify HEAD') -cne $head) { throw 'LOCAL_HEAD_MISMATCH' }
        if ((Read-Git "cat-file -t $tag") -cne 'tag') { throw 'ANNOTATED_TAG_REQUIRED' }
        if ((Read-Git "rev-parse --verify $tag^{commit}") -cne $head) { throw 'TAG_PEEL_MISMATCH' }
        $tagObject = Read-Git "rev-parse --verify $tag"
        if ($tagObject -cnotmatch '^[0-9a-f]{40}$') { throw 'TAG_OBJECT_ID_MISMATCH' }
        $rawCommit = Read-Git "cat-file commit $head"
        $header = ($rawCommit -split "`n`n", 2)[0] -split "`n"
        $observedParents = @($header | Where-Object { $_ -cmatch '^parent [0-9a-f]{40}$' } | ForEach-Object { $_.Substring(7) })
        if ((@($head) + $observedParents -join ' ') -cne $parents) { throw 'ORDERED_PARENTS_MISMATCH' }
        $treeHeaders = @($header | Where-Object { $_ -cmatch '^tree [0-9a-f]{40}$' })
        if ($treeHeaders.Count -ne 1) { throw 'COMMIT_TREE_HEADER_MISMATCH' }
        $treeId = $treeHeaders[0].Substring(5)
        $history = Count-Objects (Read-Git "rev-list --objects --no-object-names --missing=print $head $tag --")
        $rootTreePresent = $true
        try { $null = Read-Git "cat-file -e $treeId" }
        catch {
            if ($_.Exception.Message -cne 'GIT_CAT_FILE_EXIT_1') { throw }
            $rootTreePresent = $false
        }
        if ($rootTreePresent) {
            $tree = Count-Objects (Read-Git "rev-list --objects --no-object-names --missing=print $treeId --")
        } else {
            $tree = [pscustomobject]@{ Present = 0; Missing = 1; MissingOids = @($treeId) }
        }
        if ((Read-Git 'for-each-ref --format="%(refname) %(objectname)"') -cne $refs -or
            (Read-Git 'rev-parse --verify HEAD') -cne $head -or
            (Get-FileHash -LiteralPath "$root\.git\config" -Algorithm SHA256).Hash -cne $configHash -or
            (Get-FileHash -LiteralPath "$root\.git\index" -Algorithm SHA256).Hash -cne $indexHash) { throw 'LOCAL_STATE_CHANGED' }
        [pscustomobject]@{
            Diagnostic = 'OFFLINE_OBJECT_INVENTORY_V21_EXACT_SIDE_BY_SIDE_GIT'
            AcceptedTag = $tag
            AcceptedTagObject = $tagObject
            AcceptedTagPeeledCommit = $head
            GitVersion = $version
            GitExecutableSha256 = $gitHash.ToLowerInvariant()
            CurrentTreeObject = $treeId
            RootTreePresent = $rootTreePresent
            AcceptedHead = $head
            CurrentTreePresentObjects = $tree.Present
            CurrentTreeMissingBoundaryObjects = $tree.Missing
            CurrentTreeMissingBoundaryOids = @($tree.MissingOids)
            FullHistoryPresentObjects = $history.Present
            FullHistoryMissingBoundaryObjects = $history.Missing
            FullHistoryMissingBoundaryOids = @($history.MissingOids)
            ObjectTypesInferred = $false
            ObjectPathsDisclosed = $false
            WholeWorkingTreeVerified = $false
            WholeRuntimeBinaryTreeVerified = $false
            CanonicalPreflightReceiptCreated = $false
            HeadRefsConfigIndexStable = $true
            ReachableClosureComplete = ($tree.Missing -eq 0 -and $history.Missing -eq 0)
            MissingCountsAreBoundaryOnly = $true
            OsNetworkIsolationProven = $false
            PublicationAuthorized = $false
        } | ConvertTo-Json -Depth 6
    } catch {
        $message = $_.Exception.Message
        if ($message -cnotmatch '^[A-Z][A-Z0-9_]+$') { $message = 'LOCAL_DIAGNOSTIC_ERROR' }
        'DIAGNOSTIC_REFUSED: ' + $message
        'STOP. Do not change config, fetch, retry preflight or push.'
    }
}
