import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const sourceExtensions = new Set(['.cs', '.csproj', '.props', '.targets', '.xaml', '.vb', '.fs', '.fsproj', '.vbproj', '.js', '.mjs', '.cjs', '.jsx', '.ts', '.tsx', '.ps1', '.psm1', '.psd1', '.sh', '.bash', '.cmd', '.bat', '.py']);
const generatedDirectories = new Set(['bin', 'obj', 'node_modules', '.git']);

// A lexical regression guard, not a network sandbox or a transitive dependency audit.
// Comments and string literals in source are scanned too. Policy data and Markdown
// are not source; the guard implementation and its tests are scanned like other code.
export async function scanOfflineSources(root, tokens) {
  if (!Array.isArray(tokens) || tokens.length === 0 || tokens.some(t => typeof t !== 'string' || !t.trim())) throw new Error('INVALID_GUARD_POLICY');
  const findings = [];
  const scanned = [];
  async function walk(directory) {
    const entries = await fs.readdir(directory, { withFileTypes: true });
    entries.sort((a, b) => a.name.localeCompare(b.name, 'en'));
    for (const entry of entries) {
      const full = path.join(directory, entry.name);
      if (entry.isSymbolicLink()) throw new Error('SOURCE_LINK_REFUSED');
      if (entry.isDirectory()) {
        if (!generatedDirectories.has(entry.name.toLowerCase())) await walk(full);
      } else if (entry.isFile() && (sourceExtensions.has(path.extname(entry.name).toLowerCase()) || entry.name.toLowerCase() === 'package.json')) {
        const relative = path.relative(root, full).split(path.sep).join('/');
        const lines = (await fs.readFile(full, 'utf8')).split(/\r?\n/);
        scanned.push(relative);
        for (let i = 0; i < lines.length; i++) {
          for (const token of tokens) if (lines[i].includes(token)) findings.push({ path: relative, line: i + 1, token });
        }
      }
    }
  }
  await walk(path.resolve(root));
  if (scanned.length === 0) throw new Error('NO_SOURCE_FILES');
  return { scanned, findings };
}

export async function loadPolicy() {
  const policy = JSON.parse(await fs.readFile(path.join(here, 'offline-source-policy.json'), 'utf8'));
  if (policy.schema !== 'matawaka.jev-lab-source-guard/v0.1') throw new Error('INVALID_GUARD_SCHEMA');
  return policy.forbiddenTokens;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv.length > 3) throw new Error('ONE_OPTIONAL_SOURCE_ROOT_REQUIRED');
    const result = await scanOfflineSources(process.argv[2] ?? path.resolve(here, '..'), await loadPolicy());
    for (const finding of result.findings) console.error(`${finding.path}:${finding.line}: forbidden source token ${finding.token}`);
    console.log(`Offline source guard: ${result.scanned.length} source files; ${result.findings.length} findings`);
    process.exitCode = result.findings.length === 0 ? 0 : 1;
  } catch (error) {
    console.error(`Offline source guard failed: ${error.code ?? error.message}`);
    process.exitCode = 1;
  }
}
