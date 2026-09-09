"""Build-only wrapper; reuse byte-identical qualified V2 verifier; never modify source or receipts."""
import hashlib, json, pathlib, subprocess, sys, zipfile
here=pathlib.Path(__file__).resolve().parent
out=pathlib.Path(sys.argv[1]).resolve()
subprocess.run([sys.executable,str(here.parent/'preflight-promisor-v2/prepare.py'),str(out)],check=True)
pins={'GeneratedVerifier.cs':'2dee5a690f573ba5ce445b0b9e9c691cb6c1f69a06a7236ac3102410e5addbdd','V2.cs':'6982e428f9be50005688d3bfeca2f6a922ba9371116ac6e8d7a7457497fb63f8','LegacyEvidenceTests.cs':'2b8e772c3b0591c2e58fdaee14318fd1ea7096cd0eab5bc1cec327e777188e4c','LegacySourceTests.cs':'0e6235bbcf842593a74ee5be12ec20a4d34719ec570c8cc6138c6545388f3346','V2Tests.cs':'9cab573d54433fabfb1d6836adec03d119156a373ffd70bc47ce43319aaec563'}
for name,sha in pins.items():
    assert hashlib.sha256((out/name).read_bytes()).hexdigest()==sha,('INHERITED_QUALIFIED_SOURCE_DRIFT',name)
# Make only a fixture class accessible to the new separately compiled fixture driver.
s=(out/'V2Tests.cs').read_text();assert s.count('private sealed class F:IDisposable')==1
(out/'V2Tests.cs').write_text(s.replace('private sealed class F:IDisposable','internal sealed class F:IDisposable'),encoding='utf-8',newline='\n')
for name in ['Publisher.cs','PublisherTests.cs']:
    (out/name).write_bytes((here/name).read_bytes().replace(b'\r\n',b'\n'))
archive=pathlib.Path(sys.argv[2]);b=archive.read_bytes()
assert len(b)==38792847 and hashlib.sha256(b).hexdigest()=='4e03f94c2ffbf70be337e005cee02661c732dbfc81031a078bda9299b9a7d644'
with zipfile.ZipFile(archive) as z:
    files=[{'Path':e.filename,'Bytes':e.file_size,'Sha256':hashlib.sha256(z.read(e)).hexdigest()} for e in z.infolist() if not e.is_dir()]
assert len(files)==365 and sum(x['Bytes'] for x in files)==93886493
(out/'mingit-files.json').write_text(json.dumps(files,indent=2)+'\n',encoding='utf-8',newline='\n')
base='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems><TreatWarningsAsErrors>true</TreatWarningsAsErrors>{props}</PropertyGroup><ItemGroup><Compile Include="GeneratedVerifier.cs"/><Compile Include="V2.cs"/><Compile Include="Publisher.cs"/>{tests}<EmbeddedResource Include="mingit-files.json" LogicalName="mingit-files.json"/></ItemGroup></Project>'
(out/'Publisher.csproj').write_text(base.format(props='<AssemblyName>Matawaka.Workbench.V0601FixedPublisher</AssemblyName><StartupObject>Matawaka.V0601FixedPublisher.PublisherEntryPoint</StartupObject>',tests=''),encoding='utf-8')
(out/'PublisherTests.csproj').write_text(base.format(props='<AssemblyName>PublisherTests</AssemblyName><StartupObject>Matawaka.V0601FixedPublisher.PublisherTests</StartupObject><DefineConstants>QUALIFICATION</DefineConstants>',tests='<Compile Include="LegacySourceTests.cs"/><Compile Include="LegacyEvidenceTests.cs"/><Compile Include="V2Tests.cs"/><Compile Include="PublisherTests.cs"/>'),encoding='utf-8')
(out/'publisher-materialization.json').write_text(json.dumps({'InheritedQualifiedSourceSha256':pins,'MinGitArchiveSha256':hashlib.sha256(b).hexdigest(),'MinGitFiles':365,'NoOriginalSourceFilesChanged':True,'ProductionCliOverrides':False},indent=2),encoding='utf-8')
print('MATERIALIZED_FIXED_PUBLISHER_SOURCE',out)
