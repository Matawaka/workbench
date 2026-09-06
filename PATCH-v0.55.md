# Workbench v0.55 — provenance-bound runtime execution lease

Observed predecessor:

- `65b0b49a513a6b782760a7626d6b768bf7bb7f91`
- Workbench v0.54.2 real-host materialization admission and publication closure

## Why this is additive

The observed `main` already contains the provider-neutral v0.53 one-shot runtime
execution lease. Reimplementing that primitive would duplicate authority. The
remaining integration gap is provenance: the v0.53 request/receipt binds the exact
runtime tree and executable but does not bind the calling source artifact and current
request-envelope digest.

v0.55 adds an outer provenance lease. It reuses the unchanged v0.53 primitive and
binds:

```text
source repository + source frontier + source artifact SHA-256
        + request-envelope SHA-256
        + exact v0.53 request digest
        -> one explicitly confirmed outer lease
        -> hidden process-local v0.53 grant
```

## Fail-closed properties

- source evidence must say `NONE_BY_SOURCE_RECORD`;
- process ceiling is exactly `EXACT_RUNTIME_ONLY`;
- TTL must exactly equal the inner v0.53 request TTL;
- both authority layers have one call;
- grant re-runs Preview and rejects digest drift;
- the outer lease is persisted as consumed before inner execution;
- the inner bearer is neither returned nor persisted by v0.55;
- loss/restart of the creating service cannot resume the hidden inner grant;
- bearer, state, source, request or receipt substitution fails closed;
- failure after consumption creates no retry or resume authority.

## Non-effects

This frontier does not start a process during qualification and does not authorize or
perform a model request, network access, game access, display, KONTUR policy change,
Agent Execute, ActionPermit, Stable Core change or external publication.

`External Intent != Execution Authority`.

`Source Receipt != Capability Lease`.

`Capability Lease != Model Request Authority`.

`Restart != Resume Authority`.
