function numericCheck(value, rule) {
  if (rule.min !== undefined && value < rule.min) return false;
  if (rule.max !== undefined && value > rule.max) return false;
  return true;
}

function binaryDirection(probability) {
  if (probability > 0.5) return true;
  if (probability < 0.5) return false;
  return null;
}

export function evaluateOracleRules(evidence, oracles = {}) {
  const evaluations = [];
  for (const [questionId, rule] of Object.entries(oracles)) {
    const judgment = evidence.judgments[questionId];
    if (!judgment) {
      evaluations.push({ questionId, match: false, reason: "missing judgment", oracle: rule });
      continue;
    }

    const answer = judgment.answer;
    if (judgment.primitive === "noul") {
      if (typeof rule.truth !== "boolean") {
        throw new Error(`Noul oracle '${questionId}' must declare truth: true|false.`);
      }
      const probability = answer.noul;
      const truth = rule.truth;
      const direction = binaryDirection(probability);
      const directionMatch = direction === truth;
      const brierLoss = (probability - (truth ? 1 : 0)) ** 2;
      evaluations.push({
        questionId,
        primitive: "noul",
        oracleKind: "binary_truth",
        truth,
        probability,
        direction,
        directionMatch,
        boundaryTie: direction === null,
        brierLoss,
        match: directionMatch,
      });
      continue;
    }

    if (judgment.primitive === "choice") {
      const observed = answer.choice;
      const match = Array.isArray(rule.oneOf) ? rule.oneOf.includes(observed) : true;
      const oracleProbability = Array.isArray(rule.oneOf)
        ? rule.oneOf.reduce((sum, label) => sum + (answer.probabilities[label] ?? 0), 0)
        : null;
      evaluations.push({
        questionId,
        primitive: "choice",
        oracleKind: "categorical",
        observed,
        oneOf: rule.oneOf ?? null,
        oracleProbability,
        match,
      });
      continue;
    }

    if (judgment.primitive === "score") {
      const observed = answer.score;
      const match = numericCheck(observed, rule);
      evaluations.push({
        questionId,
        primitive: "score",
        oracleKind: "numeric_range",
        observed,
        range: { min: rule.min ?? null, max: rule.max ?? null },
        match,
      });
      continue;
    }
  }

  const noul = evaluations.filter((item) => item.primitive === "noul" && item.reason === undefined);
  return {
    total: evaluations.length,
    matched: evaluations.filter((item) => item.match).length,
    allMatch: evaluations.every((item) => item.match),
    noulEvaluations: noul.length,
    noulDirectionalMatches: noul.filter((item) => item.directionMatch).length,
    noulBoundaryTies: noul.filter((item) => item.boundaryTie).length,
    noulBrierMean: noul.length ? noul.reduce((sum, item) => sum + item.brierLoss, 0) / noul.length : null,
    evaluations,
  };
}

// Transitional export for older callers; alpha.5 fixtures use semantic oracles.
export const evaluateExpectations = evaluateOracleRules;
