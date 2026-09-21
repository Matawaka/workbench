import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  buildShadowExternalizationCandidate,
  createDisclosureTemplate,
} from "../jev-system-one-judgment-adapter/src/shadow/externalization-candidate.js";

function repoRoot() {
  return path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
}

function ensureOutsideRepo(target) {
  const repo = repoRoot();
  const out = path.resolve(target);
  const rel = path.relative(repo, out);
  if (rel === "" || (!rel.startsWith("..") && !path.isAbsolute(rel))) {
    throw new Error("Real shadow candidate/disclosure must be written outside the public repository.");
  }
  return out;
}

async function readJson(file) {
  return JSON.parse((await fs.readFile(file, "utf8")).replace(/^\uFEFF/, ""));
}

const [mode, shadowPath, third, fourth] = process.argv.slice(2);
if (!mode || !shadowPath) {
  throw new Error("Usage: node prepare-candidate.mjs init <shadow-envelope.json> <private-case-dir> | build <shadow-envelope.json> <disclosure.json> <private-case-dir>");
}

const shadow = await readJson(shadowPath);

if (mode === "init") {
  const out = ensureOutsideRepo(third);
  await fs.mkdir(out, { recursive: true });
  const disclosure = createDisclosureTemplate(shadow);
  disclosure.notes = [
    "Derived from one real Workbench catalog.inspect run.",
    "Replace all syntheticContext placeholders with sanitized descriptions before attestation."
  ];
  const file = path.join(out, "disclosure.json");
  await fs.writeFile(file, `${JSON.stringify(disclosure, null, 2)}\n`, "utf8");
  console.log(JSON.stringify({
    status: "DISCLOSURE_TEMPLATE_CREATED",
    file,
    operatorAttestedSanitized: disclosure.operatorAttestedSanitized
  }, null, 2));
} else if (mode === "build") {
  const disclosure = await readJson(third);
  const out = ensureOutsideRepo(fourth);
  await fs.mkdir(out, { recursive: true });
  const candidate = buildShadowExternalizationCandidate({
    shadowEnvelope: shadow,
    disclosure
  });
  const file = path.join(out, "candidate.json");
  await fs.writeFile(file, `${JSON.stringify(candidate, null, 2)}\n`, "utf8");
  console.log(JSON.stringify({
    status: "SANITIZED_REAL_SHADOW_CANDIDATE_CREATED",
    file,
    sourceShadowDigest: candidate.sourceShadowDigest,
    requestDigest: candidate.requestDigest,
    providerInvocationAuthorized: candidate.providerInvocationAuthorized,
    networkSendAuthorized: candidate.networkSendAuthorized,
    humanReviewRequired: candidate.humanReviewRequired
  }, null, 2));
} else {
  throw new Error(`Unsupported mode '${mode}'.`);
}
