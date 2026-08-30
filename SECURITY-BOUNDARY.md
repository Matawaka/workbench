# Security boundary — Workbench v0.6

## Allowed

- local JSON validation;
- local catalog inspection;
- explicit fixed git fetch through the catalog gate;
- read-only evidence collection;
- deterministic balanced evidence selection;
- offline selection from a closed provider-id registry;
- one fixed semantic child-process launch after read-only authority ALLOW;
- semantic-host SHA-256 verification against a build-generated integrity manifest;
- restricted primary token derived from the current Workbench token;
- `DISABLE_MAX_PRIVILEGE` for the semantic child token;
- Low mandatory integrity level (`S-1-16-4096`) before child creation;
- suspended child creation followed by Job Object assignment before resume;
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

## Restricted-token launch sequence

Before semantic input is sent, the parent:

1. verifies the fixed `SemanticHost.exe` SHA-256 against `semantic-host.integrity.json`;
2. opens its own primary token for duplication/query/primary assignment;
3. derives a restricted token with `CreateRestrictedToken(DISABLE_MAX_PRIVILEGE)`;
4. lowers that token to Low integrity (`S-1-16-4096`);
5. creates `SemanticHost.exe` suspended with `CreateProcessAsUser` and the allowlisted environment;
6. assigns the suspended process to a fresh Windows Job Object;
7. requires successful Job assignment;
8. resumes the primary thread;
9. only then sends the semantic evidence packet to stdin.

The Job Object retains kill-on-close, active-process limit `1`, process-memory limit `256 MiB`, and no breakaway flags.

This sequence reduces privileges and write-up capability before provider code begins. Microsoft documents restricted tokens as reduced versions of access tokens and supports using a restricted version of the caller's primary token with `CreateProcessAsUser`; v0.6 uses that Windows mechanism without treating it as a complete sandbox.

## What v0.6 still does not claim

The child retains the same Windows **user identity**, but its token is restricted and Low-integrity, so it is not the same unrestricted security context as Workbench. v0.6 still does not provide or claim:

- AppContainer;
- firewall/WFP network isolation;
- deny-by-default filesystem ACL namespace;
- alternate desktop/window-station isolation;
- VM/container boundary;
- kernel mediation of all same-user reads;
- hostile-code containment proof.

A Low-integrity process may still be able to read objects whose integrity/DACL policy permits reads, and network access is not blocked by this token boundary alone.

`Interface Non-Transfer != OS Denial`

`Process Isolation != OS Sandbox`

`Restricted Token != Network Sandbox`

`Low Integrity != Filesystem Namespace Isolation`

`Same User Identity != Same Security Context`

## Deny semantics

`DENIED` remains a normal terminal policy outcome. Execute is denied before evidence collection and before restricted semantic-host process creation.
