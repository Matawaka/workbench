# Workbench v0.44 real-host double-click routing failure

The real Windows-host observation after local v0.44 activation found a presentation-routing defect:

- dynamic inspection tab close buttons work;
- application tree rendering works;
- text content service is qualified independently;
- **double-clicking a nested file node in the real TreeView does not open a text tab**.

Root cause candidate: `AppTreeV044_MouseDoubleClick` calls `ItemsControl.ContainerFromElement(tree, source)`. A nested file `TreeViewItem` belongs to a descendant `TreeViewItem` items host, not directly to the root `TreeView`, so resolving the container only against the root TreeView can return null for nested nodes. CI v0.44 did not exercise this exact routed-event/container boundary because it invoked `OpenOrRefreshAppTextTabV044` directly.

This evidence must remain FAIL for the v0.44 real-host double-click admission boundary. Do not publish v0.44 as accepted. A narrow successor should resolve the nearest ancestor `TreeViewItem` from the routed event source and add a compiled nested real-event probe.