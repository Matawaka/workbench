# Matawaka Workbench v0.3

Status: local Windows provider-substitution checkpoint; Execute remains closed.

v0.3 builds only on the accepted v0.2 frontier and keeps the same read-only authority boundary.

## New surfaces

1. Workbench-local offline semantic-provider registry.
2. Two interchangeable providers behind the same sanitized evidence packet:
   - `local-contract-synthesis-v0.3` (default);
   - `deterministic-evidence-semantic-v0.2` (retained compatibility provider).
3. Typed provider-selection and semantic-analysis receipts with input/output SHA-256 digests.
4. Fail-closed exact `uu-aap` source-frontier check before semantic provider execution.
5. Explicit isolation receipt: built-in in-process boundary, no dynamic provider loading; not represented as an OS sandbox.
6. Dedicated `Semantic Provider` UI tab.
7. Exact reference binding to the UU-AAP Reusable Component Admission Audit `NO_ADMISSION` guard.

## Provider boundary

Provider input contains only:

- repository name + branch + HEAD;
- selected evidence snippets and term labels;
- balanced coverage;
- typed capability receipt.

It contains no repository roots, file handles, process runner, network client, materialization authority or mutation authority.

`Provider Selection != Authority Grant`

`Proposal != Materialization`

`Semantic Similarity != Stable Core Admission`

## Acceptance intent

Run the same `propose` command twice with the same command id and evidence frontier, switching only:

- `local-contract-synthesis-v0.3`
- `deterministic-evidence-semantic-v0.2`

The `InputDigest` should remain identical while provider/output digest and proposal may differ. This demonstrates substitutability at one sanitized boundary rather than implicit ambient access.

Execute remains denied before evidence/semantic provider invocation.
