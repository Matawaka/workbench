# Matawaka Workbench v0.11 — Explicit Staging Materialization Gate

## Purpose

Convert the v0.10 plan-only update intake into the next separately-authorized maintenance layer without collapsing plan validity into source mutation or build authority.

## New flow

`Local update ZIP -> bounded v0.10 intake plan -> READY plan -> explicit human Materialize confirmation -> package/predecessor re-verification -> staging-only payload materialization receipt`

## Authority separation

- plan receipt remains non-authorizing;
- materialization authority is created only by the explicit UI button + confirmation;
- materialization writes only under `Workbench/.workbench/update-materializations`;
- tracked source apply, build, checkpoint and publication remain unauthorized successor operations.

## Revalidation before write

The package SHA-256, manifest payload set, per-file SHA-256 values, exact predecessor commit/tag and clean tracked Workbench working tree are re-verified after confirmation and before payload bytes are created.

## Non-effects

No tracked Workbench source overwrite, installer execution, arbitrary process execution, `dotnet` build, Git write, fetch/push, network access, catalog mutation, Agent Execute, ActionPermit, Stable Core promotion or canonical UU-AAP conformance claim.

The accepted v0.7 semantic security boundary and v0.10 relevant-source-set binding remain unchanged.
