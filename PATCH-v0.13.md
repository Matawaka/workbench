# Matawaka Workbench v0.13 — bounded source apply + fixed offline build gate

v0.13 closes the next maintenance gap after v0.12 staged planning.

Authority sequence:

`validated package plan`
→ `explicit staging materialization`
→ `read-only staged source-apply plan`
→ `explicit exact source apply + fixed offline build/publish`
→ `separate candidate launch`
→ `Self-test`
→ `separate local checkpoint`

## New capability

After a READY staged apply plan, the user may explicitly authorize **Применить + собрать**.
The service then re-plans the same materialization, rechecks predecessor HEAD/tag and a clean
working tree, verifies every staged/current SHA-256, and may mutate only the plan's exact
`Add`/`Replace` paths.

Replaced files are backed up under ignored `.workbench/update-source-backups`. If source
apply or build/publish fails, the exact accepted predecessor source is restored and the
transition fails closed.

The build surface is fixed:

- only `<workspace>/.dotnet-sdk/dotnet.exe`;
- `dotnet build ... --no-restore`;
- `dotnet publish App ... --no-restore`;
- `dotnet publish SemanticHost ... --no-restore`;
- local NuGet/cache/temp roots under the Matawaka workspace;
- no command or executable path accepted from JSON.

`--no-restore` prevents package restore by this gate, but it is **not OS network isolation**.

## Predecessor identity cleanup

v0.13 materialization receipts carry `PredecessorTag` explicitly. The staged planner no
longer contains a transition-specific hard-coded predecessor bridge.

## Candidate launch remains separate

A successful apply/build receipt does not launch or accept the candidate. **Запустить candidate**
requires a second explicit confirmation and may start only the exact receipt-bound executable
whose SHA-256 still matches. The launched candidate must still pass Self-test and receive a
separate **Принять** checkpoint confirmation.

## Non-effects

Neither apply/build nor launch grants:

- Git add/commit/tag/fetch/push or remote publication;
- Matawaka catalog mutation;
- Agent Execute or ActionPermit;
- checkpoint authority;
- Stable Core promotion;
- OS sandbox or network-isolation claims.

`Proof of staged possibility ≠ source-apply authority ≠ build authority ≠ launch authority ≠ checkpoint authority.`
