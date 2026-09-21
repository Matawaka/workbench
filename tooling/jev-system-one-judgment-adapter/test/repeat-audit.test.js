import test from "node:test";
import assert from "node:assert/strict";
import { SystemOneJudgmentAdapter, auditRepeatStability, choice } from "../src/index.js";

class AlternatingProvider {
  constructor() { this.name = "test.alternating"; this.model = "test-v1"; this.calls = 0; }
  async evaluate(request) {
    this.calls += 1;
    const p = this.calls % 2 ? 0.51 : 0.49;
    return {
      model: this.model,
      answers: {
        route: {
          type: "choice",
          choice: p >= 0.5 ? "alpha" : "beta",
          probabilities: { alpha: p, beta: 1 - p },
          confidence: 0.02,
        },
      },
      usage: { input_tokens: 1, output_tokens: 1 },
    };
  }
}

test("repeat audit exposes run-to-run choice instability", async () => {
  const adapter = new SystemOneJudgmentAdapter({ provider: new AlternatingProvider() });
  const report = await auditRepeatStability({
    adapter,
    state: { x: 1 },
    questions: { route: choice("Classify route.", { alpha: null, beta: null }) },
    repeats: 4,
  });
  assert.equal(report.summaries.route.stableChoice, false);
  assert.deepEqual(new Set(report.summaries.route.chosenLabels), new Set(["alpha", "beta"]));
  assert.ok(report.summaries.route.probabilityRanges.alpha.spread > 0);
});
