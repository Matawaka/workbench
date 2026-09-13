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
using System.Security.Principal;
using System.Text;

public sealed class MatawakaLinkedTokenObservation
{
    public string UserSid { get; init; } = "";
    public uint UserRid { get; init; }
    public bool SourceElevated { get; init; }
    public int SourceElevationType { get; init; }
    public uint SourceIntegrityRid { get; init; }
    public bool LinkedTokenPresent { get; init; }
    public bool? LinkedElevated { get; init; }
    public int? LinkedElevationType { get; init; }
    public uint? LinkedIntegrityRid { get; init; }
    public uint? LinkedCanaryExitCode { get; init; }
}

public static class MatawakaLinkedTokenProbe
{
    private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenUser = 1;
    private const int TokenElevationType = 18;
    private const int TokenLinkedToken = 19;
    private const int TokenElevation = 20;
    private const int TokenIntegrityLevel = 25;
    private const int TokenElevationTypeDefault = 1;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint WAIT_OBJECT_0 = 0;
    private const uint INFINITE = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential)]
    private struct SID_AND_ATTRIBUTES
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_USER_STRUCT
    {
        public SID_AND_ATTRIBUTES User;
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
    private static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr information, uint informationLength, out uint returnLength);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthority);

    [DllImport("advapi32.dll", EntryPoint = "CreateProcessAsUserW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessAsUser(IntPtr token, string applicationName, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags, IntPtr environment, string currentDirectory, ref STARTUPINFO startupInfo,
        out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    public static MatawakaLinkedTokenObservation Run(string workingDirectory)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | TOKEN_DUPLICATE | TOKEN_ASSIGN_PRIMARY, out var source))
            ThrowWin32("OPEN_SOURCE_TOKEN");
        IntPtr linked = IntPtr.Zero;
        try
        {
            var (userSid, userRid) = QueryUser(source);
            bool sourceElevated = QueryDword(source, TokenElevation) != 0;
            int sourceElevationType = QueryDword(source, TokenElevationType);
            uint sourceIntegrityRid = QueryIntegrityRid(source);

            bool linkedPresent = false;
            bool? linkedElevated = null;
            int? linkedElevationType = null;
            uint? linkedIntegrityRid = null;
            uint? linkedCanaryExitCode = null;

            // Microsoft defines TokenElevationTypeDefault as "the token does not have a linked token".
            // Only query/use TokenLinkedToken when the elevation type says one must exist.
            if (sourceElevationType != TokenElevationTypeDefault)
            {
                IntPtr buffer = Marshal.AllocHGlobal(IntPtr.Size);
                try
                {
                    Marshal.WriteIntPtr(buffer, IntPtr.Zero);
                    if (!GetTokenInformation(source, TokenLinkedToken, buffer, checked((uint)IntPtr.Size), out uint returned))
                        ThrowWin32("QUERY_LINKED_TOKEN");
                    if (returned != IntPtr.Size)
                        throw new InvalidOperationException($"LINKED_TOKEN_SIZE:{returned}");
                    linked = Marshal.ReadIntPtr(buffer);
                    if (linked == IntPtr.Zero)
                        throw new InvalidOperationException("LINKED_TOKEN_HANDLE_ABSENT");
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }

                linkedPresent = true;
                linkedElevated = QueryDword(linked, TokenElevation) != 0;
                linkedElevationType = QueryDword(linked, TokenElevationType);
                linkedIntegrityRid = QueryIntegrityRid(linked);

                string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? throw new InvalidOperationException("SYSTEM_ROOT_ABSENT");
                string canary = Path.Combine(systemRoot, "System32", "cmd.exe");
                linkedCanaryExitCode = RunCanary(linked, canary, workingDirectory);
            }

            return new MatawakaLinkedTokenObservation
            {
                UserSid = userSid,
                UserRid = userRid,
                SourceElevated = sourceElevated,
                SourceElevationType = sourceElevationType,
                SourceIntegrityRid = sourceIntegrityRid,
                LinkedTokenPresent = linkedPresent,
                LinkedElevated = linkedElevated,
                LinkedElevationType = linkedElevationType,
                LinkedIntegrityRid = linkedIntegrityRid,
                LinkedCanaryExitCode = linkedCanaryExitCode
            };
        }
        finally
        {
            if (linked != IntPtr.Zero) CloseHandle(linked);
            if (source != IntPtr.Zero) CloseHandle(source);
        }
    }

    private static uint RunCanary(IntPtr token, string executable, string workingDirectory)
    {
        var command = new StringBuilder();
        command.Append('"').Append(executable).Append('"').Append(" /d /c exit 0");
        var startup = new STARTUPINFO { cb = checked((uint)Marshal.SizeOf<STARTUPINFO>()), lpDesktop = null };
        if (!CreateProcessAsUser(token, executable, command, IntPtr.Zero, IntPtr.Zero, false, CREATE_NO_WINDOW,
            IntPtr.Zero, workingDirectory, ref startup, out var pi))
            ThrowWin32("CREATE_LINKED_CANARY");
        try
        {
            uint wait = WaitForSingleObject(pi.hProcess, INFINITE);
            if (wait != WAIT_OBJECT_0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"WAIT_LINKED_CANARY:{wait}");
            if (!GetExitCodeProcess(pi.hProcess, out uint exitCode))
                ThrowWin32("GET_LINKED_CANARY_EXIT");
            return exitCode;
        }
        finally
        {
            if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
            if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
        }
    }

    private static (string Sid, uint Rid) QueryUser(IntPtr token)
    {
        IntPtr buffer = QueryVariable(token, TokenUser);
        try
        {
            var user = Marshal.PtrToStructure<TOKEN_USER_STRUCT>(buffer);
            if (user.User.Sid == IntPtr.Zero) throw new InvalidOperationException("TOKEN_USER_SID_ABSENT");
            string sid = new SecurityIdentifier(user.User.Sid).Value;
            uint rid = LastRid(user.User.Sid);
            return (sid, rid);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static uint QueryIntegrityRid(IntPtr token)
    {
        IntPtr buffer = QueryVariable(token, TokenIntegrityLevel);
        try
        {
            var label = Marshal.PtrToStructure<TOKEN_MANDATORY_LABEL>(buffer);
            if (label.Label.Sid == IntPtr.Zero) throw new InvalidOperationException("TOKEN_INTEGRITY_SID_ABSENT");
            return LastRid(label.Label.Sid);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static IntPtr QueryVariable(IntPtr token, int informationClass)
    {
        GetTokenInformation(token, informationClass, IntPtr.Zero, 0, out uint required);
        int firstError = Marshal.GetLastWin32Error();
        if (required == 0 || firstError != ERROR_INSUFFICIENT_BUFFER)
            throw new Win32Exception(firstError, $"TOKEN_VARIABLE_SIZE_{informationClass}:{required}");
        IntPtr buffer = Marshal.AllocHGlobal(checked((int)required));
        try
        {
            if (!GetTokenInformation(token, informationClass, buffer, required, out uint returned))
                ThrowWin32($"QUERY_TOKEN_VARIABLE_{informationClass}");
            if (returned > required) throw new InvalidOperationException($"TOKEN_VARIABLE_GROWTH_{informationClass}:{returned}>{required}");
            return buffer;
        }
        catch
        {
            Marshal.FreeHGlobal(buffer);
            throw;
        }
    }

    private static uint LastRid(IntPtr sid)
    {
        IntPtr countPtr = GetSidSubAuthorityCount(sid);
        if (countPtr == IntPtr.Zero) throw new InvalidOperationException("SID_SUBAUTHORITY_COUNT_ABSENT");
        byte count = Marshal.ReadByte(countPtr);
        if (count == 0) throw new InvalidOperationException("SID_SUBAUTHORITY_EMPTY");
        IntPtr ridPtr = GetSidSubAuthority(sid, (uint)(count - 1));
        if (ridPtr == IntPtr.Zero) throw new InvalidOperationException("SID_LAST_RID_ABSENT");
        return unchecked((uint)Marshal.ReadInt32(ridPtr));
    }

    private static int QueryDword(IntPtr token, int informationClass)
    {
        IntPtr buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            if (!GetTokenInformation(token, informationClass, buffer, sizeof(int), out uint returned))
                ThrowWin32($"QUERY_TOKEN_DWORD_{informationClass}");
            if (returned != sizeof(int)) throw new InvalidOperationException($"TOKEN_DWORD_SIZE_{informationClass}:{returned}");
            return Marshal.ReadInt32(buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static void ThrowWin32(string stage) => throw new Win32Exception(Marshal.GetLastWin32Error(), stage);
}
'@

$receiptFull = [IO.Path]::GetFullPath($Receipt)
if (Test-Path -LiteralPath $receiptFull) { throw "create-only receipt required: $receiptFull" }
$parent = Split-Path -Parent $receiptFull
New-Item -ItemType Directory -Force -Path $parent | Out-Null

$observation = [MatawakaLinkedTokenProbe]::Run((Get-Location).Path)
$status = if ($observation.SourceElevationType -eq 1) {
    'DIAGNOSTIC_NO_LINKED_TOKEN_SOURCE_DEFAULT'
} elseif ($observation.LinkedTokenPresent -and $observation.LinkedCanaryExitCode -eq 0) {
    'DIAGNOSTIC_LINKED_LIMITED_TOKEN_CANARY_PASS'
} else {
    'DIAGNOSTIC_LINKED_TOKEN_PRESENT_CANARY_NOT_PASS'
}

$record = [ordered]@{
    schema = 'matawaka.windows-parent-token-diagnostic/v0.1'
    status = $status
    userSid = $observation.UserSid
    userRid = [uint32]$observation.UserRid
    sourceElevated = $observation.SourceElevated
    sourceElevationType = $observation.SourceElevationType
    sourceIntegrityRid = [uint32]$observation.SourceIntegrityRid
    linkedTokenPresent = $observation.LinkedTokenPresent
    linkedElevated = $observation.LinkedElevated
    linkedElevationType = $observation.LinkedElevationType
    linkedIntegrityRid = $observation.LinkedIntegrityRid
    linkedCanaryExitCode = $observation.LinkedCanaryExitCode
    credentialUsed = $false
    userChanged = $false
    firewallRuleMutated = $false
    networkIsolationConfigMutated = $false
    globalPolicyMutated = $false
    productionProviderRegistered = $false
    proofClaimed = $false
}

$record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $receiptFull -Encoding utf8NoBOM
$record | ConvertTo-Json -Compress | Write-Host

if ($observation.SourceElevationType -ne 1 -and -not $observation.LinkedTokenPresent) { exit 2 }
if ($observation.LinkedTokenPresent -and $observation.LinkedCanaryExitCode -ne 0) { exit 3 }
exit 0
