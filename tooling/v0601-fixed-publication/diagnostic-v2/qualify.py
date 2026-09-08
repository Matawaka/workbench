"""Fixture-only Windows PowerShell 5.1 qualification; never touches operator repository."""
import hashlib, json, os, pathlib, subprocess, sys, tempfile
SCRIPT = pathlib.Path(sys.argv[1]).read_text(encoding='utf-8')
GIT = str(pathlib.Path(sys.argv[2]).resolve())
OLD_GIT = str(pathlib.Path(sys.argv[3]).resolve())
REPORT = pathlib.Path(sys.argv[4])
HEAD = '58b9430fc544998a8e40ba00b6757cc630ba9081'
FIRST = 'ea852feeb0e8d92a8977bb251693e7e977913dca'
SECOND = 'ac083598711caa0c399cc0d2c385b980c083024a'
ROOT = r'K:\Matawaka\Workbench'
PINNED_GIT = r'K:\Matawaka\Tools\Git\MinGit-2.55.0.4-64-bit\cmd\git.exe'
ENV = {k:v for k,v in os.environ.items() if not k.upper().startswith(('GIT_', 'GCM_'))}
ENV.update(GIT_CONFIG_NOSYSTEM='1', GIT_CONFIG_GLOBAL='NUL', GIT_TERMINAL_PROMPT='0', GIT_AUTHOR_NAME='Fixture', GIT_AUTHOR_EMAIL='fixture@example.invalid', GIT_COMMITTER_NAME='Fixture', GIT_COMMITTER_EMAIL='fixture@example.invalid')
RESULTS = []
def git(root, *args):
    p = subprocess.run([GIT, '-C', str(root), *args], env=ENV, capture_output=True, timeout=30)
    if p.returncode: raise RuntimeError('FIXTURE_SETUP_FAILED: ' + p.stderr.decode(errors='replace')[:500])
    return p.stdout.decode('utf-8').strip()
def snapshot(root):
    return {str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in root.rglob('*') if p.is_file()}
def fixture(root):
    root.mkdir(); git(root, 'init', '-q')
    (root/'old.txt').write_bytes(b'historical bytes\n'); git(root,'add','.'); git(root,'commit','-qm','first')
    first=git(root,'rev-parse','HEAD'); oldblob=git(root,'rev-parse','HEAD:old.txt')
    (root/'old.txt').unlink(); (root/'new.txt').write_bytes(b'accepted bytes\n'); git(root,'add','--all'); git(root,'commit','-qm','second')
    second=git(root,'rev-parse','HEAD'); tree=git(root,'rev-parse','HEAD^{tree}'); currentblob=git(root,'rev-parse','HEAD:new.txt')
    head=git(root,'commit-tree',tree,'-p',first,'-p',second,'-m','accepted fixture'); git(root,'update-ref','HEAD',head)
    git(root,'tag','-a','workbench-v0.60.1-accepted',head,'-m','accepted fixture')
    git(root,'config','remote.fixture.promisor','true'); git(root,'config','remote.fixture.url','file:///not-authorized.invalid/no-remote')
    return dict(first=first,second=second,head=head,tree=tree,oldblob=oldblob,currentblob=currentblob)
def remove(root, oid):
    (root/'.git/objects'/oid[:2]/oid[2:]).unlink()
def run_case(name, mutation=None, wanted=None, old=False, partial=False):
    with tempfile.TemporaryDirectory(prefix='v0601-win-diagnostic-') as t:
        base=pathlib.Path(t); root=base/'subject'; f=fixture(root)
        if partial:
            remote=base/'remote.git'; git(base,'clone','--bare',str(root),str(remote)); git(remote,'config','uploadpack.allowFilter','true')
            dest=base/'partial'; git(base,'-c','protocol.file.allow=always','clone','--filter=blob:none','--no-checkout',remote.as_uri(),str(dest))
            root=dest; git(root,'read-tree',f['head'])
            packs=list((root/'.git/objects/pack').glob('*.promisor')); assert packs, 'NOT_A_REAL_PARTIAL_CLONE'
        if mutation: mutation(root,f)
        code=SCRIPT.replace(ROOT,str(root)).replace(PINNED_GIT,OLD_GIT if old else GIT).replace(HEAD,f['head']).replace(FIRST,f['first']).replace(SECOND,f['second'])
        test_script=base/'fixture.ps1'; test_script.write_text(code,encoding='utf-8')
        before=snapshot(root)
        proc=subprocess.run(['powershell.exe','-NoLogo','-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',str(test_script)],capture_output=True,timeout=150,env=ENV)
        text=proc.stdout.decode('utf-8',errors='replace').strip(); err=proc.stderr.decode('utf-8',errors='replace').strip()
        assert not err, (name,'UNEXPECTED_STDERR',err[:600])
        assert snapshot(root)==before, (name,'REPOSITORY_BYTES_CHANGED')
        if isinstance(wanted,str):
            assert 'DIAGNOSTIC_REFUSED: '+wanted in text,(name,text[:800]); outcome={'refusal':wanted}
        else:
            value=json.loads(text); assert value['HeadRefsConfigIndexStable'] is True and value['PublicationAuthorized'] is False
            assert value['GitVersion']=='git version 2.55.0.windows.4'
            ct=value['CurrentTreeMissingBoundaryObjects']; ht=value['FullHistoryMissingBoundaryObjects']
            assert wanted(ct,ht,value),(name,ct,ht,value); outcome={'CurrentTreeMissingBoundaryObjects':ct,'FullHistoryMissingBoundaryObjects':ht}
        RESULTS.append({'Id':name,'Passed':True,'RepositoryBytesUnchanged':True,'Outcome':outcome})
        print('PASS',name,flush=True)
run_case('complete-promisor-true',wanted=lambda c,h,v:c==0 and h==0 and v['ReachableClosureComplete'])
run_case('historical-blob-missing',lambda r,f:remove(r,f['oldblob']),lambda c,h,v:c==0 and h==1)
run_case('current-blob-missing',lambda r,f:remove(r,f['currentblob']),lambda c,h,v:c==1 and h==1)
run_case('current-root-tree-missing',lambda r,f:remove(r,f['tree']),lambda c,h,v:c==1 and h>=1 and not v['RootTreePresent'])
run_case('parent-commit-missing-boundary',lambda r,f:remove(r,f['second']),lambda c,h,v:c==0 and h==1)
run_case('actual-blob-none-partial-clone',wanted=lambda c,h,v:c>=1 and h>=c and not v['ReachableClosureComplete'],partial=True)
run_case('wrong-parent-order',lambda r,f:f.update(first=f['second'],second=f['first']),wanted='ORDERED_PARENTS_MISMATCH')
run_case('lightweight-tag-refused',lambda r,f:(git(r,'tag','-d','workbench-v0.60.1-accepted'),git(r,'tag','workbench-v0.60.1-accepted')),wanted='ANNOTATED_TAG_REQUIRED')
run_case('includes-remain-refused',lambda r,f:git(r,'config','include.path','absent-file'),wanted='OTHER_UNQUALIFIED_CONFIG')
run_case('git-244-unsupported-flag-classified',wanted='GIT_VERSION_EXIT_129',old=True)
REPORT.write_text(json.dumps({'Schema':'matawaka.workbench-offline-diagnostic-v2-qualification/v0.1','SourceSha':os.environ.get('GITHUB_SHA'),'Platform':sys.platform,'PowerShell':'Windows PowerShell 5.1','GitVersion':git(pathlib.Path.cwd(),'--version'),'Cases':RESULTS,'ProductionRepositoryContacted':False,'ProductionRemoteContacted':False,'NewPreflightAdmitted':False,'PublicationAuthorized':False},indent=2),encoding='utf-8')
print('ALL_PASS',len(RESULTS))
