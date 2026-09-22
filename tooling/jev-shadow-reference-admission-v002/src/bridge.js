import { createBlindedReviewPacket, createCorpusCase } from '../../jev-shadow-evidence-corpus-v001/src/corpus.js';
import { validateQuestions } from '../../jev-system-one-judgment-adapter/src/contracts.js';
import { validateProviderResponse } from '../../jev-system-one-judgment-adapter/src/validate-response.js';
import { SIGNALS, check, keys, text, oneOf, probability, equal, digest, hash, parse, schema, boundary } from './common.js';

const PACKET = 'matawaka.jev-shadow-blinded-review-packet/v0.1';
const LABEL1 = 'matawaka.jev-shadow-human-label/v0.1';
const LABEL2 = 'matawaka.jev-shadow-human-label/v0.2';
const origins = ['HUMAN_UNASSISTED', 'HUMAN_AI_ASSISTED', 'MODEL_ONLY', 'UNKNOWN'];
const exposures = ['BLINDED_DECLARED', 'EXPOSED', 'UNKNOWN', 'NOT_APPLICABLE'];
const labelFields = ['schema', 'caseId', 'reviewPacketDigest', 'labelKind', 'modelOutputVisibleToLabeler', 'independentOfModelOutput', 'reviewerPseudonym', 'labeledAt', 'noulTruth', 'diagnostics', 'authorityEffect'];

export function validatePacket(packet) {
  keys(packet, ['schema', 'caseId', 'sampleClass', 'candidateDigest', 'requestDigest', 'modelOutputVisible', 'authorityEffect', 'intendedUse', 'context', 'questions', 'questionUse', 'reviewerInstructions', 'reviewPacketDigest'], ['schema', 'caseId', 'sampleClass', 'candidateDigest', 'requestDigest', 'modelOutputVisible', 'authorityEffect', 'intendedUse', 'context', 'questions', 'questionUse', 'reviewerInstructions']);
  check(packet.schema === PACKET, 'UNKNOWN_PACKET_SCHEMA');
  text(packet.caseId); hash(packet.candidateDigest); hash(packet.requestDigest);
  oneOf(packet.sampleClass, ['SYNTHETIC_CONTROL', 'SANITIZED_REAL_SHADOW']);
  check(packet.modelOutputVisible === false && packet.authorityEffect === 'NONE' && packet.intendedUse === 'INDEPENDENT_HUMAN_LABELING', 'INVALID_PACKET_BOUNDARY');
  validateQuestions(packet.questions);
  keys(packet.questions, [...SIGNALS, 'targetSurface', 'ambiguity']);
  keys(packet.questionUse, Object.keys(packet.questions));
  for (const id of SIGNALS) {
    check(packet.questions[id].type === 'noul', 'SIGNAL_TYPE_MISMATCH');
    check(packet.questionUse[id] === (id === 'operationalSpecificity' ? 'RESEARCH_ONLY' : 'SHADOW_OBSERVATION'), 'QUESTION_USE_MISMATCH');
  }
  check(packet.questions.targetSurface.type === 'choice' && packet.questions.ambiguity.type === 'score', 'DIAGNOSTIC_TYPE_MISMATCH');
  for (const id of ['targetSurface', 'ambiguity']) check(packet.questionUse[id] === 'DIAGNOSTIC_ONLY', 'QUESTION_USE_MISMATCH');
  for (const q of Object.values(packet.questions)) keys(q, ['type', 'instructions', 'criteria'], ['type', 'instructions']);
  const { reviewPacketDigest, ...body } = packet;
  const computed = digest(body);
  if (reviewPacketDigest !== undefined) check(reviewPacketDigest === computed, 'PACKET_SELF_DIGEST_MISMATCH');
  return { digest: computed, body };
}

export function interpretLabel(packet, label, expectedRole) {
  const packetInfo = validatePacket(packet);
  keys(label, [...labelFields, 'admissibleForScoring', 'sourceLabelKind', 'migrationNote'], labelFields);
  oneOf(label.schema, [LABEL1, LABEL2]);
  oneOf(label.labelKind, label.schema === LABEL1 ? ['HUMAN_PRIMARY', 'HUMAN_SECONDARY', 'HUMAN_ADJUDICATED', 'SYNTHETIC_ORACLE'] : ['HUMAN_SECONDARY', 'HUMAN_ADJUDICATED']);
  check(label.labelKind === expectedRole, 'LABEL_ROLE_MISMATCH');
  check(label.caseId === packet.caseId && label.reviewPacketDigest === packetInfo.digest, 'LABEL_PACKET_BINDING_MISMATCH');
  check(typeof label.modelOutputVisibleToLabeler === 'boolean' && typeof label.independentOfModelOutput === 'boolean', 'INVALID_LABEL_DECLARATIONS');
  check(label.authorityEffect === 'NONE', 'LABEL_AUTHORITY_INVALID');
  check(typeof label.reviewerPseudonym === 'string', 'INVALID_REVIEWER');
  check(label.labeledAt === null || (typeof label.labeledAt === 'string' && Number.isFinite(Date.parse(label.labeledAt))), 'INVALID_LABEL_TIME');
  keys(label.diagnostics, ['targetSurface', 'ambiguityBand', 'notes']);
  keys(label.noulTruth, SIGNALS);
  const migration = 'sourceLabelKind' in label || 'migrationNote' in label || label.admissibleForScoring === false || Object.values(label.noulTruth).some(x => x.sourceLegacyPrimaryV1 === true);
  const entries = {};
  for (const signal of SIGNALS) {
    const e = label.noulTruth[signal];
    const v2 = label.schema === LABEL2;
    keys(e, ['truth', 'reviewerConfidence', 'notes', 'researchOnly', ...(v2 ? ['disposition', 'conditionalOn', 'sourceLegacyPrimaryV1'] : [])], ['truth', 'reviewerConfidence', 'notes', 'researchOnly', ...(v2 ? ['disposition', 'conditionalOn'] : [])]);
    check(typeof e.notes === 'string', 'INVALID_NOTES');
    check(e.researchOnly === (signal === 'operationalSpecificity'), 'RESEARCH_ONLY_MISMATCH');
    const disposition = v2 ? e.disposition : typeof e.truth === 'boolean' ? 'ASSERTED' : 'UNDETERMINED';
    oneOf(disposition, ['ASSERTED', 'UNDETERMINED', 'NOT_APPLICABLE']);
    if (disposition === 'ASSERTED') { check(typeof e.truth === 'boolean', 'ASSERTED_REQUIRES_BOOLEAN'); probability(e.reviewerConfidence); }
    else { check(e.truth === null && e.reviewerConfidence === null, 'EXCLUDED_REQUIRES_NULL'); }
    if (v2) check(e.conditionalOn === (signal === 'hasReliableRollback' ? 'causesExternalMutation' : null), 'CONDITION_MISMATCH');
    if (disposition === 'NOT_APPLICABLE') check(signal === 'hasReliableRollback', 'NA_NOT_DEFINED_FOR_SIGNAL');
    entries[signal] = { disposition, truth: e.truth, researchOnly: e.researchOnly, exclusionReason: disposition === 'ASSERTED' ? null : (v2 ? disposition : 'V1_NULL_UNDETERMINED') };
  }
  if (entries.hasReliableRollback.disposition === 'NOT_APPLICABLE') check(entries.causesExternalMutation.disposition === 'ASSERTED' && entries.causesExternalMutation.truth === false, 'ROLLBACK_NA_REQUIRES_ASSERTED_NO_MUTATION');
  return { sourceSchema: label.schema, sourceRole: label.labelKind, interpretation: label.schema === LABEL1 ? 'EXPLICIT_V1_BOOLEAN_OR_NULL_VIEW_NO_CONVERSION' : 'V2_DISPOSITIONS', migration, entries };
}

function evidenceRefs(refs, records, reasons, reason) {
  check(Array.isArray(refs), 'EVIDENCE_REFS_REQUIRED');
  if (!refs.length) reasons.push(reason);
  for (const id of refs) { check(typeof id === 'string' && /^evidence_[a-zA-Z0-9_-]+$/.test(id), 'INVALID_EVIDENCE_REF'); if (!records[id]) reasons.push(`MISSING_EVIDENCE:${id}`); }
}

function signoff(ref, records, expected, reasons) {
  evidenceRefs(ref ? [ref] : [], records, reasons, 'SIGNOFF_MISSING');
  if (!records[ref]) return;
  const signed = parse(records[ref].bytes);
  keys(signed, ['schema', 'subjectRawSha256', 'signerId', 'status']);
  check(signed.schema === schema('declared-signoff'), 'UNKNOWN_SIGNOFF_SCHEMA');
  check(signed.subjectRawSha256 === expected.subjectRawSha256 && signed.signerId === expected.signerId, 'SIGNOFF_BINDING_MISMATCH');
  oneOf(signed.status, ['COMPLETED', 'DRAFT']);
  if (signed.status !== 'COMPLETED') reasons.push('SIGNOFF_NOT_COMPLETED');
}

function provenance(record, labelRecord, role, records, reasons) {
  if (!record) { reasons.push(`MISSING_PROVENANCE:${role}`); return null; }
  const p = record.value, label = labelRecord.value;
  keys(p, ['schema', 'caseId', 'labelRawSha256', 'role', 'origin', 'aiAssistance', 'humanExposure', 'assistingSessionExposure', 'reviewer', 'completion']);
  check(p.schema === schema('review-provenance'), 'UNKNOWN_PROVENANCE_SCHEMA');
  check(p.caseId === label.caseId && p.labelRawSha256 === labelRecord.rawSha256 && p.role === role, 'PROVENANCE_LABEL_BINDING_MISMATCH');
  keys(p.origin, ['classification', 'basis', 'evidenceRefs']);
  oneOf(p.origin.classification, origins); oneOf(p.origin.basis, ['OPERATOR_DECLARATION', 'REVIEWER_DECLARATION', 'UNKNOWN']);
  evidenceRefs(p.origin.evidenceRefs, records, reasons, `${role}:ORIGIN_EVIDENCE_MISSING`);
  if (p.origin.classification === 'UNKNOWN' || p.origin.basis === 'UNKNOWN') reasons.push(`${role}:ORIGIN_UNKNOWN`);
  keys(p.aiAssistance, ['status', 'evidenceRefs']); oneOf(p.aiAssistance.status, ['USED', 'NOT_USED_DECLARED', 'UNKNOWN']);
  evidenceRefs(p.aiAssistance.evidenceRefs, records, reasons, `${role}:AI_EVIDENCE_MISSING`);
  if (p.origin.classification === 'HUMAN_AI_ASSISTED') check(p.aiAssistance.status === 'USED', 'ASSISTANCE_ORIGIN_MISMATCH');
  if (p.origin.classification === 'HUMAN_UNASSISTED') check(p.aiAssistance.status === 'NOT_USED_DECLARED', 'ASSISTANCE_ORIGIN_MISMATCH');
  for (const kind of ['humanExposure', 'assistingSessionExposure']) {
    keys(p[kind], ['status', 'evidenceRefs']); oneOf(p[kind].status, exposures);
    evidenceRefs(p[kind].evidenceRefs, records, reasons, `${role}:${kind}_EVIDENCE_MISSING`);
  }
  if (p.humanExposure.status !== 'BLINDED_DECLARED') reasons.push(`${role}:HUMAN_EXPOSURE_${p.humanExposure.status}`);
  if (p.aiAssistance.status === 'USED' && p.assistingSessionExposure.status !== 'BLINDED_DECLARED') reasons.push(`${role}:ASSISTING_EXPOSURE_${p.assistingSessionExposure.status}`);
  if (p.aiAssistance.status === 'UNKNOWN') reasons.push(`${role}:AI_PARTICIPATION_UNKNOWN`);
  keys(p.reviewer, ['id', 'separationStatus', 'evidenceRefs']);
  oneOf(p.reviewer.separationStatus, ['DECLARED_DISTINCT', 'UNKNOWN']);
  check(p.reviewer.id === null || typeof p.reviewer.id === 'string', 'INVALID_REVIEWER_ID');
  evidenceRefs(p.reviewer.evidenceRefs, records, reasons, `${role}:REVIEWER_EVIDENCE_MISSING`);
  if (!p.reviewer.id?.trim() || p.reviewer.separationStatus !== 'DECLARED_DISTINCT') reasons.push(`${role}:REVIEWER_SEPARATION_NOT_ESTABLISHED`);
  keys(p.completion, ['status', 'kind', 'signatureRef', 'evidenceRefs']);
  oneOf(p.completion.status, ['TEMPLATE', 'DECLARATION', 'COMPLETED']);
  oneOf(p.completion.kind, ['ORIGINAL_REVIEW', 'MIGRATION', 'UNKNOWN']);
  check(p.completion.signatureRef === null || typeof p.completion.signatureRef === 'string', 'INVALID_SIGNATURE_REF');
  evidenceRefs(p.completion.evidenceRefs, records, reasons, `${role}:COMPLETION_EVIDENCE_MISSING`);
  if (p.completion.status !== 'COMPLETED' || p.completion.kind !== 'ORIGINAL_REVIEW') reasons.push(`${role}:REVIEW_NOT_COMPLETED_OR_ORIGINAL`);
  if (!p.completion.signatureRef) reasons.push(`${role}:SIGNATURE_MISSING`);
  else signoff(p.completion.signatureRef, records, { subjectRawSha256: labelRecord.rawSha256, signerId: p.reviewer.id }, reasons);
  return p;
}

function validateChain(records) {
  const value = id => records[id].value;
  const packet = value('packet'), candidate = value('candidate'), receipt = value('receipt'), request = value('request'), caseRecord = value('caseRecord');
  const packetInfo = validatePacket(packet);
  keys(candidate, ['schema', 'evidenceKind', 'normativeEffect', 'authorityIssuance', 'principle', 'intendedUse', 'externalizationAuthorized', 'providerInvocationAuthorized', 'networkSendAuthorized', 'decisionReadbackSupported', 'humanReviewRequired', 'sourceShadowDigest', 'disclosureDigest', 'requestDigest', 'requestedModel', 'providerRequest', 'questionUse', 'leakageChecks', 'nonEffects']);
  equal(candidate.providerRequest, request, 'REQUEST_FILE_MISMATCH');
  keys(request, ['model', 'state', 'questions']);
  check(request.model === candidate.requestedModel, 'REQUEST_MODEL_MISMATCH');
  equal(createBlindedReviewPacket({ candidate, caseId: packet.caseId, sampleClass: packet.sampleClass }), packetInfo.body, 'PACKET_CANDIDATE_MISMATCH');
  keys(receipt, ['schema', 'status', 'normativeEffect', 'authorityIssuance', 'principle', 'leaseConsumed', 'leaseDigest', 'candidateDigest', 'candidateRequestDigest', 'adapterRequestDigest', 'requestedModel', 'observedModel', 'provider', 'providerRequestId', 'responseDigest', 'usage', 'judgments', 'consumptionRecord', 'workbenchReadbackAuthorized', 'authorityCreated', 'displayPermitCreated', 'actionPermitCreated', 'nonEffects']);
  check(receipt.requestedModel === request.model, 'RECEIPT_MODEL_MISMATCH');
  check(receipt.adapterRequestDigest === digest({ state: request.state, questions: request.questions }), 'ADAPTER_REQUEST_MISMATCH');
  hash(receipt.responseDigest); hash(receipt.leaseDigest); text(receipt.provider); text(receipt.observedModel); text(receipt.providerRequestId);
  keys(receipt.judgments, Object.keys(packet.questions));
  for (const [id, judgment] of Object.entries(receipt.judgments)) {
    keys(judgment, ['primitive', 'instructions', 'answer']);
    check(judgment.primitive === packet.questions[id].type, 'JUDGMENT_TYPE_MISMATCH');
    equal(judgment.instructions, packet.questions[id].instructions, 'JUDGMENT_RUBRIC_MISMATCH');
    const fields = judgment.primitive === 'noul' ? ['type', 'noul'] : judgment.primitive === 'choice' ? ['type', 'choice', 'confidence', 'probabilities'] : ['type', 'score', 'confidence', 'legend', 'probabilities'];
    keys(judgment.answer, fields);
  }
  validateProviderResponse({ model: receipt.observedModel, answers: Object.fromEntries(Object.entries(receipt.judgments).map(([id, j]) => [id, j.answer])), usage: receipt.usage }, packet.questions);
  const cr = receipt.consumptionRecord;
  keys(cr, ['schema', 'leaseId', 'leaseDigest', 'candidateDigest', 'requestDigest', 'consumedAt', 'providerAttemptAuthorized', 'workbenchReadbackAuthorized', 'authorityEffect']);
  check(cr.schema === 'matawaka.jev-shadow-send-consumption/v0.3' && cr.leaseDigest === receipt.leaseDigest && cr.candidateDigest === digest(candidate) && cr.requestDigest === candidate.requestDigest, 'CONSUMPTION_BINDING_MISMATCH');
  check(cr.providerAttemptAuthorized === true && cr.workbenchReadbackAuthorized === false && cr.authorityEffect === 'NONE', 'CONSUMPTION_BOUNDARY_MISMATCH');
  text(cr.leaseId); check(Number.isFinite(Date.parse(cr.consumedAt)), 'INVALID_CONSUMPTION_TIME');
  equal(caseRecord, createCorpusCase({ candidate, receipt, caseId: packet.caseId, sampleClass: packet.sampleClass, sourceRevision: caseRecord.sourceRevision, receiptRawSha256: records.receipt.rawSha256 }), 'CORPUS_RECEIPT_BINDING_MISMATCH');
  if (records.response) {
    const response = value('response');
    check(digest(response) === receipt.responseDigest, 'RESPONSE_DIGEST_MISMATCH');
    validateProviderResponse(response, packet.questions);
    equal(response.answers, Object.fromEntries(Object.entries(receipt.judgments).map(([id, j]) => [id, j.answer])), 'RESPONSE_ANSWERS_MISMATCH');
    check(response.model === receipt.observedModel && response.requestId === receipt.providerRequestId, 'RESPONSE_METADATA_MISMATCH');
  }
}

export function preflight(bundle) {
  const { records } = bundle;
  const required = ['packet', 'candidate', 'request', 'receipt', 'caseRecord', 'primary', 'secondary'];
  const missing = [...new Set([...(bundle.missing ?? []), ...required.filter(id => !records[id])])];
  if (missing.length) return { schema: schema('reference-preflight'), ...boundary, status: 'PRIVATE_INPUTS_UNAVAILABLE', referenceAdmission: 'HOLD', reasons: missing.map(id => `MISSING_ORIGINAL:${id}`), requiredEvidenceChecks: ['RAW_SHA256_ALL_ORIGINALS', 'RECOMPUTED_PACKET_CANDIDATE_REQUEST_RECEIPT_CORPUS_CHAIN', 'FROZEN_LABELS_AND_EXACT_PROVENANCE_BINDING', 'REVIEWER_ORIGIN_EXPOSURE_IDENTITY_AND_COMPLETION_SIGNOFFS', 'SOURCE_GROUP_INDEPENDENCE_DECLARATIONS', 'PREDECLARED_REFERENCE_POLICY', 'COMPLETED_HUMAN_ADJUDICATION_EVENT'], sourceManifest: Object.fromEntries(Object.entries(records).map(([id, r]) => [id, r.rawSha256])), reviews: [] };
  validateChain(records);
  const packet = records.packet.value, reasons = [], reviews = [];
  for (const [id, role] of [['primary', 'HUMAN_PRIMARY'], ['secondary', 'HUMAN_SECONDARY'], ['adjudicated', 'HUMAN_ADJUDICATED']]) {
    if (!records[id]) continue;
    const interpretation = interpretLabel(packet, records[id].value, role);
    const reviewReasons = [];
    if (interpretation.migration) reviewReasons.push(`${role}:MIGRATION_IS_NOT_REVIEW`);
    const p = provenance(records[`${id}Provenance`], records[id], role, records, reviewReasons);
    if (records[id].value.modelOutputVisibleToLabeler !== false || records[id].value.independentOfModelOutput !== true) reviewReasons.push(`${role}:LABEL_EXPOSURE_DECLARATION`);
    reasons.push(...reviewReasons);
    reviews.push({ id, ...interpretation, origin: p?.origin.classification ?? 'UNKNOWN', provenance: p, reasons: reviewReasons });
  }
  const reviewerIds = reviews.map(r => r.provenance?.reviewer.id).filter(Boolean);
  if (new Set(reviewerIds).size !== reviewerIds.length) reasons.push('REVIEWER_IDENTITIES_NOT_DISTINCT');
  let grouping = null;
  if (!records.source || !records.sourceGrouping) reasons.push('SOURCE_INDEPENDENCE_NOT_ESTABLISHED');
  else {
    grouping = records.sourceGrouping.value;
    keys(grouping, ['schema', 'caseId', 'candidateDigest', 'sourceRawSha256', 'groupId', 'independenceStatus', 'evidenceRefs']);
    check(grouping.schema === schema('source-grouping'), 'UNKNOWN_GROUPING_SCHEMA');
    check(grouping.caseId === packet.caseId && grouping.candidateDigest === digest(records.candidate.value) && grouping.sourceRawSha256 === records.source.rawSha256, 'SOURCE_GROUP_BINDING_MISMATCH');
    text(grouping.groupId); oneOf(grouping.independenceStatus, ['DECLARED_SOURCE_GROUP', 'UNKNOWN']);
    const groupingReasons = [];
    evidenceRefs(grouping.evidenceRefs, records, groupingReasons, 'GROUPING_EVIDENCE_MISSING');
    if (grouping.independenceStatus === 'UNKNOWN') groupingReasons.push('SOURCE_INDEPENDENCE_UNKNOWN');
    reasons.push(...groupingReasons);
    grouping = { ...grouping, establishedAtDeclarationLevel: groupingReasons.length === 0 };
  }
  let policy = null;
  if (!records.policy) reasons.push('MISSING_PREDECLARED_REFERENCE_POLICY');
  else {
    policy = records.policy.value;
    keys(policy, ['schema', 'policyId', 'status', 'allowedOrigins', 'priorToReviewEvidenceRefs']);
    check(policy.schema === schema('reference-policy'), 'UNKNOWN_POLICY_SCHEMA'); text(policy.policyId);
    oneOf(policy.status, ['PREDECLARED', 'UNKNOWN']);
    check(Array.isArray(policy.allowedOrigins) && policy.allowedOrigins.length > 0, 'POLICY_ORIGINS_REQUIRED');
    policy.allowedOrigins.forEach(x => oneOf(x, ['HUMAN_UNASSISTED', 'HUMAN_AI_ASSISTED']));
    evidenceRefs(policy.priorToReviewEvidenceRefs, records, reasons, 'POLICY_PREDECLARATION_EVIDENCE_MISSING');
    if (policy.status !== 'PREDECLARED') reasons.push('POLICY_NOT_PREDECLARED');
    for (const r of reviews) if (!policy.allowedOrigins.includes(r.origin)) reasons.push(`${r.sourceRole}:ORIGIN_OUTSIDE_POLICY`);
  }
  if (!records.adjudicated || !records.adjudication) reasons.push('ADJUDICATION_PENDING');
  if (records.adjudication) {
    const event = records.adjudication.value;
    keys(event, ['schema', 'caseId', 'packetDigest', 'primaryRawSha256', 'secondaryRawSha256', 'primaryProvenanceRawSha256', 'secondaryProvenanceRawSha256', 'adjudicatedRawSha256', 'provenanceRawSha256', 'policyRawSha256', 'adjudicatorId', 'exposure', 'status', 'decisionBasis', 'evidenceRefs', 'signatureRef']);
    check(event.schema === schema('adjudication-event'), 'UNKNOWN_ADJUDICATION_SCHEMA');
    check(event.caseId === packet.caseId && event.packetDigest === validatePacket(packet).digest && event.primaryRawSha256 === records.primary.rawSha256 && event.secondaryRawSha256 === records.secondary.rawSha256, 'ADJUDICATION_INPUT_BINDING_MISMATCH');
    for (const [key, id] of [['primaryProvenanceRawSha256', 'primaryProvenance'], ['secondaryProvenanceRawSha256', 'secondaryProvenance'], ['adjudicatedRawSha256', 'adjudicated'], ['provenanceRawSha256', 'adjudicatedProvenance'], ['policyRawSha256', 'policy']]) {
      hash(event[key]); if (!records[id]) reasons.push(`ADJUDICATION_MISSING:${id}`); else check(event[key] === records[id].rawSha256, 'ADJUDICATION_OUTPUT_BINDING_MISMATCH');
    }
    oneOf(event.status, ['DRAFT', 'COMPLETED']); oneOf(event.exposure, exposures);
    oneOf(event.decisionBasis, ['HUMAN_EVIDENCE_ONLY', 'POST_MODEL_DIAGNOSTIC']); text(event.adjudicatorId);
    evidenceRefs(event.evidenceRefs, records, reasons, 'ADJUDICATION_EVIDENCE_MISSING');
    if (event.signatureRef) signoff(event.signatureRef, records, { subjectRawSha256: records.adjudication.rawSha256, signerId: event.adjudicatorId }, reasons); else reasons.push('ADJUDICATION_SIGNATURE_MISSING');
    if (event.status !== 'COMPLETED' || event.exposure !== 'BLINDED_DECLARED' || event.decisionBasis !== 'HUMAN_EVIDENCE_ONLY') reasons.push('ADJUDICATION_NOT_COMPLETE_BLINDED_HUMAN_EVENT');
    if (event.adjudicatorId !== reviews.find(r => r.id === 'adjudicated')?.provenance?.reviewer.id) reasons.push('ADJUDICATOR_ID_NOT_BOUND');
  }
  const admitted = reasons.length === 0;
  return { schema: schema('reference-preflight'), ...boundary, caseId: packet.caseId, status: admitted ? 'DECLARED_RESEARCH_REFERENCE_REQUIREMENTS_MET' : 'DIAGNOSTIC_PENDING', referenceAdmission: admitted ? 'DECLARED_RESEARCH_REFERENCE' : 'HOLD', reasons: [...new Set(reasons)], assertionLevel: 'LINKED_DECLARATIONS_NOT_AUTHENTICATED_IDENTITY_CHRONOLOGY_OR_BLINDING', sourceManifest: Object.fromEntries(Object.entries(records).map(([id, r]) => [id, r.rawSha256])), responseDigestStatus: records.response ? 'MATCHED_ORIGINAL_RESPONSE' : 'DECLARED_ONLY_ORIGINAL_RESPONSE_UNAVAILABLE', grouping, reviews };
}

function rejectModelFields(value) {
  if (!value || typeof value !== 'object') return;
  const forbidden = /^(predictions|probabilities|jevProbability|modelPredictions|judgments|receipt|comparisons|primaryVsModel|primaryVsJev|brierMean|directionalMatch)$/i;
  for (const [key, child] of Object.entries(value)) {
    check(!forbidden.test(key), 'MODEL_OUTPUT_FIELD_IN_HUMAN_INPUT');
    rejectModelFields(child);
  }
}

// Deliberately accepts ONLY these three verified originals, never a receipt/corpus/provenance.
export function prepareHumanOnly(bundle) {
  keys(bundle.records, ['packet', 'primary', 'secondary']);
  check(!bundle.missing?.length, 'MISSING_HUMAN_INPUTS');
  const packet = bundle.records.packet.value;
  const info = validatePacket(packet);
  rejectModelFields(packet.context); rejectModelFields(packet.questions);
  const reviews = [['primary', 'HUMAN_PRIMARY'], ['secondary', 'HUMAN_SECONDARY']].map(([id, role]) => {
    const label = bundle.records[id].value;
    check(label.modelOutputVisibleToLabeler === false && label.independentOfModelOutput === true, 'EXPOSED_LABEL_IN_HUMAN_INPUT');
    const view = interpretLabel(packet, label, role);
    check(!view.migration, 'MIGRATION_IS_NOT_REVIEW');
    // No freeform reviewer notes or diagnostics: they could include post-model comparisons.
    return { role, sourceRawSha256: bundle.records[id].rawSha256, sourceSchema: view.sourceSchema, interpretation: view.interpretation, entries: view.entries };
  });
  return {
    'human-packet.json': { schema: schema('human-only-adjudication-packet'), ...boundary, caseId: packet.caseId, originalPacketDigest: info.digest, context: packet.context, questions: packet.questions, questionUse: packet.questionUse, reviews, warning: 'Human must verify sanitized source context contains no model outputs. Structural filtering does not prove blinding or sanitize adversarial prose.' },
    'adjudication-draft.json': { schema: schema('unfilled-adjudication-draft'), caseId: packet.caseId, originalPacketDigest: info.digest, status: 'DRAFT_NOT_A_LABEL', reviewerId: null, origin: null, humanExposure: 'UNKNOWN', assistingSessionExposure: 'UNKNOWN', completedAt: null, signatureRef: null, entries: Object.fromEntries(SIGNALS.map(id => [id, { disposition: null, truth: null, reviewerConfidence: null, rationale: null }])), referenceAdmission: 'HOLD' }
  };
}
