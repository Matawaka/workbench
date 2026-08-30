# Matawaka Workbench v0.18 — bounded recovery execution gate

v0.18 adds the first explicit recovery **execution** gate. It does not broaden normal update authority and is intentionally unavailable for a clean accepted repository.

## Gate sequence

`Recovery check -> Recovery plan -> explicit Recovery execute confirmation -> exact recovery -> fresh Recovery check`

The execution gate is eligible only when the bound assessment is `BOUNDED_DIRTY_UPDATE_CANDIDATE` and the bound plan is `READY_FOR_SEPARATE_RECOVERY_AUTHORITY`.

Before any mutation, v0.18 re-verifies:

- assessment and plan artifacts are Workbench-local and still match their in-memory receipts;
- HEAD, accepted tags, and dirty path set are unchanged;
- Git status is limited to worktree-only tracked modifications (` M`) and untracked additions (`??`);
- every dirty path is byte-bound to one exact prior staged apply-plan receipt for the current accepted HEAD;
- every current dirty file SHA-256 equals the corresponding staged candidate SHA-256.

## Allowed recovery effects

After a separate human confirmation, recovery may only:

- restore an exact tracked dirty candidate path from the current accepted `HEAD:<path>` blob bytes;
- delete an untracked path only when its current SHA-256 exactly matches the bound staged `Add` candidate bytes;
- write recovery authority/execution receipts under `artifacts/recovery-executions`.

The transaction must end at the same HEAD/tags with a clean working tree. It then requires a new observation-only `Recovery check`.

## Explicit non-effects

Recovery execution does **not** authorize:

- build, restore, test, or publish;
- local checkpoint/commit/tag;
- git fetch/push or remote mutation;
- network access;
- Matawaka catalog mutation;
- Agent Execute or ActionPermit;
- deletion of retained backup/candidate/evidence roots;
- restoration from an arbitrary backup or external path.

A recovery plan is still not recovery authority. A recovery authority is single-transition, explicit, byte-bound, and cannot be reused after the observed state changes.
