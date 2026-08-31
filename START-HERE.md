# Start here — Workbench v0.34 candidate

Accepted/public predecessor: `workbench-v0.33-accepted` at `df211d1f4d80d0b1f238f1166460758e73ce18d2`.

## Install/build the v0.34 candidate

From accepted v0.33:

1. Click **Update candidate** and choose the v0.34 source-only ZIP.
2. Review package SHA-256, exact predecessor `workbench-v0.33-accepted`, target `workbench-v0.34-accepted`, payload file count and bytes.
3. Explicitly confirm the maintenance session.
4. Workbench reuses its typed fresh plan → staging materialization → staged apply plan → exact apply/build gates.
5. Require `CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED`.
6. Click **Запустить candidate** separately and confirm the exact built executable SHA-256.

`Update candidate != Launch authority`.

## Accept v0.34

In the launched candidate:

1. enable **Агент включен**;
2. run **Self-test** and require `Passed=true`;
3. Self-test must remain read-only; its lifecycle checks are offline contract/hostile checks only;
4. click **Принять** and inspect the exact changed-file set;
5. explicitly create local `workbench-v0.34-accepted`.

`Self-test PASS != Checkpoint authority`.

## Publish v0.34

After local acceptance:

1. click **Publish accepted**;
2. verify fixed `github-workbench` / `https://github.com/Matawaka/workbench.git`;
3. verify exact accepted HEAD, parent `df211d1f...` and tag `workbench-v0.34-accepted`;
4. explicitly confirm publication;
5. require remote `main` and accepted tag to read back as exact local accepted HEAD;
6. require local HEAD/working tree unchanged.

`Accepted checkpoint != Publish authority`.

## Create Maintenance Lifecycle Receipt

Only after publication has independently completed:

1. click **Lifecycle receipt**;
2. Workbench performs a fail-closed read-only assessment of existing local artifacts;
3. require one exact relation binding:
   - v0.33 orchestrator receipt targeting v0.34;
   - its candidate executable SHA-256;
   - checkpoint-bound passing v0.34 Self-test artifact and exact digest;
   - v0.34 checkpoint at current HEAD;
   - v0.34 publication receipt with exact local/remote HEAD/tag;
4. inspect SHA-256 bindings for all four artifacts and `Complete=true`;
5. explicitly confirm writing the local lifecycle receipt.

The lifecycle action writes evidence only. It does not call update, build, launch, Self-test, checkpoint or publisher services and does not authorize retry/rollback.

```text
Summary != Authority
Observed Sequence != Authorized Sequence
Missing/Ambiguous Evidence != Inferred Success
Lifecycle Receipt != ActionPermit
```

## Normal analysis and recovery

Normal **Проверить / Запустить**, **Recovery check / plan / execute**, catalog scan/fetch and evidence tabs remain independent of maintenance lifecycle authority.
