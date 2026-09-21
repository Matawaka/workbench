import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { JevHttpProvider } from "../../jev-system-one-judgment-adapter/src/providers/jev-http-provider.js";
import { executeShadowSendOnce, FileOneShotLeaseStore, validateShadowSendLease } from "../src/one-shot-send.js";

const [candidatePath, leasePath, storeRootArg] = process.argv.slice(2);
if (!candidatePath || !leasePath || !storeRootArg) {
  throw new Error("Usage: node examples/send-once.mjs <candidate.json> <activated-lease.json> <consumption-store-dir>");
}
async function readJsonFile(file) {
  const text = await fs.readFile(file, "utf8");
  return JSON.parse(text.replace(/^\uFEFF/, ""));
}

const candidate = await readJsonFile(candidatePath);
const lease = await readJsonFile(leasePath);
validateShadowSendLease(candidate, lease);

const provider = new JevHttpProvider({ model: candidate.requestedModel });
const store = new FileOneShotLeaseStore(storeRootArg);
const receipt = await executeShadowSendOnce({ candidate, lease, provider, store });

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
await fs.mkdir(path.join(root, "artifacts"), { recursive: true });
const stamp = new Date().toISOString().replace(/[:.]/g, "-");
const out = path.join(root, "artifacts", `jev-shadow-send-receipt-${stamp}.json`);
await fs.writeFile(out, `${JSON.stringify(receipt, null, 2)}\n`, "utf8");
console.log(JSON.stringify({
  file: out,
  schema: receipt.schema,
  status: receipt.status,
  leaseConsumed: receipt.leaseConsumed,
  requestedModel: receipt.requestedModel,
  observedModel: receipt.observedModel,
  providerRequestId: receipt.providerRequestId,
  workbenchReadbackAuthorized: receipt.workbenchReadbackAuthorized,
  authorityCreated: receipt.authorityCreated
}, null, 2));
