# Start here — Matawaka Workbench

This page describes the stable operator path. Exact release-specific predecessor/target identities are shown by the update package preview, accepted tag at HEAD, and `PATCH-v*.md` history rather than hard-coded here.

## Normal analysis

- Paste JSON or load it from a file.
- **Проверить** validates the command envelope.
- **Запустить** follows the bounded Runtime / AgentHost / SemanticHost path.
- **Агент включен** is explicit and required where the bounded agent/Self-test path needs it.
- **Разрешить git fetch** remains a separate catalog-observation permission and does not authorize accepted-source publication.

## Install/build a bounded successor

1. Click **Update candidate** and choose the source-only ZIP.
2. Review package SHA-256, exact predecessor commit/tag, target version/tag, payload count and bytes.
3. Explicitly confirm the maintenance session.
4. Workbench sequences the existing typed fresh plan → staging materialization → staged apply plan → exact apply/build gates.
5. Require `CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED`.
6. Click **Запустить candidate** separately and confirm the exact built executable SHA-256.

`Update candidate != Launch authority`.

## Accept the launched candidate

1. In the launched candidate enable **Агент включен**.
2. Run **Self-test** and require `Passed=true`.
3. Self-test is read-only with respect to update/checkpoint/publication/lifecycle effects.
4. Click **Принять** and inspect the exact changed-file set and build-source manifest binding.
5. Explicitly create the local accepted checkpoint/tag.

`Self-test PASS != Checkpoint authority`.

## Publish accepted source

Only after local acceptance:

1. click **Publish accepted**;
2. verify fixed `github-workbench` / `https://github.com/Matawaka/workbench.git`;
3. verify exact accepted HEAD, parent and accepted tag in the preview;
4. explicitly confirm publication;
5. require remote `main` and accepted tag to read back as exact local HEAD;
6. require local HEAD and working tree unchanged.

`Accepted checkpoint != Publish authority`.

## Create a Maintenance Lifecycle Receipt

Only after publication has independently completed:

1. click **Lifecycle receipt**;
2. Workbench derives the current accepted version from the unique `workbench-v<version>-accepted` tag at HEAD;
3. it requires the exact current checkpoint and exact accepted predecessor relation;
4. it binds the checkpoint-bound Self-test artifact + SHA-256;
5. it requires one matching orchestrator receipt with the same target/predecessor and candidate executable digest;
6. it requires one matching publication receipt with exact local/remote main/tag;
7. inspect every artifact SHA-256 and require `Complete=true`;
8. explicitly confirm writing the local lifecycle evidence receipt.

Missing or ambiguous accepted tags/artifacts fail closed. The service does not choose the newest file and does not infer trust or authority from Git tag discovery.

```text
Summary != Authority
Observed Sequence != Authorized Sequence
Accepted Tag Discovery != Trust Discovery
Missing/Ambiguous Evidence != Inferred Success
Lifecycle Receipt != ActionPermit
```

## Qualification/stabilization use

When testing whether lifecycle composition is reusable, perform a real bounded successor transition and classify the outcome only after the new successor is independently accepted/published and its generic Lifecycle receipt is evaluated:

- `LIFECYCLE_REUSABLE`;
- `LIFECYCLE_NEEDS_ADAPTER`;
- `LIFECYCLE_AMBIGUOUS`;
- `LIFECYCLE_NOT_REQUIRED`.

Do not create a new feature layer merely to obtain a positive result.

## Recovery and historical evidence

**Recovery check / Recovery plan / Recovery execute**, catalog controls and historical source/receipt evidence remain separate from maintenance lifecycle authority.

`UI simplification != Evidence erasure`.
