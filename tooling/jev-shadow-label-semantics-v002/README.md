# Jev Shadow Label Semantics v0.2

The first real Workbench shadow case exposed a human-label semantics problem that synthetic qualification did not surface cleanly.

## Problem

Some Noul questions are conditional. In particular:

`hasReliableRollback` asks about rollback **if the requested operation changes externally observable state**.

For a read-only operation where `causesExternalMutation=false`, forcing a human reviewer to choose rollback=true/false creates an artificial oracle.

Likewise, reviewer uncertainty should not be encoded as a boolean with confidence 0.

## v0.2 disposition

Each human Noul label has one of:

- `ASSERTED`: truth is boolean and reviewerConfidence is [0,1].
- `UNDETERMINED`: truth=null and confidence=null.
- `NOT_APPLICABLE`: truth=null and confidence=null.

`hasReliableRollback=NOT_APPLICABLE` is accepted only when the same label asserts `causesExternalMutation=false`.

The frozen HUMAN_PRIMARY v0.1 artifact is never changed. v0.2 is used for secondary/adjudicated labels and later corpus truth.

## Primary comparison

A private helper may compare frozen HUMAN_PRIMARY to Jev after the send. Its output is explicitly:

`PRIMARY_DIAGNOSTIC_ONLY_NOT_CALIBRATION`.

It cannot create HUMAN_ADJUDICATED truth, a production threshold, or Workbench authority.


## Blinding-safe CLI separation

Secondary labeling and post-send diagnostic comparison are physically separate commands.

`secondary:init` reads only the blinded review packet. It does not accept a Jev receipt.

`primary:compare-private` reads the frozen primary label and Jev receipt and must remain private. It is not provided to the secondary reviewer before labeling.

This separation prevents accidental model-output leakage into HUMAN_SECONDARY.
