# v0.60.1 publication preparation — issue #102

This development branch must NOT be merged into public main before exact accepted-source publication: doing so would move the bound public predecessor. It adds external tooling and tests only; accepted application source and historical receipts stay unchanged.

## Implemented boundary

`preflight` is a standalone Windows local-only console utility. It accepts no arguments or environment-based target overrides. Its fixed subject is `K:\Matawaka\Workbench`, accepted HEAD `58b9430fc544998a8e40ba00b6757cc630ba9081`, annotated tag `workbench-v0.60.1-accepted`, with parents `ea852feeb0e8d92a8977bb251693e7e977913dca` and `ac083598711caa0c399cc0d2c385b980c083024a` in that order.

It verifies the six original operator JSON byte hashes without rewriting them, follows only pinned launch/handoff/claim evidence, checks the app EXE hash, reads the actual annotated tag object and raw commit identity, compares the complete Git source tree/index/working source and the exact 79-file Add/Replace delta. Unsupported layouts, include/filter/promisor configuration, source drift, unsafe paths, duplicate JSON and missing evidence fail closed. Git maintenance subprocesses are bounded and local-only.

Preview has no network, credential, Git object/ref/source write, or publication implementation. Only the exact confirmation `EXPORT-EXACT-V0601-PUBLICATION-PREFLIGHT` permits creation of one new local JSON under `artifacts/publication-v0601`; source and evidence are rechecked first, existing output is never overwritten. Historical bootstrap expiry is assessed at historical completion, not reused as current authority. Historical PID is not re-observed or signalled.

Keep the full generated Windows bundle directory together. Managed code is single-file; native runtime DLLs remain adjacent without requested native self-extraction. The tool is run outside the accepted Workbench repository. Do not admit a binary from a failed or incomplete qualification run.

## Not implemented or admitted

This is NOT an admitted production publisher. No GitHub push, remote ls-remote, credentials access, update, acceptance replay, runtime/model invocation or source mutation is implemented by preflight. A fresh preflight receipt is evidence only, not publication authority. The EXE hash does not prove the integrity of every .NET application runtime output; that broader runtime claim is explicitly absent. Local observations are bounded snapshots, not an OS-level exclusion of hostile concurrent processes.

`publisher/pre-push.template` is the proposed exact-advertisement guard. Its test harness uses isolated temporary local Git remotes, not GitHub. One ordinary atomic push plus this guard enforces the advertised old main and absent target tag; no force flag, tag-only fallback, arbitrary ref or retry is admitted. Credential strategy, durable one-attempt publication journal, exact preflight binding and production operator confirmation still require a separate qualified implementation.

## Qualification

- Python transport suite: 19 cases, including a negative control demonstrating that atomic fast-forward alone accepts an unexpected ancestor, and guarded main/tag races before and after advertisement.
- C# preflight suite: real local source/index/tag/parent checks, unsupported config and forbidden command families.
- C# evidence suite: full synthetic six-receipt + source + launch + handoff + claim chain and hostile tampering. Synthetic internal Spec injection is confined to friend test assemblies; production CLI accepts no overrides.
- Windows and Linux jobs must pass on the same exact source SHA. CI network use installs build tools and uploads artifacts; the tested fixtures do not contact the production remote.

Initial run `34222901423` is retained as failed evidence: C# span/await compile issue and Windows fixture alternates newline issue. The corrected successor source must be qualified independently; the failed run is not relabeled PASS.
