import { SystemOneJudgmentAdapter } from "../adapter.js";
import { auditChoicePermutationStability } from "../audits/permutation-audit.js";
import { auditRepeatStability } from "../audits/repeat-audit.js";
import { evaluateOracleRules } from "./expectations.js";

function polarityCoverage(fixtures) {
  const out = {};
  for (const fixture of fixtures) {
    for (const [questionId, rule] of Object.entries(fixture.oracles ?? {})) {
      if (fixture.questions[questionId]?.type !== "noul" || typeof rule.truth !== "boolean") continue;
      const entry = out[questionId] ?? { trueFixtures: [], falseFixtures: [] };
      (rule.truth ? entry.trueFixtures : entry.falseFixtures).push(fixture.id);
      out[questionId] = entry;
    }
  }
  return Object.fromEntries(Object.entries(out).map(([questionId, entry]) => [questionId, {
    ...entry,
    trueCount: entry.trueFixtures.length,
    falseCount: entry.falseFixtures.length,
    bothPolarities: entry.trueFixtures.length > 0 && entry.falseFixtures.length > 0,
  }]));
}

export async function runLiveQualification({
  provider,
  fixtures,
  repeats = 5,
  maxPermutations = 24,
  probabilityDriftThreshold = 0.10,
  winnerMarginThreshold = 0.10,
  qualificationMetadata = {},
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

    const oracleRuns = repeat.evidences.map((evidence, index) => ({
      run: index + 1,
      ...evaluateOracleRules(evidence, fixture.oracles),
    }));
    const oracleEvaluation = {
      runs: oracleRuns,
      total: oracleRuns.reduce((sum, run) => sum + run.total, 0),
      matched: oracleRuns.reduce((sum, run) => sum + run.matched, 0),
      allRunsMatch: oracleRuns.every((run) => run.allMatch),
      noulEvaluations: oracleRuns.reduce((sum, run) => sum + run.noulEvaluations, 0),
      noulDirectionalMatches: oracleRuns.reduce((sum, run) => sum + run.noulDirectionalMatches, 0),
      noulBoundaryTies: oracleRuns.reduce((sum, run) => sum + run.noulBoundaryTies, 0),
    };
    const noulItems = oracleRuns.flatMap((run) => run.evaluations.filter((item) => item.primitive === "noul" && item.reason === undefined));
    oracleEvaluation.noulBrierMean = noulItems.length
      ? noulItems.reduce((sum, item) => sum + item.brierLoss, 0) / noulItems.length
      : null;

    const permutations = {};
    for (const [questionId, question] of Object.entries(fixture.questions)) {
      if (question.type !== "choice") continue;
      permutations[questionId] = await auditChoicePermutationStability({
        adapter,
        state: fixture.state,
        questionId,
        question,
        maxPermutations,
        probabilityDriftThreshold,
        winnerMarginThreshold,
        correlationPrefix: `qualification:${fixture.id}:permutation`,
      });
    }

    const assertedQuestionIds = Object.keys(fixture.oracles ?? {});
    const observationOnlyQuestionIds = Object.keys(fixture.questions)
      .filter((questionId) => !assertedQuestionIds.includes(questionId));
    const oracle = {
      assertedQuestionIds,
      observationOnlyQuestionIds,
      assertedQuestions: assertedQuestionIds.length,
      observationOnlyQuestions: observationOnlyQuestionIds.length,
      totalQuestions: Object.keys(fixture.questions).length,
      coverage: Object.keys(fixture.questions).length
        ? assertedQuestionIds.length / Object.keys(fixture.questions).length
        : 0,
      notes: fixture.oracleNotes ?? {},
    };

    cases.push({
      id: fixture.id,
      purpose: fixture.purpose,
      oracle,
      repeat,
      oracleEvaluation,
      permutations,
    });
  }

  const observedModels = [...new Set(cases.flatMap((item) => item.repeat.observedModels))];
  const permutationAudits = cases.flatMap((item) => Object.values(item.permutations));
  const allNoulEvaluations = cases.flatMap((item) => item.oracleEvaluation.runs)
    .flatMap((run) => run.evaluations)
    .filter((item) => item.primitive === "noul" && item.reason === undefined);
  const coverage = polarityCoverage(fixtures);
  const completedAt = clock().toISOString();
  return {
    schema: "matawaka.jev-live-qualification/v0.3",
    normativeEffect: "NONE",
    authorityIssuance: "OUT_OF_SCOPE",
    principle: "PROBABILISTIC_JUDGMENT_IS_NOT_AUTHORIZATION",
    provider: provider.name ?? "unknown",
    qualificationMetadata,
    requestedModel: provider.model ?? null,
    modelInventory,
    observedModels,
    startedAt,
    completedAt,
    settings: { repeats, maxPermutations, probabilityDriftThreshold, winnerMarginThreshold },
    polarityCoverage: coverage,
    summary: {
      cases: cases.length,
      oracleEvaluations: cases.reduce((sum, item) => sum + item.oracleEvaluation.total, 0),
      oracleMatches: cases.reduce((sum, item) => sum + item.oracleEvaluation.matched, 0),
      oracleAssertedQuestions: cases.reduce((sum, item) => sum + item.oracle.assertedQuestions, 0),
      oracleObservationOnlyQuestions: cases.reduce((sum, item) => sum + item.oracle.observationOnlyQuestions, 0),
      noulOracleEvaluations: allNoulEvaluations.length,
      noulDirectionalMatches: allNoulEvaluations.filter((item) => item.directionMatch).length,
      noulBoundaryTies: allNoulEvaluations.filter((item) => item.boundaryTie).length,
      noulBrierMean: allNoulEvaluations.length
        ? allNoulEvaluations.reduce((sum, item) => sum + item.brierLoss, 0) / allNoulEvaluations.length
        : null,
      noulSignalsWithBothPolarities: Object.values(coverage).filter((item) => item.bothPolarities).length,
      noulSignalsMissingPolarity: Object.entries(coverage).filter(([, item]) => !item.bothPolarities).map(([questionId]) => questionId),
      repeatUnstableChoices: cases.flatMap((item) => Object.values(item.repeat.summaries))
        .filter((summary) => summary.primitive === "choice" && !summary.stableChoice).length,
      permutationLabelFlips: permutationAudits.filter((audit) => !audit.stableChoice).length,
      permutationMaterialDrift: permutationAudits.filter((audit) => audit.materialProbabilityDrift).length,
      permutationThinMargins: permutationAudits.filter((audit) => audit.thinWinnerMargin).length,
      permutationThresholdRelevantInstability: permutationAudits.filter((audit) => audit.thresholdRelevantInstability).length,
      maxPermutationProbabilitySpread: permutationAudits.length
        ? Math.max(...permutationAudits.map((audit) => audit.maxProbabilitySpread ?? 0))
        : 0,
      minPermutationWinnerMargin: permutationAudits.length
        ? Math.min(...permutationAudits.map((audit) => audit.minWinnerMargin ?? 1))
        : 1,
    },
    cases,
  };
}
