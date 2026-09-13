param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [Parameter(Mandatory = $true)][string[]]$Arguments,
    [Parameter(Mandatory = $true)][string]$WorkingDirectory
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public sealed class MatawakaSaferLuaLaunchResult
{
    public bool SourceElevated { get; init; }
    public uint SourceIntegrityRid { get; init; }
    public bool SaferElevated { get; init; }
    public bool SaferHasRestrictions { get; init; }
    public uint SaferIntegrityRid { get; init; }
    public bool LuaElevated { get; init; }
    public bool LuaHasRestrictions { get; init; }
    public bool LuaHasRestrictingSids { get; init; }
    public int LuaElevationType { get; init; }
    public uint LuaIntegrityRid { get; init; }
    public uint SystemCanaryExitCode { get; init; }
    public uint ExitCode { get; init; }
}

public static class MatawakaSaferLuaLauncher
{
    private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint LUA_TOKEN = 0x00000004;
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
    private static extern bool IsTokenRestricted(IntPtr token);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthority);

    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SaferCreateLevel(uint scopeId, uint levelId, uint openFlags, out IntPtr levelHandle, IntPtr reserved);

    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SaferComputeTokenFromLevel(IntPtr levelHandle, IntPtr inputToken, out IntPtr outputToken, uint flags, IntPtr reserved);

    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SaferCloseLevel(IntPtr levelHandle);

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

    public static MatawakaSaferLuaLaunchResult Run(string executable, string[] args, string workingDirectory)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_QUERY, out var source))
            ThrowWin32("OPEN_SOURCE_TOKEN");
        IntPtr level = IntPtr.Zero;
        IntPtr safer = IntPtr.Zero;
        IntPtr lua = IntPtr.Zero;
        try
        {
            bool sourceElevated = QueryDword(source, TokenElevation) != 0;
            uint sourceIntegrity = QueryIntegrityRid(source);
            if (!sourceElevated) throw new InvalidOperationException("SOURCE_TOKEN_NOT_ELEVATED_EXPECTED_HOSTED_RUNNER");

            if (!SaferCreateLevel(SAFER_SCOPEID_USER, SAFER_LEVELID_NORMALUSER, SAFER_LEVEL_OPEN, out level, IntPtr.Zero))
                ThrowWin32("SAFER_CREATE_NORMALUSER_LEVEL");
            if (!SaferComputeTokenFromLevel(level, source, out safer, 0, IntPtr.Zero))
                ThrowWin32("SAFER_COMPUTE_NORMALUSER_TOKEN");
            if (safer == IntPtr.Zero) throw new InvalidOperationException("SAFER_TOKEN_ABSENT");

            bool saferElevated = QueryDword(safer, TokenElevation) != 0;
            bool saferRestrictions = QueryBoolean(safer, TokenHasRestrictions);
            uint saferIntegrity = QueryIntegrityRid(safer);
            if (!saferRestrictions) throw new InvalidOperationException("SAFER_RESTRICTIONS_ABSENT");

            if (!CreateRestrictedToken(safer, LUA_TOKEN, 0, IntPtr.Zero, 0, IntPtr.Zero, 0, IntPtr.Zero, out lua))
                ThrowWin32("CREATE_CHAINED_LUA_TOKEN");
            if (lua == IntPtr.Zero) throw new InvalidOperationException("CHAINED_LUA_TOKEN_ABSENT");

            bool luaElevated = QueryDword(lua, TokenElevation) != 0;
            bool luaRestrictions = QueryBoolean(lua, TokenHasRestrictions);
            bool luaRestrictingSids = IsTokenRestricted(lua);
            int luaElevationType = QueryDword(lua, TokenElevationType);
            uint luaIntegrity = QueryIntegrityRid(lua);
            if (luaElevated) throw new InvalidOperationException("CHAINED_LUA_TOKEN_STILL_ELEVATED");
            if (!luaRestrictions) throw new InvalidOperationException("CHAINED_LUA_RESTRICTIONS_ABSENT");

            string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? throw new InvalidOperationException("SYSTEM_ROOT_ABSENT");
            string canary = Path.Combine(systemRoot, "System32", "cmd.exe");
            uint canaryExit = RunProcess(lua, canary, new[] { "/d", "/c", "exit", "0" }, workingDirectory);
            if (canaryExit != 0) throw new InvalidOperationException($"SAFER_LUA_CANARY_NONZERO:{canaryExit}");

            uint exitCode = RunProcess(lua, executable, args, workingDirectory);
            return new MatawakaSaferLuaLaunchResult
            {
                SourceElevated = sourceElevated,
                SourceIntegrityRid = sourceIntegrity,
                SaferElevated = saferElevated,
                SaferHasRestrictions = saferRestrictions,
                SaferIntegrityRid = saferIntegrity,
                LuaElevated = luaElevated,
                LuaHasRestrictions = luaRestrictions,
                LuaHasRestrictingSids = luaRestrictingSids,
                LuaElevationType = luaElevationType,
                LuaIntegrityRid = luaIntegrity,
                SystemCanaryExitCode = canaryExit,
                ExitCode = exitCode
            };
        }
        finally
        {
            if (lua != IntPtr.Zero) CloseHandle(lua);
            if (safer != IntPtr.Zero) CloseHandle(safer);
            if (level != IntPtr.Zero && !SaferCloseLevel(level)) ThrowWin32("SAFER_CLOSE_LEVEL");
            if (source != IntPtr.Zero) CloseHandle(source);
        }
    }

    private static uint RunProcess(IntPtr token, string executable, string[] args, string workingDirectory)
    {
        var command = new StringBuilder();
        command.Append(Quote(executable));
        foreach (string arg in args)
        {
            command.Append(' ');
            command.Append(Quote(arg));
        }
        var startup = new STARTUPINFO { cb = checked((uint)Marshal.SizeOf<STARTUPINFO>()), lpDesktop = null };
        if (!CreateProcessWithToken(token, 0, executable, command, CREATE_NO_WINDOW, IntPtr.Zero, workingDirectory, ref startup, out var pi))
            ThrowWin32("CREATE_PROCESS_WITH_SAFER_LUA_TOKEN");
        try
        {
            uint wait = WaitForSingleObject(pi.hProcess, INFINITE);
            if (wait != WAIT_OBJECT_0) throw new Win32Exception(Marshal.GetLastWin32Error(), $"WAIT_SAFER_LUA_PROCESS:{wait}");
            if (!GetExitCodeProcess(pi.hProcess, out uint exitCode)) ThrowWin32("GET_SAFER_LUA_PROCESS_EXIT_CODE");
            return exitCode;
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
            if (!GetTokenInformation(token, informationClass, buffer, sizeof(int), out uint returned)) ThrowWin32($"QUERY_TOKEN_BOOLEAN_{informationClass}");
            if (returned != 1 && returned != sizeof(int)) throw new InvalidOperationException($"TOKEN_BOOLEAN_SIZE_{informationClass}:{returned}");
            return Marshal.ReadByte(buffer) != 0;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static int QueryDword(IntPtr token, int informationClass)
    {
        IntPtr buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            if (!GetTokenInformation(token, informationClass, buffer, sizeof(int), out uint returned)) ThrowWin32($"QUERY_TOKEN_DWORD_{informationClass}");
            if (returned != sizeof(int)) throw new InvalidOperationException($"TOKEN_DWORD_SIZE_{informationClass}:{returned}");
            return Marshal.ReadInt32(buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static uint QueryIntegrityRid(IntPtr token)
    {
        GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out uint required);
        int firstError = Marshal.GetLastWin32Error();
        if (required == 0 || firstError != ERROR_INSUFFICIENT_BUFFER) throw new Win32Exception(firstError, $"TOKEN_INTEGRITY_SIZE:{required}");
        IntPtr buffer = Marshal.AllocHGlobal(checked((int)required));
        try
        {
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, required, out uint returned)) ThrowWin32("QUERY_TOKEN_INTEGRITY");
            if (returned > required) throw new InvalidOperationException($"TOKEN_INTEGRITY_GROWTH:{returned}>{required}");
            var label = Marshal.PtrToStructure<TOKEN_MANDATORY_LABEL>(buffer);
            if (label.Label.Sid == IntPtr.Zero) throw new InvalidOperationException("TOKEN_INTEGRITY_SID_ABSENT");
            IntPtr countPtr = GetSidSubAuthorityCount(label.Label.Sid);
            if (countPtr == IntPtr.Zero) throw new InvalidOperationException("TOKEN_INTEGRITY_COUNT_ABSENT");
            byte count = Marshal.ReadByte(countPtr);
            if (count == 0) throw new InvalidOperationException("TOKEN_INTEGRITY_COUNT_ZERO");
            IntPtr ridPtr = GetSidSubAuthority(label.Label.Sid, (uint)(count - 1));
            if (ridPtr == IntPtr.Zero) throw new InvalidOperationException("TOKEN_INTEGRITY_RID_ABSENT");
            return unchecked((uint)Marshal.ReadInt32(ridPtr));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string Quote(string value)
    {
        if (value.IndexOf('\0') >= 0) throw new ArgumentException("NUL_IN_ARGUMENT");
        if (value.Length == 0) return "\"\"";
        bool needsQuotes = value.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) >= 0;
        if (!needsQuotes) return value;
        var b = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') { b.Append('\\', slashes * 2 + 1); b.Append('"'); slashes = 0; continue; }
            if (slashes != 0) { b.Append('\\', slashes); slashes = 0; }
            b.Append(c);
        }
        if (slashes != 0) b.Append('\\', slashes * 2);
        b.Append('"');
        return b.ToString();
    }

    private static void ThrowWin32(string stage) => throw new Win32Exception(Marshal.GetLastWin32Error(), stage);
}
'@

$exeFull = [IO.Path]::GetFullPath($Executable)
$workingFull = [IO.Path]::GetFullPath($WorkingDirectory)
if (-not (Test-Path -LiteralPath $exeFull -PathType Leaf)) { throw "SAFER-LUA executable absent: $exeFull" }
if (-not (Test-Path -LiteralPath $workingFull -PathType Container)) { throw "SAFER-LUA working directory absent: $workingFull" }

$result = [MatawakaSaferLuaLauncher]::Run($exeFull, $Arguments, $workingFull)
$signedExitCode = [BitConverter]::ToInt32([BitConverter]::GetBytes([uint32]$result.ExitCode), 0)
[ordered]@{
    schema = 'matawaka.windows-safer-lua-proof-launch/v0.1'
    launchApi = 'CreateProcessWithTokenW'
    saferLevel = 'SAFER_LEVELID_NORMALUSER'
    sourceElevated = $result.SourceElevated
    sourceIntegrityRid = [uint32]$result.SourceIntegrityRid
    saferElevated = $result.SaferElevated
    saferHasRestrictions = $result.SaferHasRestrictions
    saferIntegrityRid = [uint32]$result.SaferIntegrityRid
    luaElevated = $result.LuaElevated
    luaHasRestrictions = $result.LuaHasRestrictions
    luaHasRestrictingSids = $result.LuaHasRestrictingSids
    luaElevationType = $result.LuaElevationType
    luaIntegrityRid = [uint32]$result.LuaIntegrityRid
    systemCanaryExitCode = [uint32]$result.SystemCanaryExitCode
    childExitCodeUnsigned = [uint32]$result.ExitCode
    childExitCodeSigned = $signedExitCode
    credentialUsed = $false
    userChanged = $false
    saferPolicyMutated = $false
    firewallRuleMutated = $false
    networkIsolationConfigMutated = $false
    globalPolicyMutated = $false
    productionProviderRegistered = $false
} | ConvertTo-Json -Compress | Write-Host

Write-Output $signedExitCode
