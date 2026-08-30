# Security boundary — Workbench v0.4

## Allowed

- local JSON validation;
- local catalog inspection;
- explicit fixed git fetch through the catalog gate;
- read-only evidence collection;
- deterministic balanced evidence selection;
- offline selection from a closed provider-id registry;
- one fixed semantic child-process launch after read-only authority ALLOW;
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

## Process boundary

Semantic provider algorithms run in `Matawaka.Workbench.SemanticHost.exe`, not in the WPF/Runtime process. The executable path is fixed by Workbench and cannot be supplied by the command JSON.

Child environment keys are limited to:

- `SystemRoot`;
- `WINDIR`;
- `DOTNET_ROOT`;
- `DOTNET_MULTILEVEL_LOOKUP`;
- `TEMP`;
- `TMP`.

The child receives no repository root in the semantic packet and starts in an isolated temp directory. Output is accepted only after schema/provider/input/output-digest verification by the parent.

This is **not** hostile-code containment. The child runs with the same Windows user token and can in principle use OS facilities available to that account if future provider code were malicious. v0.4 does not claim AppContainer, restricted token, filesystem ACL isolation or network sandboxing.

`Interface Non-Transfer != OS Denial`

`Process Isolation != OS Sandbox`

## Deny semantics

`DENIED` remains a normal terminal policy outcome. Execute is denied before evidence collection and before semantic-host process invocation.
