function range(values) {
  const nums = values.filter((value) => typeof value === "number" && Number.isFinite(value));
  if (!nums.length) return null;
  return {
    min: Math.min(...nums),
    max: Math.max(...nums),
    spread: Math.max(...nums) - Math.min(...nums),
  };
}

function probabilityRanges(observations, labels) {
  return Object.fromEntries(
    labels.map((label) => [label, range(observations.map((answer) => answer.probabilities?.[label]))]),
  );
}

export async function auditRepeatStability({
  adapter,
  state,
  questions,
  repeats = 5,
  correlationPrefix = "repeat",
}) {
  if (!Number.isInteger(repeats) || repeats < 2) throw new RangeError("repeats must be an integer >= 2.");

  const evidences = [];
  for (let index = 0; index < repeats; index += 1) {
    evidences.push(await adapter.evaluate({
      state,
      questions,
      correlationId: `${correlationPrefix}-${String(index + 1).padStart(2, "0")}`,
    }));
  }

  const summaries = {};
  for (const [questionId, question] of Object.entries(questions)) {
    const answers = evidences.map((evidence) => evidence.judgments[questionId].answer);
    if (question.type === "noul") {
      summaries[questionId] = {
        primitive: "noul",
        valueRange: range(answers.map((answer) => answer.noul)),
      };
    } else if (question.type === "choice") {
      summaries[questionId] = {
        primitive: "choice",
        chosenLabels: [...new Set(answers.map((answer) => answer.choice))],
        stableChoice: new Set(answers.map((answer) => answer.choice)).size === 1,
        confidenceRange: range(answers.map((answer) => answer.confidence)),
        probabilityRanges: probabilityRanges(answers, Object.keys(question.criteria)),
      };
    } else if (question.type === "score") {
      const labels = question.criteria.map((_, index) => String(index));
      summaries[questionId] = {
        primitive: "score",
        scoreRange: range(answers.map((answer) => answer.score)),
        confidenceRange: range(answers.map((answer) => answer.confidence)),
        probabilityRanges: probabilityRanges(answers, labels),
      };
    }
  }

  return {
    schema: "matawaka.judgment-audit.repeat/v0.1",
    repeats,
    observedModels: [...new Set(evidences.map((evidence) => evidence.observedModel))],
    providerRequestIds: evidences.map((evidence) => evidence.providerRequestId).filter(Boolean),
    requestDigests: [...new Set(evidences.map((evidence) => evidence.requestDigest))],
    responseDigests: evidences.map((evidence) => evidence.responseDigest),
    summaries,
    evidences,
  };
}
