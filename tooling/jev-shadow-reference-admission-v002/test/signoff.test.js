import test from 'node:test';
import assert from 'node:assert/strict';
import { preflight } from '../src/bridge.js';
import { digest } from '../src/common.js';
import { completedFixture, record } from './fixtures.js';

test('a signoff for unrelated bytes cannot establish review completion', () => {
  const b = completedFixture();
  b.records.evidence_secondary_signoff.value.subjectRawSha256 = digest('unrelated');
  b.records.evidence_secondary_signoff = record(b.records.evidence_secondary_signoff.value);
  assert.throws(() => preflight(b), /SIGNOFF_BINDING_MISMATCH/);
});
test('a signoff by another reviewer cannot establish review completion', () => {
  const b = completedFixture();
  b.records.evidence_secondary_signoff.value.signerId = 'wrong-reviewer';
  b.records.evidence_secondary_signoff = record(b.records.evidence_secondary_signoff.value);
  assert.throws(() => preflight(b), /SIGNOFF_BINDING_MISMATCH/);
});
test('adjudication binds both reviewer provenance supplements', () => {
  const b = completedFixture();
  b.records.adjudication.value.secondaryProvenanceRawSha256 = digest('wrong');
  assert.throws(() => preflight(b), /ADJUDICATION_OUTPUT_BINDING_MISMATCH/);
});
test('an unsigned draft cannot be completed by labelKind alone', () => {
  const b = completedFixture();
  b.records.evidence_secondary_signoff.value.status = 'DRAFT';
  b.records.evidence_secondary_signoff = record(b.records.evidence_secondary_signoff.value);
  const result = preflight(b);
  assert.equal(result.referenceAdmission, 'HOLD');
  assert.ok(result.reasons.includes('SIGNOFF_NOT_COMPLETED'));
});
