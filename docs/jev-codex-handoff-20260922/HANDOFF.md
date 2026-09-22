# Jev / Workbench: Codex handoff, 2026-09-22

## Start here

This is a continuation checkpoint, NOT production admission, a merge instruction, or a new human adjudication. Read `CODEX-PROMPT.md` next. The public repository contains code, synthetic controls, and public status notes. Real case material belongs exclusively in the separately supplied private/local evidence bundle.

Implementation baseline: `a6e389f1b2910e987f5749186152fb5d31f98eda` on `research/jev-shadow-label-semantics-v0.2`, Draft PR #117. The handoff branch is `handoff/jev-shadow-codex-2026-09-22`. It adds only these documents and a checkpoint-validation/archive workflow; it must not change implementation files. Do not assume `main` contains this stacked research chain.

As rechecked on 2026-09-22, PR #117 is open, Draft, not merged; its head is the baseline above. Its historical label-semantics CI run `35583894030` concluded success. Historical GREEN statements below refer only to their specific tests, commits and observations, not a complete security proof.

## The objective

Use TypeSafe Jev/System One as typed probabilistic evidence in Matawaka/Workbench. Keep semantic judgments distinct from identity, authority, policy, permissions, execution, and liability. The project has progressed from synthetic model qualification to a single real Workbench-derived observation and human-reference provenance work.

Hard boundary: `Probabilistic Judgment != Authorization`.

Never infer that booleans in an artifact enforce this boundary by themselves. Runtime separation, exact bindings and negative tests must substantiate it. A hash checks content identity; it does not independently authenticate authorship, creation time, blinding, or an external provider.

## Existing source map

- `tooling/jev-system-one-judgment-adapter/`: Node adapter, Choice/Score/Noul contracts, provider wrapper, repeat/permutation audits, proper scoring, v0.2 sanitized candidate builder. The package also contains explicitly live examples; do not run them for this handoff.
- `src/Matawaka.Workbench.App/JevShadowObservationExportV001.cs`: local observation export component, no production UI call site in the accepted research baseline.
- `integrations/jev-shadow/`: export boundary and JSON schema.
- `tooling/jev-shadow-one-shot-send-v003/`: explicit operator-activated one-shot send sidecar. Consumption precedes the provider attempt. Local create-new records are not a global, rollback-proof authorization ledger.
- `tooling/jev-shadow-evidence-corpus-v001/`: original corpus, blinded packet generation, private intake, synthetic/deployment separation and scoring.
- `tooling/jev-shadow-real-case-lab-v001/`: explicit `catalog.inspect` capture through the real CommandRouter with AgentEnabled=false and AllowGitFetch=false, candidate preparation, pre-send label helper.
- `tooling/jev-shadow-label-semantics-v002/`: separate v0.2 dispositions, secondary initialization and private post-model comparison. It is NOT integrated with the v0.1 corpus scorer.
- `.github/qualification/jev-shadow-v001/`: Windows export probe.
- `.github/workflows/workbench-jev-*.yml`: historical version-scoped CI gates. Preserve them; do not rewrite predecessor gates just to get GREEN.

## Research chain and historical reference points

| PR | Surface | Known reference head |
|---|---|---|
| #108 | alpha.3 live qualification | retrieve from PR when needed |
| #109 | alpha.4 / alpha.4.1 | aafae5bbfbdb8b5776d93e623b4c4fd1736d65a1 |
| #110 | alpha.4.2 oracle discipline | 7ab1e04d994be4b1ccf2c6501aaf594efedb724d |
| #111 | alpha.5 proper scoring | 9d17b6c583b7acbb1a4a0524e5acaa046abeb1c9 |
| #112 | local shadow export v0.1 | daefb4420f0165c3325e6a260d05b37a614ba77f |
| #113 | sanitized candidate v0.2 | 1de336da6a263a57cf465b1bc1a95345dc8286c7 |
| #114 | one-shot send v0.3 + frozen synthetic live receipt | 58d2b5ab005fd8cd075551d6ff74d964e33a2b43 |
| #115 | corpus v0.1 + private intake | 470c885cf4ef70b23a83b5b7c3fbbdc4b5c388b2 |
| #116 | real-case lab + pre-send blind label | bf5326c355578db3d3dc9eb46051e07973b07748 |
| #117 | label semantics v0.2 | a6e389f1b2910e987f5749186152fb5d31f98eda |

These are historical anchors. The new workflow exports current PR metadata separately; compare before acting. No automatic merging, rebasing, force pushing, retargeting, or closing this chain is requested.

## What has actually been observed

Alpha.3 standard: 36/39 old-style checks, with material distribution changes hidden by stable Choice argmax labels. Alpha.4 separated goal alignment from operational specificity and effect dimensions. Alpha.4.2 separated asserted oracles from observation-only judgments. Alpha.5 replaced Noul probability floors with explicit boolean truth and Brier/direction/tie diagnostics, adding opposite-polarity examples.

Alpha.5 standard, operator receipt dated 2026-09-21: 7 synthetic fixtures; 135 oracle evaluations / 131 matches; 105 Noul evaluations / 101 directional matches / 1 exact tie; mean Noul Brier 0.0340752380952381. All six Noul signals have both polarities in that small catalog. Choice maximum measured spread 0.01 and minimum winner margin 0.96. This is a small, repeated development set, not deployment calibration or held-out accuracy. `operationalSpecificity` remains RESEARCH_ONLY. Changes of fixture semantics mean old and new pass ratios are not directly comparable.

One synthetic live call was frozen under `tooling/jev-shadow-one-shot-send-v003/evidence/live/2026-09-21/`. One separate real Workbench catalog-inspection case was captured and sent using a private sanitized candidate and a separate one-shot lease. The first live call is a SYNTHETIC_CONTROL, not a real deployment case. The real case is a deliberately selected laboratory observation, not a representative sample of all production traffic.

## Current human-reference checkpoint

For the real case, PRIMARY was submitted before the recorded Jev response and its detached hash was supplied. SECONDARY and a separate explanatory addendum were supplied later. The operator explicitly clarified: the secondary judgments were made by a human with AI assistance.

Record `HUMAN_AI_ASSISTED` as declared origin in a separate provenance supplement. Keep the historical `HUMAN_SECONDARY` field and original bytes unchanged. The origin descriptor is not an implemented new label kind in the baseline. AI assistance is not disqualifying by itself, but does not establish an unassisted human reference, reviewer identity separation, or exposure history outside the assisting conversation.

The addendum declares that the assisting conversation contained the review packet and blank template, not the Jev predictions or primary comparison. It does not independently verify what the human saw outside that conversation. Its approximate timing comes from an interface file-creation indication, not a trusted timestamp. The frozen secondary retains labeledAt=null; never fill it retrospectively.

No completed HUMAN_ADJUDICATED reference exists. Current checkpoint: `SECONDARY_ORIGIN_DECLARED_HUMAN_AI_ASSISTED_ADJUDICATION_PENDING`. Reference/deployment-calibration admission remains HOLD. The next Codex session has access to post-model context and must not impersonate a blinded adjudicator or a new human reviewer.

## Corrections and known implementation gaps to carry forward

The following are based on inspection of baseline code. They are not fixes implemented by this handoff.

1. **Schema split:** corpus.js validates human labels only at v0.1; label-v2.js lives separately. Do not silently coerce v0.2 secondary/adjudicated labels into v0.1.
2. **Insufficient reference-admission evidence:** the legacy scorer selects HUMAN_ADJUDICATED by labelKind and declared flags. It does not establish a linked two-reviewer adjudication event or separate reviewer-origin/exposure provenance.
3. **ECE counting defect:** summarizeObservations checks observations.length against minEceCases, including the pooled aggregate. Five cases with six signals each can satisfy 30 observations. That is not 30 independent cases. Independence is also asserted by construction, while duplicate detection is only on caseId.
4. **Packet binding weakness:** createSecondaryLabelV2 accepts packet.reviewPacketDigest if supplied instead of always computing/rejecting discrepancies. The private comparison helper checks schema names but not a complete packet-candidate-receipt-primary chain.
5. **Incomplete submissions:** legacy validators tolerate missing completion metadata and do not consistently distinguish a template, declaration, completed review and admitted reference. Numeric and unknown-field validation need focused negative tests.
6. **Private intake lifecycle:** old intake/helper paths can overwrite existing packet/label/output files. Lexical outside-repository checks are not a complete symlink/junction defence. Do not run legacy intake over a frozen case directory.
7. **Wording versus implementation:** legacy `policyEligibleSignals` means merely not researchOnly; it does NOT grant production-policy eligibility. Use non-normative reporting names in successors. Existing booleans about no authority or blinding are declarations, not independent proofs.

The next focused engineering step is a separate, offline **provenance-aware reference-admission and adjudication bridge** for corpus v0.1 + labels v0.2, using synthetic fixtures for tests. Default to diagnostic/pending on missing evidence. Do not broaden the provider-send surface. Broader sidecar/transport hardening, stronger canonicalization, global lease replay resistance and comprehensive security audit are deferred; historical tests do not prove them.

## Environment and safe continuation

Operator paths already used: repository `K:\matawaka\workbench`; remote name `github-workbench` (not origin); private store `K:\matawaka-private-evidence\jev-shadow`. Node 24.19.0 and .NET SDK 10 were successfully used locally. Use `npm.cmd` in Windows PowerShell; write UTF-8 without BOM and tolerate one BOM on legacy JSON reads. Restore variables in every standalone PowerShell block. Do not replace actual paths with fake `K:\path\to` examples.

Codex Cloud must not assume it can access drive K:. Without authorized local evidence, finish code/tests on synthetic controls and emit an explicit missing-input inventory. Do not request that private originals be committed to the public repo. With local access, inspect only the named case/handoff directories; do not sweep unrelated files, secret stores or environment values. A local Codex client is not an offline model: do not ingest confidential originals into any external AI context without the operator's applicable authorization.

Baseline tests to run without a TypeSafe key:

- `npm test` in `tooling/jev-system-one-judgment-adapter` (historically 20)
- `npm test` in `tooling/jev-shadow-one-shot-send-v003` (historically 4)
- `npm test` in `tooling/jev-shadow-evidence-corpus-v001` (historically 7)
- `npm test` in `tooling/jev-shadow-label-semantics-v002` (historically 5)
- Windows/.NET 10: `dotnet build tooling/jev-shadow-real-case-lab-v001/Capture.csproj -c Release`

Report actual counts; never equate a suite's historical GREEN to current success. Do not run `qualify:smoke`, `qualify:live`, `qualify:deep`, `send:once`, or a new real capture in the next engineering step.

## Source/evidence retention

The handoff CI exports the exact source tree and a Git history bundle from its candidate commit, with per-file SHA-256, test logs and reference metadata. Those archives contain tracked public repository data only. Private source artifacts and earlier conversation results are packaged separately for the operator, with a manifest distinguishing original bytes, derived reports and transcript-only facts. Some originals are only on the operator's K: drive; a checksum or a reconstructed JSON is not a substitute for those bytes.

Keep source records append-only. Write successor diagnostics into a new directory. Never overwrite the frozen primary, secondary, addendum, candidate, receipt, or source log. End the next Codex step with a Draft PR, exact SHAs, test evidence, non-effects, and an explicit human/evidence stop condition rather than manufactured admission.
