# Security Boundary — Workbench v0.7

## Authority

- Observe / Propose: read-only only.
- Execute: denied before evidence collection or semantic-host launch.
- Mutation budget: 0.
- Network access requested/granted: false.
- Arbitrary process execution requested/granted: false.

## Fixed semantic child

The only semantic executable is the build-bound `Matawaka.Workbench.SemanticHost.exe`.

Parent-side launch sequence:

1. verify fixed executable SHA-256;
2. create a restricted primary token using `CreateRestrictedToken(DISABLE_MAX_PRIVILEGE)`;
3. lower token integrity to Low (`S-1-16-4096`);
4. create the process suspended;
5. assign Windows Job Object limits;
6. resume the primary thread;
7. receive one child runtime-security attestation line;
8. verify user SID, filtered-token evidence, Low integrity, Job membership, no AppContainer claim, and no enabled privilege beyond `SeChangeNotifyPrivilege`;
9. only then send the sanitized semantic evidence packet on stdin;
10. verify provider/input/output digests in the parent.

## Job limits

- active process limit: 1;
- per-process committed memory: 256 MiB;
- kill on Job close: enabled;
- breakaway: disabled.

## Explicit non-claims

- runtime attestation != OS sandbox;
- restricted token != AppContainer;
- Low integrity != network isolation;
- Job Object containment != filesystem namespace isolation;
- same user identity != same security context;
- binary integrity != provider authority.

The child runs under the same Windows user identity with a reduced security context. OS network isolation remains false in v0.7.
