# Workbench v0.21 — Recovery Negative-Control Matrix

This increment adds an isolated negative-control matrix for the already-admitted bounded recovery capability.

The matrix runs only after `workbench-v0.21-accepted` and never dirties the main Workbench repository. It creates three retained nested Git fixtures under `.workbench/recovery-negative-controls` and exercises the existing recovery assessment, planning, and execution gates against deliberately invalid recovery states:

1. an unknown dirty path with no retained candidate evidence;
2. exact bounded candidate paths whose bytes change after a READY recovery plan;
3. an exact bounded candidate whose dirty path set changes after a READY recovery plan.

The expected result in every control is refusal before any recovery authority artifact is created. Candidate bytes are retained after refusal so the negative evidence remains inspectable. Main Workbench HEAD/tags/working-tree state must remain unchanged.

This is a negative-control proof, not a general recovery claim. It adds no automatic recovery, main-repository recovery, deletion, build, checkpoint, network, catalog, Agent Execute, or Stable Core authority.
