# Security boundary — Workbench v0.5

## Allowed

- local JSON validation;
- local catalog inspection;
- explicit fixed git fetch through the catalog gate;
- read-only evidence collection;
- deterministic balanced evidence selection;
- offline selection from a closed provider-id registry;
- one fixed semantic child-process launch after read-only authority ALLOW;
- semantic-host SHA-256 verification against a build-generated integrity manifest;
- Windows Job Object containment before semantic stdin is written;
- active-process limit `1`;
- per-process committed-memory limit `256 MiB`;
- kill-on-job-close cleanup;
- stdin JSON semantic packet / stdout JSON receipt IPC;
- reduced child environment allowlist;
- isolated temporary child working directory;
- timeout/cancellation and bounded IPC sizes;
- parent-side semantic digest verification;
- typed capability/provider/process/semantic receipts;
- PCL-compatible visible progress.

## Not authorized through the command/provider interface

- repository mutation by `agent.run`;
- executable/DLL/script/path selection from JSON;
- arbitrary shell/process execution from JSON;
- network model/provider calls;
- repository root or file-handle transfer to semantic IPC;
- inherited credentials/secrets outside the explicit child environment allowlist;
- provider self-registration after authority decision;
- materialization authority creation;
- execution authority creation;
- ActionPermit creation;
- Stable Core/interface-registry promotion;
- game control;
- self-expansion of authority.

## Process and resource boundary

Semantic provider algorithms run in `Matawaka.Workbench.SemanticHost.exe`, not in the WPF/Runtime process. The executable path is fixed by Workbench and cannot be supplied by command JSON.

Before semantic input is sent, the parent:

1. verifies the host SHA-256 against `semantic-host.integrity.json`;
2. starts the fixed host;
3. assigns it to a fresh Windows Job Object;
4. requires successful Job Object assignment;
5. only then writes the semantic evidence packet to stdin.

The job sets kill-on-close, active-process limit `1` and process-memory limit `256 MiB`, and does not set breakaway flags. Windows documents that child processes are associated with the job by default when breakaway is not permitted; the active-process limit therefore bounds the job to one active process under the normal CreateProcess job inheritance path.

Child environment keys are limited to:

- `SystemRoot`;
- `WINDIR`;
- `DOTNET_ROOT`;
- `DOTNET_MULTILEVEL_LOOKUP`;
- `TEMP`;
- `TMP`.

The child receives no repository root in the semantic packet and starts in an isolated temp directory. Output is accepted only after schema/provider/input/output-digest verification by the parent.

## What v0.5 still does not claim

This is **not** hostile-code containment. The child runs with the same Windows user token. v0.5 does not claim:

- restricted token;
- AppContainer;
- filesystem ACL isolation;
- network isolation/firewall sandbox;
- VM/container isolation;
- impossibility of same-user filesystem/network access by arbitrary hostile code.

`Interface Non-Transfer != OS Denial`

`Process Isolation != OS Sandbox`

`Job Object Containment != Restricted Token`

`Offline Provider Implementation != Network Sandbox`

## Deny semantics

`DENIED` remains a normal terminal policy outcome. Execute is denied before evidence collection and before semantic-host process invocation.
