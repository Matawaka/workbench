"""Offline, no-package qualification. Executes only a synthetic C# probe, never the app/model.

Create-only output; no updates to Windows, installed tools, repositories or old evidence.
The dotnet SDK must already exist. No WPF project/its Exec target is invoked.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import zipfile
from xml.sax.saxutils import escape

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
PACKAGE = "integrations/model-invocation/qwen-code-only/"
BASE = "58b9430fc544998a8e40ba00b6757cc630ba9081"
OLD = {
    "src/Matawaka.Workbench.App/SourceBoundModelInvocation.cs": "14d092d47e6a699405eededad6b577bd4bf8a4da",
    "src/Matawaka.Workbench.App/BoundedLocalModelInvocationV055Service.cs": "40f79c296afe5e762b3f61634ae3983ddb2111a8",
    ".github/qualification/source-bound-model/translation-fixture.json": "3409b9f71c0f47f286fd191994857b8e3c862825",
}
FILES = [*OLD, "src/Matawaka.Workbench.App/QwenIsolatedOneShotAdapter.cs",
         "src/Matawaka.Workbench.App/QwenOneShotRehearsal.cs", PACKAGE+"Probe.cs",
         PACKAGE+"qualify.py", PACKAGE+"README.md", PACKAGE+"REQUEST.json"]


def need(ok, code):
    if not ok:
        raise ValueError(code)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def read_source(root):
    data = {}
    for rel in FILES:
        path = root/rel
        need(path.is_file() and not path.is_symlink(), "source missing/symlink")
        for parent in [path, *path.parents]:
            need(not (getattr(parent.lstat(), 'st_file_attributes', 0) & 0x400), "source reparse")
        data[rel] = path.read_bytes()
        need(len(data[rel]) < 200000, "source byte limit")
    for rel, expected in OLD.items():
        canonical = data[rel].replace(b'\r\n', b'\n')
        need(b'\r' not in canonical, "unexpected CR")
        blob = hashlib.sha1(b'blob '+str(len(canonical)).encode()+b'\0'+canonical).hexdigest()
        need(blob == expected, "historical source drift")
    # This is a supplementary lexical guard, not a security sandbox or arbitrary-code audit.
    for rel in FILES[3:6]:
        source = data[rel].decode('utf-8')
        for forbidden in ['Process.Start(', 'ProcessStartInfo', 'DllImport(', 'LibraryImport(',
                          'HttpClient', 'TcpClient', 'UdpClient', 'Socket(', 'WebRequest',
                          'Assembly.Load', 'Environment.GetEnvironmentVariable']:
            need(forbidden not in source, "active capability in code-only source")
    return data


def declarations(data):
    results = []
    for rel, name in [(list(OLD)[0], 'ModelInvocationSourceBinding'),
                      (list(OLD)[1], 'LocalModelInvocationRequestV055')]:
        matches = re.findall(r'public sealed record '+name+r'\([\s\S]*?\);', data[rel].decode('utf-8'))
        need(len(matches) == 1 and '{' not in matches[0], "record extraction")
        results.append(matches[0])
    return 'namespace Matawaka.Workbench.App;\n'+'\n'.join(results)+'\n'


def execute(args, cwd, environment):
    p = subprocess.run(args, cwd=cwd, env=environment, stdout=subprocess.PIPE,
                       stderr=subprocess.STDOUT, timeout=90, check=False)
    need(len(p.stdout) <= 2_000_000, "test-output size")
    return p.returncode, p.stdout


def qualify_stage(root, out, dotnet, expect_red=False):
    data = read_source(root)
    out.mkdir()
    env = dict(os.environ)
    env.update(DOTNET_CLI_HOME=str(out/'dotnet-home'), DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',
               DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1',
               DOTNET_GENERATE_ASPNET_CERTIFICATE='false', MSBuildEnableWorkloadResolver='false',
               NUGET_PACKAGES=str(out/'packages'), MSBUILDDISABLENODEREUSE='1')
    (out/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>',encoding='utf-8')
    (out/'DataRecords.cs').write_text(declarations(data),encoding='utf-8')
    links = [root/p for p in FILES[3:6]]
    csproj = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup><ItemGroup><Compile Include="DataRecords.cs" />'
    csproj += ''.join('<Compile Include="'+escape(str(p), {'"':'&quot;'})+'" />' for p in links)
    csproj += '</ItemGroup></Project>'
    project = out/'Probe.csproj'; project.write_text(csproj,encoding='utf-8')
    for name, args in [('restore',[dotnet,'restore',str(project),'--configfile',str(out/'NuGet.Config'),'-p:NuGetAudit=false']),
                       ('build',[dotnet,'build',str(project),'--no-restore','-c','Release','--nologo',
                                 '--disable-build-servers','-p:UseSharedCompilation=false'])]:
        code, log = execute(args,out,env); (out/(name+'.log')).write_bytes(log)
        if code:
            print(log.decode('utf-8',errors='replace'))
            raise ValueError(name+' failed')
    fixture = root/list(OLD)[2]
    code, log = execute([dotnet,str(out/'bin/Release/net10.0/Probe.dll'),str(fixture)],out,env)
    (out/'probe.log').write_bytes(log)
    lines=log.decode('utf-8').splitlines()
    for line in lines:
        if line.startswith('FAIL '): print(line)
    summary = json.loads(lines[-1]); print(json.dumps(summary))
    if expect_red:
        need(code == 1 and summary['status'] == 'CODE_ONLY_QWEN_PROBE_FAIL' and summary['failed'] > 0,
             "mutant did not produce targeted RED")
    else:
        need(code == 0 and summary['status'] == 'CODE_ONLY_QWEN_PROBE_PASS' and summary['failed'] == 0, "probe summary")
    names=[line[5:].split(' ')[0] for line in lines if line.startswith(('PASS ','FAIL '))]
    need(len(names)==len(set(names))==summary['passed']+summary['failed'], "probe IDs/counts")
    for key in ['liveModel','processStart','realLease','processNetworkIsolationProven','display']:
        need(summary[key] is False, "probe effect")
    need(read_source(root) == data, "source changed during test")
    return {**summary, 'dll_sha256':sha((out/'bin/Release/net10.0/Probe.dll').read_bytes()),
            'source_sha256':{p:sha(b) for p,b in data.items()},
            'data_record_extraction_sha256':sha((out/'DataRecords.cs').read_bytes())}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--dotnet', required=True)
    ap.add_argument('--output', required=True)
    args = ap.parse_args()
    # Resolve/check before create. No deletion/reuse; caller chooses one bounded output directory.
    raw = Path(args.output).absolute()
    need(not raw.exists() and raw.name.startswith('qwen-code-only-qualification-'), "create-only output required")
    need(raw.parent.is_dir(), "output parent absent")
    for p in [raw.parent,*raw.parent.parents]:
        need(not p.is_symlink() and not (getattr(p.lstat(),'st_file_attributes',0)&0x400), "output reparse")
    out = raw.resolve(); need(not out.is_relative_to(ROOT) and out != ROOT.parent, "output outside checkout required")
    dotnet = str(Path(args.dotnet).resolve()); need(Path(dotnet).is_file(), "existing dotnet required")
    data = read_source(ROOT)
    out.mkdir()
    code, version = execute([dotnet,'--version'],out,dict(os.environ))
    need(code == 0 and version.decode().strip() == '10.0.400', "SDK 10.0.400 required; no installation")
    report = {'status':'INCOMPLETE', 'base_frontier':BASE, 'sdk':'10.0.400',
              'platform':sys.platform, 'repository':None,'clean_unpacked':None,
              'live_model':False,'real_lease':False,'os_isolation_qualified':False,
              'windows_settings_changed':False,'historical_source_modified':False}
    try:
        archive = out/'source.zip'
        with zipfile.ZipFile(archive,'x',compression=zipfile.ZIP_STORED) as z:
            for path,b in sorted(data.items()):
                info=zipfile.ZipInfo(path,date_time=(2026,1,1,0,0,0));info.external_attr=0o100644 << 16
                z.writestr(info,b)
        report['source_archive_sha256']=sha(archive.read_bytes())
        report['source_files']=len(data)
        report['repository']=qualify_stage(ROOT,out/'repository',dotnet)
        clean=out/'clean-source';clean.mkdir()
        with zipfile.ZipFile(archive) as z:
            need(set(z.namelist())==set(FILES) and len(z.namelist())==len(FILES), "archive entries")
            for rel in FILES:
                b=z.read(rel);need(b==data[rel], "archive byte mismatch")
                dest=clean/rel;dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(b)
        report['clean_unpacked']=qualify_stage(clean,out/'clean-build',dotnet)
        for key in ['passed','failed','source_sha256','data_record_extraction_sha256']:
            need(report['repository'][key]==report['clean_unpacked'][key], "qualification context mismatch")
        # Controlled mutations of new code only, in separate throwaway source COPIES.
        # Never overwrite historical inputs, production source or original pass/fail records.
        mutants = [
            ('live-guard', FILES[3], 'throw new QwenAdapterRefusal("HOST_ISOLATION_PROVIDER_NOT_QUALIFIED");', 'return;'),
            ('one-shot', FILES[4], ' && state == "PREPARED_SYNTHETIC_ONLY"', ''),
            ('time', FILES[4], 'now >= issued && now >= observed && now < expires', 'true'),
        ]
        report['negative_mutations']={}
        for name, path, before, after in mutants:
            mutant=out/('mutant-'+name);mutant.mkdir()
            for rel,b in data.items():
                if rel==path:
                    text=b.decode('utf-8');need(text.count(before)==1, "mutation target count")
                    b=text.replace(before,after).encode('utf-8')
                dest=mutant/rel;dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(b)
            report['negative_mutations'][name]=qualify_stage(mutant,out/('mutant-build-'+name),dotnet,expect_red=True)
        report['status']='CODE_ONLY_QWEN_ADAPTER_AND_REHEARSAL_QUALIFIED_NOT_LIVE_READY'
    finally:
        (out/'QUALIFICATION.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(report['status'])


if __name__ == '__main__':
    main()
