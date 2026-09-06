# Workbench v0.56.1 — Provenance human review

Status: `HUMAN_TEST_REQUIRED_BEFORE_MERGE`

This candidate adds a read-only human review surface over the already merged Workbench provenance admission primitive.

## Safe test mode

Run only:

```text
Matawaka.Workbench.App.exe --provenance-review-only
```

The review-only path uses the normal `MainWindow` but does not configure the v0.55.2 acceptance/publication startup route. It detaches the historical `Loaded` and `Closing` handlers, disables maintenance and installed-app action surfaces, selects the Provenance tab automatically, and reads only the exact embedded accepted UU-AAP #955 qualification bytes.

The provenance admission service remains pure data admission. The review surface does not retrieve or refresh evidence and does not run Git, `c2patool`, another process, a model, or a runtime.

## Expected meaning

The first message must be:

`PROVENANCE OBSERVED — NO AUTHORITY`

The screen should make it easy to distinguish:

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

The exact evidence SHA-256 and pinned source frontier must remain visible.

## Human acceptance questions

A human reviewer must answer these before the PR can become merge-ready:

1. Within about five seconds, is it unmistakable that provenance was observed while no authority was granted?
2. Is `UNSIGNED / NOT VERIFIED` visually obvious rather than buried in detail?
3. Is it clear that truth and publication authority are `NOT ESTABLISHED`?
4. Does any element look or read like permission to approve, trust, grant, run, execute, or publish?
5. Are the evidence source and SHA-256 readable at normal Windows scaling?
6. Is the hierarchy understandable without specialist C2PA knowledge?

Human feedback may require wording/layout changes and a fresh Windows qualification. A green CI result is not sufficient to merge this candidate.

## Non-effects

This candidate does not create an accepted v0.56.1 release or tag. `workbench-v0.55.2-accepted` remains the accepted/public Workbench release identity until a separate accepted-version procedure exists.
