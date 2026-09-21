import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { buildShadowExternalizationCandidate } from "../src/shadow/externalization-candidate.js";
import { sha256Json } from "../src/canonical-json.js";

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, "..");
const shadowPath = process.argv[2] ?? path.join(root, "fixtures", "shadow-observation.synthetic.json");
const disclosurePath = process.argv[3] ?? path.join(root, "fixtures", "shadow-disclosure.synthetic.json");

const shadowEnvelope = JSON.parse(await fs.readFile(shadowPath, "utf8"));
const disclosure = JSON.parse(await fs.readFile(disclosurePath, "utf8"));
if (disclosure.sourceShadowDigest === "PLACEHOLDER") disclosure.sourceShadowDigest = sha256Json(shadowEnvelope);

const candidate = buildShadowExternalizationCandidate({ shadowEnvelope, disclosure });
await fs.mkdir(path.join(root, "artifacts"), { recursive: true });
const stamp = new Date().toISOString().replace(/[:.]/g, "-");
const out = path.join(root, "artifacts", `jev-shadow-externalization-candidate-${stamp}.json`);
await fs.writeFile(out, `${JSON.stringify(candidate, null, 2)}\n`, "utf8");
console.log(JSON.stringify({
  file: out,
  schema: candidate.schema,
  sourceShadowDigest: candidate.sourceShadowDigest,
  disclosureDigest: candidate.disclosureDigest,
  requestDigest: candidate.requestDigest,
  providerInvocationAuthorized: candidate.providerInvocationAuthorized,
  networkSendAuthorized: candidate.networkSendAuthorized,
  humanReviewRequired: candidate.humanReviewRequired,
}, null, 2));
