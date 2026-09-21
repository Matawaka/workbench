import test from "node:test";
import assert from "node:assert/strict";
import {
  createSecondaryLabelV2,
  validateHumanLabelV2,
  migratePrimaryV1ToDiagnosticV2,
  comparePrimaryV1ToReceipt,
} from "../src/label-v2.js";

const packet = {
  caseId: "case-0001",
  modelOutputVisible: false,
  reviewPacketDigest: "sha256:packet",
  questionUse: {
    goalAlignment: "SHADOW_OBSERVATION",
    operationalSpecificity: "RESEARCH_ONLY",
    causesExternalMutation: "SHADOW_OBSERVATION",
    hasReliableRollback: "SHADOW_OBSERVATION",
  },
  questions: {
    goalAlignment: { type: "noul" },
    operationalSpecificity: { type: "noul" },
    causesExternalMutation: { type: "noul" },
    hasReliableRollback: { type: "noul" },
  },
};

test("v0.2 secondary template separates asserted, undetermined, and not-applicable semantics", () => {
  const label = createSecondaryLabelV2(packet);
  assert.equal(label.schema, "matawaka.jev-shadow-human-label/v0.2");
  assert.match(label.reviewPacketDigest, /^sha256:[0-9a-f]{64}$/);
  assert.equal(label.noulTruth.goalAlignment.disposition, "UNDETERMINED");
  assert.equal(label.noulTruth.goalAlignment.truth, null);
  assert.equal(label.noulTruth.hasReliableRollback.conditionalOn, "causesExternalMutation");
  assert.equal(label.noulTruth.operationalSpecificity.researchOnly, true);
});

test("NOT_APPLICABLE rollback is valid only when mutation is asserted false", () => {
  const label = createSecondaryLabelV2(packet);
  label.noulTruth.causesExternalMutation = {
    ...label.noulTruth.causesExternalMutation,
    disposition: "ASSERTED",
    truth: false,
    reviewerConfidence: 1,
  };
  label.noulTruth.hasReliableRollback = {
    ...label.noulTruth.hasReliableRollback,
    disposition: "NOT_APPLICABLE",
    truth: null,
    reviewerConfidence: null,
  };
  assert.equal(validateHumanLabelV2(label), true);

  const bad = structuredClone(label);
  bad.noulTruth.causesExternalMutation.truth = true;
  assert.throws(() => validateHumanLabelV2(bad), /NOT_APPLICABLE/);
});

test("UNDETERMINED cannot carry a forced boolean or confidence", () => {
  const label = createSecondaryLabelV2(packet);
  label.noulTruth.goalAlignment.truth = false;
  assert.throws(() => validateHumanLabelV2(label), /requires truth=null/);
});

test("legacy primary migration is diagnostic only and does not mutate source", () => {
  const primary = {
    schema: "matawaka.jev-shadow-human-label/v0.1",
    caseId: "case-0001",
    reviewPacketDigest: "sha256:packet",
    labelKind: "HUMAN_PRIMARY",
    modelOutputVisibleToLabeler: false,
    independentOfModelOutput: true,
    reviewerPseudonym: "operator-01",
    labeledAt: "2026-09-21T09:18:09Z",
    authorityEffect: "NONE",
    noulTruth: {
      goalAlignment: { truth: false, reviewerConfidence: 0.7, notes: "", researchOnly: false },
      hasReliableRollback: { truth: true, reviewerConfidence: 0, notes: "", researchOnly: false },
    },
  };
  const before = JSON.stringify(primary);
  const migrated = migratePrimaryV1ToDiagnosticV2(primary);
  assert.equal(JSON.stringify(primary), before);
  assert.equal(migrated.noulTruth.hasReliableRollback.disposition, "ASSERTED");
  assert.equal(migrated.admissibleForScoring, false);
  assert.equal(migrated.sourceLabelKind, "HUMAN_PRIMARY");
  assert.match(migrated.migrationNote, /does not replace/);
});

test("primary-vs-Jev comparison is explicitly not calibration", () => {
  const primary = {
    schema: "matawaka.jev-shadow-human-label/v0.1",
    caseId: "case-0001",
    reviewPacketDigest: "sha256:packet",
    labelKind: "HUMAN_PRIMARY",
    modelOutputVisibleToLabeler: false,
    independentOfModelOutput: true,
    noulTruth: {
      goalAlignment: { truth: false, reviewerConfidence: 0.7, researchOnly: false },
      operationalSpecificity: { truth: false, reviewerConfidence: 0.6, researchOnly: true },
    },
  };
  const receipt = {
    schema: "matawaka.jev-shadow-send-receipt/v0.3",
    judgments: {
      goalAlignment: { primitive: "noul", answer: { noul: 0.97 } },
      operationalSpecificity: { primitive: "noul", answer: { noul: 0.69 } },
    },
  };
  const report = comparePrimaryV1ToReceipt(primary, receipt);
  assert.equal(report.status, "PRIMARY_DIAGNOSTIC_ONLY_NOT_CALIBRATION");
  assert.equal(report.summary.observations, 2);
  assert.equal(report.comparisons[1].researchOnly, true);
  assert.equal(report.comparisons.every((x) => x.calibrationEligible === false), true);
});
