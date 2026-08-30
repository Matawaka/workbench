# Matawaka Workbench v0.11

Windows/.NET 10 Workbench for bounded local Matawaka analysis and explicitly separated maintenance gates.

Current surfaces:

- persistent `K:\Matawaka` workspace;
- local Matawaka catalog observation;
- typed read-only agent authority;
- balanced evidence frontier;
- interchangeable offline semantic providers;
- verified fixed `SemanticHost.exe`;
- restricted Low-integrity Windows token + Job Object;
- child runtime security attestation before semantic input;
- PCL-compatible visible liveness;
- automated two-provider + denied-Execute Self-test;
- explicit GUI-local accepted checkpoint gate;
- relevant UU-AAP source-set binding independent of unrelated repository HEAD drift;
- bounded local update-package intake plan;
- explicit GUI staging-only materialization gate.

## v0.9 checkpoint gate

A passing Self-test enables **Принять**. The user sees the exact local Workbench files that will enter the checkpoint and must confirm again. Only then may Workbench execute a fixed local Git sequence for its own repository.

The checkpoint gate never performs `git fetch`, `git push`, remote mutation, catalog mutation, agent Execute, network model calls, arbitrary command execution, ActionPermit creation or catalog materialization authority creation.

## v0.10 relevant source set + update plan

Relevant UU-AAP protocol dependencies are verified as an exact byte-bound source set rather than requiring repository HEAD equality. The local **Пакет обновления** surface validates a bounded manifest ZIP and exact predecessor relationship but creates no materialization/build/checkpoint authority.

## v0.11 explicit staging materialization

A READY update plan may enable **Материализовать**. The user sees package SHA-256, predecessor, target and bounded payload size and must explicitly confirm.

After confirmation Workbench re-verifies the same package, predecessor, clean working tree and every payload digest, then copies only the validated payload bytes into an ignored local staging area under `Workbench/.workbench/update-materializations`.

This is deliberately not an update apply/build gate:

`Valid Plan != Materialization Authority != Build Authority != Source Apply Authority != Checkpoint Authority`

v0.11 does not overwrite tracked Workbench source, run an installer, run `dotnet`, commit/tag, fetch/push, use the network, mutate Matawaka catalog repositories, or grant Agent Execute.

Receipts remain Workbench-local evidence. They are not canonical UU-AAP conformance and do not make the Workbench repository authoritative over other Matawaka repositories.


## v0.12 staged source-apply plan

v0.12 adds a read-only plan over already materialized update bytes. It verifies the staging receipt, predecessor, clean working tree, exact staged file set and SHA-256s, then reports Add/Replace/NoOp effects without modifying Workbench source. Source apply, build and checkpoint remain separate future authority gates.
