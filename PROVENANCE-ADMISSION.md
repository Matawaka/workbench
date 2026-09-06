# Workbench Provenance Observation Admission v0.56

Status: bounded candidate implementation for `Matawaka/workbench#84`.

## Purpose

Workbench can now admit one exact, already-verified C2PA provenance evidence frontier as an **observation input** without converting that evidence into authority.

```text
accepted external provenance qualification bytes
        ↓
closed Workbench admission profile
        ↓
exact SHA-256 / Git blob / size / schema / verdict checks
        ↓
semantic non-escalation checks
        ↓
PROVENANCE_OBSERVED_NO_AUTHORITY
```

The first closed profile is bound to the accepted `Matawaka/uu-aap#955` qualification for the public Workbench v0.55.2 release observation.

## What this proves

On a successful admission Workbench may record that the supplied evidence bytes exactly match the accepted profile and that the evidence itself reports:

- standard `c2pa.external-reference` binding established;
- external-reference hash match established;
- immutable external resolution exact-byte match established;
- live C2PA validation accepted;
- exact Workbench v0.55.2 release commit observed;
- annotated Git tag signature remains unverified/unsigned.

## What this never proves

```text
C2PA validation != Workbench authority
provenance observation != publication authority
provenance observation != truth
provenance observation != runtime execution authority
provenance observation != model request authority
provenance observation != response/display authority
provenance observation != action/successor permit
input evidence != permission to fetch or refresh evidence
```

The service has no network/process/Git/file-mutation path. Retrieval and cryptographic verification of new evidence require separate future capabilities and separate authority.

## Current boundary

This candidate does not change the accepted `workbench-v0.55.2-accepted` tag and does not route admitted provenance into runtime/model/action decisions. It creates only a reusable typed observation boundary for later composition.
