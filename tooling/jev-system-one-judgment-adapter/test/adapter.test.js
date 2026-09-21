import test from "node:test";
import assert from "node:assert/strict";
import {
  SystemOneJudgmentAdapter,
  FixtureProvider,
  choice,
  noul,
  score,
  validateQuestions,
} from "../src/index.js";

const fixture = {
  answers: {
    intentMatch: { type: "noul", noul: 0.9 },
    actionClass: {
      type: "choice",
      choice: "read_only",
      probabilities: { read_only: 0.91, reversible_write: 0.09 },
      confidence: 0.82,
    },
    ambiguity: {
      type: "score",
      score: 0.25,
      legend: { "0": "low", "1": "high" },
      probabilities: { "0": 0.75, "1": 0.25 },
      confidence: 0.5,
    },
  },
};

const questions = {
  intentMatch: noul("Does the operation semantically match the declared intent?"),
  actionClass: choice("Classify the effect class.", {
    read_only: null,
    reversible_write: null,
  }),
  ambiguity: score("How ambiguous is the operation?", ["low", "high"]),
};

test("produces non-normative judgment evidence", async () => {
  const adapter = new SystemOneJudgmentAdapter({
    provider: new FixtureProvider({ fixture }),
    clock: () => new Date("2026-09-20T00:00:00.000Z"),
  });
  const evidence = await adapter.evaluate({ state: { x: 1 }, questions });
  assert.equal(evidence.normativeEffect, "NONE");
  assert.equal(evidence.authorityIssuance, "OUT_OF_SCOPE");
  assert.equal(evidence.sideEffects, "OUT_OF_SCOPE");
  assert.equal(evidence.principle, "PROBABILISTIC_JUDGMENT_IS_NOT_AUTHORIZATION");
  assert.equal(evidence.judgments.actionClass.answer.choice, "read_only");
  assert.ok(evidence.requestDigest.startsWith("sha256:"));
  assert.ok(evidence.responseDigest.startsWith("sha256:"));
  assert.equal("permit" in evidence, false);
  assert.equal("authorized" in evidence, false);
});

test("rejects normative question ids", () => {
  assert.throws(
    () => validateQuestions({ authorized: noul("Is the request within the declared scope?") }),
    /normative/i,
  );
});

test("rejects normative choice outputs", () => {
  assert.throws(
    () => validateQuestions({ route: choice("Classify policy route.", { allow: null, review: null }) }),
    /normative/i,
  );
});

test("rejects asking the model to authorize execution", () => {
  assert.throws(
    () => validateQuestions({ scope: noul("Should this action be allowed to execute?") }),
    /authorization\/execution/i,
  );
});

test("rejects undeclared provider choice", async () => {
  const adapter = new SystemOneJudgmentAdapter({
    provider: new FixtureProvider({
      fixture: {
        answers: {
          route: {
            type: "choice",
            choice: "invented",
            probabilities: { known: 1 },
            confidence: 1,
          },
        },
      },
    }),
  });
  await assert.rejects(
    adapter.evaluate({
      state: {},
      questions: { route: choice("Classify the route.", { known: null }) },
    }),
    /undeclared option/i,
  );
});

test("accepts structured JSON instructions and criteria descriptions", () => {
  assert.equal(validateQuestions({
    structured: choice(
      { task: "Classify effect", context: ["synthetic", { risk: 2 }] },
      {
        read_only: { mutation: false, note: "Reads state" },
        reversible_write: ["Mutates state", { rollback: true }],
      },
    ),
  }), true);
});
