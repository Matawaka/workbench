# Jev Shadow One-Shot Send v0.3

A separate network-capable sidecar for one exact, operator-reviewed Jev shadow request candidate.

This package is deliberately outside the no-send v0.2 candidate builder.

## Capability boundary

A send requires an explicit lease bound to:
- exact candidate digest;
- exact provider request digest;
- exact requested model;
- exact TypeSafe System One endpoint;
- expiry;
- `maxUses=1`;
- explicit operator confirmation;
- explicit provider/network-send authorization.

The lease is consumed atomically **before** provider invocation. Provider failure therefore does not make the lease reusable.

The response is shadow evidence only. `workbenchReadbackAuthorized=false`; no Workbench authority, display permit or action permit is created.

## Manual live path

1. Generate a lease template from one reviewed v0.2 candidate.
2. Review it and explicitly fill `leaseId`, set `operatorConfirmed=true`, `providerInvocationAuthorized=true`, `networkSendAuthorized=true`, and keep a short future `expiresAt`.
3. Keep `TYPESAFE_API_KEY` in the local environment only.
4. Run `npm run send:once -- <candidate> <lease> <consumption-store-dir>`.

No automatic Workbench integration is present.
