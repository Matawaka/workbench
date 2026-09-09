"""Build-only successor materialization. Never execute an operator tool or change original sources."""
import hashlib, json, pathlib, subprocess, sys
here=pathlib.Path(__file__).resolve().parent
out=pathlib.Path(sys.argv[1]).resolve()
subprocess.run([sys.executable,str(here.parent/'permission-recheck-v1/prepare.py'),str(out),sys.argv[2]],check=True)
def blob(b):return hashlib.sha1(b'blob '+str(len(b)).encode()+b'\0'+b).hexdigest()
def text(n):return (out/n).read_text(encoding='utf-8')
def write(n,s): (out/n).write_text(s,encoding='utf-8',newline='\n')
publisher=(out/'Publisher.cs').read_bytes()
assert blob(publisher)=='de4c99c9f95e75c2727472c233079a011627a7a0','ORIGINAL_PUBLISHER_DRIFT'
assert blob((out/'BoundedProbe.cs').read_bytes())=='8810bed3a9d84ef37e9491bd7b71e0a20cd5b7ec','BOUNDED_PROCESS_SOURCE_DRIFT'
assert blob((out/'PermissionRecheck.cs').read_bytes())=='9f365044272f298a0583a201d4484630d595fe66','PERMISSION_RECHECK_SOURCE_DRIFT'
s=publisher.decode('utf-8')
# Reuse immutable transport construction, exact hook, parser and config. Replace ONLY the
# native process driver with the separately qualified bounded capture. No old orchestrator.
plan='''internal sealed record PublishPlan(ProofSpec Proof, string GitExe, string Endpoint,
    string StageRoot, string OutputRoot, BoundFile Preflight, string GitRoot)
{
    internal const string MainRef = "refs/heads/main";
    internal const string TagRef = "refs/tags/workbench-v0.60.1-accepted";
    internal const string HistoricalRef = "refs/tags/workbench-v0.60-accepted";
}
'''
core=s[:s.index('// This assembly')].replace('namespace Matawaka.V0601FixedPublisher;', 'using Matawaka.V0601DiscoveryDiagnostic;\nusing Matawaka.V0601ReconciledPublisher;\nnamespace Matawaka.V0601FixedPublisher;')+plan+s[s.index('internal sealed record RemoteState'):s.index('internal static class FixedPublisher')]
a=core.index('    private async Task<NativeResult> Native(')
b=core.index('    internal static RemoteState Parse(',a)
core=core[:a]+'''    internal NativeObservation? LastObservation { get; private set; }
    private async Task<NativeResult> Native(ProcessStartInfo psi, bool push)
    {
        Safe.Need(Safe.Hash(Safe.Read(plan.GitExe,32*1024*1024)) == gitSha,"GIT_IMAGE_DRIFT");
        if(push) PushMayHaveStarted = true;
        var capture = await BoundedProcess.Run(psi, Array.Empty<byte>(), maximum:512*1024, deadlineMs:30000);
        LastObservation = NativeObservation.From(capture, push ? "EXACT_ATOMIC_PUSH" : "PUBLIC_REF_READ");
        return new(capture.ExitCode ?? -1, capture.Output, capture.Error);
    }
'''+core[b:]
assert all(x not in core for x in ['class FixedPublisher','class PublisherEntryPoint','PUBLISH-EXACT-V0601-58B9430-C07409C','WaitForExitAsync()'])
write('GeneratedPublicationCore.cs',core)
d=text('Diagnostic.cs'); read=d[:d.index('internal static class Diagnostic\n')]
# Empty context constructor is used only by disposable fixtures; no historical Execute/Main.
constructor=d[d.index('    internal static void CreateStage('):d.index('    internal static async Task<string> Execute(')]
write('IncidentReadSupport.cs',read+'internal static class Diagnostic\n{\n'+constructor+'}\n')
r=text('PermissionRecheck.cs');write('PermissionReadSupport.cs',r[:r.index('internal static class PermissionRecheck\n')])
f=text('PermissionRecheckTests.cs'); pre=f[:f.index('internal static class PermissionRecheckTests')]
fixture=f[f.index('    private sealed class F : IDisposable'):f.index('    private static Capture Capture(')]
write('RecheckFixture.cs',pre+fixture.replace('private sealed class F : IDisposable','internal sealed class RecheckFixture : IDisposable',1))
n=text('NetworkTests.cs');write('SuccessorHttpsFixture.cs',n[:n.index('    private static async Task Case(')].replace('internal static class NetworkTests','internal static partial class SuccessorHttps',1)+'}\n')
for name in ['ReconciledPublisher.cs','SuccessorTests.cs','SuccessorHttpsTests.cs']:
    (out/name).write_bytes((here/name).read_bytes().replace(b'\r\n',b'\n'))
common=['GeneratedVerifier.cs','V2.cs','GeneratedPublicationCore.cs','BoundedProbe.cs','IncidentReadSupport.cs','PermissionReadSupport.cs','ReconciledPublisher.cs']
tests=['LegacySourceTests.cs','LegacyEvidenceTests.cs','V2Tests.cs','RecheckFixture.cs','SuccessorTests.cs']
def project(name,start,extra=(),qualification=False):
    files=common+list(extra)
    write(name+'.csproj','<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems><TreatWarningsAsErrors>true</TreatWarningsAsErrors><AssemblyName>'+name+'</AssemblyName><StartupObject>'+start+'</StartupObject>'+('<DefineConstants>QUALIFICATION</DefineConstants>' if qualification else '')+'</PropertyGroup><ItemGroup>'+''.join('<Compile Include="'+f+'"/>' for f in files)+'<EmbeddedResource Include="mingit-files.json" LogicalName="mingit-files.json"/></ItemGroup></Project>')
project('Matawaka.Workbench.V0601ReconciledPublisher','Matawaka.V0601ReconciledPublisher.SuccessorEntryPoint')
project('SuccessorTests','Matawaka.V0601ReconciledPublisher.SuccessorTests',tests,True)
project('SuccessorHttpsTests','Matawaka.V0601FixedPublisher.SuccessorHttps',tests+['SuccessorHttpsFixture.cs','SuccessorHttpsTests.cs'],True)
write('successor-materialization.json',json.dumps({'OriginalPublisherGitBlob':blob(publisher),'OriginalFilesChanged':False,'OldPublisherEntryPointCompiled':False,'OldDiscoveryExecuteCompiled':False,'OldPermissionExecuteCompiled':False,'NativeCaptureReplacedByPinnedBoundedProcess':True,'ProductionCliOverrides':False,'ProductionCompileFiles':common,'GeneratedCoreSha256':hashlib.sha256(core.encode()).hexdigest()},indent=2)+'\n')
print('MATERIALIZED_INCIDENT_LINKED_EXACT_PUBLICATION_SUCCESSOR',out)
