# Workbench v0.54.1 — canonical acquisition execution-receipt status binding

## Trigger
The first real-host v0.54 smoke acquisition produced the exact canonical v0.52 execution receipt with:

- `Schema = matawaka.artifact-acquisition-execution-receipt/v0.52`
- `State = ACQUISITION_VERIFIED`
- `Status = ACQUISITION_VERIFIED`
- `AllArtifactsSha256Verified = true`

The initial v0.54 materialization consumer incorrectly compared the canonical receipt `Status` to the operator/UI wrapper label `ARTIFACT_ACQUISITION_VERIFIED`.

## Correction
`BoundedRuntimeTreeMaterializationV054Service.Preview` now requires the actual canonical producer contract:

`State = ACQUISITION_VERIFIED` and `Status = ACQUISITION_VERIFIED`.

The UI-only wrapper label is not accepted as canonical receipt provenance.

## Versioning
- exact local predecessor: `b501ba5820d0ae03265723b8d8cd413ba4818984 / workbench-v0.54-accepted`
- target: `0.54.1 / workbench-v0.54.1-accepted`
- build-source schema family remains `matawaka.workbench-build-source-manifest/v0.54` because the accepted predecessor writer uses major.minor schema identity.
- materialization primitive request/state/grant/receipt schemas remain v0.54.

## Non-effects
No new acquisition, materialization, execution, publication, model, benchmark, game, KONTUR, catalog, Agent Execute or general authority is introduced by this correction.
