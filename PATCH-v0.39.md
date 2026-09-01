# Workbench v0.39 — Candidate Launch Handoff

Status: candidate-only until full accepted lifecycle completes.

## Exact predecessor

- accepted commit: `612eccf4ff440b27f37fe634205980c3e2f9e0d8`
- accepted tag: `workbench-v0.38.1-accepted`
- target: `0.39.0`
- target tag: `workbench-v0.39-accepted`

## Why

After `Launch candidate`, the predecessor Workbench previously remained open beside the newly launched candidate. That is safe but operator-hostile because two nearly identical Workbench windows remain visible.

v0.39 adds a bounded handoff so a Workbench performing a successful future candidate launch closes **its own predecessor window** automatically only after the launch has been independently evidenced as stable enough for handoff.

## Exact sequence

```text
Existing exact Launch gate
  -> persisted CANDIDATE_LAUNCHED_NOT_ACCEPTED receipt
  -> v0.39 handoff receipt rebind
  -> 750 ms bounded observation
  -> PID still alive
  -> PID process image == exact receipt-bound candidate executable
  -> handoff receipt atomically persisted
  -> current predecessor MainWindow Close()
```

Any failure before the final receipt leaves the predecessor open.

## Invariants

- `Launch Attempt != Predecessor Close`
- `Candidate Started != Candidate Accepted`
- `Launch Receipt Persisted Before Predecessor Close`
- `Live PID != Exact Candidate Process Image`
- `Predecessor Self-Close != External Process-Kill Authority`
- `New Successor Code != Retroactive Authority Over Predecessor Process`

## Authority boundary

The new handoff observer:

- does not launch the candidate; the accepted `BoundedUpdateApplyBuildService` still owns the exact launch gate;
- does not kill, terminate, signal or otherwise control any external process;
- does not create candidate acceptance/checkpoint/publication authority;
- does not use network, Git, catalog mutation, Agent Execute or ActionPermit;
- only returns `PredecessorSelfCloseEligible=true` after exact launch-artifact/candidate/PID/process-image evidence is rebound and its own receipt is persisted.

Success status:

`CANDIDATE_ALIVE_PREDECESSOR_SELF_CLOSE_ELIGIBLE_NOT_ACCEPTED`

## Activation boundary

The launch that enters v0.39 is still executed by the older accepted v0.38.1 executable, so v0.39 cannot safely close that already-running predecessor retroactively. The new behavior becomes active when v0.39 itself later launches its first successor candidate.

## Non-effects

No change to Local Apps updater/builder/registration/receipt-store semantics. No change to non-App Runtime/Protocol/AgentHost/Engine/Catalog/SemanticHost. No candidate acceptance is implied by handoff or predecessor closure.
