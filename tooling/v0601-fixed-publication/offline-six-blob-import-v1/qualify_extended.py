"""Extend the fixture runner; instrumented copies are never delivered."""
import json,pathlib,runpy,sys,tempfile
runner=pathlib.Path(__file__).with_name('qualify.py')
original=sys.argv[1]
source=pathlib.Path(original).read_text(encoding='utf-8-sig')
needle='Need ($probe -ceq $o[0])'
instrument='if ($probe -cne $o[0]) { Write-Output ("FIXTURE_CHANNEL oid="+$o[0]+" inputBytes="+$payload[$o[0]].Length+" inputSha="+(Sha $payload[$o[0]])+" returned="+$probe) }; '+needle
with tempfile.TemporaryDirectory(prefix='instrument-six-blob-') as t:
    script=pathlib.Path(t)/'fixture-instrumented.ps1';script.write_text(source.replace(needle,instrument),encoding='utf-8');sys.argv[1]=str(script)
    x=runpy.run_path(str(runner),run_name='__main__')
    case=x['case']
    case('arguments-refused',expected='ARGUMENTS_NOT_ACCEPTED',extra=('--root','arbitrary'))
    case('text-writer-close-regression-refused-before-write',lambda f:setattr(f,'code',f.code.replace('$p.StandardInput.BaseStream.Close()','$p.StandardInput.Close()')),expected='PAYLOAD_GIT_CHANNEL_ID_MISMATCH')
    case('forbidden-push-command-refused',lambda f:setattr(f,'code',f.code.replace('$before=Snapshot;','GitBytes "push arbitrary"; $before=Snapshot;')),expected='COMMAND_NOT_ADMITTED')
    case('forbidden-config-write-refused',lambda f:setattr(f,'code',f.code.replace('$before=Snapshot;','GitBytes "config x.y z"; $before=Snapshot;')),expected='CONFIG_WRITE_NOT_AUTHORIZED')
    case('confirmation-expiry-no-write',lambda f:setattr(f,'code',f.code.replace('$previewAt=[DateTime]::UtcNow','$previewAt=[DateTime]::UtcNow.AddSeconds(-301)')),expected='PREVIEW_EXPIRED')
    p=pathlib.Path(sys.argv[4]); report=json.loads(p.read_text(encoding='utf-8')); report['Cases']=x['RESULTS'];report['RawStdinNoWriteRegressionQualified']=True
    p.write_text(json.dumps(report,indent=2),encoding='utf-8');sys.argv[1]=original
    print('EXTENDED_ALL_PASS',len(report['Cases']))
