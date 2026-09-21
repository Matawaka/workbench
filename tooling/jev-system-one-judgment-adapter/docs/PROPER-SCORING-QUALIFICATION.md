# Proper-scoring qualification

## Separation

A probabilistic semantic model produces beliefs. A semantic fixture may supply a ground-truth label. A production workflow may later supply an operational threshold. These are three separate layers.

For a Noul with oracle truth `y ∈ {0,1}` and returned probability `p`, alpha.5 records:

- raw `p`;
- binary direction (`p > 0.5`, `p < 0.5`, or boundary tie at `p = 0.5`);
- whether the direction matches the oracle;
- Brier loss `(p - y)^2`.

A future automation threshold such as `p >= 0.8` is **not** part of oracle correctness. It belongs to a separately reviewed policy/admission layer.

## Why

The alpha.4.2 standard run showed correct-looking semantic direction with probabilities that differed by task complexity. Treating every value below 0.8 as a model failure would reward threshold tuning rather than probabilistic honesty.

## Calibration limits

Brier loss on a small synthetic repeated fixture set is only a diagnostic. Deployment calibration requires a larger labeled population representative of the actual Workbench traffic distribution, with independent examples and frozen model/harness identity.

## Authority

Neither an oracle match nor a high probability issues authority. `Probabilistic Judgment != Authorization` remains invariant.
