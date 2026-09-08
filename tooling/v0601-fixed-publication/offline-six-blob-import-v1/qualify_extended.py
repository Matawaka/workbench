"""Extend fixtures; diagnostic instrumentation is never delivered to the operator."""
import json,pathlib,runpy,sys,tempfile
runner=pathlib.Path(__file__).with_name('qualify.py'); original=sys.argv[1]
source=pathlib.Path(original).read_text(encoding='utf-8-sig')
source=source.replace('Meta=(Digest $meta); Objects=$obj }','Meta=(Digest $meta); Objects=$obj; DebugMeta=$meta }')
source=source.replace('Meta=$storage.Meta; Objects=$storage.Objects;','Meta=$storage.Meta; DebugMeta=$storage.DebugMeta; Objects=$storage.Objects;')
needle="Need ($after.Refs -ceq $before.Refs -and $after.Meta -ceq $before.Meta -and $after.Sources -ceq $before.Sources) 'PROTECTED_STATE_CHANGED'"
extra='Write-Output ("FIXTURE_STATE refs="+($after.Refs -ceq $before.Refs)+" meta="+($after.Meta -ceq $before.Meta)+" source="+($after.Sources -ceq $before.Sources)); foreach($k in $after.DebugMeta.Keys){if (-not $before.DebugMeta.ContainsKey($k) -or $after.DebugMeta[$k] -cne $before.DebugMeta[$k]) { Write-Output ("FIXTURE_META_CHANGE "+$k) }}; '+needle
source=source.replace(needle,extra)
with tempfile.TemporaryDirectory(prefix='instrument-six-blobs-') as t:
    script=pathlib.Path(t)/'instrument.ps1';script.write_text(source,encoding='utf-8');sys.argv[1]=str(script)
    x=runpy.run_path(str(runner),run_name='__main__');case=x['case']
    case('arguments-refused',expected='ARGUMENTS_NOT_ACCEPTED',extra=('--root','arbitrary'))
    case('text-stdin-close-cannot-change-file-import',lambda f:setattr(f,'code',f.code.replace('$p.StandardInput.BaseStream.Close()','$p.StandardInput.Close()')),expected='success')
    case('forbidden-push-command-refused',lambda f:setattr(f,'code',f.code.replace('$before=Snapshot;','GitBytes "push arbitrary"; $before=Snapshot;')),expected='COMMAND_NOT_ADMITTED')
    case('forbidden-config-write-refused',lambda f:setattr(f,'code',f.code.replace('$before=Snapshot;','GitBytes "config x.y z"; $before=Snapshot;')),expected='CONFIG_WRITE_NOT_AUTHORIZED')
    case('confirmation-expiry-no-write',lambda f:setattr(f,'code',f.code.replace('$previewAt=[DateTime]::UtcNow','$previewAt=[DateTime]::UtcNow.AddSeconds(-301)')),expected='PREVIEW_EXPIRED')
    case('payload-change-after-initial-hash-refused',lambda f:setattr(f,'code',f.code.replace('$before=Snapshot;','[IO.File]::WriteAllBytes((Join-Path $bundle "162aa48105259c20b067ba43e48db35db4f9d34a.blob"),[Text.Encoding]::UTF8.GetBytes("wrong")); $before=Snapshot;')),expected='LOCKED_PAYLOAD_SIZE_MISMATCH')
    p=pathlib.Path(sys.argv[4]);report=json.loads(p.read_text(encoding='utf-8'));report['Cases']=x['RESULTS'];report['LockedFileNoFiltersRegressionQualified']=True
    p.write_text(json.dumps(report,indent=2),encoding='utf-8');sys.argv[1]=original
    print('EXTENDED_ALL_PASS',len(report['Cases']))
