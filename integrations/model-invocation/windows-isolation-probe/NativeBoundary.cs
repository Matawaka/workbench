using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Workbench.IsolationQualification;

internal sealed class BoundaryFailure(string stage, int nativeCode = 0) : Exception(stage)
{
    internal int NativeCode { get; } = nativeCode;
}

internal sealed record TokenObservation(bool AppContainer, int Capabilities, bool PackageMatches,
    bool LowIntegrity, bool Elevated, bool InOwnedJob);

/// <summary>Qualification-only AppContainer boundary. Not registered as a model host or lease provider.</summary>
internal sealed class NativeBoundary : IDisposable
{
    private nint packageSid, job, process, thread;
    private string? profile;
    private string? preparedRoot;
    private bool profileOwned, resumed, disposed, processCreated;
    internal bool ProfileRemoved { get; private set; }
    internal bool ProcessExited { get; private set; }
    internal uint? ExitCode { get; private set; }
    internal TokenObservation? Token { get; private set; }
    internal bool JobLimitsVerified { get; private set; }
    private readonly List<FileStream> pinnedFiles = [];
    private FileStream? stdout, stderr;
    private nint parentOut, parentErr, childOut, childErr, childIn, parentIn;
    private readonly Stopwatch lifetime = Stopwatch.StartNew();
    internal Stream Output => stdout ?? throw new BoundaryFailure("NO_OUTPUT_PIPE");
    internal Stream Error => stderr ?? throw new BoundaryFailure("NO_ERROR_PIPE");
    internal bool CleanupSucceeded => (!profileOwned || ProfileRemoved) && (!processCreated || ProcessExited);
    internal bool ProfileCreated => profileOwned;
    internal bool ProcessCreated => processCreated;

    internal static void Need(bool ok, string stage) { if (!ok) throw new BoundaryFailure(stage); }
    private static void Win(bool ok, string stage) { if (!ok) throw new BoundaryFailure(stage, Marshal.GetLastWin32Error()); }
    internal static void ValidateToken(TokenObservation t) => Need(t.AppContainer && t.Capabilities == 0 &&
        t.PackageMatches && t.LowIntegrity && !t.Elevated && t.InOwnedJob, "TOKEN_BOUNDARY_REFUSED");
    internal static void ValidateJob(uint flags,uint active,nuint memory)=>Need(flags==(0x8|0x100|0x400|0x2000)&&active==1&&memory==512*1024*1024,"JOB_LIMITS_REFUSED");

    internal static string ValidateRoot(string directory)
    {
        Need(Path.IsPathFullyQualified(directory) && !directory.StartsWith("\\\\", StringComparison.Ordinal), "ROOT_LOCAL_REQUIRED");
        var root = Path.GetFullPath(directory);
        Need(Directory.Exists(root) && new DirectoryInfo(root).Name == "trial", "TRIAL_ROOT_REQUIRED");
        foreach (var part in Walk(root)) Need((File.GetAttributes(part) & FileAttributes.ReparsePoint) == 0, "REPARSE_REFUSED");
        return root;
    }
    private static IEnumerable<string> Walk(string path)
    {
        for (var p = path; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p)) yield return p;
    }

    internal void Prepare(string directory, IReadOnlyDictionary<string,string> hashes)
    {
        Need(OperatingSystem.IsWindows() && Environment.Is64BitProcess, "WINDOWS_X64_REQUIRED");
        Need(profile is null && !disposed, "BOUNDARY_SINGLE_USE");
        string root = ValidateRoot(directory);
        Need(new DirectoryInfo(Path.GetDirectoryName(root)!).Name.StartsWith("windows-isolation-qualification-",StringComparison.Ordinal),"QUALIFICATION_PARENT_REQUIRED");
        string[] expected = ["Workbench.AppContainerProbe.exe", "Workbench.AppContainerProbe.dll",
            "Workbench.AppContainerProbe.runtimeconfig.json", "Workbench.AppContainerProbe.deps.json"];
        Need(hashes.Count == expected.Length && hashes.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expected), "BUNDLE_KEYS");
        var allNames=expected.Concat(new[]{"allow-read.txt","deny-read.txt","deny-write.txt"}).ToHashSet(StringComparer.Ordinal);
        Need(Directory.EnumerateFileSystemEntries(root).Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal).SetEquals(allNames),"TRIAL_CLOSED_FILES");
        foreach(var name in allNames)
            Need((File.GetAttributes(Path.Combine(root,name))&(FileAttributes.ReparsePoint|FileAttributes.Directory))==0,"TRIAL_FILE_REPARSE_REFUSED");
        Need(File.ReadAllText(Path.Combine(root,"allow-read.txt"))=="SYNTHETIC_ALLOWED"&&
            File.ReadAllText(Path.Combine(root,"deny-read.txt"))=="SYNTHETIC_DENIED"&&
            File.ReadAllText(Path.Combine(root,"deny-write.txt"))=="SYNTHETIC_UNCHANGED","SYNTHETIC_SENTINELS_REQUIRED");
        var owner = WindowsIdentity.GetCurrent().User ?? throw new BoundaryFailure("OWNER_UNAVAILABLE");
        // Preparation and execution must belong to the SAME account. Never take ownership
        // of a foreign pre-existing object or create a profile before this read-only guard.
        Need(Equals(new DirectoryInfo(root).GetAccessControl().GetOwner(typeof(SecurityIdentifier)),owner),"TRIAL_OWNER_MISMATCH");
        foreach(var name in expected.Concat(new[]{"allow-read.txt","deny-read.txt","deny-write.txt"}))
            Need(Equals(new FileInfo(Path.Combine(root,name)).GetAccessControl().GetOwner(typeof(SecurityIdentifier)),owner),"TRIAL_FILE_OWNER_MISMATCH");
        foreach (var name in expected)
        {
            var path = Path.Combine(root, name);
            Need(File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "BUNDLE_FILE");
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            pinnedFiles.Add(stream);
            Need(stream.Length > 0 && stream.Length < 2_000_000, "BUNDLE_SIZE");
            Need(Convert.ToHexString(SHA256.HashData(stream)).Equals(hashes[name], StringComparison.OrdinalIgnoreCase), "BUNDLE_HASH");
        }
        // Fresh per-trial name. Existing profiles are NOT adopted or modified.
        profile = "Matawaka.IsolationProbe." + Guid.NewGuid().ToString("N");
        int hr = CreateAppContainerProfile(profile, "Workbench isolated test", "Temporary no-model qualification",
            0, 0, out packageSid);
        if (hr != 0) throw new BoundaryFailure("CREATE_PROFILE_REFUSED", hr);
        profileOwned = true;
        Need(packageSid != 0, "PROFILE_SID_ABSENT");
        var package = new SecurityIdentifier(packageSid);
        // Only the newly created trial directory is changed. No parent or installed-runtime ACL is touched.
        var acl = new DirectoryInfo(root).GetAccessControl();
        acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(package, FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        try { new DirectoryInfo(root).SetAccessControl(acl); }
        catch(UnauthorizedAccessException e){throw new BoundaryFailure("TRIAL_DACL_WRITE_REFUSED",e.HResult);}
        var forbidden = new FileInfo(Path.Combine(root,"deny-read.txt"));
        Need(forbidden.Exists, "DENY_SENTINEL_ABSENT");
        var fileAcl = forbidden.GetAccessControl(); fileAcl.SetAccessRuleProtection(true,false);
        fileAcl.AddAccessRule(new FileSystemAccessRule(owner,FileSystemRights.FullControl,AccessControlType.Allow));
        try { forbidden.SetAccessControl(fileAcl); }
        catch(UnauthorizedAccessException e){throw new BoundaryFailure("DENY_SENTINEL_DACL_WRITE_REFUSED",e.HResult);}
        var observedAcl = new DirectoryInfo(root).GetAccessControl();
        Need(observedAcl.AreAccessRulesProtected && Equals(observedAcl.GetOwner(typeof(SecurityIdentifier)),owner), "TRIAL_ACL_REFUSED");
        var rules=observedAcl.GetAccessRules(true,true,typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        Need(rules.Length==2 && rules.All(r=>!r.IsInherited && r.AccessControlType==AccessControlType.Allow &&
            r.InheritanceFlags==(InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit) && r.PropagationFlags==PropagationFlags.None) &&
            rules.Any(r=>r.IdentityReference.Equals(owner)&&r.FileSystemRights==FileSystemRights.FullControl) &&
            rules.Any(r=>r.IdentityReference.Equals(package)&&r.FileSystemRights==(FileSystemRights.ReadAndExecute|FileSystemRights.Synchronize)),"TRIAL_ACL_RULES");
        preparedRoot=root;
    }

    internal void Start(string directory, int loopbackPort)
    {
        Need(profileOwned && !resumed && !disposed && process == 0, "START_STATE");
        Need(loopbackPort is > 0 and <= 65535 && lifetime.Elapsed < TimeSpan.FromSeconds(30), "START_BOUND");
        var root = ValidateRoot(directory);
        Need(preparedRoot is not null&&string.Equals(root,preparedRoot,StringComparison.OrdinalIgnoreCase),"PREPARED_ROOT_MISMATCH");
        var executable = Path.Combine(root,"Workbench.AppContainerProbe.exe");
        var sa = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Inherit = true };
        Win(CreatePipe(out childIn,out parentIn,ref sa,0),"STDIN_PIPE");
        Win(CreatePipe(out parentOut,out childOut,ref sa,0),"STDOUT_PIPE");
        Win(CreatePipe(out parentErr,out childErr,ref sa,0),"STDERR_PIPE");
        foreach (var h in new[]{parentIn,parentOut,parentErr}) Win(SetHandleInformation(h,1,0),"PIPE_INHERITANCE");
        using var attrs = new AttributeList(3);
        var caps = new SecurityCapabilities { AppContainerSid = packageSid };
        attrs.Add(0x20009, caps); // PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES, zero capabilities.
        attrs.AddHandles(0x20002, [childIn,childOut,childErr]); // Exact inherited-handle allowlist.
        attrs.Add(0x2000e, 1u); // PROCESS_CREATION_CHILD_PROCESS_RESTRICTED, not an arbitrary child launcher.
        var si = new StartupInfoEx { StartupInfo = new StartupInfo {
            Size = Marshal.SizeOf<StartupInfoEx>(), Flags=0x100,
            StdInput=childIn,StdOutput=childOut,StdError=childErr }, Attributes=attrs.Pointer };
        // Only fixed test mode + numeric owned loopback port. No caller commands, scripts or model arguments.
        var command = new StringBuilder('"'+executable+'"'+" --child "+loopbackPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        // Windows needs its ordinary user-directory variables for AppContainer setup.
        // Bind them to the NEW container's own OS-created folder, never inherit parent secrets.
        Win(ConvertSidToStringSid(packageSid,out nint sidText),"PACKAGE_SID_TEXT");
        string containerFolder;
        try
        {
            int hr=GetAppContainerFolderPath(Marshal.PtrToStringUni(sidText)!,out nint folder);
            if(hr!=0)throw new BoundaryFailure("CONTAINER_FOLDER_QUERY",hr);
            try{containerFolder=Marshal.PtrToStringUni(folder)??throw new BoundaryFailure("CONTAINER_FOLDER_ABSENT");}
            finally{Marshal.FreeCoTaskMem(folder);}
        }
        finally{LocalFree(sidText);}
        Need(Path.IsPathFullyQualified(containerFolder)&&Directory.Exists(containerFolder),"CONTAINER_FOLDER_INVALID");
        var variables = new SortedDictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
            ["COMPlus_EnableDiagnostics"]="0",["DOTNET_EnableDiagnostics"]="0",["DOTNET_MULTILEVEL_LOOKUP"]="0",
            ["DOTNET_ROOT"]=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)+"\\dotnet",
            ["SystemRoot"]=systemRoot,["WINDIR"]=systemRoot,["SystemDrive"]=Path.GetPathRoot(systemRoot)!.TrimEnd('\\'),
            ["USERPROFILE"]=containerFolder,["APPDATA"]=containerFolder,["LOCALAPPDATA"]=containerFolder,
            ["TEMP"]=containerFolder,["TMP"]=containerFolder };
        var environment=string.Join('\0',variables.Select(p=>p.Key+"="+p.Value))+"\0\0";
        nint env = Marshal.StringToHGlobalUni(environment);
        ProcessInformation pi;
        try
        {
            // DETACHED_PROCESS: no inherited or newly allocated console. Standard
            // streams remain the exact pipe handles supplied in STARTUPINFOEX.
            // This is a fixed test variant, never a fallback outside AppContainer.
            Win(CreateProcess(executable,command,0,0,true,0x00080000|0x00000400|0x00000004|0x00000008,
                env,root,ref si,out pi),"CREATE_APPCONTAINER_PROCESS_REFUSED");
        }
        finally { Marshal.FreeHGlobal(env); }
        process=pi.Process; thread=pi.Thread; processCreated=true;
        job=CreateJobObject(0,null); Win(job!=0,"CREATE_JOB");
        var limits=new ExtendedLimits { Basic=new BasicLimits { Flags=0x8|0x100|0x400|0x2000,ActiveProcesses=1 },
            ProcessMemory=(nuint)(512*1024*1024) };
        nint limit=Marshal.AllocHGlobal(Marshal.SizeOf<ExtendedLimits>());
        try
        {
            Marshal.StructureToPtr(limits,limit,false);
            Win(SetInformationJobObject(job,9,limit,(uint)Marshal.SizeOf<ExtendedLimits>()),"SET_JOB_LIMITS");
            Win(QueryInformationJobObject(job,9,limit,(uint)Marshal.SizeOf<ExtendedLimits>(),out uint returned),"QUERY_JOB_LIMITS");
            Need(returned==(uint)Marshal.SizeOf<ExtendedLimits>(),"JOB_LIMITS_SHAPE");
            var observed=Marshal.PtrToStructure<ExtendedLimits>(limit);
            ValidateJob(observed.Basic.Flags,observed.Basic.ActiveProcesses,observed.ProcessMemory);
            JobLimitsVerified=true;
        }
        finally { Marshal.FreeHGlobal(limit); }
        Win(AssignProcessToJobObject(job,process),"ASSIGN_JOB_BEFORE_RESUME");
        Win(IsProcessInJob(process,job,out bool inJob),"QUERY_OWN_JOB");
        Win(OpenProcessToken(process,8,out nint token),"QUERY_CHILD_TOKEN");
        try
        {
            using var app=QueryToken(token,29);
            using var capabilities=QueryToken(token,30);
            using var sid=QueryToken(token,31);
            using var integrity=QueryToken(token,25);
            using var elevated=QueryToken(token,20);
            Need(app.Size>=4 && capabilities.Size>=4 && sid.Size>=IntPtr.Size && integrity.Size>=IntPtr.Size && elevated.Size>=4,"TOKEN_SHAPE");
            Win(ConvertStringSidToSid("S-1-16-4096",out nint low),"LOW_SID");
            try { Token=new(Marshal.ReadInt32(app.Pointer)==1,Marshal.ReadInt32(capabilities.Pointer),
                Marshal.ReadIntPtr(sid.Pointer)!=0&&EqualSid(Marshal.ReadIntPtr(sid.Pointer),packageSid),
                Marshal.ReadIntPtr(integrity.Pointer)!=0&&EqualSid(Marshal.ReadIntPtr(integrity.Pointer),low),
                Marshal.ReadInt32(elevated.Pointer)!=0,inJob); }
            finally { LocalFree(low); }
            ValidateToken(Token);
        }
        finally { CloseHandle(token); }
        var image=new StringBuilder(32768);uint length=(uint)image.Capacity;
        Win(QueryFullProcessImageName(process,0,image,ref length),"PROCESS_IMAGE_QUERY");
        Need(string.Equals(Path.GetFullPath(image.ToString()),executable,StringComparison.OrdinalIgnoreCase),"PROCESS_IMAGE_MISMATCH");
        Close(ref parentIn); // Exact EOF: no model prompt or arbitrary input can be sent.
        Close(ref childIn);Close(ref childOut);Close(ref childErr);
        stdout=new FileStream(new SafeFileHandle(parentOut,true),FileAccess.Read,4096,false);parentOut=0;
        stderr=new FileStream(new SafeFileHandle(parentErr,true),FileAccess.Read,4096,false);parentErr=0;
        Win(ResumeThread(thread)!=uint.MaxValue,"RESUME_THREAD"); resumed=true;
    }

    internal bool Wait(uint milliseconds)
    {
        Need(process!=0 && milliseconds<=15000,"WAIT_BOUND");
        uint result=WaitForSingleObject(process,milliseconds);
        if(result==0) { Win(GetExitCodeProcess(process,out uint code),"EXIT_CODE");ExitCode=code;ProcessExited=true;return true; }
        if(result==258)return false;
        throw new BoundaryFailure("PROCESS_WAIT_FAILED",Marshal.GetLastWin32Error());
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        if(process!=0&&!ProcessExited)
        {
            if(job!=0) _=TerminateJobObject(job,90);
            _=TerminateProcess(process,90); // Exact owned handle, also covers failure before Job assignment.
            if(WaitForSingleObject(process,5000)==0) {ProcessExited=true;if(GetExitCodeProcess(process,out uint code))ExitCode=code;}
        }
        Close(ref thread);Close(ref job);
        stdout?.Dispose();stderr?.Dispose();
        foreach(var stream in pinnedFiles)stream.Dispose();
        Close(ref parentIn);Close(ref childIn);Close(ref parentOut);Close(ref childOut);Close(ref parentErr);Close(ref childErr);
        // Do not delete an active profile. Retain/report if termination could not be verified.
        if(profileOwned && (process==0||ProcessExited)) ProfileRemoved=DeleteAppContainerProfile(profile!)==0;
        Close(ref process);
        if(packageSid!=0){FreeSid(packageSid);packageSid=0;}
    }
    private static void Close(ref nint h){if(h!=0){CloseHandle(h);h=0;}}
    private static NativeMemory QueryToken(nint token,int kind)
    {
        // TOKEN_ELEVATION is a fixed DWORD structure. Query it with its exact
        // buffer; some hosts return ERROR_BAD_LENGTH for the zero-buffer probe.
        if(kind==20)
        {
            var fixedBuffer=new NativeMemory(sizeof(uint));
            try
            {
                Win(GetTokenInformation(token,kind,fixedBuffer.Pointer,sizeof(uint),out uint length),"TOKEN_ELEVATION_QUERY");
                Need(length==sizeof(uint),"TOKEN_ELEVATION_SHAPE");return fixedBuffer;
            }
            catch{fixedBuffer.Dispose();throw;}
        }
        bool initial=GetTokenInformation(token,kind,0,0,out uint needed);
        int sizeError=Marshal.GetLastWin32Error();
        if(initial||sizeError!=122||needed is 0 or >16384)
            throw new BoundaryFailure("TOKEN_SIZE_QUERY_"+kind.ToString(System.Globalization.CultureInfo.InvariantCulture),sizeError);
        var b=new NativeMemory((int)needed);
        try {Win(GetTokenInformation(token,kind,b.Pointer,needed,out uint written),"TOKEN_QUERY");Need(written<=needed,"TOKEN_SIZE_CHANGED");return b;}
        catch{b.Dispose();throw;}
    }
    private sealed class NativeMemory(int size) : IDisposable
    { internal int Size{get;}=size;internal nint Pointer{get;}=Marshal.AllocHGlobal(size);public void Dispose()=>Marshal.FreeHGlobal(Pointer); }
    private sealed class AttributeList : IDisposable
    {
        internal nint Pointer {get;}
        private readonly List<nint> values=[];
        internal AttributeList(int count)
        {
            nuint size=0;_=InitializeProcThreadAttributeList(0,count,0,ref size);
            Need(Marshal.GetLastWin32Error()==122 && size>0&&size<65536,"ATTRIBUTE_SIZE");
            Pointer=Marshal.AllocHGlobal((nint)size);
            try{Win(InitializeProcThreadAttributeList(Pointer,count,0,ref size),"ATTRIBUTE_INIT");}
            catch{Marshal.FreeHGlobal(Pointer);throw;}
        }
        internal void Add<T>(nuint key,T value) where T:struct
        {var n=Marshal.SizeOf<T>();var p=Marshal.AllocHGlobal(n);values.Add(p);Marshal.StructureToPtr(value,p,false);Win(UpdateProcThreadAttribute(Pointer,0,key,p,(nuint)n,0,0),"ATTRIBUTE_SET");}
        internal void AddHandles(nuint key,nint[] handles)
        {var p=Marshal.AllocHGlobal(handles.Length*IntPtr.Size);values.Add(p);Marshal.Copy(handles,0,p,handles.Length);Win(UpdateProcThreadAttribute(Pointer,0,key,p,(nuint)(handles.Length*IntPtr.Size),0,0),"HANDLE_ALLOWLIST");}
        public void Dispose(){DeleteProcThreadAttributeList(Pointer);foreach(var p in values)Marshal.FreeHGlobal(p);Marshal.FreeHGlobal(Pointer);}
    }

    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes{internal int Length;internal nint Descriptor;[MarshalAs(UnmanagedType.Bool)]internal bool Inherit;}
    [StructLayout(LayoutKind.Sequential)] private struct SecurityCapabilities{internal nint AppContainerSid,Capabilities;internal uint Count,Reserved;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct StartupInfo{internal int Size;internal nint Reserved,Desktop,Title;internal uint X,Y,XSize,YSize,XCount,YCount,Fill,Flags;internal ushort Show,Reserved2;internal nint ReservedPointer,StdInput,StdOutput,StdError;}
    [StructLayout(LayoutKind.Sequential)]private struct StartupInfoEx{internal StartupInfo StartupInfo;internal nint Attributes;}
    [StructLayout(LayoutKind.Sequential)]private struct ProcessInformation{internal nint Process,Thread;internal uint ProcessId,ThreadId;}
    [StructLayout(LayoutKind.Sequential)]private struct BasicLimits{internal long ProcessTime,JobTime;internal uint Flags;internal nuint MinWorking,MaxWorking;internal uint ActiveProcesses;internal nuint Affinity;internal uint Priority,Scheduling;}
    [StructLayout(LayoutKind.Sequential)]private struct IoCounters{internal ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)]private struct ExtendedLimits{internal BasicLimits Basic;internal IoCounters Io;internal nuint ProcessMemory,JobMemory,PeakProcess,PeakJob;}
    [DllImport("userenv.dll",CharSet=CharSet.Unicode)]private static extern int CreateAppContainerProfile(string name,string display,string description,nint caps,uint count,out nint sid);
    [DllImport("userenv.dll",CharSet=CharSet.Unicode)]private static extern int DeleteAppContainerProfile(string name);
    [DllImport("userenv.dll",CharSet=CharSet.Unicode)]private static extern int GetAppContainerFolderPath(string sid,out nint path);
    [DllImport("advapi32.dll")]private static extern nint FreeSid(nint sid);
    [DllImport("kernel32.dll")]private static extern nint LocalFree(nint p);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool CreatePipe(out nint r,out nint w,ref SecurityAttributes sa,uint size);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetHandleInformation(nint h,uint mask,uint flags);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool InitializeProcThreadAttributeList(nint list,int count,uint flags,ref nuint size);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool UpdateProcThreadAttribute(nint list,uint flags,nuint key,nint value,nuint size,nint previous,nint returned);
    [DllImport("kernel32.dll")]private static extern void DeleteProcThreadAttributeList(nint list);
    [DllImport("kernel32.dll",EntryPoint="CreateProcessW",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool CreateProcess(string app,StringBuilder cmd,nint pa,nint ta,bool inherit,uint flags,nint env,string cwd,ref StartupInfoEx si,out ProcessInformation pi);
    [DllImport("kernel32.dll",EntryPoint="CreateJobObjectW",CharSet=CharSet.Unicode,SetLastError=true)]private static extern nint CreateJobObject(nint attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetInformationJobObject(nint job,int kind,nint data,uint size);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool QueryInformationJobObject(nint job,int kind,nint data,uint size,out uint returned);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool AssignProcessToJobObject(nint job,nint process);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool IsProcessInJob(nint process,nint job,out bool result);
    [DllImport("advapi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool OpenProcessToken(nint process,uint access,out nint token);
    [DllImport("advapi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetTokenInformation(nint token,int kind,nint data,uint size,out uint returned);
    [DllImport("advapi32.dll",EntryPoint="ConvertStringSidToSidW",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool ConvertStringSidToSid(string text,out nint sid);
    [DllImport("advapi32.dll",EntryPoint="ConvertSidToStringSidW",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool ConvertSidToStringSid(nint sid,out nint text);
    [DllImport("advapi32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool EqualSid(nint a,nint b);
    [DllImport("kernel32.dll",EntryPoint="QueryFullProcessImageNameW",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool QueryFullProcessImageName(nint process,uint flags,StringBuilder path,ref uint length);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern uint ResumeThread(nint thread);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern uint WaitForSingleObject(nint h,uint ms);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetExitCodeProcess(nint process,out uint code);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool TerminateJobObject(nint job,uint code);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool TerminateProcess(nint process,uint code);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool CloseHandle(nint h);
}
