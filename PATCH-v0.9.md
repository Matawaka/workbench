# Matawaka Workbench v0.9

Workbench-local GUI checkpoint acceptance over the accepted v0.8 automated acceptance matrix.

Adds an explicit **Принять** button that is enabled only after a passing Self-test in the current process. The button previews the exact Workbench working-tree changes and, after a separate confirmation dialog, performs only fixed local Git operations in `K:\Matawaka\Workbench`:

- `git add -A -- .`;
- one fixed v0.9 commit;
- one fixed annotated `workbench-v0.9-accepted` tag.

The operation does **not** push/fetch, create/update remotes, mutate Matawaka catalog repositories, enable agent Execute, create ActionPermit/materialization authority, or accept executable/command paths from JSON.

`Passing Self-test != Authority to checkpoint`

`Explicit checkpoint confirmation != Agent Execute`

`Local Git checkpoint != Remote publication`
