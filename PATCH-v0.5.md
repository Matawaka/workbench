# Workbench v0.5 — Windows bounded semantic child process

## Goal

Strengthen the accepted v0.4 separate-process semantic boundary without misrepresenting Job Object containment as a full Windows sandbox.

## Changes

- verify fixed `SemanticHost.exe` SHA-256 against a build-generated manifest before semantic input;
- assign the fixed host to a fresh Windows Job Object before stdin semantic data;
- job limits: active process `1`, process committed memory `256 MiB`, kill-on-close, no breakaway flags;
- retain environment allowlist, temp working directory, timeout/cancellation, IPC limits and parent digest verification;
- add explicit receipt facts for restricted-token/network-sandbox absence;
- add repository `.gitattributes` line-ending policy;
- preserve provider input/output digests and read-only authority semantics.

## Non-effects

- no repository mutation authority;
- no network provider call;
- no arbitrary process authority;
- no restricted token or AppContainer claim;
- no materialization authority;
- no ActionPermit;
- no Stable Core/interface-registry promotion.
