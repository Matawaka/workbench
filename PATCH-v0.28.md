# Matawaka Workbench v0.28 - Transport Adversarial Evidence Closure

Status: additive Workbench maintenance/evidence layer. No UU-AAP Stable Core promotion.

## Purpose

v0.26 proved one exact same-machine copied transport can support transport-only replay/materialization without dereferencing the original evidence roots after the copy boundary.

v0.27 proved three negative controls over that same exact transport identity: post-binding byte drift, an extra ZIP entry, and transport-manifest evidence-envelope drift are refused before evidence materialization.

v0.28 closes those two observations into one byte-bound evidence envelope.

The closure binds:

- the exact retained v0.26 transport-independence receipt bytes;
- the exact retained v0.27 adversarial-control matrix bytes;
- the exact common source transport SHA-256;
- the exact v0.26 source transport-manifest SHA-256;
- the exact three v0.27 adversarial candidate SHA-256 values.

## Exact evidence frontier

Positive receipt SHA-256:

`c94bbb3ec3b7ec577f1199bffadde02ac84bac9c52139b74ccb73e064793a543`

Common source transport SHA-256:

`692d0dfb375dd07c482f80accb0bf3250fe6f10332506dcb6fb35fee250ecdf8`

Source transport-manifest SHA-256:

`22aa0903566cab24bc8cfbd08f49df66ff584b7d90328d045b410c6422f46ad4`

Observed adversarial candidate SHA-256 values:

- copy-byte-drift: `60bebb261744358a4e07d7b6672ea705a0328b981d071a354cd6dccead77c53b`
- extra-zip-entry: `6fdba0636740aae212b71be7ba2b91dfe84b3defd39cbb52a8a83eb622ee7177`
- transport-manifest-drift: `9c93f08da1a82add2d632c1c8fc6ed89dfe81de8fd5f89675459c2af9f4bf599`

## New surface

After `workbench-v0.28-accepted`, the GUI exposes **Transport closure**.

The closure action:

1. requires a clean accepted v0.28 Workbench repository;
2. selects exactly one passing retained v0.27 adversarial matrix matching the fixed v0.27 accepted frontier;
3. hashes the exact matrix file bytes;
4. resolves the v0.26 positive receipt only through the path/SHA binding carried by that matrix;
5. hashes the exact v0.26 receipt bytes and requires the fixed positive receipt SHA-256;
6. verifies both receipts bind the same exact transport identity and the fixed transport-manifest digest;
7. verifies all three exact negative scenarios were refused before evidence materialization;
8. verifies the constituent evidence preserves all authority limitations;
9. writes one closure receipt under `artifacts/recovery-transport-adversarial-evidence-closures`.

The closure does not open, copy, inspect, import, materialize, mutate, or execute the transport ZIP.

## Expected receipt

Schema:

`matawaka.workbench-recovery-transport-adversarial-evidence-closure/v0.28`

Passing status:

`CLOSED_BYTE_BOUND_TRANSPORT_ADVERSARIAL_EVIDENCE_ENVELOPE`

Required aggregate:

- `Closed=true`
- `PositiveIndependenceReceiptVerified=true`
- `AdversarialControlMatrixVerified=true`
- `MatrixToPositiveByteBindingVerified=true`
- `CommonSourceTransportBindingVerified=true`
- `AllAdversarialControlsRefusedBeforeEvidenceMaterialization=true`
- `PositiveNegativeEvidencePairClosed=true`
- `AuthorityLimitationsPreserved=true`
- `AuthorityExpansionDetected=false`
- `MainRepositoryUnchanged=true`

## Authority boundary

Allowed effects are limited to reading/hashing the exact two retained receipt files, validating fixed bindings, and writing one closure receipt.

The closure does not authorize:

- input receipt mutation;
- transport inspection/import/materialization;
- recovery execution or rollback;
- Workbench source mutation;
- build/checkpoint actions;
- git fetch/push or remote mutation;
- network access;
- Matawaka catalog mutation;
- Agent Execute or ActionPermit creation;
- producer-authentication claims;
- cross-machine/cross-OS portability claims;
- production-main recovery claims;
- general failure-recovery claims;
- automatic recovery;
- canonical UU-AAP conformance;
- Stable Core/interface-registry promotion.

## Acceptance sequencing

Self-test does not run the closure.

First update from accepted v0.27 to the v0.28 candidate through the ordinary package -> materialize -> apply plan -> apply+build -> candidate launch path. Run a passing v0.28 Self-test and explicitly accept `workbench-v0.28-accepted`.

Only then run **Transport closure** with its separate confirmation dialog.