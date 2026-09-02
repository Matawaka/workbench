# Local App Chat Handoff

This document defines conservative handoff conventions between independent ChatGPT conversations and the accepted Workbench Local Apps flows.

Workbench does **not** automatically upload, import, copy or move another conversation's files. The operator deliberately places seed/source bytes under the fixed local roots and later chooses explicit Workbench actions.

## 1. New application seed capsule

Filename:

```text
matawaka-local-app-seed-<applicationId>-<version>.zip
```

The ZIP contains exactly one top-level directory `<applicationId>/` with ordinary installed/runtime application files only and MUST NOT contain `.matawaka-app.json`, `.matawaka-target.json` or `.matawaka-source.json`.

Operator path:

```text
verify ZIP SHA-256
-> extract <applicationId>/ under <WorkspaceRoot>/Apps/
-> Workbench -> Local apps
-> select Apps/<applicationId>
-> Register
```

Workbench registration creates `.matawaka-app.json` from actual local bytes. Initial Workbench identity is `baseline-<digest-prefix>` and is observed-byte evidence rather than an upstream/vendor version claim.

## 2. Full update candidate capsule

Filename:

```text
matawaka-local-app-candidate-<applicationId>-<targetVersion>.zip
```

The ZIP contains exactly one top-level `<applicationId>/` target tree plus `.matawaka-target.json`:

```json
{
  "Schema": "matawaka.local-app-target/v1",
  "ApplicationId": "<applicationId>",
  "TargetVersion": "<targetVersion>"
}
```

Extract under `<WorkspaceRoot>/AppCandidates/<applicationId>` and use `Local apps -> Build update package`. The builder derives Add/Replace/NoOp against the actual registered app and writes a compact `matawaka.local-app-update-package/v1` ZIP. Omitted current files are refused because implicit Delete is unsupported.

## 3. Sparse update package from another chat

For routine development, a full candidate tree is not required if the producing chat receives a Workbench **Export update context** JSON.

That context contains no application file contents. It provides ApplicationId, current Workbench identity/version, exact `.matawaka-app.json` SHA-256, installed tree digest, and relative path/SHA-256/size/role for every installed file.

A producing chat can use those predecessor bindings to create a direct sparse ZIP:

```text
local-app-update-manifest.json
payload/.matawaka-app.json
payload/<only actual Add/Replace files>
```

Unchanged files are not copied. Every Replace uses exact `CurrentSha256` from the update context. Add uses `CurrentSha256 = null`. Absence from the update package is not Delete.

## 4. Development source seed

Installed/runtime bytes are not necessarily enough to reproduce or continue development. Source handoff is a separate role.

Filename:

```text
matawaka-local-app-source-seed-<applicationId>-<version>.zip
```

The ZIP MUST contain exactly one top-level `<applicationId>/` directory with reproducible development sources and build materials: source code, project/build files, resource sources, build instructions and toolchain metadata.

The source seed MUST NOT contain `.matawaka-source.json`; Workbench creates the binding sidecar from the actual local source bytes.

Operator path:

```text
verify ZIP SHA-256
-> extract <applicationId>/ under <WorkspaceRoot>/AppSources/
-> Workbench -> Local apps
-> select registered Apps/<applicationId>
-> Bind development source
```

Binding creates only `<WorkspaceRoot>/AppSources/<applicationId>/.matawaka-source.json`. It records role association + initial source tree evidence. Source edits after binding are expected development activity and do not mutate the installed application until a separately built/applied update package exists.

```text
Installed Bytes != Development Sources
Source Binding != Source Mutation Authority
```

## 5. PRIVATE development context for a new chat

After source binding, `Local apps -> Export PRIVATE development context` may create one local ZIP under ignored Workbench artifacts.

The capsule intentionally includes:

```text
installed/**
source/**
context/context-manifest.json
context/update-context.json
context/read-tool-contract.json
HANDOFF.md
```

It may include confidential bank statements, receipts, screenshots and other private application evidence. Workbench shows an explicit PRIVATE warning before export. Workbench does **not** upload the capsule. Attaching it to a selected chat is a later human disclosure decision.

```text
Export Context != Upload Context != Authority to Disclose
Private Context Export != Public Repository Publication
```

For large long-lived applications, future handoff may use one immutable PRIVATE base capsule plus smaller context deltas bound to the base SHA-256. That direction is not automatic upload or cloud synchronization.

## 6. Future local content-read tool contract

Workbench v0.46 defines a reusable fixed-root read primitive:

- request: `matawaka.local-app-read-request/v0.46`;
- response: `matawaka.local-app-read-response/v0.46`;
- role: exactly `installed` or `source`;
- path: ApplicationId + safe relative path only;
- reparse points refused;
- maximum chunk: 1 MiB;
- response includes full-file SHA-256/size plus Base64 chunk and strict UTF-8 text when decodable;
- file mutation/process launch/network transport are false.

v0.46 intentionally has **no external connector/network transport** for this service. A later ChatGPT/Workbench tool adapter should invoke this local primitive rather than receiving arbitrary filesystem access.

```text
Content Read != File Mutation != Execution
Tool Contract != Transport Authority
```

## 7. Prompt to request a source seed from the creating chat

```text
The application is now installed and registered in Matawaka Workbench.
I need the reproducible DEVELOPMENT SOURCE as a separate source-role capsule.

Create:
matawaka-local-app-source-seed-<applicationId>-<version>.zip

Requirements:
- exactly one top-level <applicationId>/ folder;
- include all source code, project/build files, resource sources, build instructions and toolchain metadata needed to rebuild the executable/UI;
- do not include .matawaka-app.json, .matawaka-target.json or .matawaka-source.json;
- do not include unrelated Git history, Workbench source or other applications;
- if private runtime evidence is not required to build the program, do not duplicate it in the source seed;
- include SOURCE_HANDOFF.md explaining entry projects, build command/toolchain and which outputs correspond to the installed program;
- provide exact ZIP SHA-256, file count and uncompressed bytes;
- self-audit the archive before returning it.

Workbench will bind the source after I manually extract it under:
<WorkspaceRoot>/AppSources/<applicationId>
Do not invent the Workbench source-binding sidecar; Workbench creates it from actual local source bytes.
```

## 8. Role separation summary

```text
Apps/<ApplicationId>           = registered installed/runtime/evidence bytes
AppSources/<ApplicationId>     = development source bytes
AppCandidates/<ApplicationId>  = optional complete target candidate bytes
Workbench/artifacts/...        = local receipts/update/private-context artifacts

Seed Handoff != Registration
Source Handoff != Source Binding
Candidate Handoff != Update Package
Build Package != Update App != Launch App
Export Context != Upload Context
```
