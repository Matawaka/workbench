param(
    [Parameter(Mandatory = $true)][string]$Receipt
)

$ErrorActionPreference='Stop'

Add-Type -TypeDefinition @'
#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public sealed class MatawakaSaferLuaObservation
{
    public bool SourceElevated { get; init; }
    public uint SourceIntegrityRid { get; init; }
    public bool SaferElevated { get; init; }
    public bool SaferHasRestrictions { get; init; }
    public uint SaferIntegrityRid { get; init; }
    public bool ChainedLuaCreated { get; init; }
    public bool ChainedLuaElevated { get; init; }
    public int ChainedLuaElevationType { get; init; }
    public bool ChainedLuaHasRestrictions { get; init; }
    public bool ChainedLuaHasRestrictingSids { get; init; }
    public uint ChainedLuaIntegrityRid { get; init; }
    public bool ProcessCreated { get; init; }
    public int? CreateError { get; init; }
    public uint? ExitCode { get; init; }
}

public static class MatawakaSaferLuaProbe
{
    private const uint TOKEN_ASSIGN_PRIMARY=0x0001, TOKEN_DUPLICATE=0x0002, TOKEN_QUERY=0x0008;
    private const uint LUA_TOKEN=0x00000004;
    private const uint SAFER_SCOPEID_USER=2, SAFER_LEVELID_NORMALUSER=0x00020000, SAFER_LEVEL_OPEN=1;
    private const int TokenElevationType=18, TokenElevation=20, TokenHasRestrictions=21, TokenIntegrityLevel=25;
    private const int ERROR_INSUFFICIENT_BUFFER=122;
    private const uint CREATE_NO_WINDOW=0x08000000, WAIT_OBJECT_0=0, INFINITE=0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential)] private struct SID_AND_ATTRIBUTES { public IntPtr Sid; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct TOKEN_MANDATORY_LABEL { public SID_AND_ATTRIBUTES Label; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct STARTUPINFO
    { public uint cb; public string? lpReserved; public string? lpDesktop; public string? lpTitle; public uint dwX,dwY,dwXSize,dwYSize,dwXCountChars,dwYCountChars,dwFillAttribute,dwFlags; public ushort wShowWindow,cbReserved2; public IntPtr lpReserved2,hStdInput,hStdOutput,hStdError; }
    [StructLayout(LayoutKind.Sequential)] private struct PROCESS_INFORMATION { public IntPtr hProcess,hThread; public uint dwProcessId,dwThreadId; }

    [DllImport("kernel32.dll",ExactSpelling=true)] private static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll",SetLastError=true,ExactSpelling=true)][return:MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(IntPtr p,uint a,out IntPtr t);
    [DllImport("advapi32.dll",SetLastError=true,ExactSpelling=true)][return:MarshalAs(UnmanagedType.Bool)] private static extern bool CreateRestrictedToken(IntPtr existing,uint flags,uint disableCount,IntPtr disableSids,uint deleteCount,IntPtr deletePrivs,uint restrictCount,IntPtr restrictSids,out IntPtr token);
    [DllImport("advapi32.dll",SetLastError=true,ExactSpelling=true)][return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetTokenInformation(IntPtr t,int c,IntPtr b,uint n,out uint r);
    [DllImport("advapi32.dll",SetLastError=true,ExactSpelling=true)][return:MarshalAs(UnmanagedType.Bool)] private static extern bool IsTokenRestricted(IntPtr t);
    [DllImport("advapi32.dll",ExactSpelling=true)] private static extern IntPtr GetSidSubAuthorityCount(IntPtr s);
    [DllImport("advapi32.dll",ExactSpelling=true)] private static extern IntPtr GetSidSubAuthority(IntPtr s,uint i);
    [DllImport("advapi32.dll",SetLastError=true,ExactSpelling=true)][return:MarshalAs(UnmanagedType.Bool)] private static extern bool SaferCreateLevel(uint scope,uint level,uint flags,out IntPtr h,IntPtr reserved);
    [DllImport("advapi32.dll",SetLastError=true,ExactSpelling=true)][return:MarshalAs(UnmanagedType.Bool)] private static extern bool SaferComputeTokenFromLevel(IntPtr level,IntPtr input,out IntPtr output,uint flags,IntPtr reserved);
    [DllImport("advapi32.dll",SetLastError=true,ExactSpelling=true)][return:MarshalAs(UnmanagedType.Bool)] private static extern bool SaferCloseLevel(IntPtr h);
    [DllImport("advapi32.dll",EntryPoint="CreateProcessAsUserW",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcessAsUser(IntPtr t,string app,StringBuilder cmd,IntPtr pa,IntPtr ta,[MarshalAs(UnmanagedType.Bool)]bool inherit,uint flags,IntPtr env,string cwd,ref STARTUPINFO si,out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll",SetLastError=true,ExactSpelling=true)] private static extern uint WaitForSingleObject(IntPtr h,uint ms);
    [DllImport("kernel32.dll",SetLastError=true,ExactSpelling=true)][return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetExitCodeProcess(IntPtr h,out uint c);
    [DllImport("kernel32.dll",SetLastError=true,ExactSpelling=true)][return:MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr h);

    public static MatawakaSaferLuaObservation Run(string cwd)
    {
        if(!OpenProcessToken(GetCurrentProcess(),TOKEN_ASSIGN_PRIMARY|TOKEN_DUPLICATE|TOKEN_QUERY,out var source)) Fail("OPEN_SOURCE");
        IntPtr level=IntPtr.Zero, safer=IntPtr.Zero, lua=IntPtr.Zero;
        try
        {
            bool sourceElevated=Dword(source,TokenElevation)!=0; uint sourceIl=Integrity(source);
            if(!SaferCreateLevel(SAFER_SCOPEID_USER,SAFER_LEVELID_NORMALUSER,SAFER_LEVEL_OPEN,out level,IntPtr.Zero)) Fail("SAFER_LEVEL");
            if(!SaferComputeTokenFromLevel(level,source,out safer,0,IntPtr.Zero)) Fail("SAFER_TOKEN");
            if(safer==IntPtr.Zero) throw new InvalidOperationException("SAFER_TOKEN_ABSENT");
            bool saferElevated=Dword(safer,TokenElevation)!=0; bool saferRestrictions=Bool(safer,TokenHasRestrictions); uint saferIl=Integrity(safer);
            if(!CreateRestrictedToken(safer,LUA_TOKEN,0,IntPtr.Zero,0,IntPtr.Zero,0,IntPtr.Zero,out lua)) Fail("CHAINED_LUA_TOKEN");
            if(lua==IntPtr.Zero) throw new InvalidOperationException("CHAINED_LUA_ABSENT");
            bool luaElevated=Dword(lua,TokenElevation)!=0; int luaType=Dword(lua,TokenElevationType); bool luaRestrictions=Bool(lua,TokenHasRestrictions); bool luaRestricting=IsTokenRestricted(lua); uint luaIl=Integrity(lua);
            string root=Environment.GetEnvironmentVariable("SystemRoot")??throw new InvalidOperationException("SYSTEM_ROOT_ABSENT"); string exe=Path.Combine(root,"System32","cmd.exe");
            var launch=TryLaunch(lua,exe,cwd);
            return new MatawakaSaferLuaObservation{SourceElevated=sourceElevated,SourceIntegrityRid=sourceIl,SaferElevated=saferElevated,SaferHasRestrictions=saferRestrictions,SaferIntegrityRid=saferIl,ChainedLuaCreated=true,ChainedLuaElevated=luaElevated,ChainedLuaElevationType=luaType,ChainedLuaHasRestrictions=luaRestrictions,ChainedLuaHasRestrictingSids=luaRestricting,ChainedLuaIntegrityRid=luaIl,ProcessCreated=launch.Created,CreateError=launch.Error,ExitCode=launch.Exit};
        }
        finally { if(lua!=IntPtr.Zero)CloseHandle(lua); if(safer!=IntPtr.Zero)CloseHandle(safer); if(level!=IntPtr.Zero&&!SaferCloseLevel(level))Fail("SAFER_CLOSE"); if(source!=IntPtr.Zero)CloseHandle(source); }
    }
    private static (bool Created,int? Error,uint? Exit) TryLaunch(IntPtr token,string exe,string cwd)
    { var cmd=new StringBuilder().Append('"').Append(exe).Append('"').Append(" /d /c exit 0"); var si=new STARTUPINFO{cb=checked((uint)Marshal.SizeOf<STARTUPINFO>()),lpDesktop=null}; if(!CreateProcessAsUser(token,exe,cmd,IntPtr.Zero,IntPtr.Zero,false,CREATE_NO_WINDOW,IntPtr.Zero,cwd,ref si,out var pi)) return(false,Marshal.GetLastWin32Error(),null); try{uint w=WaitForSingleObject(pi.hProcess,INFINITE);if(w!=WAIT_OBJECT_0)throw new Win32Exception(Marshal.GetLastWin32Error(),$"WAIT:{w}");if(!GetExitCodeProcess(pi.hProcess,out uint e))Fail("EXIT");return(true,null,e);}finally{if(pi.hThread!=IntPtr.Zero)CloseHandle(pi.hThread);if(pi.hProcess!=IntPtr.Zero)CloseHandle(pi.hProcess);} }
    private static bool Bool(IntPtr t,int c){IntPtr b=Marshal.AllocHGlobal(4);try{Marshal.WriteInt32(b,0);if(!GetTokenInformation(t,c,b,4,out uint r))Fail("BOOL_"+c);if(r!=1&&r!=4)throw new InvalidOperationException($"BOOL_SIZE:{c}:{r}");return Marshal.ReadByte(b)!=0;}finally{Marshal.FreeHGlobal(b);}}
    private static int Dword(IntPtr t,int c){IntPtr b=Marshal.AllocHGlobal(4);try{if(!GetTokenInformation(t,c,b,4,out uint r))Fail("DWORD_"+c);if(r!=4)throw new InvalidOperationException($"DWORD_SIZE:{c}:{r}");return Marshal.ReadInt32(b);}finally{Marshal.FreeHGlobal(b);}}
    private static uint Integrity(IntPtr t){GetTokenInformation(t,TokenIntegrityLevel,IntPtr.Zero,0,out uint n);int e=Marshal.GetLastWin32Error();if(n==0||e!=ERROR_INSUFFICIENT_BUFFER)throw new Win32Exception(e,$"IL_SIZE:{n}");IntPtr b=Marshal.AllocHGlobal((int)n);try{if(!GetTokenInformation(t,TokenIntegrityLevel,b,n,out _))Fail("IL_QUERY");var l=Marshal.PtrToStructure<TOKEN_MANDATORY_LABEL>(b);IntPtr cp=GetSidSubAuthorityCount(l.Label.Sid);if(cp==IntPtr.Zero)throw new InvalidOperationException("IL_COUNT");byte c=Marshal.ReadByte(cp);IntPtr rp=GetSidSubAuthority(l.Label.Sid,(uint)(c-1));if(rp==IntPtr.Zero)throw new InvalidOperationException("IL_RID");return unchecked((uint)Marshal.ReadInt32(rp));}finally{Marshal.FreeHGlobal(b);}}
    private static void Fail(string s)=>throw new Win32Exception(Marshal.GetLastWin32Error(),s);
}
'@

$full=[IO.Path]::GetFullPath($Receipt);if(Test-Path -LiteralPath $full){throw "create-only receipt required: $full"};New-Item -ItemType Directory -Force -Path (Split-Path -Parent $full)|Out-Null
$o=[MatawakaSaferLuaProbe]::Run((Get-Location).Path)
$viable=$o.ChainedLuaCreated -and (-not $o.ChainedLuaElevated) -and $o.ChainedLuaHasRestrictions -and $o.ProcessCreated -and $o.ExitCode -eq 0
$status=if($viable){'SAFER_LUA_CHAIN_LAUNCHABLE_NON_ELEVATED'}elseif($o.ProcessCreated){'SAFER_LUA_CHAIN_PROCESS_FAIL_CLOSED'}else{'SAFER_LUA_CHAIN_CREATE_FAILED'}
$r=[ordered]@{schema='matawaka.windows-safer-lua-chain-diagnostic/v0.1';status=$status;sourceElevated=$o.SourceElevated;sourceIntegrityRid=[uint32]$o.SourceIntegrityRid;saferElevated=$o.SaferElevated;saferHasRestrictions=$o.SaferHasRestrictions;saferIntegrityRid=[uint32]$o.SaferIntegrityRid;chainedLuaCreated=$o.ChainedLuaCreated;chainedLuaElevated=$o.ChainedLuaElevated;chainedLuaElevationType=$o.ChainedLuaElevationType;chainedLuaHasRestrictions=$o.ChainedLuaHasRestrictions;chainedLuaHasRestrictingSids=$o.ChainedLuaHasRestrictingSids;chainedLuaIntegrityRid=[uint32]$o.ChainedLuaIntegrityRid;processCreated=$o.ProcessCreated;createError=$o.CreateError;exitCodeUnsigned=$o.ExitCode;credentialUsed=$false;userChanged=$false;saferPolicyMutated=$false;firewallRuleMutated=$false;networkIsolationConfigMutated=$false;globalPolicyMutated=$false;productionProviderRegistered=$false;proofClaimed=$false}
$r|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $full -Encoding utf8NoBOM;$r|ConvertTo-Json -Compress|Write-Host
if(-not $viable){exit 3};exit 0
