# Matawaka Workbench v0.7.4 runtime hotfix

Fixes child runtime-attestation parsing for `TokenHasRestrictions` (`TOKEN_INFORMATION_CLASS=21`).

Observed Windows returns a successful one-byte boolean payload for this class, while Microsoft documentation describes a nonzero `DWORD`. The observer now accepts only the two unambiguous boolean scalar encodings:

- 1-byte BOOL/BOOLEAN-compatible value;
- 4-byte-or-larger DWORD-compatible value.

Two- or three-byte results remain fail-closed. `TokenElevation`, `TokenElevationType`, and `TokenIsAppContainer` keep their direct DWORD readers.

No authority, provider, repository, network, or execution semantics are expanded. The application remains Workbench v0.7.0; this is an installer/runtime-attestation hotfix only.
