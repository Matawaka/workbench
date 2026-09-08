"""Windows PowerShell 5.1 fixtures only; no operator repo or production push."""
import hashlib,json,os,pathlib,re,subprocess,sys,tempfile
SCRIPT=pathlib.Path(sys.argv[1]).read_text(encoding='utf-8-sig')
GIT=str(pathlib.Path(sys.argv[2]).resolve()); PAYLOAD=pathlib.Path(sys.argv[3]); REPORT=pathlib.Path(sys.argv[4])
OIDS='162aa48105259c20b067ba43e48db35db4f9d34a 3b542376bae6ed3ab5a5fdd232cdfd96b453c586 5fdd8744827ddd202e317174c937761fb20a81e2 81aab74193548f4061853abb4a6cfcaea25975c2 c363c499acc2fd5a93624e3ad51d9320ea48e7af f68e906e127777b1cf918b75829b8f692cecc682'.split()
CONFIRM='IMPORT-EXACT-V0601-SIX-HISTORICAL-BLOBS'; RESULTS=[]
ENV={k:v for k,v in os.environ.items() if not k.upper().startswith(('GIT_','GCM_','GH_')) and k.upper()!='PSMODULEPATH'}
ENV.update(GIT_CONFIG_NOSYSTEM='1',GIT_CONFIG_GLOBAL='NUL',GIT_TERMINAL_PROMPT='0',GIT_AUTHOR_NAME='Fixture',GIT_AUTHOR_EMAIL='fixture@example.invalid',GIT_COMMITTER_NAME='Fixture',GIT_COMMITTER_EMAIL='fixture@example.invalid')
def git(root,*args,data=None):
 p=subprocess.run([GIT,'-C',str(root),*args],input=data,env=ENV,capture_output=True,timeout=30)
 if p.returncode:raise RuntimeError('FIXTURE_SETUP_FAILED '+p.stderr.decode(errors='replace')[:500])
 return p.stdout.decode('utf-8').strip()
def snap(root):return {p.relative_to(root).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in root.rglob('*') if p.is_file()}
def remove(root,oid):
 p=root/'.git/objects'/oid[:2]/oid[2:];p.chmod(0o666);p.unlink()
class Fixture:
 def __init__(self,base):
  self.base=base;self.root=base/'subject';self.root.mkdir();self.bundle=base/'bundle';self.bundle.mkdir()
  git(self.root,'init','-q')
  (self.root/'.gitignore').write_bytes(b'artifacts/\n')
  for oid in OIDS:
   data=(PAYLOAD/(oid+'.blob')).read_bytes();(self.root/(oid+'.txt')).write_bytes(data);(self.bundle/(oid+'.blob')).write_bytes(data)
  git(self.root,'add','--all');git(self.root,'commit','-qm','first history with six public blobs');self.first=git(self.root,'rev-parse','HEAD')
  git(self.root,'tag','workbench-v0.55.2-accepted',self.first)
  for oid in OIDS:(self.root/(oid+'.txt')).unlink()
  (self.root/'current.txt').write_bytes(b'current accepted source\n');git(self.root,'add','--all');git(self.root,'commit','-qm','second source')
  self.second=git(self.root,'rev-parse','HEAD');self.tree=git(self.root,'rev-parse','HEAD^{tree}');self.current=git(self.root,'rev-parse','HEAD:current.txt')
  self.head=git(self.root,'commit-tree',self.tree,'-p',self.first,'-p',self.second,'-m','accepted fixture');git(self.root,'update-ref','HEAD',self.head)
  git(self.root,'tag','-a','workbench-v0.60.1-accepted',self.head,'-m','accepted fixture');self.tag=git(self.root,'rev-parse','refs/tags/workbench-v0.60.1-accepted')
  git(self.root,'config','remote.fixture.promisor','true');git(self.root,'config','remote.fixture.url','file:///not-authorized.invalid/fixture')
  for oid in OIDS:remove(self.root,oid)
  self.history=len(git(self.root,'--no-lazy-fetch','rev-list','--objects','--no-object-names','--missing=print',self.head,'refs/tags/workbench-v0.60.1-accepted','--').splitlines())-6
  self.treecount=len(git(self.root,'--no-lazy-fetch','rev-list','--objects','--no-object-names','--missing=print',self.tree,'--').splitlines())
  self.code=SCRIPT
  bindings={r'K:\Matawaka\Workbench':str(self.root),r'K:\Matawaka\Tools\WorkbenchV0601SixBlobImportV1':str(self.bundle),r'K:\Matawaka\Tools\Git\MinGit-2.55.0.4-64-bit\cmd\git.exe':GIT,'58b9430fc544998a8e40ba00b6757cc630ba9081':self.head,'ea852feeb0e8d92a8977bb251693e7e977913dca':self.first,'ac083598711caa0c399cc0d2c385b980c083024a':self.second,'a6baa9d2d90fb9f00e54ac4c2373b7d9b8aab4e1':self.tree,'c07409c7973f1a4181e94168ba172bba0d56355f':self.tag,'$expectedTreeCount = 583':'$expectedTreeCount = '+str(self.treecount),'$expectedHistoryCount = 1378':'$expectedHistoryCount = '+str(self.history)}
  for old,new in bindings.items():self.code=self.code.replace(old,new)
  self.receipts=[]
  for rel,digest in re.findall(r"@\('(artifacts/[^']+)','([0-9a-f]{64})'\)",self.code):
   p=self.root/rel;p.parent.mkdir(parents=True,exist_ok=True);data=('SYNTHETIC FIXTURE RECEIPT '+rel).encode();p.write_bytes(data);self.receipts.append(p);self.code=self.code.replace(digest,hashlib.sha256(data).hexdigest())
  assert len(self.receipts)==6
  self.attempt=self.root/'artifacts/object-recovery-v0601'/('attempt-six-blobs-'+self.head+'.json')
  self.receipt=self.attempt.with_name('six-blobs-'+self.head+'.json');self.script=base/'fixture.ps1'
 def run(self,token=CONFIRM,extra=()):
  code=self.code.replace("$reason=$_.Exception.Message;", "Write-Output ('FIXTURE_EXCEPTION: '+$_.Exception.ToString()+' AT '+$_.ScriptStackTrace); $reason=$_.Exception.Message;")
  self.script.write_text(code,encoding='utf-8')
  p=subprocess.run(['powershell.exe','-NoLogo','-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',str(self.script),*extra],input=(token+'\n').encode(),capture_output=True,env=ENV,timeout=180)
  text=p.stdout.decode('utf-8',errors='replace');err=p.stderr.decode('utf-8',errors='replace');assert not err,err[:1200]
  return text
 def present(self):return {oid for oid in OIDS if (self.root/'.git/objects'/oid[:2]/oid[2:]).is_file()}
def reverse_parents(f):
 newhead=git(f.root,'commit-tree',f.tree,'-p',f.second,'-p',f.first,'-m','wrong order fixture');git(f.root,'update-ref','HEAD',newhead)
 git(f.root,'tag','-f','-a','workbench-v0.60.1-accepted',newhead,'-m','wrong ordered fixture');newtag=git(f.root,'rev-parse','refs/tags/workbench-v0.60.1-accepted')
 f.code=f.code.replace(f.head,newhead).replace(f.tag,newtag)
def case(name,mutate=None,expected='success',token=CONFIRM,extra=()):
 with tempfile.TemporaryDirectory(prefix='six-blob-import-') as t:
  f=Fixture(pathlib.Path(t)); mutate and mutate(f);before=snap(f.root);text=f.run(token,extra);after=snap(f.root)
  if expected=='success':
   assert 'COMPLETED: SIX_HISTORICAL_BLOBS_IMPORTED_OFFLINE_NO_REF_MUTATION' in text,(name,text[-3500:])
   r=json.loads(f.receipt.read_bytes());assert r['FullHistoryMissingBoundaryObjects']==0 and r['ReachableClosureComplete'] and r['OriginalCanonicalReceiptsUnchanged'] and not r['PublicationAuthorized']
   assert r['ImportedObjects']==OIDS and r['ImportedRawBytes']==17695 and r['GitMetadataSha256Before']==r['GitMetadataSha256After']
   assert all(after.get(k)==v for k,v in before.items()),'PREEXISTING_BYTES_CHANGED'
   allowed={'.git/objects/'+o[:2]+'/'+o[2:] for o in OIDS}|{f.attempt.relative_to(f.root).as_posix(),f.receipt.relative_to(f.root).as_posix()}
   assert set(after)-set(before)==allowed,(name,set(after)-set(before))
   replay=f.run();assert 'ATTEMPT_OR_RECEIPT_EXISTS_NO_RETRY' in replay and snap(f.root)==after
  elif expected=='cancel':assert 'CANCELLED_NO_WRITE' in text and before==after,(name,text[-2500:])
  elif expected=='partial':
   assert 'FIXTURE_INJECTED_FAILURE' in text and f.present()=={OIDS[0]} and f.attempt.exists() and not f.receipt.exists(),(name,text[-2500:])
   assert all(after.get(k)==v for k,v in before.items()); replay=f.run();assert 'ATTEMPT_OR_RECEIPT_EXISTS_NO_RETRY' in replay and snap(f.root)==after
  elif expected=='fixture-drift':
   assert 'IMPORT_REFUSED: SNAPSHOT_CHANGED_BEFORE_IMPORT' in text and not f.attempt.exists() and not f.present(),(name,text[-3500:])
   assert set(before)==set(after) and {k for k in before if before[k]!=after[k]}=={'current.txt'},'UNRELATED_EFFECT_DURING_INJECTED_DRIFT'
  else:assert 'IMPORT_REFUSED: '+expected in text and before==after,(name,expected,text[-3500:])
  RESULTS.append({'Id':name,'Passed':True,'Expected':expected,'Scope':'disposable Windows repository; no operator host or network hydration'})
  print('PASS',name,flush=True)
case('exact-six-historical-blobs-positive-and-replay')
case('cancel-no-local-writes',expected='cancel',token='')
case('wrong-confirmation-no-local-writes',expected='cancel',token='yes')
case('payload-tamper',lambda f:(f.bundle/(OIDS[0]+'.blob')).write_bytes(b'wrong'),expected='PAYLOAD_IDENTITY_MISMATCH')
case('canonical-byte-tamper',lambda f:f.receipts[0].write_bytes(b'wrong'),expected='CANONICAL_RECEIPT_CHANGED')
case('wrong-tag-object',lambda f:git(f.root,'tag','-f','-a','workbench-v0.60.1-accepted',f.head,'-m','different tag'),expected='TAG_OBJECT_MISMATCH')
case('wrong-local-head',lambda f:git(f.root,'update-ref','HEAD',f.second),expected='HEAD_MISMATCH')
case('wrong-ordered-parents',reverse_parents,expected='ORDERED_PARENTS_MISMATCH')
case('unsafe-include-config',lambda f:git(f.root,'config','include.path','missing'),expected='OTHER_UNQUALIFIED_CONFIG')
case('five-missing-is-drift',lambda f:git(f.root,'hash-object','-w','--stdin',data=(f.bundle/(OIDS[0]+'.blob')).read_bytes()),expected='MISSING_SET_DRIFT')
case('current-object-missing',lambda f:remove(f.root,f.current),expected='CURRENT_OBJECT_STATE_DRIFT')
case('existing-consumed-attempt',lambda f:(f.attempt.parent.mkdir(parents=True),f.attempt.write_bytes(b'prior attempt')),expected='ATTEMPT_OR_RECEIPT_EXISTS_NO_RETRY')
case('wrong-git-sha',lambda f:setattr(f,'code',f.code.replace('c470d205517c7a53ceca321df16a6e4549fcd52b576ab4d09536d36f26fda5a9','0'*64)),expected='GIT_IMAGE_MISMATCH')
case('post-confirmation-source-drift-before-first-write',lambda f:setattr(f,'code',f.code.replace('$fresh=Snapshot;','[IO.File]::WriteAllText((Join-Path $root "current.txt"),"fixture concurrent drift"); $fresh=Snapshot;')),expected='fixture-drift')
case('one-object-write-failure-is-terminal',lambda f:setattr(f,'code',f.code.replace('$writeAttempted=$true','if ($completed.Count -eq 1) { throw "FIXTURE_INJECTED_FAILURE" }; $writeAttempted=$true')),expected='partial')
REPORT.write_text(json.dumps({'Schema':'matawaka.offline-six-blob-import-qualification/v0.1','SourceSha':os.environ.get('GITHUB_SHA'),'PowerShell':'Windows PowerShell 5.1','GitVersion':git(pathlib.Path.cwd(),'--version'),'Cases':RESULTS,'OperatorRepositoryContacted':False,'ProductionPushPerformed':False},indent=2),encoding='utf-8')
print('ALL_PASS',len(RESULTS))
