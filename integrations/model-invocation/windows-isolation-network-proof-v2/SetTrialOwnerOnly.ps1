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

$owner = [Security.Principal.WindowsIdentity]::GetCurrent().User
if ($null -eq $owner) { throw 'trial owner unavailable' }
$ownerSid = $owner.Value
$ownedPaths = @($Trial) + ($BundleNames | ForEach-Object { Join-Path $Trial $_ }) + @(
    (Join-Path $Trial 'allow-read.txt'),
    (Join-Path $Trial 'deny-read.txt'),
    (Join-Path $Trial 'deny-write.txt')
)

foreach ($ownedPath in $ownedPaths) {
    if (-not (Test-Path -LiteralPath $ownedPath)) { throw "trial owner target absent: path=$ownedPath" }
    $beforeAcl = Get-Acl -LiteralPath $ownedPath
    $beforeDacl = $beforeAcl.GetSecurityDescriptorSddlForm([Security.AccessControl.AccessControlSections]::Access)
    $beforeOwner = $beforeAcl.GetOwner([Security.Principal.SecurityIdentifier])
    if (-not $beforeOwner.Equals($owner)) {
        $rc = [MatawakaOwnerOnly]::SetOwner($ownedPath, $ownerSid)
        if ($rc -ne 0) { throw "owner-only SetNamedSecurityInfo failed: path=$ownedPath code=$rc" }
    }
    $afterAcl = Get-Acl -LiteralPath $ownedPath
    $afterOwner = $afterAcl.GetOwner([Security.Principal.SecurityIdentifier])
    $afterDacl = $afterAcl.GetSecurityDescriptorSddlForm([Security.AccessControl.AccessControlSections]::Access)
    if (-not $afterOwner.Equals($owner)) { throw "trial owner mismatch after owner-only normalization: path=$ownedPath owner=$afterOwner expected=$owner" }
    if ($afterDacl -ne $beforeDacl) { throw "trial DACL changed during owner-only normalization: path=$ownedPath" }
}

Write-Host "TRIAL_OWNER_ONLY_NORMALIZATION_PASS targets=$($ownedPaths.Count) owner=$ownerSid dacl_unchanged=true"
