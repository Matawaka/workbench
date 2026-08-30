# Start Here — v0.7

1. Keep `K:\Matawaka\Catalog` read-only unless an explicit catalog fetch is separately enabled.
2. Enable the Agent only for a deliberate Observe/Propose run.
3. `execute` remains denied.
4. Inspect **Process Boundary** after a successful propose. v0.7 should show `RuntimeSecurityAttestationVerified=true` and `AttestationBeforeSemanticInput=true`.
5. The attested child should report Low integrity (`S-1-16-4096`), token restrictions, Job membership, same user SID, no AppContainer, and no enabled privilege beyond `SeChangeNotifyPrivilege`.
6. `OsSandbox=false` and `NetworkIsolationEnforced=false` remain expected.
