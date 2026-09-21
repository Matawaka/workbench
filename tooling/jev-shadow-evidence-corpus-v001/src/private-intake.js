import fs from "node:fs/promises";
import path from "node:path";
import { createHash } from "node:crypto";
import {
  createBlindedReviewPacket,
  createCorpusCase,
  createHumanLabelTemplate,
} from "./corpus.js";

function sha256Bytes(bytes) {
  return `sha256:${createHash("sha256").update(bytes).digest("hex")}`;
}

function isInside(parent, child) {
  const rel = path.relative(parent, child);
  return rel === "" || (!rel.startsWith("..") && !path.isAbsolute(rel));
}

export async function writePrivateRealShadowBundle({
  candidateBytes,
  receiptBytes,
  outputDir,
  repoRoot,
  caseId,
  sourceRevision = null,
} = {}) {
  if (!Buffer.isBuffer(candidateBytes)) candidateBytes = Buffer.from(candidateBytes ?? "");
  if (!Buffer.isBuffer(receiptBytes)) receiptBytes = Buffer.from(receiptBytes ?? "");
  if (!outputDir || !repoRoot || !caseId) throw new TypeError("outputDir, repoRoot, and caseId are required.");

  const out = path.resolve(outputDir);
  const repo = path.resolve(repoRoot);
  if (isInside(repo, out)) throw new Error("Private real-shadow bundle must be written outside the public repository root.");

  const candidate = JSON.parse(candidateBytes.toString("utf8").replace(/^\uFEFF/, ""));
  const receipt = JSON.parse(receiptBytes.toString("utf8").replace(/^\uFEFF/, ""));
  const packet = createBlindedReviewPacket({ candidate, caseId, sampleClass: "SANITIZED_REAL_SHADOW" });
  const label = createHumanLabelTemplate(packet, { labelKind: "HUMAN_PRIMARY" });
  const caseRecord = createCorpusCase({
    candidate,
    receipt,
    caseId,
    sampleClass: "SANITIZED_REAL_SHADOW",
    sourceRevision,
    receiptRawSha256: sha256Bytes(receiptBytes),
  });

  await fs.mkdir(path.join(out, "source"), { recursive: true });
  const manifest = {
    schema: "matawaka.jev-shadow-private-intake-manifest/v0.1",
    caseId,
    sampleClass: "SANITIZED_REAL_SHADOW",
    publicRepositoryPublicationAuthorized: false,
    reviewerMayReceive: ["review-packet.json", "label.primary.json"],
    reviewerMustNotReceiveBeforeLabeling: ["source/receipt.json", "case.json"],
    candidateRawSha256: sha256Bytes(candidateBytes),
    receiptRawSha256: sha256Bytes(receiptBytes),
    authorityEffect: "NONE",
  };

  await Promise.all([
    fs.writeFile(path.join(out, "source", "candidate.json"), candidateBytes),
    fs.writeFile(path.join(out, "source", "receipt.json"), receiptBytes),
    fs.writeFile(path.join(out, "review-packet.json"), `${JSON.stringify(packet, null, 2)}\n`, "utf8"),
    fs.writeFile(path.join(out, "label.primary.json"), `${JSON.stringify(label, null, 2)}\n`, "utf8"),
    fs.writeFile(path.join(out, "case.json"), `${JSON.stringify(caseRecord, null, 2)}\n`, "utf8"),
    fs.writeFile(path.join(out, "MANIFEST.json"), `${JSON.stringify(manifest, null, 2)}\n`, "utf8"),
  ]);

  return { outputDir: out, manifest, packet, label, caseRecord };
}
