# Layers, design options and wireframes

Since 0.7.0 the View menu shows the model's Rhino layer tree, and single objects can be switched to wireframe by pointing at them. Checked in the Editor and the Meta XR Simulator, not yet on a Quest.

## The layer tree (Rhino)

Every Rhino layer that holds imported geometry, directly or in its sublayers, appears in **View** under the same name and nesting as in Rhino's Layers panel, and in the same order. No prefixes are needed for that. Two prefixes add meaning:

| Layer name | Becomes |
|---|---|
| `Option: Existing`, `Option: Renovation` | Design options (group "Design"): only one is **solid** at a time |
| `Option: Furniture / Layout A`, `Option: Furniture / Layout B` | Another, independent option group ("Furniture") |
| `Toggle: Tools` | A layer named "Tools" (the prefix is optional and hidden in the app) |

- Prefixes are not case-sensitive (`toggle:Tools` works).
- A layer's visibility in Rhino is its **starting state** in the app. Hidden `Option:`/`Toggle:` layers are still imported so they can be turned on in the headset. Other layers you turned off in Rhino stay out of the app.
- Below an `Option:`/`Toggle:` layer, sublayers you turned off yourself stay out. Sublayers that are only off because their parent is off are imported. Rhino remembers the difference, and Lab Walk reads it.
- Block instances follow the layer they are placed on. Parts of a block that sit on an `Option:`/`Toggle:` layer follow that layer instead.
- Put geometry that is removed by a renovation (for example walls to be demolished) under `Option: Existing`. Put new work under `Option: Renovation`. Unchanged geometry stays on normal layers.
- GLB files: nodes named `Toggle: ...` / `Option: ...` become switches too (no per-object pointing or wireframes).

A layer with no renderable geometry (only curves, points or labels) is not listed.

## In the headset

**Left grip** opens the menu. It rides above the left controller, drawn on top of the model. **Pin here** in its title bar (or **Y**) pins it in the room at eye level in front of you; it stays there as you walk around until you press **To hand** (or **Y**) again. Each time the menu opens, it starts on your hand.

- Each row is a layer with three buttons: **Solid**, **Wire** (orange wireframe) and **Off**. The lit button is the current setting.
- Folders have a blue **›**: open it (or point at the name) to see the layers inside. **‹ Back** or **B** goes up a level; B at the top closes the menu.
- A folder limits everything inside it: **Wire** draws its contents as wireframes, **Off** hides them, **Solid** lets each layer inside decide. A row shows "(limited by folder)" when that applies.
- Design options: setting one **Solid** turns off the other solid option in its group. The other can stay as a **Wire** overlay, for example Renovation solid with Existing outlined on top. The status panel shows it, e.g. `Design: Renovation + Existing wireframe`.
- **Use:** point at a button with the right controller and pull the **trigger**, or move with either stick and press **A**.
- **Change model...** at the bottom of the top page lists the files to import.
- **Left stick click** (menu closed) makes the next design option solid, e.g. Existing → Renovation.
- Choices are remembered per model version on the headset. A changed file (new fingerprint) starts from the file's own layer visibility.

## Pointing at objects

With the model placed (after alignment), point the right controller at any part of the model:

- The item under the ray is tinted and a label names it with its layer path, e.g. `W201 SawStop aligned cabinet saw / Renovation > Equipment > Woodworking machines`.
- **Right trigger** switches the whole item to wireframe; its outline stays, the solid is gone. Point at it again (the ray still finds it) and pull the trigger to make it solid.
- **What counts as one item** (a whole tool, not a single part):
  1. A Rhino **group**: all its objects, named after the group.
  2. Objects on the same layer named `<item> / <part>`, e.g. `W201 SawStop aligned cabinet saw / Carbide blade tooth`: everything before ` / ` is the item. This is how the shop model is already named, so every tool, duct run and bench is one item without any extra work in Rhino.
  3. A **block instance**.
  4. Otherwise the single object (e.g. an unnamed wall).
- The View menu's top page shows how many items are wireframe and has **Make all solid**.
- Pointed items are remembered per model version (by group ID, layer and item name, or object ID).

## Wireframe details

Wireframes show the real edges: outlines, and creases where faces meet at more than 20°. Flat triangulation diagonals and the facets of round shapes such as ducts are left out. Lines are drawn slightly toward the viewer, so edges that lie on a surface stay visible. They are one pixel wide, so thin, distant edges can shimmer in the headset.

Edges are computed once while reading the file (about 390,000 segments for checkpoint 38) and capped at 2 million line vertices, with a warning if the cap is reached.

## Implementation notes

- `RhinoModelReader` makes every layer on an object's path a node (`RhinoLayerGroup`, with Rhino's layer order) and merges meshes per color **and** layer node. Every top-level object gets an index (`RhinoModelData.Objects`), carried by each of its mesh and line vertices (UV0.x).
- `Rhino3dmModelLoader` / `GlbModelLoader` expose the tree (`ModelLayer.Children`) and `LoadedModel.Objects`.
- `LayerView` holds each layer's setting (Solid / Wireframe / Off; effective = the minimum along the path), the pointed objects, and a small texture with one texel per object (`_LabWalkObjectState`: wireframe, highlighted). `RhinoBasic.shader` leaves out pointed objects and tints the highlighted one. `LabWalkWire.shader` draws either the whole layer (`_WireAll`) or only pointed objects.
- `ObjectPicker` renders the visible solids with `LabWalkPick.shader` into a one-pixel target along the controller ray (command buffer, asynchronous readback) to get the object index and distance. No mesh data is kept on the CPU for this.
- `MenuPanel` draws the menu with `LabWalkOverlay.shader` and the built-in text shader with depth testing off.
- Tests: `Tools/RhinoImportTests` (layer tree, options, nesting, hidden defaults, user-hidden sublayers, blocks, object indices); **Lab Walk** editor test `LabWalk.Editor.LayerViewTest.Run(path, "x,y,z,yaw")` (tree, Solid/Wire/Off, folder limits, options, pointed objects, remembered choices, renders).
