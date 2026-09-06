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

## Final exact-head qualification

Exact candidate head:

`f54cbfb5c613616f66b139fb55a14b8fab73e874`

Windows run / job:

- run `34016037030`
- job `101439846420`
- result `SUCCESS`

Artifact:

- artifact `9983945849`
- name `workbench-v0561-provenance-human-test`
- bytes `66896539`
- ZIP SHA-256 `002c929760f6a65701ff0398e74c07ace18a5cfcc81065aa99e2aaced4ed39bc`
- bound exact head `f54cbfb5c613616f66b139fb55a14b8fab73e874`

The final qualification proves full Release build, exact fixture, plain-Russian origin wording, absence of Cyrillic `провенанс` in the Russian presentation source, hostile fail-closed semantics, review-only fencing, self-contained human-test publish, and clean repository boundary.

## Final human acceptance focus

The prior visual review already passed layout, hierarchy, copyability, unsigned/truth/authority distinction, and absence of action-like controls. The remaining human check is only:

1. Is `СВЕДЕНИЯ О ПРОИСХОЖДЕНИИ ЗАФИКСИРОВАНЫ — ПОЛНОМОЧИЙ НЕТ` clearer than `ПРОВЕНАНС ЗАФИКСИРОВАН — ПОЛНОМОЧИЙ НЕТ`?
2. Does the new phrase still avoid implying truth, trust, authenticity, or authority?

A human confirmation of these two points is required before this PR may become merge-ready.

## Non-effects

This candidate does not create an accepted v0.56.1 release or tag. `workbench-v0.55.2-accepted` remains the accepted/public Workbench release identity until a separate accepted-version procedure exists.
