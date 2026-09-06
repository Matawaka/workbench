# Workbench × KONTUR integration backlog

Status: **planning evidence plus generic substrate tracking / no KONTUR runtime authority**.

This file records reusable integration opportunities discovered from the current KONTUR main frontier while Workbench develops the local read/MCP/runtime transaction substrate. It is intentionally additive and does not change v0.51.12 listener-readiness semantics.

## Reuse candidates

1. **KONTUR one-shot request envelope → Workbench capability lease**
   - KONTUR owns semantic/current human request intent.
   - Workbench owns technical runtime/read authority.
   - Preserve `Player Request != Runtime Authority`.
   - v0.53 now supplies the provider-neutral one-shot runtime execution lease.
   - v0.55 candidate adds exact source-frontier, source-artifact and request-envelope provenance binding without exposing the inner bearer.
   - Still open: KONTUR handoff translation, cross-process runtime ownership proof and a separately authorized one-shot model request/output transaction.

2. **KONTUR LM1 selected artifact → bounded Workbench download/hash-verification lease**
   - Exact artifact currently selected in KONTUR but not downloaded/locally verified.
   - Candidate Workbench corridor: exact repository/revision/file, expected size/SHA-256, fixed destination, bounded bytes, one download, no execution.
   - Preserve `Artifact Selected != Downloaded != Verified != Runtime Activated`.

3. **KONTUR llama.cpp local provider → generic Workbench local runtime lifecycle**
   - Reuse app-scoped runtime ownership, prepared/committed state, exact endpoint readiness and stop receipts.
   - Preserve `Runtime Started != Provider Ready != Benchmark Authority != Request Authority`.

4. **KONTUR V2 Component Manifest / Affected Conformance → Workbench qualification planner**
   - Map changed paths to explicit owning components and reverse dependents.
   - Use component-owned validator metadata without silently executing it.
   - Preserve `Affected != Incompatible` and `Execution Plan != Authority`.

5. **KONTUR honest liveness → Workbench UI projection**
   - Waiting/spinner/process-alive must never be presented as proof of transport/model progress.
   - Candidate vocabulary: INITIALIZING / WAITING_FOR_AUTHORITY / WAITING_FOR_RUNTIME / READY / TERMINAL.

6. **KONTUR sanitized error classification → Workbench external MCP/runtime errors**
   - Return fixed categories to external clients while keeping raw local exception evidence private.
   - Do not disclose bearer/hash/endpoint secrets, response bodies, headers, identifiers or local paths.

7. **Event-Responsive Dormancy handshake**
   - KONTUR explicit foreground cue may wake reevaluation only.
   - Workbench may create a short-lived bounded runtime/read lease only after fresh current authority.
   - `Trigger != Authorization`; `Wake != Resume of Old Authority`.

8. **KONTUR exact Display Permit → first future Workbench Action-Lease pilot**
   - Prefer a one-use display effect before file-write/process/game-control action permits.
   - Candidate chain: candidate effect -> review -> exact permit -> one bounded effect -> receipt.

## Priority

Near-term preferred sequence:

1. bounded model artifact download + local hash verification;
2. one-shot request envelope + cross-process Workbench capability lease;
3. component manifest / affected-conformance bridge.

## Prepared intake scaffold

Workbench now has a durable inert landing zone under `integrations/kontur/`:

- `integrations/kontur/README.md` — ownership split, intended first integration and non-effects;
- `integrations/kontur/KONTUR_ANCHOR.json` — exact observed KONTUR/Workbench frontier reference;
- `integrations/kontur/CAPABILITY_HANDOFF.schema.json` — mirrored machine-readable KONTUR → Workbench handoff schema; validation is not authority;
- `integrations/kontur/LM1_ARTIFACT_INTAKE.template.json` — exact LM1 model-artifact intake template for a future bounded transfer/hash-verification review.

This scaffold remains non-executable. Generic implementations live outside it:
v0.52 artifact acquisition, v0.53 runtime execution, v0.54 runtime-tree
materialization and the v0.55 provenance-bound outer-lease candidate. None of those
primitives derives KONTUR authority from this backlog or scaffold.

## Ownership boundary

- Workbench remains generic local capability/runtime infrastructure.
- KONTUR remains companion policy, player-cue semantics, response review and game-specific behavior.
- UU-AAP remains the shared semantic/invariant/provenance layer.
- Cross-project reuse must remain additive and must never silently widen existing authority.
