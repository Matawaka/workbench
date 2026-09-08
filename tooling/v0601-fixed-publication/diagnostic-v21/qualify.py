"""Windows fixture-only tests. No operator repository or production remote is accessed."""
import hashlib, json, os, pathlib, re, subprocess, sys, tempfile
SCRIPT_PATH = pathlib.Path(sys.argv[1])
SCRIPT = SCRIPT_PATH.read_text(encoding='utf-8')
GIT = str(pathlib.Path(sys.argv[2]).resolve())
OLD_GIT = str(pathlib.Path(sys.argv[3]).resolve())
REPORT = pathlib.Path(sys.argv[4])
HEAD = '58b9430fc544998a8e40ba00b6757cc630ba9081'
FIRST = 'ea852feeb0e8d92a8977bb251693e7e977913dca'
SECOND = 'ac083598711caa0c399cc0d2c385b980c083024a'
ROOT = r'K:\Matawaka\Workbench'
PINNED_GIT = r'K:\Matawaka\Tools\Git\MinGit-2.55.0.4-64-bit\cmd\git.exe'
ENV = {k:v for k,v in os.environ.items() if not k.upper().startswith(('GIT_', 'GCM_')) and k.upper() != 'PSMODULEPATH'}
ENV.update(GIT_CONFIG_NOSYSTEM='1', GIT_CONFIG_GLOBAL='NUL', GIT_TERMINAL_PROMPT='0', GIT_AUTHOR_NAME='Fixture', GIT_AUTHOR_EMAIL='fixture@example.invalid', GIT_COMMITTER_NAME='Fixture', GIT_COMMITTER_EMAIL='fixture@example.invalid')
RESULTS = []
def git(root, *args):
    p = subprocess.run([GIT, '-C', str(root), *args], env=ENV, capture_output=True, timeout=30)
    if p.returncode: raise RuntimeError('FIXTURE_SETUP_FAILED: ' + p.stderr.decode(errors='replace')[:500])
    return p.stdout.decode('utf-8').strip()
def snapshot(root):
    return {str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in root.rglob('*') if p.is_file()}
def fixture(root, historical):
    root.mkdir(); git(root, 'init', '-q')
    for i in range(historical): (root/f'historical-{i}.txt').write_bytes(f'historical {i}\n'.encode())
    git(root,'add','.'); git(root,'commit','-qm','first'); first=git(root,'rev-parse','HEAD')
    old=[git(root,'rev-parse',f'HEAD:historical-{i}.txt') for i in range(historical)]
    for i in range(historical): (root/f'historical-{i}.txt').unlink()
    (root/'current.txt').write_bytes(b'accepted bytes\n'); git(root,'add','--all'); git(root,'commit','-qm','second')
    second=git(root,'rev-parse','HEAD'); tree=git(root,'rev-parse','HEAD^{tree}'); current=git(root,'rev-parse','HEAD:current.txt')
    head=git(root,'commit-tree',tree,'-p',first,'-p',second,'-m','accepted fixture'); git(root,'update-ref','HEAD',head)
    git(root,'tag','-a','workbench-v0.60.1-accepted',head,'-m','accepted fixture')
    tag=git(root,'rev-parse','refs/tags/workbench-v0.60.1-accepted')
    git(root,'config','remote.fixture.promisor','true'); git(root,'config','remote.fixture.url','file:///not-authorized.invalid/no-remote')
    return dict(first=first,second=second,head=head,tree=tree,old=old,current=current,tag=tag)
def remove(root, oid):
    # Disposable fixture corruption BEFORE the read-only baseline, never operator code.
    p=root/'.git/objects'/oid[:2]/oid[2:]; p.chmod(0o666); p.unlink()
def powershell(path):
    p=subprocess.run(['powershell.exe','-NoLogo','-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',str(path)],capture_output=True,timeout=150,env=ENV)
    text=p.stdout.decode('utf-8',errors='replace').strip(); error=p.stderr.decode('utf-8',errors='replace').strip()
    assert not error, ('UNEXPECTED_STDERR',error[:700])
    return text

def run_case(name, mutation=None, wanted=None, old=False, partial=False, historical=1, rewrite=None):
    with tempfile.TemporaryDirectory(prefix='v0601-win-inventory-') as t:
        base=pathlib.Path(t); root=base/'subject'; f=fixture(root,historical)
        if partial:
            remote=base/'remote.git'; git(base,'clone','--bare',str(root),str(remote)); git(remote,'config','uploadpack.allowFilter','true')
            dest=base/'partial'; git(base,'-c','protocol.file.allow=always','clone','--filter=blob:none','--no-checkout',remote.as_uri(),str(dest))
            root=dest; git(root,'read-tree',f['head']); assert list((root/'.git/objects/pack').glob('*.promisor'))
        if mutation: mutation(root,f)
        code=SCRIPT.replace(ROOT,str(root)).replace(PINNED_GIT,OLD_GIT if old else GIT).replace(HEAD,f['head']).replace(FIRST,f['first']).replace(SECOND,f['second'])
        if rewrite: code=rewrite(code)
        # Exception detail confined to disposable fixture, never delivered operator bytes.
        code=code.replace('$message = $_.Exception.Message', "Write-Output ('FIXTURE_ERROR: ' + $_.Exception.ToString() + ' AT ' + $_.ScriptStackTrace); $message = $_.Exception.Message")
        test_script=base/'fixture.ps1'; test_script.write_text(code,encoding='utf-8'); before=snapshot(root)
        text=powershell(test_script); assert snapshot(root)==before, (name,'REPOSITORY_BYTES_CHANGED')
        if isinstance(wanted,str):
            assert 'DIAGNOSTIC_REFUSED: '+wanted in text,(name,text[:2500]); outcome={'Refusal':wanted}
        else:
            assert text.startswith('{'), (name,'NOT_JSON',text[:3000]); v=json.loads(text)
            assert v['Diagnostic']=='OFFLINE_OBJECT_INVENTORY_V21_EXACT_SIDE_BY_SIDE_GIT'
            assert v['HeadRefsConfigIndexStable'] is True and v['PublicationAuthorized'] is False
            assert v['GitVersion']=='git version 2.55.0.windows.4'
            assert v['AcceptedTagObject']==f['tag'] and v['AcceptedTagPeeledCommit']==f['head']
            for key in ('ObjectTypesInferred','ObjectPathsDisclosed','WholeWorkingTreeVerified','WholeRuntimeBinaryTreeVerified','CanonicalPreflightReceiptCreated','OsNetworkIsolationProven'):
                assert v[key] is False,(name,key)
            expected_current, expected_history=wanted(f)
            for prefix, expected in (('CurrentTree',expected_current),('FullHistory',expected_history)):
                ids=v[prefix+'MissingBoundaryOids']; assert isinstance(ids,list)
                assert ids==sorted(set(expected)), (name,prefix,ids,expected)
                assert len(ids)==v[prefix+'MissingBoundaryObjects']
                assert all(re.fullmatch('[0-9a-f]{40}',i) for i in ids)
            assert v['ReachableClosureComplete']==(not expected_current and not expected_history)
            outcome={'CurrentTreeMissingBoundaryOids':v['CurrentTreeMissingBoundaryOids'],'FullHistoryMissingBoundaryOids':v['FullHistoryMissingBoundaryOids'],'AcceptedTagObject':v['AcceptedTagObject']}
        RESULTS.append({'Id':name,'Passed':True,'RepositoryBytesUnchanged':True,'Outcome':outcome}); print('PASS',name,flush=True)

run_case('complete-promisor-true-empty-arrays',wanted=lambda f:([],[]))
run_case('historical-blob-exact-oid',lambda r,f:remove(r,f['old'][0]),lambda f:([],f['old']))
run_case('current-blob-exact-oid',lambda r,f:remove(r,f['current']),lambda f:([f['current']],[f['current']]))
run_case('missing-root-tree-boundary-not-descendants',lambda r,f:remove(r,f['tree']),lambda f:([f['tree']],[f['tree']]))
run_case('missing-parent-commit-exact-boundary',lambda r,f:remove(r,f['second']),lambda f:([],[f['second']]))
run_case('genuine-blob-none-partial-clone',wanted=lambda f:([f['current']],f['old']+[f['current']]),partial=True)
run_case('wrong-parent-order',lambda r,f:f.update(first=f['second'],second=f['first']),wanted='ORDERED_PARENTS_MISMATCH')
run_case('lightweight-tag-refused',lambda r,f:(git(r,'tag','-d','workbench-v0.60.1-accepted'),git(r,'tag','workbench-v0.60.1-accepted')),wanted='ANNOTATED_TAG_REQUIRED')
run_case('include-config-refused',lambda r,f:git(r,'config','include.path','absent-file'),wanted='OTHER_UNQUALIFIED_CONFIG')
run_case('old-git-244-no-fallback',wanted='GIT_VERSION_EXIT_129',old=True)
run_case('six-missing-historical-oids-sorted-complete-list',lambda r,f:[remove(r,i) for i in f['old']],lambda f:([],f['old']),historical=6)
run_case('fixed-git-sha-mismatch-refused',wanted='EXACT_GIT_IMAGE_MISMATCH',rewrite=lambda s:s.replace('c470d205517c7a53ceca321df16a6e4549fcd52b576ab4d09536d36f26fda5a9','0'*64))

# Exercise the exact production parser body, with no repository or Git invocation.
start=SCRIPT.index('        function Count-Objects('); end=SCRIPT.index('        # Capability probe',start); parser=SCRIPT[start:end]
def parser_case(name, lines, expected):
    with tempfile.TemporaryDirectory(prefix='v0601-oid-parser-') as t:
        p=pathlib.Path(t)/'parser.ps1'
        code="& { $ErrorActionPreference='Stop'\n"+parser+"\ntry { $null=Count-Objects ((@("+','.join("'"+line+"'" for line in lines)+")) -join \"`n\"); 'UNEXPECTED_PASS' } catch { $_.Exception.Message }\n}"
        p.write_text(code,encoding='utf-8'); text=powershell(p); assert text==expected,(name,text)
        RESULTS.append({'Id':name,'Passed':True,'NoRepositoryAccess':True,'Outcome':{'Refusal':expected}}); print('PASS',name,flush=True)
parser_case('duplicate-missing-oid-refused',['?'+'1'*40,'?'+'1'*40],'DUPLICATE_OBJECT_OUTPUT')
parser_case('present-missing-conflict-refused',['1'*40,'?'+'1'*40],'DUPLICATE_OBJECT_OUTPUT')
parser_case('malformed-object-oid-refused',['?not-an-oid'],'UNEXPECTED_OBJECT_OUTPUT')
parser_case('missing-list-over-256-refused-not-truncated',['?'+f'{i:040x}' for i in range(257)],'MISSING_OID_DISCLOSURE_LIMIT')
REPORT.write_text(json.dumps({'Schema':'matawaka.workbench-offline-inventory-v21-qualification/v0.1','SourceSha':os.environ.get('GITHUB_SHA'),'ScriptSha256':hashlib.sha256(SCRIPT_PATH.read_bytes()).hexdigest(),'Platform':sys.platform,'PowerShell':'Windows PowerShell 5.1','GitVersion':git(pathlib.Path.cwd(),'--version'),'Cases':RESULTS,'ProductionRepositoryContacted':False,'ProductionRemoteContacted':False,'NewPreflightAdmitted':False,'PublicationAuthorized':False},indent=2),encoding='utf-8')
print('ALL_PASS',len(RESULTS))
