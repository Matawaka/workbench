[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$StatePath,
    [switch]$ExecuteQualifiedTrial
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$QualifiedCoordinatorHead = '8bb6ccad8a69d1c937ea9a6e65899aeff89dc1fd'
$Authorization = 'ISSUE-107-CONTROLLED-EXECUTOR-TRIAL'
$ProofIp = '10.77.0.1'
$PeerIp = '10.77.0.2'

function Fail([string]$Message) { throw "FAIL_CLOSED: $Message" }
function Sha256([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

if (-not $ExecuteQualifiedTrial) {
    Fail 'explicit -ExecuteQualifiedTrial switch is required; preparation alone is not authority to run the network trial'
}

$stateFull = (Resolve-Path -LiteralPath $StatePath).Path
$state = Get-Content -LiteralPath $stateFull -Raw | ConvertFrom-Json
if ($state.schema -ne 'matawaka.windows-private-peer-executor-state/v0.1') { Fail 'executor state schema mismatch' }
if ($state.trialExecuted -ne $false -or $state.proofClaimed -ne $false) { Fail 'executor state is not fresh' }
if ($state.qualifiedCoordinatorHead -ne $QualifiedCoordinatorHead) { Fail 'qualified coordinator head mismatch' }
if ($state.proofNodeAddress -ne $ProofIp -or $state.peerAddress -ne $PeerIp -or $state.managementPort -ne 41060 -or $state.targetPort -ne 41061) { Fail 'executor topology mismatch' }

$exe = [string]$state.coordinatorExe
$trial = [string]$state.trialRoot
$manifest = [string]$state.manifestPath
$evidence = [string]$state.evidencePath
$stdoutPath = [string]$state.stdoutPath
$provisioning = [string]$state.provisioningPath
foreach ($p in @($exe,$manifest,$provisioning)) { if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { Fail "required file missing: $p" } }
if (-not (Test-Path -LiteralPath $trial -PathType Container)) { Fail 'trial root missing' }
if (Test-Path -LiteralPath $evidence) { Fail 'proof evidence path must not pre-exist' }
if (Test-Path -LiteralPath $stdoutPath) { Fail 'coordinator stdout path must not pre-exist' }
$attemptMarker = Join-Path (Split-Path -Parent $evidence) 'PRIVATE-PEER-PROOF-ATTEMPT.json'
if (Test-Path -LiteralPath $attemptMarker) { Fail 'proof attempt marker already exists' }

$proofAddresses = @(Get-NetIPAddress -AddressFamily IPv4 -IPAddress $ProofIp -ErrorAction SilentlyContinue | Where-Object PrefixLength -eq 30)
if ($proofAddresses.Count -ne 1) { Fail 'exact 10.77.0.1/30 proof address no longer present' }
if (@(Get-NetIPAddress -AddressFamily IPv4 -IPAddress $PeerIp -ErrorAction SilentlyContinue).Count -ne 0) { Fail 'peer address 10.77.0.2 became local' }
$ifIndex = [int]$proofAddresses[0].InterfaceIndex
if (@(Get-NetRoute -AddressFamily IPv4 -InterfaceIndex $ifIndex -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue).Count -ne 0) { Fail 'test interface acquired a default route' }
$dns = @(Get-DnsClientServerAddress -InterfaceIndex $ifIndex -AddressFamily IPv4 -ErrorAction Stop).ServerAddresses
if (@($dns | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count -ne 0) { Fail 'test interface acquired IPv4 DNS configuration' }

$manifestObject = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
$expected = @('Workbench.AppContainerProbe.exe','Workbench.AppContainerProbe.dll','Workbench.AppContainerProbe.runtimeconfig.json','Workbench.AppContainerProbe.deps.json')
$manifestNames = @($manifestObject.files.PSObject.Properties.Name | Sort-Object)
if (($manifestNames -join '|') -ne (($expected | Sort-Object) -join '|')) { Fail 'runtime manifest keys drifted' }
foreach ($name in $expected) {
    $path = Join-Path $trial $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { Fail "trial runtime file missing: $name" }
    $expectedHash = [string]$manifestObject.files.$name
    if ((Sha256 $path) -ne $expectedHash.ToLowerInvariant()) { Fail "trial runtime hash drift: $name" }
}

Write-Host 'PRIVATE_PEER_REAL_TRIAL_STARTING'
Write-Host 'No preflight TCP probe is performed. The coordinator BEGIN message will be the first management connection.'
Write-Host 'The Mac peer listener must already be running on 10.77.0.2.'

$argsList = @('--peer-private-trial',$trial,$manifest,$evidence,$QualifiedCoordinatorHead,$Authorization)
$lines = @()
$exitCode = 255
try {
    $lines = @(& $exe @argsList 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
}
finally {
    $lines | Set-Content -LiteralPath $stdoutPath -Encoding utf8
}

$state.trialExecuted = $true
$state | Add-Member -NotePropertyName trialExitCode -NotePropertyValue $exitCode -Force
$state | Add-Member -NotePropertyName completedUtc -NotePropertyValue ([DateTimeOffset]::UtcNow.ToString('o')) -Force
if (Test-Path -LiteralPath $evidence) {
    $state | Add-Member -NotePropertyName evidenceSha256 -NotePropertyValue (Sha256 $evidence) -Force
}
$state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $stateFull -Encoding utf8

if ($exitCode -ne 0) {
    Write-Host "PRIVATE_PEER_REAL_TRIAL_FAIL_CLOSED exitCode=$exitCode"
    Write-Host "stdout=$stdoutPath"
    if (Test-Path -LiteralPath $evidence) { Write-Host "evidence=$evidence" }
    exit $exitCode
}

if (-not (Test-Path -LiteralPath $evidence -PathType Leaf)) { Fail 'trial returned zero but proof evidence is absent' }
$proof = Get-Content -LiteralPath $evidence -Raw | ConvertFrom-Json
if ($proof.Schema -ne 'matawaka.workbench-windows-private-peer-isolation-proof/v0.1') { Fail 'proof schema mismatch' }
if ($proof.Status -ne 'OS_PRIVATE_NETWORK_PATH_NOT_AUTHORIZED_PROVEN' -or $proof.OsPrivateNetworkIsolationProven -ne $true) { Fail 'zero exit without exact proof status' }
if ($proof.Child.DiagnoseInfoType -ne 1 -or $proof.Child.DiagnosticClassificationUsedAsProof -ne $true) { Fail 'exact PRIVATE_NETWORK(1) diagnostic evidence absent' }
if ($proof.Child.SocketBehaviorUsedAsProof -ne $false -or $proof.SocketTimeoutPromotedToProof -ne $false) { Fail 'socket behavior was promoted to proof' }
if ($proof.Peer.ChildWindowAcceptedConnections -ne 0 -or $proof.Peer.TargetAcceptsTotal -ne 1 -or $proof.Peer.ProtocolSatisfied -ne $true) { Fail 'peer zero-accept evidence absent' }
if ($proof.CleanupSucceeded -ne $true -or $proof.ProfileRemoved -ne $true -or $proof.ProcessExited -ne $true) { Fail 'cleanup evidence incomplete' }
if ($proof.DnsUsed -ne $false -or $proof.InternetTargetUsed -ne $false -or $proof.ExternalServiceUsed -ne $false) { Fail 'forbidden network effect recorded' }
if ($proof.NetworkIsolationConfigMutated -ne $false -or $proof.FirewallRuleMutated -ne $false -or $proof.RouteOrInterfaceMutated -ne $false -or $proof.GlobalWindowsPolicyMutated -ne $false) { Fail 'forbidden global/network mutation recorded' }
if ($proof.ModelStarted -ne $false -or $proof.GameAccessed -ne $false -or $proof.ProductionProviderRegistered -ne $false) { Fail 'authority widened beyond isolation qualification' }

$state = Get-Content -LiteralPath $stateFull -Raw | ConvertFrom-Json
$state.proofClaimed = $true
$state | Add-Member -NotePropertyName proofStatus -NotePropertyValue 'OS_PRIVATE_NETWORK_PATH_NOT_AUTHORIZED_PROVEN' -Force
$state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $stateFull -Encoding utf8

Write-Host 'OS_PRIVATE_NETWORK_PATH_NOT_AUTHORIZED_PROVEN'
Write-Host "evidence=$evidence"
Write-Host "evidenceSha256=$(Sha256 $evidence)"
Write-Host "stdout=$stdoutPath"
exit 0
