# Matawaka Workbench v0.27 — transport-capsule adversarial independence controls

Status: additive Workbench maintenance/evidence layer. No UU-AAP Stable Core promotion.

## Purpose

v0.26 demonstrated one same-machine transport-copy independence shape using an exact v0.25 self-contained recovery-evidence transport ZIP. v0.27 adds post-acceptance adversarial negative controls around that transport boundary.

The goal is to prove that a transport copy is not accepted merely because it came from a previously valid source path. Exact bytes and exact ZIP structure remain live verification conditions at the point of evidence inspection.

## New surface

After `workbench-v0.27-accepted`, the GUI exposes **Transport negatives**.

The matrix runs three isolated controls under `.workbench/recovery-transport-adversarial-controls/...`:

1. **copy-byte-drift-after-binding-refused**
   - create an exact copy of the retained v0.26-bound transport;
   - bind its SHA-256;
   - mutate one expected payload entry after binding;
   - refuse before evidence inspection/materialization because the copy no longer matches the bound transport digest.

2. **extra-zip-entry-refused**
   - create an exact copy;
   - add one unexpected ZIP entry;
   - invoke the existing v0.25 verify-only transport inspection;
   - require rejection before any evidence materialization.

3. **transport-manifest-drift-refused**
   - create an exact copy;
   - replace only `transport-manifest.json` with a structurally valid manifest whose evidence-envelope digest is changed;
   - invoke the existing v0.25 verify-only transport inspection;
   - require `Verified=false` / refusal before evidence materialization.

The source transport ZIP and the main Workbench repository remain unchanged.

## Authority boundary

The matrix may only:

- create and retain three adversarial transport copies under one `.workbench` control root;
- mutate those copies inside the control root;
- run verify-only transport inspection against the mutated copies;
- write one matrix receipt under `artifacts/recovery-transport-adversarial-controls`.

It does **not** authorize:

- mutation or deletion of the source v0.25 transport ZIP;
- Workbench source mutation;
- recovery execution, rollback, or deletion;
- evidence import/materialization outside the isolated control roots;
- `dotnet` restore/build/test/publish;
- Git add/commit/tag outside the later ordinary checkpoint gate;
- Git fetch/push or remote mutation;
- network access;
- Matawaka catalog mutation;
- Agent Execute or ActionPermit creation;
- producer-authentication or cross-machine/cross-OS portability claims;
- Stable Core/interface-registry promotion.

## Expected post-acceptance receipt

A passing matrix uses:

- schema `matawaka.workbench-recovery-transport-adversarial-control-matrix/v0.27`;
- status `TRANSPORT_ADVERSARIAL_CONTROLS_PASSED`;
- `CopyByteDriftAfterBindingRefused=true`;
- `ExtraZipEntryRefused=true`;
- `TransportManifestDriftRefused=true`;
- `AllControlsRefusedBeforeEvidenceMaterialization=true`;
- `SourceTransportUnchanged=true`;
- `MainRepositoryUnchanged=true`.

Each scenario must record:

- `Rejected=true`;
- no evidence materialization attempt/root;
- the adversarial copy is preserved after refusal;
- the source transport remains unchanged.

## Acceptance sequencing

The v0.27 Self-test does not run the adversarial matrix. First accept the v0.27 candidate through the existing explicit GUI checkpoint gate. Only then run **Transport negatives** with a separate explicit confirmation.
