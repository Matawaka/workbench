# Matawaka Workbench v0.4

Status: fixed semantic child-process checkpoint; Execute remains closed.

v0.4 builds only on the accepted v0.3 frontier. It preserves the same evidence and typed read-only authority semantics while moving semantic provider implementation out of the WPF/Runtime process.

## New boundary

```text
Workbench process
  -> sanitized SemanticEvidencePacket
  -> fixed Matawaka.Workbench.SemanticHost.exe
       stdin JSON only
       built-in provider id only
       allowlisted environment
       no dynamic path/assembly loading
  <- typed stdout JSON receipt
  -> parent recomputes output digest
  -> proposal/checkpoint
  -> STOP
```

The provider id remains data. JSON cannot select an executable, DLL, script or path.

## Isolation claims

v0.4 establishes a separate-process and IPC boundary with:

- one fixed child executable under the published Workbench directory;
- stdin-only semantic input and stdout-only semantic receipt output;
- a reduced environment allowlist;
- isolated temporary working directory;
- timeout and cancellation with process-tree kill;
- bounded input/output size;
- parent-side provider/input/output digest verification.

It does **not** establish an AppContainer, ACL sandbox, VM, restricted token, network sandbox or different Windows account. The child process uses the same Windows user security context.

`Process Isolation != OS Sandbox`

`Fixed Process Invocation != Arbitrary Process Authority`

## Provider equivalence target

The two built-in provider algorithms are intentionally kept semantically equivalent to v0.3. For the same accepted evidence packet:

- `InputDigest` should remain unchanged;
- provider-specific `OutputDigest` should remain equal to the same provider's v0.3 output;
- authority remains read-only;
- mutations remain zero.

## Authority

`freeshield-read-only-bridge/v0.4` still grants only Observe/Propose. `execute` is denied before evidence collection and before semantic child-process launch.
