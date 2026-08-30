# Matawaka Workbench v0.8 security boundary

v0.8 does not widen the accepted v0.7 authority/security boundary.

The semantic host remains a verified fixed binary launched with a restricted
Low-integrity token, assigned to a bounded Job Object before resume, and required
to attest its observed token/Job state before semantic input is released.

The new Workbench-local acceptance harness:

- forces `AllowGitFetch=false` for its internal runs;
- requires the Agent checkbox to be explicitly enabled;
- runs only Observe/Propose plus a negative Execute request;
- does not grant repository mutation, network model access, arbitrary process
  execution, materialization authority, execution authority, or ActionPermit;
- writes only one receipt below `Workbench/artifacts/acceptance`;
- does not represent its result as canonical UU-AAP conformance.

`Acceptance Automation != Authority`.
`Acceptance Receipt != OS Sandbox Proof`.
