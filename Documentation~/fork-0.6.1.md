# CSG Brush 0.6.1

CSG Brush lets you block out a level directly in Unity. A *brush* is a shape you can add to the level or use to cut a hole in another shape. Jaroslav Stehlik’s original editor already did both. The changes in this fork are easiest to see while building a small loading bay. How do you land a crate on a raised platform, cut a recess in the wall behind it, and build on a ramp that slopes away from the world grid? [Original CSG Brush 0.6.0](https://github.com/jaroslavstehlik/csg-brush/tree/71ab1dd51140fa07030dfbe36012047289aa233f)

## Land a crate on a platform

![A crate’s gizmo moves from its centre to the midpoint of its bottom edge. Move to places it on a platform and the height field changes from 2 m to 2.5 m.](images/0.6.1/wcs-move-and-resize.gif)

*The top face of the blue platform has a seam down its middle. Picking that edge’s midpoint lands the crate’s new pivot exactly at the centre of the platform. Watch the base stay in contact as the height changes.*

Start with a box created from the **GameObject** menu. Its pivot, the point the Move and Rotate tools act on, starts at the centre. To place the bottom edge precisely, open **WCS** (working coordinate system) and choose **Snap WCS to Vertex**. Move the pointer along the bottom edge. Its corners and midpoint light up in turn. Click the midpoint. The gizmo moves there while the crate stays put.

Now click **Move to**. Pick a vertex or an edge midpoint on the platform and click again to land the crate there. For an offset, move along the edge before the second click. The outline shows where the crate will land, and **Ctrl** measures the offset from the picked point in grid steps, even with the magnet off. The crate keeps its rotation and scale. The target can be another brush, an imported mesh, a posed skinned mesh, or a mesh or box collider. Flat triangulated faces expose their perimeter, so a hidden diagonal cannot catch the cursor.

A box drawn in the Scene view already starts with its pivot at the first point you clicked. You can pick a new origin or reset it to the centre later. This also works for floor plans, splines and wall-mounted windows. For a floor plan or spline, the pickable points include graph nodes, link midpoints and edges of the generated shape. **Reset WCS** returns the pivot to the centre of its local bounds. Changing the origin keeps the geometry and child objects in place. Moving the object afterwards carries its children with it.

## Change a size where you see it

Switch on **Dimensions**. The coloured X, Y and Z letters identify which way each measurement runs. Click a number on a selected editable brush and type the size in metres. The outline previews the result as you type. **Enter** applies it and **Esc** cancels. Exact input can fall between grid steps and accepts a decimal point or comma. Groups and ordinary mesh objects show their overall dimensions for reference. When dragging a face in **Shape Edit**, hold **Ctrl** to grow from its current size in steps, or **Ctrl+Shift** to move both sides symmetrically.

The same idea works on a floor plan. Type **9.37 m** on a straight wall and the end nearest the plan’s WCS stays fixed. The other end moves, taking its adjoining walls along. Curved walls show the length of their path. In floor-plan edit mode, a selected wall also exposes height and thickness, and a selected room exposes its height.

<table>
<tr>
<td width="50%"><img src="images/0.6.1/brush-dimensions.png" alt="A box with dimensions of 4 m on X, 3 m on Y and 2 m on Z."><br>Each dimension belongs to a local axis.</td>
<td width="50%"><img src="images/0.6.1/floor-plan-dimensions.png" alt="Entering 9.37 m on a wall previews the new position of its shared corner."><br>The cyan outline previews the wall edit before Enter.</td>
</tr>
</table>

## Cut into a wall or build out from it

Start a rectangle on a solid brush and give it depth. Push into the face and the orange preview cuts that brush. Pull outwards and the preview switches to an addition. A neighbouring solid can stay intact even if it overlaps the cutter. You can also target one wall of a floor plan. The new cut belongs to its target and follows it when it moves. For manual control, click **+ add** or **− subtract**. Click the active button again to return to automatic selection.

![In Unity, the left rectangle is pushed into the blue wall to cut an opening. The right rectangle is pulled out to add a block.](images/0.6.1/auto-operation.gif)

*Both gestures start on the same face. Push in 3 m on the left, then pull out 2.5 m on the right. The tool chooses the operation from the direction.*

## Draw a box on a ramp

Choose **Create → Box** and move the pointer from the floor to a vertical face, then onto a wedge. With **Local surface grid** selected under **Snapping**, the drawing grid follows each surface through 0°, 90° and 20°. The white dot marks where the brush will start, and the grid lines fade around it.

![The grid follows a 0° floor, a 90° wall and a 20° wedge. A box is then drawn on a 48° ramp.](images/0.6.1/sloped-surface.gif)

On the new 48° ramp, draw a 2 m square directly on the slope, then pull 2 m along the surface normal. The cube’s base sits flush against the ramp. Use **Snapping → Lock grid to a face** to keep drawing on the same plane past the ramp’s edge. Brushes drawn on a surface grid can follow its axes when moved or duplicated. [Still image of the surface grid](images/0.6.1/surface-grid.png)

## Read the distance or angle while you drag

Enable **Δ** in the CSG Brush overlay and drag with Unity’s **Move** tool (**W**). The coloured labels show the signed distance in metres from the start of the current drag. Pull an axis arrow to see its displacement, or drag the square between X and Z to see both components. Hold **Shift** to show all three world components, including zero values. With brush snapping enabled, the numbers follow the snapped preview. The labels disappear when you release the mouse.

![In the English Scene view, Move shows X and Z displacement in metres as the brush is dragged. The labels disappear on release.](images/0.6.1/relative-move.gif)

With **Rotate** (**E**), drag a coloured axis ring to see the signed angle in degrees from the start of the gesture. The readout follows the active Global or Local axis and keeps counting as you turn through 180° or complete a full revolution. It disappears when you release the mouse.

## Turn on grid snapping without moving the brush

Switch off the magnet and move a box to any position. Turn it back on and the box stays where you left it. On the next **Move**, the changed coordinates snap to the nearest nodes of the current grid. In the example, the grid step is **0.5 m**. Drag along X on the **World grid** and the Y and Z coordinates stay at their previous values, even if they are off-grid. With **Local surface grid** selected, a locked face or the brush’s saved surface can supply the grid origin and axes.

![With the magnet off, the brush moves freely. Turning it back on preserves the position, and the next X drag snaps on the 0.5 m grid.](images/0.6.1/grid-snap.gif)

The brush’s **Grid snap** setting controls what aligns. With **Shape** selected, a WCS on the −X face makes that face align to the grid when you move along X. The +X face aligns when the WCS is there. If the WCS lies between the faces, **Shape** uses the minimum boundary. **WCS origin** (`Pivot`) aligns the origin itself. **Snap WCS to Vertex** also selects **WCS origin**, so choose **Shape** again if you want to compare the two modes.

## Switch between drawing and editing

Go straight from **Shape Edit** or **Edit Brush** to **Create → Box** and back. Starting creation closes the current edit tool and cancels an unfinished edit drag. Switching to an edit mode cancels the current creation preview. Confirmed brushes, wall segments and spline links stay in the level. Use the overlay’s Move, Rotate and Scale buttons or Unity’s **W**, **E** and **R** shortcuts to switch manipulators in the Scene view.

![The Scene view switches between Shape Edit, Edit Brush and Create Box. Switching back discards an unfinished box preview.](images/0.6.1/tool-switching.gif)

## Organise and adjust the level

- **Group new objects as you draw.** Choose a GameObject in the Hierarchy and use Unity’s **Set as Default Parent**. New brushes, floor plans and splines are created beneath it. For example, set the loading bay as the parent and the boxes you draw for that bay appear together in the Hierarchy. A targeted cut stays with the brush or plan it cuts.
- **Move a window to another wall.** Pick a corner as the window’s WCS, then use **Move to** on a wall in the same or another floor plan. The opening turns to match the wall, fits its thickness and keeps its sill height above the destination floor.
- **Switch the editor language.** English is the default. Choose Russian in **Edit → Project Settings → CSG Brush** to switch the package’s overlay, settings and custom Inspectors. Unity remembers the choice for that editor user.

![The CSG Brush settings page switches between English and Russian.](images/0.6.1/editor-language.gif)

## Larger builds and existing projects

Moving brushes far from the world origin now keeps their visible meshes, cut faces and colliders together. When one opening moves in a heavily cut solid, the builder reuses unchanged collider pieces. Large, suitable axis-aligned meshes also get a faster second UV layout for baked lighting. In one Unity scene with **1,152 cuts**, moving a single cut took **0.22–0.27 s** of CSG and lightmap UV processing, down from **4.78–4.79 s** in the preceding Plus build. Both measurements exclude the wait before automatic unwrapping.

![An orange subtract brush carries its opening across a blue slab in a Unity scene.](images/0.6.1/moving-cut.gif)

*Here the cut already exists. Moving it shifts the opening in both the visible slab and its collider.*

A world unit is one metre throughout the fork. Existing project settings convert their saved lengths on first use. For example, a grid step of 16 at 32 units per metre becomes 0.5 m. Existing scene geometry stays the same physical size. Trigger relays now survive saving and reopening a scene. Rebuilding an older prefab also reconnects its generated trigger collider to the owning brush if 0.6.0 saved the relay with an empty script reference.

Editor scripts can set a brush origin with `BrushApi.SetPivotToVertex`, `BrushApi.SetPivotToEdge` and `BrushApi.ResetPivot`. The former unit-preset API has been removed. Pass lengths in metres to `BrushGridMaterial.Generate(int, float[])`. The legacy `ToMeters` and `ToUnits` methods now return their input.

The [development sandbox](https://github.com/veagis1/csg-brush/tree/main/Development~/Sandbox) opens this checkout as a local package in Unity 6000.6.8f1. Its URP setup and first-person controller let you walk through the geometry you build.

I wrote this entire description of the updates and features with the help of GPT‑6.2 Sol. A few details in the write-up may be off. The tools work great and make life easier if you’re used to CAD modelling. :-)
