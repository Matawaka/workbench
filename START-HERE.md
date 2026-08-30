# Start here — Workbench v0.11

## Acceptance

1. Enable the agent for the read-only Self-test.
2. Click **Self-test** and require `Passed=true`.
3. Click **Принять** only when you intend to create the local accepted Workbench checkpoint.
4. Review the exact changed-file list and confirm.

The checkpoint performs no remote push/fetch or Matawaka catalog mutation.

## Local update package

1. Click **Пакет обновления** and select a manifest-based Workbench update ZIP.
2. Require `READY_FOR_SEPARATE_MATERIALIZATION_AUTHORITY` in **Update Plan**.
3. A READY plan alone changes no update payload bytes.
4. Click **Материализовать** only if you want to create a local staging copy.
5. Review package SHA-256, predecessor, target, file count and byte count, then confirm.
6. Require `MATERIALIZED_STAGING_ONLY` in the resulting receipt.

The v0.11 materialization gate writes only to ignored Workbench-local staging and artifacts. It does not apply source changes, build, execute installers, commit/tag, access the network, mutate catalog repositories, or grant Agent Execute.


## v0.12 staged source-apply plan

After an update package has been explicitly materialized to `.workbench`, use **План применения** to calculate the exact bounded source delta. A READY apply plan is still non-authorizing: no tracked source is changed until a later, separate source-apply gate exists.
