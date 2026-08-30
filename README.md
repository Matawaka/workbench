# Matawaka Workbench

Independent Windows workbench for bounded analysis and development flows over the local Matawaka catalog.

## v0.2 checkpoint

The current v0.2 path is deliberately non-actuating:

```text
JSON command
  -> terminal-safe router
  -> catalog snapshot (repo + branch + HEAD)
  -> typed read-only capability decision
  -> balanced evidence frontier
  -> evidence-only semantic provider boundary
  -> proposal/checkpoint
  -> STOP before mutation
```

The Windows UI exposes separate tabs for Events, Result, Evidence Receipt, Authority Receipt, Liveness and Agent.

### PCL-compatible liveness

Workbench v0.2 projects visible progress using the field semantics of UU-AAP Perceived Causal Liveness: phase, progress kind, waiting category, next observable event and checkpoint reference. Meaningful progress changes only when one of those fields changes. Every local progress receipt keeps `hidden reasoning disclosed = false` and `external effect authority created = false`.

This is an exact-source-bound compatibility adapter. It does not execute the canonical UU-AAP JavaScript implementation and does not claim canonical conformance.

### Semantic provider boundary

`ISemanticProvider` is the reusable provider seam. The built-in `DeterministicSemanticProvider` receives only:

- repository name + branch + HEAD;
- balanced evidence anchors/snippets;
- coverage;
- typed capability receipt.

It does not receive repository roots, file handles, arbitrary process execution, network access or mutation authority. A future model provider must preserve the same boundary and request any additional capability separately.

### Authority

The Workbench-local `freeshield-read-only-bridge/v0.2` is not represented as canonical FREESHIELD policy. It grants only read-only Observe/Propose when explicitly enabled and when mutation/network/process requests remain zero/false. Execute is denied.

The UI also carries exact-source references to UU-AAP Scoped Authority Evidence and Materialization Authority so later stages can remain distinct:

`Authority Evidence != Materialization Authority != Execution Authority`.

No materialization evaluator or ActionPermit path is activated in v0.2.

## Local layout

Default workspace:

`K:\Matawaka`

Default catalog:

`K:\Matawaka\Catalog`

The Workbench itself should be tracked as its own local Git history. Catalog repositories are evidence sources and are not modified by the v0.2 patch.
