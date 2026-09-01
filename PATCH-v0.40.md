# Workbench v0.40 — One-confirmation transition bootstrap

## Predecessor

Exact accepted predecessor for this release:

- commit `d877005b2070759cf24ea4ea5f31e90545cd2bcf`
- tag `workbench-v0.39.1-accepted`

## Delta

v0.40 introduces a reusable one-shot transition bootstrap over the existing typed update, exact candidate launch and v0.39 verified handoff gates.

Normal future transition after v0.40 is accepted:

```text
Explicit Update Workbench confirmation
  -> fresh plan/materialize/staged-plan/apply-build
  -> PREPARED exact bootstrap lease
  -> automatic exact candidate launch
  -> persisted v0.39 live exact-image handoff
  -> ACTIVATED lease
  -> predecessor self-close
  -> exact successor PID first boot
  -> atomic CONSUMING claim
  -> normal Self-test
  -> Passed=true only
  -> bounded local checkpoint/tag
  -> COMPLETED_ACCEPTED
```

`Publish accepted` and `Lifecycle receipt` stay separate explicit actions.

## One-shot lease

Schema:

`matawaka.workbench-transition-bootstrap-lease/v0.40`

States:

- `PREPARED`
- `ACTIVATED`
- `CONSUMING`
- `COMPLETED_ACCEPTED`
- `FAILED_NO_RETRY`

The lease binds the exact predecessor/target transition from the verified apply/build receipt, candidate path/SHA, build receipt, launch receipt, handoff receipt and launched PID. The first successor claim is created with `FileMode.CreateNew` before automatic Self-test begins.

Any stale/mismatched/ambiguous evidence, failed Self-test or checkpoint failure creates no automatic retry authority.

## Manual paths remain

`Launch candidate` remains available as a manual fallback using the accepted v0.39 launch/handoff gate. Manual launch does not create a bootstrap lease and does not cause automatic first-boot Self-test/Accept.

Manual `Self-test` and `Accept` remain available for normal/manual recovery. Automatic Accept records a separate bootstrap authority source rather than pretending an Accept-button confirmation occurred.

## Activation boundary

The running v0.39.1 executable cannot retroactively acquire v0.40 behavior. Therefore installation `v0.39.1 -> v0.40` still requires the predecessor's existing manual Launch/Self-test/Accept sequence. The first natural real-host proof of one-confirmation bootstrap occurs when accepted v0.40 launches its next successor.

`New Successor Code != Retroactive Authority Over Predecessor Process`.

## Non-effects

- no general future launch authority;
- no reusable/persistent candidate acceptance authority;
- no automatic retry authority;
- no automatic Publish or Lifecycle;
- no force push/tag movement;
- no arbitrary executable/command authority;
- no external process kill/termination/signal;
- no catalog mutation;
- no Agent Execute or ActionPermit;
- no Stable Core or interface-registry promotion.
