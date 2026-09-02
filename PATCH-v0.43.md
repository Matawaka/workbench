# Workbench v0.43 — clickable Apps + read-only tree tabs

## Accepted predecessor

- commit: `2a68f43b7c8b3a5e8aae7d960f2d997ffa7ec525`
- tag: `workbench-v0.42-accepted`
- semantic predecessor: `0.42.0`

## Operator surface

v0.43 preserves the accepted v0.42 compact shell and changes only the installed-app inspection surface:

- installed `ApplicationId · Version` chips are real single-click buttons;
- clicking a registered app opens a dedicated `App · <ApplicationId>` tab inside the existing output TabControl;
- multiple app tabs may remain open simultaneously;
- clicking an already-open app refreshes/selects the existing tab instead of creating a duplicate;
- the app tab contains a read-only TreeView of the managed application directory;
- the root is expanded automatically; nested folders remain ordinary expandable TreeView nodes;
- folder/file entries are visually distinct and file entries show byte size.

## Structural observation boundary

Tree inspection is intentionally metadata-only:

- the target must already be a registered direct child returned by the accepted v0.42 installed-app observer;
- traversal is confined to the exact `<WorkspaceRoot>/Apps/<ApplicationId>` root;
- reparse roots are refused and reparse descendants are skipped rather than followed;
- depth is bounded to 64 and observed child nodes to 20,000;
- application file contents are not read; only names, attributes and `FileInfo.Length` are observed;
- incomplete access fails the selected tree observation rather than silently pretending completeness.

## Preserved behavior

- exactly five visible top-level maintenance buttons / zero persistent authority checkboxes;
- Workspace/Catalog remain hidden while internally retained;
- Find remains below output tabs;
- status remains over the bottom progress bar with v0.42 green/red/gold classification;
- accepted v0.41.2 JSON search/focus behavior remains unchanged;
- accepted v0.42 transition bootstrap, updater, Local Apps maintenance and non-App runtime behavior remain predecessor behavior.

## Lifecycle

- semantic Version: `0.43.0`
- target tag: `workbench-v0.43-accepted`
- exact parent: `2a68f43b7c8b3a5e8aae7d960f2d997ffa7ec525 / workbench-v0.42-accepted`
- Update Workbench remains one-confirmation with first-boot validation + automatic local Accept on PASS;
- Publish accepted remains explicit;
- Lifecycle receipt remains explicit.

## Non-effects

Opening/refreshing an app tree tab creates no registration/update/copy/move/delete/launch authority, reads no application file contents, performs no process/network/catalog/Agent Execute action, creates no ActionPermit, and makes no Stable Core/interface-registry promotion.
