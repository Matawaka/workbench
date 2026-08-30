# Workbench v0.8.1 installer/source hotfix

Fix the v0.8 acceptance harness compile failure by explicitly importing `System.IO` for `InvalidDataException` and `File`.

No acceptance semantics, semantic-provider behavior, authority policy, runtime security boundary, or UU-AAP source binding changes.
Application version remains v0.8.0; v0.8.1 is the patch/installer revision.
