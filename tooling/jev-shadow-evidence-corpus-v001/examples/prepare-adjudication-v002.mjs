import fs from "node:fs/promises";
import path from "node:path";
import {
  comparePrimaryV1ToReceipt,
  createSecondaryLabelV2,
} from "../src/label-v2.js";

async function readJson(file) {
  return JSON.parse((await fs.readFile(file, "utf8")).replace(/^\uFEFF/, ""));
}

const [packetPath, primaryPath, receiptPath, outputDir] = process.argv.slice(2);
if (!packetPath || !primaryPath || !receiptPath || !outputDir) {
  throw new Error("Usage: node examples/prepare-adjudication-v002.mjs <review-packet.json> <label.primary.json> <receipt.json> <private-output-dir>");
}

const [packet, primary, receipt] = await Promise.all([
  readJson(packetPath),
  readJson(primaryPath),
  readJson(receiptPath),
]);

await fs.mkdir(outputDir, { recursive: true });
const secondary = createSecondaryLabelV2(packet);
const comparison = comparePrimaryV1ToReceipt(primary, receipt);

const secondaryPath = path.join(outputDir, "label.secondary.v0.2.json");
const comparisonPath = path.join(outputDir, "comparison.primary-vs-jev.private.json");
await Promise.all([
  fs.writeFile(secondaryPath, `${JSON.stringify(secondary, null, 2)}\n`, "utf8"),
  fs.writeFile(comparisonPath, `${JSON.stringify(comparison, null, 2)}\n`, "utf8"),
]);

console.log(JSON.stringify({
  status: "SECONDARY_AND_DIAGNOSTIC_PREPARED",
  secondaryLabel: secondaryPath,
  privateComparison: comparisonPath,
  comparisonStatus: comparison.status,
  observations: comparison.summary.observations,
  directionalMatches: comparison.summary.directionalMatches,
  directionalMisses: comparison.summary.directionalMisses,
}, null, 2));
