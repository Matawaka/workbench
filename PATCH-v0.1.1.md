# Workbench v0.1.1 build hotfix

This hotfix preserves the v0.1 functional scope and fixes patch application reliability.

- invalidates all project `bin/obj` caches before compiling a cross-project contract change;
- validates that `CatalogRepository.Branch` and the AgentHost -> Catalog project reference are present before build;
- performs a clean solution build before publish;
- restores predecessor sources and clears mixed build outputs on failure;
- keeps agent authority read-only: Observe/Propose only, zero target-repository mutations.
