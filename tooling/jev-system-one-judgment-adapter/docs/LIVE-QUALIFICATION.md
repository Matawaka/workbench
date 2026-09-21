# Live qualification protocol — alpha.3

Account-access state: **TypeSafe access granted**.

The remaining live dependency is deliberately local: a `TYPESAFE_API_KEY` must be present in the process environment. Do not commit the key and do not paste it into receipts.

## Phase 0 — local checks

```bash
npm test
npm run demo
```

Both must be green before any billable/network call.

## Phase 1 — smoke

One synthetic adversarial fixture, two repeat observations, no meaningful permutation expansion:

```bash
npm run qualify:smoke
```

Pass condition for this phase is transport/contract only:

- `GET /v1/models` succeeds;
- `POST /v1/systemone` succeeds;
- response validates against the current primitive schema;
- request IDs and observed model IDs are captured;
- no authority-shaped field is emitted by the adapter.

Semantic accuracy is **not** established by the smoke phase.

## Phase 2 — standard live qualification

```bash
npm run qualify:live
```

Current standard profile:

- all synthetic adversarial fixtures;
- 3 repeated identical calls per fixture;
- up to 6 Choice permutations per Choice question;
- semantic expectation checks recorded as observations, not as authority decisions.

## Phase 3 — deep qualification

```bash
npm run qualify:deep
```

Current deep profile:

- 5 repeated identical calls;
- up to 24 permutations per Choice question.

Only run this after smoke/standard results are understood and account usage limits are acceptable.

## Artifacts

Reports are written under `artifacts/` and ignored by Git. They contain synthetic fixture outputs, model inventory, TypeSafe request IDs, request/response digests, repeat variability, and Choice permutation observations. They do **not** contain the API key.

## Admission rule

A live result does not enter the production authority path. The next admissible state after successful alpha.3 is **shadow mode**: Workbench computes its existing authority result exactly as before, while Jev evidence is captured and compared out-of-band.
