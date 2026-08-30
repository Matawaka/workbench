# Matawaka Workbench v0.30 - Key Rotation Continuity Boundary

Status: additive Workbench maintenance/evidence layer. No UU-AAP Stable Core promotion.

## Purpose

v0.29 demonstrated one exact detached-signature fixture under one exact public key, while explicitly preserving:

- key possession != real-world producer identity;
- public-key fingerprint != identity;
- trust anchor != authority;
- cryptographic provenance != action permission.

v0.30 adds one narrowly bounded successor-key continuity fixture.

The predecessor fixture key signs one exact claim that names one exact successor public-key fingerprint and binds the exact accepted v0.29 provenance receipt. The successor fixture key independently signs one exact possession claim that binds the exact rotation-claim SHA-256.

This demonstrates only a cryptographic fixture relationship between two keys.

It does not prove that the same real-world person or organization controls both keys, that the predecessor was revoked, that time ordering is trusted, that the successor is trusted, or that any authority was delegated.

## Exact predecessor frontier

- accepted commit: `c45581a0f93be150cd7c1ac88d0d5296fbcc03bf`
- accepted tag: `workbench-v0.29-accepted`
- exact v0.29 provenance receipt SHA-256:
  `4a17aebda73c8d24907597449ba95712bd4622228254a040fb89d6f67f06af56`
- predecessor public-key fingerprint:
  `1048a67242e8d24db9fb900ae1d54275710831623b0ad30c811030a2bb86c734`

## Fixture cryptographic bindings

- successor public-key fingerprint:
  `ccce3e9dc674eac4633d348f1c19c307b1b55730974875c9e733e24f1a4e53ea`
- rotation-claim SHA-256:
  `38fbca126115d9af594e088d9cce626315c8c8dfda679396bb65325d27bfe9c7`
- predecessor rotation-signature SHA-256:
  `e052acb3ccc6a320d7341c16f7cf8066981527e7d4657519b4317806b550397b`
- successor possession-claim SHA-256:
  `de4aa4a3ffb8eb7da7c12db0a0caebab0e777769a84616b43dd3388449d521ba`
- successor possession-signature SHA-256:
  `fe2c2eab3313528f320827dcf732c79f588df92bfe526ac15f5423810632b3d3`

Algorithm for both detached signatures:

`RSA-2048 / PKCS#1 v1.5 / SHA-256`

No private key is present in Workbench or required at verification time.

## New surface

After `workbench-v0.30-accepted`, the GUI exposes **Key continuity**.

The action:

1. requires clean accepted v0.30 at HEAD;
2. locates the exact retained v0.29 provenance receipt by fixed SHA-256;
3. revalidates the v0.29 bounded key-possession contract;
4. verifies the predecessor public-key fingerprint;
5. verifies the predecessor detached signature over the exact successor-binding claim;
6. verifies the successor public-key fingerprint;
7. verifies the successor detached signature over the exact successor-possession claim;
8. runs in-memory negative controls:
   - rotation claim drift is refused;
   - predecessor signature drift is refused;
   - successor possession claim drift is refused;
   - successor signature drift is refused;
   - successor public-key substitution is refused;
9. verifies main Workbench Git state is unchanged;
10. writes one bounded continuity receipt.

No signing operation occurs.

## Expected receipt

Schema:

`matawaka.workbench-producer-key-rotation-continuity-boundary/v0.30`

Passing status:

`VERIFIED_FIXTURE_KEY_ROTATION_CONTINUITY_IDENTITY_TRUST_AUTHORITY_UNPROVEN`

Required aggregate:

- `Passed=true`
- `SourceProvenanceVerified=true`
- `PredecessorRotationSignatureVerified=true`
- `SuccessorPossessionSignatureVerified=true`
- `PredecessorSourceBindingVerified=true`
- `PredecessorToSuccessorBindingVerified=true`
- `SuccessorPossessionBindingVerified=true`
- `RotationClaimByteDriftRefused=true`
- `PredecessorSignatureByteDriftRefused=true`
- `SuccessorPossessionClaimByteDriftRefused=true`
- `SuccessorSignatureByteDriftRefused=true`
- `SuccessorPublicKeySubstitutionRefused=true`
- `KeyRotationContinuityFixtureDemonstrated=true`
- `PrivateKeyMaterialLoadedByBoundary=false`
- `SigningOperationAttempted=false`
- `ProducerIdentityProven=false`
- `ProducerAuthenticationProven=false`
- `CommonControllerProven=false`
- `TrustAnchorEstablished=false`
- `CertificateChainValidated=false`
- `TrustedTimestampValidated=false`
- `TrustedTemporalOrderingProven=false`
- `PredecessorRevocationProven=false`
- `DelegationAuthorityGranted=false`
- `SuccessorOperationalAuthorityGranted=false`
- `AuthorityExpansionDetected=false`
- `MainRepositoryUnchanged=true`

## Strengthened invariants

```text
Predecessor Signature != Identity Continuity.
Successor Possession != Common Controller.
Key Rotation Claim != Trusted Time Ordering.
Successor Binding != Predecessor Revocation.
Cryptographic Successor Relation != Delegation Authority.
Continuity Evidence != Operational Key Activation.
```

## Authority boundary

Allowed effects are limited to:

- reading/hashing the exact retained v0.29 provenance receipt;
- public-key verification of two fixed detached signatures;
- in-memory negative verification attempts;
- writing one bounded receipt.

The action does not authorize or perform:

- private-key access, generation, import, persistence, or signing;
- key revocation or key activation;
- key registry mutation;
- producer identity/common-controller claims;
- producer authentication;
- certificate-chain validation;
- trust-anchor establishment;
- trusted timestamp or trusted temporal ordering;
- delegation/action authority;
- source mutation/build/checkpoint;
- git fetch/push or remote mutation;
- network/catalog mutation;
- Agent Execute or ActionPermit creation;
- portability claims;
- canonical UU-AAP conformance;
- Stable Core/interface-registry promotion.

## Acceptance sequencing

Self-test does not run Key continuity.

First update from accepted v0.29 to the v0.30 candidate through the ordinary package -> materialize -> apply plan -> apply+build -> candidate launch path. Run a passing v0.30 Self-test and explicitly accept `workbench-v0.30-accepted`.

Only then run **Key continuity** with its separate confirmation dialog.
