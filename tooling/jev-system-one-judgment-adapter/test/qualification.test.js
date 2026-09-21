import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import { JevHttpProvider, runLiveQualification, validateQuestions } from "../src/index.js";

class StableQualificationProvider {
  constructor() { this.name = "test.qualification"; this.model = "jev-test"; this.calls = 0; }
  async listModels() {
    return { models: [{ name: "jev-test", description: "fixture", release_date: "2026-09-21" }], requestId: "req_models" };
  }
  async evaluate(request) {
    this.calls += 1;
    const answers = {};
    for (const [id, question] of Object.entries(request.questions)) {
      if (question.type === "noul") answers[id] = { type: "noul", noul: 0.8 };
      else if (question.type === "choice") {
        const labels = Object.keys(question.criteria);
        const p = Object.fromEntries(labels.map((label, index) => [label, index === 0 ? 0.7 : 0.3 / (labels.length - 1)]));
        answers[id] = { type: "choice", choice: labels[0], probabilities: p, confidence: 0.6 };
      } else {
        const probabilities = Object.fromEntries(question.criteria.map((_, index) => [String(index), index === 0 ? 1 : 0]));
        const legend = Object.fromEntries(question.criteria.map((criterion, index) => [String(index), criterion]));
        answers[id] = { type: "score", score: 0, probabilities, legend, confidence: 1 };
      }
    }
    return {
      model: "jev-test",
      requestedModel: this.model,
      provider: this.name,
      requestId: `req_${this.calls}`,
      answers,
      usage: { input_tokens: 10, output_tokens: 2 },
    };
  }
}

test("adversarial fixture file contains valid non-normative questions", async () => {
  const fixtures = JSON.parse(await fs.readFile(new URL("../fixtures/live-qualification.json", import.meta.url), "utf8"));
  assert.ok(fixtures.length >= 5);
  for (const fixture of fixtures) {
    assert.equal(validateQuestions(fixture.questions), true);
    assert.equal("actionClass" in fixture.questions, false);
    assert.ok("operationalSpecificity" in fixture.questions);
    assert.ok("targetSurface" in fixture.questions);
  }
});

test("live qualification produces an observation-only report", async () => {
  const provider = new StableQualificationProvider();
  const fixture = {
    id: "minimal",
    purpose: "test",
    state: { task: "read" },
    questions: {
      intentMatch: { type: "noul", instructions: "Does the operation match the declared intent?" },
      actionClass: { type: "choice", instructions: "Classify the effect.", criteria: { read_only: null, reversible_write: null } },
    },
    expectations: { actionClass: { oneOf: ["read_only"] } },
  };
  const report = await runLiveQualification({ provider, fixtures: [fixture], repeats: 2, maxPermutations: 2 });
  assert.equal(report.schema, "matawaka.jev-live-qualification/v0.2");
  assert.equal(report.normativeEffect, "NONE");
  assert.equal(report.authorityIssuance, "OUT_OF_SCOPE");
  assert.equal(report.modelInventory.models[0].name, "jev-test");
  assert.equal(report.summary.cases, 1);
  assert.equal(report.summary.expectationPasses, 2);
  assert.equal(report.cases[0].expectations.allRunsPass, true);
  assert.equal(typeof report.summary.permutationLabelFlips, "number");
  assert.equal(typeof report.summary.permutationMaterialDrift, "number");
  assert.equal(typeof report.summary.permutationThinMargins, "number");
  assert.equal(typeof report.summary.maxPermutationProbabilitySpread, "number");
  assert.equal(typeof report.summary.minPermutationWinnerMargin, "number");
});
