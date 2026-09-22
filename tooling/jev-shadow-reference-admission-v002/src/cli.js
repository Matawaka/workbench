import { loadBundle, writePrivateOutput } from './private-io.js';
import { preflight, prepareHumanOnly } from './bridge.js';
import { diagnosticScore } from './score.js';
import { check, oneOf } from './common.js';

async function main(args) {
  const command = args.shift();
  oneOf(command, ['preflight', 'prepare-human-only-adjudication', 'diagnostic-score']);
  const manifests = []; let outputDir;
  while (args.length) {
    const flag = args.shift(), value = args.shift();
    check(value && !value.startsWith('--'), 'FLAG_VALUE_REQUIRED');
    if (flag === '--manifest') manifests.push(value);
    else if (flag === '--out' && !outputDir) outputDir = value;
    else throw new Error('UNKNOWN_OR_REPEATED_FLAG');
  }
  check(outputDir && manifests.length && (command === 'diagnostic-score' || manifests.length === 1), 'USAGE_REQUIRES_MANIFEST_AND_NEW_PRIVATE_OUT');
  const bundles = [];
  for (const manifest of manifests) bundles.push(await loadBundle(manifest, { humanOnly: command === 'prepare-human-only-adjudication' }));
  let documents, status;
  if (command === 'prepare-human-only-adjudication') { documents = prepareHumanOnly(bundles[0]); status = 'UNFILLED_HUMAN_DRAFT_CREATED'; }
  else if (command === 'preflight') { const report = preflight(bundles[0]); documents = { 'preflight.json': report }; status = report.status; }
  else { const report = diagnosticScore(bundles); documents = { 'diagnostic-score.json': report }; status = report.status; }
  // Preserve exact input-manifest hashes; do not serialize local source paths or original bytes.
  documents['input-manifests.json'] = { rawSha256: bundles.map(b => b.manifestRawSha256) };
  const output = await writePrivateOutput(outputDir, documents);
  console.log(JSON.stringify({ status, ...output }));
  if (status === 'PRIVATE_INPUTS_UNAVAILABLE') process.exitCode = 2;
}

main(process.argv.slice(2)).catch(error => {
  // Errors may contain private values/paths in predecessor validators. Keep stderr generic.
  console.error(JSON.stringify({ status: 'REJECTED', reason: /^[A-Z][A-Z0-9_:-]+$/.test(error.message) ? error.message : 'INPUT_VALIDATION_OR_IO_FAILURE', ioCode: error.code ?? null }));
  process.exitCode = 1;
});
