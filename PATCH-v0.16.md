# Matawaka Workbench v0.16 — Maintenance Recovery Assessment

v0.16 follows the accepted v0.15 self-hosted update repeatability proof with an observation-only recovery layer.

New surface:

- `Recovery check` button and `Recovery` tab;
- fixed read-only Git inspection of `HEAD`, tags, and porcelain status;
- bounded observation of Workbench-local update receipts, source-backup roots, and built-candidate roots;
- categorical classification: `CLEAN_ACCEPTED`, `CLEAN_ACCEPTED_WITH_STALE_MAINTENANCE_EVIDENCE`, `BOUNDED_DIRTY_UPDATE_CANDIDATE`, or `UNKNOWN_DIRTY_WORKTREE`;
- a receipt under `artifacts/recovery-assessments`.

The assessment is deliberately not a recovery action. It cannot rollback, delete, restore, apply source, build, checkpoint, fetch/push, mutate Matawaka catalog repositories, access the network, or grant Agent Execute.

This version is the observation predecessor for a later separately authorized recovery-plan / rollback gate. Observation of a recoverable state must not mint recovery authority.
