# Matawaka Workbench v0.29 - Producer-Key Provenance Boundary

Status: additive Workbench maintenance/evidence layer. No UU-AAP Stable Core promotion.

## Purpose

v0.28 closed one exact positive/negative transport evidence pair into a byte-bound evidence envelope.

v0.29 adds a deliberately narrower cryptographic provenance boundary:

**valid detached signature under one public key != real-world producer identity != trust anchor != authority grant**

The layer demonstrates only that one exact detached RSA-SHA256 signature verifies for one exact canonical fixture claim under one exact public fixture key, and that three in-memory negative controls fail closed.

The private signing key is not present in Workbench, is not requested by Workbench, and is not needed to run the boundary.

## Exact predecessor evidence

Accepted predecessor:

- commit: `c60ce4280f8c9d0bdad773bb581c22ba244cf08d`
- tag: `workbench-v0.28-accepted`
- exact observed v0.28 closure receipt SHA-256:
  `ddc96a76bee5b6615d101b3f7e8b45847e1f0f5f9eb796730498f982cfe9aa3a`
- exact v0.28 closure evidence-envelope digest:
  `f96045702c4fc9ae369a4b92ed4a312563be4f8f6210fcf7934a50fd9c2702c4`

The v0.29 canonical fixture claim binds those exact values.

## Fixture cryptographic bindings

Canonical claim SHA-256:

`94ddcb67ee4e3ac3cfd3fa5cc2e0af24ca46975b3f50516de66889d60282eaba`

Public-key SubjectPublicKeyInfo SHA-256 fingerprint:

`1048a67242e8d24db9fb900ae1d54275710831623b0ad30c811030a2bb86c734`

Detached signature SHA-256:

`0123a4f6ed55a8ce9b67d55d736359661204b3d5218f1330ea375009b3a631a0`

Algorithm:

`RSA-2048 / PKCS#1 v1.5 / SHA-256`

These are fixture bindings only. The public key is not certified as belonging to Matawaka, any person, company, device, or service.

## New surface

After `workbench-v0.29-accepted`, the GUI exposes **Key provenance**.

The action:

1. requires a clean accepted v0.29 Workbench repository;
2. finds the exact retained v0.28 closure receipt by its fixed SHA-256;
3. revalidates the exact v0.28 closure contract and envelope digest;
4. hashes the fixed canonical v0.29 fixture claim;
5. imports only the fixed public key;
6. verifies the fixed detached signature over the exact claim bytes;
7. runs three in-memory negative controls:
   - claim-byte drift must fail verification;
   - signature-byte drift must fail verification;
   - public-key substitution must fail verification;
8. verifies the main Workbench Git state is unchanged;
9. writes one provenance-boundary receipt under `artifacts/producer-key-provenance-boundaries`.

No signing operation occurs.

## Expected receipt

Schema:

`matawaka.workbench-producer-key-provenance-boundary/v0.29`

Passing status:

`VERIFIED_DETACHED_KEY_POSSESSION_FIXTURE_IDENTITY_UNPROVEN`

Required aggregate:

- `Passed=true`
- `SourceClosureVerified=true`
- `DetachedSignatureVerified=true`
- `ClaimByteDriftRefused=true`
- `SignatureByteDriftRefused=true`
- `PublicKeySubstitutionRefused=true`
- `ExactClaimToClosureBindingVerified=true`
- `KeyPossessionFixtureDemonstrated=true`
- `PrivateKeyMaterialLoadedByBoundary=false`
- `SigningOperationAttempted=false`
- `ProducerIdentityProven=false`
- `ProducerAuthenticationProven=false`
- `TrustAnchorEstablished=false`
- `CertificateChainValidated=false`
- `TrustedTimestampValidated=false`
- `AuthorityExpansionDetected=false`
- `MainRepositoryUnchanged=true`

## Authority boundary

Allowed effects are limited to:

- reading/hashing the exact retained v0.28 closure receipt;
- public-key signature verification;
- three in-memory negative verification attempts;
- writing one bounded receipt.

The action does not authorize or perform:

- private-key access, generation, import, persistence, or signing;
- producer identity resolution;
- producer authentication claims;
- certificate-store or certificate-chain validation;
- trust-anchor establishment;
- trusted timestamp validation;
- transport inspection/import/materialization;
- recovery execution or rollback;
- source mutation;
- build/checkpoint actions;
- git fetch/push or remote mutation;
- network access;
- Matawaka catalog mutation;
- Agent Execute or ActionPermit creation;
- cross-machine/cross-OS portability claims;
- canonical UU-AAP conformance;
- Stable Core/interface-registry promotion.

## Strengthened invariants

```text
Valid Signature != Trusted Signer.
Key Possession != Real-World Identity.
Public Key Fingerprint != Identity.
Trust Anchor != Authority Grant.
Cryptographic Provenance != Action Permission.
Fixture Verification != Production Authentication.
```

## Acceptance sequencing

Self-test does not run the provenance boundary.

First update from accepted v0.28 to the v0.29 candidate through the ordinary package -> materialize -> apply plan -> apply+build -> candidate launch path. Run a passing v0.29 Self-test and explicitly accept `workbench-v0.29-accepted`.

Only then run **Key provenance** with its separate confirmation dialog.
