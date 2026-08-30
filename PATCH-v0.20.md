# Workbench v0.20 — Recovery Capability Admission

This increment converts the retained successful v0.19 isolated recovery drill into an explicit, read-only, evidence-bound admission decision.

The new `Recovery admission` surface runs only against a clean main Workbench repository whose current HEAD is tagged `workbench-v0.20-accepted`. It byte-binds the retained `isolated-recovery-drill-v0.19` artifact, revalidates the drill's positive bounded-recovery facts and the drill authority boundary, and emits a separate admission receipt under `artifacts/recovery-admissions`.

A successful admission means only that Workbench has isolated evidence for one bounded recovery shape: restoring exact tracked candidate bytes from the current accepted HEAD and removing exact byte-reverified untracked candidate additions while preserving fixture HEAD/tags. It does not claim recovery from every failure mode, does not prove production-main-repository recovery, does not authorize automatic recovery, and does not promote the mechanism to UU-AAP Stable Core.

Admission is evidence classification, not recovery authority. No source mutation, rollback, deletion, build, checkpoint, network, catalog mutation or Agent Execute is performed by this surface.
