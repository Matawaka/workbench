# v0.1 patch checkpoint

Predecessor: user-verified Matawaka Workbench v0 Windows launch.

This patch changes only source/docs/samples. It does not modify local Matawaka catalog repositories.

Acceptance gates after applying:

1. `dotnet publish` succeeds on Windows with local .NET 10 SDK.
2. App launches as `Matawaka Workbench v0.1`.
3. Workspace defaults to or persists `K:\Matawaka` in the user's current setup.
4. `catalog.inspect` shows exact branch and HEAD SHA in Result.
5. `agent.run` while disabled is denied.
6. `agent.run` with mode `propose` and Agent enabled produces Evidence and Agent receipt.
7. Receipt contains `authorityUsed: read-only` and empty `mutations`.
8. `payload.mode = execute` is denied.
9. `git fetch` remains independently default-deny.
