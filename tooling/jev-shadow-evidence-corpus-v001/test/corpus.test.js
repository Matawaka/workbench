import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import {
  createBlindedReviewPacket,
  createCorpusCase,
  createHumanLabelTemplate,
  scoreCorpus,
  sha256Json,
  validateHumanLabel,
} from "../src/corpus.js";

async function read(name) {
  const text = await fs.readFile(new URL(`../evidence/synthetic-control-001/${name}`, import.meta.url), "utf8");
  return JSON.parse(text.replace(/^\uFEFF/, ""));
}

async function base() {
  const candidate = await read("candidate.json");
  const receipt = await read("receipt.json");
  const caseId = "case-001";
  const packet = createBlindedReviewPacket({ candidate, caseId, sampleClass: "SYNTHETIC_CONTROL" });
  const c = createCorpusCase({ candidate, receipt, caseId, sampleClass: "SYNTHETIC_CONTROL" });
  return { candidate, receipt, packet, c };
}

test("blinded review packet contains sanitized context/questions but no model outputs", async () => {
  const { candidate, packet } = await base();
  assert.equal(packet.modelOutputVisible, false);
  assert.deepEqual(packet.context, candidate.providerRequest.state);
  assert.equal(JSON.stringify(packet).includes("providerRequestId"), false);
  assert.equal(JSON.stringify(packet).includes('"noul":0.1'), false);
  assert.equal(packet.questionUse.operationalSpecificity, "RESEARCH_ONLY");
});

test("corpus case binds exact candidate/request and preserves no-authority boundary", async () => {
  const { candidate, receipt, c } = await base();
  assert.equal(c.binding.candidateDigest, sha256Json(candidate));
  assert.equal(c.binding.requestDigest, candidate.requestDigest);
  assert.equal(c.provider.providerRequestId, receipt.providerRequestId);
  assert.equal(c.authorityBoundary.workbenchReadbackAuthorized, false);
  assert.equal(c.authorityBoundary.authorityCreated, false);
  const tampered = structuredClone(receipt);
  tampered.candidateRequestDigest = "sha256:bad";
  assert.throws(() => createCorpusCase({ candidate, receipt: tampered, caseId: "x", sampleClass: "SYNTHETIC_CONTROL" }), /candidateRequestDigest mismatch/);
});

test("human label template is blinded and supports nullable truth before review", async () => {
  const { packet, c } = await base();
  const label = createHumanLabelTemplate(packet);
  assert.equal(label.modelOutputVisibleToLabeler, false);
  assert.equal(label.independentOfModelOutput, true);
  assert.equal(label.noulTruth.goalAlignment.truth, null);
  assert.equal(label.noulTruth.operationalSpecificity.researchOnly, true);
  assert.equal(validateHumanLabel({ packet, label, caseRecord: c }), true);
});

test("synthetic oracle scores separately and never contributes to deployment calibration", async () => {
  const { packet, c } = await base();
  const label = createHumanLabelTemplate(packet, { labelKind: "SYNTHETIC_ORACLE" });
  const truths = { goalAlignment: false, operationalSpecificity: true, scopeExpansion: true, causesExternalMutation: true, hasReliableRollback: false, externalCommunication: false };
  for (const [id, truth] of Object.entries(truths)) label.noulTruth[id].truth = truth;
  const score = scoreCorpus({ cases: [c], packets: [packet], labels: [label] });
  assert.equal(score.syntheticControl.independentCases, 1);
  assert.equal(score.syntheticControl.observations, 6);
  assert.equal(score.deploymentCalibration.observations, 0);
  assert.equal(score.syntheticControl.policyEligibleSignals.observations, undefined);
  assert.equal(score.syntheticControl.policyEligibleSignals.n, 5);
  assert.equal(score.syntheticControl.bySignal.operationalSpecificity.n, 1);
  assert.equal(score.syntheticControl.bySignal.operationalSpecificity.directionalMatches, 0);
});

test("deployment calibration requires sanitized real shadow plus blinded adjudicated label", async () => {
  const { candidate, receipt } = await base();
  const caseId = "real-001";
  const packet = createBlindedReviewPacket({ candidate, caseId, sampleClass: "SANITIZED_REAL_SHADOW" });
  const c = createCorpusCase({ candidate, receipt, caseId, sampleClass: "SANITIZED_REAL_SHADOW" });
  const primary = createHumanLabelTemplate(packet, { labelKind: "HUMAN_PRIMARY" });
  for (const entry of Object.values(primary.noulTruth)) entry.truth = true;
  let score = scoreCorpus({ cases: [c], packets: [packet], labels: [primary] });
  assert.equal(score.deploymentCalibration.observations, 0);
  assert.equal(score.pending[0].reason, "LABEL_NOT_ADMISSIBLE_FOR_SCORING_POPULATION");

  const adjudicated = structuredClone(primary);
  adjudicated.labelKind = "HUMAN_ADJUDICATED";
  score = scoreCorpus({ cases: [c], packets: [packet], labels: [adjudicated] });
  assert.equal(score.deploymentCalibration.independentCases, 1);
  assert.equal(score.deploymentCalibration.observations, 6);
  assert.equal(score.deploymentCalibration.allSignals.ece, null);
  assert.match(score.deploymentCalibration.allSignals.eceStatus, /INSUFFICIENT_INDEPENDENT_CASES/);
});

test("duplicate case IDs are rejected and visible-model labels are rejected", async () => {
  const { packet, c } = await base();
  const label = createHumanLabelTemplate(packet, { labelKind: "SYNTHETIC_ORACLE" });
  assert.throws(() => scoreCorpus({ cases: [c, c], packets: [packet], labels: [label] }), /Duplicate corpus caseId/);
  label.modelOutputVisibleToLabeler = true;
  assert.throws(() => validateHumanLabel({ packet, label, caseRecord: c }), /not independent\/blinded/);
});
