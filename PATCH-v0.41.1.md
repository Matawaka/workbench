# Workbench v0.41.1 — JSON search visible-selection stabilization

## Predecessor

- accepted commit: `1ae2ccd8a0d789c46b26ac29970e2a1a697d8763`
- accepted tag: `workbench-v0.41-accepted`
- semantic predecessor: `0.41.0`

## Problem

v0.41 correctly finds, wraps, counts and scrolls to text matches, but after a match the UI returns keyboard focus to the search box. WPF does not display an inactive TextBox selection by default. Therefore a match can be logically selected while not remaining visibly highlighted.

`Found Match != Invisible Match`.

## Stabilization

v0.41.1 keeps the accepted v0.41 pure search algorithm and v0.41 source handler byte-identical. A new presentation adapter:

- enables `IsInactiveSelectionHighlightEnabled=true` on the four active text output panes;
- presents a match only with `Select(start,length)` plus bounded line scrolling;
- performs no post-selection `CaretIndex` move;
- returns focus to the search box so Enter/F3 traversal remains convenient;
- does not edit output text, JSON, clipboard, files, receipts or authority state.

The v0.41.1 startup routing first installs the complete accepted v0.41 routing, then replaces only search-presentation and release-bound Self-test/Accept/Publish handlers.

## Lifecycle

- target version: `0.41.1`
- target tag: `workbench-v0.41.1-accepted`
- exact parent: `1ae2ccd8a0d789c46b26ac29970e2a1a697d8763`
- one-confirmation v0.40 transition bootstrap remains unchanged and reusable;
- Publish accepted remains explicit;
- Lifecycle receipt remains explicit.

## Non-effects

No search algorithm change, no output mutation, no clipboard mutation, no Local Apps import/copy/move authority, no local app mutation, no network/catalog/Agent Execute/ActionPermit, no Stable Core/interface-registry promotion.
