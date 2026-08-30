# Workbench v0.24 — Relocatable Recovery Replay Capsule Drill

v0.24 adds a post-acceptance `Recovery relocate` drill over a retained passing
v0.23 recovery replay capsule.

The drill requires `workbench-v0.24-accepted` on a clean main Workbench
repository, verifies the exact five-file v0.23 capsule set and every SHA-256,
then copies those exact JSON bytes from `artifacts/recovery-replays` into a
disjoint local root under `.workbench/recovery-replay-relocations`.

Replay after the copy reads only the relocated files. It reproduces both the
capsule manifest digest and the v0.22 evidence-envelope digest, rechecks the
positive drill, admission binding and negative-refusal semantics, and confirms
that the main Workbench Git HEAD/tags/dirty state remain unchanged.

The drill deliberately proves only local-root relocation on the same machine.
It does **not** prove cross-machine/cross-OS portability, does not dereference
historical fixture paths during relocated replay, and does not create recovery,
rollback, deletion, source mutation, build, checkpoint, network, catalog,
Agent Execute, general recovery-claim, automatic recovery, or Stable Core
authority.
