# v0.60.1 exact publisher boundary

This is external maintenance tooling, not a Workbench update or acceptance replay. Source is under `publisher-fixed-v1/`. Delivery requires an exact successful Windows CI run including the HTTPS suite, a downloaded artifact hash audit, and a separate operator confirmation. Merely committing this document does not admit execution.

## Fixed inputs and transition

- Operator repository: `K:\Matawaka\Workbench`.
- Audited preflight: `artifacts/publication-v0601/preflight-promisor-v2-58b9430fc544998a8e40ba00b6757cc630ba9081.json`, 6187 bytes, SHA-256 `c7a7dae81f93c783629f3fc5686aec5a2fb45533d95a5413628effa5763e1671`.
- Accepted HEAD: `58b9430fc544998a8e40ba00b6757cc630ba9081`.
- Exact annotated tag object: `c07409c7973f1a4181e94168ba172bba0d56355f`; tag `workbench-v0.60.1-accepted`, peeled commit equals accepted HEAD. No tag recreation.
- Fixed GitHub endpoint: `https://github.com/Matawaka/workbench.git`.
- One intended atomic transaction: `refs/heads/main` from `ac083598711caa0c399cc0d2c385b980c083024a` to accepted HEAD; currently absent `refs/tags/workbench-v0.60.1-accepted` to the exact annotated object.
- Public `workbench-v0.60-accepted` must remain absent at the before/after observations. This is not a claim that unrelated actors cannot concurrently create refs.

## Local preview is not publication

The operator executable accepts zero arguments and requires Windows. It checks the existing side-by-side MinGit distribution against a manifest of all 365 files derived from exact archive SHA-256 `4e03f94c2ffbf70be337e005cee02661c732dbfc81031a078bda9299b9a7d644`. It does not install Git or modify PATH or promisor configuration.

The byte-identical qualified V2 verifier is reused internally. Original preflight/source evidence and all eight earlier receipts are preserved. The publisher freshly compares the current source/index/tag/parents/closure/Git-store snapshot with the original audited preflight. It does not ask the operator to export that preflight again. A read-sharing lock protects each existing MinGit file and, during execution, existing source Git-store files; these handles do not prove OS-wide namespace or concurrency isolation.

Before confirmation there are no publisher-requested remote operations, credential-store queries, source/ref writes, or new attempt/staging writes. Runtime loading is not an OS sandbox guarantee.

## New explicit human confirmation and credentials

Only `PUBLISH-EXACT-V0601-58B9430-C07409C`, entered after an exact matching local preview, advances this operation. The confirmation has a bounded monotonic preview lifetime. All previous bootstrap/import/preflight tokens remain non-authorizing for publication.

The credential strategy is a GitHub personal access token entered in the local console with input echo disabled, only after confirmation. No token is requested in chat. Prefer a short-lived fine-grained token restricted to Matawaka/workbench, with Contents read/write and Workflows read/write for this source transition. No administration, repository deletion, account-wide or other-repository access is requested. The tool validates token syntax, not remote token scope; GitHub remains the authority for actual access control.

The token is converted to a Basic header in memory and added only to the exact push child's runtime configuration environment, scoped to the fixed HTTPS endpoint. It is not put in command-line arguments, disk config, local receipts or displayed transport output. Stored credential helpers, user configuration, proxies, redirects and automatic HTTP 429 retries are excluded from that child. Certificate verification stays enabled and uses the pinned MinGit CA bundle. Public before/after ref reads carry no token.

This is not encrypted-memory, zeroization, anti-debugger or administrator-isolation assurance: privileged processes can inspect process memory/environment. Managed string zeroization is not claimed. Operators should revoke the short-lived token after the attempt. A successful dummy-credential loopback test is not proof of live GitHub authorization or reachability.

## Effects after confirmation

A create-new WriteThrough consumed-attempt receipt is flushed and verified before any network operation. It is never reset or overwritten. The source `.git/objects` bytes are copied into a NEW bounded external transport directory `K:\Matawaka\Tools\WorkbenchV0601FixedPublicationV1`; source repository config, hooks and refs are not copied. This local copy may include preexisting unreferenced Git objects, but the push has exactly two fixed object-ID refspecs, no extra refs or tag following. Private managed applications outside the repository are not read.

The isolated transport has its own minimal bare metadata, empty user home and fixed pre-push guard. There is no clone/fetch, source object-store write, source ref change, commit/tag creation, installation, app launch, model invocation or Update/Accept. The isolated directory and new receipts are retained rather than automatically deleted after an uncertain attempt.

At most two explicit `ls-remote` commands and ONE ordinary `git push --atomic --porcelain` are allowed. This counts Git command invocations, not HTTP packets: the smart-Git protocol necessarily has an advertisement and RPC exchange. No force flags, API-ref bypass, retry, tag-only fallback, arbitrary destination or arbitrary refspec is supported.

The pre-push guard validates exact local/new OIDs and exact server-advertised old main in the actual push connection, as well as zero old tag OID and exactly two rows. Git's receive-side ref update checks then handle races after advertisement. Atomicity is not substituted for the exact-old-main guard. No source-repository configuration, hooks, rewriting, push options or credentials are inherited by the transport.

## Result semantics

Success is `EXACT_V0601_TWO_REF_PUBLICATION_VERIFIED` only after: exit 0 from the one push, a verified guard marker, exact public post-readback (including annotated peel and historical-tag absence), and unchanged source/evidence snapshot. Independent assistant-side GitHub readback is still needed for external closure of #102/#804.

New original files under `artifacts/publication-v0601`:

- `attempt-publish-fixed-v1-58b9430fc544998a8e40ba00b6757cc630ba9081.json` — consumed once, not success by itself.
- `publication-fixed-v1-58b9430fc544998a8e40ba00b6757cc630ba9081.json` — success only after the full checks.
- `outcome-publish-fixed-v1-58b9430fc544998a8e40ba00b6757cc630ba9081.json` — available failure/uncertainty evidence instead of a success assertion.

Once a push process may have started, failure must not be described as proof of no remote effect. Lost reply or failed readback preserves uncertainty and forbids automatic replay even if the target refs are later observed. No rollback/reset/cleanup authority is inferred. Existing attempts/results/staging refuse reuse.

## Verification scope

Local fixture tests cover exact two-ref success, byte preservation, preflight/source drift, existing/same/conflicting tags, ancestor and post-advertisement races, unsupported atomic, source config/hook independence, cancellation/expiry, replay, argument closure and secret omission. The separate loopback HTTPS suite exercises actual native Git smart HTTP, per-process CA trust, dummy authentication, redirect/429 denial, failed TLS and remote-success/lost-response cases. Test endpoint and CA overrides are compiled under QUALIFICATION only, not exposed by the production executable.

No production push or live credential test is performed by CI. The accepted application and historical receipts are not rewritten. Do not merge this tooling branch into the bound public predecessor before the exact publication.
