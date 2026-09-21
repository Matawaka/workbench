import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { execFileSync } from "node:child_process";
import { sha256Json } from "../src/canonical-json.js";
import { JevHttpProvider, runLiveQualification } from "../src/index.js";

const here = path.dirname(fileURLToPath(import.meta.url));
const projectRoot = path.resolve(here, "..");
const fixtures = JSON.parse(await fs.readFile(path.join(projectRoot, "fixtures/live-qualification.json"), "utf8"));
const mode = process.argv.includes("--deep") ? "deep" : process.argv.includes("--smoke") ? "smoke" : "standard";
const selectedFixtures = mode === "smoke" ? fixtures.slice(0, 1) : fixtures;
const packageJson = JSON.parse(await fs.readFile(path.join(projectRoot, "package.json"), "utf8"));
let harnessRevision = null;
try {
  harnessRevision = execFileSync("git", ["rev-parse", "HEAD"], { cwd: projectRoot, encoding: "utf8" }).trim();
} catch {
  // Standalone ZIP snapshots may not contain .git metadata.
}
const qualificationMetadata = {
  harnessVersion: packageJson.version,
  harnessRevision,
  fixtureCatalogDigest: sha256Json(fixtures),
  selectedFixturesDigest: sha256Json(selectedFixtures),
  selectedFixtureIds: selectedFixtures.map((fixture) => fixture.id),
};
const repeats = mode === "deep" ? 5 : mode === "smoke" ? 2 : 3;
const maxPermutations = mode === "deep" ? 24 : mode === "smoke" ? 2 : 6;

const provider = new JevHttpProvider();
const report = await runLiveQualification({
  provider,
  fixtures: selectedFixtures,
  repeats,
  maxPermutations,
  qualificationMetadata,
});

await fs.mkdir(path.join(projectRoot, "artifacts"), { recursive: true });
const stamp = new Date().toISOString().replace(/[:.]/g, "-");
const file = path.join(projectRoot, "artifacts", `jev-${mode}-qualification-${stamp}.json`);
await fs.writeFile(file, `${JSON.stringify(report, null, 2)}\n`);

console.log(JSON.stringify({
  file,
  schema: report.schema,
  requestedModel: report.requestedModel,
  observedModels: report.observedModels,
  availableModels: report.modelInventory.models.map((model) => model.name),
  qualificationMetadata: report.qualificationMetadata,
  summary: report.summary,
}, null, 2));
