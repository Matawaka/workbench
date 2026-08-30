# Matawaka Workbench v0.7 — Runtime Security Attestation

v0.7 preserves the accepted v0.6 restricted-token + low-integrity + Job Object boundary and adds a child-observed runtime attestation handshake **before semantic input is sent**.

The fixed `SemanticHost.exe` now observes its own effective Windows context and reports:

- user SID;
- integrity-level SID;
- `TokenHasRestrictions`;
- Job Object membership;
- AppContainer state;
- elevation state/type;
- enabled privilege names;
- whether any enabled privilege exists beyond `SeChangeNotifyPrivilege`.

The parent verifies the attestation before writing the sanitized evidence packet to stdin. A mismatch fails closed.

Invariants:

- `Launch Configuration != Runtime Security Observation`.
- `Parent Claim != Child Observation`.
- `Runtime Attestation != OS Sandbox`.
- `Low Integrity != Network Isolation`.

v0.7 still creates no execution authority, materialization authority, ActionPermit, network sandbox, AppContainer, repository mutation, arbitrary process authority, or Stable Core admission.


## Installer/runtime hotfix v0.7.3

Fixed child runtime token attestation for fixed-size TOKEN_INFORMATION_CLASS values: TokenElevation, TokenElevationType, TokenHasRestrictions and TokenIsAppContainer are now queried with direct 4-byte buffers instead of the variable-size null-buffer probe.
