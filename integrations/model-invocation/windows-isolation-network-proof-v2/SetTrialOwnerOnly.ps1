param(
    [Parameter(Mandatory = $true)][string]$Trial,
    [Parameter(Mandatory = $true)][string[]]$BundleNames
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class MatawakaOwnerOnly
{
    private const int SE_FILE_OBJECT = 1;
    private const uint OWNER_SECURITY_INFORMATION = 0x00000001;

    [DllImport("advapi32.dll", EntryPoint = "SetNamedSecurityInfoW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint SetNamedSecurityInfo(
        string objectName,
        int objectType,
        uint securityInfo,
        IntPtr ownerSid,
        IntPtr groupSid,
        IntPtr dacl,
        IntPtr sacl);

    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSidToSidW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSidToSid(string stringSid, out IntPtr sid);

    [DllImport("kernel32.dll", EntryPoint = "LocalFree", ExactSpelling = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static uint SetOwner(string path, string sidText)
    {
        if (!ConvertStringSidToSid(sidText, out var sid))
            return unchecked((uint)Marshal.GetLastWin32Error());
        try
        {
            return SetNamedSecurityInfo(
                path,
                SE_FILE_OBJECT,
                OWNER_SECURITY_INFORMATION,
                sid,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);
        }
        finally
        {
            _ = LocalFree(sid);
        }
    }
}
'@

function Get-RuleShape($acl) {
    @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) | ForEach-Object {
        [ordered]@{
            sid = $_.IdentityReference.Value
            rights = [int64]$_.FileSystemRights
            type = $_.AccessControlType.ToString()
            inherited = $_.IsInherited
            inheritance = [int]$_.InheritanceFlags
            propagation = [int]$_.PropagationFlags
        }
    })
}

$owner = [Security.Principal.WindowsIdentity]::GetCurrent().User
if ($null -eq $owner) { throw 'trial owner unavailable' }
$ownerSid = $owner.Value
$ownedPaths = @($Trial) + ($BundleNames | ForEach-Object { Join-Path $Trial $_ }) + @(
    (Join-Path $Trial 'allow-read.txt'),
    (Join-Path $Trial 'deny-read.txt'),
    (Join-Path $Trial 'deny-write.txt')
)
$icacls = Join-Path $env:SystemRoot 'System32\icacls.exe'

foreach ($ownedPath in $ownedPaths) {
    if (-not (Test-Path -LiteralPath $ownedPath)) { throw "trial security target absent: path=$ownedPath" }

    $beforeAcl = Get-Acl -LiteralPath $ownedPath
    $beforeOwner = $beforeAcl.GetOwner([Security.Principal.SecurityIdentifier])
    if (-not $beforeOwner.Equals($owner)) {
        $rc = [MatawakaOwnerOnly]::SetOwner($ownedPath, $ownerSid)
        if ($rc -ne 0) { throw "owner-only SetNamedSecurityInfo failed: path=$ownedPath code=$rc" }
    }

    # Windows hosted runners currently create RUNNER_TEMP children owned by BUILTIN\Administrators
    # with one explicit Administrators ACE. NativeBoundary's pinned contract requires a fresh
    # caller-owned object whose unrelated access rules are inherited only, so that its own
    # SetAccessRuleProtection(true,false) starts from zero explicit rules before adding exactly
    # owner + AppContainer. Reset only this GUID-scoped disposable object; never its parent.
    & $icacls $ownedPath /reset /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "disposable trial DACL reset failed: path=$ownedPath code=$LASTEXITCODE" }

    $afterAcl = Get-Acl -LiteralPath $ownedPath
    $afterOwner = $afterAcl.GetOwner([Security.Principal.SecurityIdentifier])
    $rules = @($afterAcl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
    $explicit = @($rules | Where-Object { -not $_.IsInherited })
    $deny = @($rules | Where-Object { $_.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow })

    if (-not $afterOwner.Equals($owner) -or $afterAcl.AreAccessRulesProtected -or $explicit.Count -ne 0 -or $deny.Count -ne 0 -or $rules.Count -eq 0) {
        [ordered]@{
            schema = 'matawaka.windows-disposable-trial-security-refusal/v0.1'
            path = $ownedPath
            expectedOwner = $ownerSid
            observedOwner = $afterOwner.Value
            protected = $afterAcl.AreAccessRulesProtected
            ruleCount = $rules.Count
            explicitRuleCount = $explicit.Count
            denyRuleCount = $deny.Count
            rules = Get-RuleShape $afterAcl
        } | ConvertTo-Json -Depth 8 -Compress | Write-Host
        throw "disposable trial security normalization refused: path=$ownedPath"
    }
}

Write-Host "TRIAL_DISPOSABLE_SECURITY_NORMALIZATION_PASS targets=$($ownedPaths.Count) owner=$ownerSid protected=false explicit_aces=0 deny_aces=0"
