# Matawaka Workbench v0.32 candidate

Windows/.NET 10 WPF control plane for bounded local Matawaka analysis, evidence/authority inspection, self-hosted maintenance, recovery and explicit accepted-source publication.

**Current accepted remote frontier remains `workbench-v0.31-accepted` until a v0.32 candidate is built locally, passes Self-test, is explicitly accepted, and is separately published.**

## Architecture

Workbench is a product/application layer, not UU-AAP Stable Core:

- `Matawaka.Workbench.App` — WPF operator surface and explicit maintenance gates;
- `Matawaka.Workbench.Runtime` — command routing and runtime composition;
- `Matawaka.Workbench.Protocol` — Workbench-local typed contracts and progress semantics;
- `Matawaka.Workbench.AgentHost` — bounded development-agent host and Windows process/security boundary;
- `Matawaka.Workbench.Engine` — reusable analytic future adapter;
- `Matawaka.Workbench.Catalog` — local Matawaka catalog inspection;
- `Matawaka.Workbench.SemanticHost` — fixed verified semantic host.

The semantic/runtime security line remains fixed and separately evidenced: restricted Low-integrity token, Windows Job Object, runtime attestation before semantic input, byte-bound SemanticHost, read-only proposal path and denied Execute acceptance control.

## Active operator responsibilities

v0.32 intentionally keeps the main toolbar focused on current reusable product responsibilities:

1. bounded local JSON/agent analysis;
2. visible evidence, authority and liveness inspection;
3. Self-test and explicit local accepted checkpoint;
4. candidate update chain;
5. maintenance recovery check / plan / execute;
6. local catalog inspect/fetch controls;
7. explicit **Publish accepted** to the fixed Workbench GitHub repository.

Completed recovery/transport/key proof milestones remain in source and Git history but no longer consume one permanent toolbar button each.

`Historical evidence UI removal != Historical evidence erasure`

## Update authority chain

A valid update never acquires downstream authority automatically:

`Valid Package != Materialization Authority != Source Apply/Build Authority != Launch Authority != Checkpoint Authority != Remote Publication Authority`

The normal candidate cycle is:

1. **Пакет обновления** — validate bounded local ZIP, exact predecessor tag/commit, file set and SHA-256s;
2. **Материализовать** — copy only verified payload bytes into ignored staging after separate confirmation;
3. **План применения** — derive exact `Add/Replace/NoOp` delta without source mutation;
4. **Применить + собрать** — after separate confirmation, apply only exact planned bytes and run fixed workspace-local `dotnet build/publish --no-restore`;
5. **Запустить candidate** — launch only exact receipt-bound executable after separate confirmation;
6. candidate **Self-test** — read-only semantic/runtime matrix plus offline v0.32 publisher-contract checks;
7. **Принять** — local fixed commit/tag `workbench-v0.32-accepted` after a passing v0.32 Self-test;
8. **Publish accepted** — separate network decision after local acceptance.

No earlier receipt silently authorizes a later effect.

## Fixed GitHub publication boundary

`Publish accepted` is a human maintenance network capability with one fixed destination only:

- remote name: `github-workbench`;
- remote URL: `https://github.com/Matawaka/workbench.git`;
- branch: `refs/heads/main`;
- accepted tag: `workbench-v0.32-accepted`.

The publisher requires a clean local repository and the exact accepted tag at HEAD. It derives local HEAD and parent from Git. The fixed remote may be added only when absent and a conflicting URL is refused.

Before main publication, remote `main` must be either the exact local parent or already the exact local HEAD. A conflicting remote accepted tag is refused. The only admitted branch update is non-force fast-forward of the exact accepted HEAD. The exact accepted tag may be published only when absent or already equal to that same HEAD. The service reads both refs back and requires exact equality, while proving local HEAD and working tree unchanged.

A retry after `main` succeeded but tag publication did not is intentionally idempotent.

```text
Accepted checkpoint != Remote publication authority
Publish button != Agent Execute
Fixed repository network authority != General network authority
Fast-forward permission != Force-push permission
Remote main update != Tag movement authority
Source publication != Catalog mutation
Source publication != Canonical UU-AAP conformance
```

The ordinary **Разрешить git fetch** catalog control is separate from **Publish accepted**. Neither implies the other.

## Status

v0.32 is a candidate-development line over exact accepted predecessor:

- predecessor commit: `532c1c8220d160321c928055139aa8f76a0dc08b`;
- predecessor tag: `workbench-v0.31-accepted`.

Until local v0.32 acceptance and explicit publication complete, the accepted/public Workbench remains v0.31.

Workbench receipts are bounded product evidence. They do not by themselves establish canonical UU-AAP conformance, Stable Core membership, real-world identity/trust, legal authority, or general execution authority.
