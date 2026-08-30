# Workbench v0.23 — Recovery Evidence Portability / Replay Check

v0.23 adds a post-acceptance `Recovery replay` surface over the closed v0.22
bounded recovery evidence envelope.

The replay service first SHA-verifies the retained v0.22 closure and its three
bound evidence artifacts (v0.19 positive drill, v0.20 admission, v0.21 negative
matrix), copies those exact JSON bytes into one local replay capsule under
`artifacts/recovery-replays`, and then performs replay from the copied bytes
only. Historical absolute paths embedded inside those receipts are not
dereferenced during replay.

This demonstrates independence from the continued availability of the
historical nested fixture directories after capsule creation. It does **not**
claim cross-machine/cross-OS portability, does not authenticate producers
beyond retained byte hashes, and does not create live recovery, rollback,
deletion, source, build, checkpoint, network, catalog, Agent Execute, general
recovery-claim, or Stable Core authority.
