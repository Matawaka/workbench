import { preflight } from './bridge.js';
import { check, digest, schema, boundary } from './common.js';

function ece(rows) {
  let result = 0;
  for (let bin = 0; bin < 10; bin++) {
    const items = rows.filter(x => Math.min(9, Math.floor(x.p * 10)) === bin);
    if (items.length) result += items.length / rows.length * Math.abs(items.reduce((s, x) => s + x.p - Number(x.truth), 0) / items.length);
  }
  return result;
}

export function diagnosticScore(bundles, { minEceCases = 30 } = {}) {
  check(Array.isArray(bundles) && bundles.length > 0, 'BUNDLES_REQUIRED');
  check(Number.isSafeInteger(minEceCases) && minEceCases >= 1, 'INVALID_ECE_MINIMUM');
  const audits = bundles.map(preflight);
  check(audits.every(a => a.status !== 'PRIVATE_INPUTS_UNAVAILABLE'), 'PRIVATE_INPUTS_UNAVAILABLE');
  // Union both original-byte identity and declared source group, transitively, across cohorts.
  const parents = bundles.map((_, i) => i);
  const find = i => parents[i] === i ? i : (parents[i] = find(parents[i]));
  const seen = new Map();
  audits.forEach((a, i) => {
    const tokens = [`packet:${bundles[i].records.packet.value.candidateDigest}`, ...(a.grouping ? [`source:${a.grouping.sourceRawSha256}`, `group:${a.grouping.groupId}`] : [])];
    for (const token of tokens) { if (seen.has(token)) parents[find(i)] = find(seen.get(token)); else seen.set(token, i); }
  });
  const cohorts = new Map(), exclusions = [];
  audits.forEach((audit, index) => {
    const packet = bundles[index].records.packet.value, receipt = bundles[index].records.receipt.value;
    for (const review of audit.reviews) {
      for (const [signal, entry] of Object.entries(review.entries)) {
        if (review.migration || entry.disposition !== 'ASSERTED') {
          exclusions.push({ caseId: packet.caseId, role: review.sourceRole, signal, reason: review.migration ? 'MIGRATION_IS_NOT_INDEPENDENT_REVIEW' : entry.exclusionReason });
          continue;
        }
        const cohort = { sampleClass: packet.sampleClass, provider: receipt.provider, requestedModel: receipt.requestedModel, observedModel: receipt.observedModel, rubricDigest: digest({ questions: packet.questions, questionUse: packet.questionUse }), role: review.sourceRole, origin: review.origin, humanExposure: review.provenance?.humanExposure.status ?? 'UNKNOWN', assistingSessionExposure: review.provenance?.assistingSessionExposure.status ?? 'UNKNOWN', completion: review.provenance?.completion.status ?? 'UNKNOWN', referenceAdmission: audit.referenceAdmission, signal, use: entry.researchOnly ? 'RESEARCH_ONLY' : 'SHADOW_DIAGNOSTIC' };
        const key = digest(cohort);
        if (!cohorts.has(key)) cohorts.set(key, { cohort, observations: [] });
        cohorts.get(key).observations.push({ caseId: packet.caseId, sourceGroup: find(index), established: audit.grouping?.establishedAtDeclarationLevel === true, p: receipt.judgments[signal].answer.noul, truth: entry.truth });
      }
    }
  });
  const summaries = [...cohorts.values()].map(({ cohort, observations }) => {
    const groups = new Map();
    for (const observation of observations) {
      if (!groups.has(observation.sourceGroup)) groups.set(observation.sourceGroup, []);
      groups.get(observation.sourceGroup).push(observation);
    }
    const rows = [];
    let conflicts = 0;
    for (const values of groups.values()) {
      if (new Set(values.map(x => x.truth)).size !== 1) {
        conflicts++;
        exclusions.push({ caseIds: [...new Set(values.map(x => x.caseId))], role: cohort.role, signal: cohort.signal, reason: 'CONFLICTING_TRUTH_WITHIN_SOURCE_GROUP' });
      } else rows.push({ p: values.reduce((s, x) => s + x.p, 0) / values.length, truth: values[0].truth });
    }
    const independenceDeclared = observations.every(x => x.established);
    const eceStatus = conflicts ? 'SOURCE_GROUP_TRUTH_CONFLICT' : !independenceDeclared ? 'SOURCE_INDEPENDENCE_NOT_ESTABLISHED' : rows.length < minEceCases ? `INSUFFICIENT_SOURCE_GROUPS_MIN_${minEceCases}` : 'EXPLORATORY_DIAGNOSTIC_ONLY';
    return { ...cohort, observations: observations.length, sourceGroups: groups.size, independentCasesAtDeclarationLevel: independenceDeclared ? rows.length : 0, scoredGroups: rows.length, repeatAggregation: 'MEAN_PROBABILITY_PER_SOURCE_GROUP_EQUAL_GROUP_WEIGHT', brierMean: rows.length ? rows.reduce((s, x) => s + (x.p - Number(x.truth)) ** 2, 0) / rows.length : null, directionalMatches: rows.filter(x => x.p !== 0.5 && (x.p > 0.5) === x.truth).length, boundaryTies: rows.filter(x => x.p === 0.5).length, ece: eceStatus === 'EXPLORATORY_DIAGNOSTIC_ONLY' ? ece(rows) : null, eceStatus };
  });
  return { schema: schema('reference-diagnostic-score'), ...boundary, status: 'PRIVATE_POST_MODEL_DIAGNOSTIC_NOT_BLINDED_REFERENCE', minEceCases, eceMinimumMeaning: 'EXPLORATORY_DEFAULT_NOT_SAMPLE_SUFFICIENCY_PROOF', pooledEce: null, pooledEceReason: 'SIGNALS_MODELS_RUBRICS_ROLES_ORIGINS_POPULATIONS_NOT_POOLED', cohorts: summaries, exclusions, admissions: audits.map(a => ({ caseId: a.caseId, referenceAdmission: a.referenceAdmission, reasons: a.reasons })) };
}
