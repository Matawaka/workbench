function numericCheck(value, rule) {
  if (rule.min !== undefined && value < rule.min) return false;
  if (rule.max !== undefined && value > rule.max) return false;
  return true;
}

export function evaluateExpectations(evidence, expectations = {}) {
  const checks = [];
  for (const [questionId, rule] of Object.entries(expectations)) {
    const judgment = evidence.judgments[questionId];
    if (!judgment) {
      checks.push({ questionId, pass: false, reason: "missing judgment" });
      continue;
    }
    const answer = judgment.answer;
    let pass = true;
    let observed;
    if (judgment.primitive === "noul") {
      observed = answer.noul;
      pass = numericCheck(observed, rule);
    } else if (judgment.primitive === "choice") {
      observed = answer.choice;
      if (rule.oneOf) pass = rule.oneOf.includes(observed);
      if (pass && rule.probabilityMin !== undefined) pass = answer.probabilities[observed] >= rule.probabilityMin;
    } else if (judgment.primitive === "score") {
      observed = answer.score;
      pass = numericCheck(observed, rule);
    }
    checks.push({ questionId, pass, observed, rule });
  }
  return {
    total: checks.length,
    passed: checks.filter((check) => check.pass).length,
    allPass: checks.every((check) => check.pass),
    checks,
  };
}
