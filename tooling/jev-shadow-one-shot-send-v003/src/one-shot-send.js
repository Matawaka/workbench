import fs from "node:fs/promises";
import path from "node:path";
import { createHash } from "node:crypto";
import { sha256Json } from "../../jev-system-one-judgment-adapter/src/canonical-json.js";
import { SystemOneJudgmentAdapter } from "../../jev-system-one-judgment-adapter/src/adapter.js";

const CANDIDATE_SCHEMA = "matawaka.jev-shadow-externalization-candidate/v0.2";
const LEASE_SCHEMA = "matawaka.jev-shadow-send-lease/v0.3";
const RECEIPT_SCHEMA = "matawaka.jev-shadow-send-receipt/v0.3";
const ENDPOINT = "https://api.typesafe.ai/v1/systemone";

function requireObject(value, label) {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw new TypeError(`${label} must be an object.`);
}

function requireText(value, label) {
  if (typeof value !== "string" || value.trim() === "") throw new TypeError(`${label} must be a non-empty string.`);
}

function validateCandidate(candidate) {
  requireObject(candidate, "candidate");
  if (candidate.schema !== CANDIDATE_SCHEMA) throw new Error(`Unsupported candidate schema '${candidate.schema}'.`);
  if (candidate.normativeEffect !== "NONE" || candidate.authorityIssuance !== "OUT_OF_SCOPE") {
    throw new Error("Candidate has authority semantics in scope.");
  }
  for (const field of ["externalizationAuthorized", "providerInvocationAuthorized", "networkSendAuthorized", "decisionReadbackSupported"]) {
    if (candidate[field] !== false) throw new Error(`Candidate ${field} must remain false.`);
  }
  if (candidate.humanReviewRequired !== true) throw new Error("Candidate must require human review.");
  requireObject(candidate.providerRequest, "candidate.providerRequest");
  if (sha256Json(candidate.providerRequest) !== candidate.requestDigest) throw new Error("Candidate requestDigest mismatch.");
  return true;
}

export function createShadowSendLeaseTemplate(candidate, { now = new Date(), ttlSeconds = 900 } = {}) {
  validateCandidate(candidate);
  if (!(now instanceof Date) || Number.isNaN(now.valueOf())) throw new TypeError("now must be a valid Date.");
  if (!Number.isInteger(ttlSeconds) || ttlSeconds < 1 || ttlSeconds > 3600) throw new RangeError("ttlSeconds must be 1..3600.");
  return {
    schema: LEASE_SCHEMA,
    leaseId: "",
    candidateDigest: sha256Json(candidate),
    requestDigest: candidate.requestDigest,
    requestedModel: candidate.requestedModel,
    endpoint: ENDPOINT,
    issuedAt: now.toISOString(),
    expiresAt: new Date(now.getTime() + ttlSeconds * 1000).toISOString(),
    maxUses: 1,
    operatorConfirmed: false,
    providerInvocationAuthorized: false,
    networkSendAuthorized: false,
    workbenchReadbackAuthorized: false,
    authorityEffect: "NONE",
  };
}

export function validateShadowSendLease(candidate, lease, { now = new Date() } = {}) {
  validateCandidate(candidate);
  requireObject(lease, "lease");
  if (lease.schema !== LEASE_SCHEMA) throw new Error(`Unsupported lease schema '${lease.schema}'.`);
  requireText(lease.leaseId, "lease.leaseId");
  if (lease.candidateDigest !== sha256Json(candidate)) throw new Error("Lease candidateDigest mismatch.");
  if (lease.requestDigest !== candidate.requestDigest) throw new Error("Lease requestDigest mismatch.");
  if (lease.requestedModel !== candidate.requestedModel) throw new Error("Lease requestedModel mismatch.");
  if (lease.endpoint !== ENDPOINT) throw new Error("Lease endpoint mismatch.");
  if (lease.maxUses !== 1) throw new Error("Lease maxUses must equal 1.");
  if (lease.operatorConfirmed !== true) throw new Error("Lease requires explicit operator confirmation.");
  if (lease.providerInvocationAuthorized !== true) throw new Error("Lease does not authorize provider invocation.");
  if (lease.networkSendAuthorized !== true) throw new Error("Lease does not authorize network send.");
  if (lease.workbenchReadbackAuthorized !== false) throw new Error("Lease must not authorize Workbench readback.");
  if (lease.authorityEffect !== "NONE") throw new Error("Lease authorityEffect must be NONE.");
  const expiresAt = new Date(lease.expiresAt);
  if (Number.isNaN(expiresAt.valueOf())) throw new Error("Lease expiresAt is invalid.");
  if (expiresAt <= now) throw new Error("Lease is expired.");
  return true;
}

function token(value) {
  return createHash("sha256").update(value).digest("hex");
}

export class FileOneShotLeaseStore {
  constructor(root) {
    requireText(root, "root");
    this.root = root;
  }

  async consume(lease, candidate) {
    await fs.mkdir(this.root, { recursive: true });
    const file = path.join(this.root, `${token(lease.leaseId)}.consumed.json`);
    const record = {
      schema: "matawaka.jev-shadow-send-consumption/v0.3",
      leaseId: lease.leaseId,
      leaseDigest: sha256Json(lease),
      candidateDigest: sha256Json(candidate),
      requestDigest: candidate.requestDigest,
      consumedAt: new Date().toISOString(),
      providerAttemptAuthorized: true,
      workbenchReadbackAuthorized: false,
      authorityEffect: "NONE",
    };
    try {
      await fs.writeFile(file, `${JSON.stringify(record, null, 2)}\n`, { encoding: "utf8", flag: "wx" });
    } catch (error) {
      if (error?.code === "EEXIST") throw new Error("One-shot lease has already been consumed.");
      throw error;
    }
    return { file, record };
  }
}

export async function executeShadowSendOnce({ candidate, lease, provider, store, now = new Date(), signal = undefined } = {}) {
  validateShadowSendLease(candidate, lease, { now });
  if (!provider || typeof provider.evaluate !== "function") throw new TypeError("provider.evaluate is required.");
  if (!store || typeof store.consume !== "function") throw new TypeError("one-shot store.consume is required.");

  const candidateDigest = sha256Json(candidate);
  const leaseDigest = sha256Json(lease);
  const consumption = await store.consume(lease, candidate);

  const adapter = new SystemOneJudgmentAdapter({ provider });
  const evidence = await adapter.evaluate({
    state: candidate.providerRequest.state,
    questions: candidate.providerRequest.questions,
    correlationId: `shadow-send:${lease.leaseId}`,
    signal,
  });

  return {
    schema: RECEIPT_SCHEMA,
    status: "PROVIDER_OBSERVED_NO_AUTHORITY_CHANGE",
    normativeEffect: "NONE",
    authorityIssuance: "OUT_OF_SCOPE",
    principle: "PROBABILISTIC_JUDGMENT_IS_NOT_AUTHORIZATION",
    leaseConsumed: true,
    leaseDigest,
    candidateDigest,
    candidateRequestDigest: candidate.requestDigest,
    adapterRequestDigest: evidence.requestDigest,
    requestedModel: candidate.requestedModel,
    observedModel: evidence.observedModel,
    provider: evidence.provider,
    providerRequestId: evidence.providerRequestId,
    responseDigest: evidence.responseDigest,
    usage: evidence.usage,
    judgments: evidence.judgments,
    consumptionRecord: consumption.record,
    workbenchReadbackAuthorized: false,
    authorityCreated: false,
    displayPermitCreated: false,
    actionPermitCreated: false,
    nonEffects: [
      "one-shot provider response is shadow evidence only",
      "provider response cannot revise Workbench CapabilityDecision",
      "provider response cannot create display, action, or successor permits",
      "consumed lease cannot be replayed even when provider invocation fails",
    ],
  };
}
