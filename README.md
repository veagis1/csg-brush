# CSG Brush

This fork experiments with bringing CAD-style parametric modelling to level design in Unity. It builds on
[Jaroslav Stehlik’s CSG Brush](https://github.com/jaroslavstehlik/csg-brush), with thanks for the original editor
and its focus on building levels directly in the Scene view.

A brush-based level editor for Unity, modelled on the Quake and Unreal level editors. Levels are built from convex
brushes that are added to or subtracted from each other. The package generates the render meshes and convex colliders
from the brushes in the editor.

It was written for teaching level design and character controllers, so the toolset is small, brushes snap to a world
grid, and colliders are made of convex pieces.

Version 0.6.1 adds surface grids, exact dimensions, placement by vertices and edges, and distance and angle readouts
while you drag in the Scene view. [Stable release 0.6.1](https://github.com/veagis1/csg-brush/releases/tag/v0.6.1)

![A floor plan room with a door and a window cut into its walls.](Documentation~/images/doors-and-windows.png)

## In the Scene view

<table>
<tr>
<td width="50%"><img src="Documentation~/images/draw-add.gif" alt="Draw a brush: its base, then its height."><br>Draw a brush: its base, then its height.</td>
<td width="50%"><img src="Documentation~/images/draw-subtract.gif" alt="A subtract brush carves a tunnel; Cuts shows it in red."><br>A subtract brush carves a tunnel; Cuts shows it in red.</td>
</tr>
<tr>
<td width="50%"><img src="Documentation~/images/shape-edit-box.gif" alt="Shape Edit: drag a box's sides to resize it."><br>Shape Edit: drag a box's sides to resize it.</td>
<td width="50%"><img src="Documentation~/images/shape-edit-round.gif" alt="Drag a cylinder's round side for its radius, its top for its height."><br>Drag a cylinder's round side for its radius, its top for its height.</td>
</tr>
<tr>
<td width="50%"><img src="Documentation~/images/shape-edit-arch.gif" alt="Drag an arch's curve for its height, its front for its depth."><br>Drag an arch's curve for its height, its front for its depth.</td>
<td width="50%"><img src="Documentation~/images/shape-edit-selection.gif" alt="With several brushes, Offset changes them by the same amount and Match gives them the same size."><br>With several brushes, Offset changes them by the same amount and Match gives them the same size.</td>
</tr>
<tr>
<td width="50%"><img src="Documentation~/images/floor-plan-draw.gif" alt="Click a floor plan's corners; its walls follow."><br>Click a floor plan's corners; its walls follow.</td>
<td width="50%"><img src="Documentation~/images/doors-windows.gif" alt="Doors and windows cut openings where they are placed on a wall."><br>Doors and windows cut openings where they are placed on a wall.</td>
</tr>
</table>

## Features

- Brush shapes: box, wedge, cylinder, cone, sphere, arch, and linear, curved and spiral stairs, drawn in the Scene view
  with the Create tool.
- Additive and subtractive brushes, combined in Hierarchy order using the [Manifold](https://github.com/elalish/manifold)
  library.
- Grid snapping of position and size in metres, with rotation snapping in degrees.
- Vertex, edge and face editing with move, rotate, scale and extrude.
- A pivot per brush, as a fraction of its size or a distance from its corner.
- Materials dragged onto a brush in the Scene view, with an outline of where they will go.
- Floor plans: networks of walls with automatic junctions, and rooms with their own floors, ceilings and materials.
- Door and window brushes that cut openings of a set size.
- Wall anchors that keep doors, windows and other objects on floor plan walls, lined up with the wall and at a set
  distance from it.
- One convex collider per brush piece, with trigger, physics material and module support.
- Static flags per brush, renderer settings per Brush Group, and lightmap UV generation.
- Brush Groups that build into prefabs and scenes.

The package is editor-only. Builds contain only the generated meshes and colliders.

## Requirements

- Unity 6000.6 or later.
- An editor on macOS (Apple Silicon), Windows (x86-64) or Linux (x86-64, glibc 2.35 or later, such as Ubuntu 22.04). The
  CSG library is a native editor plugin built for these three platforms.

## Install

1. In Unity, open **Window** > **Package Manager**.
2. Click **+** and select **Install package from git URL**.
3. Enter `https://github.com/veagis1/csg-brush.git` and click **Install**.

The package is at the root of the fork. To pin version 0.6.1, use `https://github.com/veagis1/csg-brush.git#v0.6.1`.
If you already have the upstream package installed, replace its Git URL. The fork uses the same package ID.

## Documentation

- [What's new in 0.6.1](Documentation~/fork-0.6.1.md)
- [Manual](Documentation~/index.md): floor plans, doors and windows, wall anchors, and rendering and lightmapping.
- [Brush reference](Documentation~/brush-reference.md): detailed notes on the brush tools and components.
- [Changelog](CHANGELOG.md)

## Development

The original project is developed and maintained by AI coding agents (Claude Code), directed by the author.
ChatGPT is the AI assistant for this fork.
For fork development, see [Development~/](Development~/). The Unity test project is under `Development~/Sandbox`.
See [CONTRIBUTING.md](CONTRIBUTING.md) for the EditMode suite and native plugin.

## Support this project

CSG Brush is free to use under the MIT license, and it will stay free. Development is unpaid; sponsorship pays for
maintenance, new tools, and support for more platforms.

- Individuals: [GitHub Sponsors](https://github.com/sponsors/jaroslavstehlik).
- Companies and schools: invoiced sponsorship, support, and workshops are available. Contact the author through
  [GitHub](https://github.com/jaroslavstehlik).

## License

CSG Brush is released under the [MIT license](LICENSE.md). It includes the [Manifold](https://github.com/elalish/manifold) library (Apache License 2.0) and began as a fork of [Chisel](https://github.com/RadicalCSG/Chisel.Prototype) (MIT); see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
