# Matawaka System One Judgment Adapter — alpha.4.2

A dependency-free research prototype for using TypeSafe Jev / System One models as **non-normative judgment evidence** inside Matawaka/Workbench.

> **Invariant:** `Probabilistic Judgment ≠ Authorization`.

## Current state

- TypeSafe access: granted.
- Observed concrete model: `jev-1.13.0`.
- Alpha.3 standard qualification: **MIXED / HOLD**.
- Alpha.4.1 smoke: **MIXED / ORACLE-AMBIGUITY**, not a model admission failure.
- Offline alpha.4.2 suite: **14/14 GREEN**.
- Shadow-mode admission: **not granted**.

## Oracle discipline

A qualification expectation is asserted only when the fixture supplies a defensible semantic oracle.

Every judgment is still recorded, but questions without a defensible binary/threshold ground truth are **observation-only**. They do not become PASS/FAIL checks merely because the harness happens to contain a convenient threshold.

Each case now records:

- `oracle.assertedQuestionIds`
- `oracle.observationOnlyQuestionIds`
- asserted/observation-only counts
- oracle coverage
- optional oracle notes

The global summary separately reports asserted and observation-only questions.

For `scope-smuggling`, `operationalSpecificity` is now observation-only. The fixture is designed to establish scope expansion and destructive mutation; it does not establish a defensible ground-truth threshold for whether an explicit compound sequence is "specific enough."

## Judgment surface

- `goalAlignment` — Noul
- `operationalSpecificity` — Noul
- `scopeExpansion` — Noul
- `causesExternalMutation` — Noul
- `hasReliableRollback` — Noul
- `externalCommunication` — Noul
- `targetSurface` — Choice, diagnostic only
- `ambiguity` — Score

## Qualification evidence

Receipts bind harness version/revision and fixture digests, and record repeat variance plus Choice permutation probability drift and winner margins.

Smoke uses two Choice orders; standard uses six; deep remains gated.

## Run

```powershell
npm.cmd test
npm.cmd run qualify:smoke
npm.cmd run qualify:live
```

Do **not** run `qualify:deep` until the alpha.4.2 standard receipt has been reviewed.

## Authority boundary

`Probabilistic Judgment != Authorization` remains unchanged. Model outputs are evidence only; Workbench/Authority Runtime remains the only layer allowed to compose identity, authority source, scope, policy, expiry, replay state, and evidence into an enforceable decision.
