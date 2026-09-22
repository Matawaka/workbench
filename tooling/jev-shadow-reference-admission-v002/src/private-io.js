import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { check, keys, text, hash, rawHash, parse, schema } from './common.js';

export const PUBLIC_REPO = path.resolve(fileURLToPath(new URL('../../../', import.meta.url)));
const inside = (parent, child) => { const rel = path.relative(parent, child); return rel === '' || (!rel.startsWith(`..${path.sep}`) && rel !== '..' && !path.isAbsolute(rel)); };

// Refuse every link/reparse symlink in existing ancestry, including the leaf.
// Not an adversarial filesystem transaction: operator must exclusively control the parent.
async function checkedPath(target, { exists = true } = {}) {
  check(path.isAbsolute(target), 'ABSOLUTE_PATH_REQUIRED');
  const resolved = path.resolve(target);
  const root = path.parse(resolved).root;
  let current = root;
  for (const part of path.relative(root, resolved).split(path.sep).filter(Boolean)) {
    current = path.join(current, part);
    let stat;
    try { stat = await fs.lstat(current); }
    catch (e) { if (!exists && e.code === 'ENOENT' && current === resolved) return resolved; throw e; }
    check(!stat.isSymbolicLink(), 'SYMLINK_OR_JUNCTION_REFUSED');
    // Also catches path aliases resolved differently on Windows.
    check(path.relative(current, await fs.realpath(current)) === '', 'REALPATH_ALIAS_REFUSED');
  }
  return resolved;
}

export async function loadBundle(manifestPath, { humanOnly = false } = {}) {
  const manifestFile = await checkedPath(manifestPath);
  const manifestBytes = await fs.readFile(manifestFile);
  const manifest = parse(manifestBytes);
  keys(manifest, ['schema', 'artifacts']);
  check(manifest.schema === schema(humanOnly ? 'human-inputs' : 'reference-inputs'), 'UNKNOWN_MANIFEST_SCHEMA');
  const allowed = humanOnly ? ['packet', 'primary', 'secondary'] : ['packet', 'candidate', 'request', 'receipt', 'caseRecord', 'primary', 'secondary', 'primaryProvenance', 'secondaryProvenance', 'source', 'sourceGrouping', 'policy', 'adjudicated', 'adjudicatedProvenance', 'adjudication', 'response'];
  keys(manifest.artifacts, [...allowed, ...Object.keys(manifest.artifacts).filter(k => !humanOnly && /^evidence_[a-zA-Z0-9_-]+$/.test(k))], []);
  const records = {}, missing = [];
  for (const [id, ref] of Object.entries(manifest.artifacts)) {
    keys(ref, ['path', 'rawSha256']); text(ref.path); hash(ref.rawSha256);
    // References are explicit; never glob a directory or inspect secret stores.
    const target = path.resolve(path.dirname(manifestFile), ref.path);
    try {
      await checkedPath(target);
      const bytes = await fs.readFile(target);
      check(rawHash(bytes) === ref.rawSha256, `RAW_DIGEST_MISMATCH:${id}`);
      records[id] = { bytes, rawSha256: rawHash(bytes), value: id === 'source' || id.startsWith('evidence_') ? null : parse(bytes) };
    } catch (e) {
      if (e.code === 'ENOENT') missing.push(id); else throw e;
    }
  }
  return { records, missing, manifestRawSha256: rawHash(manifestBytes) };
}

export async function writePrivateOutput(outputDir, documents) {
  check(path.isAbsolute(outputDir), 'ABSOLUTE_PATH_REQUIRED');
  const out = path.resolve(outputDir);
  check(!inside(PUBLIC_REPO, out), 'PUBLIC_REPO_WRITE_REFUSED');
  const parent = await checkedPath(path.dirname(out));
  const repo = await fs.realpath(PUBLIC_REPO);
  check(!inside(repo, parent), 'PUBLIC_REPO_WRITE_REFUSED');
  // Refuse writes into any other Git checkout, even through an innocently named parent.
  for (let dir = parent;; dir = path.dirname(dir)) {
    try { await fs.lstat(path.join(dir, '.git')); throw new Error('GIT_REPO_WRITE_REFUSED'); }
    catch (e) { if (e.code !== 'ENOENT') throw e; }
    if (path.dirname(dir) === dir) break;
  }
  await checkedPath(out, { exists: false });
  await fs.mkdir(out); // EEXIST is intentional, including an empty preexisting directory.
  const entries = [];
  for (const [name, document] of Object.entries(documents)) {
    check(/^[a-zA-Z0-9_-]+\.json$/.test(name) && name !== 'OUTPUT-MANIFEST.json', 'INVALID_OUTPUT_NAME');
    const bytes = Buffer.from(`${JSON.stringify(document, null, 2)}\n`);
    await checkedPath(out);
    await fs.writeFile(path.join(out, name), bytes, { flag: 'wx', mode: 0o600 });
    entries.push({ name, rawSha256: rawHash(bytes), bytes: bytes.length });
  }
  const bytes = Buffer.from(`${JSON.stringify({ schema: schema('private-output-manifest'), entries }, null, 2)}\n`);
  await fs.writeFile(path.join(out, 'OUTPUT-MANIFEST.json'), bytes, { flag: 'wx', mode: 0o600 });
  return { outputDir: out, manifestRawSha256: rawHash(bytes) };
}
