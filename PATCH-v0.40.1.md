# Workbench v0.40.1 — One-Confirmation Activation Probe

Predecessor: `26e12f75abbba99323190f79693d585790e55bc1` / `workbench-v0.40-accepted`.

Target: `0.40.1` / `workbench-v0.40.1-accepted`.

Purpose: provide the first natural successor for the accepted v0.40 reusable transition bootstrap.

Expected real-host path from accepted v0.40:

`Update Workbench -> one confirmation -> exact build -> automatic launch -> exact-image handoff -> v0.40 self-close -> first v0.40.1 boot claims one-shot lease -> automatic Self-test -> only if Passed=true automatic local Accept`.

Publication and lifecycle remain separate explicit actions.

## Deliberately unchanged accepted runtime

The following v0.40 implementation files are not modified by this probe:

- `TransitionBootstrapV040Service.cs`
- `MainWindow.V040.cs`
- `MainWindow.xaml`
- `CandidateLaunchHandoffV039Service.cs`
- `BoundedUpdateApplyBuildService.cs`
- `MaintenanceUpdateOrchestratorService.cs`
- `MainWindow.V033.cs`
- Local Apps services
- non-App Runtime / Protocol / AgentHost / Engine / Catalog / SemanticHost

## Successor-only additions

v0.40.1 adds version-bound acceptance/checkpoint/publication routing. `App` constructs the existing `MainWindow`, calls `ConfigureV0401Routing()` before it is shown, and only then displays it. This detaches the v0.40 `Loaded / Self-test / Accept / Publish` handlers and attaches v0.40.1 equivalents while retaining the accepted v0.40 `Update Workbench` handler and v0.39 manual launch fallback.

This avoids rewriting the bootstrap runtime just to test it.

## Authority boundaries

- `One Update Confirmation != General Future Launch Authority`.
- `First Boot != Reusable Acceptance Authority`.
- `Self-test Passed=true` is required before automatic local Accept.
- A claimed bootstrap lease is one-shot and cannot authorize retry.
- Manual/repeated startup without the exact activated lease does not auto Self-test/Accept.
- Local Accept does not authorize `Publish accepted`.
- Publication does not authorize `Lifecycle receipt`.
- No force push, arbitrary process execution, catalog mutation, Agent Execute or ActionPermit.
