# Matawaka Workbench v0.7

Independent Windows workbench for evidence-bounded Matawaka analysis.

## Current boundary

`agent.run` supports read-only `observe` / `propose`. `execute` remains deny-by-default with mutation budget 0.

The semantic stage is executed by one fixed `SemanticHost.exe` whose SHA-256 is bound at build time. The child is started with a restricted primary token, maximum privileges disabled, Low integrity, suspended launch, and Windows Job Object containment. v0.7 adds a pre-input **runtime security attestation**: the child observes its own effective token and Job membership; the parent verifies that evidence before transmitting semantic input.

Semantic providers remain built-in and offline:

- `local-contract-synthesis-v0.3`
- `deterministic-evidence-semantic-v0.2`

## Important non-claims

Runtime attestation is evidence about the observed child context, not an OS sandbox. v0.7 does not enforce network isolation, AppContainer, filesystem namespace isolation, VM isolation, repository mutation, or arbitrary executable loading.

The Workbench remains a local composition. Repeated implementation mechanics do not by themselves establish a new UU-AAP Stable Core primitive or shared interface admission.
