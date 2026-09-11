"""Build the native boundary probe and run PURE controls only; never invokes native-trial.

An operator/agent must separately invoke the fixed --native-trial entry point within
the explicit current window. All outputs create-only, no SDK install or NuGet source.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
from xml.sax.saxutils import escape

HERE=Path(__file__).resolve().parent
def require(ok,message):
    if not ok:raise ValueError(message)
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def main():
    p=argparse.ArgumentParser();p.add_argument('--dotnet',required=True);p.add_argument('--output',required=True);a=p.parse_args()
    out=Path(a.output).absolute()
    require(not out.exists() and out.name.startswith('windows-isolation-qualification-'),'new named directory required')
    require(out.parent.is_dir(),'parent absent')
    for parent in [out.parent,*out.parent.parents]:require(not(getattr(parent.lstat(),'st_file_attributes',0)&0x400),'reparse parent')
    require(Path(a.dotnet).is_file(),'existing SDK required')
    out.mkdir();build=out/'build';build.mkdir()
    env=dict(os.environ);env.update(DOTNET_CLI_HOME=str(out/'dotnet-home'),DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1',
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',DOTNET_GENERATE_ASPNET_CERTIFICATE='false',MSBuildEnableWorkloadResolver='false',
        MSBUILDDISABLENODEREUSE='1',NUGET_PACKAGES=str(out/'packages'),
        DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true')
    config=out/'NuGet.Config';config.write_text('<configuration><packageSources><clear /></packageSources></configuration>',encoding='utf-8')
    source={name:sha(HERE/name) for name in ['NativeBoundary.cs','Probe.cs','prepare.py']}
    project=build/'Probe.csproj'
    xml='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><AssemblyName>Workbench.AppContainerProbe</AssemblyName><TargetFramework>net10.0-windows</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup><ItemGroup>'
    xml+=''.join('<Compile Include="'+escape(str(HERE/name),{'"':'&quot;'})+'" />' for name in ['NativeBoundary.cs','Probe.cs'])
    project.write_text(xml+'</ItemGroup></Project>',encoding='utf-8')
    commands=[('restore',[a.dotnet,'restore',str(project),'--configfile',str(config),'-p:NuGetAudit=false']),
        ('build',[a.dotnet,'build',str(project),'--no-restore','-c','Release','--disable-build-servers','-p:UseSharedCompilation=false'])]
    for name,command in commands:
        r=subprocess.run(command,cwd=out,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=90)
        (out/(name+'.log')).write_bytes(r.stdout)
        if r.returncode:print(r.stdout.decode(errors='replace'));raise ValueError(name+' failed')
    binary=build/'bin/Release/net10.0-windows/Workbench.AppContainerProbe.dll'
    r=subprocess.run([a.dotnet,str(binary),'--unit'],cwd=out,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=10)
    (out/'unit.log').write_bytes(r.stdout);print(r.stdout.decode());require(r.returncode==0,'pure tests failed')
    trial=out/'trial';trial.mkdir()
    names=['Workbench.AppContainerProbe'+suffix for suffix in ['.exe','.dll','.runtimeconfig.json','.deps.json']]
    hashes={}
    for name in names:
        raw=(binary.parent/name).read_bytes();require(len(raw)<2_000_000,'bundle limit')
        target=trial/name;target.write_bytes(raw);hashes[name]=sha(target)
    for name,text in {'allow-read.txt':'SYNTHETIC_ALLOWED','deny-read.txt':'SYNTHETIC_DENIED','deny-write.txt':'SYNTHETIC_UNCHANGED'}.items():
        (trial/name).write_text(text,encoding='utf-8')
    result={'schema':'workbench.native-test-build/v0.1','source_sha256':source,'files':hashes,'native_trial_invoked':False,
        'model_started':False,'pure_tests':json.loads(r.stdout),'requires_explicit_native_step':True}
    (out/'BUNDLE.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print('NATIVE_PROBE_BUILT_NOT_EXECUTED')
if __name__=='__main__':main()
