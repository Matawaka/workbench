# Workbench v0.6 — Restricted Windows semantic security context

## Goal

Reduce the semantic child process security context while preserving the accepted v0.5 fixed-binary, separate-process, bounded-IPC and Job Object boundary.

## Changes

- create a restricted primary token from the Workbench process token with `CreateRestrictedToken(DISABLE_MAX_PRIVILEGE)`;
- lower the child token mandatory integrity level to Low (`S-1-16-4096`) before process creation;
- create the fixed SemanticHost with `CreateProcessAsUser` in `CREATE_SUSPENDED` state;
- assign the existing Job Object before the primary thread is resumed;
- send semantic stdin only after restricted-token creation, low-integrity application, Job assignment and resume;
- keep host SHA-256 verification, fixed executable path, allowlisted environment, timeout/cancellation and IPC limits;
- preserve semantic provider digests and read-only authority semantics;
- expose restricted-token facts in process/provider receipts.

## Explicit limits

v0.6 is still not a general hostile-code sandbox. It does not claim:

- network isolation;
- AppContainer;
- filesystem ACL virtualization/deny-by-default;
- desktop/window-station isolation;
- VM/container isolation;
- impossibility of low-integrity reads of same-user data;
- execution authority or ActionPermit.

`Restricted Token != OS Sandbox`

`Low Integrity != Network Isolation`

`Same User Identity != Same Security Context`
