# Workbench v0.59 — Authority / Evidence Human Review

Status: **candidate; human UI review required before merge**.

## Purpose

v0.58 attached an audit-only provenance/evidence receipt to the real `agent.run` result path **after** the existing `DevelopmentAgentHost` had already evaluated authority and returned. v0.59 does not change that path. It adds a human presentation candidate so an operator can distinguish:

1. **what policy actually allowed or denied**, from
2. **what origin/evidence audit observed without granting authority**.

The key invariant is:

`Evidence displayed != Authority granted`

## Review-only launch

```text
Matawaka.Workbench.App.exe --capability-evidence-review-only
```

This mode is intentionally detached from normal boot/acceptance/publication and does not run `DevelopmentAgentHost`, provider execution, SemanticHost, network access, process execution, Git operations, or file mutation.

It constructs only:

```text
fixed read-only CapabilityRequest
        ↓
existing FreeShieldReadOnlyCapabilityPolicy
        ↓
base CapabilityDecision
        ↓
existing v0.58 embedded audit observation
        ↓
shared v0.59 presentation service
        ↓
human review UI
```

The review fixture is not itself an authorization ceremony and creates no `ActionPermit`, `SuccessorPermit`, `ResponseAuthority`, `DisplayPermit`, model authority, or runtime authority.

## Human semantics

Russian headline for the positive read-only fixture:

```text
ПОЛИТИКА РАЗРЕШИЛА ТОЛЬКО ЧТЕНИЕ — ПРОВЕРКА СВЕДЕНИЙ ПОЛНОМОЧИЙ НЕ ДОБАВИЛА
```

Russian human-facing text deliberately uses plain wording such as **сведения о происхождении / проверка сведений** instead of unexplained `провенанс` jargon.

The UI separates four sections:

- what policy allowed;
- what evidence review showed;
- what the review does **not** authorize;
- exact machine values.

Exact IDs, repository/frontier/path values and SHA-256 are not truncated and are rendered in selectable read-only text fields so they can be copied when needed.

Russian and English tabs use the same underlying authority/audit receipts and the same presentation service.

## Activation boundary

v0.59 deliberately does **not** modify the normal `RenderResult` path yet. This prevents an unreviewed visual interpretation from becoming part of ordinary live operation.

After independent Windows qualification, the only permitted status is:

`READY_FOR_HUMAN_UI_REVIEW`

Only after a human confirms that the distinction is visually clear and non-misleading may a separate successor attach this already-reviewed presentation service to ordinary live `CommandResult.CapabilityEvidence` rendering.

## Non-effects

- UI clarity is not authorization.
- Evidence review is not permission.
- Origin/provenance evidence is not truth.
- A policy deny cannot be overridden by evidence.
- The accepted/public Workbench release remains `workbench-v0.56.2-accepted` unless a separate future acceptance/publication procedure proves and publishes a successor identity.
