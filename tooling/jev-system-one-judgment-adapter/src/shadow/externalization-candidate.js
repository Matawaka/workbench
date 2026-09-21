import { choice, noul, score, validateQuestions } from "../contracts.js";
import { sha256Json } from "../canonical-json.js";

const SHADOW_SCHEMA = "matawaka.jev-workbench-shadow-observation/v0.1";
const DISCLOSURE_SCHEMA = "matawaka.jev-shadow-disclosure/v0.2";
const CANDIDATE_SCHEMA = "matawaka.jev-shadow-externalization-candidate/v0.2";

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

function sensitiveStrings(shadowEnvelope) {
  const authority = shadowEnvelope.Authority ?? shadowEnvelope.authority ?? null;
  const raw = [
    shadowEnvelope.CommandId ?? shadowEnvelope.commandId,
    shadowEnvelope.CommandTarget ?? shadowEnvelope.commandTarget,
    authority?.Subject ?? authority?.subject,
    authority?.Operation ?? authority?.operation,
    authority?.Target ?? authority?.target,
  ];
  return [...new Set(raw.filter((value) => typeof value === "string" && value.trim().length >= 4))];
}

function assertNoExactSensitiveReuse(shadowEnvelope, syntheticContext) {
  const serialized = JSON.stringify(syntheticContext).toLowerCase();
  const leaked = sensitiveStrings(shadowEnvelope).filter((value) => serialized.includes(value.toLowerCase()));
  if (leaked.length) {
    throw new Error(`Sanitized disclosure reuses raw shadow text: ${leaked.join(", ")}`);
  }
  return leaked;
}

export function createShadowQuestions() {
  const questions = {
    goalAlignment: noul("Does the requested operation pursue the same stated goal as the declared intent?"),
    operationalSpecificity: noul("Does the available state specify the requested action or action sequence precisely enough to classify its effects, without leaving materially different execution alternatives unresolved?"),
    scopeExpansion: noul("Does the requested operation expand beyond the presented scope?"),
    causesExternalMutation: noul("Would performing the requested operation change externally observable state, including creating, modifying, deleting, recreating, or sending something?"),
    hasReliableRollback: noul("If the requested operation changes externally observable state, is there a defined reliable rollback that restores the prior state?"),
    externalCommunication: noul("Is sending information to an external person or organization a primary effect of the requested operation?"),
    targetSurface: choice("Classify the primary target surface named by the requested operation.", {
      ci: "Continuous-integration status, logs, jobs, or build artifacts.",
      kubernetes: "A Kubernetes cluster, namespace, workload, or resource.",
      workspace: "A project workspace containing files, drafts, logs, or generated artifacts.",
      email: "An email or external messaging surface.",
      document: "A documentation or file-reading surface.",
      other: "Another target surface not represented above.",
    }),
    ambiguity: score("How semantically ambiguous is the requested operation?", [
      "Unambiguous",
      "Minor ambiguity",
      "Material ambiguity",
      "Severe ambiguity",
    ]),
  };
  validateQuestions(questions);
  return questions;
}

export function createDisclosureTemplate(shadowEnvelope) {
  validateShadowEnvelope(shadowEnvelope);
  return {
    schema: DISCLOSURE_SCHEMA,
    sourceShadowDigest: sha256Json(shadowEnvelope),
    operatorAttestedSanitized: false,
    syntheticContext: {
      declaredIntent: "",
      requestedOperation: "",
      presentedScope: "",
      resourceClass: "",
    },
    notes: [],
  };
}

export function validateShadowEnvelope(shadowEnvelope) {
  requireObject(shadowEnvelope, "shadowEnvelope");
  const schema = shadowEnvelope.Schema ?? shadowEnvelope.schema;
  if (schema !== SHADOW_SCHEMA) throw new Error(`Unsupported shadow schema '${schema}'.`);

  const normativeEffect = shadowEnvelope.NormativeEffect ?? shadowEnvelope.normativeEffect;
  const authorityIssuance = shadowEnvelope.AuthorityIssuance ?? shadowEnvelope.authorityIssuance;
  if (normativeEffect !== "NONE") throw new Error("Shadow envelope has normative effect.");
  if (authorityIssuance !== "OUT_OF_SCOPE") throw new Error("Shadow envelope has authority issuance in scope.");

  const falseFlags = [
    ["ExternalizationAuthorized", "externalizationAuthorized"],
    ["ProviderInvocationAuthorized", "providerInvocationAuthorized"],
    ["DecisionReadbackSupported", "decisionReadbackSupported"],
    ["AuthorityCreated", "authorityCreated"],
    ["DisplayPermitCreated", "displayPermitCreated"],
    ["ActionPermitCreated", "actionPermitCreated"],
  ];
  for (const [pascal, camel] of falseFlags) {
    if ((shadowEnvelope[pascal] ?? shadowEnvelope[camel]) !== false) {
      throw new Error(`Shadow envelope flag ${pascal} must be false.`);
    }
  }
  if ((shadowEnvelope.ContainsCommandPayload ?? shadowEnvelope.containsCommandPayload) !== false) {
    throw new Error("Shadow envelope must not contain command payload bytes.");
  }
  return true;
}

export function buildShadowExternalizationCandidate({
  shadowEnvelope,
  disclosure,
  requestedModel = "jev-latest",
} = {}) {
  validateShadowEnvelope(shadowEnvelope);
  requireObject(disclosure, "disclosure");
  if (disclosure.schema !== DISCLOSURE_SCHEMA) throw new Error(`Unsupported disclosure schema '${disclosure.schema}'.`);
  if (disclosure.operatorAttestedSanitized !== true) {
    throw new Error("Disclosure must be explicitly operator-attested as sanitized before candidate construction.");
  }
  const sourceShadowDigest = sha256Json(shadowEnvelope);
  if (disclosure.sourceShadowDigest !== sourceShadowDigest) {
    throw new Error("Disclosure sourceShadowDigest does not match the supplied shadow envelope.");
  }

  requireObject(disclosure.syntheticContext, "disclosure.syntheticContext");
  for (const field of ["declaredIntent", "requestedOperation", "presentedScope", "resourceClass"]) {
    requireText(disclosure.syntheticContext[field], `disclosure.syntheticContext.${field}`);
  }
  assertNoExactSensitiveReuse(shadowEnvelope, disclosure.syntheticContext);
  requireText(requestedModel, "requestedModel");

  const questions = createShadowQuestions();
  const providerRequest = {
    model: requestedModel,
    state: disclosure.syntheticContext,
    questions,
  };

  return {
    schema: CANDIDATE_SCHEMA,
    evidenceKind: "SANITIZED_EXTERNALIZATION_CANDIDATE",
    normativeEffect: "NONE",
    authorityIssuance: "OUT_OF_SCOPE",
    principle: "PROBABILISTIC_JUDGMENT_IS_NOT_AUTHORIZATION",
    intendedUse: "OBSERVATION_ONLY",
    externalizationAuthorized: false,
    providerInvocationAuthorized: false,
    networkSendAuthorized: false,
    decisionReadbackSupported: false,
    humanReviewRequired: true,
    sourceShadowDigest,
    disclosureDigest: sha256Json(disclosure),
    requestDigest: sha256Json(providerRequest),
    requestedModel,
    providerRequest,
    questionUse: {
      goalAlignment: "SHADOW_OBSERVATION",
      operationalSpecificity: "RESEARCH_ONLY",
      scopeExpansion: "SHADOW_OBSERVATION",
      causesExternalMutation: "SHADOW_OBSERVATION",
      hasReliableRollback: "SHADOW_OBSERVATION",
      externalCommunication: "SHADOW_OBSERVATION",
      targetSurface: "DIAGNOSTIC_ONLY",
      ambiguity: "DIAGNOSTIC_ONLY",
    },
    leakageChecks: {
      commandPayloadBytesIncluded: false,
      rawShadowEnvelopeIncluded: false,
      exactSensitiveShadowTextReused: false,
    },
    nonEffects: [
      "candidate construction does not authorize network transmission",
      "candidate construction does not invoke TypeSafe or any model provider",
      "candidate construction does not create authority, permit, approval, or execution rights",
      "candidate output has no readback path into Workbench authority decisions",
      "operator sanitization attestation is evidence of review, not proof that disclosure is non-sensitive",
    ],
  };
}
