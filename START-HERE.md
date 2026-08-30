# Start Here — Workbench v0.6

1. Keep `K:\Matawaka\Catalog` as the local evidence catalog.
2. Enable the agent only for explicit Observe/Propose tests.
3. Keep `git fetch` disabled unless a catalog refresh is intentionally requested.
4. Run the same provider inputs used in accepted v0.5 and compare semantic `InputDigest` / `OutputDigest`.
5. Inspect `Process Boundary` and confirm restricted-token + Low-integrity + Job Object facts.
6. Run an `execute` negative test and confirm denial occurs before evidence/provider process creation.

Expected process-boundary facts for a successful v0.6 propose:

```text
RestrictedToken=true
MaximumPrivilegesDisabled=true
LowIntegrityLevel=true
IntegrityLevelSid=S-1-16-4096
CreatedSuspended=true
JobAssignmentBeforeResume=true
JobObjectApplied=true
KillOnJobClose=true
ActiveProcessLimit=1
ProcessMemoryLimitBytes=268435456
BreakawayAllowed=false
NetworkIsolationEnforced=false
OsSandbox=false
SameUserIdentity=true
SameUserSecurityContext=false
```

v0.6 intentionally stops before AppContainer/network/filesystem namespace isolation and before any Execute authority.
