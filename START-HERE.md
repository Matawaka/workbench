# Start here — Workbench v0.33 candidate

Accepted/public predecessor: `workbench-v0.32-accepted` at `24c98787817b3b37f1a7197ecb5627be130f2581`.

## Normal local analysis

- Paste JSON or load it from a file.
- **Проверить** validates the command envelope.
- **Запустить** follows the bounded Runtime / AgentHost / SemanticHost path.
- **Агент включен** is explicit and required where the bounded agent/Self-test path needs it.
- **Разрешить git fetch** remains a separate catalog-observation permission and does not authorize accepted-source publication.

## Build the v0.33 candidate

From accepted v0.32:

1. Click **Update candidate** and choose the v0.33 source-only ZIP.
2. Review package SHA-256, exact predecessor `workbench-v0.32-accepted`, target `workbench-v0.33-accepted`, and payload size/file count.
3. Explicitly confirm one maintenance session.
4. Workbench then sequences existing typed gates:
   - fresh package plan;
   - staging-only materialization;
   - fresh staged `Add/Replace/NoOp` plan;
   - exact source apply + fixed local `dotnet --no-restore` build/publish.
5. Require the aggregate result `CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED`.
6. Click **Запустить candidate** separately and confirm the exact built executable SHA-256.

The Update candidate confirmation does not authorize launch, Self-test, checkpoint or publication.

## Accept v0.33

In the launched candidate:

1. enable **Агент включен**;
2. click **Self-test**;
3. require `Passed=true`;
4. inspect the v0.33 acceptance artifact;
5. click **Принять** and inspect the exact changed-file set;
6. explicitly create local `workbench-v0.33-accepted`.

Self-test is read-only. Its new v0.33 checks are offline contract checks for the orchestrator and fixed publisher successor; no update/build/launch/publication effect is performed.

## Publish accepted v0.33

Only after the local accepted tag exists at HEAD:

1. click **Publish accepted**;
2. verify fixed remote `github-workbench` / `https://github.com/Matawaka/workbench.git`;
3. verify local accepted HEAD, exact parent and `workbench-v0.33-accepted`;
4. explicitly confirm the separate network effect;
5. require remote `main` and accepted tag both read back as the exact local HEAD;
6. require local HEAD and working tree unchanged.

The publisher refuses conflicting main/tag state, force-push, arbitrary remote/URL, catalog mutation, Agent Execute, ActionPermit and general Workbench network authority.

## Recovery and historical evidence

The active surface still keeps **Recovery check / Recovery plan / Recovery execute**. Historical proof handlers/services remain source/audit evidence behind collapsed compatibility bindings.

`UI simplification != Evidence erasure`
