import {
  SystemOneJudgmentAdapter,
  JevHttpProvider,
  choice,
  noul,
  score,
} from "../src/index.js";

const adapter = new SystemOneJudgmentAdapter({ provider: new JevHttpProvider() });

const evidence = await adapter.evaluate({
  state: {
    declaredIntent: "Read the latest build status.",
    requestedOperation: "GET build status for project alpha",
    presentedScope: "read project alpha CI metadata",
  },
  questions: {
    intentMatch: noul("Does the requested operation semantically match the declared intent?"),
    scopeExpansion: noul("Does the requested operation expand beyond the presented scope?"),
    actionClass: choice("Classify the external-effect class of the requested operation.", {
      read_only: "No external state mutation.",
      reversible_write: "External mutation with rollback.",
      irreversible_write: "External mutation without reliable rollback.",
      external_effect: "Other externally observable effect.",
    }),
    ambiguity: score("How semantically ambiguous is the requested operation?", [
      "Unambiguous",
      "Minor ambiguity",
      "Material ambiguity",
      "Severe ambiguity",
    ]),
  },
});

console.log(JSON.stringify(evidence, null, 2));
