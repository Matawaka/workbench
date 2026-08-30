# Security boundary — Workbench v0.3

## Allowed

- local JSON validation;
- local catalog inspection;
- explicit fixed git fetch through the catalog gate;
- read-only evidence collection;
- deterministic balanced evidence selection;
- offline provider selection from a fixed Workbench-local registry;
- local semantic analysis over sanitized evidence only;
- typed capability/provider/semantic receipts;
- PCL-compatible visible progress;
- cancellation.

## Not authorized

- repository mutation by `agent.run`;
- arbitrary shell/process execution from JSON;
- network model/provider calls;
- ambient filesystem access by semantic providers;
- provider self-registration/self-selection after the authority decision;
- materialization authority creation;
- execution authority creation;
- ActionPermit creation;
- Stable Core/interface-registry promotion;
- game control;
- self-expansion of authority.

## Provider isolation

Semantic providers receive no repository roots, file handles, process runner, network client or mutation capability. They receive a bounded evidence packet plus typed authority receipt.

The default provider registry contains built-in providers only and JSON cannot load an assembly, executable, script or provider path. Unknown provider ids fail closed.

The v0.3 provider boundary is in-process. Not passing repository roots/process/network clients is an interface-level isolation property, **not** an OS sandbox for hostile provider code. The shipped built-in providers are local deterministic code and perform no provider-side filesystem, network or process operations.

## Exact source binding

Semantic provider execution requires the local `uu-aap` focus HEAD to equal the exact Workbench compatibility frontier. A mismatch fails before semantic analysis rather than silently using stale protocol assumptions.

## Deny semantics

`DENIED` is a normal terminal policy outcome. A denied authority request does not invoke evidence collection or semantic providers and preserves zero mutations.
