import fs from 'node:fs';
import fsp from 'node:fs/promises';
import path from 'node:path';
import { createBlindedReviewPacket, createCorpusCase, createHumanLabelTemplate } from '../../jev-shadow-evidence-corpus-v001/src/corpus.js';
import { createSecondaryLabelV2 } from '../../jev-shadow-label-semantics-v002/src/label-v2.js';
import { digest, rawHash, schema } from '../src/common.js';

const publicSynthetic = name => JSON.parse(fs.readFileSync(new URL(`../../jev-shadow-evidence-corpus-v001/evidence/synthetic-control-001/${name}.json`, import.meta.url), 'utf8').replace(/^\uFEFF/, ''));
export const record = value => { const bytes = Buffer.from(JSON.stringify(value)); return { value, bytes, rawSha256: rawHash(bytes) }; };
export function fixture({ id = 'synthetic-0', source = id, model = 'synthetic-model-1', rubric = '', sampleClass = 'SYNTHETIC_CONTROL', origin = 'HUMAN_UNASSISTED' } = {}) {
  const candidate = publicSynthetic('candidate');
  candidate.providerRequest.state = { declaredIntent: 'Read synthetic fixture', requestedOperation: `Read ${source}`, presentedScope: 'Read only', resourceClass: 'synthetic' };
  candidate.providerRequest.questions.goalAlignment.instructions += rubric;
  candidate.sourceShadowDigest = digest({ source });
  candidate.requestDigest = digest(candidate.providerRequest);
  const receipt = publicSynthetic('receipt');
  receipt.provider = 'synthetic.offline'; receipt.observedModel = model; receipt.providerRequestId = `fake-${id}`;
  receipt.candidateDigest = digest(candidate); receipt.candidateRequestDigest = candidate.requestDigest;
  receipt.adapterRequestDigest = digest({ state: candidate.providerRequest.state, questions: candidate.providerRequest.questions });
  receipt.responseDigest = digest({ synthetic: id });
  receipt.consumptionRecord = { ...receipt.consumptionRecord, candidateDigest: digest(candidate), requestDigest: candidate.requestDigest, leaseId: `fake-${id}` };
  for (const [signal, judgment] of Object.entries(receipt.judgments)) {
    judgment.instructions = candidate.providerRequest.questions[signal].instructions;
    if (judgment.primitive === 'noul') judgment.answer.noul = 0.2;
  }
  const packet = createBlindedReviewPacket({ candidate, caseId: id, sampleClass });
  const primary = createHumanLabelTemplate(packet);
  const secondary = createSecondaryLabelV2(packet);
  for (const [index, label] of [primary, secondary].entries()) {
    label.reviewerPseudonym = `fake-reviewer-${index}`; label.labeledAt = '2026-09-01T00:00:00Z';
    for (const entry of Object.values(label.noulTruth)) { entry.truth = false; entry.reviewerConfidence = 0.8; if ('disposition' in entry) entry.disposition = 'ASSERTED'; }
  }
  const records = Object.fromEntries(Object.entries({ candidate, request: candidate.providerRequest, receipt, packet, primary, secondary }).map(([k, v]) => [k, record(v)]));
  records.caseRecord = record(createCorpusCase({ candidate, receipt, caseId: id, sampleClass, receiptRawSha256: records.receipt.rawSha256 }));
  records.evidence_declaration = { bytes: Buffer.from('SYNTHETIC DECLARATION AND SIGNATURE PLACEHOLDER; NOT A REAL HUMAN REVIEW'), value: null };
  records.evidence_declaration.rawSha256 = rawHash(records.evidence_declaration.bytes);
  records.source = { bytes: Buffer.from(`SYNTHETIC ORIGINAL SOURCE ${source}`), value: null };
  records.source.rawSha256 = rawHash(records.source.bytes);
  records.sourceGrouping = record({ schema: schema('source-grouping'), caseId: id, candidateDigest: digest(candidate), sourceRawSha256: records.source.rawSha256, groupId: source, independenceStatus: 'DECLARED_SOURCE_GROUP', evidenceRefs: ['evidence_declaration'] });
  const bundle = { records, missing: [], manifestRawSha256: digest({ syntheticManifest: id }) };
  setProvenance(bundle, 'primary', 'HUMAN_UNASSISTED', 'fake-human-1');
  setProvenance(bundle, 'secondary', origin, 'fake-human-2');
  return bundle;
}
export function setProvenance(bundle, id, origin, reviewerId) {
  const label = bundle.records[id];
  const refs = ['evidence_declaration'];
  bundle.records[`evidence_${id}_signoff`] = record({ schema: schema('declared-signoff'), subjectRawSha256: label.rawSha256, signerId: reviewerId, status: 'COMPLETED' });
  bundle.records[`${id}Provenance`] = record({ schema: schema('review-provenance'), caseId: label.value.caseId, labelRawSha256: label.rawSha256, role: label.value.labelKind, origin: { classification: origin, basis: 'REVIEWER_DECLARATION', evidenceRefs: refs }, aiAssistance: { status: origin === 'HUMAN_UNASSISTED' ? 'NOT_USED_DECLARED' : 'USED', evidenceRefs: refs }, humanExposure: { status: 'BLINDED_DECLARED', evidenceRefs: refs }, assistingSessionExposure: { status: origin === 'HUMAN_UNASSISTED' ? 'NOT_APPLICABLE' : 'BLINDED_DECLARED', evidenceRefs: refs }, reviewer: { id: reviewerId, separationStatus: 'DECLARED_DISTINCT', evidenceRefs: refs }, completion: { status: 'COMPLETED', kind: 'ORIGINAL_REVIEW', signatureRef: `evidence_${id}_signoff`, evidenceRefs: refs } });
}
export function rebindLabel(bundle, id) {
  bundle.records[id] = record(bundle.records[id].value);
  if (bundle.records[`${id}Provenance`]) {
    bundle.records[`${id}Provenance`].value.labelRawSha256 = bundle.records[id].rawSha256;
    bundle.records[`${id}Provenance`] = record(bundle.records[`${id}Provenance`].value);
    const ref = bundle.records[`${id}Provenance`].value.completion.signatureRef;
    if (bundle.records[ref]) { bundle.records[ref].value.subjectRawSha256 = bundle.records[id].rawSha256; bundle.records[ref] = record(bundle.records[ref].value); }
  }
}
// Only a synthetic input fixture supplies a finished event; production code never manufactures one.
export function completedFixture(options = {}) {
  const b = fixture(options), r = b.records;
  const label = structuredClone(r.secondary.value); label.labelKind = 'HUMAN_ADJUDICATED'; label.reviewerPseudonym = 'fake-human-3';
  r.adjudicated = record(label); setProvenance(b, 'adjudicated', 'HUMAN_UNASSISTED', 'fake-human-3');
  r.policy = record({ schema: schema('reference-policy'), policyId: 'SYNTHETIC-PREDECLARED-ONLY', status: 'PREDECLARED', allowedOrigins: ['HUMAN_UNASSISTED', 'HUMAN_AI_ASSISTED'], priorToReviewEvidenceRefs: ['evidence_declaration'] });
  r.adjudication = record({ schema: schema('adjudication-event'), caseId: r.packet.value.caseId, packetDigest: digest(r.packet.value), primaryRawSha256: r.primary.rawSha256, secondaryRawSha256: r.secondary.rawSha256, primaryProvenanceRawSha256: r.primaryProvenance.rawSha256, secondaryProvenanceRawSha256: r.secondaryProvenance.rawSha256, adjudicatedRawSha256: r.adjudicated.rawSha256, provenanceRawSha256: r.adjudicatedProvenance.rawSha256, policyRawSha256: r.policy.rawSha256, adjudicatorId: 'fake-human-3', exposure: 'BLINDED_DECLARED', status: 'COMPLETED', decisionBasis: 'HUMAN_EVIDENCE_ONLY', evidenceRefs: ['evidence_declaration'], signatureRef: 'evidence_event_signoff' });
  r.evidence_event_signoff = record({ schema: schema('declared-signoff'), subjectRawSha256: r.adjudication.rawSha256, signerId: 'fake-human-3', status: 'COMPLETED' });
  return b;
}
export async function writeFixture(dir, bundle, { humanOnly = false, bom = false } = {}) {
  await fsp.mkdir(dir);
  const artifacts = {};
  for (const [id, r] of Object.entries(bundle.records)) {
    if (humanOnly && !['packet', 'primary', 'secondary'].includes(id)) continue;
    const bytes = bom && id === 'packet' ? Buffer.concat([Buffer.from('\uFEFF'), r.bytes]) : r.bytes;
    await fsp.writeFile(path.join(dir, `${id}.json`), bytes, { flag: 'wx' });
    artifacts[id] = { path: `${id}.json`, rawSha256: rawHash(bytes) };
  }
  const file = path.join(dir, 'inputs.json');
  await fsp.writeFile(file, JSON.stringify({ schema: schema(humanOnly ? 'human-inputs' : 'reference-inputs'), artifacts }), { flag: 'wx' });
  return file;
}
