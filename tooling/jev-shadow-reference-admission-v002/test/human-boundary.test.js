import test from 'node:test';
import assert from 'node:assert/strict';
import { prepareHumanOnly } from '../src/bridge.js';
import { fixture } from './fixtures.js';

function human() {
  const b = fixture();
  b.records = Object.fromEntries(['packet', 'primary', 'secondary'].map(id => [id, b.records[id]]));
  return b;
}
test('human-only packet rejects nested model probabilities in source context', () => {
  const b = human(); b.records.packet.value.context.nested = { probabilities: { yes: 0.99 } };
  assert.throws(() => prepareHumanOnly(b), /MODEL_OUTPUT_FIELD_IN_HUMAN_INPUT/);
});
test('human-only packet refuses labels declaring exposure to model output', () => {
  const b = human(); b.records.secondary.value.modelOutputVisibleToLabeler = true;
  assert.throws(() => prepareHumanOnly(b), /EXPOSED_LABEL_IN_HUMAN_INPUT/);
});
