# Prototype status v0.1

Date: 2026-09-20

## Implemented

- Non-normative evidence envelope.
- Choice / Score / Noul constructors.
- Anti-authority validation guard.
- Direct TypeSafe Jev HTTP provider.
- Offline fixture provider.
- Provider response validation.
- Canonical request/response SHA-256 digests.
- Choice permutation audit.
- Brier score and ECE helpers.
- Offline demo.
- Live example awaiting TypeSafe invite/API key.
- 8 automated tests passing.

## Not yet qualified

- Live TypeSafe request: blocked by invite-only access/API key.
- Exact compatibility against future Jev API versions.
- Production calibration on Matawaka fixtures.
- Workbench C# bridge.
- Receipt signing / incorporation into existing Workbench receipt structures.
- Model-version admission policy.

## Next trigger

When TypeSafe grants access:

1. Run `npm run live` with a synthetic state.
2. Save sanitized response fixtures with concrete model ID.
3. Run 24-option permutation audit on representative Choice tasks.
4. Repeat each fixture enough times to quantify run-to-run variance.
5. Compare Jev to the same question contract using TypeSafe's official System One LLM adapter or a controlled LLM baseline.
6. Only then design a Workbench shadow-mode bridge.
