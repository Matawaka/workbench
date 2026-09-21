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

export async function auditChoicePermutationStability({
  adapter,
  state,
  questionId,
  question,
  maxPermutations = 24,
}) {
  if (question.type !== "choice") throw new Error("Permutation audit requires a Choice question.");

  const optionEntries = Object.entries(question.criteria);
  const orders = permutations(optionEntries, maxPermutations);
  const observations = [];

  for (const order of orders) {
    const criteria = Object.fromEntries(order);
    const evidence = await adapter.evaluate({
      state,
      questions: {
        [questionId]: { ...question, criteria },
      },
    });
    const answer = evidence.judgments[questionId].answer;
    observations.push({
      order: order.map(([key]) => key),
      choice: answer.choice,
      probabilities: answer.probabilities,
    });
  }

  const labels = optionEntries.map(([key]) => key);
  const ranges = Object.fromEntries(
    labels.map((label) => {
      const values = observations.map((o) => o.probabilities[label]).filter((v) => typeof v === "number");
      return [label, values.length ? Math.max(...values) - Math.min(...values) : null];
    }),
  );

  return {
    schema: "matawaka.judgment-audit.choice-permutation/v0.1",
    questionId,
    runs: observations.length,
    chosenLabels: [...new Set(observations.map((o) => o.choice))],
    probabilityRanges: ranges,
    stableChoice: new Set(observations.map((o) => o.choice)).size === 1,
    observations,
  };
}
