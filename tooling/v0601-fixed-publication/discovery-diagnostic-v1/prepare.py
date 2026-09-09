"""Build-only no-push diagnostic. Preserve original publisher and all historical verifiers."""
import hashlib, json, pathlib, subprocess, sys, zipfile
here = pathlib.Path(__file__).resolve().parent
out = pathlib.Path(sys.argv[1]).resolve()
subprocess.run([sys.executable, str(here.parent/'publisher-fixed-v1/prepare.py'), str(out), sys.argv[2]], check=True)
publisher = (out/'Publisher.cs').read_bytes()
def blob(b): return hashlib.sha1(b'blob '+str(len(b)).encode()+b'\0'+b).hexdigest()
assert blob(publisher) == 'de4c99c9f95e75c2727472c233079a011627a7a0', 'ORIGINAL_PUBLISHER_DRIFT'
s = publisher.decode('utf-8')
# Copy pure file/lock/tool-manifest/hidden-input support, not IsolatedTransport, Guard,
# FixedPublisher, PublishPlan or the old entry point. The old source is never edited.
prefix = s[:s.index('// This assembly')]
support = prefix + s[s.index('internal sealed record FilePin'):s.index('internal static class Guard')]
assert all(x not in support for x in ['class FixedPublisher', 'class IsolatedTransport', 'class Guard', 'record PublishPlan'])
(out/'DiagnosticSupport.cs').write_text(support, encoding='utf-8', newline='\n')
for name in ['BoundedProbe.cs', 'Diagnostic.cs', 'DiagnosticTests.cs']:
    (out/name).write_bytes((here/name).read_bytes().replace(b'\r\n', b'\n'))
base = '''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems><TreatWarningsAsErrors>true</TreatWarningsAsErrors>{props}</PropertyGroup><ItemGroup><Compile Include="GeneratedVerifier.cs"/><Compile Include="V2.cs"/><Compile Include="DiagnosticSupport.cs"/><Compile Include="BoundedProbe.cs"/><Compile Include="Diagnostic.cs"/>{tests}<EmbeddedResource Include="mingit-files.json" LogicalName="mingit-files.json"/></ItemGroup></Project>'''
(out/'Diagnostic.csproj').write_text(base.format(props='<AssemblyName>Matawaka.Workbench.V0601ReceiveDiscoveryDiagnostic</AssemblyName><StartupObject>Matawaka.V0601DiscoveryDiagnostic.DiagnosticEntryPoint</StartupObject>', tests=''), encoding='utf-8')
(out/'DiagnosticTests.csproj').write_text(base.format(props='<AssemblyName>DiscoveryDiagnosticTests</AssemblyName><StartupObject>Matawaka.V0601DiscoveryDiagnostic.DiagnosticTests</StartupObject><DefineConstants>QUALIFICATION</DefineConstants>', tests='<Compile Include="DiagnosticTests.cs"/><Compile Include="LegacySourceTests.cs"/><Compile Include="LegacyEvidenceTests.cs"/><Compile Include="V2Tests.cs"/>'), encoding='utf-8')
manifest = {'OriginalPublisherGitBlob': blob(publisher), 'SupportSha256': hashlib.sha256(support.encode()).hexdigest(), 'PriorSourceFilesChanged': False, 'PublisherImplementationCompiledIntoDiagnostic': False, 'ProductionCliOverrides': False, 'ProductionPushImplemented': False, 'PriorAttemptRearmed': False}
(out/'diagnostic-materialization.json').write_text(json.dumps(manifest, indent=2)+'\n', encoding='utf-8', newline='\n')
print('MATERIALIZED_SEPARATE_NO_PUSH_DISCOVERY_DIAGNOSTIC', out)
