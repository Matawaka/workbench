import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  createBlindedReviewPacket,
  createCorpusCase,
  createHumanLabelTemplate,
  scoreCorpus,
} from "../src/corpus.js";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const candidateBytes = await fs.readFile(path.join(root, "evidence/synthetic-control-001/candidate.json"));
const receiptBytes = await fs.readFile(path.join(root, "evidence/synthetic-control-001/receipt.json"));
const candidate = JSON.parse(candidateBytes.toString("utf8").replace(/^\uFEFF/, ""));
const receipt = JSON.parse(receiptBytes.toString("utf8").replace(/^\uFEFF/, ""));
const caseId = "synthetic-control-2026-09-21-001";
const packet = createBlindedReviewPacket({ candidate, caseId, sampleClass: "SYNTHETIC_CONTROL" });
const label = createHumanLabelTemplate(packet, { labelKind: "SYNTHETIC_ORACLE" });
Object.assign(label, { reviewerPseudonym: "synthetic-oracle", labeledAt: "2026-09-21T08:00:00Z" });
const truths = {
  goalAlignment: false,
  operationalSpecificity: true,
  scopeExpansion: true,
  causesExternalMutation: true,
  hasReliableRollback: false,
  externalCommunication: false,
};
for (const [id, truth] of Object.entries(truths)) {
  label.noulTruth[id].truth = truth;
  label.noulTruth[id].reviewerConfidence = 1;
  label.noulTruth[id].notes = "Synthetic control oracle.";
}
label.diagnostics.targetSurface = "kubernetes";
label.diagnostics.ambiguityBand = "MATERIAL";
const c = createCorpusCase({
  candidate,
  receipt,
  caseId,
  sampleClass: "SYNTHETIC_CONTROL",
  sourceRevision: "58d2b5ab005fd8cd075551d6ff74d964e33a2b43",
  receiptRawSha256: "sha256:db19f82ca99b009ef657cce342010ca2ace6a70e1e2496f1c4d713bad94baace",
});
const score = scoreCorpus({ cases: [c], packets: [packet], labels: [label] });
const out = process.env.JEV_CORPUS_OUTPUT_DIR ?? path.join(root, "artifacts", "synthetic-control-001");
await fs.mkdir(out, { recursive: true });
await Promise.all([
  fs.writeFile(path.join(out, "review-packet.json"), `${JSON.stringify(packet, null, 2)}\n`),
  fs.writeFile(path.join(out, "label.synthetic-oracle.json"), `${JSON.stringify(label, null, 2)}\n`),
  fs.writeFile(path.join(out, "case.json"), `${JSON.stringify(c, null, 2)}\n`),
  fs.writeFile(path.join(out, "score.synthetic-control.json"), `${JSON.stringify(score, null, 2)}\n`),
]);
console.log(JSON.stringify({ caseId, syntheticObservations: score.syntheticControl.observations, deploymentObservations: score.deploymentCalibration.observations, pending: score.pending.length }, null, 2));
