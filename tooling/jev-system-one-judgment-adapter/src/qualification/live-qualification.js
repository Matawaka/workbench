import { SystemOneJudgmentAdapter } from "../adapter.js";
import { auditChoicePermutationStability } from "../audits/permutation-audit.js";
import { auditRepeatStability } from "../audits/repeat-audit.js";
import { evaluateExpectations } from "./expectations.js";

export async function runLiveQualification({
  provider,
  fixtures,
  repeats = 5,
  maxPermutations = 24,
  clock = () => new Date(),
}) {
  if (!provider || typeof provider.evaluate !== "function") throw new TypeError("provider is required.");
  if (!Array.isArray(fixtures) || fixtures.length === 0) throw new Error("fixtures must be a non-empty array.");

  const adapter = new SystemOneJudgmentAdapter({ provider, clock });
  const startedAt = clock().toISOString();
  const modelInventory = typeof provider.listModels === "function"
    ? await provider.listModels()
    : { models: [], requestId: null };

  const cases = [];
  for (const fixture of fixtures) {
    const repeat = await auditRepeatStability({
      adapter,
      state: fixture.state,
      questions: fixture.questions,
      repeats,
      correlationPrefix: `qualification:${fixture.id}`,
    });

    const expectationRuns = repeat.evidences.map((evidence, index) => ({
      run: index + 1,
      ...evaluateExpectations(evidence, fixture.expectations),
    }));
    const expectations = {
      runs: expectationRuns,
      total: expectationRuns.reduce((sum, run) => sum + run.total, 0),
      passed: expectationRuns.reduce((sum, run) => sum + run.passed, 0),
      allRunsPass: expectationRuns.every((run) => run.allPass),
    };
    const permutations = {};
    for (const [questionId, question] of Object.entries(fixture.questions)) {
      if (question.type !== "choice") continue;
      permutations[questionId] = await auditChoicePermutationStability({
        adapter,
        state: fixture.state,
        questionId,
        question,
        maxPermutations,
      });
    }

    cases.push({
      id: fixture.id,
      purpose: fixture.purpose,
      repeat,
      expectations,
      permutations,
    });
  }

  const observedModels = [...new Set(cases.flatMap((item) => item.repeat.observedModels))];
  const completedAt = clock().toISOString();
  return {
    schema: "matawaka.jev-live-qualification/v0.1",
    normativeEffect: "NONE",
    authorityIssuance: "OUT_OF_SCOPE",
    principle: "PROBABILISTIC_JUDGMENT_IS_NOT_AUTHORIZATION",
    provider: provider.name ?? "unknown",
    requestedModel: provider.model ?? null,
    modelInventory,
    observedModels,
    startedAt,
    completedAt,
    settings: { repeats, maxPermutations },
    summary: {
      cases: cases.length,
      expectationChecks: cases.reduce((sum, item) => sum + item.expectations.total, 0),
      expectationPasses: cases.reduce((sum, item) => sum + item.expectations.passed, 0),
      repeatUnstableChoices: cases.flatMap((item) => Object.values(item.repeat.summaries))
        .filter((summary) => summary.primitive === "choice" && !summary.stableChoice).length,
      permutationUnstableChoices: cases.flatMap((item) => Object.values(item.permutations))
        .filter((audit) => !audit.stableChoice).length,
    },
    cases,
  };
}
