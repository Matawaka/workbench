# Workbench v0.44 — double-click text inspection + closable dynamic tabs

## Accepted predecessor

- commit: `77f1a7027b0f2bf2a95dbdd415c06efa231b2e22`
- tag: `workbench-v0.43-accepted`
- semantic predecessor: `0.43.0`

## Operator interaction

v0.44 extends only the accepted application-inspection surface:

- installed App chips remain single-click entry points to application tree tabs;
- application tree tabs now have an explicit `×` close control;
- double-clicking a file node attempts bounded text inspection;
- a supported text file opens in its own read-only direct-`TextBox` tab, so accepted `Find in output` behavior remains usable there;
- text tabs also have `×` close controls;
- reopening the same application tree or exact application/file path refreshes/selects the existing dynamic tab instead of creating a duplicate;
- different files/applications may remain open simultaneously;
- fixed Workbench tabs remain stable and do not acquire close buttons.

Directories retain ordinary TreeView expand/collapse behavior and do not open text tabs.

## Text inspection boundary

The new content read is explicit and bounded:

- application must already be registered and observable by accepted `InstalledAppsV042Service` / `WorkbenchAppTreeV043Service`;
- the target must be a file node represented by the current bounded application tree;
- resolved file path must remain strictly inside the exact managed application root;
- target file may not be a reparse point;
- maximum file length is 2 MiB;
- decoding accepts strict UTF-8 (BOM optional) and BOM-marked UTF-16 LE/BE;
- invalid byte sequences, NUL-bearing/binary content and oversized files are refused;
- content is displayed read-only and no write, execute, launch, copy, move, delete, process or network capability is created.

## Close semantics

- `×` removes only the selected dynamic inspection tab from the current WPF presentation;
- closing a tree tab does not close already-open file tabs;
- closing a file tab does not alter its application or on-disk file;
- closing a dynamic tab does not create rollback, mutation or application authority.

## Preserved surface

- exactly five visible top-level maintenance buttons / zero persistent authority checkboxes;
- Workspace/Catalog remain hidden while internally retained;
- Find remains below output tabs;
- status remains over the bottom progress bar with accepted green/red/gold classification;
- accepted v0.41.2 visible search selection remains predecessor behavior;
- accepted v0.43 structural app tree remains metadata-only until the operator explicitly double-clicks a file.

## Lifecycle

- semantic Version: `0.44.0`
- target tag: `workbench-v0.44-accepted`
- exact parent: `77f1a7027b0f2bf2a95dbdd415c06efa231b2e22 / workbench-v0.43-accepted`
- Update Workbench remains one-confirmation with first-boot validation + automatic local Accept on PASS;
- Publish accepted remains explicit;
- Lifecycle receipt remains explicit.

## Non-effects

No application mutation/execution authority, no arbitrary file access outside a registered managed app, no unbounded content read, no process/network/catalog/Agent Execute action, no ActionPermit, and no Stable Core/interface-registry promotion.
