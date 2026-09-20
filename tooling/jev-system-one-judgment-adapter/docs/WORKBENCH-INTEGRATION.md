# Workbench integration sketch

The existing Workbench `WeightedAnalyticFutureAdapter` already states that its ranking is non-normative and that availability/intent/authority remain separate gates. The System One adapter should preserve that same architectural direction.

## Proposed integration surface

Do not replace the existing authority/runtime path. Add a sibling semantic evidence provider:

```text
Matawaka.Workbench.Engine
  IJudgmentEvidenceProvider
      EvaluateAsync(JudgmentRequest) -> JudgmentEvidenceEnvelope

Providers
  FixtureSystemOneProvider
  JevSystemOneProvider
```

The first integration target should be *observation only*:

1. Workbench builds a bounded semantic `state` from an already-known request.
2. Jev produces atomic judgments.
3. Workbench records the evidence envelope and hashes.
4. Existing deterministic authority result is calculated exactly as before.
5. A qualification runner compares whether Jev evidence would have changed routing thresholds, but it may not change production authority.

This creates an A/B observation phase before any policy consumption.

## Suggested first four judgments

- `intentMatch` — Noul
- `scopeExpansion` — Noul
- `actionClass` — Choice: read-only / reversible-write / irreversible-write / other external effect
- `ambiguity` — Score: four ordered criteria labels

These deliberately avoid asking whether the action is authorized.

## Admission sequence

```text
alpha.1 offline fixture provider
alpha.2 HTTP contract tests with recorded/sanitized fixture
alpha.3 live Jev observation-only probe
alpha.4 permutation + repeatability + calibration qualification
alpha.5 shadow mode beside existing gate
alpha.6 policy-consumption experiment (still no authority issuance)
```

A separate design/review would be required before any Jev signal is allowed to alter a production authority decision.
