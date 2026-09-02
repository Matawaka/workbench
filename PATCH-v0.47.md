# Workbench v0.47 — Bounded Chat Read Relay

## Predecessor

- commit: `8ca8a66f0892050e02024880a8d4bf4a6a8ec4d2`
- tag: `workbench-v0.46-accepted`

## Target

- semantic version: `0.47.0`
- accepted tag: `workbench-v0.47-accepted`

## Added registered-app action

The four-button top-level Workbench surface is unchanged. Registered applications gain one additional action under `Local apps`:

`Chat read relay`

The operator selects the registered app first. A chat then supplies one bounded JSON request using schema:

`matawaka.local-app-chat-read-request/v0.47`

Required request fields bind:

- unique `RequestId`;
- exact selected `ApplicationId`;
- `Role = installed|source`;
- safe `RelativePath`;
- `Offset`;
- `MaxBytes <= 1 MiB`;
- optional `ExpectedFileSha256` for stale-context refusal.

## Human-gated disclosure chain

```text
Chat request JSON
-> selected-app/request validation
-> fixed-root path + whole-file SHA/size preview
-> explicit human disclosure confirmation
-> fresh SHA/size/range revalidation
-> accepted v0.46 bounded local read primitive
-> exact response JSON
-> local Windows clipboard
-> human paste into chosen chat
```

A request or preview alone does not read file contents and does not write the clipboard.

The final response schema is:

`matawaka.local-app-chat-read-response/v0.47`

It contains the request id, exact file SHA/size, returned range, EOF flag, Base64 bytes and strict UTF-8 text when available. It explicitly records `ClipboardWritePerformed=true`, `UploadPerformed=false` and `NetworkAccessPerformed=false`.

## Stale-context guard

If the target file changes after preview, or if an optional chat-supplied expected SHA does not match, Workbench refuses the read/disclosure and requires a new request/preview.

```text
Expected Hash Mismatch => Refuse, Not Guess
Preview != Durable Read Lease
```

## Invariants

```text
Chat Request != Local Read Authority
Local Read != Clipboard Disclosure
Clipboard Response != Automatic Upload
Selected App != Arbitrary Filesystem Root
Read Authority != Mutation/Execution/Network Authority
```

v0.47 adds no HTTP listener, tunnel, MCP exposure, cloud connector or automatic network transport. It proves a transport-neutral human relay before any later direct adapter is considered.
