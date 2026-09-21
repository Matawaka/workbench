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
    this.calls = 0;
  }
  async evaluate(request) {
    this.calls += 1;
    const [id, q] = Object.entries(request.questions)[0];
    const labels = Object.keys(q.criteria);
    const first = labels[0];
    const second = labels[1];
    return {
      model: this.model,
      requestId: `req_${this.calls}`,
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

class StableLabelDriftProvider {
  constructor() {
    this.name = "test.stable-label-drift";
    this.model = "test-v1";
    this.calls = 0;
  }
  async evaluate(request) {
    this.calls += 1;
    const [id, q] = Object.entries(request.questions)[0];
    const labels = Object.keys(q.criteria);
    const alphaFirst = labels[0] === "alpha";
    const probabilities = alphaFirst
      ? { alpha: 0.80, beta: 0.20 }
      : { alpha: 0.51, beta: 0.49 };
    return {
      model: this.model,
      requestId: `req_${this.calls}`,
      answers: {
        [id]: {
          type: "choice",
          choice: "alpha",
          probabilities,
          confidence: alphaFirst ? 0.60 : 0.02,
        },
      },
    };
  }
}

test("permutation audit detects choice-label instability and preserves provenance", async () => {
  const adapter = new SystemOneJudgmentAdapter({ provider: new OrderSensitiveProvider() });
  const result = await auditChoicePermutationStability({
    adapter,
    state: { ticket: "example" },
    questionId: "route",
    question: choice("Classify route.", { alpha: null, beta: null }),
  });
  assert.equal(result.schema, "matawaka.judgment-audit.choice-permutation/v0.2");
  assert.equal(result.runs, 2);
  assert.equal(result.stableChoice, false);
  assert.equal(result.thresholdRelevantInstability, true);
  assert.deepEqual(new Set(result.chosenLabels), new Set(["alpha", "beta"]));
  assert.equal(result.providerRequestIds.length, 2);
  assert.equal(result.responseDigests.length, 2);
});

test("permutation audit detects material distribution drift even when the chosen label is stable", async () => {
  const adapter = new SystemOneJudgmentAdapter({ provider: new StableLabelDriftProvider() });
  const result = await auditChoicePermutationStability({
    adapter,
    state: { ticket: "example" },
    questionId: "route",
    question: choice("Classify route.", { alpha: null, beta: null }),
    probabilityDriftThreshold: 0.10,
    winnerMarginThreshold: 0.10,
  });
  assert.equal(result.stableChoice, true);
  assert.equal(result.materialProbabilityDrift, true);
  assert.equal(result.thinWinnerMargin, true);
  assert.equal(result.thresholdRelevantInstability, true);
  assert.ok(result.maxProbabilitySpread >= 0.29);
  assert.ok(result.minWinnerMargin <= 0.02 + Number.EPSILON);
});
