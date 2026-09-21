import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { FixtureProvider } from "../../jev-system-one-judgment-adapter/src/providers/fixture-provider.js";
import { sha256Json } from "../../jev-system-one-judgment-adapter/src/canonical-json.js";
import { buildShadowExternalizationCandidate } from "../../jev-system-one-judgment-adapter/src/shadow/externalization-candidate.js";
import {
  createShadowSendLeaseTemplate,
  executeShadowSendOnce,
  FileOneShotLeaseStore,
  validateShadowSendLease,
} from "../src/one-shot-send.js";

async function fixture(name) {
  return JSON.parse(await fs.readFile(new URL(`../../jev-system-one-judgment-adapter/fixtures/${name}`, import.meta.url), "utf8"));
}

async function candidate() {
  const shadowEnvelope = await fixture("shadow-observation.synthetic.json");
  const disclosure = await fixture("shadow-disclosure.synthetic.json");
  disclosure.sourceShadowDigest = sha256Json(shadowEnvelope);
  return buildShadowExternalizationCandidate({ shadowEnvelope, disclosure });
}

function activate(template, now) {
  return {
    ...template,
    leaseId: "synthetic-one-shot-lease-001",
    issuedAt: now.toISOString(),
    expiresAt: new Date(now.getTime() + 300_000).toISOString(),
    operatorConfirmed: true,
    providerInvocationAuthorized: true,
    networkSendAuthorized: true,
  };
}

function fixtureProvider(c) {
  const answers = {};
  for (const [id, q] of Object.entries(c.providerRequest.questions)) {
    if (q.type === "noul") answers[id] = { type: "noul", noul: 0.8 };
    else if (q.type === "choice") {
      const labels = Object.keys(q.criteria);
      answers[id] = {
        type: "choice",
        choice: labels[0],
        confidence: 1,
        probabilities: Object.fromEntries(labels.map((label, i) => [label, i === 0 ? 1 : 0])),
      };
    } else {
      answers[id] = {
        type: "score",
        score: 0,
        confidence: 1,
        probabilities: Object.fromEntries(q.criteria.map((_, i) => [String(i), i === 0 ? 1 : 0])),
        legend: Object.fromEntries(q.criteria.map((v, i) => [String(i), v])),
      };
    }
  }
  return new FixtureProvider({
    name: "fixture.typesafe-shadow",
    model: c.requestedModel,
    fixture: {
      requestedModel: c.requestedModel,
      model: "jev-1.13.0-fixture",
      requestId: "req_fixture_shadow_001",
      answers,
      usage: { input_tokens: 100, output_tokens: 20 },
    },
  });
}

test("one-shot send lease is exact, bounded, and non-readback", async () => {
  const c = await candidate();
  const now = new Date("2026-09-21T08:00:00Z");
  const template = createShadowSendLeaseTemplate(c, { now });
  assert.equal(template.operatorConfirmed, false);
  assert.equal(template.providerInvocationAuthorized, false);
  assert.equal(template.networkSendAuthorized, false);
  const lease = activate(template, now);
  assert.equal(validateShadowSendLease(c, lease, { now }), true);
  assert.equal(lease.maxUses, 1);
  assert.equal(lease.workbenchReadbackAuthorized, false);
});

test("one-shot lease is consumed before provider call and cannot replay", async () => {
  const c = await candidate();
  const now = new Date("2026-09-21T08:00:00Z");
  const lease = activate(createShadowSendLeaseTemplate(c, { now }), now);
  const root = await fs.mkdtemp(path.join(os.tmpdir(), "jev-shadow-send-"));
  try {
    const store = new FileOneShotLeaseStore(root);
    const provider = fixtureProvider(c);
    const receipt = await executeShadowSendOnce({ candidate: c, lease, provider, store, now });
    assert.equal(receipt.status, "PROVIDER_OBSERVED_NO_AUTHORITY_CHANGE");
    assert.equal(receipt.leaseConsumed, true);
    assert.equal(receipt.workbenchReadbackAuthorized, false);
    assert.equal(receipt.authorityCreated, false);
    assert.equal(receipt.providerRequestId, "req_fixture_shadow_001");
    await assert.rejects(
      executeShadowSendOnce({ candidate: c, lease, provider, store, now }),
      /already been consumed/,
    );
  } finally {
    await fs.rm(root, { recursive: true, force: true });
  }
});

test("provider failure still burns the one-shot lease", async () => {
  const c = await candidate();
  const now = new Date("2026-09-21T08:00:00Z");
  const lease = activate(createShadowSendLeaseTemplate(c, { now }), now);
  const root = await fs.mkdtemp(path.join(os.tmpdir(), "jev-shadow-send-fail-"));
  const failingProvider = { name: "fixture.fail", model: c.requestedModel, async evaluate() { throw new Error("provider down"); } };
  try {
    const store = new FileOneShotLeaseStore(root);
    await assert.rejects(executeShadowSendOnce({ candidate: c, lease, provider: failingProvider, store, now }), /provider down/);
    await assert.rejects(executeShadowSendOnce({ candidate: c, lease, provider: fixtureProvider(c), store, now }), /already been consumed/);
  } finally {
    await fs.rm(root, { recursive: true, force: true });
  }
});

test("lease rejects digest drift, expiry, and absent confirmation", async () => {
  const c = await candidate();
  const now = new Date("2026-09-21T08:00:00Z");
  const template = createShadowSendLeaseTemplate(c, { now });
  assert.throws(() => validateShadowSendLease(c, template, { now }), /leaseId/);
  const lease = activate(template, now);
  assert.throws(() => validateShadowSendLease(c, { ...lease, requestDigest: "sha256:bad" }, { now }), /requestDigest mismatch/);
  assert.throws(() => validateShadowSendLease(c, { ...lease, expiresAt: "2026-09-21T07:59:59Z" }, { now }), /expired/);
  assert.throws(() => validateShadowSendLease(c, { ...lease, operatorConfirmed: false }, { now }), /operator confirmation/);
});
