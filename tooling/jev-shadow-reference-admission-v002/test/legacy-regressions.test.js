import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createBlindedReviewPacket, createCorpusCase, createHumanLabelTemplate, scoreCorpus } from '../../jev-shadow-evidence-corpus-v001/src/corpus.js';
import { createSecondaryLabelV2 } from '../../jev-shadow-label-semantics-v002/src/label-v2.js';

// Public synthetic control only. These tests document predecessor defects; no edits to it.
const read = name => JSON.parse(fs.readFileSync(new URL(`../../jev-shadow-evidence-corpus-v001/evidence/synthetic-control-001/${name}.json`, import.meta.url), 'utf8').replace(/^\uFEFF/, ''));
test('reproduce legacy pooled ECE: five cases times six questions passes a gate of 30', () => {
  const candidate = read('candidate'), receipt = read('receipt');
  const cases = [], packets = [], labels = [];
  for (let i = 0; i < 5; i++) {
    const args = { candidate, receipt, caseId: `synthetic-${i}`, sampleClass: 'SYNTHETIC_CONTROL' };
    const packet = createBlindedReviewPacket(args);
    const label = createHumanLabelTemplate(packet, { labelKind: 'SYNTHETIC_ORACLE' });
    for (const entry of Object.values(label.noulTruth)) entry.truth = true;
    cases.push(createCorpusCase(args)); packets.push(packet); labels.push(label);
  }
  const result = scoreCorpus({ cases, packets, labels }).syntheticControl;
  assert.equal(result.independentCases, 5);
  assert.equal(result.allSignals.n, 30);
  assert.equal(result.allSignals.eceStatus, 'COMPUTED');
});
test('reproduce legacy secondary accepting a fake packet self-digest', () => {
  const packet = createBlindedReviewPacket({ candidate: read('candidate'), caseId: 'synthetic', sampleClass: 'SYNTHETIC_CONTROL' });
  packet.reviewPacketDigest = 'sha256:fake';
  assert.equal(createSecondaryLabelV2(packet).reviewPacketDigest, 'sha256:fake');
});
