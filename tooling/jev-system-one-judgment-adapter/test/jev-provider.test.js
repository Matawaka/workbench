import test from "node:test";
import assert from "node:assert/strict";
import { JevHttpProvider, score } from "../src/index.js";

test("Jev provider sends the documented System One request shape", async () => {
  let captured;
  const fetchImpl = async (url, init) => {
    captured = { url, init };
    return new Response(JSON.stringify({
      model: "jev-1.13.0",
      answers: { q: { type: "noul", noul: 0.7 }, ambiguity: { type: "score", score: 1.1, legend: { "0": "low", "1": "medium", "2": "high" }, confidence: 0.5, probabilities: { "0": 0.2, "1": 0.5, "2": 0.3 } } },
      usage: { input_tokens: 10, output_tokens: 0 },
    }), { status: 200, headers: { "content-type": "application/json", "x-typesafe-request-id": "req_test" } });
  };

  const provider = new JevHttpProvider({ apiKey: "test-key", fetchImpl, model: "jev-latest" });
  const result = await provider.evaluate({
    state: { message: "x" },
    questions: {
      q: { type: "noul", instructions: "Is x present?" },
      ambiguity: score("How ambiguous is x?", ["low", "medium", "high"]),
    },
  });

  assert.equal(captured.url, "https://api.typesafe.ai/v1/systemone");
  assert.equal(captured.init.method, "POST");
  assert.equal(captured.init.headers.authorization, "Bearer test-key");
  const body = JSON.parse(captured.init.body);
  assert.equal(body.model, "jev-latest");
  assert.deepEqual(body.state, { message: "x" });
  assert.equal(body.questions.q.type, "noul");
  assert.equal(body.questions.ambiguity.type, "score");
  assert.deepEqual(body.questions.ambiguity.criteria, ["low", "medium", "high"]);
  assert.equal("levels" in body.questions.ambiguity, false);
  assert.equal(result.model, "jev-1.13.0");
  assert.equal(result.requestId, "req_test");
});

test("Jev provider lists available models and captures request id", async () => {
  const fetchImpl = async (url, init) => {
    assert.equal(url, "https://api.typesafe.ai/v1/models");
    assert.equal(init.method, "GET");
    return new Response(JSON.stringify({
      models: [{ name: "jev-1.13.0", description: "test", release_date: "2026-09-15" }],
    }), { status: 200, headers: { "x-typesafe-request-id": "req_models" } });
  };
  const provider = new JevHttpProvider({ apiKey: "test-key", fetchImpl });
  const result = await provider.listModels();
  assert.equal(result.models[0].name, "jev-1.13.0");
  assert.equal(result.requestId, "req_models");
});
