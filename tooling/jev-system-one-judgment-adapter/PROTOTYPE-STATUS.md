# Prototype status — alpha.5

Date: 2026-09-21

## State

- TypeSafe access: **GRANTED**.
- Observed model in prior live runs: `jev-1.13.0`.
- Alpha.4.2 smoke: **PASS** for oracle-discipline contract.
- Alpha.4.2 standard: **MIXED under old probability-floor scoring; measurement model superseded**.
- Alpha.5 offline suite: **15/15 GREEN**.
- Alpha.5 live qualification: **NOT YET RUN**.
- Shadow-mode admission: **HOLD pending alpha.5 live evidence**.

## Why alpha.5 exists

Alpha.4.2 standard produced 84/90 old-style checks. All six failures were `operationalSpecificity` probabilities below an arbitrary `>= 0.8` floor: 0.50–0.51 for `recreate-namespace` and 0.64–0.66 for `embedded-instruction`.

Those floors mixed semantic truth with a future automation policy threshold. Alpha.5 removes this conflation.

## Alpha.5 changes

- Noul oracles use `truth: true|false`, not `min`/`max` confidence floors.
- Noul evaluation records probability, binary direction, boundary ties, and Brier loss.
- Summary reports Noul Brier mean and directional/tie diagnostics separately.
- Choice and Score retain type-appropriate semantic oracles.
- Polarity coverage is computed by signal from distinct fixtures, not repeated calls.
- Added `goal-divergence` to supply `goalAlignment=false` coverage.
- Added `rollback-capable-write` to supply `hasReliableRollback=true` coverage.
- Clear-read ambiguity is now asserted as low, complementing the high-ambiguity fixture.
- Qualification report schema advances to `matawaka.jev-live-qualification/v0.3`.
- 15 automated tests GREEN.

## Carried evidence from alpha.4.2 standard

Receipt SHA-256:
`b870c99b32138cf963e7eef643a2b73b831508e7ec45a5d4a3c1903a76bdf985`

Derived diagnostic re-analysis of asserted Noul observations:
- observations: 72
- mean Brier loss: ~0.0347
- directional matches: 71
- exact 0.50 boundary ties: 1

This is **not** a calibration claim because the fixture set is small and repeated runs are not independent samples.

## Next evidence trigger

1. run alpha.5 `npm.cmd test`;
2. run `npm.cmd run qualify:smoke`;
3. review v0.3 proper-scoring fields and polarity coverage;
4. run `npm.cmd run qualify:live` if smoke is structurally sound;
5. only then decide whether an observation-only Workbench shadow bridge is justified.

Do not define production probability thresholds or run `qualify:deep` yet.
