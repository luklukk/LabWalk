# Switchable layers: design options and toggles

Added after 0.4.0 (unreleased). Checked in the Editor and the Meta XR Simulator, not yet on a Quest.

## Naming convention (Rhino)

Name a layer with one of these prefixes. Everything on it and on its sublayers belongs to it:

| Layer name | Becomes |
|---|---|
| `Option: Existing`, `Option: Renovation` | Design options: exactly one is shown at a time (group "Design") |
| `Option: Furniture / Layout A`, `Option: Furniture / Layout B` | Another, independent option group ("Furniture") |
| `Toggle: Tools`, `Toggle: Overhead` | On/off switches |

- Prefixes are not case-sensitive (`toggle:Tools` works). Layers without a prefix are always shown, as before.
- A switchable layer's visibility in Rhino is its **starting state** in the app. Hidden `Option:`/`Toggle:` layers are still imported so they can be turned on in the headset.
- Below a switchable layer, sublayers you turned off yourself stay out. Sublayers that are only off because their switchable parent is off are imported. Rhino remembers the difference, and Lab Walk reads it.
- Nesting works: a `Toggle:` inside `Option: Renovation` shows only while Renovation is selected **and** the toggle is on.
- Block instances follow the layer they are placed on.
- Put geometry that is removed by a renovation (for example walls to be demolished) on the `Option: Existing` layer. Put new work on `Option: Renovation`. Unchanged geometry stays on normal layers.
- GLB files: nodes named the same way (`Toggle: Tools`) become switches too.

A switchable layer with no renderable geometry (only curves or labels) is not offered.

## In the headset

- **Left grip** opens **View**: the design options (`(o)` selected), toggles (`[on]`/`[off]`), and **Change model...** (the file list; **B** goes back). **A** selects or switches the highlighted line; the tooltips say which.
- **Left stick click** cycles the first option group, e.g. Existing → Renovation; its tooltip names the next option.
- The status panel shows the current options, e.g. `Design: Renovation`.
- Choices are remembered per model version on the headset. A changed file (new fingerprint) starts from the file's own layer visibility.

## Implementation notes

- `RhinoModelReader` resolves each object's switchable layer (`LayerGroupNames.TryParse`) and merges meshes per color **and** layer, so each switch hides whole meshes. The lab demo went from 75 to 90 meshes.
- `Rhino3dmModelLoader` / `GlbModelLoader` expose `LoadedModel.Layers` (`ModelLayer`: kind, option group, name, default, parent, root object).
- `LayerView` holds the state (exclusive options, toggles, `PlayerPrefs` per fingerprint) and activates the layer roots.
- Tests: `Tools/RhinoImportTests` (options, toggles, nesting, hidden defaults, user-hidden sublayers, blocks, name parsing); **Lab Walk** editor test `LabWalk.Editor.LayerViewTest.Run(path, "x,y,z,yaw")` (switching, remembered choices, one render per option).
