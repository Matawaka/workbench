import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  createBlindedReviewPacket,
  createHumanLabelTemplate,
} from "../jev-shadow-evidence-corpus-v001/src/corpus.js";

function repoRoot() {
  return path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
}

function ensureOutsideRepo(target) {
  const repo = repoRoot();
  const out = path.resolve(target);
  const rel = path.relative(repo, out);
  if (rel === "" || (!rel.startsWith("..") && !path.isAbsolute(rel))) {
    throw new Error("Blind real-shadow label packet must be written outside the public repository.");
  }
  return out;
}

async function readJson(file) {
  return JSON.parse((await fs.readFile(file, "utf8")).replace(/^\uFEFF/, ""));
}

const [candidatePath, outputDir, caseId] = process.argv.slice(2);
if (!candidatePath || !outputDir || !caseId) {
  throw new Error("Usage: node prepare-blind-label.mjs <candidate.json> <private-case-dir> <case-id>");
}

const candidate = await readJson(candidatePath);
const out = ensureOutsideRepo(outputDir);
await fs.mkdir(out, { recursive: true });

const packet = createBlindedReviewPacket({
  candidate,
  caseId,
  sampleClass: "SANITIZED_REAL_SHADOW",
});
const label = createHumanLabelTemplate(packet, { labelKind: "HUMAN_PRIMARY" });

const packetPath = path.join(out, "review-packet.json");
const labelPath = path.join(out, "label.primary.json");
await Promise.all([
  fs.writeFile(packetPath, `${JSON.stringify(packet, null, 2)}\n`, "utf8"),
  fs.writeFile(labelPath, `${JSON.stringify(label, null, 2)}\n`, "utf8"),
]);

console.log(JSON.stringify({
  status: "BLINDED_HUMAN_LABEL_PACKET_CREATED",
  caseId,
  reviewPacket: packetPath,
  labelTemplate: labelPath,
  modelOutputVisible: packet.modelOutputVisible,
  labelIndependentOfModelOutput: label.independentOfModelOutput,
  authorityEffect: packet.authorityEffect,
}, null, 2));
