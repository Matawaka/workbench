# Start here — Workbench v0.9

1. Enable the agent for read-only Self-test.
2. Click **Self-test**.
3. Require `Passed=true` in Acceptance.
4. Click **Принять** only if you want to create the local accepted Workbench checkpoint.
5. Review the exact changed-file list in the confirmation dialog.
6. Confirm to create a local commit + `workbench-v0.9-accepted` tag.

No remote push/fetch or Matawaka catalog mutation is performed by the checkpoint gate.


## Workbench v0.10

Relevant UU-AAP protocol dependencies are now verified as an exact byte-bound source set rather than requiring repository HEAD equality. A new local Update Plan intake surface validates bounded future update packages but creates no materialization/build/checkpoint authority.
