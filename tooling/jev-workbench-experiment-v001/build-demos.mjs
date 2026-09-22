import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { fixture, record } from '../jev-shadow-reference-admission-v002/test/fixtures.js';
import { digest, rawHash } from '../jev-shadow-reference-admission-v002/src/common.js';

// Deterministic synthetic UI inputs. No network or real case data, no human labels written.
const demos = {
  'read-only': { context: { declaredIntent: 'Inspect a staging service health report.', requestedOperation: 'Read the existing service health report from the workspace.', presentedScope: 'Read an existing workspace report without changing files.', resourceClass: 'synthetic workspace document' }, p: [0.94, 0.74, 0.08, 0.03, 0.45, 0.02] },
  'scope-expansion': { context: { declaredIntent: 'Inspect the health of a staging service.', requestedOperation: 'Read the health report and remove an old isolated test namespace.', presentedScope: 'Read-only staging service health inspection.', resourceClass: 'synthetic orchestration namespace' }, p: [0.35, 0.81, 0.93, 0.88, 0.22, 0.05] },
};
const root = path.dirname(fileURLToPath(import.meta.url));
for (const [id, demo] of Object.entries(demos)) {
  const { records } = fixture({ id: `demo-${id}` });
  const candidate = records.candidate.value, receipt = records.receipt.value;
  candidate.providerRequest.state = demo.context;
  candidate.requestDigest = digest(candidate.providerRequest);
  receipt.candidateDigest = digest(candidate);
  receipt.candidateRequestDigest = candidate.requestDigest;
  receipt.adapterRequestDigest = digest({ state: demo.context, questions: candidate.providerRequest.questions });
  receipt.consumptionRecord.candidateDigest = receipt.candidateDigest;
  receipt.consumptionRecord.requestDigest = candidate.requestDigest;
  let index = 0;
  for (const j of Object.values(receipt.judgments)) if (j.primitive === 'noul') j.answer.noul = demo.p[index++];
  const out = path.join(root, 'fixtures', id);
  await fs.mkdir(out, { recursive: true });
  const artifacts = {};
  for (const [kind, value] of Object.entries({ candidate, receipt })) {
    const bytes = Buffer.from(`${JSON.stringify(value, null, 2)}\n`);
    await fs.writeFile(path.join(out, `${kind}.json`), bytes, { flag: 'wx' });
    artifacts[kind] = { path: `${kind}.json`, rawSha256: rawHash(bytes) };
  }
  await fs.writeFile(path.join(out, 'inputs.json'), `${JSON.stringify({ schema: 'matawaka.jev-lab-inputs/v0.1', caseId: `demo-${id}`, sampleClass: 'SYNTHETIC_CONTROL', artifacts }, null, 2)}\n`, { flag: 'wx' });
}
console.log('Created two synthetic demonstration inputs; no provider invoked.');
