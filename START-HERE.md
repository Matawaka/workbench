# Start here — Workbench v0.32 candidate

The accepted/public predecessor is still `workbench-v0.31-accepted` until the complete v0.32 local acceptance and publication sequence finishes.

## Normal local analysis

- Paste JSON or load it from a file.
- **Проверить** validates the command envelope.
- **Запустить** follows the current bounded Runtime / AgentHost / SemanticHost authority path.
- **Агент включен** is required only where the chosen action explicitly requires the bounded agent path.
- **Разрешить git fetch** is a separate catalog-observation permission. It does not authorize source publication.

## Build and accept the v0.32 candidate

From accepted v0.31 use the existing self-hosted GUI update chain:

1. **Пакет обновления** → require `READY_FOR_SEPARATE_MATERIALIZATION_AUTHORITY`.
2. **Материализовать** → explicitly confirm → require staging-only materialization.
3. **План применения** → inspect exact `Add/Replace/NoOp` and require a READY separate source-apply decision.
4. **Применить + собрать** → explicitly confirm the exact source delta and fixed local `dotnet --no-restore` build/publish.
5. Require a byte-bound candidate build receipt.
6. **Запустить candidate** → explicitly confirm the exact candidate executable SHA-256.
7. In the launched v0.32 candidate enable **Агент включен** and run **Self-test**.
8. Require `Passed=true`. Self-test is read-only and does **not** call GitHub publication.
9. Click **Принять**, inspect the exact source set, and explicitly create the local `workbench-v0.32-accepted` commit/tag.

At this point v0.32 is locally accepted but still not remotely published.

## Publish the accepted checkpoint

Only after local v0.32 acceptance:

1. Click **Publish accepted**.
2. Review the preview. It must show:
   - `github-workbench`;
   - `https://github.com/Matawaka/workbench.git`;
   - current accepted HEAD;
   - exact local parent;
   - `workbench-v0.32-accepted`.
3. Explicitly confirm the network effect.
4. Require the publication receipt to show remote `main` and the accepted tag both equal the exact local accepted HEAD.
5. Require local HEAD and working tree unchanged.

The publisher refuses:

- any different remote name/URL;
- remote `main` that is neither exact parent nor exact local HEAD;
- a conflicting accepted tag;
- force-push or tag movement;
- catalog mutation;
- Agent Execute / ActionPermit;
- general Workbench network authority.

A retry is safe when remote `main` already equals accepted HEAD but the tag is still absent.

## Recovery

The active maintenance surface keeps:

- **Recovery check**;
- **Recovery plan**;
- **Recovery execute**.

Older recovery/transport/key proof buttons were removed from the active toolbar in v0.32, but their source, receipts, patch notes and Git history remain evidence.

`Historical evidence UI removal != Historical evidence erasure`
