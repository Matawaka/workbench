# Workbench v0.55.2 — converged acceptance/publication closure

v0.55.2 is a fresh successor after the v0.55.1 first-boot candidate failed closed and was recovered without ref/publication effects.

It converges two distinct, already evidenced lines without reinterpreting either:

- local accepted v0.55 bounded one-shot model invocation (`02d81b8559bc7c9676949be0557d20ecb50a9890` / `workbench-v0.55-accepted`);
- public PR #82 provenance-bound runtime execution lease (`6111fdf82a9e8947a7722e9b603c1e9268a19105`).

Invariant:

`Provenance-bound runtime execution authority != Model request authority`.

The historical exact v0.55 title assertion remains historical. v0.55.2 reuses the accepted v0.54.2 predecessor harness, reruns v0.55 model service/parser semantics directly, checks its own title separately, and revalidates exact real-host/recovery/no-ref-import evidence.

Local acceptance creates one exact two-parent commit only after PASS:

1. first parent — exact local accepted v0.55 `02d81b...`;
2. second parent — exact imported public #82 main `6111fdf...`.

Publication is separate. Preview is local/evidence-only. After explicit human confirmation only, the fixed publisher may fast-forward the exact two-parent accepted commit to `refs/heads/main` and publish only `workbench-v0.55.2-accepted` to `https://github.com/Matawaka/workbench.git`.

No force push, arbitrary remote/ref, intermediate v0.55 tag promotion, failed v0.55.1 tag publication, automatic retry, source mutation during publication, artifact acquisition, runtime materialization/execution, model invocation, benchmark, game, display, ResponseAuthority, Agent Execute, ActionPermit or SuccessorPermit is authorized by this closure.
