import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import {
  buildShadowExternalizationCandidate,
  createDisclosureTemplate,
} from "../src/shadow/externalization-candidate.js";
import { sha256Json } from "../src/canonical-json.js";

async function fixture(name) {
  return JSON.parse(await fs.readFile(new URL(`../fixtures/${name}`, import.meta.url), "utf8"));
}

test("builds a no-send externalization candidate from operator-sanitized synthetic context", async () => {
  const shadowEnvelope = await fixture("shadow-observation.synthetic.json");
  const disclosure = await fixture("shadow-disclosure.synthetic.json");
  disclosure.sourceShadowDigest = sha256Json(shadowEnvelope);

  const candidate = buildShadowExternalizationCandidate({ shadowEnvelope, disclosure });
  assert.equal(candidate.schema, "matawaka.jev-shadow-externalization-candidate/v0.2");
  assert.equal(candidate.normativeEffect, "NONE");
  assert.equal(candidate.externalizationAuthorized, false);
  assert.equal(candidate.providerInvocationAuthorized, false);
  assert.equal(candidate.networkSendAuthorized, false);
  assert.equal(candidate.decisionReadbackSupported, false);
  assert.equal(candidate.humanReviewRequired, true);
  assert.equal(candidate.questionUse.operationalSpecificity, "RESEARCH_ONLY");
  assert.equal(candidate.providerRequest.model, "jev-latest");
  assert.equal(candidate.sourceShadowDigest, sha256Json(shadowEnvelope));

  const text = JSON.stringify(candidate);
  assert.equal(text.includes("payments-prod-eu-west-secret"), false);
  assert.equal(text.includes("employee-alice-private"), false);
  assert.equal(text.includes("customer ledger"), false);
});

test("rejects disclosure that copies exact raw shadow operation/target text", async () => {
  const shadowEnvelope = await fixture("shadow-observation.synthetic.json");
  const disclosure = createDisclosureTemplate(shadowEnvelope);
  disclosure.operatorAttestedSanitized = true;
  disclosure.syntheticContext = {
    declaredIntent: "Inspect a staging service.",
    requestedOperation: shadowEnvelope.Authority.Operation,
    presentedScope: "Read-only staging inspection.",
    resourceClass: "synthetic namespace",
  };
  assert.throws(
    () => buildShadowExternalizationCandidate({ shadowEnvelope, disclosure }),
    /reuses raw shadow text/,
  );
});

test("requires explicit operator sanitization attestation", async () => {
  const shadowEnvelope = await fixture("shadow-observation.synthetic.json");
  const disclosure = createDisclosureTemplate(shadowEnvelope);
  disclosure.syntheticContext = {
    declaredIntent: "Inspect a staging service.",
    requestedOperation: "Inspect health only.",
    presentedScope: "Synthetic read-only scope.",
    resourceClass: "synthetic service",
  };
  assert.throws(
    () => buildShadowExternalizationCandidate({ shadowEnvelope, disclosure }),
    /operator-attested/,
  );
});

test("rejects a shadow envelope that claims provider invocation authority", async () => {
  const shadowEnvelope = await fixture("shadow-observation.synthetic.json");
  shadowEnvelope.ProviderInvocationAuthorized = true;
  const disclosure = await fixture("shadow-disclosure.synthetic.json");
  disclosure.sourceShadowDigest = sha256Json(shadowEnvelope);
  assert.throws(
    () => buildShadowExternalizationCandidate({ shadowEnvelope, disclosure }),
    /ProviderInvocationAuthorized must be false/,
  );
});

test("disclosure template binds exact shadow digest but is non-attested by default", async () => {
  const shadowEnvelope = await fixture("shadow-observation.synthetic.json");
  const template = createDisclosureTemplate(shadowEnvelope);
  assert.equal(template.sourceShadowDigest, sha256Json(shadowEnvelope));
  assert.equal(template.operatorAttestedSanitized, false);
  assert.deepEqual(template.notes, []);
});
