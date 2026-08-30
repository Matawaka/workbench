# Matawaka Workbench v0.11 — Security boundary

The accepted v0.7 semantic security boundary remains unchanged: fixed verified semantic host, restricted Low-integrity token, Windows Job Object and child runtime attestation before semantic input. Later maintenance surfaces do not widen semantic-provider authority.

## Local checkpoint authority

Checkpoint acceptance remains explicit human maintenance authority for the Workbench repository only. It requires a passing in-process Self-test, byte-bound build-source manifest, exact accepted predecessor tag, exact changed-file preview and a separate **Принять** confirmation. Only fixed local `git add` / fixed commit / fixed annotated tag operations are allowed.

`Self-test PASS != Checkpoint authority`

`Checkpoint authority != Catalog mutation authority`

`Checkpoint authority != Remote publication authority`

`Checkpoint authority != Agent Execute`

## v0.10 source-set and update-intake boundaries

- `Repository HEAD != Relevant Source Set`: unrelated repository movement is observable but does not replace exact bound-file verification.
- Relevant-source verification is fail-closed and performs no fetch or repository mutation.
- `Update Package Valid != Materialization Authority`: intake only reads/validates a local ZIP and writes a plan receipt.
- ZIP traversal, unmanifested payload files, digest mismatch, oversized payloads and packages requesting network/catalog/Execute/arbitrary-process/installer-script authority are rejected.

## v0.11 staging materialization boundary

Materialization is a separate human-confirmed authority gate and consumes only a READY plan from the current Workbench process.

Before any staging write v0.11 requires:

1. exact package SHA-256 still equals the planned package;
2. the bounded intake verifier re-plans the package successfully;
3. target/predecessor/file set still equals the confirmed plan;
4. current HEAD and accepted predecessor tag still match;
5. tracked Workbench working tree is clean;
6. the user explicitly confirms **Материализовать**.

Allowed effect is limited to creating validated payload bytes under `Workbench/.workbench/update-materializations` plus a receipt under `Workbench/artifacts/update-materializations`.

The gate explicitly does **not** authorize:

- overwrite of tracked Workbench source;
- `dotnet restore/build/publish`;
- installer or arbitrary process execution;
- Git add/commit/tag/fetch/push;
- network access;
- Matawaka catalog mutation;
- Agent Execute or ActionPermit;
- later build/apply/checkpoint authority inferred from the materialization receipt.

`Valid Plan != Explicit Materialization Authority`

`Staging Materialization != Source Apply`

`Staging Materialization != Build Authority`

`Staging Materialization != Checkpoint Authority`
