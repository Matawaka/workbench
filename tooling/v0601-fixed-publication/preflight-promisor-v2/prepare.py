"""Deterministic build-only materialization; never edits original preflight or app source."""
import hashlib, json, pathlib, sys
root = pathlib.Path(__file__).resolve().parents[3]
here = pathlib.Path(__file__).resolve().parent
out = pathlib.Path(sys.argv[1]).resolve()
out.mkdir(parents=True, exist_ok=False)
original = root / 'tooling/v0601-fixed-publication/preflight/Program.cs'
raw = original.read_bytes().replace(b'\r\n', b'\n')
oid = hashlib.sha1(b'blob '+str(len(raw)).encode()+b'\0'+raw).hexdigest()
assert oid == '29bf0f34eeb6b1f5ba0de957c8c55b0bb486c675', ('ORIGINAL_VERIFIER_DRIFT', oid)
s = raw.decode('utf-8')
def swap(old, new):
    global s
    assert s.count(old) == 1, ('PATCH_ANCHOR_COUNT', old, s.count(old))
    s = s.replace(old, new)
swap('internal GitRead(string exe,string root){Exe=Path.GetFullPath(exe);Root=Path.GetFullPath(root);ExeSha=Safe.Hash(Safe.Read(Exe,32*1024*1024));}',
     'internal GitRead(string exe,string root,string? expectedSha=null){Exe=Path.GetFullPath(exe);Root=Path.GetFullPath(root);ExeSha=Safe.Hash(Safe.Read(Exe,32*1024*1024));Safe.Need(expectedSha is null || ExeSha==expectedSha,"PINNED_GIT_IMAGE_MISMATCH");}')
swap('"hash-object","--version"', '"hash-object","rev-list","--version"')
swap('return await Process(args,null);', 'Safe.Need(V2.Admitted(args),"NON_READ_ARGUMENT_FORM_REFUSED");return await Process(args,null);')
swap(' && !key.EndsWith(".promisor",StringComparison.OrdinalIgnoreCase)', '')
swap("var key=entry.Split('\\n')[0];", "var key=entry.Split('\\n')[0]; V2.CheckConfigEntry(entry);")
swap('".git/objects/info/alternates"', '".git/objects/info/alternates",".git/objects/info/http-alternates",".git/info/attributes"')
swap('CreateNoWindow=true};', 'CreateNoWindow=true,StandardInputEncoding=new UTF8Encoding(false)};')
swap('foreach(var a in new[]{"--no-optional-locks",', '''psi.Environment["GIT_ALLOW_PROTOCOL"]="";
        psi.Environment["GIT_PROTOCOL_FROM_USER"]="0";
        foreach(var a in new[]{"--no-lazy-fetch","--no-replace-objects","--literal-pathspecs","-c","protocol.allow=never","-c","protocol.https.allow=never","-c","protocol.http.allow=never","-c","protocol.ssh.allow=never","-c","protocol.git.allow=never","-c","protocol.file.allow=never","-c","protocol.ext.allow=never","--no-optional-locks",''')
swap('process.StandardInput.Close();', 'process.StandardInput.BaseStream.Close();')
# Retire the old CLI only in the generated successor. Its six-evidence inspector stays inherited.
assert s.count('internal static class Program\n') == 1
s = s.split('internal static class Program\n')[0]
(out/'GeneratedVerifier.cs').write_text(s, encoding='utf-8', newline='\n')
(out/'V2.cs').write_bytes((here/'V2.cs').read_bytes().replace(b'\r\n', b'\n'))
# Versioned test copies: keep the original suites untouched in the repository.
for src, name, klass in [('preflight-tests','LegacySourceTests.cs','Tests'),('evidence-tests','LegacyEvidenceTests.cs','EvidenceTests')]:
    text=(root/f'tooling/v0601-fixed-publication/{src}/Program.cs').read_text(encoding='utf-8')
    text=text.replace('private static async Task Main()', 'internal static async Task Run()')
    if klass=='Tests':
        old='await Refused("promisor-config-refused",f=>G(f.Root,"config","remote.origin.promisor","true"));'
        assert old in text
        text=text.replace(old,'await Pass("complete-promisor-config-supported-read-only",async()=>{using var f=new Fixture();G(f.Root,"config","remote.origin.promisor","true");await f.Read();});')
    else:
        text=text.replace('private sealed class Fixture:IDisposable','internal sealed class Fixture:IDisposable')
    (out/name).write_text(text,encoding='utf-8',newline='\n')
(out/'V2Tests.cs').write_bytes((here/'V2Tests.cs').read_bytes().replace(b'\r\n',b'\n'))
base='''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems><TreatWarningsAsErrors>true</TreatWarningsAsErrors>{props}</PropertyGroup><ItemGroup><Compile Include="GeneratedVerifier.cs"/><Compile Include="V2.cs"/>{tests}</ItemGroup></Project>'''
(out/'PreflightV2.csproj').write_text(base.format(props='<AssemblyName>Matawaka.Workbench.V0601PublicationPreflightV2</AssemblyName><StartupObject>Matawaka.V0601PublicationPreflight.EntryPointV2</StartupObject>',tests=''),encoding='utf-8')
(out/'V2Tests.csproj').write_text(base.format(props='<AssemblyName>PreflightV2Tests</AssemblyName><StartupObject>Matawaka.V0601PublicationPreflight.V2Tests</StartupObject>',tests='<Compile Include="LegacySourceTests.cs"/><Compile Include="LegacyEvidenceTests.cs"/><Compile Include="V2Tests.cs"/>'),encoding='utf-8')
(out/'materialization.json').write_text(json.dumps({'OriginalVerifierGitBlob':oid,'OriginalFilesModified':False,'GeneratedVerifierSha256':hashlib.sha256(s.encode()).hexdigest(),'GeneratorPurpose':'build-only successor; no operator-host source generation'},indent=2),encoding='utf-8')
print('MATERIALIZED_PINNED_SUCCESSOR',out)
