# Jev Shadow Evidence Corpus v0.1

Purpose: collect observation-only Jev evidence against independently produced human labels without expanding Workbench authority.

## Separation

`sanitized candidate → blinded review packet → independent label → frozen Jev receipt → corpus join → scoring`

The labeler receives the blinded review packet, not the Jev receipt or probabilities.

## Public vs private data

This public repository may contain **synthetic controls only**. Real sanitized shadow packets and human labels may still contain sensitive enterprise context and must remain in a private/local evidence store unless separately approved for publication.

Public code may publish aggregate metrics and non-sensitive frozen receipts only after review.

## Scoring populations

- `SYNTHETIC_CONTROL + SYNTHETIC_ORACLE` → synthetic-control diagnostics only.
- `SANITIZED_REAL_SHADOW + HUMAN_ADJUDICATED` → deployment-calibration population.
- primary/secondary labels are retained for review but are not deployment-calibration ground truth.
- `operationalSpecificity` remains research-only and is excluded from policy-eligible aggregate metrics.

## Metrics

Per signal and aggregate:
- Brier mean;
- directional matches;
- 0.5 boundary ties;
- high-confidence wrong observations;
- truth polarity counts;
- ECE only after at least 30 independent eligible cases (default).

Repeated calls from the same case must not be counted as independent deployment samples.

## Authority boundary

Corpus creation, labeling and scoring all carry `normativeEffect=NONE` / `authorityIssuance=OUT_OF_SCOPE`. No metric creates a Workbench permit/deny decision or production threshold.

## Private real-shadow intake

Use `npm run intake:private-real -- <candidate.json> <receipt.json> <private-output-dir> <case-id>` only with an output directory **outside this public repository**. The tool refuses repository-local destinations.

The private bundle separates files for blinded review:
- reviewer may receive: `review-packet.json`, `label.primary.json`;
- reviewer must not receive before labeling: `source/receipt.json`, `case.json`.

After independent primary/secondary review, adjudication should produce a `HUMAN_ADJUDICATED` label before the case is admitted to deployment-calibration scoring.
