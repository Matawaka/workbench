# Threat model — System One judgment evidence

## Protected boundary

The protected invariant is:

> A model-produced judgment cannot itself create, expand, delegate, renew, or exercise authority.

## Threats and mitigations

| Threat | Example | Prototype mitigation |
|---|---|---|
| Semantic error with valid schema | Jev returns a valid but wrong `reversible_write` label | Typed output is treated as evidence only; raw probabilities retained |
| Calibration drift | 0.9 no longer means roughly 90% in production traffic | In-domain calibration audits; no global magic threshold |
| Choice order sensitivity | Same options, reordered, cross a policy threshold | Permutation audit included |
| Provider/model alias drift | `jev-latest` changes model behavior | Record requested and observed model; production should allowlist |
| Non-deterministic numerical variation | repeated calls differ slightly | Hash each response; do not require bitwise determinism |
| Question coupling assumed by caller | caller thinks Q2 consumed Q1 | Contract states questions are independent; dependent flow requires another stage |
| Prompt injection in state | untrusted text asks model to ignore criteria | Narrow atomic questions; adversarial fixtures; no model authority |
| Normative output smuggling | choice keys are `allow`/`deny` | validator rejects normative question IDs/options/instructions |
| Provider outage/error | no model evidence available | throw / return no evidence; never synthesize favorable judgment |
| Replay/misattribution | old judgment reused for a new request | request/response digests + correlation ID; authority layer must bind evidence to current request |
| Digest ambiguity | same logical object serialized differently | canonicalized JSON before SHA-256 |
| Excessive trust in `confidence` | caller interprets it as probability of correctness | preserve distribution; docs distinguish confidence from calibration |

## Explicit non-goals

This adapter does not:

- authenticate the human or agent;
- establish delegation;
- validate cryptographic authority;
- issue a display permit or action permit;
- invoke tools;
- mutate repositories, files, accounts, or external systems;
- decide liability;
- prove that Jev's probability is calibrated for a given deployment.

Those remain responsibilities of other Matawaka layers.
