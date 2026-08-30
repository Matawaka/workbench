# Workbench v0.1.3 patch

## Live finding from v0.1.2

The first acceptance run used `maxEvidenceItems = 80` across `FREESHIELD`, `kontur`, and `uu-aap`. FREESHIELD reached the global limit first, so later repositories were effectively inspected only until the global-limit check and produced zero selected evidence.

This was a selection-order artifact, not evidence that the later repositories lacked matching material.

## Correction

v0.1.3 separates two phases:

1. collect bounded deterministic candidates independently per focus repository;
2. select the final global evidence frontier with deterministic repository round-robin.

The global limit therefore remains meaningful while repository order no longer monopolizes it.

## Authority increment

The agent now creates a typed capability request and receives a typed decision before repository analysis.

The local policy can allow only read-only Observe/Propose. Execute remains a typed deny and does not invoke the provider.

This increment deliberately does **not** add repository mutation or network-model authority.
