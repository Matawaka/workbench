const LABEL_V2_SCHEMA = "matawaka.jev-shadow-human-label/v0.2";
const PRIMARY_V1_SCHEMA = "matawaka.jev-shadow-human-label/v0.1";
const DISPOSITIONS = new Set(["ASSERTED", "UNDETERMINED", "NOT_APPLICABLE"]);

function requireObject(value, label) {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    throw new TypeError(`${label} must be an object.`);
  }
}

function requireText(value, label) {
  if (typeof value !== "string" || value.trim() === "") {
    throw new TypeError(`${label} must be a non-empty string.`);
  }
}

function validateConfidence(value, label) {
  if (typeof value !== "number" || !Number.isFinite(value) || value < 0 || value > 1) {
    throw new RangeError(`${label} must be a number in [0,1].`);
  }
}

export function createSecondaryLabelV2(packet, { reviewerPseudonym = "" } = {}) {
  requireObject(packet, "packet");
  requireText(packet.caseId, "packet.caseId");
  if (packet.modelOutputVisible !== false) throw new Error("Secondary label packet must remain blinded.");

  const noulTruth = {};
  for (const [id, question] of Object.entries(packet.questions ?? {})) {
    if (question?.type !== "noul") continue;
    noulTruth[id] = {
      disposition: "UNDETERMINED",
      truth: null,
      reviewerConfidence: null,
      notes: "",
      researchOnly: packet.questionUse?.[id] === "RESEARCH_ONLY",
      conditionalOn: id === "hasReliableRollback" ? "causesExternalMutation" : null,
    };
  }

  return {
    schema: LABEL_V2_SCHEMA,
    caseId: packet.caseId,
    reviewPacketDigest: packet.reviewPacketDigest ?? null,
    labelKind: "HUMAN_SECONDARY",
    modelOutputVisibleToLabeler: false,
    independentOfModelOutput: true,
    reviewerPseudonym,
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

export function validateHumanLabelV2(label) {
  requireObject(label, "label");
  if (label.schema !== LABEL_V2_SCHEMA) throw new Error(`Unsupported label schema '${label.schema}'.`);
  if (!["HUMAN_SECONDARY", "HUMAN_ADJUDICATED"].includes(label.labelKind)) {
    throw new Error(`Unsupported v0.2 labelKind '${label.labelKind}'.`);
  }
  if (label.modelOutputVisibleToLabeler !== false || label.independentOfModelOutput !== true) {
    throw new Error("v0.2 label is not blinded/independent.");
  }
  if (label.authorityEffect !== "NONE") throw new Error("v0.2 label cannot create authority.");

  for (const [signal, entry] of Object.entries(label.noulTruth ?? {})) {
    requireObject(entry, `label.noulTruth.${signal}`);
    if (!DISPOSITIONS.has(entry.disposition)) {
      throw new Error(`Unsupported disposition '${entry.disposition}' for '${signal}'.`);
    }

    if (entry.disposition === "ASSERTED") {
      if (typeof entry.truth !== "boolean") {
        throw new Error(`ASSERTED '${signal}' requires boolean truth.`);
      }
      validateConfidence(entry.reviewerConfidence, `reviewerConfidence for '${signal}'`);
    } else {
      if (entry.truth !== null) {
        throw new Error(`${entry.disposition} '${signal}' requires truth=null.`);
      }
      if (entry.reviewerConfidence !== null) {
        throw new Error(`${entry.disposition} '${signal}' requires reviewerConfidence=null.`);
      }
    }
  }

  const mutation = label.noulTruth?.causesExternalMutation;
  const rollback = label.noulTruth?.hasReliableRollback;
  if (rollback?.disposition === "NOT_APPLICABLE") {
    if (!mutation || mutation.disposition !== "ASSERTED" || mutation.truth !== false) {
      throw new Error("hasReliableRollback may be NOT_APPLICABLE only when causesExternalMutation is ASSERTED false.");
    }
  }
  if (mutation?.disposition === "ASSERTED" && mutation.truth === true && rollback?.disposition === "NOT_APPLICABLE") {
    throw new Error("Rollback cannot be NOT_APPLICABLE when mutation is asserted true.");
  }

  return true;
}

export function migratePrimaryV1ToDiagnosticV2(primary) {
  requireObject(primary, "primary");
  if (primary.schema !== PRIMARY_V1_SCHEMA) throw new Error("Expected HUMAN_PRIMARY v0.1 source.");
  if (primary.labelKind !== "HUMAN_PRIMARY") throw new Error("Expected HUMAN_PRIMARY source.");
  if (primary.modelOutputVisibleToLabeler !== false || primary.independentOfModelOutput !== true) {
    throw new Error("Primary source is not blinded/independent.");
  }

  const noulTruth = {};
  for (const [signal, entry] of Object.entries(primary.noulTruth ?? {})) {
    const asserted = typeof entry.truth === "boolean";
    noulTruth[signal] = {
      disposition: asserted ? "ASSERTED" : "UNDETERMINED",
      truth: asserted ? entry.truth : null,
      reviewerConfidence: asserted ? entry.reviewerConfidence : null,
      notes: entry.notes ?? "",
      researchOnly: entry.researchOnly === true,
      conditionalOn: signal === "hasReliableRollback" ? "causesExternalMutation" : null,
      sourceLegacyPrimaryV1: true,
    };
  }

  return {
    schema: LABEL_V2_SCHEMA,
    caseId: primary.caseId,
    reviewPacketDigest: primary.reviewPacketDigest,
    labelKind: "HUMAN_SECONDARY",
    modelOutputVisibleToLabeler: false,
    independentOfModelOutput: true,
    reviewerPseudonym: primary.reviewerPseudonym,
    labeledAt: primary.labeledAt,
    noulTruth,
    diagnostics: primary.diagnostics ?? {},
    authorityEffect: "NONE",
    migrationNote: "Diagnostic migration only. Does not replace or mutate the frozen HUMAN_PRIMARY v0.1 artifact.",
  };
}

export function comparePrimaryV1ToReceipt(primary, receipt) {
  requireObject(primary, "primary");
  requireObject(receipt, "receipt");
  if (primary.schema !== PRIMARY_V1_SCHEMA) throw new Error("Expected primary v0.1 label.");
  if (receipt.schema !== "matawaka.jev-shadow-send-receipt/v0.3") throw new Error("Expected v0.3 Jev receipt.");

  const comparisons = [];
  for (const [signal, entry] of Object.entries(primary.noulTruth ?? {})) {
    const judgment = receipt.judgments?.[signal];
    if (!judgment || judgment.primitive !== "noul" || typeof entry.truth !== "boolean") continue;
    const probability = judgment.answer?.noul;
    const direction = probability > 0.5 ? true : probability < 0.5 ? false : null;
    comparisons.push({
      signal,
      humanTruth: entry.truth,
      humanConfidence: entry.reviewerConfidence,
      jevProbability: probability,
      jevDirection: direction,
      directionalMatch: direction === null ? null : direction === entry.truth,
      brierDiagnostic: (probability - (entry.truth ? 1 : 0)) ** 2,
      researchOnly: entry.researchOnly === true,
      calibrationEligible: false,
      note: signal === "hasReliableRollback"
        ? "Conditional signal: adjudication may set NOT_APPLICABLE when causesExternalMutation is asserted false."
        : "",
    });
  }

  return {
    schema: "matawaka.jev-shadow-primary-diagnostic-comparison/v0.1",
    status: "PRIMARY_DIAGNOSTIC_ONLY_NOT_CALIBRATION",
    normativeEffect: "NONE",
    authorityIssuance: "OUT_OF_SCOPE",
    primaryLabelKind: primary.labelKind,
    primaryModelBlind: primary.modelOutputVisibleToLabeler === false,
    comparisons,
    summary: {
      observations: comparisons.length,
      policyEligibleObservations: comparisons.filter((x) => !x.researchOnly).length,
      directionalMatches: comparisons.filter((x) => x.directionalMatch === true).length,
      directionalMisses: comparisons.filter((x) => x.directionalMatch === false).length,
      boundaryTies: comparisons.filter((x) => x.directionalMatch === null).length,
      brierMeanDiagnostic: comparisons.length
        ? comparisons.reduce((sum, x) => sum + x.brierDiagnostic, 0) / comparisons.length
        : null,
    },
    nonEffects: [
      "primary comparison is diagnostic only",
      "primary label remains immutable",
      "comparison does not create HUMAN_ADJUDICATED truth",
      "comparison does not create production thresholds or Workbench authority",
    ],
  };
}
