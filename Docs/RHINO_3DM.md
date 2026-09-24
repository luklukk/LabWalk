# Direct Rhino (.3dm) import

Lab Walk can read a `.3dm` directly on Windows x64 in Unity and includes an Android ARM64 native reader for Quest. No Rhino installation, conversion server or network connection is required at runtime. The Android integration is built locally; execution on a physical Quest is still untested.

## Add the lab

1. In Rhino, confirm the document units and move the model near the world origin. Keep the physical floor at Rhino Z = 0 where practical.
2. Generate the desired render meshes by viewing the model in Shaded mode. Save with render meshes included; do not use **Save Small**. For predictable results, use a copy of the document with surfaces, extrusions and SubD converted to explicit meshes. Keep the editable original.
3. Embed linked blocks before saving. External linked files are not resolved. Choose mesh detail appropriate to a mobile headset; trim unseen geometry and unnecessary detail.
4. Put `lab.3dm` in `Assets/StreamingAssets/Models`. Copy `lab.3dm.example.json` to `model.json` and set the two real floor references. Rebuild, or put the `.3dm` and `model.json` together in a folder and use `Tools/Deploy-Quest.ps1 -ModelFolder ...` with the updated APK.
5. Check the import summary and dimensions before alignment. **INCOMPLETE** means geometry was omitted; the status panel stays visible. Detailed reasons appear in the Unity/device log (first 32 items). Correct the Rhino file before treating it as a complete lab.

The supplied startup model is now `sample-room.3dm`: original room geometry saved in **millimeters**, read as **6 × 3.1 × 8 meters**. It includes 15 meshes and 180 triangles. `sample-room.3dm.json` is its manifest. To return to GLB, copy `sample-room.glb.json` over `model.json`. A changed file or manifest invalidates the old placement and requires alignment.

## Scale and coordinates

- Recommended: `coordinateUnits: "rhinoDocument"`. The reader obtains the document units from the `.3dm` and converts once to meters. `sourceUnits` is informational in this mode.
- Unitless/custom-unit files are rejected. For these, explicitly set `coordinateUnits: "sourceUnits"` and `sourceUnits` to `millimeters`, `centimeters`, `meters`, `inches` or `feet`. This override intentionally replaces the document conversion.
- Rhino `(X,Y,Z)` becomes Unity `(X,Z,Y)`, multiplied by meters per Rhino unit. Thus Rhino +Z is up and +Y becomes Unity +Z. Mesh winding and normals are corrected, including mirrored/nonuniform block transforms.
- References in the manifest are always the resulting **Unity-local meters**, not raw Rhino coordinates. Example: Rhino `(0,2000,0)` in a millimeter file is reference `(0,0,2)`.
- The importer preserves the document origin. It does not center, fit, resize or move the tracking rig. Mesh coordinates are converted to meters and the imported root stays at scale one.

## Supported scope

| Rhino content | Behavior |
|---|---|
| Mesh objects | Triangle/quad faces and normals; quads split into triangles |
| Breps / polysurfaces | Saved render mesh for each face; missing faces counted explicitly |
| Extrusions | Saved render mesh; missing mesh counted explicitly |
| Embedded blocks | Recursive transforms, including mirrors and nonuniform scale; cycle/depth guards |
| Hidden objects / hidden parent layers | Omitted intentionally and counted separately |
| Materials | Basic opaque diffuse color from object/layer material, or object/layer display color; parent inheritance for blocks |
| Textures, transparency, PBR/procedural materials, decals, vertex colors | Not reproduced; the panel states that materials are approximate |
| SubD, bare surfaces without caches, curves, points, annotations, lights, clipping planes | Not rendered; counted as unsupported. Convert required geometry to meshes in Rhino |
| External linked blocks | No external file loading; use embedded content |

OpenNURBS reads geometry but does not supply Rhino's general-purpose NURBS mesher. No tessellation service or control-net substitution is used. The app never presents a SubD control cage as the finished surface.

Limits: 150 MB input file; 2 million expanded vertices; 4 million triangles (a conservative two-triangles-per-face budget); 200,000 source surface faces/meshes; 100,000 object/instance visits; 32 nested block levels. Decoded `.3dm` memory can exceed its file size before mesh limits are applied. Initial native file parsing cannot be interrupted mid-call; cancellation is checked before and after parsing and during extraction. These are guards, not a performance guarantee. Large coordinates over 100 km from origin are rejected; keep architectural content much nearer for float precision.

## Implementation and verification

`ModelLoaderFactory` selects `GlbModelLoader` or `Rhino3dmModelLoader` behind `IModelLoader`. `RhinoModelReader` performs native parsing and extraction on a worker thread. Extracted geometry is merged into one mesh per color (split at 250,000 vertices), so thousands of Brep faces become a few dozen Unity meshes and draw calls. Unity mesh creation happens on the main thread with a yield per mesh. Curves, points, text dots and annotations are counted as "not shown" rather than as missing geometry, so they do not mark an import `INCOMPLETE`. The lightweight `RhinoBasic` shader supports instanced stereo; its actual Quest Vulkan appearance still needs device testing. Generated meshes and materials are disposed with `LoadedModel`.

Dependencies are pinned to **Rhino3dm 8.35.0**, using its .NET Standard 2.0 assembly and official Windows x64 native binary. The Android native binary was compiled from the matching source with Unity's NDK. See [native build record](RHINO_NATIVE_BUILD.md).

Verified locally:

- Real `.3dm` save/read tests: units, axes, normals/winding, quads, colors, mirrored blocks, hidden parent layers, cached Breps, missing geometry, bad input and cancellation.
- Real `.3dm` merge test: 24,000 same-color parts merge into per-color batches split at the vertex cap, with index offsets and winding intact.
- Unity runtime loader: 5 merged meshes (15 source objects, 5 colors), correct sample dimensions, unit root scale, placement unchanged; rendered preview inspected ([preview](rhino-import.png), [result](rhino-import.txt)).
- Simulator: `.3dm` sample loaded, healthy tracking, dimensions and import summary displayed; old GLB placement rejected after the model changed.

Run pure reader tests on Windows with .NET SDK 10: `dotnet run --project Tools/RhinoImportTests/RhinoImportTests.csproj -- TestResults/RhinoFixtures`. In Unity, stop Play mode and choose **Lab Walk > 4. Test 3DM import** for the independent import/render check. The test uses the bundled Rhino sample regardless of the selected manifest. **Lab Walk > 5. Check a 3DM file...** loads any file through the same runtime loader and writes `TestResults/model-check.txt` with top and eye-level previews.

### Architecture lab file check (2026-09-24, desktop Editor only)

`Handley_B1_15_Duct_Connections.3dm` (90,651,806 bytes, feet). Before merging, the reader rejected it: its 2,309 visible objects expand to 10,019 Brep-face meshes, over the former 10,000-mesh cap. After merging: 70 meshes, 372,265 vertices, 611,656 triangles, nothing missing, 121 labels/curves not shown; parse 0.8 s standalone, Unity load 4.2 s in the Editor. Bounds 19.76 x 3.25 x 18.38 m, Rhino origin at the floor near the southwest wall corner. The "Overhead 08 - provisional" parent layer is hidden in the file, so ceilings, beams, lights and upper ducts are not imported. This is not a Quest measurement.

Still required: the actual architecture lab file, Android native import on a physical Quest 3S, scale against tape measurements, large-model memory/frame rate, shader stereo appearance, alignment and anchor restoration. Existing headset checks remain in `VALIDATION.md`.

## Official references

- [McNeel Rhino3dm source and supported APIs](https://github.com/mcneel/rhino3dm/tree/8.35.0)
- [Rhino3dm native Android build target](https://github.com/mcneel/rhino3dm/blob/8.35.0/src/librhino3dm_native/CMakeLists.txt)
- [OpenNURBS overview](https://developer.rhino3d.com/en/guides/general/rhino-technology-overview/)
- [Rhino rendering assets](https://developer.rhino3d.com/guides/opennurbs/accessing-rendering-assets/)
