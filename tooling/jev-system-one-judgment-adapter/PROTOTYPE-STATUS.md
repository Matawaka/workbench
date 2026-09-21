# Prototype status — alpha.3

Date: 2026-09-21

## Access state

- TypeSafe account / early-access invite: **GRANTED**.
- API key: supplied only through local environment variable `TYPESAFE_API_KEY`; not stored in repository or chat artifacts.
- Live smoke execution: **PASS** on 2026-09-21 from the operator workstation.

## Live smoke evidence

Operator-reported receipt:

- schema: `matawaka.jev-live-qualification/v0.1`
- requested model: `jev-latest`
- observed concrete model: `jev-1.13.0`
- account-visible models: `jev-latest`, `jev-preview`
- cases: 1
- expectation checks: 6/6 PASS
- repeat unstable choices: 0
- permutation unstable choices: 0
- receipt path: `artifacts/jev-smoke-qualification-2026-09-21T04-19-36-901Z.json`

Qualification meaning: **transport / API contract / first synthetic semantic probe PASS**. This is not yet a model qualification, calibration claim, or authority-path admission.

The full smoke receipt has not yet been independently reviewed in this repository/chat context; the values above are recorded from the operator console output.

## Implemented

- Non-normative `JudgmentEvidence` envelope.
- Choice / Score / Noul constructors.
- JSON-structured `state`, `instructions`, and criteria support aligned with the current official SDK contract.
- Anti-authority validation guard across textual leaves of structured questions.
- Direct TypeSafe Jev HTTP provider.
- `GET /v1/models` inventory capture.
- TypeSafe `x-typesafe-request-id` capture.
- Strict provider response validation, including primitive `type`, Score `legend` and probabilities, exact outcome keys, model and usage metadata.
- Canonical request/response SHA-256 digests.
- Choice permutation audit.
- Repeatability / run-to-run variance audit.
- Brier score and ECE helpers.
- Five synthetic adversarial live fixtures.
- Smoke / standard / deep live qualification runner.
- Offline demo.
- 13 automated tests passing on Node 22.
- First live smoke PASS against `jev-1.13.0`.

## Still not qualified

- Full alpha.3 adversarial fixture set against live Jev.
- Repeat/permutation behavior across the full fixture set.
- Deployment calibration on Matawaka-labeled data.
- Workbench C# shadow-mode bridge.
- Receipt signing / incorporation into existing Workbench receipt structures.
- Model-version admission policy.
- Any authority-path consumption.

## Next evidence trigger

Run:

1. `npm.cmd run qualify:live`
2. inspect the generated standard qualification receipt;
3. compare observed model ID, expectation checks, repeat spread, and Choice order sensitivity across all fixtures;
4. only if those results are acceptable, design Workbench **shadow mode**.

Do **not** run `qualify:deep` until the standard receipt has been reviewed.

No Jev signal is permitted to alter production authority before a separate shadow-mode review and admission decision.
