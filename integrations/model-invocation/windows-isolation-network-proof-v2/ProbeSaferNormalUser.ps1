param(
    [Parameter(Mandatory = $true)][string]$Receipt
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public sealed class MatawakaSaferObservation
{
    public bool SourceElevated { get; init; }
    public int SourceElevationType { get; init; }
    public uint SourceIntegrityRid { get; init; }
    public bool SaferTokenCreated { get; init; }
    public bool SaferElevated { get; init; }
    public int SaferElevationType { get; init; }
    public bool SaferHasRestrictions { get; init; }
    public bool SaferHasRestrictingSids { get; init; }
    public uint SaferIntegrityRid { get; init; }
    public bool AsUserCreated { get; init; }
    public int? AsUserCreateError { get; init; }
    public uint? AsUserExitCode { get; init; }
    public bool WithTokenCreated { get; init; }
    public int? WithTokenCreateError { get; init; }
    public uint? WithTokenExitCode { get; init; }
}

public static class MatawakaSaferProbe
{
    private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SAFER_SCOPEID_USER = 2;
    private const uint SAFER_LEVELID_NORMALUSER = 0x00020000;
    private const uint SAFER_LEVEL_OPEN = 1;
    private const int TokenElevationType = 18;
    private const int TokenElevation = 20;
    private const int TokenHasRestrictions = 21;
    private const int TokenIntegrityLevel = 25;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint WAIT_OBJECT_0 = 0;
    private const uint INFINITE = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential)]
    private struct SID_AND_ATTRIBUTES { public IntPtr Sid; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_MANDATORY_LABEL { public SID_AND_ATTRIBUTES Label; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public uint cb; public string? lpReserved; public string? lpDesktop; public string? lpTitle;
        public uint dwX; public uint dwY; public uint dwXSize; public uint dwYSize; public uint dwXCountChars; public uint dwYCountChars;
        public uint dwFillAttribute; public uint dwFlags; public ushort wShowWindow; public ushort cbReserved2;
        public IntPtr lpReserved2; public IntPtr hStdInput; public IntPtr hStdOutput; public IntPtr hStdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION { public IntPtr hProcess; public IntPtr hThread; public uint dwProcessId; public uint dwThreadId; }

    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, uint length, out uint returned);
    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsTokenRestricted(IntPtr token);
    [DllImport("advapi32.dll", ExactSpelling = true)] private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("advapi32.dll", ExactSpelling = true)] private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthority);

    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SaferCreateLevel(uint scopeId, uint levelId, uint openFlags, out IntPtr levelHandle, IntPtr reserved);
    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SaferComputeTokenFromLevel(IntPtr levelHandle, IntPtr inAccessToken, out IntPtr outAccessToken, uint flags, IntPtr reserved);
    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SaferCloseLevel(IntPtr levelHandle);

    [DllImport("advapi32.dll", EntryPoint = "CreateProcessAsUserW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcessAsUser(IntPtr token, string app, StringBuilder command,
        IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags,
        IntPtr environment, string currentDirectory, ref STARTUPINFO startup, out PROCESS_INFORMATION processInfo);

    [DllImport("advapi32.dll", EntryPoint = "CreateProcessWithTokenW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcessWithToken(IntPtr token, uint logonFlags, string app, StringBuilder command,
        uint flags, IntPtr environment, string currentDirectory, ref STARTUPINFO startup, out PROCESS_INFORMATION processInfo);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);

    public static MatawakaSaferObservation Run(string workingDirectory)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_QUERY, out var source))
            ThrowWin32("OPEN_SOURCE_TOKEN");
        IntPtr level = IntPtr.Zero;
        IntPtr safer = IntPtr.Zero;
        try
        {
            bool sourceElevated = QueryDword(source, TokenElevation) != 0;
            int sourceType = QueryDword(source, TokenElevationType);
            uint sourceIl = QueryIntegrityRid(source);

            if (!SaferCreateLevel(SAFER_SCOPEID_USER, SAFER_LEVELID_NORMALUSER, SAFER_LEVEL_OPEN, out level, IntPtr.Zero))
                ThrowWin32("SAFER_CREATE_NORMALUSER_LEVEL");
            if (!SaferComputeTokenFromLevel(level, source, out safer, 0, IntPtr.Zero))
                ThrowWin32("SAFER_COMPUTE_NORMALUSER_TOKEN");
            if (safer == IntPtr.Zero) throw new InvalidOperationException("SAFER_TOKEN_ABSENT");

            bool saferElevated = QueryDword(safer, TokenElevation) != 0;
            int saferType = QueryDword(safer, TokenElevationType);
            bool saferHasRestrictions = QueryBoolean(safer, TokenHasRestrictions);
            bool saferHasRestrictingSids = IsTokenRestricted(safer);
            uint saferIl = QueryIntegrityRid(safer);

            string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? throw new InvalidOperationException("SYSTEM_ROOT_ABSENT");
            string canary = Path.Combine(systemRoot, "System32", "cmd.exe");
            var asUser = TryAsUser(safer, canary, workingDirectory);
            var withToken = TryWithToken(safer, canary, workingDirectory);

            return new MatawakaSaferObservation
            {
                SourceElevated = sourceElevated,
                SourceElevationType = sourceType,
                SourceIntegrityRid = sourceIl,
                SaferTokenCreated = true,
                SaferElevated = saferElevated,
                SaferElevationType = saferType,
                SaferHasRestrictions = saferHasRestrictions,
                SaferHasRestrictingSids = saferHasRestrictingSids,
                SaferIntegrityRid = saferIl,
                AsUserCreated = asUser.Created,
                AsUserCreateError = asUser.Error,
                AsUserExitCode = asUser.ExitCode,
                WithTokenCreated = withToken.Created,
                WithTokenCreateError = withToken.Error,
                WithTokenExitCode = withToken.ExitCode
            };
        }
        finally
        {
            if (safer != IntPtr.Zero) CloseHandle(safer);
            if (level != IntPtr.Zero && !SaferCloseLevel(level)) ThrowWin32("SAFER_CLOSE_LEVEL");
            if (source != IntPtr.Zero) CloseHandle(source);
        }
    }

    private static (bool Created, int? Error, uint? ExitCode) TryAsUser(IntPtr token, string exe, string cwd)
    {
        var cmd = Command(exe); var si = new STARTUPINFO { cb = checked((uint)Marshal.SizeOf<STARTUPINFO>()), lpDesktop = null };
        if (!CreateProcessAsUser(token, exe, cmd, IntPtr.Zero, IntPtr.Zero, false, CREATE_NO_WINDOW, IntPtr.Zero, cwd, ref si, out var pi))
            return (false, Marshal.GetLastWin32Error(), null);
        return Complete(pi, "AS_USER");
    }

    private static (bool Created, int? Error, uint? ExitCode) TryWithToken(IntPtr token, string exe, string cwd)
    {
        var cmd = Command(exe); var si = new STARTUPINFO { cb = checked((uint)Marshal.SizeOf<STARTUPINFO>()), lpDesktop = null };
        if (!CreateProcessWithToken(token, 0, exe, cmd, CREATE_NO_WINDOW, IntPtr.Zero, cwd, ref si, out var pi))
            return (false, Marshal.GetLastWin32Error(), null);
        return Complete(pi, "WITH_TOKEN");
    }

    private static StringBuilder Command(string exe) => new StringBuilder().Append('"').Append(exe).Append('"').Append(" /d /c exit 0");

    private static (bool Created, int? Error, uint? ExitCode) Complete(PROCESS_INFORMATION pi, string stage)
    {
        try
        {
            uint wait = WaitForSingleObject(pi.hProcess, INFINITE);
            if (wait != WAIT_OBJECT_0) throw new Win32Exception(Marshal.GetLastWin32Error(), $"WAIT_{stage}:{wait}");
            if (!GetExitCodeProcess(pi.hProcess, out uint exitCode)) ThrowWin32("EXIT_" + stage);
            return (true, null, exitCode);
        }
        finally
        {
            if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
            if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
        }
    }

    private static bool QueryBoolean(IntPtr token, int cls)
    {
        IntPtr b = Marshal.AllocHGlobal(sizeof(int));
        try { Marshal.WriteInt32(b, 0); if (!GetTokenInformation(token, cls, b, sizeof(int), out uint r)) ThrowWin32("BOOL_"+cls); if (r != 1 && r != 4) throw new InvalidOperationException($"BOOL_SIZE_{cls}:{r}"); return Marshal.ReadByte(b) != 0; }
        finally { Marshal.FreeHGlobal(b); }
    }
    private static int QueryDword(IntPtr token, int cls)
    {
        IntPtr b=Marshal.AllocHGlobal(4); try { if(!GetTokenInformation(token,cls,b,4,out uint r)) ThrowWin32("DWORD_"+cls); if(r!=4) throw new InvalidOperationException($"DWORD_SIZE_{cls}:{r}"); return Marshal.ReadInt32(b); } finally { Marshal.FreeHGlobal(b); }
    }
    private static uint QueryIntegrityRid(IntPtr token)
    {
        GetTokenInformation(token,TokenIntegrityLevel,IntPtr.Zero,0,out uint n); int e=Marshal.GetLastWin32Error(); if(n==0||e!=ERROR_INSUFFICIENT_BUFFER) throw new Win32Exception(e,$"IL_SIZE:{n}");
        IntPtr b=Marshal.AllocHGlobal(checked((int)n)); try { if(!GetTokenInformation(token,TokenIntegrityLevel,b,n,out _)) ThrowWin32("IL_QUERY"); var l=Marshal.PtrToStructure<TOKEN_MANDATORY_LABEL>(b); if(l.Label.Sid==IntPtr.Zero) throw new InvalidOperationException("IL_SID"); IntPtr cp=GetSidSubAuthorityCount(l.Label.Sid); if(cp==IntPtr.Zero) throw new InvalidOperationException("IL_COUNT"); byte c=Marshal.ReadByte(cp); if(c==0) throw new InvalidOperationException("IL_EMPTY"); IntPtr rp=GetSidSubAuthority(l.Label.Sid,(uint)(c-1)); if(rp==IntPtr.Zero) throw new InvalidOperationException("IL_RID"); return unchecked((uint)Marshal.ReadInt32(rp)); } finally { Marshal.FreeHGlobal(b); }
    }
    private static void ThrowWin32(string s) => throw new Win32Exception(Marshal.GetLastWin32Error(), s);
}
'@

$full=[IO.Path]::GetFullPath($Receipt)
if(Test-Path -LiteralPath $full){throw "create-only receipt required: $full"}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $full)|Out-Null
$obs=[MatawakaSaferProbe]::Run((Get-Location).Path)
$viable = ($obs.AsUserCreated -and $obs.AsUserExitCode -eq 0) -or ($obs.WithTokenCreated -and $obs.WithTokenExitCode -eq 0)
$status = if($viable){'SAFER_NORMALUSER_LAUNCHABLE'}else{'SAFER_NORMALUSER_NOT_LAUNCHABLE'}
$record=[ordered]@{
 schema='matawaka.windows-safer-normaluser-diagnostic/v0.1';status=$status
 sourceElevated=$obs.SourceElevated;sourceElevationType=$obs.SourceElevationType;sourceIntegrityRid=[uint32]$obs.SourceIntegrityRid
 saferTokenCreated=$obs.SaferTokenCreated;saferElevated=$obs.SaferElevated;saferElevationType=$obs.SaferElevationType;saferHasRestrictions=$obs.SaferHasRestrictions;saferHasRestrictingSids=$obs.SaferHasRestrictingSids;saferIntegrityRid=[uint32]$obs.SaferIntegrityRid
 asUserCreated=$obs.AsUserCreated;asUserCreateError=$obs.AsUserCreateError;asUserExitCode=$obs.AsUserExitCode
 withTokenCreated=$obs.WithTokenCreated;withTokenCreateError=$obs.WithTokenCreateError;withTokenExitCode=$obs.WithTokenExitCode
 credentialUsed=$false;userChanged=$false;saferPolicyMutated=$false;firewallRuleMutated=$false;networkIsolationConfigMutated=$false;globalPolicyMutated=$false;productionProviderRegistered=$false;proofClaimed=$false
}
$record|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $full -Encoding utf8NoBOM
$record|ConvertTo-Json -Compress|Write-Host
if(-not $viable){exit 3}
exit 0
