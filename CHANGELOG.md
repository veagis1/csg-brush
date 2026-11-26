# Changelog

## 0.6.1

- Rebuilding a 0.6.0 prefab repairs a missing script reference on its generated trigger collider. Existing scene
  instances keep their overrides.
- The Scene view can use the surface under the pointer as a drawing grid. Lock the grid to a face to keep drawing past
  its edge. The grid fades around the starting point. Drawing into a solid or a floor plan wall makes a cut there.
  Use **+ add** or **- subtract** to choose the operation for the current editor session.
- Click a brush dimension or a straight floor plan wall's length to enter an exact measurement in metres. The
  outline previews the change, Enter applies it, and Esc cancels. In Shape Edit, Ctrl moves eligible handles by
  increments of the grid step from their starting positions.
- New brushes drawn in the Scene view start with their origin at the first drawing point. Set or reset the working
  coordinate system (WCS) of an editable brush, floor plan, spline or wall-anchored window. **Move to** places a
  selected object at a vertex or along an edge and can move a window between floor plan walls while keeping its sill height.
- The CSG Brush overlay groups snapping and WCS controls and provides Move, Rotate and Scale buttons. The Scene view
  handles Unity's W, E and R shortcuts for those tools. Switch directly between Create and Shape Edit, Edit Brush,
  floor plan editing or spline editing. Switching cancels the active preview or unfinished drag and preserves work
  already confirmed. Creation uses the Hierarchy's Default Parent. GameObject commands are under **CSG Brush**.
- The **Δ** toggle shows signed displacement in metres during Move and signed angles in degrees during Rotate.
  Labels measure from the start of each drag and disappear on release. Move can show one axis, a pair of axes for a
  plane handle, or all three world components with Shift. Rotate tracks the current Global or Local axis through full turns.
- Turning on grid snapping preserves a freely placed brush. The next translation snaps only the changed coordinates
  of the current world or surface grid, leaving the size and other coordinates intact. **Shape** snapping prefers the
  brush boundary at the WCS on each moved axis, including a positive-side boundary. **WCS origin** (`Pivot`) snaps
  the origin itself.
  The first Move after reopening the scene uses the saved position for snapping.
- Choose English or Russian in **Edit** > **Project Settings** > **CSG Brush**. English is the default. The choice is
  saved per editor user.
- Fixed: moving brushes with cuts far from the world origin now keeps their meshes and colliders aligned. Editing
  heavily cut brushes reuses matching collider pieces. Suitable large meshes use a faster planar lightmap unwrap.
  Generated trigger relays can be saved with their collider pieces and restored when the scene is reopened.
- On upgrade, stored grid and creation defaults convert to metres, the same units already used by brush geometry.
  The old unit presets are removed. Interactive drawing starts in automatic mode rather than taking the old project
  operation default. Existing brushes keep their operations. See the [fork manual](Documentation~/fork-0.6.1.md)
  and [scripting notes](Documentation~/scripting.md#upgrade-from-060).

[Upstream changelog through 0.6.0](https://github.com/jaroslavstehlik/csg-brush/blob/71ab1dd51140fa07030dfbe36012047289aa233f/CHANGELOG.md)
