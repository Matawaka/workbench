"""Extend the unchanged fixture runner; production script is never patched for delivery."""
import json,pathlib,runpy,sys
runner=pathlib.Path(__file__).with_name('qualify.py')
x=runpy.run_path(str(runner),run_name='__main__')
case=x['case']
case('arguments-refused',expected='ARGUMENTS_NOT_ACCEPTED',extra=('--root','arbitrary'))
case('text-writer-close-regression-refused-before-write',lambda f:setattr(f,'code',f.code.replace('$p.StandardInput.BaseStream.Close()','$p.StandardInput.Close()')),expected='PAYLOAD_GIT_CHANNEL_ID_MISMATCH')
case('forbidden-push-command-refused',lambda f:setattr(f,'code',f.code.replace('$before=Snapshot;','GitBytes "push arbitrary"; $before=Snapshot;')),expected='COMMAND_NOT_ADMITTED')
case('forbidden-config-write-refused',lambda f:setattr(f,'code',f.code.replace('$before=Snapshot;','GitBytes "config x.y z"; $before=Snapshot;')),expected='CONFIG_WRITE_NOT_AUTHORIZED')
case('confirmation-expiry-no-write',lambda f:setattr(f,'code',f.code.replace('$previewAt=[DateTime]::UtcNow','$previewAt=[DateTime]::UtcNow.AddSeconds(-301)')),expected='PREVIEW_EXPIRED')
p=pathlib.Path(sys.argv[4]); report=json.loads(p.read_text(encoding='utf-8')); report['Cases']=x['RESULTS'];report['RawStdinNoWriteRegressionQualified']=True
p.write_text(json.dumps(report,indent=2),encoding='utf-8')
print('EXTENDED_ALL_PASS',len(report['Cases']))
