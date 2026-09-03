Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms

$ExpectedSourceCommit = '1545c98f9982917d6d10fcd76e86938b4eb7a2a0'
$ExpectedPredecessorTag = 'workbench-v0.51.1-accepted'
$ExpectedTargetTag = 'workbench-v0.51.2-accepted'
$ExpectedParent = 'a8a93143e942a02913475013e355d61b2fa6bee8'

$ExpectedV0511Files = [ordered]@{
    'PATCH-v0.51.1.md' = 'b6dd85c85afd67177cf5d5608554d75a5824819d6ff70443581ef8a86f60c64c'
    'src/Matawaka.Workbench.App/App.xaml.cs' = '12c7b5974d6a7349149e9f1e2b80d720a131f668b5ac4ea2bd3578be3077765d'
    'src/Matawaka.Workbench.App/LocalCheckpointV0511Service.cs' = '7847c8ad10f3f69da01aec21f23dc64aa753ddaccb43d02928d429465dff095d'
    'src/Matawaka.Workbench.App/MainWindow.V0511.cs' = '1eb7e3d0470959958bd6c321f0c38c44957f51dab14951910306d98c50449291'
    'src/Matawaka.Workbench.App/WorkbenchV0511AcceptanceHarness.cs' = '39f47c7c0f13a2080d31c933fb890d8ef00fc4894d21300f337f56ee3e4fcd94'
}

$PayloadPaths = @(
    'PATCH-v0.51.2.md',
    'src/Matawaka.Workbench.App/App.xaml.cs',
    'src/Matawaka.Workbench.App/GlobalUsings.V0512.cs',
    'src/Matawaka.Workbench.App/LocalAppReadLeaseExactRevokeV0512Service.cs',
    'src/Matawaka.Workbench.App/LocalAppsActionDialogV0512.cs',
    'src/Matawaka.Workbench.App/LocalCheckpointV0512Service.cs',
    'src/Matawaka.Workbench.App/MainWindow.V0512.Acceptance.cs',
    'src/Matawaka.Workbench.App/MainWindow.V0512.cs',
    'src/Matawaka.Workbench.App/WorkbenchV0512AcceptanceHarness.cs'
)

function Show-Info([string]$message) {
    [System.Windows.Forms.MessageBox]::Show($message, 'Matawaka Workbench v0.51.2 binder', [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}

function Refuse([string]$message) {
    [System.Windows.Forms.MessageBox]::Show($message, 'Matawaka Workbench v0.51.2 binder — REFUSED', [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    throw $message
}

function Run-Git([string[]]$args) {
    $output = & $script:GitExe @args 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($args -join ' ') failed:`n$($output -join [Environment]::NewLine)"
    }
    return @($output)
}

function Sha256([string]$path) {
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

try {
    $script:GitExe = (Get-Command git.exe -ErrorAction Stop).Source

    $bundleSourcePath = Join-Path $PSScriptRoot 'bundle-source.json'
    $payloadRoot = Join-Path $PSScriptRoot 'payload'
    if (-not (Test-Path -LiteralPath $bundleSourcePath -PathType Leaf)) { Refuse 'bundle-source.json is missing.' }
    if (-not (Test-Path -LiteralPath $payloadRoot -PathType Container)) { Refuse 'v0.51.2 payload directory is missing.' }

    $bundleSource = Get-Content -LiteralPath $bundleSourcePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$bundleSource.SourceCommit -ne $ExpectedSourceCommit) {
        Refuse "Unexpected v0.51.2 source commit.`nExpected: $ExpectedSourceCommit`nObserved: $($bundleSource.SourceCommit)"
    }

    $observedPayload = @(
        Get-ChildItem -LiteralPath $payloadRoot -File -Recurse | ForEach-Object {
            [IO.Path]::GetRelativePath($payloadRoot, $_.FullName).Replace('\','/')
        } | Sort-Object
    )
    $expectedPayload = @($PayloadPaths | Sort-Object)
    if ($observedPayload.Count -ne $expectedPayload.Count -or @(Compare-Object $expectedPayload $observedPayload).Count -ne 0) {
        Refuse "Payload file set differs from the fixed v0.51.2 source set.`nNo Workbench files were changed."
    }

    $sourceMap = @{}
    foreach ($entry in $bundleSource.Files) { $sourceMap[[string]$entry.Path] = ([string]$entry.Sha256).ToLowerInvariant() }
    foreach ($relative in $PayloadPaths) {
        if (-not $sourceMap.ContainsKey($relative)) { Refuse "Missing payload binding for $relative" }
        $full = Join-Path $payloadRoot ($relative -replace '/', [IO.Path]::DirectorySeparatorChar)
        $sha = Sha256 $full
        if ($sha -ne $sourceMap[$relative]) {
            Refuse "Bundled v0.51.2 payload hash mismatch:`n$relative`nExpected: $($sourceMap[$relative])`nObserved: $sha"
        }
    }

    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = 'Select the Matawaka workspace root — the folder that contains Workbench'
    $dialog.ShowNewFolderButton = $false
    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { throw 'Operator cancelled workspace selection.' }

    $workspace = [IO.Path]::GetFullPath($dialog.SelectedPath)
    $repo = Join-Path $workspace 'Workbench'
    if (-not (Test-Path -LiteralPath (Join-Path $repo '.git'))) { Refuse "Selected folder does not contain Workbench Git repository:`n$repo" }

    Push-Location $repo
    try {
        $status = @(Run-Git @('status','--porcelain=v1','--untracked-files=all') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($status.Count -ne 0) { Refuse "Workbench working tree is not clean.`n`n$($status -join "`n")`n`nNothing was changed." }

        $head = ((Run-Git @('rev-parse','HEAD')) -join "`n").Trim().ToLowerInvariant()
        if ($head.Length -ne 40 -or $head.ToCharArray().Where({ -not [Uri]::IsHexDigit($_) }).Count -ne 0) { Refuse "Current HEAD is not a 40-character Git SHA: $head" }

        $tagHead = ((Run-Git @('rev-list','-n','1',$ExpectedPredecessorTag)) -join "`n").Trim().ToLowerInvariant()
        if ($tagHead -ne $head) { Refuse "$ExpectedPredecessorTag is not at current HEAD.`nTag: $tagHead`nHEAD: $head" }

        $parent = ((Run-Git @('rev-parse','HEAD^')) -join "`n").Trim().ToLowerInvariant()
        if ($parent -ne $ExpectedParent) { Refuse "Accepted v0.51.1 parent mismatch.`nExpected v0.51: $ExpectedParent`nObserved parent: $parent" }

        $targetTag = ((Run-Git @('tag','--list',$ExpectedTargetTag)) -join "`n").Trim()
        if (-not [string]::IsNullOrWhiteSpace($targetTag)) { Refuse "$ExpectedTargetTag already exists. Binder is only for v0.51.1 -> v0.51.2." }

        $changedInCommit = @(
            Run-Git @('diff-tree','--no-commit-id','--name-only','-r','HEAD') |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_.Trim().Replace('\','/') } |
            Sort-Object
        )
        $expectedV0511Set = @($ExpectedV0511Files.Keys | Sort-Object)
        if ($changedInCommit.Count -ne $expectedV0511Set.Count -or @(Compare-Object $expectedV0511Set $changedInCommit).Count -ne 0) {
            Refuse "Accepted v0.51.1 commit path set differs from the supplied source manifest.`nNothing was changed."
        }

        foreach ($relative in $ExpectedV0511Files.Keys) {
            $full = Join-Path $repo ($relative -replace '/', [IO.Path]::DirectorySeparatorChar)
            if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { Refuse "Accepted v0.51.1 file is missing: $relative" }
            $sha = Sha256 $full
            if ($sha -ne $ExpectedV0511Files[$relative]) {
                Refuse "Accepted v0.51.1 source hash mismatch:`n$relative`nExpected: $($ExpectedV0511Files[$relative])`nObserved: $sha"
            }
        }

        $outDir = Join-Path $PSScriptRoot 'bound-output'
        New-Item -ItemType Directory -Path $outDir -Force | Out-Null
        $stage = Join-Path $env:TEMP ("matawaka-v0512-bind-" + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $stage -Force | Out-Null
        try {
            $stagePayload = Join-Path $stage 'payload'
            Copy-Item -LiteralPath $payloadRoot -Destination $stagePayload -Recurse -Force

            $items = @()
            foreach ($relative in $PayloadPaths) {
                $full = Join-Path $stagePayload ($relative -replace '/', [IO.Path]::DirectorySeparatorChar)
                $items += [ordered]@{ Path = $relative; Sha256 = (Sha256 $full) }
            }

            $manifest = [ordered]@{
                Schema = 'matawaka.workbench-update-package/v0.10'
                PackageVersion = '0.10'
                TargetVersion = '0.51.2'
                PredecessorTag = $ExpectedPredecessorTag
                PredecessorCommit = $head
                TargetTag = $ExpectedTargetTag
                PayloadRoot = 'payload/'
                Files = $items
                NetworkAccessRequested = $false
                CatalogMutationRequested = $false
                AgentExecuteRequested = $false
                ArbitraryProcessExecutionRequested = $false
                InstallerScriptExecutionRequested = $false
                NonEffects = @(
                    'source only',
                    'exact local workbench-v0.51.1-accepted predecessor only',
                    'no private app bytes',
                    'no runtime lease/bearer/clipboard/MCP endpoint bytes',
                    'no automatic Secure MCP Tunnel',
                    'no remote publication authority',
                    'local predecessor binding performed by read-only Git/tag/hash checks'
                )
            }
            $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stage 'workbench-update-manifest.json') -Encoding UTF8

            $short = $head.Substring(0,12)
            $zipPath = Join-Path $outDir "workbench-v0.51.2-end-read-session-update-$short.zip"
            if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
            Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipPath -CompressionLevel Optimal
            $zipSha = Sha256 $zipPath
            $shaPath = "$zipPath.sha256.txt"
            "SHA256  $zipSha`r`nPredecessor  $head`r`nPredecessorTag  $ExpectedPredecessorTag`r`nSourceCode  $ExpectedSourceCommit`r`n" | Set-Content -LiteralPath $shaPath -Encoding UTF8

            Show-Info @"
Local binding succeeded.

Exact predecessor:
$head
$ExpectedPredecessorTag

Created update ZIP:
$zipPath

SHA-256:
$zipSha

The Workbench repository was not modified.
Use this ZIP with Update Workbench v0.51.1.
"@
        }
        finally {
            if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
        }
    }
    finally { Pop-Location }
}
catch {
    if ($_.Exception.Message -notmatch '^Operator cancelled') {
        Write-Host $_.Exception.ToString() -ForegroundColor Red
    }
    Write-Host ''
    Write-Host 'Press Enter to close...' -ForegroundColor Yellow
    [void][Console]::ReadLine()
    exit 1
}
