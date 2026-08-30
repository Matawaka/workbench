# Matawaka Workbench v0.15 — self-hosted update repeatability proof

v0.15 intentionally adds no new maintenance capability. Its purpose is to
exercise the accepted v0.14 updater end to end without an external bootstrap,
repair runtime, or out-of-band source mutation.

Target sequence:

`validated local package`
→ `explicit staging materialization`
→ `read-only staged Add/Replace/NoOp plan`
→ `explicit exact source apply + fixed offline build/publish`
→ `separate exact candidate launch`
→ `Self-test`
→ `separate local checkpoint`

## Acceptance claim

A successful v0.15 transition is evidence of **repeatability**, not merely
reachability: the same accepted Workbench maintenance gates can carry a second
successor from an already accepted self-hosted predecessor.

`one successful repaired transition != repeatable self-hosting proof`

`v0.14 accepted + clean GUI-only v0.15 transition = repeatability evidence`

## Authority remains non-transitive

Package validity, staging materialization, staged source planning, source
apply/build, candidate launch, and checkpoint remain distinct authority gates.
No receipt from an earlier gate is treated as authority for a later gate.

## Non-effects

v0.15 does not add Git remote publication authority, network model access,
Matawaka catalog mutation authority, Agent Execute/ActionPermit authority,
Stable Core promotion, or an OS sandbox claim. The package-format contract
remains `matawaka.workbench-update-package/v0.10`.
