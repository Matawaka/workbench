# Offline reference-admission bridge v0.2

This successor reads corpus v0.1 and human labels v0.1/v0.2 without rewriting or migrating their bytes. It has no provider, lease activation, capture, authority, or production readback path. `Probabilistic Judgment != Authorization`.

## Commands

Run from the repository root with Node >=22; Windows uses `npm.cmd`. Every output path must be absolute, outside all Git repositories, have an existing parent controlled exclusively by the operator, and name a **new** directory. Repeated invocation with the same output directory fails, even if it is empty. The CLI never overwrites inputs.

```powershell
npm.cmd --prefix tooling/jev-shadow-reference-admission-v002 test
node tooling/jev-shadow-reference-admission-v002/src/cli.js preflight --manifest K:\matawaka-private-evidence\jev-shadow\reference-inputs.json --out K:\matawaka-private-evidence\jev-shadow\preflight-new
node tooling/jev-shadow-reference-admission-v002/src/cli.js prepare-human-only-adjudication --manifest K:\matawaka-private-evidence\jev-shadow\human-inputs.json --out K:\matawaka-private-evidence\jev-shadow\human-adjudication-new
node tooling/jev-shadow-reference-admission-v002/src/cli.js diagnostic-score --manifest K:\matawaka-private-evidence\jev-shadow\reference-inputs.json --out K:\matawaka-private-evidence\jev-shadow\diagnostic-new
```

The paths above are intended locations for operator-created manifests, not assertions that these files already exist. `diagnostic-score` accepts repeated `--manifest` flags for multiple observations. It reports both primary and secondary separately; it never chooses the label that agrees with Jev. Exit codes: 0 = output written (may still be HOLD), 1 = validation/path rejection, 2 = preflight `PRIVATE_INPUTS_UNAVAILABLE`. An unavailable-input preflight lists missing originals and remaining evidence checks; scoring refuses incomplete chains.

`prepare-human-only-adjudication` accepts a distinct manifest schema containing **only** packet, primary, secondary. Receipt/candidate/corpus/provenance inputs and receipt flags are rejected. It emits source context, questions, structured human answers, explicit interpretations, and an entirely unfilled `DRAFT_NOT_A_LABEL`. Freeform label notes/diagnostics and original reviewer instructions are excluded because they could carry model comparisons. No `HUMAN_ADJUDICATED` label is generated. The human must still inspect source prose for model leakage; structural filtering cannot authenticate blinding or safely redact arbitrary adversarial prose.

`preflight` and `diagnostic-score` produce private post-model artifacts; never send their outputs to an intended blinded adjudicator. Stdout contains only status, output location and output-manifest hash. Stderr avoids original values/paths. Do not publish these outputs in GitHub or CI.

## File-backed input contracts

Executable schemas are strict validators in `src/bridge.js` and `src/private-io.js`. Unsupported schema versions, extra fields in contract objects, extra/missing signals and malformed probabilities fail explicitly. Context and evidence prose remain opaque. The fixture factory in `test/fixtures.js` is a complete executable example using synthetic records; its signoffs are explicitly fabricated **test data**, never actual human evidence.

A reference manifest has exactly this shape:

```json
{
  "schema": "matawaka.jev-shadow-reference-inputs/v0.2",
  "artifacts": {
    "packet": { "path": "cases/case-0001/review-packet.json", "rawSha256": "sha256:<actual 64 lowercase hex digits>" }
  }
}
```

Each present artifact needs its actual byte hash. Paths resolve relative to the manifest, or may be absolute. Do not put fabricated hashes against absent originals: omit the role and inspect the resulting pending inventory. All referenced files are read and hashed; only the named paths are inspected. One leading UTF-8 BOM is tolerated during JSON parsing and remains included in the raw hash. No source bytes are reserialized into the private output directory.

| Artifact ID | Schema/content and binding |
| --- | --- |
| `packet` | Frozen blinded-review-packet/v0.1. Body digest recomputed, optional self-digest checked, complete body regenerated from candidate and compared. |
| `candidate` | Frozen externalization-candidate/v0.2, including actual provider request and request digest. |
| `request` | Original JSON provider request `{model,state,questions}`. Must equal the embedded candidate request. A newly extracted request is derived evidence, not a missing original replacement. |
| `receipt` | Frozen send-receipt/v0.3. Candidate/request/adapter request, observation boundary, consumption bindings and every judgment validated. Lease booleans remain recorded declarations; no lease file is read or activated. |
| `caseRecord` | Original corpus-case/v0.1, compared in full with a recomputation from candidate/receipt, including receipt **raw** hash. No corpus record is silently created for missing input. |
| `primary`, `secondary` | Frozen human-label/v0.1 or v0.2 in their historical roles. PRIMARY v0.1 and SECONDARY v0.1/v0.2 supported. |
| `primaryProvenance`, `secondaryProvenance`, `adjudicatedProvenance` | Separate review-provenance/v0.2 supplements bound to exact label raw bytes, case and role. |
| `source` | Original underlying source bytes, opaque to this package. Identity is raw SHA-256. |
| `sourceGrouping` | source-grouping/v0.2: `caseId`, `candidateDigest`, `sourceRawSha256`, `groupId`, `independenceStatus` (`DECLARED_SOURCE_GROUP` / `UNKNOWN`), `evidenceRefs`. |
| `policy` | reference-policy/v0.2: `policyId`, `status` (`PREDECLARED` / `UNKNOWN`), `allowedOrigins`, `priorToReviewEvidenceRefs`. Explicitly permitting assisted human origin is possible; MODEL_ONLY/UNKNOWN cannot be reference origins. |
| `adjudicated` | Externally supplied frozen HUMAN_ADJUDICATED label, validated like other labels. The CLI cannot generate one. |
| `adjudication` | adjudication-event/v0.2; exact bindings specified below. |
| `response` | Optional original normalized adapter provider response. If supplied, verify canonical digest, answers, observed model, request ID against receipt. Otherwise report response digest as declared only; never reconstruct it as an original. |
| `evidence_<id>` | Explicitly referenced original declaration/addendum/signoff bytes. Freeform sources support declarations but are not automatically authenticated. |

For human-only input use `matawaka.jev-shadow-human-inputs/v0.2` and exactly `packet`, `primary`, `secondary` references. Never pass a full reference manifest to that command.

## Provenance and admission

Every provenance supplement has `schema= matawaka.jev-shadow-review-provenance/v0.2`, `caseId`, `labelRawSha256`, `role` and these separate objects:

| Object | Required fields |
| --- | --- |
| `origin` | `classification`: HUMAN_UNASSISTED / HUMAN_AI_ASSISTED / MODEL_ONLY / UNKNOWN; `basis`: OPERATOR_DECLARATION / REVIEWER_DECLARATION / UNKNOWN; `evidenceRefs` |
| `aiAssistance` | `status`: USED / NOT_USED_DECLARED / UNKNOWN; `evidenceRefs` |
| `humanExposure` | `status`: BLINDED_DECLARED / EXPOSED / UNKNOWN / NOT_APPLICABLE; `evidenceRefs` |
| `assistingSessionExposure` | Same statuses, separate evidence. A blinded assisting conversation never establishes human exposure outside it. |
| `reviewer` | `id` (nullable), `separationStatus`: DECLARED_DISTINCT / UNKNOWN; `evidenceRefs` |
| `completion` | `status`: TEMPLATE / DECLARATION / COMPLETED; `kind`: ORIGINAL_REVIEW / MIGRATION / UNKNOWN; `signatureRef` (nullable); `evidenceRefs` |

Evidence references name `evidence_<id>` artifacts whose bytes have been checked against the manifest. `signatureRef` points to a `matawaka.jev-shadow-declared-signoff/v0.2` JSON containing `subjectRawSha256`, `signerId`, `status` (`COMPLETED` / `DRAFT`). For a review it must bind the frozen label and reviewer; for adjudication it must bind the complete event and adjudicator. This is a **declared signoff format, not a cryptographic signature or authenticated identity proof**. Preserve historical `labeledAt=null`; a separate supplement does not backdate it.

The event contains `schema= matawaka.jev-shadow-adjudication-event/v0.2`, `caseId`, `packetDigest`, `primaryRawSha256`, `secondaryRawSha256`, `primaryProvenanceRawSha256`, `secondaryProvenanceRawSha256`, `adjudicatedRawSha256`, `provenanceRawSha256` (adjudicator provenance), `policyRawSha256`, `adjudicatorId`, `exposure`, `status` (DRAFT / COMPLETED), `decisionBasis` (HUMAN_EVIDENCE_ONLY / POST_MODEL_DIAGNOSTIC), `evidenceRefs`, `signatureRef`. All supplied bindings must match. Three reviewer IDs must be declared distinct with source references; different strings alone do not satisfy the check.

Missing policy, incomplete review, missing signoff, unknown/exposed human, unknown assisting exposure when AI is used, migration, missing source grouping or missing adjudication keeps HOLD. A supplied complete event and allowed origins can yield only `DECLARED_RESEARCH_REFERENCE`, with assertion level `LINKED_DECLARATIONS_NOT_AUTHENTICATED_IDENTITY_CHRONOLOGY_OR_BLINDING`. No code result creates trusted human ground truth, deployment-calibration admission, permission or production thresholds. Policy predeclaration, authorship and actual reviewer independence require human evidence assessment.

case-0001 SECONDARY is HUMAN_AI_ASSISTED by the operator's explicit declaration. This changes no historical labelKind. Completed human adjudication does not exist. Its current gate remains **HOLD / ADJUDICATION_PENDING**. The engineering agent sees post-model context and cannot be its independent human reviewer.

## Diagnostic semantics and independence

v0.1 booleans are viewed explicitly as ASSERTED; null is reported as `V1_NULL_UNDETERMINED`. This is an interpretation in the report, never a new v0.2 label. v0.2 ASSERTED needs boolean truth and finite confidence in [0,1]. UNDETERMINED/NOT_APPLICABLE need null truth/confidence and are excluded with a reason, never mapped to false. N/A is currently defined only for hasReliableRollback and requires ASSERTED false causesExternalMutation. Migration markers are carried as diagnostic provenance and excluded from independent-review scoring.

ECE is computed separately per signal, provider, requested/observed model, exact question/use digest, role, origin, human/assisting exposure, completion, reference status and synthetic/real sample class. No pooled ECE exists. Source raw identity, declared source-group identity and identical candidate digests are joined transitively. Renaming a case or repeating a request does not create independent samples. Identical candidate digests conservatively collapse cases even if different source records exist.

Repeated forecasts within a source group are averaged with equal weight per group; the reported Brier/ECE therefore describes the group mean forecast. Conflicting truth within a group is explicitly excluded and disables that cohort's ECE. Unknown source grouping also disables ECE. The minimum of 30 counts distinct declared source groups **within each cohort and signal**, after exclusions. It is an exploratory default, not proof of adequate sampling. The `independentSample` v0.1 flag has no gating authority. Operational specificity is always RESEARCH_ONLY; all remaining signals are merely SHADOW_DIAGNOSTIC.

## Limits and verification

The CLI boundary reads actual files, verifies raw hashes, and refuses symlink/junction input or output ancestry, realpath aliases, existing outputs and Git repository destinations. Files use create-new writes. There is no portable, race-proof transaction against an attacker replacing filesystem ancestors between checks; parent directories must remain exclusively controlled. Partial outputs after an I/O failure are retained and cannot be overwritten. Hard links, OS-specific non-symlink reparse mechanisms and hostile filesystems are outside this bounded hardening claim.

Canonical digests use the predecessor's recursively sorted JSON object keys and array order. Raw hashes retain byte-level distinctions. Duplicate JSON object keys, Unicode canonicalization, trusted timestamps, authenticated signatures, global lease replay resistance, provider attestation and privacy classification of arbitrary source prose require separate work. Validated hashes cannot establish those properties. In-process library functions assume the records returned by `loadBundle` are not subsequently mutated; the supported security boundary is the file-backed CLI.

Tests use temporary synthetic directories. The two legacy regression tests intentionally demonstrate the historical bugs. The new workflow runs only offline suites on Linux and Windows, checks the exact handoff ancestor and restricts changed paths to this successor and its own workflow. Historical gates and source evidence remain unchanged.
