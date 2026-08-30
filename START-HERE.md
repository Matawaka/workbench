# Start here — Workbench v0.13

## Normal acceptance

1. Enable **Агент включен** for the read-only acceptance matrix.
2. Click **Self-test** and require `Passed=true`.
3. Click **Принять**, review the exact Workbench changed-file list, and explicitly confirm.

## Local update cycle

1. **Пакет обновления** → require `READY_FOR_SEPARATE_MATERIALIZATION_AUTHORITY`.
2. **Материализовать** → explicitly confirm → require `MATERIALIZED_STAGING_ONLY`.
3. **План применения** → require `READY_FOR_SEPARATE_SOURCE_APPLY_AUTHORITY` and inspect `Add/Replace/NoOp`.
4. **Применить + собрать** → explicitly confirm the exact source delta and fixed local offline build/publish.
5. Require `CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED`.
6. **Запустить candidate** → explicitly confirm the exact executable SHA-256.
7. In the launched candidate run **Self-test**; only after PASS use **Принять**.

No earlier receipt automatically authorizes a later step. Git remote publication, Matawaka catalog mutation and Agent Execute remain outside this update chain.
