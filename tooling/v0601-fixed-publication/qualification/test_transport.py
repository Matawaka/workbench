"""Real local Git transports; no GitHub, credentials, operator root, or production push.
Tests the guard template proposed for the separately qualified standalone publisher.
"""
from pathlib import Path
import os, subprocess, tempfile, json, hashlib, time
TAG='refs/tags/workbench-v0.60.1-accepted'
TEMPLATE=Path(__file__).resolve().parents[1]/'publisher'/'pre-push.template'
RESULTS=[]
ENV={k:v for k,v in os.environ.items() if not k.upper().startswith(('GIT_','GCM_'))}
ENV.update(GIT_CONFIG_NOSYSTEM='1',GIT_CONFIG_GLOBAL=os.devnull,GIT_TERMINAL_PROMPT='0',GIT_NO_REPLACE_OBJECTS='1',GIT_OPTIONAL_LOCKS='0',GIT_AUTHOR_NAME='Fixture',GIT_AUTHOR_EMAIL='fixture@example.invalid',GIT_COMMITTER_NAME='Fixture',GIT_COMMITTER_EMAIL='fixture@example.invalid')
def run(*a,cwd=None,stdin=None,ok=True):
 p=subprocess.run(['git',*map(str,a)],cwd=cwd,input=stdin,stdout=subprocess.PIPE,stderr=subprocess.PIPE,env=ENV,timeout=20)
 if ok and p.returncode: raise AssertionError((a,p.returncode,p.stderr.decode(errors='replace')))
 return p

def render(head,base,tag,remote):
 text=TEMPLATE.read_text()
 for k,v in {'HEAD':head,'BASE':base,'TAGOID':tag}.items():
  assert len(v)==40 and all(c in '0123456789abcdef' for c in v)
  text=text.replace('@'+k+'@',v)
 return text.replace('@REMOTE@',"'"+str(remote).replace("'","'\\''")+"'")

class Fixture:
 def __init__(self,root):
  self.root=root;self.repo=root/'source'; self.remote=root/'remote.git'; self.view=root/'view.git';self.hooks=root/'hooks'
  run('init','-q',self.repo)
  (self.repo/'one.txt').write_text('base\n');run('add','one.txt',cwd=self.repo);run('commit','-qm','base',cwd=self.repo)
  self.old=run('rev-parse','HEAD',cwd=self.repo).stdout.decode().strip()
  (self.repo/'one.txt').write_text('public\n');run('add','one.txt',cwd=self.repo);run('commit','-qm','public',cwd=self.repo)
  self.base=run('rev-parse','HEAD',cwd=self.repo).stdout.decode().strip()
  (self.repo/'one.txt').write_text('accepted\n');run('add','one.txt',cwd=self.repo)
  tree=run('write-tree',cwd=self.repo).stdout.decode().strip()
  self.head=run('commit-tree',tree,'-p',self.old,'-p',self.base,stdin=b'accepted fixture\n',cwd=self.repo).stdout.decode().strip()
  run('update-ref','refs/heads/fixture-accepted',self.head,cwd=self.repo)
  run('tag','-a',TAG.removeprefix('refs/tags/'),self.head,'-m','fixture accepted',cwd=self.repo)
  self.tag=run('rev-parse',TAG,cwd=self.repo).stdout.decode().strip()
  run('init','--bare','-q',self.remote)
  run('push','-q',self.remote.as_posix(),self.base+':refs/heads/main',cwd=self.repo)
  run('init','--bare','-q',self.view)
  (self.view/'objects/info/alternates').write_text(str(self.repo/'.git/objects').replace('\\','/')+'\n',newline='\n')
  self.hooks.mkdir();self.hook=self.hooks/'pre-push'
  self.hook.write_text(render(self.head,self.base,self.tag,self.remote.as_posix()),newline='\n'); self.hook.chmod(0o700)
  self.before=run('for-each-ref',cwd=self.repo).stdout
 def ref(self,ref):
  p=run('--git-dir='+self.remote.as_posix(),'rev-parse','--verify',ref,ok=False)
  return p.stdout.decode().strip() if p.returncode==0 else None
 def update(self,ref,value):run('--git-dir='+self.remote.as_posix(),'update-ref',ref,value)
 def push(self,extra=(),hook=True):
  hooks=self.hooks if hook else self.root/'no-hooks'
  return run('--git-dir='+str(self.view),'-c','core.hooksPath='+str(hooks),'-c','push.followTags=false','-c','push.recurseSubmodules=no','-c','push.gpgSign=false','push','--atomic','--porcelain','--no-follow-tags','--recurse-submodules=no','--no-signed',self.remote.as_posix(),self.head+':refs/heads/main',self.tag+':'+TAG,*extra,ok=False)
 def unchanged_local(self):assert self.before==run('for-each-ref',cwd=self.repo).stdout

def test(name, fn):
 with tempfile.TemporaryDirectory(prefix='v0601-cas-', ignore_cleanup_errors=True) as d:
  f=Fixture(Path(d));fn(f);f.unchanged_local()
 RESULTS.append({'Id':name,'Passed':True});print('PASS',name)

def happy(f):
 p=f.push();assert p.returncode==0,p.stderr;assert b'V0601_EXACT_ADVERTISEMENT_VERIFIED' in p.stdout+p.stderr
 assert f.ref('refs/heads/main')==f.head and f.ref(TAG)==f.tag
 assert f.ref('refs/tags/workbench-v0.60-accepted') is None
 assert len(run('--git-dir='+f.remote.as_posix(),'for-each-ref',stdin=None).stdout.splitlines())==2

def older_unguarded(f):
 f.update('refs/heads/main',f.old);p=f.push(hook=False)
 assert p.returncode==0

def older_guarded(f):
 f.update('refs/heads/main',f.old);p=f.push();assert p.returncode!=0
 assert f.ref('refs/heads/main')==f.old and f.ref(TAG) is None

def existing_tag(f, same):
 run('push','-q',f.remote.as_posix(),f.tag+':refs/tags/fixture-object-stage',cwd=f.repo)
 f.update(TAG,f.tag if same else f.base)
 p=f.push();assert p.returncode!=0
 assert f.ref('refs/heads/main')==f.base
 assert f.ref(TAG)==(f.tag if same else f.base)

def target_main(f):
 run('push','-q',f.remote.as_posix(),f.head+':refs/heads/main',cwd=f.repo)
 p=f.push();assert p.returncode!=0 and f.ref(TAG) is None

def atomic_unsupported(f):
 run('--git-dir='+f.remote.as_posix(),'config','receive.advertiseAtomic','false')
 p=f.push();assert p.returncode!=0;assert f.ref('refs/heads/main')==f.base and f.ref(TAG) is None

def extra_ref(f):
 p=f.push([f.head+':refs/heads/extra']);assert p.returncode!=0
 assert f.ref('refs/heads/main')==f.base and f.ref(TAG) is None and f.ref('refs/heads/extra') is None

def after_advertisement(f,tag=False):
 f.hook.write_text(f.hook.read_text()+f"git --git-dir='{f.remote.as_posix()}' update-ref '{TAG if tag else 'refs/heads/main'}' '{f.base if tag else f.old}'\n",newline='\n')
 p=f.push();assert p.returncode!=0
 assert f.ref('refs/heads/main')==(f.base if tag else f.old)
 assert f.ref(TAG)==(f.base if tag else None)

def hostile_config(f):
 marker=f.root/'BAD_HOOK';bad=f.root/'bad-hooks';bad.mkdir()
 h=bad/'pre-push';h.write_text('#!/bin/sh\ntouch "'+str(marker).replace('\\','/')+'"\nexit 1\n',newline='\n');h.chmod(0o700)
 run('config','core.hooksPath',str(bad),cwd=f.repo)
 run('config','url.'+str(f.root/'elsewhere')+'.insteadOf',f.remote.as_posix(),cwd=f.repo)
 run('config','push.followTags','true',cwd=f.repo)
 run('config','push.pushOption','UNREVIEWED',cwd=f.repo)
 happy(f); assert not marker.exists()

def rejects_input(f,lines):
 p=subprocess.run(['sh',str(f.hook),f.remote.as_posix(),f.remote.as_posix()],input=lines(f).encode(),stdout=subprocess.PIPE,stderr=subprocess.PIPE,env=ENV,timeout=5)
 assert p.returncode!=0

def mainline(f):return f'{f.head} {f.head} refs/heads/main {f.base}\n'
def tagline(f):return f'{f.tag} {f.tag} {TAG} '+('0'*40)+'\n'

if __name__=='__main__':
 test('atomic-two-ref-exact-happy-path',happy)
 test('demonstrate-atomic-alone-insufficient-for-old-main',older_unguarded)
 test('exact-advertisement-refuses-benign-ancestor-drift',older_guarded)
 test('existing-identical-target-tag-refused',lambda f:existing_tag(f,True))
 test('conflicting-target-tag-refused',lambda f:existing_tag(f,False))
 test('main-already-target-refused-no-tag-only-fallback',target_main)
 test('unsupported-atomic-no-sequential-fallback',atomic_unsupported)
 test('unexpected-third-ref-refused',extra_ref)
 test('server-main-race-after-advertisement-atomic-CAS-refusal',lambda f:after_advertisement(f))
 test('server-tag-race-after-advertisement-atomic-CAS-refusal',lambda f:after_advertisement(f,True))
 test('isolated-view-ignores-source-rewrites-hooks-push-config',hostile_config)
 test('duplicate-main-line-refused',lambda f:rejects_input(f,lambda f:mainline(f)*2+tagline(f)))
 test('duplicate-tag-line-refused',lambda f:rejects_input(f,lambda f:mainline(f)+tagline(f)*2))
 test('missing-line-refused',lambda f:rejects_input(f,mainline))
 test('extra-field-refused',lambda f:rejects_input(f,lambda f:mainline(f).strip()+' extra\n'+tagline(f)))
 test('wrong-new-head-refused',lambda f:rejects_input(f,lambda f:mainline(f).replace(f.head,f.old)+tagline(f)))
 test('wrong-old-main-refused',lambda f:rejects_input(f,lambda f:mainline(f).replace(f.base,f.old)+tagline(f)))
 test('wrong-tag-object-refused',lambda f:rejects_input(f,lambda f:mainline(f)+tagline(f).replace(f.tag,f.base)))
 test('empty-advertisement-refused',lambda f:rejects_input(f,lambda f:''))
 report={'Schema':'matawaka.workbench-v0601-publication-transport-qualification/v0.1','Environment':'isolated local Git fixture transports','GitVersion':run('--version').stdout.decode().strip(),'TemplateSha256':hashlib.sha256(TEMPLATE.read_bytes()).hexdigest(),'Checks':RESULTS,'Passed':all(x['Passed'] for x in RESULTS),'ProductionRemoteContacted':False,'OperatorHostAccessed':False,'ProductionPublicationPerformed':False}
 out=Path(__file__).with_name('transport-qualification.json');out.write_text(json.dumps(report,indent=2)+'\n')
 print('TRANSPORT_QUALIFICATION_PASS',len(RESULTS))
