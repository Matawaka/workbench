# Jev Shadow Real-Case Lab v0.1

Purpose: produce the first real Workbench-derived shadow source without wiring Jev into production UI or authority code.

## Safe capture

v0.1 permits only `catalog.inspect` through the real `CommandRouter` with:

- `AgentEnabled=false`
- `AllowGitFetch=false`
- no network send
- no mutation authority
- no TypeSafe invocation

The resulting v0.1 shadow envelope is written only outside the public repository.

## First real case flow

1. Run one real `catalog.inspect` against the local catalog.
2. Create a disclosure template from the captured shadow envelope.
3. Replace disclosure placeholders with sanitized descriptions and explicitly attest sanitization.
4. Build a v0.2 externalization candidate.
5. Use the already-qualified v0.3 one-shot send lease for one live Jev observation.
6. Feed candidate + receipt into corpus private intake.

This yields a true Workbench-derived `SANITIZED_REAL_SHADOW` case while keeping production `MainWindow`, `CommandRouter`, provider authority, and permit logic unchanged.

## Important

The private disclosure/candidate may still contain sensitive information. They must remain outside the public repository until separately reviewed for publication.


## Blind human label before Jev

After building the sanitized candidate and **before** any TypeSafe send, create the blinded human-label packet:

`npm run label:init -- <candidate.json> <private-case-dir> <case-id>`

This writes only:
- `review-packet.json`
- `label.primary.json`

The reviewer should complete `label.primary.json` before seeing the Jev receipt or probabilities.
