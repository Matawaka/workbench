# Fixed local object-store recovery. NOT Update, Accept, publication or network authority.
# Run only the qualified delivery; no arguments, config overrides or automatic retries.
if ($args.Count -ne 0) { 'IMPORT_REFUSED: ARGUMENTS_NOT_ACCEPTED'; return }
& {
    $ErrorActionPreference = 'Stop'
    $root = 'K:\Matawaka\Workbench'
    $bundle = 'K:\Matawaka\Tools\WorkbenchV0601SixBlobImportV1'
    $git = 'K:\Matawaka\Tools\Git\MinGit-2.55.0.4-64-bit\cmd\git.exe'
    $head = '58b9430fc544998a8e40ba00b6757cc630ba9081'
    $first = 'ea852feeb0e8d92a8977bb251693e7e977913dca'
    $second = 'ac083598711caa0c399cc0d2c385b980c083024a'
    $treeId = 'a6baa9d2d90fb9f00e54ac4c2373b7d9b8aab4e1'
    $tag = 'refs/tags/workbench-v0.60.1-accepted'
    $tagObject = 'c07409c7973f1a4181e94168ba172bba0d56355f'
    $expectedTreeCount = 583
    $expectedHistoryCount = 1378
    $gitSha = 'c470d205517c7a53ceca321df16a6e4549fcd52b576ab4d09536d36f26fda5a9'
    $confirmation = 'IMPORT-EXACT-V0601-SIX-HISTORICAL-BLOBS'
    $objects = @(
        @('162aa48105259c20b067ba43e48db35db4f9d34a',880,'3a3339c278be0e63354923ca0d6660482e216758465d6c947126397fe0eed59d'),
        @('3b542376bae6ed3ab5a5fdd232cdfd96b453c586',7023,'b08b11e6b6cf4a727a1f8ce668f6b8b944190c888b9dcc058fbb78f6618c8663'),
        @('5fdd8744827ddd202e317174c937761fb20a81e2',934,'349559a3ad35d50213e011fd42487b536c0aff97350e195260f26eedddfa8b8b'),
        @('81aab74193548f4061853abb4a6cfcaea25975c2',919,'53c5bc8890af7ecbdc458583c84981c03cf5c8e0a0e4c2548d3dfdeaf4fb06f4'),
        @('c363c499acc2fd5a93624e3ad51d9320ea48e7af',6389,'75890922bb50bd4ba792e8115c0b4c5eba6676f30c183d2247108bc5b4b26624'),
        @('f68e906e127777b1cf918b75829b8f692cecc682',1550,'d61204d4dd9226aa7d27116a18518ffa0854867cfe98566d5093253f3da4fa72')
    )
    $canonical = @(
        @('artifacts/convergence-v0601/public-main-ac083598711caa0c399cc0d2c385b980c083024a.json','08422305e99b9440641ab0d85f2685f357323c2745da7f19df596abdbcd3438b'),
        @('artifacts/update-applies/apply-build-v0.14-20260908-152159275.json','69becfc3a8582feebe8d158dfc60189c31e0556880bbc676f7e41251743f0bd2'),
        @('artifacts/checkpoints/v0.60.1-source-manifest-20260908-152159119.json','22de05b99a0de2684fa719c541bbfeea4923defe6ee98deee412ba52fd5baebf'),
        @('artifacts/acceptance/v0.60.1-20260908-152211331.json','4e619bf59d0033fbf75f5a32eef31bc486c7eee25227051447229c77af2d6d82'),
        @('artifacts/acceptance/checkpoint-v0.60.1-20260908-152212795.json','a817d84f3ffd6d8d9bb3839bca4c8e266512f92f538050b1d70686e242021f0c'),
        @('artifacts/transition-bootstrap/transition-bootstrap-v0.40-27c29a0d53ef448e9a42a65709a7a9eb.json','7ccd660f3d2077f4dc5c708204af4b9d17f109ac9b14f361d92e7592c8b41a7d')
    )
    $outDir = Join-Path $root 'artifacts/object-recovery-v0601'
    $attempt = Join-Path $outDir ('attempt-six-blobs-' + $head + '.json')
    $receiptPath = Join-Path $outDir ('six-blobs-' + $head + '.json')
    $writeAttempted = $false
    $attemptStarted = $false
    $completed = New-Object 'System.Collections.Generic.List[string]'
    function Need([bool]$v,[string]$code) { if (-not $v) { throw $code } }
    function NoReparse([string]$path) {
        $p = [IO.Path]::GetFullPath($path)
        while ($p) {
            if ([IO.File]::Exists($p) -or [IO.Directory]::Exists($p)) { Need (([IO.File]::GetAttributes($p) -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'REPARSE_PATH_REFUSED' }
            $p = [IO.Path]::GetDirectoryName($p)
        }
    }
    function Sha([byte[]]$b) {
        $h = [Security.Cryptography.SHA256]::Create()
        try { return ([BitConverter]::ToString($h.ComputeHash($b))).Replace('-','').ToLowerInvariant() } finally { $h.Dispose() }
    }
    function ReadBytes([string]$path,[long]$maximum=134217728) {
        NoReparse $path
        $f = [IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        try {
            Need ($f.Length -le $maximum) 'FILE_BYTE_LIMIT'
            $m = New-Object IO.MemoryStream; $f.CopyTo($m)
            try { return ,$m.ToArray() } finally { $m.Dispose() }
        } finally { $f.Dispose() }
    }
    function FileSha([string]$path) { return Sha (ReadBytes $path) }
    function Digest($map) { return Sha ([Text.Encoding]::UTF8.GetBytes((@($map.Keys | Sort-Object | ForEach-Object { $_ + "`0" + $map[$_] }) -join "`n"))) }
    function GitBytes([string]$op,[byte[]]$data=$null) {
        Need ((FileSha $git) -ceq $gitSha) 'GIT_IMAGE_MISMATCH'
        $family = ($op -split ' ')[0]
        Need ($family -in @('--version','config','rev-parse','cat-file','for-each-ref','rev-list','ls-files','hash-object')) 'COMMAND_NOT_ADMITTED'
        $payloadHandle=$null; $p=$null; $m=$null
        try {
            if ($family -eq 'hash-object') {
                # These are internal operation tokens, not a caller-supplied command or stdin transport.
                Need ($null -ne $data -and ($op -ceq 'hash-object -t blob --stdin' -or ($attemptStarted -and $op -ceq 'hash-object -w -t blob --stdin'))) 'OBJECT_WRITE_NOT_AUTHORIZED'
                $pin=@($objects | Where-Object { $_[1] -eq $data.Length -and $_[2] -ceq (Sha $data) })
                Need ($pin.Count -eq 1) 'UNPINNED_OBJECT_BYTES'
                $selected=$pin[0]; $payloadPath=Join-Path $bundle ($selected[0]+'.blob'); NoReparse $payloadPath
                $payloadHandle=[IO.File]::Open($payloadPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
                Need ($payloadHandle.Length -eq $selected[1]) 'LOCKED_PAYLOAD_SIZE_MISMATCH'
                $locked=New-Object IO.MemoryStream
                try { $payloadHandle.CopyTo($locked); Need ((Sha $locked.ToArray()) -ceq $selected[2]) 'LOCKED_PAYLOAD_HASH_MISMATCH' } finally { $locked.Dispose() }
                $writeFlag=''; if ($op -ceq 'hash-object -w -t blob --stdin') { $writeFlag='-w ' }
                # Git reads only this exact locked external payload. No text/BOM conversion or filters.
                $op='hash-object '+$writeFlag+'--no-filters -t blob -- "'+$payloadPath+'"'
            }
            if ($family -eq 'config') { Need ($op -ceq 'config --local --no-includes --null --list') 'CONFIG_WRITE_NOT_AUTHORIZED' }
            $p = New-Object Diagnostics.Process; $s = New-Object Diagnostics.ProcessStartInfo
            $s.FileName=$git; $s.UseShellExecute=$false; $s.CreateNoWindow=$true
            $s.Arguments='--no-pager --no-lazy-fetch --no-replace-objects --no-optional-locks -C "'+$root+'" -c core.commitGraph=false -c core.fsmonitor=false -c core.untrackedCache=false -c gc.auto=0 -c maintenance.auto=false -c protocol.allow=never -c credential.helper= -c credential.interactive=false '+$op
            $s.RedirectStandardInput=$true; $s.RedirectStandardOutput=$true; $s.RedirectStandardError=$true
            $s.EnvironmentVariables.Clear()
            foreach ($k in @('SystemRoot','WINDIR','PATH','TEMP','TMP','COMSPEC')) { $v=[Environment]::GetEnvironmentVariable($k); if ($null -ne $v) { $s.EnvironmentVariables[$k]=$v } }
            foreach ($pair in @(@('GIT_CONFIG_NOSYSTEM','1'),@('GIT_CONFIG_GLOBAL','NUL'),@('GIT_NO_LAZY_FETCH','1'),@('GIT_NO_REPLACE_OBJECTS','1'),@('GIT_OPTIONAL_LOCKS','0'),@('GIT_ALLOW_PROTOCOL',''),@('GIT_TERMINAL_PROMPT','0'),@('LC_ALL','C'))) { $s.EnvironmentVariables[$pair[0]]=$pair[1] }
            $p.StartInfo=$s; $m=New-Object IO.MemoryStream
            Need ($p.Start()) 'GIT_START_FAILED'
            $output=$p.StandardOutput.BaseStream.CopyToAsync($m); $errorOutput=$p.StandardError.ReadToEndAsync()
            $p.StandardInput.BaseStream.Close()
            if (-not $p.WaitForExit(60000)) { try { $p.Kill() } catch {}; throw 'GIT_TIMEOUT' }
            Need ($output.Wait(5000) -and $errorOutput.Wait(5000)) 'GIT_DRAIN_TIMEOUT'
            Need ($m.Length -le 16777216 -and $errorOutput.Result.Length -le 131072) 'GIT_OUTPUT_LIMIT'
            Need ($p.ExitCode -eq 0) ('GIT_'+($family.TrimStart('-').ToUpperInvariant().Replace('-','_'))+'_EXIT_'+$p.ExitCode)
            return ,$m.ToArray()
        } finally {
            if ($null -ne $p) { try { if (-not $p.HasExited) { $p.Kill() } } catch {}; $p.Dispose() }
            if ($null -ne $m) { $m.Dispose() }
            if ($null -ne $payloadHandle) { $payloadHandle.Dispose() }
        }
    }
    function GitText([string]$op) { return ([Text.Encoding]::UTF8.GetString((GitBytes $op))).TrimEnd([char[]]"`r`n") }
    function Walk([string]$oid) {
        $text=GitText ('rev-list --objects --no-object-names --missing=print '+$oid+' --')
        $present=0; $missing=New-Object 'System.Collections.Generic.List[string]'; $seen=New-Object 'System.Collections.Generic.HashSet[string]'
        foreach ($line in ($text -split '\r?\n')) {
            Need ($line -cmatch '^\??[0-9a-f]{40}$') 'OBJECT_OUTPUT_INVALID'
            $key=$line.TrimStart('?'); Need ($seen.Add($key)) 'DUPLICATE_OBJECT_OUTPUT'
            if ($line.StartsWith('?')) { $missing.Add($key); Need ($missing.Count -le 256) 'MISSING_OBJECT_LIMIT' } else { $present++ }
        }
        return [pscustomobject]@{ Present=$present; Missing=@($missing | Sort-Object) }
    }
    function Metadata {
        $meta=@{}; $obj=@{}; $total=0L; $count=0
        $stack=New-Object 'System.Collections.Generic.Stack[string]'; $stack.Push((Join-Path $root '.git'))
        while ($stack.Count) {
            foreach ($p in [IO.Directory]::EnumerateFileSystemEntries($stack.Pop())) {
                NoReparse $p; $attr=[IO.File]::GetAttributes($p)
                if (($attr -band [IO.FileAttributes]::Directory) -ne 0) { $stack.Push($p); continue }
                $count++; Need ($count -le 20000) 'GIT_FILE_COUNT_LIMIT'
                $b=ReadBytes $p; $total+=$b.Length; Need ($total -le 536870912) 'GIT_STORE_BYTE_LIMIT'
                $rel=$p.Substring(([IO.Path]::GetFullPath((Join-Path $root '.git'))).Length+1).Replace('\','/')
                if ($rel.StartsWith('objects/')) { $obj[$rel]=Sha $b } else { $meta[$rel]=Sha $b }
            }
        }
        return [pscustomobject]@{ Meta=(Digest $meta); Objects=$obj }
    }
    function SourceDigest {
        $source=@{}; $total=0L
        $paths=(GitText 'ls-files -z').Split([char]0)
        foreach ($path in $paths) {
            if ($path -eq '') { continue }
            Need (-not [IO.Path]::IsPathRooted($path) -and $path -notmatch '(^|/)\.\.(/|$)|[\r\n:\\]' -and -not $path.StartsWith('.git/')) 'UNSAFE_TRACKED_PATH'
            Need (-not $source.ContainsKey($path)) 'DUPLICATE_TRACKED_PATH'
            $b=ReadBytes (Join-Path $root $path); $total+=$b.Length; Need ($total -le 104857600 -and $source.Count -lt 5000) 'TRACKED_SOURCE_LIMIT'
            $source[$path]=Sha $b
        }
        return Digest $source
    }
    function Snapshot {
        NoReparse $root; NoReparse $git; NoReparse $bundle
        Need ([IO.Directory]::Exists((Join-Path $root '.git'))) 'DIRECT_GIT_DIRECTORY_REQUIRED'
        foreach ($rel in @('commondir','shallow','info/grafts','objects/info/alternates','objects/info/http-alternates')) { Need (-not [IO.File]::Exists((Join-Path "$root/.git" $rel))) 'UNQUALIFIED_GIT_LAYOUT' }
        $cfg=GitText 'config --local --no-includes --null --list'
        foreach ($entry in $cfg.Split([char]0)) { Need ($entry.Split([char]10)[0] -notmatch '^(include|includeif|filter|extensions)\.') 'OTHER_UNQUALIFIED_CONFIG' }
        Need ((GitText '--version') -ceq 'git version 2.55.0.windows.4') 'EXACT_GIT_VERSION_MISMATCH'
        $refs=GitText 'for-each-ref --format="%(refname) %(objectname)"'
        Need ($refs -notmatch '(?m)^refs/replace/|(?m)^refs/tags/workbench-v0\.60-accepted ') 'FORBIDDEN_REF'
        Need ((GitText 'rev-parse --verify HEAD') -ceq $head) 'HEAD_MISMATCH'
        Need ((GitText ('rev-parse --verify '+$tag)) -ceq $tagObject) 'TAG_OBJECT_MISMATCH'
        Need ((GitText ('cat-file -t '+$tagObject)) -ceq 'tag') 'ANNOTATED_TAG_REQUIRED'
        Need ((GitText ('rev-parse --verify '+$tag+'^{commit}')) -ceq $head) 'TAG_PEEL_MISMATCH'
        Need ((GitText 'rev-parse --verify refs/tags/workbench-v0.55.2-accepted^{commit}') -ceq $first) 'PREDECESSOR_TAG_MISMATCH'
        $headers=((GitText ('cat-file commit '+$head)) -split "`n`n",2)[0] -split "`n"
        $parents=@($headers | Where-Object { $_.StartsWith('parent ') } | ForEach-Object { $_.Substring(7) })
        Need (($parents -join ' ') -ceq ($first+' '+$second)) 'ORDERED_PARENTS_MISMATCH'
        Need (@($headers | Where-Object { $_ -ceq ('tree '+$treeId) }).Count -eq 1) 'TREE_MISMATCH'
        foreach ($c in $canonical) { Need ((FileSha (Join-Path $root $c[0])) -ceq $c[1]) 'CANONICAL_RECEIPT_CHANGED' }
        $storage=Metadata
        return [pscustomobject]@{ Refs=$refs; Meta=$storage.Meta; Objects=$storage.Objects; Sources=(SourceDigest); Current=(Walk $treeId); History=(Walk ($head+' '+$tag)) }
    }
    function BeforeGate($s) {
        Need ($s.Current.Present -eq $expectedTreeCount -and $s.Current.Missing.Count -eq 0) 'CURRENT_OBJECT_STATE_DRIFT'
        Need ($s.History.Present -eq $expectedHistoryCount -and ($s.History.Missing -join ' ') -ceq (($objects | ForEach-Object { $_[0] }) -join ' ')) 'MISSING_SET_DRIFT'
    }
    function WriteNew([string]$path,$value) {
        NoReparse $path; $bytes=[Text.Encoding]::UTF8.GetBytes(($value | ConvertTo-Json -Depth 8))
        $s=New-Object IO.FileStream($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None,4096,[IO.FileOptions]::WriteThrough)
        try { $s.Write($bytes,0,$bytes.Length); $s.Flush($true) } finally { $s.Dispose() }
        Need ((FileSha $path) -ceq (Sha $bytes)) 'NEW_RECEIPT_READBACK_FAILED'
    }
    try {
        Need ($args.Count -eq 0) 'ARGUMENTS_NOT_ACCEPTED'
        NoReparse $outDir
        Need (-not [IO.File]::Exists($attempt) -and -not [IO.File]::Exists($receiptPath)) 'ATTEMPT_OR_RECEIPT_EXISTS_NO_RETRY'
        $payload=@{}
        foreach ($o in $objects) {
            $b=ReadBytes (Join-Path $bundle ($o[0]+'.blob')) 131072
            Need ($b.Length -eq $o[1] -and (Sha $b) -ceq $o[2]) 'PAYLOAD_IDENTITY_MISMATCH'
            $payload[$o[0]]=$b
        }
        $before=Snapshot; BeforeGate $before
        # The no-write probe uses the same pinned-file path and no-filters semantics as the later write.
        foreach ($o in $objects) {
            $probe=[Text.Encoding]::ASCII.GetString((GitBytes 'hash-object -t blob --stdin' $payload[$o[0]])).Trim()
            Need ($probe -ceq $o[0]) 'PAYLOAD_GIT_CHANNEL_ID_MISMATCH'
        }
        $previewAt=[DateTime]::UtcNow
        Write-Output ('LOCAL PREVIEW: '+$root+'; HEAD='+$head+'; TAG_OBJECT='+$tagObject)
        Write-Output 'Only six exact historical blob objects (17695 raw bytes) and new local attempt/receipt files may be added.'
        Write-Output 'No source/ref/tag/config/index change, no fetch/network, no publication, no retry. Not all-six atomic: partial failure remains recorded and must not be retried.'
        Write-Output ('Enter exactly: '+$confirmation)
        if ([Console]::ReadLine() -cne $confirmation) { Write-Output 'CANCELLED_NO_WRITE'; return }
        $age=([DateTime]::UtcNow-$previewAt).TotalSeconds; Need ($age -ge 0 -and $age -le 300) 'PREVIEW_EXPIRED'
        $fresh=Snapshot; BeforeGate $fresh
        Need ($fresh.Refs -ceq $before.Refs -and $fresh.Meta -ceq $before.Meta -and $fresh.Sources -ceq $before.Sources -and (Digest $fresh.Objects) -ceq (Digest $before.Objects)) 'SNAPSHOT_CHANGED_BEFORE_IMPORT'
        NoReparse $outDir; [IO.Directory]::CreateDirectory($outDir) | Out-Null; NoReparse $outDir
        WriteNew $attempt ([ordered]@{ Schema='matawaka.workbench-six-blob-import-attempt/v0.1'; State='ATTEMPT_CONSUMED_NO_RETRY'; CreatedAt=[DateTimeOffset]::Now; AcceptedHead=$head; AcceptedTagObject=$tagObject; Objects=@($objects | ForEach-Object { $_[0] }); ExplicitConfirmation=$confirmation; PublicationAuthorized=$false; NetworkAuthorized=$false; RetryAuthorized=$false })
        $attemptStarted=$true
        foreach ($o in $objects) {
            $writeAttempted=$true
            $written=[Text.Encoding]::ASCII.GetString((GitBytes 'hash-object -w -t blob --stdin' $payload[$o[0]])).Trim()
            Need ($written -ceq $o[0]) 'IMPORTED_OBJECT_ID_MISMATCH'
            Need ((Sha (GitBytes ('cat-file blob '+$o[0]))) -ceq $o[2]) 'IMPORTED_BYTES_READBACK_MISMATCH'
            $completed.Add($o[0])
        }
        $after=Snapshot
        Need ($after.Refs -ceq $before.Refs -and $after.Meta -ceq $before.Meta -and $after.Sources -ceq $before.Sources) 'PROTECTED_STATE_CHANGED'
        foreach ($key in $before.Objects.Keys) { Need ($after.Objects.ContainsKey($key) -and $after.Objects[$key] -ceq $before.Objects[$key]) 'PREEXISTING_OBJECT_STORE_CHANGED' }
        $added=@($after.Objects.Keys | Where-Object { -not $before.Objects.ContainsKey($_) } | Sort-Object)
        $allowed=@($objects | ForEach-Object { 'objects/'+$($_[0]).Substring(0,2)+'/'+$($_[0]).Substring(2) } | Sort-Object)
        Need (($added -join ' ') -ceq ($allowed -join ' ')) 'OBJECT_STORE_DELTA_NOT_EXACT_SIX'
        Need ($after.Current.Present -eq $expectedTreeCount -and $after.Current.Missing.Count -eq 0 -and $after.History.Present -eq ($expectedHistoryCount+6) -and $after.History.Missing.Count -eq 0) 'POST_IMPORT_CLOSURE_NOT_COMPLETE'
        $receipt=[ordered]@{ Schema='matawaka.workbench-offline-six-blob-import-receipt/v0.1'; Status='SIX_HISTORICAL_BLOBS_IMPORTED_OFFLINE_NO_REF_MUTATION'; ObservedAt=[DateTimeOffset]::Now; AcceptedHead=$head; AcceptedTag=$tag; AcceptedTagObject=$tagObject; CurrentTree=$treeId; GitExecutableSha256=$gitSha; ImportedObjects=@($completed); ImportedRawBytes=17695; CurrentTreePresentObjects=$after.Current.Present; CurrentTreeMissingBoundaryObjects=$after.Current.Missing.Count; FullHistoryPresentObjects=$after.History.Present; FullHistoryMissingBoundaryObjects=$after.History.Missing.Count; ReachableClosureComplete=$true; GitMetadataSha256Before=$before.Meta; GitMetadataSha256After=$after.Meta; TrackedSourceDigestBefore=$before.Sources; TrackedSourceDigestAfter=$after.Sources; PreexistingObjectFilesUnchanged=$true; AddedObjectFiles=@($added); AttemptReceiptSha256=(FileSha $attempt); OriginalCanonicalReceiptsUnchanged=$true; ObjectDatabaseChanged=$true; RefMutationPerformed=$false; SourceMutationPerformed=$false; ConfigMutationPerformed=$false; IndexMutationPerformed=$false; NetworkOperationImplemented=$false; PublicationAuthorized=$false; RetryAuthorized=$false; WholeWorkingTreeVerified=$false; WholeRuntimeBinaryTreeVerified=$false; OsNetworkIsolationProven=$false; Note='Exact historical blob addition only. Full source integrity and publication preflight remain separate; active promisor is unchanged. Stable snapshots are not an OS concurrency exclusion.' }
        WriteNew $receiptPath $receipt
        Write-Output 'COMPLETED: SIX_HISTORICAL_BLOBS_IMPORTED_OFFLINE_NO_REF_MUTATION'
        Write-Output ('Receipt: '+$receiptPath)
        Write-Output ('Receipt SHA-256: '+(FileSha $receiptPath))
        Write-Output 'STOP. Return the new receipt. Do not run old preflight, Update, Accept or push.'
    } catch {
        $reason=$_.Exception.Message; if ($reason -cnotmatch '^[A-Z][A-Z0-9_]+$') { $reason='LOCAL_IMPORT_ERROR' }
        Write-Output ('IMPORT_REFUSED: '+$reason)
        if ($writeAttempted) { Write-Output ('LOCAL OBJECT WRITES MAY HAVE OCCURRED; verified completed writes='+$completed.Count+'; no automatic rollback or retry.') }
        else { Write-Output 'No object write was attempted by this invocation. A local attempt marker may exist; do not delete it.' }
        Write-Output 'STOP. No network operation is implemented. Do not bypass, retry, edit receipts or push.'
    }
}
