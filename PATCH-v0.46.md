# Workbench v0.46 — Local App Operational Handoff

## Predecessor

- commit: `710e3b8a98cc0f733735a7676ed7f78eee27a996`
- tag: `workbench-v0.45-accepted`

## Target

- semantic version: `0.46.0`
- accepted tag: `workbench-v0.46-accepted`

## Added registered-app actions

The existing top-level four-button Workbench surface is unchanged. Registered-app operational actions live only under `Local apps`:

- `Update from package` — existing exact Add/Replace updater;
- `Build update package` — existing full-candidate diff builder;
- `Launch app` — explicit exact `.exe` selection inside the registered app root, SHA/size preview, zero arguments, separate confirmation and local launch receipt;
- `Export update context` — content-free installed inventory with current Workbench identity/version, relative paths, SHA-256 and sizes for sparse update generation by another conversation;
- `Bind development source` — binds manually placed `Workspace/AppSources/<ApplicationId>` source bytes by creating only `.matawaka-source.json` after fresh bounded inventory;
- `Export PRIVATE development context` — separately confirmed local ZIP containing installed/private bytes, bound development source, update context, handoff manifest and read-tool contract. No upload is performed.

## Future tool-read primitive

v0.46 adds a reusable local read service/contract:

- request schema: `matawaka.local-app-read-request/v0.46`;
- response schema: `matawaka.local-app-read-response/v0.46`;
- roles: `installed` and `source`;
- fixed ApplicationId/relative-path confinement;
- reparse refusal;
- maximum 1 MiB chunk per call;
- full-file SHA-256/size evidence;
- response chunk as Base64 and strict UTF-8 text when decodable.

No connector/network transport is implemented in v0.46. The service is the local primitive a later ChatGPT/Workbench tool adapter can invoke instead of receiving arbitrary filesystem authority.

## Source handoff convention

A producing chat may return:

`matawaka-local-app-source-seed-<applicationId>-<version>.zip`

with exactly one top-level `<applicationId>/` directory containing reproducible development sources/build files. It must not contain `.matawaka-source.json`; Workbench creates that binding from the actual local source bytes after manual extraction under `Workspace/AppSources/<ApplicationId>`.

## Invariants

```text
Export Context != Upload Context != Authority to Disclose
Installed Bytes != Development Sources
Source Binding != Source Mutation Authority
Registration != Update != Launch
Content Read != File Mutation != Execution
Private Context Export != Public Repository Publication
```

Private app/source/context bytes remain outside the Workbench Git repository. `Publish accepted` publishes only accepted Workbench source.
