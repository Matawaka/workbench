import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import test from 'node:test';
import { loadPolicy, scanOfflineSources } from './offline-source-guard.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const tokens = await loadPolicy();
const locations = ['core/Injected.cs', 'app/Injected.cs', 'test/Injected.cs', 'build-injected.mjs'];

async function temporarySource(action) {
  const parent = path.resolve(os.tmpdir());
  const root = await fs.mkdtemp(path.join(parent, 'jev-guard-test-'));
  try { await action(root); }
  finally {
    if (path.dirname(path.resolve(root)) !== parent || !path.basename(root).startsWith('jev-guard-test-')) throw new Error('UNSAFE_TEST_CLEANUP');
    await fs.rm(root, { recursive: true, force: true });
  }
}
async function write(root, relative, text) {
  const file = path.join(root, relative);
  await fs.mkdir(path.dirname(file), { recursive: true });
  await fs.writeFile(file, text, { flag: 'wx' });
}
function run(root) {
  return spawnSync(process.execPath, [path.join(here, 'offline-source-guard.mjs'), root], { encoding: 'utf8' });
}

for (const location of locations) {
  for (const token of tokens) {
    test(`CI guard rejects policy token in ${location}: ${token}`, async () => {
      await temporarySource(async root => {
        // These are inert text files, never imported, built or executed.
        await write(root, location, `// synthetic guard regression\n${token}\n`);
        const result = run(root);
        assert.equal(result.status, 1, result.stderr);
        assert.ok(result.stderr.includes(`${location}:2:`), result.stderr);
      });
    });
  }
}

test('project files, scripts, package scripts and guard sources are included', async () => {
  await temporarySource(async root => {
    const names = ['app/Injected.csproj', 'Directory.Build.targets', 'Directory.Build.props', 'test/check.ps1', 'test/check.sh', 'app/Window.xaml', 'checks/self.mjs', 'nested/NEW.CS', 'package.json'];
    for (const name of names) await write(root, name, tokens[0]);
    const result = await scanOfflineSources(root, tokens);
    assert.deepEqual(result.findings.map(f => f.path).sort(), names.sort());
  });
});

test('documentation, fixture data and generated directories are not source', async () => {
  await temporarySource(async root => {
    for (const name of ['README.md', 'fixtures/receipt.json', 'checks/offline-source-policy.json', 'app/bin/generated.cs', 'core/OBJ/generated.cs', 'node_modules/dependency.js']) await write(root, name, tokens.join('\n'));
    await write(root, 'app/Safe.cs', 'class Safe {}');
    const result = run(root);
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stdout, /1 source files; 0 findings/);
  });
});

test('empty tree or invalid policy cannot produce a passing guard', async () => {
  await temporarySource(async root => {
    assert.equal(run(root).status, 1);
    await assert.rejects(scanOfflineSources(root, []), /INVALID_GUARD_POLICY/);
  });
});

test('actual package passes with app, test, root helper and guard itself included', async () => {
  const result = await scanOfflineSources(path.resolve(here, '..'), tokens);
  for (const file of ['app/LabApplication.cs', 'test/Program.cs', 'build-demos.mjs', 'checks/offline-source-guard.mjs', 'checks/offline-source-guard.test.mjs']) assert.ok(result.scanned.includes(file), file);
  assert.deepEqual(result.findings, []);
});
