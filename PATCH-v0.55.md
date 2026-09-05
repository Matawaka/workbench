# Workbench v0.55 — bounded one-shot local-model invocation lease

Exact accepted/public predecessor:
- `workbench-v0.54.2-accepted`
- `65b0b49a513a6b782760a7626d6b768bf7bb7f91`

Purpose:
- add a separate provider-neutral model-request authority above exact v0.52 model-artifact evidence and exact v0.54/v0.53 runtime-tree evidence;
- preserve `Process Execution Authority != Model Request Authority`;
- first admitted profile is deterministic `FIXTURE_STDIO_V1` for offline/real-host boundary qualification, not a real LM1/llama.cpp policy adapter.

Core corridor:

`exact verified model + MATERIALIZED_VERIFIED runtime -> Preview -> explicit confirmation -> one-shot Model Invocation Lease -> authority consumed -> exact model/runtime rehash -> one direct subprocess request -> bounded stdout/stderr -> UNTRUSTED_LOCAL_MODEL_OUTPUT -> STOP`

Key boundaries:
- caller supplies no arbitrary process argument vector;
- request text is byte-bounded; canonical lease state retains digest + size, not raw text;
- stdout/stderr are independently bounded;
- timeout/overrun stops only the owned process tree;
- no automatic retry/resume/replay;
- output remains untrusted and creates no response/display/game/action/successor authority;
- `No Workbench Network Transport != OS-Level Process Network Isolation`;
- v0.52 acquisition, v0.53 execution and v0.54 materialization primitives remain unchanged;
- real Qwen/llama/CUDA acquisition, benchmark and KONTUR inference are not authorized by v0.55.

Also includes the reusable #73 smoke-identity helper: admitted test-artifact size/hash are derived only from bytes re-fetched from an immutable raw GitHub commit URL.

Publication remains deferred after local v0.55 acceptance until a tiny real-host v0.55 fixture admission is separately observed.
