# Workbench v0.19 — Isolated Recovery Drill

This increment adds a human-confirmed recovery drill that exercises the accepted recovery assessment, planning and execution services against a nested Git fixture under `.workbench/recovery-drills`.

The drill intentionally creates a tiny accepted fixture repository, materializes one exact interrupted candidate with one tracked `Replace` and one untracked `Add`, binds those bytes to local staged/apply evidence, requires the real recovery services to classify `BOUNDED_DIRTY_UPDATE_CANDIDATE`, produce `READY_FOR_SEPARATE_RECOVERY_AUTHORITY`, execute exact recovery, and then prove a fresh clean assessment.

The main Workbench repository must be clean and tagged `workbench-v0.19-accepted` before the drill can run. Its HEAD, tags and dirty set are rechecked after the drill and must be unchanged. No dotnet build, checkpoint, network, catalog mutation or Agent Execute authority is part of the drill. Fixture Git mutation is limited to the nested drill repository and retained as evidence.

This is a bounded recovery demonstration, not proof that every failure mode is recoverable.
