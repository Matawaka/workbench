import test from 'node:test';
import assert from 'node:assert/strict';
import { preflight, prepareHumanOnly, interpretLabel, validatePacket } from '../src/bridge.js';
import { diagnosticScore } from '../src/score.js';
import { digest } from '../src/common.js';
import { fixture, completedFixture, rebindLabel, record } from './fixtures.js';

test('bridge retains v1/v2 originals, explicit interpretations, HOLD without completed adjudication', () => {
  const b = fixture({ origin: 'HUMAN_AI_ASSISTED' }); const before = JSON.stringify(b);
  const result = preflight(b);
  assert.equal(result.referenceAdmission, 'HOLD');
  assert.ok(result.reasons.includes('ADJUDICATION_PENDING'));
  assert.equal(result.reviews[0].interpretation, 'EXPLICIT_V1_BOOLEAN_OR_NULL_VIEW_NO_CONVERSION');
  assert.equal(result.reviews[1].origin, 'HUMAN_AI_ASSISTED');
  assert.equal(JSON.stringify(b), before);
});
test('separately supplied complete synthetic event meets declared research requirements only', () => {
  const result = preflight(completedFixture({ origin: 'HUMAN_AI_ASSISTED' }));
  assert.equal(result.referenceAdmission, 'DECLARED_RESEARCH_REFERENCE');
  assert.equal(result.deploymentCalibrationClaim, false);
  assert.match(result.assertionLevel, /NOT_AUTHENTICATED/);
});

for (const [name, edit] of [
  ['fake packet self-digest', b => { b.records.packet.value.reviewPacketDigest = digest('fake'); }],
  ['packet context', b => { b.records.packet.value.context.extra = 'wrong'; }],
  ['case identity', b => { b.records.caseRecord.value.caseId = 'wrong'; }],
  ['request file', b => { b.records.request.value = { ...b.records.request.value, model: 'wrong' }; }],
  ['receipt candidate digest', b => { b.records.receipt.value.candidateDigest = digest('wrong'); }],
  ['receipt request digest', b => { b.records.receipt.value.candidateRequestDigest = digest('wrong'); }],
  ['adapter request digest', b => { b.records.receipt.value.adapterRequestDigest = digest('wrong'); }],
  ['corpus predictions', b => { b.records.caseRecord.value.predictions.goalAlignment.probability = 0.9; }],
  ['label packet digest', b => { b.records.secondary.value.reviewPacketDigest = digest('wrong'); }],
  ['provenance exact frozen digest', b => { b.records.secondaryProvenance.value.labelRawSha256 = digest('wrong'); }],
  ['unknown label schema', b => { b.records.secondary.value.schema = 'unknown'; }],
  ['unknown candidate field', b => { b.records.candidate.value.extra = true; }],
  ['unknown provenance field', b => { b.records.secondaryProvenance.value.extra = true; }],
  ['unknown signal', b => { b.records.secondary.value.noulTruth.fake = b.records.secondary.value.noulTruth.goalAlignment; }],
  ['missing signal', b => { delete b.records.secondary.value.noulTruth.goalAlignment; }],
  ['mismatched judgment signal', b => { b.records.receipt.value.judgments.fake = b.records.receipt.value.judgments.goalAlignment; }],
  ['research-only demotion', b => { b.records.secondary.value.noulTruth.operationalSpecificity.researchOnly = false; }],
  ['source grouping binding', b => { b.records.sourceGrouping.value.sourceRawSha256 = digest('wrong'); }],
]) test(`reject ${name}`, () => { const b = fixture(); edit(b); assert.throws(() => preflight(b)); });

for (const value of [NaN, Infinity, -Infinity, -0.1, 1.1, '0.2', null]) {
  test(`reject invalid probability ${String(value)}`, () => { const b = fixture(); b.records.receipt.value.judgments.goalAlignment.answer.noul = value; assert.throws(() => preflight(b)); });
  test(`reject invalid confidence ${String(value)}`, () => { const b = fixture(); b.records.secondary.value.noulTruth.goalAlignment.reviewerConfidence = value; assert.throws(() => preflight(b)); });
}

for (const [name, edit, reason] of [
  ['missing provenance', b => { delete b.records.secondaryProvenance; }, 'MISSING_PROVENANCE'],
  ['unknown human exposure despite blinded assisting session', b => { b.records.secondaryProvenance.value.humanExposure.status = 'UNKNOWN'; }, 'HUMAN_EXPOSURE_UNKNOWN'],
  ['unknown assisting exposure', b => { b.records.secondaryProvenance.value.assistingSessionExposure.status = 'UNKNOWN'; b.records.secondaryProvenance.value.origin.classification = 'HUMAN_AI_ASSISTED'; b.records.secondaryProvenance.value.aiAssistance.status = 'USED'; }, 'ASSISTING_EXPOSURE_UNKNOWN'],
  ['same reviewer', b => { b.records.secondaryProvenance.value.reviewer.id = 'fake-human-1'; b.records.evidence_secondary_signoff.value.signerId = 'fake-human-1'; b.records.evidence_secondary_signoff = record(b.records.evidence_secondary_signoff.value); }, 'REVIEWER_IDENTITIES_NOT_DISTINCT'],
  ['unconfirmed distinct reviewers', b => { b.records.secondaryProvenance.value.reviewer.separationStatus = 'UNKNOWN'; }, 'SEPARATION_NOT_ESTABLISHED'],
  ['template posing as completed review', b => { b.records.secondaryProvenance.value.completion.status = 'TEMPLATE'; }, 'REVIEW_NOT_COMPLETED'],
  ['missing signature evidence', b => { b.records.secondaryProvenance.value.completion.signatureRef = 'evidence_absent'; }, 'MISSING_EVIDENCE'],
  ['migration posing as independent review', b => { b.records.secondary.value.sourceLabelKind = 'HUMAN_PRIMARY'; }, 'MIGRATION_IS_NOT_REVIEW'],
  ['no policy', b => { delete b.records.policy; }, 'MISSING_PREDECLARED'],
  ['assisted origin excluded by explicit policy', b => { b.records.policy.value.allowedOrigins = ['HUMAN_UNASSISTED']; b.records.secondaryProvenance.value.origin.classification = 'HUMAN_AI_ASSISTED'; b.records.secondaryProvenance.value.aiAssistance.status = 'USED'; }, 'ORIGIN_OUTSIDE_POLICY'],
  ['post-model adjudicator', b => { b.records.adjudication.value.exposure = 'EXPOSED'; }, 'NOT_COMPLETE_BLINDED'],
]) test(`HOLD for ${name}`, () => {
  const b = completedFixture(); edit(b); const result = preflight(b);
  assert.equal(result.referenceAdmission, 'HOLD'); assert.ok(result.reasons.some(r => r.includes(reason)), result.reasons.join(','));
});

test('adjudication event must bind exact input/output originals', () => {
  const b = completedFixture(); b.records.adjudication.value.secondaryRawSha256 = digest('wrong');
  assert.throws(() => preflight(b), /ADJUDICATION_INPUT_BINDING/);
});
test('UNDETERMINED and N/A are explicit exclusions; N/A requires asserted no mutation', () => {
  const b = fixture(), l = b.records.secondary.value;
  for (const [signal, disposition] of [['goalAlignment', 'UNDETERMINED'], ['hasReliableRollback', 'NOT_APPLICABLE']]) {
    Object.assign(l.noulTruth[signal], { disposition, truth: null, reviewerConfidence: null });
  }
  rebindLabel(b, 'secondary');
  const report = diagnosticScore([b]);
  assert.deepEqual(report.exclusions.map(x => x.reason), ['UNDETERMINED', 'NOT_APPLICABLE']);
  l.noulTruth.causesExternalMutation.truth = true;
  assert.throws(() => interpretLabel(b.records.packet.value, l, 'HUMAN_SECONDARY'), /ROLLBACK_NA/);
});
test('undetermined cannot carry false; unsupported N/A is rejected', () => {
  const b = fixture(), l = b.records.secondary.value;
  l.noulTruth.goalAlignment.disposition = 'UNDETERMINED';
  assert.throws(() => interpretLabel(b.records.packet.value, l, 'HUMAN_SECONDARY'), /EXCLUDED_REQUIRES_NULL/);
  Object.assign(l.noulTruth.goalAlignment, { disposition: 'NOT_APPLICABLE', truth: null, reviewerConfidence: null });
  assert.throws(() => interpretLabel(b.records.packet.value, l, 'HUMAN_SECONDARY'), /NA_NOT_DEFINED/);
});
test('five cases times six signals never unlock ECE=30', () => {
  const report = diagnosticScore(Array.from({ length: 5 }, (_, i) => fixture({ id: `five-${i}` })));
  assert.equal(report.pooledEce, null);
  assert.equal(report.cohorts.length, 12);
  for (const c of report.cohorts) { assert.equal(c.independentCasesAtDeclarationLevel, 5); assert.equal(c.ece, null); }
});
test('duplicate source with different case/group IDs collapses, including repeated requests', () => {
  const a = fixture({ id: 'alias1', source: 'same' }), b = fixture({ id: 'alias2', source: 'same' });
  b.records.sourceGrouping.value.groupId = 'different-alias';
  const report = diagnosticScore([a, b, a]);
  assert.ok(report.cohorts.every(c => c.observations === 3 && c.sourceGroups === 1 && c.ece === null));
});
test('declared same source group across different bytes is also one case', () => {
  const a = fixture({ id: 'a' }), b = fixture({ id: 'b' }); b.records.sourceGrouping.value.groupId = 'a';
  assert.ok(diagnosticScore([a, b]).cohorts.every(c => c.sourceGroups === 1));
});
test('missing grouping defeats ECE even when independentSample=true', () => {
  const b = fixture(); delete b.records.sourceGrouping;
  assert.ok(diagnosticScore([b], { minEceCases: 1 }).cohorts.every(c => c.ece === null && c.eceStatus === 'SOURCE_INDEPENDENCE_NOT_ESTABLISHED'));
});
test('thirty distinct source groups enable only per-signal exploratory ECE', () => {
  const report = diagnosticScore(Array.from({ length: 30 }, (_, i) => fixture({ id: `thirty-${i}` })));
  assert.ok(report.cohorts.every(c => c.independentCasesAtDeclarationLevel === 30 && Math.abs(c.ece - 0.2) < 1e-12));
  assert.ok(report.cohorts.filter(c => c.signal === 'operationalSpecificity').every(c => c.use === 'RESEARCH_ONLY'));
  assert.equal(report.productionThresholds, null);
});
test('model, rubric, assisted origin, role, synthetic/real stay in separate cohorts', () => {
  const bundles = [fixture({ id: 'a' }), fixture({ id: 'b', model: 'synthetic-model-2' }), fixture({ id: 'c', rubric: ' Different version.' }), fixture({ id: 'd', origin: 'HUMAN_AI_ASSISTED' }), fixture({ id: 'e', sampleClass: 'SANITIZED_REAL_SHADOW' })];
  const report = diagnosticScore(bundles);
  assert.equal(report.cohorts.filter(c => c.signal === 'goalAlignment' && c.role === 'HUMAN_SECONDARY').length, 5);
  assert.ok(report.cohorts.every(c => c.ece === null));
});
test('conflicting labels in one source group are excluded without choosing by model agreement', () => {
  const a = fixture({ id: 'a', source: 'same' }), b = fixture({ id: 'b', source: 'same' });
  b.records.secondary.value.noulTruth.goalAlignment.truth = true; rebindLabel(b, 'secondary');
  const report = diagnosticScore([a, b], { minEceCases: 1 });
  const c = report.cohorts.find(c => c.role === 'HUMAN_SECONDARY' && c.signal === 'goalAlignment');
  assert.equal(c.brierMean, null); assert.equal(c.ece, null);
  assert.ok(report.exclusions.some(x => x.reason === 'CONFLICTING_TRUTH_WITHIN_SOURCE_GROUP'));
});
test('human-only preparation refuses receipt and emits an unfilled draft with no predictions or freeform model notes', () => {
  const b = fixture(); assert.throws(() => prepareHumanOnly(b), /UNKNOWN_FIELD/);
  b.records.primary.value.noulTruth.goalAlignment.notes = 'Jev probability 0.12345; select matching truth';
  const human = { records: Object.fromEntries(['packet', 'primary', 'secondary'].map(id => [id, b.records[id]])), missing: [] };
  const docs = prepareHumanOnly(human), body = JSON.stringify(docs);
  assert.doesNotMatch(body, /0\.12345|jevProbability|predictions|judgments|directionalMatch|brierMean/);
  assert.equal(docs['adjudication-draft.json'].status, 'DRAFT_NOT_A_LABEL');
  assert.ok(Object.values(docs['adjudication-draft.json'].entries).every(x => x.truth === null && x.disposition === null));
  assert.doesNotMatch(body, /HUMAN_ADJUDICATED/);
});
test('correct packet self digest is recomputed over the body, never trusted', () => {
  const b = fixture(), packet = b.records.packet.value; packet.reviewPacketDigest = digest(packet);
  assert.equal(validatePacket(packet).digest, packet.reviewPacketDigest);
});
test('missing originals produce PRIVATE_INPUTS_UNAVAILABLE with HOLD', () => {
  const b = fixture(); delete b.records.request;
  const report = preflight(b); assert.equal(report.status, 'PRIVATE_INPUTS_UNAVAILABLE'); assert.equal(report.referenceAdmission, 'HOLD');
  assert.throws(() => diagnosticScore([b]), /PRIVATE_INPUTS_UNAVAILABLE/);
});
test('raw provider response is optional but a supplied original must match', () => {
  const b = fixture(); b.records.response = record({ model: 'fake' }); assert.throws(() => preflight(b), /RESPONSE_DIGEST_MISMATCH/);
});
