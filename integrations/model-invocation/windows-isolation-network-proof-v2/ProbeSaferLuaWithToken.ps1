param([Parameter(Mandatory=$true)][string]$Receipt)
$ErrorActionPreference='Stop'

Add-Type -TypeDefinition @'
#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public sealed class SaferLuaWithTokenObservation
{
    public bool SaferElevated { get; init; }
    public bool SaferHasRestrictions { get; init; }
    public uint SaferIntegrityRid { get; init; }
    public bool LuaElevated { get; init; }
    public bool LuaHasRestrictions { get; init; }
    public int LuaElevationType { get; init; }
    public uint LuaIntegrityRid { get; init; }
    public bool ProcessCreated { get; init; }
    public int? CreateError { get; init; }
    public uint? ExitCode { get; init; }
}

public static class SaferLuaWithTokenProbe
{
    const uint TOKEN_ASSIGN_PRIMARY=1,TOKEN_DUPLICATE=2,TOKEN_QUERY=8,LUA_TOKEN=4;
    const uint SAFER_SCOPEID_USER=2,SAFER_LEVELID_NORMALUSER=0x20000,SAFER_LEVEL_OPEN=1;
    const int TokenElevationType=18,TokenElevation=20,TokenHasRestrictions=21,TokenIntegrityLevel=25,ERROR_INSUFFICIENT_BUFFER=122;
    const uint CREATE_NO_WINDOW=0x08000000,WAIT_OBJECT_0=0,INFINITE=0xFFFFFFFF;
    [StructLayout(LayoutKind.Sequential)] struct SID_AND_ATTRIBUTES{public IntPtr Sid;public uint Attributes;}
    [StructLayout(LayoutKind.Sequential)] struct TOKEN_MANDATORY_LABEL{public SID_AND_ATTRIBUTES Label;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct STARTUPINFO{public uint cb;public string? lpReserved,lpDesktop,lpTitle;public uint dwX,dwY,dwXSize,dwYSize,dwXCountChars,dwYCountChars,dwFillAttribute,dwFlags;public ushort wShowWindow,cbReserved2;public IntPtr lpReserved2,hStdInput,hStdOutput,hStdError;}
    [StructLayout(LayoutKind.Sequential)] struct PROCESS_INFORMATION{public IntPtr hProcess,hThread;public uint dwProcessId,dwThreadId;}
    [DllImport("kernel32.dll",ExactSpelling=true)] static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll",SetLastError=true,ExactSpelling=true)][return:MarshalAs(UnmanagedType.Bool)] static extern bool OpenProcessToken(IntPtr p,uint a,out IntPtr t);
    [DllImport("advapi32.dll",SetLastError=true,ExactSpelling=true)][return:MarshalAs(UnmanagedType.Bool)] static extern bool CreateRestrictedToken(IntPtr e,uint f,uint dc,IntPtr ds,uint pc,IntPtr ps,uint rc,IntPtr rs,out IntPtr t);
    [DllImport("advapi32.dll",SetLastError=true,ExactSpelling=true)][return:MarshalAs(UnmanagedType.Bool)] static extern bool GetTokenInformation(IntPtr t,int c,IntPtr b,uint n,out uint r);
    [DllImport("advapi32.dll",ExactSpelling=true)] static extern IntPtr GetSidSubAuthorityCount(IntPtr s);
    [DllImport("advapi32.dll",ExactSpelling=true)] static extern IntPtr GetSidSubAuthority(IntPtr s,uint i);
    [DllImport("advapi32.dll",SetLastError=true,ExactSpelling=true)][return:MarshalAs(UnmanagedType.Bool)] static extern bool SaferCreateLevel(uint s,uint l,uint f,out IntPtr h,IntPtr r);
    [DllImport("advapi32.dll",SetLastError=true,ExactSpelling=true)][return:MarshalAs(UnmanagedType.Bool)] static extern bool SaferComputeTokenFromLevel(IntPtr l,IntPtr i,out IntPtr o,uint f,IntPtr r);
    [DllImport("advapi32.dll",SetLastError=true,ExactSpelling=true)][return:MarshalAs(UnmanagedType.Bool)] static extern bool SaferCloseLevel(IntPtr h);
    [DllImport("advapi32.dll",EntryPoint="CreateProcessWithTokenW",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)] static extern bool CreateProcessWithToken(IntPtr t,uint logon,string app,StringBuilder cmd,uint f,IntPtr env,string cwd,ref STARTUPINFO si,out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll",SetLastError=true,ExactSpelling=true)] static extern uint WaitForSingleObject(IntPtr h,uint ms);
    [DllImport("kernel32.dll",SetLastError=true,ExactSpelling=true)][return:MarshalAs(UnmanagedType.Bool)] static extern bool GetExitCodeProcess(IntPtr h,out uint c);
    [DllImport("kernel32.dll",SetLastError=true,ExactSpelling=true)][return:MarshalAs(UnmanagedType.Bool)] static extern bool CloseHandle(IntPtr h);

    public static SaferLuaWithTokenObservation Run(string cwd)
    {
        if(!OpenProcessToken(GetCurrentProcess(),TOKEN_ASSIGN_PRIMARY|TOKEN_DUPLICATE|TOKEN_QUERY,out var source))Fail("OPEN_SOURCE");
        IntPtr level=IntPtr.Zero,safer=IntPtr.Zero,lua=IntPtr.Zero;
        try{
            if(!SaferCreateLevel(SAFER_SCOPEID_USER,SAFER_LEVELID_NORMALUSER,SAFER_LEVEL_OPEN,out level,IntPtr.Zero))Fail("SAFER_LEVEL");
            if(!SaferComputeTokenFromLevel(level,source,out safer,0,IntPtr.Zero))Fail("SAFER_TOKEN");
            bool se=Dword(safer,TokenElevation)!=0,sr=Bool(safer,TokenHasRestrictions);uint si=Integrity(safer);
            if(!CreateRestrictedToken(safer,LUA_TOKEN,0,IntPtr.Zero,0,IntPtr.Zero,0,IntPtr.Zero,out lua))Fail("LUA_TOKEN");
            bool le=Dword(lua,TokenElevation)!=0,lr=Bool(lua,TokenHasRestrictions);int lt=Dword(lua,TokenElevationType);uint li=Integrity(lua);
            string root=Environment.GetEnvironmentVariable("SystemRoot")??throw new InvalidOperationException("SYSTEM_ROOT_ABSENT");string exe=Path.Combine(root,"System32","cmd.exe");
            var launch=Launch(lua,exe,cwd);
            return new SaferLuaWithTokenObservation{SaferElevated=se,SaferHasRestrictions=sr,SaferIntegrityRid=si,LuaElevated=le,LuaHasRestrictions=lr,LuaElevationType=lt,LuaIntegrityRid=li,ProcessCreated=launch.Created,CreateError=launch.Error,ExitCode=launch.Exit};
        }finally{if(lua!=IntPtr.Zero)CloseHandle(lua);if(safer!=IntPtr.Zero)CloseHandle(safer);if(level!=IntPtr.Zero&&!SaferCloseLevel(level))Fail("SAFER_CLOSE");if(source!=IntPtr.Zero)CloseHandle(source);}
    }
    static (bool Created,int? Error,uint? Exit) Launch(IntPtr t,string exe,string cwd){var cmd=new StringBuilder().Append('"').Append(exe).Append('"').Append(" /d /c exit 0");var st=new STARTUPINFO{cb=(uint)Marshal.SizeOf<STARTUPINFO>(),lpDesktop=null};if(!CreateProcessWithToken(t,0,exe,cmd,CREATE_NO_WINDOW,IntPtr.Zero,cwd,ref st,out var pi))return(false,Marshal.GetLastWin32Error(),null);try{uint w=WaitForSingleObject(pi.hProcess,INFINITE);if(w!=WAIT_OBJECT_0)throw new Win32Exception(Marshal.GetLastWin32Error(),$"WAIT:{w}");if(!GetExitCodeProcess(pi.hProcess,out uint e))Fail("EXIT");return(true,null,e);}finally{if(pi.hThread!=IntPtr.Zero)CloseHandle(pi.hThread);if(pi.hProcess!=IntPtr.Zero)CloseHandle(pi.hProcess);}}
    static bool Bool(IntPtr t,int c){IntPtr b=Marshal.AllocHGlobal(4);try{Marshal.WriteInt32(b,0);if(!GetTokenInformation(t,c,b,4,out uint r))Fail("BOOL");if(r!=1&&r!=4)throw new InvalidOperationException($"BOOL_SIZE:{r}");return Marshal.ReadByte(b)!=0;}finally{Marshal.FreeHGlobal(b);}}
    static int Dword(IntPtr t,int c){IntPtr b=Marshal.AllocHGlobal(4);try{if(!GetTokenInformation(t,c,b,4,out uint r))Fail("DWORD");if(r!=4)throw new InvalidOperationException($"DWORD_SIZE:{r}");return Marshal.ReadInt32(b);}finally{Marshal.FreeHGlobal(b);}}
    static uint Integrity(IntPtr t){GetTokenInformation(t,TokenIntegrityLevel,IntPtr.Zero,0,out uint n);int e=Marshal.GetLastWin32Error();if(n==0||e!=ERROR_INSUFFICIENT_BUFFER)throw new Win32Exception(e,$"IL_SIZE:{n}");IntPtr b=Marshal.AllocHGlobal((int)n);try{if(!GetTokenInformation(t,TokenIntegrityLevel,b,n,out _))Fail("IL");var l=Marshal.PtrToStructure<TOKEN_MANDATORY_LABEL>(b);IntPtr cp=GetSidSubAuthorityCount(l.Label.Sid);byte c=Marshal.ReadByte(cp);IntPtr rp=GetSidSubAuthority(l.Label.Sid,(uint)(c-1));return unchecked((uint)Marshal.ReadInt32(rp));}finally{Marshal.FreeHGlobal(b);}}
    static void Fail(string s)=>throw new Win32Exception(Marshal.GetLastWin32Error(),s);
}
'@
$full=[IO.Path]::GetFullPath($Receipt);if(Test-Path $full){throw 'create-only receipt required'};New-Item -ItemType Directory -Force -Path (Split-Path -Parent $full)|Out-Null
$o=[SaferLuaWithTokenProbe]::Run((Get-Location).Path);$ok=(-not $o.LuaElevated)-and$o.LuaHasRestrictions-and$o.ProcessCreated-and$o.ExitCode-eq 0;$status=if($ok){'SAFER_LUA_WITH_TOKEN_LAUNCHABLE'}elseif($o.ProcessCreated){'SAFER_LUA_WITH_TOKEN_PROCESS_FAIL_CLOSED'}else{'SAFER_LUA_WITH_TOKEN_CREATE_FAILED'}
$r=[ordered]@{schema='matawaka.windows-safer-lua-with-token-diagnostic/v0.1';status=$status;saferElevated=$o.SaferElevated;saferHasRestrictions=$o.SaferHasRestrictions;saferIntegrityRid=[uint32]$o.SaferIntegrityRid;luaElevated=$o.LuaElevated;luaHasRestrictions=$o.LuaHasRestrictions;luaElevationType=$o.LuaElevationType;luaIntegrityRid=[uint32]$o.LuaIntegrityRid;processCreated=$o.ProcessCreated;createError=$o.CreateError;exitCodeUnsigned=$o.ExitCode;credentialUsed=$false;userChanged=$false;saferPolicyMutated=$false;firewallRuleMutated=$false;networkIsolationConfigMutated=$false;globalPolicyMutated=$false;productionProviderRegistered=$false;proofClaimed=$false}
$r|ConvertTo-Json -Depth 4|Set-Content $full -Encoding utf8NoBOM;$r|ConvertTo-Json -Compress|Write-Host;if(-not $ok){exit 3};exit 0
