# Workbench v0.44.1 — nested file double-click stabilization

## Exact local predecessor

- commit: `fbce2c3d20517e99e0752fe5ac53c5cc30f0a2af`
- local tag: `workbench-v0.44-accepted`
- real-host status: **FAIL for file double-click**; dynamic close controls PASS
- remote status: v0.44 is intentionally unpublished; remote `main` remains accepted v0.43

## Repair

v0.44 used `ItemsControl.ContainerFromElement(tree, source)` against the root `TreeView`. Nested file containers belong to descendant `TreeViewItem` item hosts, so the root-only lookup can miss the actual file node and silently return.

v0.44.1 preserves v0.44 text-read and close-tab behavior but resolves the routed source by walking the actual visual/logical ancestry to the nearest `TreeViewItem` before crossing the exact root tree.

- file double-click -> nearest nested `TreeViewItem` -> exact `AppTreeNodeV043` -> existing bounded text reader;
- directories keep normal expand/collapse behavior;
- the event is handled only after a valid file node is resolved;
- no text-read bound is weakened.

## Operator surface

The obsolete visible `Launch candidate` button is removed. The visible maintenance surface is now exactly four actions:

1. Update Workbench
2. Local apps
3. Publish accepted
4. Lifecycle receipt

A hidden `LaunchCandidateButton` compatibility binding remains only so historical code that references the named WPF control can continue to load. Hidden compatibility binding does not create launch authority.

## Admission

- semantic version: `0.44.1`
- local predecessor: `fbce2c3d20517e99e0752fe5ac53c5cc30f0a2af / workbench-v0.44-accepted`
- target tag: `workbench-v0.44.1-accepted`
- remote publication base: accepted v0.43 `77f1a7027b0f2bf2a95dbdd415c06efa231b2e22 / workbench-v0.43-accepted`
- failed `workbench-v0.44-accepted` must remain absent remotely.

Publishing v0.44.1 may place the failed v0.44 commit in remote history only as an untagged fast-forward ancestor; this does not reclassify v0.44 as a passed accepted frontier.
