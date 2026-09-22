import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { loadBundle, writePrivateOutput, PUBLIC_REPO } from '../src/private-io.js';
import { preflight } from '../src/bridge.js';
import { rawHash } from '../src/common.js';
import { fixture, writeFixture } from './fixtures.js';

async function temporary(t) {
  // Hosted Windows may expose its temp directory via an alias. Fixtures use the real path; the production guard still rejects aliases.
  const tempRoot = await fs.realpath(os.tmpdir());
  const dir = await fs.mkdtemp(path.join(tempRoot, 'jev-reference-synthetic-'));
  t.after(async () => { assert.ok(path.resolve(dir).startsWith(path.resolve(tempRoot) + path.sep)); await fs.rm(dir, { recursive: true, force: true }); });
  return dir;
}
test('actual byte hashes, one BOM, and source manifest preserve originals', async t => {
  const dir = await temporary(t), manifest = await writeFixture(path.join(dir, 'inputs'), fixture(), { bom: true });
  const before = await fs.readFile(manifest);
  const bundle = await loadBundle(manifest);
  assert.equal(preflight(bundle).referenceAdmission, 'HOLD');
  assert.deepEqual(await fs.readFile(manifest), before);
  assert.equal(bundle.records.packet.rawSha256, rawHash(await fs.readFile(path.join(dir, 'inputs', 'packet.json'))));
});
test('raw digest mismatch rejects even whitespace-only source changes', async t => {
  const dir = await temporary(t), manifest = await writeFixture(path.join(dir, 'inputs'), fixture());
  await fs.appendFile(path.join(dir, 'inputs', 'secondary.json'), '\n');
  await assert.rejects(loadBundle(manifest), /RAW_DIGEST_MISMATCH/);
});
test('missing files yield explicit missing-original inventory', async t => {
  const dir = await temporary(t), manifest = await writeFixture(path.join(dir, 'inputs'), fixture());
  await fs.rename(path.join(dir, 'inputs', 'request.json'), path.join(dir, 'inputs', 'request-retained.json'));
  const bundle = await loadBundle(manifest);
  assert.deepEqual(bundle.missing, ['request']);
  assert.equal(preflight(bundle).status, 'PRIVATE_INPUTS_UNAVAILABLE');
});
test('private output hashes verify and repeated run refuses overwrite', async t => {
  const dir = await temporary(t), out = path.join(dir, 'new-audit');
  const result = await writePrivateOutput(out, { 'report.json': { status: 'SYNTHETIC' } });
  const bytes = await fs.readFile(path.join(out, 'OUTPUT-MANIFEST.json'));
  assert.equal(result.manifestRawSha256, rawHash(bytes));
  const manifest = JSON.parse(bytes);
  assert.equal(manifest.entries[0].rawSha256, rawHash(await fs.readFile(path.join(out, 'report.json'))));
  await assert.rejects(writePrivateOutput(out, { 'report.json': { overwrite: true } }), { code: 'EEXIST' });
  assert.deepEqual(await fs.readFile(path.join(out, 'OUTPUT-MANIFEST.json')), bytes);
});
test('reject public repository, traversal into it, relative paths, existing directories', async t => {
  const dir = await temporary(t);
  for (const out of [PUBLIC_REPO, path.join(PUBLIC_REPO, 'private-audit'), path.join(PUBLIC_REPO, 'docs', '..', 'private-audit')]) {
    await assert.rejects(writePrivateOutput(out, {}), /PUBLIC_REPO_WRITE_REFUSED/);
  }
  await assert.rejects(writePrivateOutput('relative-out', {}), /ABSOLUTE_PATH_REQUIRED/);
  await assert.rejects(writePrivateOutput(dir, {}), { code: 'EEXIST' });
});
test('reject another Git checkout even outside the executing repo', async t => {
  const dir = await temporary(t); await fs.mkdir(path.join(dir, '.git'));
  await assert.rejects(writePrivateOutput(path.join(dir, 'out'), {}), /GIT_REPO_WRITE_REFUSED/);
});
test('reject symlink/junction output escape and linked private input ancestors', async t => {
  const dir = await temporary(t), link = path.join(dir, 'repo-link');
  await fs.symlink(PUBLIC_REPO, link, process.platform === 'win32' ? 'junction' : 'dir');
  await assert.rejects(writePrivateOutput(path.join(link, 'must-not-write'), {}), /SYMLINK_OR_JUNCTION|REALPATH_ALIAS/);
  const manifest = await writeFixture(path.join(dir, 'inputs'), fixture());
  const inputLink = path.join(dir, 'input-link');
  await fs.symlink(path.dirname(manifest), inputLink, process.platform === 'win32' ? 'junction' : 'dir');
  await assert.rejects(loadBundle(path.join(inputLink, 'inputs.json')), /SYMLINK_OR_JUNCTION|REALPATH_ALIAS/);
});
test('CLI preflight, scoring, and human-only draft operate on actual files; rerun is refused', async t => {
  const dir = await temporary(t), b = fixture();
  const manifest = await writeFixture(path.join(dir, 'inputs'), b);
  const human = await writeFixture(path.join(dir, 'human-inputs'), b, { humanOnly: true });
  const cli = fileURLToPath(new URL('../src/cli.js', import.meta.url));
  for (const command of ['preflight', 'diagnostic-score', 'prepare-human-only-adjudication']) {
    const out = path.join(dir, command);
    const args = [cli, command, '--manifest', command === 'prepare-human-only-adjudication' ? human : manifest, '--out', out];
    const result = spawnSync(process.execPath, args, { encoding: 'utf8' });
    assert.equal(result.status, 0, result.stderr);
    assert.ok(JSON.parse(result.stdout).manifestRawSha256);
    assert.equal(spawnSync(process.execPath, args, { encoding: 'utf8' }).status, 1);
  }
  const rejected = spawnSync(process.execPath, [cli, 'prepare-human-only-adjudication', '--manifest', manifest, '--out', path.join(dir, 'bad')], { encoding: 'utf8' });
  assert.equal(rejected.status, 1); assert.match(rejected.stderr, /UNKNOWN_MANIFEST_SCHEMA/);
});
test('CLI refuses unknown receipt flags without reading them', async t => {
  const dir = await temporary(t), cli = fileURLToPath(new URL('../src/cli.js', import.meta.url));
  const result = spawnSync(process.execPath, [cli, 'prepare-human-only-adjudication', '--receipt', 'not-read', '--out', path.join(dir, 'out')], { encoding: 'utf8' });
  assert.equal(result.status, 1); assert.match(result.stderr, /UNKNOWN_OR_REPEATED_FLAG/);
});
