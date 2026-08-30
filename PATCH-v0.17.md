# Matawaka Workbench v0.17 — Recovery Plan Gate

v0.17 adds a read-only recovery planning layer over the v0.16 recovery assessment.

The new boundary is explicit:

`Recovery Assessment -> Recovery Plan -> separate future Recovery Authority`

A recovery plan is byte-bound to the assessment artifact, re-verifies the current Workbench Git state, and fails closed if the assessment is stale.

For a clean accepted Workbench with stale maintenance evidence, the plan chooses retention by default: stale backup/candidate evidence is not treated as garbage and no deletion is inferred.

For a bounded dirty update candidate, the plan may describe exact future restore/removal steps, but every mutating step remains marked as requiring separate authority. Unknown dirty worktrees are refused rather than generalized into a rollback plan.

Non-effects:
- no source mutation;
- no restore or rollback;
- no file/directory deletion;
- no build/publish;
- no Git checkpoint/fetch/push;
- no network access;
- no catalog mutation;
- no Agent Execute / ActionPermit;
- only a Workbench-local recovery-plan receipt may be written.
