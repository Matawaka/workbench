[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$QualifiedCoordinatorHead = '8bb6ccad8a69d1c937ea9a6e65899aeff89dc1fd'
$CoordinatorSourceSha256 = '8f59e72f2d5fa556acc39e5054abac5fcd6cf0a200e61bebb4d1b968df939288'
$CoordinatorProjectSha256 = '1e4b7b4a0ee3ed8803ad4e55273ce8e43e7ef71e0227756a68a9fb4f4ccc8b21'
$QualifiedPeerHead = 'efff344bc1418e9b1da946a8493f854bf8ada9af'
$PeerSourceSha256 = 'b01ab34cd9923d34b72c4ca557ffb292cb9621a896be0fbbb020ddd88d8bc049'
$PeerContractSha256 = '793de6f7d0f68317d331b2e7e3eec20e02dcdd9acac5c665ceab633122aea1ca'
$NativeBoundaryBlob = '8a8f91a13c114e9c224cf449193800f0fedfdaf2'
$ProofIp = '10.77.0.1'
$PeerIp = '10.77.0.2'
$PrefixLength = 30

function Fail([string]$Message) { throw "FAIL_CLOSED: $Message" }
function Sha256([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

$repo = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$source = Join-Path $repo 'integrations\model-invocation\windows-private-peer-proof-v1\Program.cs'
$project = Join-Path $repo 'integrations\model-invocation\windows-private-peer-proof-v1\PrivatePeerProofV1.csproj'
$native = Join-Path $repo 'integrations\model-invocation\windows-isolation-probe\NativeBoundary.cs'
$peerSource = Join-Path $repo 'integrations\model-invocation\windows-private-peer-executor-v1\Program.cs'
$peerContract = Join-Path $repo 'integrations\model-invocation\windows-private-peer-executor-v1\PEER_CONTRACT.json'

if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess) { Fail 'Windows x64 OS and process are required' }

$cv = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$productName = [string]$cv.ProductName
$build = [string]$cv.CurrentBuildNumber
if ($productName -notmatch 'Windows Server 2025') { Fail "canonical executor requires Windows Server 2025; found '$productName'" }
if ($build -ne '26100') { Fail "canonical executor requires Windows Server 2025 build family 26100; found '$build'" }

foreach ($p in @($source,$project,$native,$peerSource,$peerContract)) { if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { Fail "required file missing: $p" } }
if ((Sha256 $source) -ne $CoordinatorSourceSha256) { Fail 'qualified coordinator source drift' }
if ((Sha256 $project) -ne $CoordinatorProjectSha256) { Fail 'qualified coordinator project drift' }
if ((Sha256 $peerSource) -ne $PeerSourceSha256) { Fail 'qualified peer source drift' }
if ((Sha256 $peerContract) -ne $PeerContractSha256) { Fail 'qualified peer contract drift' }
$observedNativeBlob = (& git -C $repo hash-object $native).Trim()
if ($LASTEXITCODE -ne 0 -or $observedNativeBlob -ne $NativeBoundaryBlob) { Fail 'NativeBoundary blob drift' }
& git -C $repo merge-base --is-ancestor $QualifiedCoordinatorHead HEAD
if ($LASTEXITCODE -ne 0) { Fail 'checkout does not descend from qualified coordinator head' }
& git -C $repo merge-base --is-ancestor $QualifiedPeerHead HEAD
if ($LASTEXITCODE -ne 0) { Fail 'checkout does not descend from qualified peer head' }
$repoHead = (& git -C $repo rev-parse HEAD).Trim()

$dotnetVersion = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $dotnetVersion.StartsWith('10.')) { Fail ".NET 10 SDK required; found '$dotnetVersion'" }

$proofAddresses = @(Get-NetIPAddress -AddressFamily IPv4 -IPAddress $ProofIp -ErrorAction SilentlyContinue | Where-Object PrefixLength -eq $PrefixLength)
if ($proofAddresses.Count -ne 1) { Fail "exact local address $ProofIp/$PrefixLength must exist once" }
if (@(Get-NetIPAddress -AddressFamily IPv4 -IPAddress $PeerIp -ErrorAction SilentlyContinue).Count -ne 0) { Fail "$PeerIp must not be assigned locally" }
$proofAddress = $proofAddresses[0]
$ifIndex = [int]$proofAddress.InterfaceIndex
$adapter = Get-NetAdapter -InterfaceIndex $ifIndex -ErrorAction Stop
if ($adapter.Status -ne 'Up') { Fail "test adapter is not Up: $($adapter.Name)" }
if (@(Get-NetRoute -AddressFamily IPv4 -InterfaceIndex $ifIndex -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue).Count -ne 0) { Fail 'test interface must not own an IPv4 default route' }
$dns = @(Get-DnsClientServerAddress -InterfaceIndex $ifIndex -AddressFamily IPv4 -ErrorAction Stop).ServerAddresses
if (@($dns | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count -ne 0) { Fail 'test interface must have no IPv4 DNS servers configured' }

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$parentElevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

$parent = Join-Path $env:TEMP ('windows-isolation-qualification-' + [Guid]::NewGuid().ToString('N'))
$trial = Join-Path $parent 'trial'
$buildOut = Join-Path $parent 'build'
New-Item -ItemType Directory -Path $trial -Force | Out-Null
New-Item -ItemType Directory -Path $buildOut -Force | Out-Null

& dotnet publish $project -c Release -p:DebugType=None -p:DebugSymbols=false -o $buildOut --nologo
if ($LASTEXITCODE -ne 0) { Fail "coordinator publish failed: $LASTEXITCODE" }

$expected = @(
    'Workbench.AppContainerProbe.exe',
    'Workbench.AppContainerProbe.dll',
    'Workbench.AppContainerProbe.runtimeconfig.json',
    'Workbench.AppContainerProbe.deps.json'
)
foreach ($name in $expected) {
    $from = Join-Path $buildOut $name
    if (-not (Test-Path -LiteralPath $from -PathType Leaf)) { Fail "publish output missing: $name" }
    Copy-Item -LiteralPath $from -Destination (Join-Path $trial $name) -Force
}
Set-Content -LiteralPath (Join-Path $trial 'allow-read.txt') -Value 'SYNTHETIC_ALLOWED' -NoNewline -Encoding utf8
Set-Content -LiteralPath (Join-Path $trial 'deny-read.txt') -Value 'SYNTHETIC_DENIED' -NoNewline -Encoding utf8
Set-Content -LiteralPath (Join-Path $trial 'deny-write.txt') -Value 'SYNTHETIC_UNCHANGED' -NoNewline -Encoding utf8

$trialNames = @(Get-ChildItem -LiteralPath $trial -Force | Select-Object -ExpandProperty Name | Sort-Object)
$requiredNames = @($expected + @('allow-read.txt','deny-read.txt','deny-write.txt') | Sort-Object)
if (($trialNames -join '|') -ne ($requiredNames -join '|')) { Fail 'trial directory is not closed to the exact seven files' }

$currentOwner = $identity.User.Value
foreach ($path in @($trial) + ($requiredNames | ForEach-Object { Join-Path $trial $_ })) {
    $owner = (Get-Acl -LiteralPath $path).Owner
    try { $ownerSid = ([Security.Principal.NTAccount]$owner).Translate([Security.Principal.SecurityIdentifier]).Value }
    catch { $ownerSid = $owner }
    if ($ownerSid -ne $currentOwner) { Fail "trial ownership mismatch: $path owner=$owner" }
}

$files = [ordered]@{}
foreach ($name in $expected) { $files[$name] = (Sha256 (Join-Path $trial $name)) }
$manifestPath = Join-Path $parent 'manifest.json'
[ordered]@{ files = $files } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8

$provisioningPath = Join-Path $parent 'windows-proof-provisioning.json'
$statePath = Join-Path $parent 'executor-state.json'
$evidencePath = Join-Path $parent 'proof-evidence.json'
$stdoutPath = Join-Path $parent 'coordinator-stdout.jsonl'
$coordinatorExe = Join-Path $buildOut 'Workbench.AppContainerProbe.exe'

$provision = [ordered]@{
    schema = 'matawaka.windows-private-peer-proof-provisioning/v0.1'
    createdUtc = [DateTimeOffset]::UtcNow.ToString('o')
    repoHead = $repoHead
    qualifiedCoordinatorHead = $QualifiedCoordinatorHead
    coordinatorSourceSha256 = $CoordinatorSourceSha256
    coordinatorProjectSha256 = $CoordinatorProjectSha256
    nativeBoundaryBlob = $NativeBoundaryBlob
    qualifiedPeerHead = $QualifiedPeerHead
    peerSourceSha256 = $PeerSourceSha256
    peerContractSha256 = $PeerContractSha256
    osProductName = $productName
    osBuild = $build
    osArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    dotnetSdkVersion = $dotnetVersion
    parentUserSid = $currentOwner
    parentElevated = $parentElevated
    proofNodeAddress = $ProofIp
    peerAddress = $PeerIp
    prefixLength = $PrefixLength
    interfaceIndex = $ifIndex
    interfaceAlias = $adapter.Name
    interfaceDescription = $adapter.InterfaceDescription
    interfaceMacAddress = $adapter.MacAddress
    interfaceStatus = $adapter.Status.ToString()
    interfaceHasDefaultRoute = $false
    interfaceHasIpv4Dns = $false
    managementPort = 41060
    targetPort = 41061
    networkCallsDuringPreparation = $false
    networkConfigurationMutationByProofTrial = $false
    proofClaimed = $false
}
$provision | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $provisioningPath -Encoding utf8

$state = [ordered]@{
    schema = 'matawaka.windows-private-peer-executor-state/v0.1'
    repositoryRoot = $repo
    repoHead = $repoHead
    qualifiedCoordinatorHead = $QualifiedCoordinatorHead
    coordinatorExe = $coordinatorExe
    trialRoot = $trial
    manifestPath = $manifestPath
    evidencePath = $evidencePath
    stdoutPath = $stdoutPath
    provisioningPath = $provisioningPath
    proofNodeAddress = $ProofIp
    peerAddress = $PeerIp
    managementPort = 41060
    targetPort = 41061
    trialExecuted = $false
    proofClaimed = $false
}
$state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath -Encoding utf8

Write-Host 'WINDOWS_PRIVATE_PEER_PROOF_NODE_PREPARED'
Write-Host "state=$statePath"
Write-Host "provisioning=$provisioningPath"
Write-Host "trial=$trial"
Write-Host "parentElevated=$parentElevated (recorded, not used as proof)"
Write-Host 'No peer network call was executed by this preparation script.'
