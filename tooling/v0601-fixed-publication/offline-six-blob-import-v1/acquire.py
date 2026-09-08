"""CI preparation only. Public reads; never operator hydration or publication."""
import base64, hashlib, json, os, pathlib, subprocess, sys, urllib.request
OIDS = '162aa48105259c20b067ba43e48db35db4f9d34a 3b542376bae6ed3ab5a5fdd232cdfd96b453c586 5fdd8744827ddd202e317174c937761fb20a81e2 81aab74193548f4061853abb4a6cfcaea25975c2 c363c499acc2fd5a93624e3ad51d9320ea48e7af f68e906e127777b1cf918b75829b8f692cecc682'.split()
MAIN = 'ac083598711caa0c399cc0d2c385b980c083024a'
def main():
    out = pathlib.Path(sys.argv[1]); out.mkdir(parents=True, exist_ok=False)
    env = {k:v for k,v in os.environ.items() if not k.upper().startswith(('GIT_', 'GCM_', 'GH_'))}
    env.update(GIT_CONFIG_NOSYSTEM='1', GIT_CONFIG_GLOBAL='NUL', GIT_TERMINAL_PROMPT='0')
    repo = out.parent/'public-object-classification.git'
    def git(*args):
        p = subprocess.run(['git', *args], env=env, capture_output=True, timeout=120)
        if p.returncode: raise RuntimeError('CI_PUBLIC_OBJECT_READ_FAILED')
        return p.stdout
    git('init','--bare',str(repo))
    git('-C',str(repo),'-c','credential.helper=','fetch','--no-tags','--no-write-fetch-head','https://github.com/Matawaka/workbench.git',MAIN)
    reachable = git('-C',str(repo),'rev-list','--objects',MAIN).decode().splitlines()
    names = {x.split(' ',1)[0]:x.split(' ',1)[1] if ' ' in x else None for x in reachable}
    entries = []
    for oid in OIDS:
        assert oid in names, 'OBJECT_NOT_REACHABLE_FROM_FIXED_PUBLIC_MAIN'
        kind = git('-C',str(repo),'cat-file','-t',oid).decode().strip(); assert kind == 'blob'
        data = git('-C',str(repo),'cat-file','blob',oid)
        assert len(data) <= 128*1024
        assert hashlib.sha1(('blob '+str(len(data))+'\0').encode()+data).hexdigest() == oid
        # Independent API transport plus raw Git object transport must produce the same bytes.
        url = 'https://api.github.com/repos/Matawaka/workbench/git/blobs/'+oid
        req = urllib.request.Request(url, headers={'Accept':'application/vnd.github+json','User-Agent':'Matawaka-exact-six-blob-audit'})
        with urllib.request.urlopen(req,timeout=30) as r: payload=json.load(r)
        assert payload['sha']==oid and payload['encoding']=='base64' and payload['size']==len(data)
        assert base64.b64decode(payload['content'])==data
        (out/(oid+'.blob')).write_bytes(data)
        entries.append({'Oid':oid,'Type':kind,'Bytes':len(data),'Sha256':hashlib.sha256(data).hexdigest(),'PublicHistoryPath':names[oid],'SourceUrl':url,'ReachableFromFixedPublicMain':True})
    assert len(entries)==6
    manifest={'Schema':'matawaka.workbench-six-historical-blobs/v0.1','SourceSha':os.environ['GITHUB_SHA'],'FixedPublicMain':MAIN,'Objects':entries,'TotalBlobBytes':sum(x['Bytes'] for x in entries),'GitAndApiBytesMatched':True,'OperatorObjectDatabaseWritten':False,'PublicationAuthorized':False}
    (out/'six-blobs-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(manifest,indent=2))
if __name__=='__main__':main()
