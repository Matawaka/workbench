# Workbench v0.56.1 — Provenance human review

Status: `READY_FOR_FINAL_WORDING_CONFIRMATION`

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

The only remaining human-facing issue was terminology. The evidence model and authority boundary remained unchanged.

## Terminology refinement

For the Russian presentation only, user-facing `провенанс` wording has been replaced with the plainer and semantically narrower phrase:

`сведения о происхождении`

The Russian no-authority headline is now:

`СВЕДЕНИЯ О ПРОИСХОЖДЕНИИ ЗАФИКСИРОВАНЫ — ПОЛНОМОЧИЙ НЕТ`

This wording intentionally does **not** use `подлинность`, `доверие`, `сертифицировано`, or another stronger claim. Those would overstate what the admitted C2PA/external-reference evidence proves.

The English technical term `provenance` remains unchanged in the English view and in internal code/schema names.

## Preserved presentation requirements

The exact same admitted evidence and authority boundary remain in force:

- yellow information/warning banner foregrounds the no-authority headline;
- `Русский` is the default local read-only view and `English` remains available;
- both views are generated from the same admitted receipt;
- exact evidence SHA-256, repository/frontier, path, and admission decision remain single-line read-only selectable values;
- normal selection / `Ctrl+C` is available without a copy button or custom clipboard capability;
- no Approve, Trust, Grant, Run, Execute, Publish, or equivalent authority-like control exists.

## Final terminology qualification

Exact qualified candidate head:

`185dcd980e4565d9e128291fd0b9f4cf0ed6d3bf`

Windows run / job:

- run `34015906909`
- job `101439497330`
- result `SUCCESS`

The final semantic qualification additionally proves:

- Russian headline uses `СВЕДЕНИЯ О ПРОИСХОЖДЕНИИ`;
- Russian headline still foregrounds `ПОЛНОМОЧИЙ НЕТ`;
- Russian presentation source contains no user-facing Cyrillic `провенанс` jargon;
- English technical `provenance` wording remains unchanged;
- exact evidence fixture/admission boundary is unchanged;
- full Workbench Release build, hostile suite, review-only fencing, self-contained publish, and clean-tree checks remain GREEN.

## Final human acceptance focus

The prior visual review already passed layout, hierarchy, copyability, unsigned/truth/authority distinction, and absence of action-like controls. The remaining human check is only:

1. Is `СВЕДЕНИЯ О ПРОИСХОЖДЕНИИ ЗАФИКСИРОВАНЫ — ПОЛНОМОЧИЙ НЕТ` clearer than `ПРОВЕНАНС ЗАФИКСИРОВАН — ПОЛНОМОЧИЙ НЕТ`?
2. Does the new phrase still avoid implying truth, trust, authenticity, or authority?

A human confirmation of these two points is required before this PR may become merge-ready.

## Non-effects

This candidate does not create an accepted v0.56.1 release or tag. `workbench-v0.55.2-accepted` remains the accepted/public Workbench release identity until a separate accepted-version procedure exists.
