# Workbench v0.41 — JSON Output Search + Local-App Chat Handoff

## Exact predecessor

- `45178dfc6488c2e4699b584ac29cbbc9c001c2f3`
- `workbench-v0.40.1-accepted`

## Target

- semantic version `0.41.0`
- tag `workbench-v0.41-accepted`

## Product delta

### Read-only output search

Workbench adds a compact `Find in output` bar above the output tabs.

- `Ctrl+F` focuses the search box;
- `Enter` or `F3` selects the next match;
- `Shift+Enter` or `Shift+F3` selects the previous match;
- search is `OrdinalIgnoreCase`, Unicode-safe and wraps at either end;
- the status shows `current / total` and `↻` on wrap;
- search targets the currently selected TextBox output tab;
- search does not edit the output, write files, touch clipboard or mutate receipts/authority.

The pure `JsonOutputSearchV041Service` is independently acceptance-tested for next/previous/wrap/case-insensitive/Unicode/no-match/input-preservation behavior.

### Chat-to-local-app handoff guidance

`LOCAL-APP-CHAT-HANDOFF.md` defines two manual archive conventions over existing Local Apps roles:

- new seed capsule: `matawaka-local-app-seed-<applicationId>-<version>.zip` → extract under `Apps/` → Register;
- update candidate capsule: `matawaka-local-app-candidate-<applicationId>-<targetVersion>.zip` → extract under `AppCandidates/` → Build update package.

The seed contains neither `.matawaka-app.json` nor `.matawaka-target.json`. The update candidate contains `.matawaka-target.json` but never `.matawaka-app.json`.

This documentation does not add ZIP extraction/import/copy/move authority to Workbench.

## Lifecycle preservation

- accepted v0.40 one-confirmation Update Workbench handler remains the transition mechanism;
- first-boot v0.41 may consume an exact one-shot v0.40 lease and auto Self-test → PASS-gated local Accept;
- manual Self-test/Accept remain fallback actions;
- Publish accepted and Lifecycle receipt remain separate explicit actions;
- top-level maintenance surface remains exactly 8 buttons / 0 persistent authority checkboxes.

## Non-effects

No output/JSON mutation, no clipboard effect, no Local Apps import/copy/move authority, no arbitrary process execution, no network/catalog/Agent Execute/ActionPermit, no Stable Core/interface-registry promotion.
