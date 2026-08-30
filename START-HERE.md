# Start here — Workbench v0.14

## Normal acceptance

1. Enable **Агент включен** for the read-only acceptance matrix.
2. Click **Self-test** and require `Passed=true`.
3. Click **Принять**, review the exact Workbench changed-file list, and explicitly confirm.

## Local GUI update cycle

1. **Пакет обновления** → require `READY_FOR_SEPARATE_MATERIALIZATION_AUTHORITY`.
2. **Материализовать** → explicitly confirm → require `MATERIALIZED_STAGING_ONLY`.
3. **План применения** → require `READY_FOR_SEPARATE_SOURCE_APPLY_AUTHORITY` and inspect `Add/Replace/NoOp`.
4. **Применить + собрать** → explicitly confirm exact source delta and fixed local `--no-restore` build/publish.
5. Require `CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED`.
6. **Запустить candidate** → explicitly confirm exact executable SHA-256.
7. In the launched candidate run **Self-test**; only after PASS use **Принять**.

No external PowerShell/source bootstrap is part of this normal update cycle. A package still arrives as a local file, but no earlier receipt automatically authorizes a later effect. Git remote publication, Matawaka catalog mutation and Agent Execute remain outside the update chain.
