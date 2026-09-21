function assertProbability(value, label) {
  if (typeof value !== "number" || !Number.isFinite(value) || value < 0 || value > 1) {
    throw new Error(`${label} must be a finite probability in [0,1].`);
  }
}

function validateDistribution(distribution, expectedKeys, label) {
  if (!distribution || typeof distribution !== "object" || Array.isArray(distribution)) {
    throw new Error(`${label} must be an object of probabilities.`);
  }
  const keys = Object.keys(distribution);
  const expected = [...expectedKeys];
  if (keys.length !== expected.length || expected.some((key) => !(key in distribution))) {
    throw new Error(`${label} keys must exactly match the declared outcomes.`);
  }
  keys.forEach((key) => assertProbability(distribution[key], `${label}.${key}`));
  const sum = keys.reduce((total, key) => total + distribution[key], 0);
  if (Math.abs(sum - 1) > 0.03) {
    throw new Error(`${label} must sum to approximately 1; got ${sum}.`);
  }
}

function canonical(value) {
  if (Array.isArray(value)) return value.map(canonical);
  if (value && typeof value === "object") {
    return Object.fromEntries(Object.keys(value).sort().map((key) => [key, canonical(value[key])]));
  }
  return value;
}

function equalJson(a, b) {
  return JSON.stringify(canonical(a)) === JSON.stringify(canonical(b));
}

function validateUsage(usage) {
  if (usage === undefined) return;
  if (!usage || typeof usage !== "object" || Array.isArray(usage)) throw new Error("Provider usage must be an object.");
  for (const field of ["input_tokens", "output_tokens"]) {
    if (!Number.isInteger(usage[field]) || usage[field] < 0) throw new Error(`Provider usage.${field} must be a non-negative integer.`);
  }
}

export function validateProviderResponse(response, questions) {
  if (!response || typeof response !== "object" || Array.isArray(response)) throw new Error("Provider response must be an object.");
  if (typeof response.model !== "string" || response.model.trim() === "") throw new Error("Provider response.model is required.");
  if (!response.answers || typeof response.answers !== "object" || Array.isArray(response.answers)) throw new Error("Provider response.answers is required.");

  const expectedIds = Object.keys(questions);
  const answerIds = Object.keys(response.answers);
  if (answerIds.length !== expectedIds.length || expectedIds.some((id) => !(id in response.answers))) {
    throw new Error("Provider response.answers must exactly match the requested question IDs.");
  }

  for (const [id, question] of Object.entries(questions)) {
    const answer = response.answers[id];
    if (!answer || typeof answer !== "object" || Array.isArray(answer)) throw new Error(`Missing answer for '${id}'.`);
    if (answer.type !== question.type) throw new Error(`Answer '${id}' type '${answer.type}' does not match requested '${question.type}'.`);

    if (question.type === "noul") {
      assertProbability(answer.noul, `Noul '${id}'`);
      continue;
    }

    if (question.type === "choice") {
      if (typeof answer.choice !== "string" || !(answer.choice in question.criteria)) {
        throw new Error(`Choice '${id}' returned an undeclared option '${answer.choice}'.`);
      }
      validateDistribution(answer.probabilities, Object.keys(question.criteria), `Choice '${id}' probabilities`);
      assertProbability(answer.confidence, `Choice '${id}' confidence`);
      continue;
    }

    if (question.type === "score") {
      if (typeof answer.score !== "number" || !Number.isFinite(answer.score)) throw new Error(`Score '${id}' score must be finite.`);
      if (answer.score < 0 || answer.score > question.criteria.length - 1) throw new Error(`Score '${id}' is outside the declared rubric.`);
      const scoreKeys = question.criteria.map((_, index) => String(index));
      validateDistribution(answer.probabilities, scoreKeys, `Score '${id}' probabilities`);
      assertProbability(answer.confidence, `Score '${id}' confidence`);
      if (!answer.legend || typeof answer.legend !== "object" || Array.isArray(answer.legend)) throw new Error(`Score '${id}' legend is required.`);
      const expectedLegend = Object.fromEntries(question.criteria.map((criterion, index) => [String(index), criterion]));
      if (!equalJson(answer.legend, expectedLegend)) throw new Error(`Score '${id}' legend does not match the requested rubric.`);
    }
  }

  validateUsage(response.usage);
  return true;
}
