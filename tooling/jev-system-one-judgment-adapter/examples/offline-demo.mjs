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
  requestedOperation: "Write deployment metadata for staging/service-a using the existing rollback-capable deployment mechanism.",
  target: "staging/service-a",
  presentedScope: "staging metadata only",
  evidence: ["review-record:rr-204", "lease:one-shot-42"],
};

const questions = {
  goalAlignment: noul("Does the requested operation pursue the same stated goal as the declared intent?"),
  operationalSpecificity: noul("Does the available state specify the requested action or action sequence precisely enough to classify its effects, without leaving materially different execution alternatives unresolved?"),
  scopeExpansion: noul("Does the requested operation expand beyond the presented scope?"),
  causesExternalMutation: noul("Would performing the requested operation change externally observable state?"),
  hasReliableRollback: noul("If the operation changes externally observable state, is there a defined reliable rollback that restores the prior state?"),
  externalCommunication: noul("Is sending information to an external person or organization a primary effect of the requested operation?"),
  targetSurface: choice("Classify the primary target surface named by the requested operation.", {
    ci: "Continuous-integration status, logs, jobs, or build artifacts.",
    kubernetes: "A Kubernetes cluster, namespace, workload, or resource.",
    workspace: "A project workspace containing files, drafts, logs, or generated artifacts.",
    email: "An email or external messaging surface.",
    document: "A documentation or file-reading surface.",
    other: "Another target surface not represented above.",
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
