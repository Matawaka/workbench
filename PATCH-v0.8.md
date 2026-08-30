# Workbench v0.8 — Automated Acceptance Matrix

v0.8 adds a Workbench-local `Self-test` surface without changing the accepted
v0.7 semantic/security boundary.

The self-test runs two read-only `propose` passes against the same command/evidence
identity using both built-in offline providers, then a denied `execute` pass. It
records a typed Workbench-local acceptance receipt covering:

- same bounded semantic input across providers;
- distinct provider identity without authority drift;
- identical evidence and authority receipts;
- exact UU-AAP source frontier match;
- child-observed restricted/Low-integrity/Job runtime attestation;
- mutation-free proposals;
- Execute denial before evidence/semantic pipeline activation.

The receipt is written only below `Workbench/artifacts/acceptance`.

This is development tooling, not a new UU-AAP primitive, Stable Core admission,
canonical conformance claim, ActionPermit, materialization authority, or OS sandbox.
