import test from "node:test";
import assert from "node:assert/strict";
import { brierScore, expectedCalibrationError } from "../src/index.js";

test("computes calibration diagnostics", () => {
  const samples = [
    { probability: 0.9, outcome: 1 },
    { probability: 0.8, outcome: 1 },
    { probability: 0.2, outcome: 0 },
    { probability: 0.1, outcome: 0 },
  ];
  assert.ok(brierScore(samples) < 0.05);
  const report = expectedCalibrationError(samples, 5);
  assert.ok(report.ece >= 0);
  assert.ok(Array.isArray(report.bins));
});
