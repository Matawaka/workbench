import { createHash } from "node:crypto";

const CANDIDATE_SCHEMA = "matawaka.jev-shadow-externalization-candidate/v0.2";
const RECEIPT_SCHEMA = "matawaka.jev-shadow-send-receipt/v0.3";
const PACKET_SCHEMA = "matawaka.jev-shadow-blinded-review-packet/v0.1";
const LABEL_SCHEMA = "matawaka.jev-shadow-human-label/v0.1";
const CASE_SCHEMA = "matawaka.jev-shadow-corpus-case/v0.1";
const SCORE_SCHEMA = "matawaka.jev-shadow-corpus-score/v0.1";

const SAMPLE_CLASSES = new Set(["SYNTHETIC_CONTROL", "SANITIZED_REAL_SHADOW"]);
const LABEL_KINDS = new Set(["SYNTHETIC_ORACLE", "HUMAN_PRIMARY", "HUMAN_SECONDARY", "HUMAN_ADJUDICATED"]);

function normalize(value) {
  if (Array.isArray(value)) return value.map(normalize);
  if (value && typeof value === "object") {
    return Object.fromEntries(Object.keys(value).sort().map((key) => [key, normalize(value[key])]));
  }
  return value;
}

export function canonicalJson(value) {
  return JSON.stringify(normalize(value));
}

export function sha256Json(value) {
  return `sha256:${createHash("sha256").update(canonicalJson(value)).digest("hex")}`;
}

export function sha256Bytes(value) {
  const bytes = Buffer.isBuffer(value) ? value : Buffer.from(value);
  return `sha256:${createHash("sha256").update(bytes).digest("hex")}`;
}

function requireObject(value, label) {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw new TypeError(`${label} must be an object.`);
}

function requireText(value, label) {
  if (typeof value !== "string" || value.trim() === "") throw new TypeError(`${label} must be a non-empty string.`);
}

function validateSampleClass(sampleClass) {
  if (!SAMPLE_CLASSES.has(sampleClass)) throw new Error(`Unsupported sampleClass '${sampleClass}'.`);
}

function validateCandidate(candidate) {
  requireObject(candidate, "candidate");
  if (candidate.schema !== CANDIDATE_SCHEMA) throw new Error(`Unsupported candidate schema '${candidate.schema}'.`);
  if (candidate.normativeEffect !== "NONE" || candidate.authorityIssuance !== "OUT_OF_SCOPE") throw new Error("Candidate authority boundary invalid.");
  for (const key of ["externalizationAuthorized", "providerInvocationAuthorized", "networkSendAuthorized", "decisionReadbackSupported"]) {
    if (candidate[key] !== false) throw new Error(`candidate.${key} must remain false.`);
  }
  if (candidate.humanReviewRequired !== true) throw new Error("candidate.humanReviewRequired must be true.");
  requireObject(candidate.providerRequest, "candidate.providerRequest");
  if (candidate.requestDigest !== sha256Json(candidate.providerRequest)) throw new Error("Candidate requestDigest mismatch.");
  return true;
}

function validateReceipt(candidate, receipt) {
  validateCandidate(candidate);
  requireObject(receipt, "receipt");
  if (receipt.schema !== RECEIPT_SCHEMA) throw new Error(`Unsupported receipt schema '${receipt.schema}'.`);
  if (receipt.status !== "PROVIDER_OBSERVED_NO_AUTHORITY_CHANGE") throw new Error("Receipt status is not observation-only success.");
  if (receipt.normativeEffect !== "NONE" || receipt.authorityIssuance !== "OUT_OF_SCOPE") throw new Error("Receipt authority boundary invalid.");
  if (receipt.workbenchReadbackAuthorized !== false || receipt.authorityCreated !== false || receipt.displayPermitCreated !== false || receipt.actionPermitCreated !== false) {
    throw new Error("Receipt permits authority/readback effects.");
  }
  if (receipt.leaseConsumed !== true) throw new Error("Receipt does not prove one-shot lease consumption.");
  if (receipt.candidateDigest !== sha256Json(candidate)) throw new Error("Receipt candidateDigest mismatch.");
  if (receipt.candidateRequestDigest !== candidate.requestDigest) throw new Error("Receipt candidateRequestDigest mismatch.");
  requireObject(receipt.judgments, "receipt.judgments");
  return true;
}

function strippedQuestion(question) {
  const out = { type: question.type, instructions: question.instructions ?? null };
  if (question.criteria !== undefined) out.criteria = question.criteria;
  return out;
}

export function createBlindedReviewPacket({ candidate, caseId, sampleClass }) {
  validateCandidate(candidate);
  requireText(caseId, "caseId");
  validateSampleClass(sampleClass);

  return {
    schema: PACKET_SCHEMA,
    caseId,
    sampleClass,
    candidateDigest: sha256Json(candidate),
    requestDigest: candidate.requestDigest,
    modelOutputVisible: false,
    authorityEffect: "NONE",
    intendedUse: "INDEPENDENT_HUMAN_LABELING",
    context: candidate.providerRequest.state,
    questions: Object.fromEntries(
      Object.entries(candidate.providerRequest.questions).map(([id, question]) => [id, strippedQuestion(question)]),
    ),
    questionUse: candidate.questionUse,
    reviewerInstructions: [
      "Label from the sanitized context and question definitions only.",
      "Do not inspect Jev/model predictions before submitting the independent label.",
      "A human label does not create Workbench authority or a permit.",
      "operationalSpecificity remains research-only until separately qualified.",
    ],
  };
}

export function createHumanLabelTemplate(packet, { labelKind = "HUMAN_PRIMARY" } = {}) {
  requireObject(packet, "packet");
  if (packet.schema !== PACKET_SCHEMA) throw new Error(`Unsupported packet schema '${packet.schema}'.`);
  if (packet.modelOutputVisible !== false) throw new Error("Packet is not blinded.");
  if (!LABEL_KINDS.has(labelKind)) throw new Error(`Unsupported labelKind '${labelKind}'.`);

  const noulTruth = {};
  for (const [id, question] of Object.entries(packet.questions)) {
    if (question.type !== "noul") continue;
    noulTruth[id] = {
      truth: null,
      reviewerConfidence: null,
      notes: "",
      researchOnly: packet.questionUse?.[id] === "RESEARCH_ONLY",
    };
  }

  return {
    schema: LABEL_SCHEMA,
    caseId: packet.caseId,
    reviewPacketDigest: sha256Json(packet),
    labelKind,
    modelOutputVisibleToLabeler: false,
    independentOfModelOutput: true,
    reviewerPseudonym: "",
    labeledAt: null,
    noulTruth,
    diagnostics: {
      targetSurface: null,
      ambiguityBand: null,
      notes: "",
    },
    authorityEffect: "NONE",
  };
}

export function createCorpusCase({ candidate, receipt, caseId, sampleClass, sourceRevision = null, receiptRawSha256 = null }) {
  validateReceipt(candidate, receipt);
  requireText(caseId, "caseId");
  validateSampleClass(sampleClass);

  const predictions = {};
  const diagnostics = {};
  for (const [id, judgment] of Object.entries(receipt.judgments)) {
    if (judgment.primitive === "noul") {
      predictions[id] = {
        probability: judgment.answer.noul,
        questionUse: candidate.questionUse?.[id] ?? "UNSPECIFIED",
        researchOnly: candidate.questionUse?.[id] === "RESEARCH_ONLY",
      };
    } else {
      diagnostics[id] = judgment.answer;
    }
  }

  return {
    schema: CASE_SCHEMA,
    caseId,
    sampleClass,
    independentSample: true,
    deploymentCalibrationEligibleBase: sampleClass === "SANITIZED_REAL_SHADOW",
    syntheticControl: sampleClass === "SYNTHETIC_CONTROL",
    contextRetention: "DIGEST_ONLY_IN_CASE_RECORD",
    sourceRevision,
    binding: {
      candidateDigest: receipt.candidateDigest,
      requestDigest: receipt.candidateRequestDigest,
      leaseDigest: receipt.leaseDigest,
      responseDigest: receipt.responseDigest,
      receiptRawSha256,
    },
    provider: {
      name: receipt.provider,
      requestedModel: receipt.requestedModel,
      observedModel: receipt.observedModel,
      providerRequestId: receipt.providerRequestId,
      usage: receipt.usage,
    },
    predictions,
    diagnostics,
    authorityBoundary: {
      normativeEffect: receipt.normativeEffect,
      authorityIssuance: receipt.authorityIssuance,
      workbenchReadbackAuthorized: receipt.workbenchReadbackAuthorized,
      authorityCreated: receipt.authorityCreated,
      displayPermitCreated: receipt.displayPermitCreated,
      actionPermitCreated: receipt.actionPermitCreated,
    },
  };
}

export function validateHumanLabel({ packet, label, caseRecord }) {
  requireObject(label, "label");
  if (label.schema !== LABEL_SCHEMA) throw new Error(`Unsupported label schema '${label.schema}'.`);
  if (!LABEL_KINDS.has(label.labelKind)) throw new Error(`Unsupported labelKind '${label.labelKind}'.`);
  if (label.caseId !== caseRecord.caseId || packet.caseId !== caseRecord.caseId) throw new Error("caseId mismatch across packet/label/case.");
  if (label.reviewPacketDigest !== sha256Json(packet)) throw new Error("Human label reviewPacketDigest mismatch.");
  if (label.modelOutputVisibleToLabeler !== false || label.independentOfModelOutput !== true) throw new Error("Label is not independent/blinded.");

  for (const [signal, entry] of Object.entries(label.noulTruth ?? {})) {
    if (!(signal in caseRecord.predictions)) continue;
    if (entry.truth !== null && typeof entry.truth !== "boolean") throw new Error(`Label truth for '${signal}' must be boolean or null.`);
  }
  return true;
}

function directional(probability) {
  if (probability > 0.5) return true;
  if (probability < 0.5) return false;
  return null;
}

function ece(observations, bins = 10) {
  if (!observations.length) return null;
  let total = 0;
  for (let b = 0; b < bins; b += 1) {
    const lo = b / bins;
    const hi = (b + 1) / bins;
    const bucket = observations.filter((x) => x.p >= lo && (b === bins - 1 ? x.p <= hi : x.p < hi));
    if (!bucket.length) continue;
    const conf = bucket.reduce((s, x) => s + x.p, 0) / bucket.length;
    const acc = bucket.reduce((s, x) => s + (x.truth ? 1 : 0), 0) / bucket.length;
    total += (bucket.length / observations.length) * Math.abs(conf - acc);
  }
  return total;
}

function summarizeObservations(observations, { minEceCases }) {
  if (!observations.length) {
    return { n: 0, truthTrue: 0, truthFalse: 0, directionalMatches: 0, boundaryTies: 0, brierMean: null, ece: null, eceStatus: "NO_DATA", highConfidenceWrong: 0 };
  }
  const directionalMatches = observations.filter((x) => directional(x.p) === x.truth).length;
  const boundaryTies = observations.filter((x) => directional(x.p) === null).length;
  const highConfidenceWrong = observations.filter((x) => (x.truth && x.p <= 0.2) || (!x.truth && x.p >= 0.8)).length;
  return {
    n: observations.length,
    truthTrue: observations.filter((x) => x.truth).length,
    truthFalse: observations.filter((x) => !x.truth).length,
    directionalMatches,
    boundaryTies,
    brierMean: observations.reduce((s, x) => s + (x.p - (x.truth ? 1 : 0)) ** 2, 0) / observations.length,
    ece: observations.length >= minEceCases ? ece(observations) : null,
    eceStatus: observations.length >= minEceCases ? "COMPUTED" : `INSUFFICIENT_INDEPENDENT_CASES_MIN_${minEceCases}`,
    highConfidenceWrong,
  };
}

function selectLabel(labels) {
  const priority = ["HUMAN_ADJUDICATED", "SYNTHETIC_ORACLE", "HUMAN_SECONDARY", "HUMAN_PRIMARY"];
  return [...labels].sort((a, b) => priority.indexOf(a.labelKind) - priority.indexOf(b.labelKind))[0] ?? null;
}

export function scoreCorpus({ cases, packets, labels, minEceCases = 30 } = {}) {
  if (!Array.isArray(cases) || !Array.isArray(packets) || !Array.isArray(labels)) throw new TypeError("cases, packets, and labels must be arrays.");
  const caseIds = cases.map((x) => x.caseId);
  if (new Set(caseIds).size !== caseIds.length) throw new Error("Duplicate corpus caseId detected.");
  const packetByCase = new Map(packets.map((x) => [x.caseId, x]));
  const labelsByCase = new Map();
  for (const label of labels) {
    const list = labelsByCase.get(label.caseId) ?? [];
    list.push(label);
    labelsByCase.set(label.caseId, list);
  }

  const populations = { syntheticControl: [], deploymentCalibration: [] };
  const errors = [];
  const pending = [];

  for (const c of cases) {
    const packet = packetByCase.get(c.caseId);
    const label = selectLabel(labelsByCase.get(c.caseId) ?? []);
    if (!packet || !label) {
      pending.push({ caseId: c.caseId, reason: !packet ? "MISSING_BLINDED_PACKET" : "MISSING_LABEL" });
      continue;
    }
    validateHumanLabel({ packet, label, caseRecord: c });

    let population = null;
    if (c.sampleClass === "SYNTHETIC_CONTROL" && label.labelKind === "SYNTHETIC_ORACLE") population = "syntheticControl";
    if (c.sampleClass === "SANITIZED_REAL_SHADOW" && label.labelKind === "HUMAN_ADJUDICATED") population = "deploymentCalibration";
    if (!population) {
      pending.push({ caseId: c.caseId, reason: "LABEL_NOT_ADMISSIBLE_FOR_SCORING_POPULATION", labelKind: label.labelKind, sampleClass: c.sampleClass });
      continue;
    }

    for (const [signal, prediction] of Object.entries(c.predictions)) {
      const labelEntry = label.noulTruth?.[signal];
      if (!labelEntry || typeof labelEntry.truth !== "boolean") continue;
      const observation = { caseId: c.caseId, signal, p: prediction.probability, truth: labelEntry.truth, researchOnly: prediction.researchOnly };
      populations[population].push(observation);
      const dir = directional(observation.p);
      if (dir === null) errors.push({ ...observation, cluster: "BOUNDARY_TIE", population });
      else if (dir !== observation.truth) errors.push({ ...observation, cluster: "DIRECTIONAL_MISS", population });
      if ((observation.truth && observation.p <= 0.2) || (!observation.truth && observation.p >= 0.8)) errors.push({ ...observation, cluster: "HIGH_CONFIDENCE_WRONG", population });
    }
  }

  function populationSummary(observations) {
    const signals = [...new Set(observations.map((x) => x.signal))].sort();
    const bySignal = Object.fromEntries(signals.map((signal) => [signal, summarizeObservations(observations.filter((x) => x.signal === signal), { minEceCases })]));
    const policyEligible = observations.filter((x) => !x.researchOnly);
    return {
      observations: observations.length,
      independentCases: new Set(observations.map((x) => x.caseId)).size,
      allSignals: summarizeObservations(observations, { minEceCases }),
      policyEligibleSignals: summarizeObservations(policyEligible, { minEceCases }),
      bySignal,
    };
  }

  return {
    schema: SCORE_SCHEMA,
    normativeEffect: "NONE",
    authorityIssuance: "OUT_OF_SCOPE",
    principle: "PROBABILISTIC_JUDGMENT_IS_NOT_AUTHORIZATION",
    syntheticControl: populationSummary(populations.syntheticControl),
    deploymentCalibration: populationSummary(populations.deploymentCalibration),
    pending,
    errorClusters: errors,
    calibrationPolicy: {
      independentCasesOnly: true,
      syntheticControlsExcludedFromDeployment: true,
      deploymentRequiresSampleClass: "SANITIZED_REAL_SHADOW",
      deploymentRequiresLabelKind: "HUMAN_ADJUDICATED",
      labelerMustBeBlindedToModelOutput: true,
      operationalSpecificityResearchOnly: true,
      minIndependentCasesForEce: minEceCases,
    },
  };
}
