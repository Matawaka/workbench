# Matawaka System One Judgment Adapter ‚Äî alpha.3

A dependency-free Node.js prototype for integrating TypeSafe Jev / System One-style models as **non-normative judgment evidence** inside Matawaka/Workbench.

> **Invariant:** `Probabilistic Judgment ‚â† Authorization`.
>
> This package can classify, score, estimate ambiguity, and emit probability distributions. It cannot issue authority, permits, approvals, or external effects.

## Why this exists

TypeSafe's Jev is designed as `state + typed questions -> typed probabilistic answers`, with Choice, Score, and Noul evaluated in parallel against shared state. That is useful for the fuzzy semantic layer between a user's request and deterministic policy code.

Matawaka needs an additional boundary: model judgment may be evidence for an authority decision, but it must never *become* the authority decision. This prototype makes that boundary executable.

## Status

- Works offline with a fixture provider.
- Contains a direct Jev HTTP provider for `POST https://api.typesafe.ai/v1/systemone`.
- Does not require the official SDK.
- TypeSafe early access has been granted; live execution now requires only a locally supplied `TYPESAFE_API_KEY`.
- Captures `GET /v1/models` inventory and `x-typesafe-request-id` provenance.
- Includes smoke, standard, and deep live qualification profiles.
- 13 automated tests currently pass on Node 22.

## Run

```bash
npm test
npm run demo
```

With TypeSafe access granted, supply the key only through the local environment:

```bash
export TYPESAFE_API_KEY='...'
npm run qualify:smoke
npm run qualify:live
```

Do not commit or paste the API key into receipts.

## Core contract

The adapter returns an evidence envelope like:

```json
{
  "schema": "matawaka.judgment-evidence/v0.1",
  "evidenceKind": "PROBABILISTIC_JUDGMENT",
  "normativeEffect": "NONE",
  "principle": "PROBABILISTIC_JUDGMENT_IS_NOT_AUTHORIZATION",
  "authorityIssuance": "OUT_OF_SCOPE",
  "sideEffects": "OUT_OF_SCOPE",
  "provider": "typesafe.jev",
  "requestedModel": "jev-latest",
  "observedModel": "jev-1.13.0",
  "requestDigest": "sha256:...",
  "responseDigest": "sha256:...",
  "judgments": {}
}
```

The envelope deliberately has **no** `permit`, `deny`, `authorized`, `execute`, or approval field.

## Recommended Matawaka placement

```text
Agent / KONTUR semantic request
        |
        v
SystemOneJudgmentAdapter
  - intentMatch (Noul)
  - scopeExpansion (Noul)
  - actionClass (Choice)
  - ambiguity (Score)
        |
        v
JudgmentEvidenceEnvelope
        |
        |  evidence only
        v
Authority Runtime / Workbench
  - identity binding
  - authority source
  - scope
  - expiry
  - policy
  - replay / one-shot controls
        |
        v
permit / deny / human review
        |
        v
external action + receipt
```

The adapter cannot cross the eVidence/authority boundary by construction.

## Example questions

```js
const questions = {
  intentMatch: noul(
    "Does the requested operation semantically match the declared intent?"
  ),
  scopeExpansion: noul(
    "Does the requested operation expand beyond the presented scope?"
  ),
  actionClass: choice(
    "Classify the external-effect class of the requested operation.",
    {
      read_only: "No external state mutation.",
      reversible_write: "External mutation with a defined rollback path.",
      irreversible_write: "External mutation without a reliable rollback path.",
      external_effect: "Other externally observable effect."
    }
  ),
  ambiguity: score(
    "How semantically ambiguous is the requested operation?",
    ["Unambiguous", "Minor ambiguity", "Material ambiguity", "Severe ambiguity"]
  )
};
```

Do **not** ask:

```js
noul("Should this action be allowed to execute?")
choice("Authorize the action", { allow: null, deny: null })
```

The validator rejects those shapes.

## Design consequences from current Jev evidence

1. **Store raw distributions, not only `confidence`.** Choice confidence is a derived summary, while the probability distribution is the actual predictive output.
2. **Do not assume deterministic responses.** Repeated calls may differ slightly; capture model/version and request/response digests.
3. **Permutation-test Choice questions.** Independent probing found material order sensitivity in Jev 1.13.0 on some tasks, enough to cross plausible thresholds.
4. **Questions in one request are independent.** If question B genuinely depends on answer A, run a second stage in application code; do not pretend a parallel batch is a reasoning chain.
5. **Calibrate on the deployment distribution.** Vendor or benchmark calibration is not sufficient evidence that a threshold is safe for a specific authority workflow.
6. **Pin or record model identity.** `jev-latest` is useful for experimentation; production evidence should preserve the returned concrete model identifier and preferably qualify allowed versions.
7. **Provider failure never becomes a synthetic safe answer.** The prototype throws; higher layers decide how to fail closed or route to review.

## Included audits

### Choice permutation stability

`auditChoicePermutationStability()` runs the same Choice with different option orders and reports:

- whether the winning label changes;
- probability range per label;
- all observed orders and distributions.

This directly targets the order sensitivity reported in independent Jev probing.

### Calibration helpers

`brierScore()` and `expectedCalibrationError()` provide small, local diagnostics for labeled evaluation sets. They are intentionally not a claim of production calibration.

## Files

```text
src/
  adapter.js                  judgment-evidence envelope
  contracts.js                Choice / Score / Noul + anti-authority guard
  validate-response.js        response/schema validation
  canonical-json.js           deterministic SHA-256 digests
  providers/
    jev-http-provider.js       live TypeSafe endpoint
    fixture-provider.js        offline provider
  audits/
    permutation-audit.js       Choice order-sensitivity audit
    calibration.js             Brier / ECE helpers
    repeat-audit.js             run-to-run variance audit
  qualification/
    live-qualification.js       staged live qualification
    expectations.js             synthetic semantic checks
examples/
  offline-demo.mjs
  live-jev.mjs
  live-qualification.mjs
fixtures/
  authority-boundary.json
  live-qualification.json
test/
docs/
```

## Relationship to TypeSafe's official SDK

TypeSafe also publishes `@typesafe-ai/sdk`. This prototype intentionally uses a tiny direct HTTP provider so the Matawaka boundary remains inspectable and dependency-light. The alpha.3 contract has been checked against the current official SDK types. A future official-SDK-backed provider can still be added behind the same `provider.evaluate()` interface without changing the Matawaka evidence contract. See `docs/OFFICIAL-CONTRACT-SNAPSHOT.md`.

## Sources snapshot

Research/design snapshot updated: 21 September 2026.

- TypeSafe AI ‚Äî *Introducing System One Models & Jev* (15 Sep 2026): https://typesafe.ai/blog/introducing-system-one-models-and-jev
- TypeSafe docs ‚Äî Introduction: https://docs.typesafe.ai/introduction
- TypeSafe official JS SDK: https://github.com/typesafe-ai/typesafe-sdkZú¬ãH\ò⁄\à[YH8†%
íô]â‹»\ò⁄]X›\ôH[õX\⁄ŸY
à
M»Ÿ\åçäNàŒãÀÿ\ò⁄\ö[YKò€€K‹‹›À⁄ô]úÀX\ò⁄]X›\ôK][õX\⁄ŸYœ›èL¬ÇîŸYHÿ‹À–Tê“UP’TëKSì’TÀõYõ‹àH\›[ò›[€àô]ŸY[àXõ\⁄YòX›ÀÿúŸ\ùò][€úÀ[ô\ò⁄]X›\ò[[ôô\ô[òŸKÇ