# Brush reference

A brush is a GameObject with a **Brush** component. It describes a solid shape: a box, a cylinder, a flight of stairs, or a shape you edit by hand. CSG Brush combines the brushes of a scene into render meshes and convex colliders. A brush has nothing hidden under it; the meshes and colliders are generated under the [Brush Group](#brush-groups-and-prefabs) that builds it.

This page describes brushes in detail. For floor plans, doors and windows, wall anchors, and rendering, see [Additional resources](#additional-resources).

## Create a brush

To draw a brush in the Scene view:

1. In the Scene view toolbar, open the **Create** tool dropdown and select a shape.
2. Optional: In the **CSG Brush** overlay, set what the new brush gets, such as its modules, number of sides, step height, or **Hollow**.
3. Press on a surface, a brush face, or the ground, and drag out the base.
4. Release, then move the pointer to set the height.
5. Click to create the brush.

Press **Esc** to cancel the brush, or to leave the tool.

In the **CSG Brush** overlay, use **Snapping** to choose the world grid, follow the surface under the pointer, or lock the grid to a face. The magnet turns grid snapping on or off. While you draw, **Dimensions** shows the width and depth of the base, then the height. See [the surface grid and cutting tools](fork-0.6.1.md).

Some shapes are drawn differently:

| **Shape** | **How to draw it** |
| :--- | :--- |
| **Box** | Drag the base rectangle, then set the height. |
| **Wedge**, **Linear Stairs** | Press at the low end and drag to the high end, then release. Click to set the width; it goes to the side the pointer is on, and **Shift** centres it on the line. Then click to set the height. The direction snaps to the rotation snap, and the length to the grid. |
| **Arch** | As a wedge, but the line is the span: press at one foot and drag to the other, then release. Click to set the depth, then the height. |
| **Cylinder**, **Cone**, **Sphere** | Press on the centre of the base and drag to set the radius. For a round sphere, release and click again at the same spot. Move the pointer after releasing to adjust the height from the sphere's diameter. |
| **Curved Stairs**, **Spiral Stairs** | Press where the column's axis goes and drag the outer radius. The drag direction is where the first step starts. Then set the height. The brush's transform is on the axis, at floor level. |
| **Door**, **Window** | Click once. See [Doors and windows](doors-and-windows.md). |
| **Floor Plan** | Click the corners of the walls. See [Floor plans](floor-plans.md). |

A new brush starts with the settings shown in the **CSG Brush** overlay while the Create tool is active. After you create a brush, change them on the brush itself. Its static flags come from **Project Settings** > **CSG Brush** > **Static flags**.

You can also create a brush in these ways:

* **Tools** > **CSG Brush** > **Create** selects a shape in the Create tool.
* **GameObject** > **CSG Brush** creates a brush of the default size at the centre of the Scene view.
* Add a **Brush** component to an empty GameObject.

## Place, size, and rotate a brush

Use the **Move** and **Rotate** tools as for any GameObject. Set the brush's **Size** in metres in the Inspector. One Unity world unit equals one metre.

For an exact size in the Scene view, turn on **Dimensions**, select a brush and click an editable dimension. Type a size in metres. The outline previews the change. Press **Enter** to apply it or **Esc** to cancel.

The **Scale** tool also resizes the brush. When you release it, the scale goes into the brush's **Size** and the transform's scale returns to one.

### Shape handles

A selected brush's shape can be changed directly in the Scene view: drag one of its sides, or one of its points, such as a cone's apex or a cylinder's radius. The handles stay with the Move, Rotate, Scale, and Transform tools, so you can move or rotate the brush and change its shape without switching tools. They're hidden while you draw a brush or a floor plan. The brush stays the shape it is; to edit vertices, edges, and faces freely, use [edit mode](#edit-a-brushs-shape) instead.

Only the sides turned towards you are handles. A side shows a faint outline, and lights up when the pointer is on it; drag it anywhere to move it out or in, and the opposite side stays where it is. A plain click on a side still selects, as usual: a side only starts moving after a short drag. Where a part of the shape is an edge, such as a wedge's low end or a cylinder's rim, it's a line you grab anywhere along; where it's a point, such as a cone's apex, it's a dot. Where an edge runs along the border of a side, the edge takes the drag.

| **Shape** | **Sides** | **Edges** | **Points** |
| :--- | :--- | :--- | :--- |
| Box | Every side. | | |
| Wedge | The diagonal face, the high end, the bottom, and the two triangular sides. Pushing the diagonal face makes the ramp longer and taller at the same steepness; its length snaps and its height follows. | The low end (start) and the high top edge (height). | |
| Cylinder | The top and the bottom, and the round side: the radius. Only the part of the round side facing you lights up; drag it out or in, away from the axis. A round cylinder stays round; an oval one keeps its proportions. | | |
| Cone | The bottom, and the sloped side: the radius of the base. | | The apex. |
| Sphere | The surface: the radius. Only the part facing you lights up; drag it out or in, away from the centre. A round sphere stays round; a stretched one keeps its proportions. | | |
| Linear stairs | The stepped sides, the first riser (the start), the back wall, the top tread (the height), and the bottom. Set the step height in the Inspector. | | |
| Arch | The front and the back. The curved outer face: the height. The curved inner face, under the opening: the thickness. | The feet (the width). | |
| Curved stairs | The inner face (towards the column) and the outer face: each moves its own edge, and the other stays. The top step's tread: the height, spread over the same steps. The last step's end: the angle the stairs cover. | | |
| Spiral stairs | The inner and the outer face, as on curved stairs. The top step's tread and the last step's end: more steps, going round further. | | |
| Door, window | The sides, the top, and the bottom. | | |

Unity's Move, Rotate, and Scale handles take priority when the pointer is over them. With Dimensions enabled, the dark badge updates as you drag. Each badge sits just outside its edge and shows the measurement in metres followed by its local axis, for example 2.00 m (X). Hold **Shift** to move both sides at once, about the middle. Hold **Ctrl** to move in increments of the current grid step from the start of the drag, including when grid snapping is off. With a 0.125 m step, a 1.03 m block grows to 1.155 m, then 1.28 m. **Ctrl+Shift** moves both sides by the selected step. With ordinary grid snapping, sizes snap to the grid, step heights snap in increments of 0.01 m, and angles follow the rotation snap. Escape cancels the drag. Each completed drag takes one step to undo.

To turn the handles on or off, use **Shape Edit** in the **CSG Brush** overlay, or press **B** in the Scene view. While they're on, three buttons beside it set what a drag does to the other selected brushes:

| **Choice** | **What happens** |
| :--- | :--- |
| **Single** (default) | Only the brush you drag changes. |
| **Offset** | Selected brushes of the same shape change by the same amount: drag one cylinder's top up 1 m, and every selected cylinder gets 1 m taller. |
| **Match** | Selected brushes of the same shape take the dragged brush's new size: every selected cylinder becomes as tall as the one you drag. |

Brushes of other shapes never change.

### Grid snapping

With the snap magnet on in the **CSG Brush** overlay, moving and resizing a brush use the current grid step and rotating it uses the rotation step. A brush drawn on a local grid can keep snapping along that grid. Exact sizes entered in the Scene view can fall between grid steps.

Enable **Δ** in the overlay to measure a drag from its starting pose. **Move (W)** shows signed distances in metres, such as **X: +0.125 m**. **Rotate (E)** shows signed degrees beside the dragged axis ring, such as **Y: −15.000°**. Both readouts use three decimal places.

When you move or resize a brush, its **Grid snap** setting decides what goes onto the grid:

| **Grid snap** | **What goes onto the grid** | **Use it for** |
| :--- | :--- | :--- |
| **Shape** | A boundary of the brush on each moved axis. If a boundary passes through the WCS origin, that boundary snaps. Otherwise the minimum extent snaps. This is the default. | Aligning faces and returning a drawn corner to a grid point, including brushes with exact sizes. |
| **WCS origin** | The brush's pivot. The shape follows, wherever it ends up. | The ProBuilder workflow, such as detail brushes placed around a point. |
| **Off** | Nothing. The brush stays exactly where it's put and keeps its size. | Levels built from measured numbers, such as by a [script](scripting.md). |

With **Shape** or **WCS origin**, rotating a brush never moves it: it turns about its pivot, and only the angle snaps. The next time you move it, it snaps again. An undo, setting the pivot, and opening a scene don't move brushes either. Vertices you move in [edit mode](#edit-a-brushs-shape) snap to the grid in both modes; a Custom shape in **Shape** mode also puts its vertices on the grid while it's axis-aligned.

Set **Grid snap** in the Brush Inspector. New brushes get the setting in the **CSG Brush** overlay while a Create tool is active.

Press **[** and **]** to change the grid step. Doors, windows, and brushes on a [wall anchor](wall-anchor.md) don't snap, because their wall places them.

Parent GameObjects may only organise brushes. If a brush's parent is rotated or scaled, the **CSG Brush** overlay lists it with a **Reset** button.

### Grid steps

**Project Settings** > **CSG Brush** sets the grid steps and the default box size in metres. Separate grid steps with spaces. The **CSG Brush** overlay shows the current grid step in metres.

Existing project settings are converted on first use. The former units-per-metre ratio converts each saved length. For example, on a project that used 32 units per metre, a grid step of 16 becomes 0.5 m and a default box edge of 64 becomes 2 m. Existing brushes, scenes and prefabs already store lengths in metres.

## Set a brush's pivot

The pivot is the origin for moving and rotating the brush. A primitive drawn in the Scene view starts with its pivot at the first construction point, including any grid snap applied to that point. It's measured from the brush's left, bottom, back corner, where the back is the side away from the direction the brush faces (+Z).

![The Brush Inspector of a 2 × 2 × 1 m box with its pivot at the bottom of its back face, and Grid snap set to Shape.](images/brush-inspector-pivot.png)

**WCS mode** sets how the **WCS origin** values are measured:

| **WCS mode** | **Values** | **When you resize the brush** |
| :--- | :--- | :--- |
| **Normalized** | A fraction of the brush's size on each axis. 0.5, 0.5, 0.5 is the centre. A drawing origin outside the bounds can give a fraction outside 0 to 1. | The pivot stays at the same fraction of the size. |
| **Absolute** | A distance from the corner in metres. | The pivot stays at the same distance from the corner. |

Useful pivots:

* **Y = 0** puts the pivot on the bottom face, so the brush stands on whatever you place it on.
* **Y = 0 and Z = 0** put the pivot at the bottom of the back face, so the brush stands against a wall. Use this for furniture on a [wall anchor](wall-anchor.md#stand-an-object-away-from-the-wall).

Setting the pivot moves the transform, never the shape. Switching the pivot mode doesn't move anything. On a Custom shape, setting the pivot moves the vertices instead.

Select one editable brush and choose **WCS** > **Snap WCS** in the CSG Brush overlay, or click **Snap WCS** in the Inspector. Pick a vertex, an edge midpoint or a face centre. Move and Rotate now use that origin and the local axes. **Reset WCS** returns the origin to the centre of the geometry. A window on a floor plan wall can have its own origin. For generated geometry, set the origin on its floor plan or spline. [Window settings](doors-and-windows.md#change-a-door-or-window).

**Move onto object** places the WCS on a surface while preserving rotation. Shift-drag to preview the primitive as you move it. **⊥** seats its base against the visible face with local +Y pointing outwards. The **magnet** starts on. Switch it off to place the object anywhere on the face, or hold Ctrl to move in grid steps. The **reference points** button shows visible vertices, edge midpoints and face centres with the same markers and hover labels as **Snap WCS**. Ordinary placement can snap to an occluded vertex or midpoint. Perpendicular placement uses visible features only.

Release over a free point to place the object. With the magnet on, click a feature twice to align exactly. Pocket floors, walls and rim edges from CSG subtraction are also placement targets.

From a face centre, move toward an edge midpoint to choose a guide. Hold Ctrl to measure from the centre in grid steps, or enter the distance in **Offset (m)** and click **Apply**. Zero returns to the centre. Esc cancels the preview. Undo restores the previous pose.

## Add and subtract

A brush's **Operation** is **Add** or **Subtract**. An added brush fills space; a subtract brush carves it out.

In automatic mode, drawing into a target makes a cut there. Use **+ add** or **- subtract** to choose the operation explicitly. Click the active button again to return to automatic mode. The override lasts for the editor session. On upgrade from 0.6.0, drawing starts in automatic mode even if the old project default was Subtract. Existing brushes keep their operations.

An assigned cutter carves its target. An unassigned cutter nested inside an additive brush carves that brush and its compound parts. An independent brush placed in the pocket keeps its mesh and collider. Unassigned cutters without an additive ancestor follow the CSG order within their group and affect earlier brushes. To change the order, drag brushes in the Hierarchy, or use **To First** and **To Last** in the Brush component's context menu.

Turn on **Cuts** in the **CSG Brush** overlay to see subtract brushes as translucent red volumes. This lets you select a subtract brush where it has carved everything away. With **Cuts** off, you can't click subtract brushes in the Scene view, except doors and windows.

### Layers

Brushes are combined per Unity layer. A subtract brush carves only brushes on its own layer, and each layer renders as its own mesh and has its own colliders on that layer. Use layers for a collision layer per kind of thing, or to keep parts of a level from carving each other.

## Edit a brush's shape

Every shape can be edited by hand. To start editing, click **Edit shape** in the Brush Inspector or **Edit Brush** in the **CSG Brush** overlay, or select **Edit Brush** in the tool context dropdown of the Scene view's Tools overlay.

In edit mode, the Tool Settings toolbar holds the selection settings:

| **Setting** | **Description** |
| :--- | :--- |
| **Vertex**, **Edge**, **Face** | What you select. Press **1**, **2**, or **3** to switch. |
| **Select Hidden** | Also select vertices, edges, and faces that face away from the camera. |
| **Drag Rectangle Mode** | Select only what's completely inside the rectangle, or everything it touches. |
| Handle orientation | **Global**, **Local**, or **Element**. **Element** aligns the gizmo with the selection, so the blue axis runs along a face's normal. |

To select, click an element. Shift-click adds to the selection, Ctrl-click removes from it, and dragging a rectangle selects several. **Esc** clears the selection.

The **Move**, **Rotate**, and **Scale** tools act on the selection, snapped to the grid. Vertices that you drop onto each other weld together.

The first edit turns the brush into a **Custom** shape. **Reset to Box** (or to the shape the brush came from) discards your edits. Concave shapes are allowed; CSG Brush splits them into convex parts for the colliders.

### Extrude

To extrude:

1. Select one or more faces.
2. In the **CSG Brush** overlay, turn on **Extrude**. The extrude settings appear under it, and the Scene view outlines the extruded shape.
3. Set the **Distance**. A negative distance cuts a pocket into the brush. The outline follows as you change it.
4. In **Faces**, choose whether the selection extrudes as one block or each face on its own.
5. Click **Apply**.

![A box in edit mode with its side face selected. The Brushes overlay shows Extrude turned on and the Scene view outlines the extruded box. Current versions show the distance in metres.](images/extrude-preview.png)

Nothing changes until you click **Apply**. The faces stay selected, so **Apply** again extrudes them again. If the extrusion can't be made, the overlay says why instead of enabling **Apply**. You can also hold **Shift** when you start dragging the **Move** gizmo on selected faces: they extrude by the distance you drag. Drag into the brush to cut.

An extrusion can run through other parts of the brush. Your own faces, edges, and vertices survive it, so coplanar faces you keep apart stay apart.

### Bridge

**Bridge** connects two selected faces of one brush with a block between them, the way a corridor joins two rooms. The faces need the same number of corners. The new walls stay selected.

If an extrusion or bridge wouldn't make a sound shape, for example because it would end exactly where the brush touches itself, or a cut would remove everything, the brush stays as it was and the **CSG Brush** overlay says why. Try another distance.

## Materials

A brush's **Material** applies to every face of the brush.

To set a material, you can also drag it from the Project window onto a brush in the Scene view. While you drag, the Scene view outlines the brushes the material will go to. On a [Floor Plan](floor-plans.md), the material goes to all of the plan's walls, or to the floor or ceiling of the one room under the pointer. A regular mesh in front of a brush takes the material the usual Unity way.

The faces a subtract brush carves take the subtract brush's material by default. For example, the sides of a doorway can have a different material from the wall. To make them look like the solid they're cut into instead, select the subtract brush and set **Cut faces** to **Cut Solid Material**: a hole in a stone wall is then stone inside.

Faces without a material show a generated ruler texture: lines at every grid step, stronger at each metre, on a metre checker. It lines up with the world grid across brushes and cuts. CSG Brush creates it under `Assets/CSG Brush` the first time it's needed. To use your own material instead, set it in **Project Settings** > **CSG Brush**.

## Collision and triggers

**Collision** sets what colliders the brush makes:

| **Collision** | **Result** |
| :--- | :--- |
| **Solid** | Solid colliders. |
| **Trigger** | Trigger colliders. |
| **None** | No colliders. |

A brush's colliders are convex pieces. Each piece takes the brush's **Physics Material**, **Provide Contacts** setting, tag, layer, and static flags.

A trigger brush made of several pieces still raises one enter and one exit per collider. To react to triggers, do one of the following:

* Put a script with `OnTriggerEnter`, `OnTriggerStay`, or `OnTriggerExit` on the brush, as on any trigger.
* Subscribe to `Brush.TriggerEntered` and `Brush.TriggerExited`.
* Implement `IBrushTriggerListener` in a component on the brush's GameObject.
* Add the **Brush Trigger** module and connect its UnityEvents.

### Modules

A module carries the game-specific values of a surface, such as ice, water, or fall damage. A module is a `BrushModule` component on the brush, or on a parent GameObject, where it applies to every brush below it. Its values go onto each collider piece of the brush, and changing them rebuilds only those pieces. A module can also make a brush a trigger, for example for water.

To give every new brush a module, choose it from the **Modules** dropdown in the **CSG Brush** overlay while the Create tool is active. The dropdown lists every module; choose one again to turn it off, or choose **Nothing** to turn them all off. The Quake controller's module, `QuakeBrushSurface`, is part of the project, not of this package.

## Shape settings

### Hollow

**Hollow** turns a box or cylinder into a room: only walls of **Wall thickness** remain.

### Stairs

You set the step height and draw the total height. The number of steps is the height divided by the step height, rounded, so the steps fill the height exactly. A step is at least 0.1 m high, on every kind of stairs: much lower steps make so many blocks that building them takes too long.

* **Linear Stairs** fill their size box. Each step runs the length divided by the number of steps.
* **Curved Stairs** and **Spiral Stairs** use Unreal's parameters: inner radius, step width, step height, angle of the curve or steps per 360 degrees, add to first step, and counter clockwise. Spiral stairs also have step thickness, sloped floor, and sloped ceiling. You set a height instead of a number of steps, and the brush's size follows from the parameters.

Linear and curved stairs stand on solid support down to the floor. Turn off **Support under steps** for open steps, each a slab of **Step thickness**. Set it on the brush, or in the **CSG Brush** overlay for new stairs.

Each step is a closed block with its own convex collider. A sloped spiral's collider is the hull of each twisted block, so use more steps per turn for a smoother ramp.

## Brush groups and prefabs

A **Brush Group** component builds the brushes below it in the Hierarchy, down to the next group, into one mesh per layer and one set of colliders. These are generated as the group's children. To add one, choose **Add Component** > **CSG Brush** > **Brush Group**. A group has an amber block icon in the Hierarchy and the Scene view.

A brush is built by the nearest group above it. Brushes without one are built by their scene's automatic group, which is hidden. Every scene has its own, so every scene holds its own geometry and scenes can be loaded together at runtime. The **Brush group** field in the Brush Inspector shows what builds the brush; click it to find the group.

A Brush Group inside a prefab builds into the prefab: its meshes are saved as sub-assets whenever you save the prefab. You can instantiate such a prefab at runtime, and it never carves anything outside itself. Instances in a scene use the prefab's meshes until you change their brushes.

A prefab without a Brush Group is a stamp. When you place it in a level in the editor, its brushes, such as a doorway cut, join that level's CSG. It builds nothing of its own.

For static flags, renderer settings, and lightmap UVs, see [Brush Group rendering and lightmapping](brush-group-rendering.md).

## Rebuild

CSG Brush builds automatically when you change a brush. To build everything again from scratch, meshes and colliders, choose **CSG Brush** > **Rebuild Now**.

## Run the package tests

Open **Window** > **General** > **Test Runner**, select **EditMode**, and run `CsgBrush.Tests`. To run them from the command line, use `-runTests -testPlatform EditMode -testFilter CsgBrush.Tests`.

## Additional resources

* [Floor plans](floor-plans.md)
* [Doors and windows](doors-and-windows.md)
* [Attach objects to walls](wall-anchor.md)
* [Brush Group rendering and lightmapping](brush-group-rendering.md)
