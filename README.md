# Matawaka Workbench v0.1.3

Independent Windows workbench and reusable engine shell for the Matawaka ecosystem.

## Purpose

This repository is intentionally **not** a fork of KONTUR, FREESHIELD, or uu-aap. It consumes their ideas and reusable contracts through explicit adapters and evidence snapshots.

Core separation:

`JSON ingress -> CommandRouter -> Engine / Catalog / AgentHost -> progress + typed authority + result/evidence receipts -> WPF UI`

The first UI is WPF on .NET 10. The engine and JSON contracts target plain `net10.0`, so additional HTTP, named-pipe, web, mobile, or service interfaces can reuse the same command router later.

## v0.1.3 changes

- Fixes the first live evidence-selection bias found in v0.1.2: a global `maxEvidenceItems` limit can no longer be exhausted by the first repository before later focus repositories are inspected.
- Each focus repository is scanned independently for deterministic evidence candidates; the final frontier is selected by deterministic round-robin across repositories. Focus is bounded to 32 repositories and 64 terms per checkpoint.
- Receipt now includes `Coverage` with strategy, total budget, selected count, repositories represented, and candidate/selected counts per repository.
- Protocol now contains typed `CapabilityRequest` and `CapabilityDecision` records.
- AgentHost emits `authority.requested` and `authority.decided` events before invoking the provider.
- The Workbench-local `freeshield-read-only-bridge/v0.1.3` policy can grant only read-only `observe`/`propose`.
- `execute`, mutation budget, network access, and arbitrary process execution remain deny-by-default.
- A denied request returns a typed decision receipt; the repository provider is not invoked.
- Successful receipts still require `mutations = []`.

## Proven predecessor behavior retained

- Persistent workspace/catalog settings.
- Separate `Events`, `Result`, `Evidence`, and `Agent` tabs.
- Catalog snapshots include repository name, local root, branch, and exact `HEAD` commit SHA.
- Real deterministic read-only repository observation.
- Evidence anchors contain repository, relative file, line, matched terms, and snippet.
- No semantic/network LLM provider yet.

## Important limitations

The v0.1.3 provider is deterministic file/keyword analysis. It is real repository observation, but it is **not semantic LLM reasoning**.

The `freeshield-read-only-bridge/v0.1.3` identifier describes a Workbench-local adapter inspired by the authority boundaries observed in FREESHIELD. It is not a claim that this local policy is a canonical FREESHIELD protocol revision.

## Projects

- `Matawaka.Workbench.Protocol` — JSON command/progress plus typed capability request/decision contracts.
- `Matawaka.Workbench.Engine` — `IAnalyticFutureAdapter`, scoring profile, command-independent analytic engine.
- `Matawaka.Workbench.Catalog` — local catalog discovery, branch+HEAD snapshots, and capability-gated git fetch.
- `Matawaka.Workbench.AgentHost` — balanced deterministic read-only Observe/Propose provider, typed authority gate, and development receipt.
- `Matawaka.Workbench.Runtime` — UI-neutral `ICommandRunner`/`CommandRouter`.
- `Matawaka.Workbench.App` — Windows WPF shell with persistent workspace and split outputs.

## Supported JSON kinds

- `analysis.run`
- `catalog.inspect`
- `catalog.fetch`
- `agent.run`

See `samples/`.
