# Reconciled Workbench v0.60.1 publication successor

Implementation and qualification only until the exact delivered artifact is audited. Do not merge tooling into main. This is not a Workbench update, acceptance replay, standalone diagnostic, or execution of the consumed V1 publisher.

## Inputs and changed-condition evidence

The fixed operator plan requires all fifteen original byte identities: eight acceptance/recovery records, the V2 preflight, V1 publication attempt/outcome, denied discovery attempt/result, and the completed permission recheck attempt/result. The last result SHA-256 is `e6c720426ad6b285dc6f078a2fb941f732f54aaf5359b788288173998d40577f`; its attempt SHA-256 is `30bce27780fb0b3acc26ffdbcb4a674adfe8c2b4114bb74e161b7f158e69329e`.

All original terminal states remain unchanged. The old unverified publication is not proved effect-free or retrospectively assigned a cause. The observed transition ACCESS_FORBIDDEN -> RECEIVE_DISCOVERY_COMPLETED after operator-reported Contents correction is evidence for this new procedure, not ongoing write authority.

## Fixed target

- Repository `K:\Matawaka\Workbench`.
- Fixed HTTPS destination `https://github.com/Matawaka/workbench.git`.
- Old public main `ac083598711caa0c399cc0d2c385b980c083024a`.
- New main `58b9430fc544998a8e40ba00b6757cc630ba9081`.
- Existing annotated tag object `c07409c7973f1a4181e94168ba172bba0d56355f` for `refs/tags/workbench-v0.60.1-accepted` only.
- No historical v0.60 tag publication, extra refs, tag recreation, force flags, API ref changes, fallback, rollback, cleanup or automatic retry.

## Operator boundary

The new zero-argument executable must run outside the repository. Existing exact MinGit 2.55.0.windows.4 is reused; its complete 365-file distribution is verified and read-locked. The accepted verifier is not changed. Local preview makes no remote or credential call, writes no record and creates no stage.

NEW confirmation `PUBLISH-RECONCILED-V0601-58B9430-C07409C`, with a five-minute confirmation lifetime, and a hidden locally entered token are required. The old publication/discovery/recheck confirmations are refused. No token is requested in chat, arguments, files or logs. Use only the selected workbench repository and previously agreed Contents read/write and Workflows write scope; do not broaden unrelated rights. A valid corrected token is not proof of branch-rule or atomic-operation admission.

A durable create-new attempt precedes stage/network work. The utility creates `K:\Matawaka\Tools\WorkbenchV0601ReconciledPublicationV1`; operator must not pre-create or reuse it. Preserve `WorkbenchV0601FixedPublicationV1`, `WorkbenchV0601ReceiveDiscoveryV1` and `WorkbenchV0601PermissionRecheckV1`. Fresh V2 and all three old-stage byte observations are checked before and after the new attempt. No old executable or old publication orchestrator is invoked.

One ordinary atomic two-ref push at most; at most two credential-free public ref reads. Exact controlled pre-push guard checks the advertised old main and absent tag within the same push session, not merely during the earlier read. New isolated transport contains copied objects and its own fixed config/hook only. Source config/hooks/refspecs/credentials/alternates are not inherited. Source Git store, index, config, HEAD, tag, tracked source and old receipts are not intentionally modified. Only the new attempt/outcome or publication record, new isolated copy and exact public updates are admitted.

## Failure and disclosure

The new native driver uses the separately qualified parallel bounded capture: 512 KiB per stream, 30-second main deadline, immediate limit signal, bounded cleanup. These limits are not OS scheduling or process-start latency guarantees. Records retain only allowlisted categories, exit codes, counts, EOF/overflow/timeouts and selected exact refs. No raw output or secret-derived hashes are retained. Fixed process construction/guard/config/parser are reused from the byte-pinned V1 source, while the old unbounded cleanup driver and old orchestrator/entry point are excluded from production compilation. The original source file remains unchanged.

Any uncertain push/readback outcome remains UNVERIFIED_NO_RETRY even if remote refs later appear updated. No post-read is attempted with incomplete cleanup. New consumption cannot authorize another run. Failed/partial record writes must not be cleaned up or overwritten.

## Completion

Successful utility result requires its own zero-exit complete push, exact guard, target main/tag/peel readback, new transport preservation and fresh original snapshot/three prior-stage verification. This creates a new success record, never modifies old failure evidence. Independent GitHub main/tag/peel/commit-tree/ordered-parent readback and evidence audit are still required before #102 closure/#804 completion.

New output files under `artifacts/publication-v0601`:
- `attempt-publish-reconciled-v1-58b9430fc544998a8e40ba00b6757cc630ba9081.json`
- success: `publication-reconciled-v1-58b9430fc544998a8e40ba00b6757cc630ba9081.json`
- refusal: `outcome-publish-reconciled-v1-58b9430fc544998a8e40ba00b6757cc630ba9081.json`

No OS-wide isolation, all-runtime integrity, secure-memory guarantee, indefinite integrity or Agent Execute/ActionPermit authority is claimed.
