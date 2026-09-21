import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { writePrivateRealShadowBundle } from "../src/private-intake.js";

const [candidatePath, receiptPath, outputDir, caseId] = process.argv.slice(2);
if (!candidatePath || !receiptPath || !outputDir || !caseId) {
  throw new Error("Usage: node examples/create-private-real-case.mjs <candidate.json> <receipt.json> <private-output-dir> <case-id>");
}
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
const result = await writePrivateRealShadowBundle({
  candidateBytes: await fs.readFile(candidatePath),
  receiptBytes: await fs.readFile(receiptPath),
  outputDir,
  repoRoot,
  caseId,
  sourceRevision: process.env.JEV_SOURCE_REVISION ?? null,
});
console.log(JSON.stringify({
  caseId,
  outputDir: result.outputDir,
  sampleClass: result.manifest.sampleClass,
  publicRepositoryPublicationAuthorized: result.manifest.publicRepositoryPublicationAuthorized,
  reviewerMayReceive: result.manifest.reviewerMayReceive,
  reviewerMustNotReceiveBeforeLabeling: result.manifest.reviewerMustNotReceiveBeforeLabeling,
}, null, 2));
