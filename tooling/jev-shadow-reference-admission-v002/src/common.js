import { createHash } from 'node:crypto';
export const schema = name => `matawaka.jev-shadow-${name}/v0.2`;
export const SIGNALS = ['goalAlignment', 'operationalSpecificity', 'scopeExpansion', 'causesExternalMutation', 'hasReliableRollback', 'externalCommunication'];
export function check(ok, code) { if (!ok) throw new Error(code); }
export function object(x) { check(x !== null && typeof x === 'object' && !Array.isArray(x), 'OBJECT_REQUIRED'); }
export function keys(x, allowed, required = allowed) {
  object(x);
  check(Object.keys(x).every(k => allowed.includes(k)), 'UNKNOWN_FIELD');
  check(required.every(k => Object.hasOwn(x, k)), 'MISSING_FIELD');
}
export function text(x) { check(typeof x === 'string' && x.trim().length > 0, 'TEXT_REQUIRED'); }
export function oneOf(x, values) { check(values.includes(x), 'UNKNOWN_ENUM'); }
export function probability(x) { check(typeof x === 'number' && Number.isFinite(x) && x >= 0 && x <= 1, 'INVALID_PROBABILITY'); }
function normalize(x) {
  if (typeof x === 'number') check(Number.isFinite(x), 'NONFINITE_NUMBER');
  check(x !== undefined && typeof x !== 'function' && typeof x !== 'bigint', 'INVALID_JSON_VALUE');
  if (Array.isArray(x)) return x.map(normalize);
  if (x && typeof x === 'object') {
    check(Object.keys(x).every(k => !['__proto__', 'constructor', 'prototype'].includes(k)), 'UNSAFE_JSON_KEY');
    return Object.fromEntries(Object.keys(x).sort().map(k => [k, normalize(x[k])]));
  }
  return x;
}
export const canonical = x => JSON.stringify(normalize(x));
export const rawHash = bytes => `sha256:${createHash('sha256').update(bytes).digest('hex')}`;
export const digest = x => rawHash(canonical(x));
export function hash(x) { check(typeof x === 'string' && /^sha256:[a-f0-9]{64}$/.test(x), 'INVALID_SHA256'); }
export function equal(a, b, code) { check(canonical(a) === canonical(b), code); }
export function parse(bytes) { const x = JSON.parse(bytes.toString('utf8').replace(/^\uFEFF/, '')); canonical(x); return x; }
export const boundary = { normativeEffect: 'NONE', authorityIssuance: 'OUT_OF_SCOPE', deploymentCalibrationClaim: false, productionThresholds: null };
