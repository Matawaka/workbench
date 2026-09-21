# Matawaka System One Judgment Adapter — alpha.4.1

A dependency-free research prototype for using TypeSafe Jev / System One models as **non-normative judgment evidence** inside Matawaka/Workbench.

> **Invariant:** `Probabilistic Judgment ≠ Authorization`.

The adapter may classify, score, estimate uncertainty, and emit probability distributions. It cannot issue authority, permits, approvals, executions, or external effects.

## Current qualification state

- TypeSafe access: granted.
- Observed concrete model: `jev-1.13.0`.
- Alpha.3 standard qualification: **MIXED / HOLD**.
- Alpha.4 pre-fix smoke: **MIXED / HARNESS-WORDING ISSUE**.
- Offline suite after alpha.4.1 correction: **14/14 GREEN**.
- Shadow-mode admission: **not granted**.

## Judgment surface

Authority-adjacent semantics are decomposed into independent questions:

- `goalAlignment` — Noul
- `operationalSpecificity` — Noul
- `scopeExpansion` — Noul
- `causesExternalMutation` — Noul
- `hasReliableRollback` — Noul
- `externalCommunication` — Noul
- `targetSurface` — Choice, diagnostic only
- `ambiguity` — Score

`operationalSpecificity` asks whether the requested action **or action sequence** is precise enough to classify its effects without unresolved materially different alternatives. A compound sequence is not automatically ambiguous.

## Qualification evidence

The live runner records:

- requested model alias and concrete observed model;
- TypeSafe request IDs;
- canonical request/response SHA-256 digests;
- repeat variance;
- Choice option-order permutations;
- maximum probability spread;
- minimum winner margin;
- threshold-relevant instability;
- harness package version;
- harness Git revision when available;
- full fixture-catalog digest;
- selected-fixture digest and IDs.

Smoke now executes **two Choice orders** so the permutation metric is exercised rather than trivially reporting a one-order zero.

## Run

PowerShell:

```powershell
npm.cmd test
npm.cmd run qualify:smoke
npm.cmd run qualify:live
```

Do **not** run `qualify:deep` until the standard alpha.4.1 receipt has been reviewed.

## Authority boundary

```text
Jev / System One
      |
      v
Probabilistic Judgment Evidence
      |
      |  no authority
      v
Workbench / Authority Runtime
      |
      v
permit / deny / human review
```

Provider failure, low confidence, semantic ambiguity, or a high probability never creates authority by itself.

See:
- `docs/QUALIFICATION-2026-09-21-STANDARD.md`
- `docs/LIVE-QUALIFICATION.md`
- `docs/THREAT-MODEL.md`
