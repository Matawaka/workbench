param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [Parameter(Mandatory = $true)][string[]]$Arguments,
    [Parameter(Mandatory = $true)][string]$WorkingDirectory
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public sealed class MatawakaLuaLaunchResult
{
    public bool SourceElevated { get; init; }
    public bool LuaElevated { get; init; }
    public bool LuaRestricted { get; init; }
    public uint ExitCode { get; init; }
}

public static class MatawakaLuaLauncher
{
    private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint LUA_TOKEN = 0x00000004;
    private const int TokenElevation = 20;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint WAIT_OBJECT_0 = 0x00000000;
    private const uint INFINITE = 0xFFFFFFFF;

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
    private static extern bool CreateRestrictedToken(
        IntPtr existingToken,
        uint flags,
        uint disableSidCount,
        IntPtr sidsToDisable,
        uint deletePrivilegeCount,
        IntPtr privilegesToDelete,
        uint restrictedSidCount,
        IntPtr sidsToRestrict,
        out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr token,
        int tokenInformationClass,
        IntPtr tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsTokenRestricted(IntPtr token);

    [DllImport("advapi32.dll", EntryPoint = "CreateProcessAsUserW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessAsUser(
        IntPtr token,
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref STARTUPINFO startupInfo,
        out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    public static MatawakaLuaLaunchResult Run(string executable, string[] args, string workingDirectory)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_QUERY, out var source))
            ThrowWin32("OPEN_SOURCE_TOKEN");
        IntPtr lua = IntPtr.Zero;
        try
        {
            bool sourceElevated = IsElevated(source);
            if (!sourceElevated)
                throw new InvalidOperationException("SOURCE_TOKEN_NOT_ELEVATED_EXPECTED_HOSTED_RUNNER");

            if (!CreateRestrictedToken(source, LUA_TOKEN, 0, IntPtr.Zero, 0, IntPtr.Zero, 0, IntPtr.Zero, out lua))
                ThrowWin32("CREATE_LUA_TOKEN");

            bool luaElevated = IsElevated(lua);
            bool luaRestricted = IsTokenRestricted(lua);
            if (luaElevated)
                throw new InvalidOperationException("LUA_TOKEN_STILL_ELEVATED");
            if (!luaRestricted)
                throw new InvalidOperationException("LUA_TOKEN_NOT_RESTRICTED");

            var command = new StringBuilder();
            command.Append(Quote(executable));
            foreach (var arg in args)
            {
                command.Append(' ');
                command.Append(Quote(arg));
            }

            var startup = new STARTUPINFO { cb = checked((uint)Marshal.SizeOf<STARTUPINFO>()) };
            if (!CreateProcessAsUser(lua, executable, command, IntPtr.Zero, IntPtr.Zero, false, CREATE_NO_WINDOW,
                IntPtr.Zero, workingDirectory, ref startup, out var pi))
                ThrowWin32("CREATE_PROCESS_AS_LUA");

            try
            {
                uint wait = WaitForSingleObject(pi.hProcess, INFINITE);
                if (wait != WAIT_OBJECT_0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"WAIT_LUA_PROCESS:{wait}");
                if (!GetExitCodeProcess(pi.hProcess, out uint exitCode))
                    ThrowWin32("GET_LUA_PROCESS_EXIT_CODE");

                return new MatawakaLuaLaunchResult
                {
                    SourceElevated = sourceElevated,
                    LuaElevated = luaElevated,
                    LuaRestricted = luaRestricted,
                    ExitCode = exitCode
                };
            }
            finally
            {
                if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
                if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
            }
        }
        finally
        {
            if (lua != IntPtr.Zero) CloseHandle(lua);
            if (source != IntPtr.Zero) CloseHandle(source);
        }
    }

    private static bool IsElevated(IntPtr token)
    {
        IntPtr buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            if (!GetTokenInformation(token, TokenElevation, buffer, sizeof(int), out uint returned))
                ThrowWin32("QUERY_TOKEN_ELEVATION");
            if (returned != sizeof(int))
                throw new InvalidOperationException($"TOKEN_ELEVATION_SIZE:{returned}");
            return Marshal.ReadInt32(buffer) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string Quote(string value)
    {
        if (value.IndexOf('\0') >= 0)
            throw new ArgumentException("NUL_IN_ARGUMENT");
        if (value.Length == 0)
            return "\"\"";
        bool needsQuotes = value.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) >= 0;
        if (!needsQuotes)
            return value;

        var b = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }
            if (c == '"')
            {
                b.Append('\\', slashes * 2 + 1);
                b.Append('"');
                slashes = 0;
                continue;
            }
            if (slashes != 0)
            {
                b.Append('\\', slashes);
                slashes = 0;
            }
            b.Append(c);
        }
        if (slashes != 0)
            b.Append('\\', slashes * 2);
        b.Append('"');
        return b.ToString();
    }

    private static void ThrowWin32(string stage)
    {
        throw new Win32Exception(Marshal.GetLastWin32Error(), stage);
    }
}
'@

$exeFull = [IO.Path]::GetFullPath($Executable)
$workingFull = [IO.Path]::GetFullPath($WorkingDirectory)
if (-not (Test-Path -LiteralPath $exeFull -PathType Leaf)) { throw "LUA executable absent: $exeFull" }
if (-not (Test-Path -LiteralPath $workingFull -PathType Container)) { throw "LUA working directory absent: $workingFull" }

$result = [MatawakaLuaLauncher]::Run($exeFull, $Arguments, $workingFull)
[ordered]@{
    schema = 'matawaka.windows-lua-parent-launch/v0.1'
    sourceElevated = $result.SourceElevated
    luaElevated = $result.LuaElevated
    luaRestricted = $result.LuaRestricted
    childExitCode = $result.ExitCode
    credentialUsed = $false
    userChanged = $false
    globalPolicyMutated = $false
} | ConvertTo-Json -Compress | Write-Host

Write-Output ([int]$result.ExitCode)
