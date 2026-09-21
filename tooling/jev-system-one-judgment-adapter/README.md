# Matawaka System One Judgment Adapter — alpha.5

A dependency-free research prototype for using TypeSafe Jev / System One models as **non-normative judgment evidence** inside Matawaka/Workbench.

> **Invariant:** `Probabilistic Judgment ≠ Authorization`.

## What alpha.5 changes

Alpha.4.2 showed that a semantic oracle and an automation confidence threshold are different things. A Noul answer of 0.65 to a proposition whose ground-truth label is `true` is not automatically a wrong answer merely because a future autonomous policy might require `p >= 0.8`.

Alpha.5 therefore separates:

1. **semantic oracle truth** — `truth: true|false` for Noul questions;
2. **probabilistic quality** — Brier loss and direction/tie diagnostics;
3. **automation/admission policy** — deliberately out of the fixture oracle and not yet defined.

No probability threshold in alpha.5 creates authority.

## Qualification surface

- Noul: binary truth oracle + probability + Brier loss
- Choice: categorical oracle + full distribution
- Score: explicit numeric/ordinal range oracle
- Questions without a defensible oracle remain observation-only

Each live report also contains polarity coverage for every asserted Noul signal. The standard fixture catalog now includes both `true` and `false` examples for:

- `goalAlignment`
- `operationalSpecificity`
- `scopeExpansion`
- `causesExternalMutation`
- `hasReliableRollback0
- `externalCommunication`

Additional fixtures provide a negative goal-alignment example and a positive reliable-rollback example.

## Evidence carried forward

Reviewed alpha.4.2 standard receipt SHA-256:

`b870c99b32138cf963e7eef643a2b73b831508e7ec45a5d4a3c1903a76bdf985`

That run reported 84/90 under the old hard probability-floor checks. Re-analysis as probabilistic binary forecasts produced 72 asserted Noul observations, mean Brier loss about 0.0347, 71 directional matches, and one exact 0.50 boundary tie. This is diagnostic only: repeated synthetic examples are not independent calibration data.

## Run

```powershell
npm.cmd test
npm.cmd run qualify:smoke
npm.cmd run qualify:live
```

`qualify:deep` remains gated.

## Authority boundary

The adapter emits evidence only. Identity, authority source, scope, expiry, deterministic policy, replay protection, and effect execution remain Workbench / Authority Runtime responsibilities.
