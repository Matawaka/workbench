# Workbench v0.6.1 installer/runtime hotfix

Installer/runtime hotfix for Workbench v0.6.0.

The v0.6 restricted process creates anonymous Win32 pipes without `FILE_FLAG_OVERLAPPED`. The initial managed wrapper incorrectly constructed `FileStream(..., isAsync: true)`, which .NET rejects for synchronous native handles before semantic IPC can proceed.

v0.6.1 keeps the same restricted-token, Low Integrity, suspended launch, Job Object, digest, authority, and semantic contracts. It changes only the three parent-side pipe wrappers to `isAsync: false`. Async `StreamReader`/`StreamWriter` operations remain usable through the stream API without claiming overlapped native handles.

No authority expansion, network access, repository mutation, ActionPermit, materialization authority, or OS sandbox claim is introduced.
