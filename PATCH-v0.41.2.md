# Workbench v0.41.2 — focus-primed visible JSON selection

## Predecessor

- exact locally accepted commit: `9214a0c39ba4acd73e766bf3eb2fdef86b984b9d`
- local predecessor tag: `workbench-v0.41.1-accepted`
- remotely published accepted base remains: `1ae2ccd8a0d789c46b26ac29970e2a1a697d8763 / workbench-v0.41-accepted`
- v0.41.1 is intentionally not published after its real-host visible-selection qualification failed.

## Observed v0.41.1 gap

On the real Windows host, search found and counted the requested text and scrolled the match into view, but the matching range did not remain visibly highlighted while keyboard focus was in the search box.

The v0.41.1 WPF probe had proven selection indices and `IsInactiveSelectionHighlightEnabled=true`, but had not exercised a shown-window focus lifecycle.

`Selected Range != Rendered Inactive Highlight`.

## Repair

v0.41.2 keeps the accepted v0.41 pure search algorithm and all v0.41.1 implementation files byte-identical except successor startup routing. A new presentation adapter performs a bounded focus pulse for a found range:

`output.Focus() -> Select(start,length) -> ScrollToLine -> search.Focus()`

It also maps the inactive-selection background/text resources to the WPF system selection brushes, so the inactive selected range is visually unambiguous while remaining aligned with system theme/high-contrast colors.

The adapter:

- preserves `IsReadOnly` output panes and exact output text bytes;
- performs no `CaretIndex` assignment;
- restores keyboard focus to the search box for Enter/F3 traversal;
- creates no clipboard/file/receipt/authority effect;
- does not change `JsonOutputSearchV041Service` semantics.

## Lifecycle

- target version: `0.41.2`
- target tag: `workbench-v0.41.2-accepted`
- exact local predecessor: `9214a0c39ba4acd73e766bf3eb2fdef86b984b9d / workbench-v0.41.1-accepted`
- one-confirmation v0.40 transition bootstrap remains unchanged and reusable;
- local Self-test + Accept may complete automatically under the already accepted one-shot transition lease;
- real-host visible-selection confirmation is still required before publication;
- Publish accepted and Lifecycle receipt remain separate explicit actions.

## Publication repair boundary

Because v0.41.1 failed its real-host UX qualification only after local acceptance, the v0.41.2 publication adapter requires this exact chain:

`remote accepted v0.41 1ae2ccd8... -> local-only v0.41.1 9214a0c3... -> accepted v0.41.2`

Remote `main` may fast-forward over the local-only predecessor commit, but `workbench-v0.41.1-accepted` MUST remain absent remotely. Only `workbench-v0.41.2-accepted` is published.

## Qualification

CI must include a shown WPF Window focus-lifecycle probe, not only selection-index inspection. It must prove target focus acquisition, exact selection, return of keyboard focus to the search box, inactive-selection configuration/system brush binding and byte-preservation. Final visible rendering remains a real-host operator observation before publication.

## Non-effects

No search algorithm change, no output/JSON/clipboard mutation, no Local Apps import/copy/move authority, no local app mutation, no catalog/Agent Execute/ActionPermit, no Stable Core/interface-registry promotion, no v0.41.1 accepted-tag publication.
