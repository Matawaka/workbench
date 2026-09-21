import fs from "node:fs/promises";
import path from "node:path";
import { createSecondaryLabelV2 } from "../src/label-v2.js";

async function readJson(file) {
  return JSON.parse((await fs.readFile(file, "utf8")).replace(/^\uFEFF/, ""));
}

const [packetPath, outputDir, reviewerPseudonym = ""] = process.argv.slice(2);
if (!packetPath || !outputDir) {
  throw new Error("Usage: node examples/init-secondary-v002.mjs <review-packet.json> <private-output-dir> [reviewer-pseudonym]");
}

const packet = await readJson(packetPath);
await fs.mkdir(outputDir, { recursive: true });
const secondary = createSecondaryLabelV2(packet, { reviewerPseudonym });
const output = path.join(outputDir, "label.secondary.v0.2.json");
await fs.writeFile(output, `${JSON.stringify(secondary, null, 2)}\n`, "utf8");

console.log(JSON.stringify({
  status: "BLINDED_SECONDARY_LABEL_CREATED",
  file: output,
  caseId: secondary.caseId,
  modelOutputVisibleToLabeler: secondary.modelOutputVisibleToLabeler,
  independentOfModelOutput: secondary.independentOfModelOutput,
  authorityEffect: secondary.authorityEffect
}, null, 2));
