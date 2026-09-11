"""Compile the exact code-only predecessor and run its existing synthetic probe.

No application launch, model invocation, download, installation or ref mutation.
The historical WPF icon-decoding build target is inspected and retained unchanged.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import subprocess
import zipfile

COMMIT="34e2af4e59ad5ba0ca209aa6334f0c7debb2bb24"

def need(ok,message):
    if not ok:raise ValueError(message)

def main():
    p=argparse.ArgumentParser();p.add_argument("--repository",required=True);p.add_argument("--output",required=True);p.add_argument("--dotnet",required=True)
    a=p.parse_args();out=Path(a.output).absolute();repo=Path(a.repository).absolute()
    need(not out.exists() and out.name.startswith("workbench-full-build-"),"create-only output")
    for parent in [out.parent,*out.parent.parents]:need(not(getattr(parent.lstat(),"st_file_attributes",0)&0x400),"reparse parent")
    out.mkdir();source=out/"source";source.mkdir();temp=out/"temporary";temp.mkdir()
    env=dict(os.environ);env.update(DOTNET_CLI_HOME=str(out/"dotnet-home"),DOTNET_CLI_TELEMETRY_OPTOUT="1",DOTNET_NOLOGO="1",
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1",DOTNET_GENERATE_ASPNET_CERTIFICATE="false",DOTNET_ADD_GLOBAL_TOOLS_TO_PATH="false",
        DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="true",MSBuildEnableWorkloadResolver="false",MSBUILDDISABLENODEREUSE="1",
        NUGET_PACKAGES=str(out/"packages"),TEMP=str(temp),TMP=str(temp),DOTNET_CLI_UI_LANGUAGE="en")
    git=["git","-c","safe.directory="+repo.as_posix(),"-C",str(repo)]
    subprocess.run(git+["archive","--format=zip","-o",str(out/"source.zip"),COMMIT],check=True,timeout=30,env=env)
    with zipfile.ZipFile(out/"source.zip") as z:
        need(sum(x.file_size for x in z.infolist())<100_000_000,"source archive bound")
        for info in z.infolist():
            name=PurePosixPath(info.filename)
            need(not name.is_absolute() and ".." not in name.parts and "\\" not in info.filename,"archive path")
            need((info.external_attr>>16)&0o170000!=0o120000,"archive symlink")
        z.extractall(source)
    config=out/"NuGet.Config";config.write_text('<configuration><packageSources><clear /></packageSources></configuration>',encoding="utf-8")
    project=source/".github/qualification/source-bound-model/Probe.csproj"
    commands=[("restore",[a.dotnet,"restore",str(project),"--configfile",str(config),"-p:NuGetAudit=false"]),
        ("build",[a.dotnet,"build",str(project),"--no-restore","-c","Release","--disable-build-servers","-p:UseSharedCompilation=false"]),
        ("synthetic-probe",[a.dotnet,str(project.parent/"bin/Release/net10.0-windows/Probe.dll")])]
    stages=[]
    for name,command in commands:
        r=subprocess.run(command,cwd=source,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=180)
        (out/(name+".log")).write_bytes(r.stdout)
        stages.append({"stage":name,"exit_code":r.returncode,"log_sha256":hashlib.sha256(r.stdout).hexdigest()})
        if r.returncode:break
    success=len(stages)==3 and all(x["exit_code"]==0 for x in stages)
    result={"schema":"workbench.full-build-synthetic-probe/v0.1","source_commit":COMMIT,
        "source_zip_sha256":hashlib.sha256((out/"source.zip").read_bytes()).hexdigest(),"stages":stages,
        "status":"FULL_BUILD_AND_EXISTING_SYNTHETIC_PROBE_PASS" if success else "QUALIFICATION_INCOMPLETE",
        "application_started":False,"model_started":False,"installed_application_changed":False,"main_changed":False}
    (out/"RESULT.json").write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(result))
    return 0 if success else 2

if __name__=="__main__":raise SystemExit(main())
