# Prototype status — alpha.4

Date: 2026-09-21

## Evidence state

- TypeSafe early access: **GRANTED**.
- Alpha.3 smoke: **PASS** against `jev-1.13.0`.
- Alpha.3 standard qualification: **MIXED / HOLD**.
- Reviewed standard receipt SHA-256: `26691a2d31c6d0b4ad33a7c8fa8b93a0799b10154d9cb3499673ef02dd91580e`.
- Alpha.4 offline suite: **14/14 GREEN**.
- Alpha.4 live qualification: **NOT YET RUN**.

## Why alpha.4 exists

The alpha.3 standard run exposed two harness issues that matter for safe automation:

1. `ambiguous-cleanup.intentMatch` mixed goal alignment with operational specificity, creating all 3 failed checks from one ambiguous concept.
2. `permutationUnstableChoices=0` hid substantial probability drift. `embedded-instruction` kept the same winning label while a label probability moved by up to 0.31 and the minimum winner margin fell to about 0.02.

Alpha.4 therefore improves the measurement surface before any shadow-mode admission.

## Implemented in alpha.4

- Split `goalAlignment` from `operationalSpecificity`.
- Replaced overlapping authority-adjacent effect Choice semantics with orthogonal Nouls:
  - `causesExternalMutation`
  - `hasReliableRollback`
  - `externalCommunication`
- Retained Choice only for mutually exclusive `targetSurface` diagnostics.
- Choice permutation audit schema v0.2.
- Per-permutation provenance:
  - concrete model ID
  - provider request ID
  - request digest
  - response digest
- `maxProbabilitySpread` and `minWinnerMargin` metrics.
- Explicit `materialProbabilityDrift`, `thinWinnerMargin`, and `thresholdRelevantInstability` flags.
- Qualification report schema v0.2 with aggregate threshold-relevant instability counts.
- 14 automated tests GREEN.
- Offline demo updated to the decomposed semantics.

## Still not qualified

- Alpha.4 live behavior on the revised fixtures.
- Deployment calibration on Matawaka-labeled data.
- Any production threshold policy.
- Workbench C# shadow-mode bridge.
- Receipt signing / durable Workbench receipt integration.
- Concrete model-version admission policy.
- Any authority-path consumption.

## Next evidence trigger

After fetching the alpha.4 branch locally:

1. run `npm.cmd test`;
2. run `npm.cmd run qualify:smoke`;
3. review the v0.2 receipt;
4. run `npm.cmd run qualify:live` only if smoke is structurally sound.

Do **not** run `qualify:deep` yet.

No Jev signal may alter production authority before a separate shadow-mode design and review.
