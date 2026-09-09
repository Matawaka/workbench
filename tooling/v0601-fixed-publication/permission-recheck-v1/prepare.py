"""Build-only permission-change recheck; old production sources and receipts remain immutable."""
import hashlib, json, pathlib, subprocess, sys
here = pathlib.Path(__file__).resolve().parent
out = pathlib.Path(sys.argv[1]).resolve()
subprocess.run([sys.executable, str(here.parent/'discovery-diagnostic-v1/prepare.py'), str(out), sys.argv[2]], check=True)
pins = {
    'GeneratedVerifier.cs':'2dee5a690f573ba5ce445b0b9e9c691cb6c1f69a06a7236ac3102410e5addbdd',
    'V2.cs':'6982e428f9be50005688d3bfeca2f6a922ba9371116ac6e8d7a7457497fb63f8',
    'DiagnosticSupport.cs':'f395d3ce017da4df6ce251fe696e39e816273e29075082d2f9bc39afb8777587',
    'BoundedProbe.cs':'bc6ca3cf41aecbafe8ca5808909672d06f86fb068464637dd0be60ab901cd738',
    'Diagnostic.cs':'1aaf8bc5327f51e4f48509f6dd6bf12592c877e9e15a3c0976e8c6fe24930d6d',
    'DiagnosticTests.cs':'3a3f26d4daa3fc22c71f4e63ea788418749062c1b27621313c90139461a82289'
}
for name, sha in pins.items():
    assert hashlib.sha256((out/name).read_bytes()).hexdigest() == sha, ('INHERITED_QUALIFIED_SOURCE_DRIFT', name)
for name in ['PermissionRecheck.cs', 'PermissionRecheckTests.cs']:
    (out/name).write_bytes((here/name).read_bytes().replace(b'\r\n', b'\n'))
# Reuse only Incident and empty-context creation, NOT either old operator entry point.
# Compiling an old class does not invoke it; the sole new StartupObject is fixed below.
base = (out/'Diagnostic.csproj').read_text(encoding='utf-8')
base = base.replace('<Compile Include="Diagnostic.cs"/>', '<Compile Include="Diagnostic.cs"/><Compile Include="PermissionRecheck.cs"/>')
base = base.replace('Matawaka.Workbench.V0601ReceiveDiscoveryDiagnostic', 'Matawaka.Workbench.V0601PermissionRecheck')
base = base.replace('Matawaka.V0601DiscoveryDiagnostic.DiagnosticEntryPoint', 'Matawaka.V0601PermissionRecheck.PermissionRecheckEntryPoint')
assert 'Publisher.cs"' not in base and 'QUALIFICATION' not in base
(out/'PermissionRecheck.csproj').write_text(base, encoding='utf-8', newline='\n')
test = base.replace('<AssemblyName>Matawaka.Workbench.V0601PermissionRecheck</AssemblyName>', '<AssemblyName>PermissionRecheckTests</AssemblyName><DefineConstants>QUALIFICATION</DefineConstants>')
test = test.replace('Matawaka.V0601PermissionRecheck.PermissionRecheckEntryPoint', 'Matawaka.V0601PermissionRecheck.PermissionRecheckTests')
test = test.replace('</ItemGroup>', '<Compile Include="PermissionRecheckTests.cs"/><Compile Include="V2Tests.cs"/><Compile Include="LegacySourceTests.cs"/><Compile Include="LegacyEvidenceTests.cs"/></ItemGroup>')
(out/'PermissionRecheckTests.csproj').write_text(test, encoding='utf-8', newline='\n')
(out/'permission-recheck-materialization.json').write_text(json.dumps({
    'InheritedSourcePins': pins, 'OriginalFilesChanged': False, 'ProductionPushCompiled': False,
    'PriorAttemptRearmed': False, 'OperatorPermissionChangeIndependentlyVerified': False,
    'ProductionCliOverrides': False, 'NewConfirmationRequired': True
}, indent=2)+'\n', encoding='utf-8', newline='\n')
print('MATERIALIZED_PERMISSION_CHANGE_RECHECK_NO_PUSH', out)
