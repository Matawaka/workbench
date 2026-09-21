import test from "node:test";
import assert from "node:assert/strict";
import {
  SystemOneJudgmentAdapter,
  auditChoicePermutationStability,
  choice,
} from "../src/index.js";

class OrderSensitiveProvider {
  constructor() {
    this.name = "test.order-sensitive";
    this.model = "test-v0";
  }
  async evaluate(request) {
    const [id, q] = Object.entries(request.questions)[0];
    const labels = Object.keys(q.criteria);
    const first = labels[0];
    const second = labels[1];
    return {
      model: this.model,
      answers: {
        [id]: {
          type: "choice",
          choice: first,
          probabilities: { [first]: 0.7, [second]: 0.3 },
          confidence: 0.4,
        },
      },
    };
  }
}

test("permutation audit detects choice instability", async () => {
  const adapter = new SystemOneJudgmentAdapter({ provider: new OrderSensitiveProvider() });
  const result = await auditChoicePermutationStability({
    adapter,
    state: { ticket: "example" },
    questionId: "route",
    question: choice("Classify route.", { alpha: null, beta: null }),
  });
  assert.equal(result.runs, 2);
  assert.equal(result.stableChoice, false);
  assert.deepEqual(new Set(result.chosenLabels), new Set(["alpha", "beta"]));
});
