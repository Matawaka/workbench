using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Matawaka.Workbench.AgentHost;

/// <summary>
/// Runtime observation of the effective Windows security context. This is
/// intentionally observational: it does not create, widen, or authorize the
/// token. v0.7 uses it as a child->parent pre-input attestation so launch
/// configuration and observed runtime state remain distinct evidence surfaces.
/// </summary>
public sealed record SemanticHostSecurityAttestation(
    string Schema,
    string UserSid,
    string IntegrityLevelSid,
    bool TokenHasRestrictions,
    bool ProcessInJob,
    bool IsAppContainer,
    bool Elevated,
    string ElevationType,
    IReadOnlyList<string> EnabledPrivileges,
    bool NoEnabledPrivilegesBeyondChangeNotify);

public static class WindowsSecurityContextObserver
{
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenUser = 1;
    private const int TokenPrivileges = 3;
    private const int TokenElevationType = 18;
    private const int TokenElevation = 20;
    private const int TokenHasRestrictions = 21;
    private const int TokenIntegrityLevel = 25;
    private const int TokenIsAppContainer = 29;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;

    public static SemanticHostSecurityAttestation CaptureCurrentProcess()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows security attestation requires Windows.");

        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken(TOKEN_QUERY) failed.");

        try
        {
            var userSid = ReadSid(token, TokenUser);
            var integritySid = ReadSid(token, TokenIntegrityLevel);
            var hasRestrictions = ReadBooleanScalar(token, TokenHasRestrictions);
            var isAppContainer = ReadFixedDword(token, TokenIsAppContainer) != 0;
            var elevated = ReadFixedDword(token, TokenElevation) != 0;
            var elevationTypeValue = ReadFixedDword(token, TokenElevationType);
            var enabledPrivileges = ReadEnabledPrivileges(token);

            if (!IsProcessInJob(GetCurrentProcess(), IntPtr.Zero, out var processInJob))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "IsProcessInJob failed.");

            var onlyChangeNotify = enabledPrivileges.All(name =>
                string.Equals(name, "SeChangeNotifyPrivilege", StringComparison.OrdinalIgnoreCase));

            return new SemanticHostSecurityAttestation(
                "matawaka.semantic-host-security-attestation/v0.7",
                userSid,
                integritySid,
                hasRestrictions,
                processInJob,
                isAppContainer,
                elevated,
                elevationTypeValue switch
                {
                    1 => "TokenElevationTypeDefault",
                    2 => "TokenElevationTypeFull",
                    3 => "TokenElevationTypeLimited",
                    _ => $"Unknown({elevationTypeValue})"
                },
                enabledPrivileges,
                onlyChangeNotify);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    public static string GetCurrentUserSid()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows SID observation requires Windows.");

        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken(TOKEN_QUERY) failed.");

        try
        {
            return ReadSid(token, TokenUser);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static string ReadSid(IntPtr token, int informationClass)
    {
        var buffer = GetTokenInformationBuffer(token, informationClass);
        try
        {
            // TOKEN_USER and TOKEN_MANDATORY_LABEL both begin with SID_AND_ATTRIBUTES.
            var sidAndAttributes = Marshal.PtrToStructure<SID_AND_ATTRIBUTES>(buffer);
            if (sidAndAttributes.Sid == IntPtr.Zero)
                throw new InvalidDataException($"Token information class {informationClass} returned a null SID.");

            if (!ConvertSidToStringSid(sidAndAttributes.Sid, out var stringSid) || stringSid == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ConvertSidToStringSid failed.");

            try
            {
                return Marshal.PtrToStringUni(stringSid)
                    ?? throw new InvalidDataException("Converted SID was empty.");
            }
            finally
            {
                LocalFree(stringSid);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }


    private static bool ReadBooleanScalar(IntPtr token, int informationClass)
    {
        // Some Windows token information classes are documented as DWORD but
        // may report a one-byte BOOL/BOOLEAN-sized payload on current systems.
        // TokenHasRestrictions is observed this way on Windows 11. Accept only
        // the two representations that preserve boolean semantics: 1 byte or
        // a full DWORD. Any ambiguous 2/3-byte result remains fail-closed.
        const int bufferSize = sizeof(uint);
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            Marshal.WriteInt32(buffer, 0);
            if (!GetTokenInformation(token, informationClass, buffer, bufferSize, out var returnedLength))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"GetTokenInformation({informationClass}) boolean query failed.");

            return returnedLength switch
            {
                1 => Marshal.ReadByte(buffer) != 0,
                >= bufferSize => Marshal.ReadInt32(buffer) != 0,
                _ => throw new InvalidDataException($"GetTokenInformation({informationClass}) returned unsupported boolean scalar length {returnedLength} bytes.")
            };
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static uint ReadFixedDword(IntPtr token, int informationClass)
    {
        const int fixedSize = sizeof(uint);
        var buffer = Marshal.AllocHGlobal(fixedSize);
        try
        {
            Marshal.WriteInt32(buffer, 0);
            if (!GetTokenInformation(token, informationClass, buffer, fixedSize, out var returnedLength))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"GetTokenInformation({informationClass}) fixed DWORD query failed.");

            if (returnedLength < fixedSize)
                throw new InvalidDataException($"GetTokenInformation({informationClass}) returned only {returnedLength} bytes for a DWORD token class.");

            return unchecked((uint)Marshal.ReadInt32(buffer));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IReadOnlyList<string> ReadEnabledPrivileges(IntPtr token)
    {
        var buffer = GetTokenInformationBuffer(token, TokenPrivileges);
        try
        {
            var count = Marshal.ReadInt32(buffer);
            if (count < 0 || count > 1024)
                throw new InvalidDataException($"Unexpected token privilege count: {count}.");

            var result = new List<string>();
            var itemSize = Marshal.SizeOf<LUID_AND_ATTRIBUTES>();
            var cursor = IntPtr.Add(buffer, sizeof(uint));

            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<LUID_AND_ATTRIBUTES>(IntPtr.Add(cursor, checked(i * itemSize)));
                if ((item.Attributes & SE_PRIVILEGE_ENABLED) == 0)
                    continue;

                result.Add(LookupPrivilege(item.Luid));
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string LookupPrivilege(LUID luid)
    {
        var length = 0;
        _ = LookupPrivilegeName(null, ref luid, null, ref length);
        var error = Marshal.GetLastWin32Error();
        if (length <= 0 || (error != 0 && error != ERROR_INSUFFICIENT_BUFFER))
            throw new Win32Exception(error, "LookupPrivilegeName(size) failed.");

        var builder = new StringBuilder(length + 1);
        if (!LookupPrivilegeName(null, ref luid, builder, ref length))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "LookupPrivilegeName failed.");

        return builder.ToString();
    }

    private static IntPtr GetTokenInformationBuffer(IntPtr token, int informationClass)
    {
        _ = GetTokenInformation(token, informationClass, IntPtr.Zero, 0, out var length);
        var error = Marshal.GetLastWin32Error();
        if (length <= 0 || (error != 0 && error != ERROR_INSUFFICIENT_BUFFER))
            throw new Win32Exception(error, $"GetTokenInformation({informationClass}) size query failed.");

        var buffer = Marshal.AllocHGlobal(length);
        if (!GetTokenInformation(token, informationClass, buffer, length, out _))
        {
            var finalError = Marshal.GetLastWin32Error();
            Marshal.FreeHGlobal(buffer);
            throw new Win32Exception(finalError, $"GetTokenInformation({informationClass}) failed.");
        }

        return buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SID_AND_ATTRIBUTES
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(IntPtr processHandle, IntPtr jobHandle, [MarshalAs(UnmanagedType.Bool)] out bool result);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertSidToStringSid(IntPtr sid, out IntPtr stringSid);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeName(
        string? systemName,
        ref LUID luid,
        StringBuilder? name,
        ref int nameLength);
}
