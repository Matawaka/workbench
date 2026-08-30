# Start Here — Workbench v0.5

1. Keep the catalog rooted at `K:\Matawaka\Catalog`.
2. Enable the agent only for an intended Observe/Propose run.
3. Keep `git fetch` disabled unless explicitly refreshing refs.
4. Run `propose` with either built-in semantic provider id.
5. Inspect `Semantic Provider` and `Process Boundary` receipts.
6. Verify `IntegrityVerified=true`, `JobObjectApplied=true`, `ActiveProcessLimit=1`, `ProcessMemoryLimitBytes=268435456` and `AssignmentBeforeSemanticInput=true`.
7. Treat `RestrictedToken=false`, `NetworkIsolationEnforced=false`, `OsSandbox=false` and `SameUserSecurityContext=true` as deliberate security facts.
8. Execute remains denied and mutation budget remains zero.
