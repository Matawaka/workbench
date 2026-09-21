import fs from "node:fs/promises";
import path from "node:path";
import { comparePrimaryV1ToReceipt } from "../src/label-v2.js";

async function readJson(file) {
  return JSON.parse((await fs.readFile(file, "utf8")).replace(/^\uFEFF/, ""));
}

const [primaryPath, receiptPath, outputDir] = process.argv.slice(2);
if (!primaryPath || !receiptPath || !outputDir) {
  throw new Error("Usage: node examples/compare-primary-private.mjs <label.primary.json> <receipt.json> <private-output-dir>");
}

const [primary, receipt] = await Promise.all([
  readJson(primaryPath),
  readJson(receiptPath),
]);
await fs.mkdir(outputDir, { recursive: true });
const comparison = comparePrimaryV1ToReceipt(primary, receipt);
const output = path.join(outputDir, "comparison.primary-vs-jev.private.json");
await fs.writeFile(output, `${JSON.stringify(comparison, null, 2)}\n`, "utf8");

console.log(JSON.stringify({
  status: comparison.status,
  file: output,
  observations: comparison.summary.observations,
  policyEligibleObservations: comparison.summary.policyEligibleObservations,
  directionalMatches: comparison.summary.directionalMatches,
  directionalMisses: comparison.summary.directionalMisses,
  boundaryTies: comparison.summary.boundaryTies
}, null, 2));
