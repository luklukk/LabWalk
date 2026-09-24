# Rhino export and true scale

Use a copy of the lab model for export. A self-contained binary glTF (`.glb`) is the first supported format. The loader interface allows a future `.3dm` adapter without changing alignment or anchoring.

## Prepare and export

1. Confirm **Document Properties > Units** and check one known lab dimension. For the most repeatable first export, set the export copy's units to **meters** and accept Rhino's option to scale existing geometry. This preserves physical size.
2. Put a chosen floor landmark at or near the origin in the export copy, and choose a second landmark on the same floor. A separation of at least 2 m makes directional alignment easier. Record their coordinates and actual separation. Avoid exporting architecture at distant survey coordinates; large coordinates reduce rendering precision.
3. Select the architectural objects needed for the walkthrough. Remove unnecessary hidden construction geometry and microscopic details. Mesh surfaces/NURBS with a moderate custom render mesh setting. Inspect curved edges and thin elements; tighten the mesh only where the walkthrough needs it. There is no tested universal triangle limit yet.
4. Export Selected as **GLB**. Enable **Map Rhino Z to glTF Y**, **Export materials**, **Export texture coordinates**, and **Export vertex normals**. Use metallic/roughness PBR materials, with a display-color fallback for unassigned materials. Prefer modest texture sizes for the first headset test.
5. Include open meshes if the model uses single architectural surfaces. Check normals/backfaces from inside the room; close or properly orient walls where possible. Avoid relying on unsupported material extensions. Keep **Draco compression off** for this milestone; no Draco decoder is installed.
6. Embed texture resources in the GLB. The app imports one file; external images and buffers are not part of the supported input contract. Layers can be exported for hierarchy, but the app has no layer controls.

Rhino source: [GLB/glTF export options](https://docs.mcneel.com/rhino/8/help/en-us/fileio/gltf_import_export.htm), [document units and scaling](https://docs.mcneel.com/rhino/8/help/en-us/documentproperties/units.htm).

## Configure the model

Copy the GLB into `Assets/StreamingAssets/Models`. Edit `model.json`; `lab.example.json` is a starting point, not a second automatically loaded model. Alternatively deploy the two files as an external override using the USB script.

| Field | Meaning |
|---|---|
| `file` | A GLB basename in this same folder, such as `lab.glb` |
| `modelId`, `displayName` | Your model/version identifier and headset label |
| `sourceUnits` | `meters`, `millimeters`, `centimeters`, `inches`, or `feet` |
| `coordinateUnits` | `gltfMeters` for a conforming GLB; `sourceUnits` only for a deliberately unconverted export |
| `referenceA`, `referenceB` | Floor reference coordinates **after import**, in Unity-local meters |
| `knownDistanceMeters` | A separately measured floor dimension to compare in the headset |

glTF defines linear distances in meters. The default `gltfMeters` policy therefore applies a scale of 1, even if the original Rhino source was in millimeters. Selecting millimeters as metadata must not shrink an already converted GLB by another factor of 1,000. Use `sourceUnits` only if inspecting the imported bounds proves that raw source-unit coordinates survived the exporter. It applies exactly one conversion, for example 0.001 for millimeters or 0.3048 for feet. Never correct scale by moving the XR rig or using alignment gestures.

## Axis mapping and reference coordinates

Rhino uses Z as up. Its documented Z-to-Y export rotation maps Rhino `(x,y,z)` to glTF `(x,z,-y)`. The pinned glTFast importer converts glTF handedness by reflecting X, yielding Unity `(-x,z,-y)`. Multiply source coordinates by meters per source unit when calculating the final reference positions. Do not add another axis rotation to the loaded model. If your exporter adds a root transform or you pre-transform the model, inspect the result and account for that transform as well.

Example: in a millimeter Rhino source, A `(0,0,0)` and B `(0,-2000,0)` become Unity-local A `(0,0,0)` and B `(0,0,2)` after a conforming export and this importer. The sidecar stores those final meter values. If you changed the export copy to meters first, B in Rhino is already `(0,-2,0)`.

## Verify before the lab walkthrough

Check reported X/Y/Z dimensions in editor preview against Rhino. Export a one-meter calibration cube or a clearly measured architectural span if the scale is ambiguous. In the headset, compare the yellow A–B line with actual floor marks, then use right grip at both ends of an independently tape-measured floor distance. A 2 m architectural span should occupy 2 m physically. The grip tool measures the floor plane, not arbitrary surfaces or vertical heights.

The app blocks saving when the two reference lengths differ by more than 5%, but that threshold is only a mistake detector. Set tighter acceptance tolerances for your lab test and record measured errors. An anchor maintains a pose; it does not prove that the model geometry, export units, floor estimate, or selected landmarks are accurate.
