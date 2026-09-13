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

public sealed class MatawakaWithTokenObservation
{
    public bool SourceElevated { get; init; }
    public int SourceElevationType { get; init; }
    public uint SourceIntegrityRid { get; init; }
    public bool LuaElevated { get; init; }
    public bool LuaHasRestrictions { get; init; }
    public uint LuaIntegrityBeforeRid { get; init; }
    public uint LuaIntegrityAfterRid { get; init; }
    public bool CreateAttempted { get; init; }
    public bool ProcessCreated { get; init; }
    public int? CreateWin32Error { get; init; }
    public uint? CanaryExitCode { get; init; }
}

public static class MatawakaWithTokenProbe
{
    private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_ADJUST_DEFAULT = 0x0080;
    private const uint LUA_TOKEN = 0x00000004;
    private const int TokenElevationType = 18;
    private const int TokenElevation = 20;
    private const int TokenHasRestrictions = 21;
    private const int TokenIntegrityLevel = 25;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const uint SE_GROUP_INTEGRITY = 0x00000020;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint WAIT_OBJECT_0 = 0;
    private const uint INFINITE = 0xFFFFFFFF;
    private const string MediumIntegritySid = "S-1-16-8192";

    [StructLayout(LayoutKind.Sequential)]
    private struct SID_AND_ATTRIBUTES
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_MANDATORY_LABEL
    {
        public SID_AND_ATTRIBUTES Label;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public uint cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateRestrictedToken(IntPtr existingToken, uint flags, uint disableSidCount, IntPtr sidsToDisable,
        uint deletePrivilegeCount, IntPtr privilegesToDelete, uint restrictedSidCount, IntPtr sidsToRestrict, out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr information, uint informationLength, out uint returnLength);

    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetTokenInformation(IntPtr token, int informationClass, ref TOKEN_MANDATORY_LABEL information, uint informationLength);

    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSidToSidW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSidToSid(string stringSid, out IntPtr sid);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthority);

    [DllImport("advapi32.dll", EntryPoint = "CreateProcessWithTokenW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessWithToken(IntPtr token, uint logonFlags, string applicationName, StringBuilder commandLine,
        uint creationFlags, IntPtr environment, string currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static MatawakaWithTokenObservation Run(string workingDirectory)
    {
        uint sourceAccess = TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ADJUST_DEFAULT;
        if (!OpenProcessToken(GetCurrentProcess(), sourceAccess, out var source))
            ThrowWin32("OPEN_SOURCE_TOKEN");
        IntPtr lua = IntPtr.Zero;
        IntPtr mediumSid = IntPtr.Zero;
        try
        {
            bool sourceElevated = QueryDword(source, TokenElevation) != 0;
            int sourceElevationType = QueryDword(source, TokenElevationType);
            uint sourceIntegrity = QueryIntegrityRid(source);

            if (!CreateRestrictedToken(source, LUA_TOKEN, 0, IntPtr.Zero, 0, IntPtr.Zero, 0, IntPtr.Zero, out lua))
                ThrowWin32("CREATE_LUA_TOKEN");

            bool luaElevated = QueryDword(lua, TokenElevation) != 0;
            bool luaHasRestrictions = QueryBoolean(lua, TokenHasRestrictions);
            uint before = QueryIntegrityRid(lua);

            if (!ConvertStringSidToSid(MediumIntegritySid, out mediumSid))
                ThrowWin32("ALLOCATE_MEDIUM_INTEGRITY_SID");
            var label = new TOKEN_MANDATORY_LABEL
            {
                Label = new SID_AND_ATTRIBUTES { Sid = mediumSid, Attributes = SE_GROUP_INTEGRITY }
            };
            if (!SetTokenInformation(lua, TokenIntegrityLevel, ref label, checked((uint)Marshal.SizeOf<TOKEN_MANDATORY_LABEL>())))
                ThrowWin32("SET_LUA_MEDIUM_INTEGRITY");

            uint after = QueryIntegrityRid(lua);
            if (after != 0x2000) throw new InvalidOperationException($"MEDIUM_INTEGRITY_NOT_APPLIED:{after}");
            if (luaElevated) throw new InvalidOperationException("LUA_TOKEN_STILL_ELEVATED");
            if (!luaHasRestrictions) throw new InvalidOperationException("LUA_TOKEN_FILTERING_NOT_RECORDED");

            string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? throw new InvalidOperationException("SYSTEM_ROOT_ABSENT");
            string canary = Path.Combine(systemRoot, "System32", "cmd.exe");
            var launch = TryCanary(lua, canary, workingDirectory);

            return new MatawakaWithTokenObservation
            {
                SourceElevated = sourceElevated,
                SourceElevationType = sourceElevationType,
                SourceIntegrityRid = sourceIntegrity,
                LuaElevated = luaElevated,
                LuaHasRestrictions = luaHasRestrictions,
                LuaIntegrityBeforeRid = before,
                LuaIntegrityAfterRid = after,
                CreateAttempted = true,
                ProcessCreated = launch.Created,
                CreateWin32Error = launch.Error,
                CanaryExitCode = launch.ExitCode
            };
        }
        finally
        {
            if (mediumSid != IntPtr.Zero) LocalFree(mediumSid);
            if (lua != IntPtr.Zero) CloseHandle(lua);
            if (source != IntPtr.Zero) CloseHandle(source);
        }
    }

    private static (bool Created, int? Error, uint? ExitCode) TryCanary(IntPtr token, string executable, string workingDirectory)
    {
        var command = new StringBuilder();
        command.Append('"').Append(executable).Append('"').Append(" /d /c exit 0");
        var startup = new STARTUPINFO { cb = checked((uint)Marshal.SizeOf<STARTUPINFO>()), lpDesktop = null };
        if (!CreateProcessWithToken(token, 0, executable, command, CREATE_NO_WINDOW, IntPtr.Zero, workingDirectory, ref startup, out var pi))
            return (false, Marshal.GetLastWin32Error(), null);
        try
        {
            uint wait = WaitForSingleObject(pi.hProcess, INFINITE);
            if (wait != WAIT_OBJECT_0) throw new Win32Exception(Marshal.GetLastWin32Error(), $"WAIT_WITH_TOKEN_CANARY:{wait}");
            if (!GetExitCodeProcess(pi.hProcess, out uint exitCode)) ThrowWin32("GET_WITH_TOKEN_CANARY_EXIT");
            return (true, null, exitCode);
        }
        finally
        {
            if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
            if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
        }
    }

    private static bool QueryBoolean(IntPtr token, int informationClass)
    {
        IntPtr buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(buffer, 0);
            if (!GetTokenInformation(token, informationClass, buffer, sizeof(int), out uint returned)) ThrowWin32($"QUERY_BOOLEAN_{informationClass}");
            if (returned != 1 && returned != sizeof(int)) throw new InvalidOperationException($"BOOLEAN_SIZE_{informationClass}:{returned}");
            return Marshal.ReadByte(buffer) != 0;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static int QueryDword(IntPtr token, int informationClass)
    {
        IntPtr buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            if (!GetTokenInformation(token, informationClass, buffer, sizeof(int), out uint returned)) ThrowWin32($"QUERY_DWORD_{informationClass}");
            if (returned != sizeof(int)) throw new InvalidOperationException($"DWORD_SIZE_{informationClass}:{returned}");
            return Marshal.ReadInt32(buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static uint QueryIntegrityRid(IntPtr token)
    {
        GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out uint required);
        int first = Marshal.GetLastWin32Error();
        if (required == 0 || first != ERROR_INSUFFICIENT_BUFFER) throw new Win32Exception(first, $"INTEGRITY_SIZE:{required}");
        IntPtr buffer = Marshal.AllocHGlobal(checked((int)required));
        try
        {
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, required, out uint returned)) ThrowWin32("QUERY_INTEGRITY");
            var label = Marshal.PtrToStructure<TOKEN_MANDATORY_LABEL>(buffer);
            if (label.Label.Sid == IntPtr.Zero) throw new InvalidOperationException("INTEGRITY_SID_ABSENT");
            IntPtr countPtr = GetSidSubAuthorityCount(label.Label.Sid);
            if (countPtr == IntPtr.Zero) throw new InvalidOperationException("INTEGRITY_COUNT_ABSENT");
            byte count = Marshal.ReadByte(countPtr);
            if (count == 0) throw new InvalidOperationException("INTEGRITY_COUNT_ZERO");
            IntPtr ridPtr = GetSidSubAuthority(label.Label.Sid, (uint)(count - 1));
            if (ridPtr == IntPtr.Zero) throw new InvalidOperationException("INTEGRITY_RID_ABSENT");
            return unchecked((uint)Marshal.ReadInt32(ridPtr));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static void ThrowWin32(string stage) => throw new Win32Exception(Marshal.GetLastWin32Error(), stage);
}
'@

$full = [IO.Path]::GetFullPath($Receipt)
if (Test-Path -LiteralPath $full) { throw "create-only receipt required: $full" }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $full) | Out-Null
$obs = [MatawakaWithTokenProbe]::Run((Get-Location).Path)
$status = if (-not $obs.ProcessCreated) {
    'WITH_TOKEN_API_CALL_FAILED'
} elseif ($obs.CanaryExitCode -eq 0) {
    'WITH_TOKEN_CANARY_PASS'
} else {
    'WITH_TOKEN_CANARY_FAIL_CLOSED'
}
$record = [ordered]@{
    schema = 'matawaka.windows-medium-lua-with-token-diagnostic/v0.1'
    status = $status
    sourceElevated = $obs.SourceElevated
    sourceElevationType = $obs.SourceElevationType
    sourceIntegrityRid = [uint32]$obs.SourceIntegrityRid
    luaElevated = $obs.LuaElevated
    luaHasRestrictions = $obs.LuaHasRestrictions
    luaIntegrityBeforeRid = [uint32]$obs.LuaIntegrityBeforeRid
    luaIntegrityAfterRid = [uint32]$obs.LuaIntegrityAfterRid
    createAttempted = $obs.CreateAttempted
    processCreated = $obs.ProcessCreated
    createWin32Error = $obs.CreateWin32Error
    canaryExitCodeUnsigned = $obs.CanaryExitCode
    credentialUsed = $false
    userChanged = $false
    firewallRuleMutated = $false
    networkIsolationConfigMutated = $false
    globalPolicyMutated = $false
    productionProviderRegistered = $false
    proofClaimed = $false
}
$record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $full -Encoding utf8NoBOM
$record | ConvertTo-Json -Compress | Write-Host
if ($status -ne 'WITH_TOKEN_CANARY_PASS') { exit 3 }
exit 0
