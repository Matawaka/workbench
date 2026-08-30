# Workbench v0.1.2 hotfix

This hotfix corrects the WPF settings compilation gate by explicitly importing `System.IO` in `WorkbenchSettings.cs`.

It preserves the v0.1.1 clean-build cache invalidation and read-only Observe/Propose design.

Acceptance:
- all six projects compile from clean `bin/obj`;
- WPF publishes to `artifacts/app-v0.1.2`;
- predecessor rollback remains active on failure;
- target catalog repositories are not mutated.
