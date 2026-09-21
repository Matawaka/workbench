# Architecture notes — Jev / System One adapter

Snapshot: 2026-09-20

This document separates three evidence levels so the adapter does not accidentally hard-code a reverse-engineering hypothesis as an API contract.

## A. Published / provider-documented behavior

TypeSafe describes Jev as a System One model: unstructured program state in, typed probabilistic decisions out. The public interface exposes three primitives:

- `Choice`: a closed choice set with probabilities and a confidence summary.
- `Score`: an ordered rubric with probabilities and a confidence summary.
- `Noul`: probability that a yes/no statement is true.

TypeSafe says questions in one request are evaluated in parallel and in isolation against the same state. Their launch material also says the model returns probabilities without autoregressive text generation, and schema/type matching is guaranteed by design.

**Adapter consequence:** the contract is built around state + independent atomic questions. It does not expose generated reasoning text and does not infer authority from schema validity.

Sources:
- https://typesafe.ai/blog/introducing-system-one-models-and-jev
- https://docs.typesafe.ai/introduction

## B. Independently observed behavioral evidence

Archer Hume reports a large black-box probe campaign against `jev-1.13.0`. The observations most relevant to an adapter are:

1. **Sibling question isolation.** Information placed only inside one question was not visible to another question, while moving the same information into shared state changed the other answer.
2. **Choice option order sensitivity.** Reordering otherwise equivalent option lists materially changed some probabilities; in a support classification example, the shift was large enough that a threshold near 0.9 could produce a different downstream branch.
3. **Choice-set interaction.** Adding an irrelevant option changed odds between existing options, inconsistent with a simple fixed independent-logit model followed by an unchanged softmax.
4. **Small run-to-run variation.** Identical requests and duplicate questions showed slight differences, so API-level determinism should not be assumed.
5. **Calibration must be evaluated statistically.** A probability can only be called calibrated over a population of cases, not from one correct or incorrect prediction.
6. **The API `confidence` summary is not a second learned probability of correctness.** For Choice, the observed official adapter computes it from the distribution's concentration above a uniform baseline.

**Adapter consequence:** preserve raw probabilities, audit permutations, retain model/version provenance, and never let one threshold silently become authority.

Source:
- https://archerhume.com/posts/jevs-architecture-unmasked/?v=3

## C. Architectural reconstruction — useful, but not a dependency

The independent article's best reconstruction is roughly:

```text
shared state prefix
      |
      +---- isolated question suffix A ---- listwise options ---- numeric readout
      +---- isolated question suffix B ---- listwise options ---- numeric readout
      +---- isolated question suffix C ---- rubric/options ------ numeric readout
```

It argues that a causal transformer with shared prefix computation is likely; a sparse Mixture-of-Experts backbone is plausible but explicitly uncertain. Final-position and pointer-style readouts are both compatible with current observations.

None of those implementation hypotheses are required by this prototype. The adapter depends only on the externally observable API contract and validates the returned typed evidence.

## Why the Matawaka boundary is stricter than TypeSafe's interface

TypeSafe's architectural promise is primarily about making model outputs directly usable by software. Matawaka adds another requirement:

```text
model-readable probability != policy decision != authority != external effect
```

A well-calibrated model could be 99.9% confident that a request is in scope and still have no power to create authority. Conversely, valid authority may exist while semantic ambiguity is high enough that policy requires human review.

Therefore:

- Jev can describe **intent match**, **scope-expansion likelihood**, **action class**, **ambiguity**, **risk indicators**, or **evidence quality**.
- Jev cannot emit **permit**, **deny**, **approved**, **authorized**, **execute**, or an equivalent normative action token through this adapter.
- Workbench/Authority Runtime remains the only layer that may compose identity, authority source, scope, policy, expiry, replay state, and evidence into an enforceable decision.

## Staging rule

Because questions in one System One request are intentionally independent, dependent workflows must be explicit application stages:

```text
Stage 1: semantic classification
    -> application code derives a new bounded state
Stage 2: follow-up judgment, if genuinely required
    -> authority layer consumes evidence from both stages
```

Do not simulate a hidden chain of thought by making question names look sequential.

## Production qualification before any authority-adjacent use

Before a Jev-backed provider is admitted near an authority workflow:

1. Pin or allowlist concrete model versions during qualification.
2. Build a labeled in-domain fixture set, including hostile/adversarial cases.
3. Measure Brier score and calibration error by decision category.
4. Run Choice permutation tests.
5. Test distribution shift and intentionally ambiguous inputs.
6. Test repeated identical calls and duplicate-question behavior.
7. Treat timeouts, malformed responses, unknown model versions, and missing probabilities as `NO_EVIDENCE`, never as a favorable answer.
8. Keep the actual authority decision in the existing deterministic gate.
