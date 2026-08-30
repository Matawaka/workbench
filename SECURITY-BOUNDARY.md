# Security boundary — Workbench v0.2

## Allowed

- local JSON validation;
- local catalog inspection;
- explicit fixed git fetch through the existing catalog gate;
- read-only file evidence collection;
- balanced evidence selection;
- deterministic evidence-only semantic proposal;
- typed capability request/decision receipts;
- PCL-compatible visible progress projection;
- cancellation.

## Not authorized

- repository mutation by `agent.run`;
- arbitrary shell/process execution from JSON;
- network model/provider calls;
- hidden escalation from UI enablement to execution authority;
- materialization authority creation;
- ActionPermit creation;
- game control;
- self-expansion of authority.

## Provider isolation

The semantic provider receives no repository root paths, file handles, process runner, network client or mutation capability. Its input is a bounded evidence packet and typed authority receipt.

## Protocol source bindings

v0.2 records exact UU-AAP source frontier/path/blob bindings for PCL progress/human view, Scoped Authority Evidence, and Materialization Authority. These bindings are compatibility/reference evidence only. Workbench v0.2 does not execute those canonical JavaScript evaluators and must not represent its local adapters as canonical protocol conformance.

## Deny semantics

`DENIED` is a successful policy outcome, not a runtime failure. A denied authority request must not invoke the development/semantic provider and must preserve zero mutations.
