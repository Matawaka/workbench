import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createShadowSendLeaseTemplate } from "../src/one-shot-send.js";

const candidatePath = process.argv[2];
if (!candidatePath) throw new Error("Usage: node examples/lease-template.mjs <externalization-candidate.json>");
const candidate = JSON.parse(await fs.readFile(candidatePath, "utf8"));
const template = createShadowSendLeaseTemplate(candidate);
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
await fs.mkdir(path.join(root, "artifacts"), { recursive: true });
const stamp = new Date().toISOString().replace(/[:.]/g, "-");
const out = path.join(root, "artifacts", `jev-shadow-send-lease-template-${stamp}.json`);
await fs.writeFile(out, `${JSON.stringify(template, null, 2)}\n`, "utf8");
console.log(JSON.stringify({ file: out, schema: template.schema, operatorConfirmed: false, providerInvocationAuthorized: false, networkSendAuthorized: false }, null, 2));
