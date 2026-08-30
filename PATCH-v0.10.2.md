# Matawaka Workbench v0.10.2 runtime source-set hotfix

This hotfix keeps the product version at `0.10.0` and changes only the Workbench-local relevant-source-set verifier plus this provenance note.

## Failure corrected

The v0.10 bootstrap preflight used Git's checkout-aware `hash-object` behavior, while the runtime verifier hashed raw worktree bytes directly. On a Windows checkout with CRLF representation, all five bound UU-AAP text files could therefore fail runtime verification even though Git reported the expected tracked blob identities.

## Bounded correction

The runtime verifier now:

1. hashes raw worktree bytes first;
2. if the raw blob identity does not match, permits exactly one bounded representation transform for the fixed `.js` / `.json` source set: byte-level `CRLF -> LF`;
3. hashes the resulting bytes as a Git blob and requires the pre-bound expected object identity;
4. rejects every other content, whitespace, encoding, or path difference;
5. launches no Git process and executes no Git clean filter while verifying the source set.

This is representation tolerance, not source-authority expansion. Repository HEAD equality is still not authority, provider selection is still not authority, and any non-EOL source difference remains fail-closed.
