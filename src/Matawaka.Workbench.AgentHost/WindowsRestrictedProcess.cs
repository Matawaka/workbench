using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Matawaka.Workbench.AgentHost;

/// <summary>
/// Starts the fixed semantic host with a restricted primary token derived from
/// the current Workbench process token. v0.7 preserves maximum-privilege removal and
/// lowers the child token integrity level before process creation. The child is
/// created suspended so the Job Object can be assigned before any provider code
/// resumes. This is a bounded Windows security-context reduction, not a network,
/// filesystem, AppContainer, VM, or hostile-code sandbox.
/// </summary>
internal sealed class WindowsRestrictedProcess : IDisposable
{
    public const string LowIntegritySid = "S-1-16-4096";

    private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_ADJUST_DEFAULT = 0x0080;
    private const uint DISABLE_MAX_PRIVILEGE = 0x00000001;

    private const uint CREATE_SUSPENDED = 0x00000004;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint STARTF_USESTDHANDLES = 0x00000100;
    private const uint HANDLE_FLAG_INHERIT = 0x00000001;
    private const uint SE_GROUP_INTEGRITY = 0x00000020;
    private const int TokenIntegrityLevel = 25;

    private readonly Process _process;
    private readonly StreamWriter _stdin;
    private readonly StreamReader _stdout;
    private readonly StreamReader _stderr;
    private readonly WindowsJobBoundary _jobBoundary;

    private WindowsRestrictedProcess(
        Process process,
        StreamWriter stdin,
        StreamReader stdout,
        StreamReader stderr,
        WindowsJobBoundary jobBoundary)
    {
        _process = process;
        _stdin = stdin;
        _stdout = stdout;
        _stderr = stderr;
        _jobBoundary = jobBoundary;
    }

    public Process ChildProcess => _process;
    public StreamWriter StandardInput => _stdin;
    public StreamReader StandardOutput => _stdout;
    public StreamReader StandardError => _stderr;
    public bool RestrictedTokenApplied => true;
    public bool MaximumPrivilegesDisabled => true;
    public bool LowIntegrityApplied => true;
    public bool StartedSuspended => true;
    public bool JobAssignedBeforeResume => true;

    public static WindowsRestrictedProcess Start(
        string executablePath,
        string argument,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Restricted semantic process requires Windows.");
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath))
            throw new ArgumentException("A fully-qualified fixed executable path is required.", nameof(executablePath));
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("Restricted semantic executable is missing.", executablePath);

        Directory.CreateDirectory(workingDirectory);

        var sa = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            lpSecurityDescriptor = IntPtr.Zero,
            bInheritHandle = true
        };

        IntPtr childStdinRead = IntPtr.Zero;
        IntPtr parentStdinWrite = IntPtr.Zero;
        IntPtr parentStdoutRead = IntPtr.Zero;
        IntPtr childStdoutWrite = IntPtr.Zero;
        IntPtr parentStderrRead = IntPtr.Zero;
        IntPtr childStderrWrite = IntPtr.Zero;
        IntPtr currentToken = IntPtr.Zero;
        IntPtr restrictedToken = IntPtr.Zero;
        IntPtr lowIntegritySid = IntPtr.Zero;
        IntPtr integrityBuffer = IntPtr.Zero;
        IntPtr environmentBlock = IntPtr.Zero;
        PROCESS_INFORMATION pi = default;
        Process? process = null;
        WindowsJobBoundary? jobBoundary = null;
        SafeFileHandle? stdinHandle = null;
        SafeFileHandle? stdoutHandle = null;
        SafeFileHandle? stderrHandle = null;
        StreamWriter? stdin = null;
        StreamReader? stdout = null;
        StreamReader? stderr = null;

        try
        {
            CreateInheritablePipe(out childStdinRead, out parentStdinWrite, sa, parentEndIsRead: false);
            CreateInheritablePipe(out parentStdoutRead, out childStdoutWrite, sa, parentEndIsRead: true);
            CreateInheritablePipe(out parentStderrRead, out childStderrWrite, sa, parentEndIsRead: true);

            if (!OpenProcessToken(
                    GetCurrentProcess(),
                    TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ADJUST_DEFAULT,
                    out currentToken))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken failed.");
            }

            if (!CreateRestrictedToken(
                    currentToken,
                    DISABLE_MAX_PRIVILEGE,
                    0,
                    IntPtr.Zero,
                    0,
                    IntPtr.Zero,
                    0,
                    IntPtr.Zero,
                    out restrictedToken))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateRestrictedToken failed.");
            }

            ApplyLowIntegrity(restrictedToken, out lowIntegritySid, out integrityBuffer);
            VerifyIntegrityLevel(restrictedToken, LowIntegritySid);
            environmentBlock = BuildEnvironmentBlock(environment);

            var startup = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                dwFlags = STARTF_USESTDHANDLES,
                hStdInput = childStdinRead,
                hStdOutput = childStdoutWrite,
                hStdError = childStderrWrite
            };

            var commandLine = new StringBuilder();
            commandLine.Append('"').Append(executablePath).Append('"');
            if (!string.IsNullOrWhiteSpace(argument))
                commandLine.Append(' ').Append(argument);

            var flags = CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW;
            if (!CreateProcessAsUser(
                    restrictedToken,
                    executablePath,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    true,
                    flags,
                    environmentBlock,
                    workingDirectory,
                    ref startup,
                    out pi))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessAsUser(restricted token) failed.");
            }

            // The native process exists but its primary thread is suspended.
            // Assign the Job Object before any child provider code can execute.
            process = Process.GetProcessById(unchecked((int)pi.dwProcessId));
            jobBoundary = WindowsJobBoundary.CreateAndAssign(process);
            if (!jobBoundary.Applied)
                throw new InvalidOperationException("Windows Job Object boundary was not applied to restricted process.");

            if (ResumeThread(pi.hThread) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ResumeThread failed.");

            // Parent retains only its pipe ends after successful process creation.
            CloseNative(ref childStdinRead);
            CloseNative(ref childStdoutWrite);
            CloseNative(ref childStderrWrite);

            stdinHandle = new SafeFileHandle(parentStdinWrite, ownsHandle: true);
            parentStdinWrite = IntPtr.Zero;
            stdoutHandle = new SafeFileHandle(parentStdoutRead, ownsHandle: true);
            parentStdoutRead = IntPtr.Zero;
            stderrHandle = new SafeFileHandle(parentStderrRead, ownsHandle: true);
            parentStderrRead = IntPtr.Zero;

            stdin = new StreamWriter(
                new FileStream(stdinHandle, FileAccess.Write, bufferSize: 4096, isAsync: false),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 4096,
                leaveOpen: false)
            {
                AutoFlush = false
            };
            stdinHandle = null;

            stdout = new StreamReader(
                new FileStream(stdoutHandle, FileAccess.Read, bufferSize: 4096, isAsync: false),
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: false);
            stdoutHandle = null;

            stderr = new StreamReader(
                new FileStream(stderrHandle, FileAccess.Read, bufferSize: 4096, isAsync: false),
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: false);
            stderrHandle = null;

            var result = new WindowsRestrictedProcess(process, stdin, stdout, stderr, jobBoundary);
            process = null;
            stdin = null;
            stdout = null;
            stderr = null;
            jobBoundary = null;
            return result;
        }
        catch
        {
            if (pi.hProcess != IntPtr.Zero)
                TerminateProcess(pi.hProcess, 1);
            throw;
        }
        finally
        {
            stdin?.Dispose();
            stdout?.Dispose();
            stderr?.Dispose();
            stdinHandle?.Dispose();
            stdoutHandle?.Dispose();
            stderrHandle?.Dispose();
            jobBoundary?.Dispose();
            process?.Dispose();

            CloseNative(ref childStdinRead);
            CloseNative(ref parentStdinWrite);
            CloseNative(ref parentStdoutRead);
            CloseNative(ref childStdoutWrite);
            CloseNative(ref parentStderrRead);
            CloseNative(ref childStderrWrite);
            CloseNative(ref currentToken);
            CloseNative(ref restrictedToken);
            CloseNative(ref pi.hThread);
            CloseNative(ref pi.hProcess);

            if (integrityBuffer != IntPtr.Zero)
                Marshal.FreeHGlobal(integrityBuffer);
            if (lowIntegritySid != IntPtr.Zero)
                LocalFree(lowIntegritySid);
            if (environmentBlock != IntPtr.Zero)
                Marshal.FreeHGlobal(environmentBlock);
        }
    }

    public void Dispose()
    {
        _stdin.Dispose();
        _stdout.Dispose();
        _stderr.Dispose();
        _jobBoundary.Dispose();
        _process.Dispose();
    }

    private static void CreateInheritablePipe(
        out IntPtr read,
        out IntPtr write,
        SECURITY_ATTRIBUTES securityAttributes,
        bool parentEndIsRead)
    {
        if (!CreatePipe(out read, out write, ref securityAttributes, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe failed.");

        var parentHandle = parentEndIsRead ? read : write;
        if (!SetHandleInformation(parentHandle, HANDLE_FLAG_INHERIT, 0))
        {
            var error = Marshal.GetLastWin32Error();
            CloseHandle(read);
            CloseHandle(write);
            read = IntPtr.Zero;
            write = IntPtr.Zero;
            throw new Win32Exception(error, "SetHandleInformation failed.");
        }
    }

    private static void ApplyLowIntegrity(
        IntPtr token,
        out IntPtr integritySid,
        out IntPtr integrityBuffer)
    {
        integritySid = IntPtr.Zero;
        integrityBuffer = IntPtr.Zero;

        if (!ConvertStringSidToSid(LowIntegritySid, out integritySid))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "ConvertStringSidToSid(low integrity) failed.");

        var label = new TOKEN_MANDATORY_LABEL
        {
            Label = new SID_AND_ATTRIBUTES
            {
                Sid = integritySid,
                Attributes = SE_GROUP_INTEGRITY
            }
        };

        var labelSize = Marshal.SizeOf<TOKEN_MANDATORY_LABEL>();
        var sidLength = checked((int)GetLengthSid(integritySid));
        var bufferSize = checked(labelSize + sidLength);
        integrityBuffer = Marshal.AllocHGlobal(bufferSize);
        for (var i = 0; i < bufferSize; i++) Marshal.WriteByte(integrityBuffer, i, 0);
        Marshal.StructureToPtr(label, integrityBuffer, fDeleteOld: false);

        if (!SetTokenInformation(
                token,
                TokenIntegrityLevel,
                integrityBuffer,
                checked((uint)bufferSize)))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetTokenInformation(TokenIntegrityLevel) failed.");
        }
    }

    private static void VerifyIntegrityLevel(IntPtr token, string expectedSid)
    {
        if (GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out var requiredLength))
            throw new InvalidOperationException("Unexpected zero-length TokenIntegrityLevel query success.");

        const int ERROR_INSUFFICIENT_BUFFER = 122;
        var error = Marshal.GetLastWin32Error();
        if (error != ERROR_INSUFFICIENT_BUFFER || requiredLength == 0)
            throw new Win32Exception(error, "GetTokenInformation(TokenIntegrityLevel) size query failed.");

        var buffer = Marshal.AllocHGlobal(checked((int)requiredLength));
        try
        {
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, requiredLength, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetTokenInformation(TokenIntegrityLevel) failed.");

            var label = Marshal.PtrToStructure<TOKEN_MANDATORY_LABEL>(buffer);
            if (label.Label.Sid == IntPtr.Zero)
                throw new InvalidDataException("Restricted token has no integrity SID.");

            if (!ConvertSidToStringSid(label.Label.Sid, out var sidStringPtr))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ConvertSidToStringSid failed.");

            try
            {
                var observed = Marshal.PtrToStringUni(sidStringPtr);
                if (!string.Equals(observed, expectedSid, StringComparison.Ordinal))
                    throw new InvalidDataException($"Restricted token integrity mismatch. Expected {expectedSid}, observed {observed ?? "<null>"}.");
            }
            finally
            {
                LocalFree(sidStringPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IntPtr BuildEnvironmentBlock(IReadOnlyDictionary<string, string> environment)
    {
        var builder = new StringBuilder();
        foreach (var pair in environment.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (pair.Key.Contains('=') || pair.Key.Contains('\0') || pair.Value.Contains('\0'))
                throw new InvalidDataException("Invalid environment key/value for restricted child process.");
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
        }
        builder.Append('\0');

        var bytes = Encoding.Unicode.GetBytes(builder.ToString());
        var block = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, block, bytes.Length);
        return block;
    }

    private static void CloseNative(ref IntPtr handle)
    {
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            return;
        CloseHandle(handle);
        handle = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateRestrictedToken(
        IntPtr existingTokenHandle,
        uint flags,
        uint disableSidCount,
        IntPtr sidsToDisable,
        uint deletePrivilegeCount,
        IntPtr privilegesToDelete,
        uint restrictedSidCount,
        IntPtr sidsToRestrict,
        out IntPtr newTokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        uint tokenInformationLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
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

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSidToSid(string stringSid, out IntPtr sid);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertSidToStringSid(IntPtr sid, out IntPtr stringSid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint GetLengthSid(IntPtr sid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(
        out IntPtr readPipe,
        out IntPtr writePipe,
        ref SECURITY_ATTRIBUTES pipeAttributes,
        uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr threadHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr processHandle, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
