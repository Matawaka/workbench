# Workbench v0.56.1 — human review of origin evidence

Status: `HUMAN_CONFIRMATION_REQUIRED_BEFORE_MERGE`

This candidate adds a read-only human review surface over the already merged Workbench provenance-admission primitive.

## Safe test mode

Run only:

```text
Matawaka.Workbench.App.exe --provenance-review-only
```

The review-only path uses the normal `MainWindow` but does not configure the v0.55.2 acceptance/publication startup route. It detaches historical `Loaded` and `Closing` handlers, disables maintenance and installed-app action surfaces, selects the review tab automatically, and reads only the exact embedded accepted evidence bytes.

No evidence retrieval/refresh, Git operation, `c2patool`, external process, model invocation, runtime execution, repository/file mutation, publication, action permit, or successor permit is created by this review surface.

## Human-review history

Round 1 established that the evidence/authority distinction was understandable, but requested a stronger yellow information banner, single-line copyable evidence identifiers, and a Russian version.

Round 2 confirmed the layout, yellow banner, `UNSIGNED / NOT VERIFIED`, truth/publication-authority boundaries, absence of action-like controls, copyability, and overall Russian hierarchy. The only remaining wording issue was that the transliterated Russian technical word `провенанс` was not natural for a general user.

## Final Russian wording

The Russian presentation therefore uses the plainer, semantically bounded phrase:

```text
СВЕДЕНИЯ О ПРОИСХОЖДЕНИИ ЗАФИКСИРОВАНЫ — ПОЛНОМОЧИЙ НЕТ
```

The Russian user-facing presentation must not use `провенанс` as unexplained jargon.

The wording deliberately avoids stronger claims such as `подлинность`, `доверие` or `сертифицировано`: the admitted C2PA/external-reference evidence does not establish those properties.

The English technical view remains:

```text
PROVENANCE OBSERVED — NO AUTHORITY
```

Internal code/schema terminology is unchanged.

## Preserved human-facing boundaries

The review must continue to make clear that:

- the C2PA external-reference hash binding was observed;
- external evidence bytes matched exactly;
- live C2PA validation was accepted;
- a Workbench release identity was observed;
- the Git tag is `UNSIGNED / NOT VERIFIED`;
- truth is not established;
- publication authority is not established;
- runtime/model authority is not created;
- response/display authority is not created;
- action/successor permits are not created.

Exact evidence SHA-256, repository/frontier, path, and admission decision remain single-line read-only selectable values and may be copied using ordinary text selection / `Ctrl+C`; no copy button or custom clipboard capability is introduced.

## Human acceptance boundary

Before merge, the human reviewer must confirm only the remaining terminology question:

1. Is `СВЕДЕНИЯ О ПРОИСХОЖДЕНИИ ЗАФИКСИРОВАНЫ — ПОЛНОМОЧИЙ НЕТ` clear and natural enough for a general Russian-speaking user?
2. Does it still avoid implying truth, trust, authenticity, approval, permission, or authority?

Green CI is not merge authority. Human confirmation does not promote an accepted Workbench version or tag.

`workbench-v0.55.2-accepted` remains the accepted/public Workbench release identity until a separate accepted-version procedure exists.
