# Source-bound one-shot model request/output adapter v0.1

Status: **candidate library / fixture-qualified / not a live KONTUR model path**.

Base: Workbench main `6541dc32182c970c8e1a6ade426a6cee7086511b`.
Related KONTUR base: `d155ba0a05325ba0fb57b7095ad91a67f4aaf456`
and translator candidate branch `codex/kontur-workbench-handoff-translator`.

Main already contains both the provenance-bound **process-only** v0.55 service
(merged #82) and `BoundedLocalModelInvocationV055Service` (converged in v0.55.2).
This adapter adds source/request/output binding to the latter; it does not create
a duplicate runtime, change process-only authority or promote the v0.60
provenance/evidence surface into a grant.

## Separate stages and API

1. The domain caller validates its handoff and current intent, then creates a
   `ModelInvocationSourceBinding` plus exact `LocalModelInvocationRequestV055`.
   JSON entry points must use `ParseSource`: missing, duplicate and extra keys
   are refused. Source provenance is an opaque assertion, not verified repository
   ownership, a signature or authority.
2. `Preview(workspace, source, request, token)` validates exact profile/artifact/
   bound correspondence and reuses the existing read-only artifact preview.
   It returns a hash of **all** source/request fields, not just the prompt.
3. `GrantAsync(..., reviewedDigest, explicitModelConfirmation, token)` requires
   a separate model-specific confirmation from a trusted local caller. It
   rechecks the digest and artifacts, then delegates to the existing model grant.
   A process lease, handoff receipt or evidence admission is not an accepted
   substitute. The boolean is a trusted API input, not proof of authenticated
   human confirmation. No public UI/IPC endpoint is registered by this change.
4. `InvokeAsync(opaquePermit, token)` accepts only an object owned by that
   service instance. It removes the prepared entry before state/expiry/artifact
   checks and execution. It verifies the exact inner state hash, delegates to
   the existing one-shot service and binds the resulting output.
5. Returned text is `UNTRUSTED_OUTPUT_PENDING_CALLER_REVIEW`, with
   `SourceClass=FIXTURE_OUTPUT_NOT_LIVE_MODEL`. No response/display/game/action/
   successor permission is created. KONTUR still owns content review, spoiler
   policy and a separate exact display permit.

Cancellation before semaphore admission does not consume the token; after its
entry is removed, failures do not restore it. An expired unused entry cannot
invoke, but this library has no background cleanup; the owning bounded caller
must release the service after its session to release in-memory request data.

## Scope and replay guarantees

The opaque outer token exposes only review/lease digests, not the inner bearer
or prompt. The inner grant/request stay in memory; persisted v0.55 receipts
contain prompt hashes/size rather than prompt text. Grant refusal does not start
a process. A successful grant may create local authority/state receipts.

Outer token replay and repeated BindingId issuance are blocked within the same
service instance; an instance restart cannot resume its old object token.
The underlying inner lease uses its existing exclusive lease lock and durable
one-call consumption. This is **not** a new durable cross-process envelope-ID
deduplication protocol: another trusted caller with a new explicit confirmation
can issue a new inner lease. No claim of global replay prevention is made.

`BindOutput` is pure consistency validation, not a signature verifier or grant
API. It ties source, envelope, full reviewed request, model/runtime/artifact
hashes, terminal receipt, inner lease ID and exact text hash together. Only
`InvokeAsync` obtains receipts from the actual service. Fabricated mutually
consistent test records do not establish execution or authenticity; their
output remains explicitly fixture/untrusted.

## Deliberate live refusal

Only the existing `FIXTURE_STDIO_V1` profile is admitted. The mirrored KONTUR
fixture requests `LLAMA_CPP_B10621_QWEN3_8B_Q4_K_M_V1`, which is **unsupported**.
Changing only the request to use the fixture profile is a refused substitution.
An OS-isolation requirement is also refused: the underlying receipt truthfully
reports `ProcessNetworkIsolationProven=false`. No Workbench network transport
is not proof that a child process cannot access a network.

The JSON schema describes the **source proposal shape**, including unsupported
profiles and an isolation requirement. Schema validity is not runtime admission.
The actual service also checks its supported profile, bounds and local evidence.

No llama.cpp installation/argument profile, GPU resource enforcement, OS
sandbox, server, model download, live model output or KONTUR display is supplied
here. Those need separate qualification; do not route a model invocation
through the process-only lease to bypass this refusal.

## Qualification

From the repository root with the .NET 10 SDK on Windows:

```text
dotnet build Matawaka.Workbench.sln -c Release
dotnet run --project .github/qualification/source-bound-model/Probe.csproj -c Release
dotnet run --project .github/qualification/v055/Probe.csproj -c Release
```

The new probe uses a deliberately non-executable synthetic runtime and model,
a temporary fake workspace and a synthetic acquisition receipt. It tests real
read-only preview and local synthetic grant, exact cross-language fixture
parsing/refusal, source/profile/bounds/review drift, output substitution and
authority widening, one-use instance ownership and state-drift refusal before
process start. Positive output qualification uses fabricated typed records for
the pure binder: **it is not an end-to-end model execution test**.

The fixture is byte-identical to the KONTUR translator candidate's
`translation-fixture.json`. Never replace its synthetic evidence with raw
observations, real prompt text, credentials, local paths or player identifiers.

No workflow is added/changed. Existing path-filtered workflows do not execute
this new probe. These are reproducible local checks, not new CI coverage or a
live-readiness claim. Review/merge and later CI qualification remain distinct
from any runtime authority.
