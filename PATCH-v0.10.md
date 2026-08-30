# Matawaka Workbench v0.10

Status: candidate / local Windows build required.

## Additive changes

- Relevant UU-AAP source-set verification by exact Git blob identity; repository HEAD equality is observable but no longer the sole validity condition.
- Workbench acceptance v0.10 requires the relevant source set to match for both semantic providers.
- New GUI `Пакет обновления` surface performs local ZIP intake and emits `matawaka.workbench-update-plan-receipt/v0.10`.
- Update intake is plan-only: no extraction, build, installer execution, Git checkpoint, network, catalog mutation, or Agent Execute.
- Existing v0.7 restricted-token/Low-integrity/Job/runtime-attestation semantic boundary remains unchanged.
- Existing GUI-local accepted checkpoint gate advances to `workbench-v0.10-accepted`.

## Source-set rule

`Repository HEAD != Relevant Source Set`

Unrelated docs/participation commits do not invalidate the adapter when all exact bound source blobs are unchanged. A relevant blob mismatch is fail-closed.

## Update-intake rule

`Valid Package Plan != Materialization Authority != Build Authority != Checkpoint Authority`

The v0.10 package reader validates paths, bounded sizes, exact file list, SHA-256 payload digests and predecessor tag/commit, but does not extract or execute the package.
