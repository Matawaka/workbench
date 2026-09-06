# Workbench v0.56.1 — Provenance human review

Status: `HUMAN_REVIEW_CHANGES_REQUIRED`

This candidate adds a read-only human review surface over the already merged Workbench provenance admission primitive.

## Safe test mode

Run only:

```text
Matawaka.Workbench.App.exe --provenance-review-only
```

The review-only path uses the normal `MainWindow` but does not configure the v0.55.2 acceptance/publication startup route. It detaches the historical `Loaded` and `Closing` handlers, disables maintenance and installed-app action surfaces, selects the Provenance tab automatically, and reads only the exact embedded accepted UU-AAP #955 qualification bytes.

The provenance admission service remains pure data admission. The review surface does not retrieve or refresh evidence and does not run Git, `c2patool`, another process, a model, or a runtime.

## Round 1 human review — 2026-09-06

The first qualified candidate was reviewed visually on Windows. Human findings:

1. `PROVENANCE OBSERVED — NO AUTHORITY` was understandable, but the primary information line should receive stronger yellow highlighting.
2. `UNSIGNED / NOT VERIFIED` was understandable.
3. Truth and publication authority boundaries were understandable.
4. No visual element looked like permission to act.
5. Evidence identifiers were readable, but SHA/source/path should remain on one line and be directly selectable/copyable when needed.
6. The hierarchy was understandable, but a Russian translation is required.

This is a **partial human PASS with mandatory presentation changes**, not merge authority.

## Round 2 candidate requirements

The revised candidate must preserve the same admitted evidence and authority boundary while changing presentation only:

- a yellow warning/information banner foregrounds the no-authority headline;
- one top-level `Provenance` surface contains local read-only language views;
- `Русский` is the default language view and `English` remains available;
- both languages are generated from the same admitted receipt;
- exact evidence SHA-256, pinned repository/frontier, path, and admission decision are single-line read-only selectable values;
- those values can be copied with normal text selection / `Ctrl+C` without adding a copy button or custom clipboard capability;
- no Approve, Trust, Grant, Run, Execute, Publish, or equivalent authority-like control is introduced.

## Expected meaning

The English message remains:

`PROVENANCE OBSERVED — NO AUTHORITY`

The Russian message is:

`ПРОВЕНАНС ЗАФИКСИРОВАН — ПОЛНОМОЧИЙ НЕТ`

Both views must make it easy to distinguish:

- observed C2PA external-reference binding;
- exact external evidence byte match;
- accepted live C2PA validation;
- observed Workbench release identity;
- unsigned / unverified Git tag;
- truth not established;
- publication authority not established;
- runtime/model authority not created;
- response/display authority not created;
- action/successor permits not created.

The exact evidence SHA-256 and pinned source frontier must remain visible and copyable as text.

## Round 2 human acceptance questions

A human reviewer must answer these before the PR can become merge-ready:

1. Is the yellow no-authority banner immediately noticeable without looking like an approval/trust signal?
2. In the Russian view, is it clear within about five seconds that provenance was observed but no authority was granted?
3. Is `НЕ ПОДПИСАН / НЕ ПРОВЕРЕН (UNSIGNED / NOT VERIFIED)` visually obvious?
4. Is it clear that truth and publication authority are not established and runtime/model/action authority was not created?
5. Can SHA-256, source frontier, evidence path, and admission decision each be selected/copied cleanly without wrapping?
6. Is the Russian wording natural and understandable without specialist C2PA knowledge?
7. Does switching between `Русский` and `English` change only presentation, with no operational action surface appearing?

Human feedback may require further wording/layout changes and a fresh Windows qualification. A green CI result is not sufficient to merge this candidate.

## Non-effects

This candidate does not create an accepted v0.56.1 release or tag. `workbench-v0.55.2-accepted` remains the accepted/public Workbench release identity until a separate accepted-version procedure exists.
