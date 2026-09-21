# Prototype status — alpha.3

Date: 2026-09-21

## Access state

- TypeSafe account / early-access invite: **GRANTED**.
- API key: expected to be supplied only through local environment variable `TYPESAFE_API_KEY`; not stored in repository or chat artifacts.
- Live calls from this development environment: **not executed**, because the account credential is intentionally not available here.

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

## Still not qualified

- Actual Jev live behavior on the alpha.3 adversarial fixture set.
- Concrete account-visible model inventory.
- Repeat/permutation behavior of the currently served Jev model.
- Deployment calibration on Matawaka-labeled data.
- Workbench C# shadow-mode bridge.
- Receipt signing / incorporation into existing Workbench receipt structures.
- Model-version admission policy.

## Next evidence trigger

Run, in order:

1. `npm run qualify:smoke`
2. inspect the generated receipt;
3. `npm run qualify:live`
4. compare observed model ID, repeat spread, Choice order sensitivity, and expectation checks;
5. only if those results are acceptable, implement Workbench shadow mode.

No Jev signal is permitted to alter production authority before a separate shadow-mode review.
