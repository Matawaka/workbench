import fs from "node:fs/promises";
import {
  SystemOneJudgmentAdapter,
  FixtureProvider,
  choice,
  noul,
  score,
} from "../src/index.js";

const fixture = JSON.parse(await fs.readFile(new URL("../fixtures/authority-boundary.json", import.meta.url)));
const adapter = new SystemOneJudgmentAdapter({
  provider: new FixtureProvider({ fixture }),
});

const state = {
  declaredIntent: "Update the staging deployment label after review.",
  requestedOperation: "write deployment metadata",
  target: "staging/service-a",
  presentedScope: "staging metadata only",
  evidence: ["review-record:rr-204", "lease:one-shot-42"],
};

const questions = {
  intentMatch: noul("Does the requested operation semantically match the declared intent?"),
  scopeExpansion: noul("Does the requested operation expand beyond the presented scope?"),
  actionClass: choice("Classify the external-effect class of the requested operation.", {
    read_only: "No external state mutation.",
    reversible_write: "External mutation with a defined rollback path.",
    irreversible_write: "External mutation without a reliable rollback path.",
    external_effect: "Other externally observable effect.",
  }),
  ambiguity: score("How semantically ambiguous is the requested operation?", [
    "Unambiguous",
    "Minor ambiguity",
    "Material ambiguity",
    "Severe ambiguity",
  ]),
};

const evidence = await adapter.evaluate({
  state,
  questions,
  correlationId: "demo-jev-boundary-001",
});

console.log(JSON.stringify(evidence, null, 2));
