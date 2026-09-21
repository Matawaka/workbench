import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import { evaluateOracleRules, runLiveQualification, validateQuestions } from "../src/index.js";

class StableQualificationProvider {
  constructor() { this.name = "test.qualification"; this.model = "jev-test"; this.calls = 0; }
  async listModels() {
    return { models: [{ name: "jev-test", description: "fixture", release_date: "2026-09-21" }], requestId: "req_models" };
  }
  async evaluate(request) {
    this.calls += 1;
    const answers = {};
    for (const [id, question] of Object.entries(request.questions)) {
      if (question.type === "noul") {
        answers[id] = { type: "noul", noul: id === "negative" ? 0.2 : 0.8 };
      } else if (question.type === "choice") {
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

test("adversarial fixture file uses semantic Noul truth oracles", async () => {
  const fixtures = JSON.parse(await fs.readFile(new URL("../fixtures/live-qualification.json", import.meta.url), "utf8"));
  assert.ok(fixtures.length >= 7);
  for (const fixture of fixtures) {
    assert.equal(validateQuestions(fixture.questions), true);
    assert.equal("expectations" in fixture, false);
    for (const [questionId, rule] of Object.entries(fixture.oracles ?? {})) {
      if (fixture.questions[questionId].type === "noul") assert.equal(typeof rule.truth, "boolean");
    }
  }
  const scopeSmuggling = fixtures.find((fixture) => fixture.id === "scope-smuggling");
  assert.ok(scopeSmuggling);
  assert.equal("operationalSpecificity" in scopeSmuggling.oracles, false);
  const coverage = new Map();
  for (const fixture of fixtures) {
    for (const [questionId, rule] of Object.entries(fixture.oracles ?? {})) {
      if (fixture.questions[questionId].type !== "noul" || typeof rule.truth !== "boolean") continue;
      const entry = coverage.get(questionId) ?? new Set();
      entry.add(rule.truth);
      coverage.set(questionId, entry);
    }
  }
  for (const questionId of ["goalAlignment", "operationalSpecificity", "scopeExpansion", "causesExternalMutation", "hasReliableRollback", "externalCommunication"]) {
    assert.deepEqual(coverage.get(questionId), new Set([true, false]));
  }
});

test("Noul oracle uses truth labels and proper Brier loss, not confidence floors", () => {
  const evidence = {
    judgments: {
      positive: { primitive: "noul", answer: { type: "noul", noul: 0.65 } },
      negative: { primitive: "noul", answer: { type: "noul", noul: 0.2 } },
    },
  };
  const result = evaluateOracleRules(evidence, {
    positive: { truth: true },
    negative: { truth: false },
  });
  assert.equal(result.allMatch, true);
  assert.equal(result.noulDirectionalMatches, 2);
  assert.equal(result.noulBoundaryTies, 0);
  assert.ok(Math.abs(result.noulBrierMean - ((0.35 ** 2 + 0.2 ** 2) / 2)) < 1e-12);
});

test("live qualification reports proper-scoring and polarity coverage separately from policy thresholds", async () => {
  const provider = new StableQualificationProvider();
  const fixtures = [
    {
      id: "positive",
      purpose: "test positive",
      state: { task: "read" },
      questions: {
        positive: { type: "noul", instructions: "Is the positive proposition true?" },
        route: { type: "choice", instructions: "Classify the route.", criteria: { read_only: null, reversible_write: null } },
      },
      oracles: { positive: { truth: true }, route: { oneOf: ["read_only"] } },
    },
    {
      id: "negative",
      purpose: "test negative",
      state: { task: "read" },
      questions: {
        negative: { type: "noul", instructions: "Is the negative proposition true?" },
      },
      oracles: { negative: { truth: false } },
    },
  ];
  const report = await runLiveQualification({ provider, fixtures, repeats: 2, maxPermutations: 2 });
  assert.equal(report.schema, "matawaka.jev-live-qualification/v0.3");
  assert.equal(report.normativeEffect, "NONE");
  assert.equal(report.authorityIssuance, "OUT_OF_SCOPE");
  assert.equal(report.summary.cases, 2);
  assert.equal(report.summary.oracleEvaluations, 6);
  assert.equal(report.summary.oracleMatches, 6);
  assert.equal(report.summary.noulOracleEvaluations, 4);
  assert.equal(report.summary.noulDirectionalMatches, 4);
  assert.equal(report.summary.noulBoundaryTies, 0);
  assert.ok(report.summary.noulBrierMean >= 0 && report.summary.noulBrierMean <= 1);
  assert.equal(typeof report.summary.permutationLabelFlips, "number");
});
