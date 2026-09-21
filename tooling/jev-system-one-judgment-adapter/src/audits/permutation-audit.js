function permutations(values, maxPermutations) {
  const out = [];
  function visit(prefix, remaining) {
    if (out.length >= maxPermutations) return;
    if (remaining.length === 0) {
      out.push(prefix);
      return;
    }
    for (let i = 0; i < remaining.length && out.length < maxPermutations; i += 1) {
      visit([...prefix, remaining[i]], [...remaining.slice(0, i), ...remaining.slice(i + 1)]);
    }
  }
  visit([], values);
  return out;
}

function winnerMargin(probabilities) {
  const values = Object.values(probabilities).filter((value) => Number.isFinite(value)).sort((a, b) => b - a);
  if (!values.length) return null;
  if (values.length === 1) return values[0];
  return values[0] - values[1];
}

export async function auditChoicePermutationStability({
  adapter,
  state,
  questionId,
  question,
  maxPermutations = 24,
  probabilityDriftThreshold = 0.10,
  winnerMarginThreshold = 0.10,
  correlationPrefix = "permutation",
}) {
  if (question.type !== "choice") throw new Error("Permutation audit requires a Choice question.");

  const optionEntries = Object.entries(question.criteria);
  const orders = permutations(optionEntries, maxPermutations);
  const observations = [];

  for (let index = 0; index < orders.length; index += 1) {
    const order = orders[index];
    const criteria = Object.fromEntries(order);
    const evidence = await adapter.evaluate({
      state,
      questions: {
        [questionId]: { ...question, criteria },
      },
      correlationId: `${correlationPrefix}:${questionId}:${String(index + 1).padStart(2, "0")}`,
    });
    const answer = evidence.judgments[questionId].answer;
    observations.push({
      order: order.map(([key]) => key),
      choice: answer.choice,
      confidence: answer.confidence,
      probabilities: answer.probabilities,
      winnerMargin: winnerMargin(answer.probabilities),
      observedModel: evidence.observedModel,
      providerRequestId: evidence.providerRequestId,
      requestDigest: evidence.requestDigest,
      responseDigest: evidence.responseDigest,
    });
  }

  const labels = optionEntries.map(([key]) => key);
  const ranges = Object.fromEntries(
    labels.map((label) => {
      const values = observations.map((o) => o.probabilities[label]).filter((v) => typeof v === "number");
      return [label, values.length ? Math.max(...values) - Math.min(...values) : null];
    }),
  );
  const finiteSpreads = Object.values(ranges).filter((value) => typeof value === "number" && Number.isFinite(value));
  const maxProbabilitySpread = finiteSpreads.length ? Math.max(...finiteSpreads) : null;
  const margins = observations.map((o) => o.winnerMargin).filter((value) => typeof value === "number" && Number.isFinite(value));
  const minWinnerMargin = margins.length ? Math.min(...margins) : null;
  const labelStable = new Set(observations.map((o) => o.choice)).size === 1;
  const materialProbabilityDrift = maxProbabilitySpread !== null && maxProbabilitySpread >= probabilityDriftThreshold;
  const thinWinnerMargin = minWinnerMargin !== null && minWinnerMargin <= winnerMarginThreshold;

  return {
    schema: "matawaka.judgment-audit.choice-permutation/v0.2",
    questionId,
    runs: observations.length,
    chosenLabels: [...new Set(observations.map((o) => o.choice))],
    probabilityRanges: ranges,
    maxProbabilitySpread,
    minWinnerMargin,
    probabilityDriftThreshold,
    winnerMarginThreshold,
    stableChoice: labelStable,
    materialProbabilityDrift,
    thinWinnerMargin,
    thresholdRelevantInstability: !labelStable || materialProbabilityDrift || thinWinnerMargin,
    observedModels: [...new Set(observations.map((o) => o.observedModel).filter(Boolean))],
    providerRequestIds: observations.map((o) => o.providerRequestId).filter(Boolean),
    requestDigests: [...new Set(observations.map((o) => o.requestDigest).filter(Boolean))],
    responseDigests: observations.map((o) => o.responseDigest).filter(Boolean),
    observations,
  };
}
