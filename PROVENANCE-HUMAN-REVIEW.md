# Workbench v0.56.1 — Provenance human review

Status: `HUMAN_REVIEW_TERMINOLOGY_REFINEMENT`

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

This was a partial human PASS with mandatory presentation changes, not merge authority.

## Round 2 human review — 2026-09-06

The revised Windows candidate was reviewed visually again. Human findings:

1. The yellow no-authority banner was clear and acceptable.
2. `UNSIGNED / NOT VERIFIED` remained understandable.
3. Truth/publication-authority and runtime/model/action boundaries remained understandable.
4. No visual element looked like permission to act.
5. Evidence identifiers were clear, single-line, and copyable.
6. Russian localization was understandable overall, but the transliterated technical word `провенанс` was not natural for a general Russian-speaking user.

The only remaining human-facing issue is terminology. The evidence model and authority boundary are unchanged.

## Terminology refinement

For the Russian presentation only, user-facing `провенанс` wording is replaced with the plainer and semantically narrower phrase:

`сведения о происхождении`

The Russian no-authority headline becomes:

`СВЕДЕНИЯ О ПРОИСХОЖДЕНИИ ЗАФИКСИРОВАНЫ — ПОЛНОМОЧИЙ НЕТ`

This wording intentionally does **not** use `подлинность`, `доверие`, `сертифицировано`, or another stronger claim. Those would overstate what the admitted C2PA/external-reference evidence proves.

The English technical term `provenance` remains unchanged in the English view and in internal code/schema names.

## Preserved round-2 presentation requirements

The candidate must preserve the same admitted evidence and authority boundary:

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

`СВЕДЕНИЯ О ПРОИСХОЖДЕНИИ ЗАФИКСИРОВАНЫ — ПОЛНОМОЧИЙ НЕТ`

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

## Final human acceptance focus

The prior visual review already passed layout, hierarchy, copyability, unsigned/truth/authority distinction, and absence of action-like controls. After fresh Windows qualification of the terminology-only change, the remaining human check is narrow:

1. Is `СВЕДЕНИЯ О ПРОИСХОЖДЕНИИ ЗАФИКСИРОВАНЫ — ПОЛНОМОЧИЙ НЕТ` clearer than the transliterated `провенанс` wording?
2. Does it still avoid implying truth, trust, authenticity, or authority?

A green CI result remains insufficient by itself to merge this candidate.

## Non-effects

This candidate does not create an accepted v0.56.1 release or tag. `workbench-v0.55.2-accepted` remains the accepted/public Workbench release identity until a separate accepted-version procedure exists.
