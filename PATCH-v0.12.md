# Matawaka Workbench v0.12 — staged source-apply plan gate

v0.12 extends the accepted v0.11 update path with a new **read-only staged apply plan**.

The authority sequence remains explicit and non-transitive:

`validated package plan`
→ `explicit staging materialization`
→ `staged source-apply plan`
→ **no source mutation authority yet**

## New capability

After v0.11 has materialized exact payload bytes under
`Workbench/.workbench/update-materializations`, v0.12 can re-verify that staging
and calculate the exact repository-source effects that a later source-apply gate
would request.

The plan records, per path:

- `Add`, `Replace`, or `NoOp`;
- current worktree SHA-256 when a destination exists;
- staged SHA-256;
- staged byte count.

## Fail-closed checks

The staged apply plan requires:

- the materialization receipt status to be `MATERIALIZED_STAGING_ONLY`;
- the materialization authority to remain staging-only;
- current Workbench HEAD to still equal the package predecessor;
- the predecessor accepted tag to still point at HEAD;
- a clean Workbench working tree;
- the staging directory to remain inside the fixed `.workbench/update-materializations` root;
- an exact staging file set, with no missing or extra payload files;
- SHA-256 equality for every staged payload file;
- safe repository-relative destination paths.

Any mismatch invalidates the plan.

## Explicit non-effects

v0.12 staged apply planning does **not**:

- overwrite tracked Workbench source;
- add, delete, or rename repository files;
- run `dotnet restore`, build, test, or publish;
- execute installer scripts or arbitrary processes;
- run `git add`, commit, tag, fetch, or push;
- mutate Matawaka catalog repositories;
- use the network;
- grant Agent Execute or ActionPermit authority.

A `READY_FOR_SEPARATE_SOURCE_APPLY_AUTHORITY` receipt is therefore evidence of
a bounded possible source transition, not authorization to perform it.

`Proof of staged possibility ≠ source-apply authority ≠ build authority ≠ checkpoint authority.`
