function assertProbability(value, label) {
  if (typeof value !== "number" || !Number.isFinite(value) || value < 0 || value > 1) {
    throw new Error(`${label} must be a finite probability in [0,1].`);
  }
}

function validateDistribution(distribution, label) {
  if (!distribution || typeof distribution !== "object" || Array.isArray(distribution)) {
    throw new Error(`${label} must be an object of probabilities.`);
  }
  const values = Object.values(distribution);
  if (values.length === 0) throw new Error(`${label} cannot be empty.`);
  values.forEach((value, index) => assertProbability(value, `${label}[${index}]`));
  const sum = values.reduce((a, b) => a + b, 0);
  if (Math.abs(sum - 1) > 0.03) {
    throw new Error(`${label} must sum to approximately 1; got ${sum}.`);
  }
}

export function validateProviderResponse(response, questions) {
  if (!response || typeof response !== "object") throw new Error("Provider response must be an object.");
  if (!response.answers || typeof response.answers !== "object") throw new Error("Provider response.answers is required.");

  for (const [id, question] of Object.entries(questions)) {
    const answer = response.answers[id];
    if (!answer || typeof answer !== "object") throw new Error(`Missing answer for '${id}'.`);

    if (question.type === "noul") {
      assertProbability(answer.noul, `Noul '${id}'`);
      continue;
    }

    if (question.type === "choice") {
      if (typeof answer.choice !== "string" || !(answer.choice in question.criteria)) {
        throw new Error(`Choice '${id}' returned an undeclared option '${answer.choice}'.`);
      }
      validateDistribution(answer.probabilities, `Choice '${id}' probabilities`);
      for (const key of Object.keys(answer.probabilities)) {
        if (!(key in question.criteria)) throw new Error(`Choice '${id}' returned undeclared probability key '${key}'.`);
      }
      if (answer.confidence !== undefined) assertProbability(answer.confidence, `Choice '${id}' confidence`);
      continue;
    }

    if (question.type === "score") {
      if (typeof answer.score !== "number" || !Number.isFinite(answer.score)) {
        throw new Error(`Score '${id}' score must be finite.`);
      }
      if (answer.score < 0 || answer.score > question.criteria.length - 1) {
        throw new Error(`Score '${id}' is outside the declared rubric.`);
      }
      validateDistribution(answer.probabilities, `Score '${id}' probabilities`);
      if (answer.confidence !== undefined) assertProbability(answer.confidence, `Score '${id}' confidence`);
    }
  }

  return true;
}
