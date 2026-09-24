# Lab Walk — development handoff for Astra, Opus, or another coding agent

Prepared 2026-09-24. This document is a self-contained project summary and continuation guide. It describes implemented behavior, recorded verification, and proposed next work. The user's present request was to write this handoff; further implementation was not performed as part of that request.

## Read this first

Lab Walk is a Unity application targeting standalone Meta Quest 3S. It loads an architectural model at true size, aligns it with two physical floor references, saves placement with a spatial anchor, and switches between passthrough and immersive VR. Physical walking provides movement.

The latest APK includes working file readers for `.glb` and a limited subset of `.3dm`, plus a generated Rhino sample room. It has **no in-app Import button, file picker, model browser, or interactive setup of model reference points**. A developer currently chooses the model and references through `model.json` and transfers the files or rebuilds the APK.

This distinction matters to the user. They asked for `.3dm` import, then discovered that the last build implemented the reader without a user-facing import workflow. The earlier assistant explicitly acknowledged that describing this broadly as “direct import” overstated what the user can do inside the app. The most useful next implementation is an in-headset import and calibration workflow.

**No physical Quest 3S tests have been completed in this conversation.** A successful Android build and simulator run do not establish Android native-library execution, real-world scale, room registration, persistence, comfort, or device performance. The actual architecture-lab model has not been supplied.

## User intent and working situation

The first real model is a 1:1 recreation of the user's architecture lab. They want to stand in the physical lab, align the digital lab with it, pin the placement, and walk through the virtual version. Keep the product small: no architectural editing, collaboration, multiuser features, cloud accounts, or Arkio-style extras.

The user accesses this Windows workstation through Parsec from another computer. Their headset may be physically connected to that client. USB attached to the client must not be assumed visible to tools on this workstation. The standalone APK can be transferred to the client and installed onto the headset there using Meta Quest Developer Hub or ADB. The installed application runs on the headset without Unity, XR Simulator, Parsec, or Quest Link running. Quest Link is a separate PC streaming workflow.

One earlier answer said the actual lab file was needed to prepare a new build. That is only one option: the existing app can also use a replacement `.3dm` plus `model.json` copied to its external app-data folder, without rebuilding. Neither method is currently an in-headset import experience.

The user prefers direct progress and clear language. Avoid asking them to reconfirm routine reversible work. Distinguish “implemented,” “built,” “simulator checked,” and “verified on Quest.”

## Project location and deliverables

On the current workstation:

```text
Workspace:       C:\Users\Luke\Documents\Codex\2026-09-22\i-wa
Unity project:   <workspace>\outputs\LabWalk
Project archive: <workspace>\outputs\LabWalk-Scaffold.zip
APK:             <project>\Builds\LabWalk-Quest3S.apk
Build staging:   <workspace>\work\QuestBuild
Rhino sources:   <workspace>\work\rhino
```

All remaining source paths in this document are relative to the Unity project unless marked `workspace`. The project archive contains the source, pinned packages/settings, native import libraries, documentation, and APK. It excludes Unity caches and local editor connection credentials. The workspace `work` helpers are not in that archive; they are conveniences for this machine.

Latest APK, rechecked while preparing this handoff:

| Property | Value |
|---|---|
| Filename | `LabWalk-Quest3S.apk` |
| Size | 95,756,524 bytes |
| SHA-256 | `5A9BBEF5D81BC7B98F107B065279D2090D473B42F578C480D36FBFBB293577A8` |
| Application ID | `com.architecturelab.labwalk` |
| Version / code | `0.1.0` / `1` |
| Build | Development, IL2CPP, ARM64, Vulkan |
| Android API | Minimum 32; target 34 |
| Signing | Local Android development key; signature v2 verified |
| Bundled default | `sample-room.3dm`, with its manifest |

Production signing/store publication is not configured. Do not identify an older APK by filename alone: the filename has remained constant across builds. Update the build record and archive hash after future builds.

## Implemented behavior

| Area | Current behavior |
|---|---|
| Model selection | One startup model selected in JSON; app-data override takes precedence over the bundled model |
| GLB reader | glTFast runtime import through `IModelLoader`; mesh content only |
| 3DM reader | Rhino3dm reads explicit meshes and saved render meshes, with embedded block transforms and basic colors |
| Scale | Convert once to meters; diagnostic dimensions and a floor-distance ruler; never fit model scale during alignment |
| Alignment | Two physical floor-ray captures corresponding to preconfigured model references; solve yaw and translation |
| Fine adjustment | Controller sticks adjust horizontal position, yaw and height |
| Placement | Local spatial anchor plus UUID, model fingerprint, and relative model pose |
| Restoration | Load/localize saved anchor; restore relative pose; retry/realign on failure |
| View modes | Passthrough alignment and immersive VR share the same model placement |
| Walking | Stationary Stage-origin rig; physical movement; normal headset boundary retained |
| Diagnostics | Bounds, approximate FPS, import summary/errors, missing geometry, anchor/tracking status, floor measurement |
| Desktop development | Preview path without XR; simulated XR path with Meta XR Simulator; Unity smoke-test menus |

Current headset controls:

- Right trigger: capture reference A, then B.
- Right stick: move the model horizontally; left stick horizontal: yaw; left stick vertical: height.
- Right grip: capture endpoints of a floor measurement.
- A: save placement or retry restoration; B: realign.
- X: toggle passthrough/VR; Y: hide/show model; Menu: status panel.

The baseline must be at least 25 cm; 2 m is preferable. A reference-length mismatch over 5% blocks saving. This catches some scale/point mistakes but is not a precision guarantee. The floor ray intersects the tracked horizontal floor plane; it does not scan a physical surface. Tracking loss returns to passthrough and hides untrusted content. Re-entering VR is explicit.

The sample room has a 6 × 8 m footprint, 3 m walls and a 0.1 m slab below floor level, so total bounds are 6 × 3.1 × 8 m. It contains 15 mesh instances, 180 triangles, a desk, a 1 m cube, and reference markers at Unity-local `(0,0,0)` and `(0,0,2)`.

## 3DM support and important limitations

The reader uses Rhino3dm 8.35.0 and OpenNURBS. It runs without Rhino installed or a conversion service.

- Supports explicit meshes, saved Brep-face render meshes, saved extrusion render meshes, embedded block recursion, mirrored/nonuniform block transforms, and object/layer visibility.
- Preserves the document origin. Rhino `(X,Y,Z)` maps to Unity `(X,Z,Y)`; units are converted to meters. Winding and normals are corrected.
- Uses opaque basic diffuse/display colors. Textures, transparency, PBR/procedural materials, decals and vertex colors are not reproduced.
- Does not tessellate arbitrary NURBS or SubD. Curves, points, annotations and unsupported geometry are omitted with diagnostics. External linked files are not fetched; blocks should be embedded.
- Missing/unsupported geometry produces an `INCOMPLETE` summary and forces the status panel visible. This is a warning, not a hard prohibition on entering VR. Up to 32 detailed omission messages are logged.
- No visible meshes is an import error. For a suitable Rhino file, generate and save render meshes; avoid Save Small. Explicit mesh geometry is the most predictable input for this version.

Limits are 150 MB input, 2 million expanded vertices, 4 million triangles using a conservative face budget, 200,000 source surface faces/meshes (merged into per-color Unity meshes), 100,000 object/instance visits and 32 block nesting levels. Coordinates beyond 100 km from the origin are rejected. Native parsing can allocate significantly more memory than the input size and cannot be cancelled mid-call. Extraction checks cancellation; Unity creation yields once per mesh. These guards do not establish suitability for a large architectural model.

The managed assembly is .NET Standard 2.0. Windows Editor uses the official Windows x64 native DLL. Quest uses a locally compiled Android ARM64 library. macOS/Linux Editor and standalone Windows player packaging are not configured.

Android build details and hashes are in `Docs/RHINO_NATIVE_BUILD.md`. The source is pinned to Rhino3dm commit `f44a7887955d471ad3e52327a5a410a96aad7957` and OpenNURBS commit `eb92af3ba1806b0a34a99aba0d3bda83e3d46083`. The native build disables unused FreeType font support and explicitly links static libc++; it is not an unmodified official Android release binary. `Tools/Build-RhinoNative.ps1` reproduces those changes. Keep dependency notices and platform-specific plugin `.meta` files.

## Loading, units and saved state

`ModelFiles.Folder` first looks for `Models/model.json` under `Application.persistentDataPath`, otherwise it uses `StreamingAssets/Models`. On Quest, the external override folder is:

```text
/sdcard/Android/data/com.architecturelab.labwalk/files/Models
```

It must contain `model.json` and the named model file. An override survives ordinary APK updates. Removing/renaming just the override manifest returns selection to the bundled model. `Tools/Deploy-Quest.ps1` installs the APK unless `-SkipInstall` is passed; `-ModelFolder` transfers the model first, then the manifest, and restarts the app. Run it on the computer that can actually see the headset via ADB.

Example manifest for a lab with known floor references:

```json
{
  "schemaVersion": 1,
  "modelId": "architecture-lab",
  "displayName": "Architecture lab",
  "file": "lab.3dm",
  "sourceUnits": "fromDocument",
  "coordinateUnits": "rhinoDocument",
  "referenceA": { "x": 0, "y": 0, "z": 0 },
  "referenceB": { "x": 0, "y": 0, "z": 2 },
  "knownDistanceMeters": 2
}
```

These reference coordinates are examples, not measurements of the actual lab. All reference positions are final Unity-local meters. Rhino `(0,2000,0)` in millimeters becomes Unity `(0,0,2)`. `rhinoDocument` reads file units; unitless/custom units require the explicit `sourceUnits` policy and supported units. GLB normally uses `gltfMeters`, preventing a second source-unit conversion. Do not assume a `.3dm` conversion and an unrelated GLB export have identical axis/origin conventions.

Only one saved placement exists, at `Application.persistentDataPath/placement.json`. It contains an anchor UUID, SHA-256 fingerprint of model+manifest bytes, timestamp and pose relative to the anchor. Atomic metadata replacement is implemented. Model/config changes force realignment. The app saves the replacement before erasing the previous anchor. There is no per-model placement library yet.

Coordinate ownership is deliberate:

```text
Quest rig / Stage origin: always identity and scale one
Spatial anchor: native tracking controls its world pose
  Model placement: saved relative pose, scale one
    Imported geometry: converted to meters
```

Alignment temporarily detaches model placement while preserving its world pose. View switching changes passthrough/background visibility, not this transform chain. Maintain those invariants when adding import or preview UI.

## Code map

| File or folder | Responsibility |
|---|---|
| `Assets/LabWalk/Runtime/LabWalkApp.cs` | Startup loading, state machine, controls, alignment orchestration, HUD |
| `Runtime/IModelLoader.cs` | Loader interface and `LoadedModel` lifetime/diagnostics |
| `Runtime/GlbModelLoader.cs` | glTFast implementation |
| `Runtime/Rhino3dmModelLoader.cs` | Unity meshes/materials; also currently contains `ModelLoaderFactory` |
| `Runtime/RhinoModelReader.cs` | Native 3DM parsing and geometry extraction without Unity objects |
| `Runtime/ModelManifest.cs`, `ModelFiles.cs` | Input validation, units policy, byte loading and fingerprinting |
| `Runtime/MetaAnchorService.cs`, `PlacementStore.cs` | Anchor creation/localization and local saved metadata |
| `Runtime/WalkthroughView.cs` | Passthrough/VR, panel and reference/measurement visuals |
| `Assets/LabWalk/Core/AlignmentMath.cs` | Engine-independent rigid alignment and unit conversion |
| `Assets/LabWalk/Resources/RhinoBasic.shader` | Basic opaque Rhino material with stereo macros |
| `Assets/LabWalk/Editor/ProjectSetup.cs` | Scene/project generation; reapplying it can overwrite later settings |
| `Editor/QuestBuild.cs` | Android development build entry point |
| `Editor/BuildSanitizer.cs` | Clears unused Meta DevAgent credentials during build |
| `Editor/EditorSmokeTest.cs`, `RhinoImportSmokeTest.cs` | Existing runtime checks and preview capture |
| `Assets/Plugins/Rhino3dm` | Managed, Windows native and Android native dependencies |
| `Assets/StreamingAssets/Models` | Selected manifest, both samples, manifest templates |
| `Tools/Deploy-Quest.ps1`, `Build-RhinoNative.ps1` | Deployment and native dependency rebuild |
| `Tools/RhinoImportTests` | .NET 10 reader tests and Rhino sample/fixture generation |

Rows abbreviated `Runtime/` or `Editor/` above are under `Assets/LabWalk/`.

`LabWalkApp.Start()` performs loading inline and transitions through Loading, Origin, Direction, Adjust, Saving, Pinned, Recovery and Error. There is no reusable runtime model-switch controller yet. Refactoring loading out of `Start()` is a likely first change for an in-app importer.

## What has actually been verified

Recorded tests from September 23–24; this handoff preparation inspected source/docs and rechecked the APK identity, but did not rerun headset or simulator sessions.

1. Core C# unit/alignment tests passed nine groups, including 1,000 random rigid transforms.
2. Khronos validation of the generated GLB returned zero errors/warnings. Unity actually loaded/rendered it; dimensions, references, unit placement scale and view-switch pose invariance passed desktop checks.
3. Real 3DM write/read fixtures passed unit/axis conversion, quad triangulation, colors, normals, mirrored blocks, hidden parent layers, cached Breps/extrusions, missing geometry, invalid headers and cancellation checks. This is synthetic coverage, not evidence about the user's lab file.
4. Unity's Rhino loader imported 15 meshes with 6 × 3.1 × 8 m bounds and unchanged parent placement. A rendered preview was inspected.
5. Meta XR Simulator exercised controller alignment, saving, immersive rendering and return to passthrough for the sample. Changing from GLB to 3DM correctly invalidated old placement. The observed simulator profile was Quest 3; Quest 3S profile behavior was not established.
6. Anchor restoration initially returned success with no matching anchor, then restored on two later simulator launches. The earlier failure remains unexplained. Do not claim reliable persistence.
7. Android IL2CPP build completed; APK signature, ARM64 payload and manifest were inspected. The Rhino native library, sample and third-party notices are packaged. Native ELF load segments have 16 KB alignment.

Relevant records: `Docs/BUILD_RECORD.md`, `SIMULATOR_TEST.md`, `VALIDATION.md`, `rhino-import.txt`, `rhino-reader-tests.txt`, `editor-smoke.txt`, and the preview PNGs. Some introductory lines in older records refer to GLB because that was the original milestone; later 3DM sections describe the added capability.

Remaining physical tests: installation/cold launch; Android Rhino P/Invoke and IL2CPP behavior; stereo rendering; actual lab import; tape-measured scale; reference pointing; alignment at distributed landmarks; anchor recovery after force-stop/reboot/room changes; drift; controller ergonomics; boundary behavior; memory, frame rate and thermal behavior. Simulator FPS is not a device benchmark.

## Proposed next implementation, in priority order

### 1. Finish the in-headset import experience

This is the most immediate product gap. Implement a simple reachable Import/Open model action and a way to return to the bundled sample. Support `.3dm` and retain `.glb` support. Keep one active model for the first iteration.

First investigate the file-selection mechanism actually available on the target Quest OS and Unity/GameActivity combination. Android's document picker is a candidate, not a verified Quest capability in this project. Consult current official documentation and confirm on a headset. If a system picker cannot be used, an in-app browser of a documented accessible import folder is a possible fallback; be clear how users place files there. Do not assume unrestricted Downloads access or `content://` URIs behaving like filesystem paths.

Introduce a small file-acquisition abstraction, then feed bytes into the existing readers. Handle cancellation, unreadable files, size limits and app pause/resume. Copy imported data into app-managed storage when appropriate so later launches do not depend on a temporary permission or removable location. Derive display name and document units automatically. Generate configuration internally; ordinary users should not edit JSON.

Refactor model switching into an explicit load operation. Enter passthrough during replacement; provide progress and useful errors. Avoid exposing a half-loaded model. Preserve the previous usable selection on failed/cancelled import and commit the new selection only when usable. Account for peak memory when retaining an old model during a new import. Reset anchor association for changed content. Keep existing saved state until replacement succeeds.

Acceptance: from inside the headset, select a new supported file, inspect its name/units/dimensions/import warnings, cancel or retry, load it successfully, and reopen it after app restart without a Unity rebuild or manual JSON edits. This workflow has not been implemented.

### 2. Make model reference setup usable for arbitrary files

A file picker alone does not solve alignment. The current two model references are hard-coded in the manifest. A newly selected model will usually not have the sample's origin, floor height or reference span.

Provide a model preview or another clear way to select two identifiable model floor landmarks, followed by their corresponding physical points in passthrough. Explain the current step and support undo/reselect. A miniature preview may have its own transform, but final reference coordinates must be converted back into original model-local meters. Never change the real walkthrough scale to make references fit. Show a dimension/known-distance check before pinning. Decide what to do when units are absent, geometry is incomplete, or the two points are unsuitable.

Acceptance: a file with an offset origin and millimeter units can be aligned to two known room marks without editing the source model or manifest. Save/reopen retains both the model configuration and its correct anchor association.

### 3. Validate on Quest and with the real architecture lab

Install the current standalone APK first to expose Android-specific reader/rendering issues before adding more native dependencies. The user can do this through their local client. Obtain the actual lab file or a local path when available; do not fabricate dimensions/reference locations.

Then run the physical checklist in `Docs/VALIDATION.md`. Record headset OS, APK hash, file fingerprint and measurements. Resolve any crash, scale, persistence or tracking issue before declaring the experience ready for workshop walking. Repeat restoration across force-stop/reboot and failures; investigate the inconsistent simulator observation if it reproduces.

### 4. Improve fidelity and performance from real evidence

Profile the real file for parse time, expanded geometry, draw calls, peak memory and sustained device frame rate. Consider material sharing, repeated-mesh reuse, batching and simpler Rhino exports based on results. If the lab needs textures/transparency or unsupported surfaces, prioritize those specific requirements. General NURBS meshing requires a separate technical decision; do not imply OpenNURBS already supplies it. A Rhino-side preparation/export workflow may remain useful.

A model library with separate placements can follow if requested. Editing, collaboration, teleportation, networking and store distribution are not current requirements.

## Build and workstation continuity

Pinned baseline: Unity `6000.0.66f2` (revision `b20bc5da3050`), Meta XR Core `205.0.0`, OpenXR `1.18.0`, XR Management `4.6.1`, glTFast `6.20.0`, Unity UI `2.0.0`, Rhino3dm `8.35.0`. Simulator observed version: `205.0`. Retain `Packages/packages-lock.json`. Do not upgrade packages just because a newer version exists.

Unity executable:

```text
C:\Program Files\Unity\Hub\Editor\6000.0.66f2\Editor\Unity.exe
```

The editor's Android tools live under `Editor\Data\PlaybackEngines\AndroidPlayer`, including SDK platform-tools/ADB, build-tools `36.0.0`, NDK, OpenJDK, and CMake `3.22.1`.

**Preserve the user's applied Meta project fixes.** Standalone/Windows OpenXR, Meta features, Touch/proximity profiles, D3D11 and simulator/operator integration were enabled after the first scaffold. Some hand-tracking settings and defines changed too. The app itself remains controller driven. Do not rerun **Lab Walk > 1. Configure project and create scene** as a routine build step: it reapplies original presets. The old workspace helper `work/Finalize-Quest.ps1` invokes Configure and is not the safe default now.

Use **Lab Walk > 2. Build Quest APK** with Android active, or batch entry point `LabWalk.Editor.QuestBuild.Build`. If the source project is open, use the existing staging project: copy current `Assets`, `Packages`, and `ProjectSettings` from source to staging, retain its Library cache, and build there. Check for staging-only stale files when source files are removed. Never copy stale staging settings back over the user's source project. Return only the intended build artifact.

Batch example after preparing the isolated project:

```powershell
$editor = 'C:\Program Files\Unity\Hub\Editor\6000.0.66f2\Editor\Unity.exe'
$buildProject = 'C:\Users\Luke\Documents\Codex\2026-09-22\i-wa\work\QuestBuild'
$buildLog = Join-Path $buildProject 'Logs\next-build.log'
$arguments = @('-batchmode','-quit','-projectPath',('"'+$buildProject+'"'),
    '-buildTarget','Android','-executeMethod','LabWalk.Editor.QuestBuild.Build',
    '-logFile',('"'+$buildLog+'"'))
$process = Start-Process -FilePath $editor -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw 'Unity build failed; inspect the build log.' }
```

Retain these previous fixes:

- XR startup checks for an initialized loader as well as device activity; early `isDeviceActive=false` must not disable the rig.
- Editor background execution allows frames to advance while the simulator window has focus.
- All quality tiers received Quest settings; Android's default tier must not silently use another configuration.
- Do not add Unity's internal `GUI/Text Shader` to Always Included Shaders; doing so caused a serialization/build failure.
- GameActivity manifest and contextual passthrough loading screen were configured for Meta validation.
- `BuildSanitizer` clears unused generated DevAgent settings after Meta's injection hook. Meta recreates local connection data at editor startup. Never distribute `Assets/Resources/DevAgentSettings.asset` or its `.meta`, and never print discovery tokens or full potentially sensitive logs.

A known OpenXR warning reports Meta declaring API `1.1.45` while Unity recommends `1.1.54`; Unity ignores the lower requested patch. The build succeeded with this warning. Do not edit package source just to silence it.

For packaging on this machine, `workspace/work/Package-Quest.ps1` makes the deliverable ZIP and validates the APK hash. **It contains a hard-coded expected hash that must be updated after any APK rebuild.** It excludes caches, credentials and test bin/obj folders. Update `Docs/BUILD_RECORD.md` and verification records to match the delivered artifact.

Existing focused checks: `Tools/Test-Core.ps1`; `dotnet run --project Tools/RhinoImportTests/RhinoImportTests.csproj -- TestResults/RhinoFixtures`; Unity menu **Lab Walk > 4. Test 3DM import**. The general desktop smoke test requires Standalone XR initialization disabled temporarily; restore the user's setting afterward. Run checks appropriate to actual changes, and distinguish them from physical device results.

## Unity bridge and XR Operator

Both were connected successfully during prior work. The last observed app session was Pinned in simulator passthrough. Processes, ports and object IDs are transient: inspect them again before controlling anything.

- Native Meta XR Operator MCP tools may be available in the next agent's tool list. Cached descriptions have said OFFLINE even when calls worked. Successful session/frame queries establish a live connection. The backend normally listens at `http://127.0.0.1:8720/sse` during Play mode with OpenXR and Operator enabled.
- Proxy executable: `C:\Users\Luke\meta-xr-operator\meta-xr-operator-mcp-proxy.exe`; registered MCP server name: `meta-xr-operator`. It takes no arguments for the normal local backend. Passing `--help` was interpreted as a backend URL.
- The Unity bridge can work outside Play mode. Workspace helper `work/Invoke-UnityBridge.ps1` reads `%TEMP%\mcpbridge_*.info`, selects this project, and uses the token in memory. The observed port was 48736; use discovery rather than assuming it. Do not print the token or whole discovery file.
- The helper currently embeds the original project path; update its project selection if working from another path. It and `work/unity-bridge-tools.json`, `work/xr-operator-tools.json`, and `work/Probe-XROperator.ps1` are workspace-only tools, not archive contents.
- Known bridge operations: `CompilationTools/GetCompilationStatus`, `ForceRecompile`; `SceneObjectsTools/GetSceneHierarchy`, `GetComponentValue`; `IReflectionService/InvokeStaticMethodFromJson`; `DiagnosticTools/GetErrorSummary`. Ask ToolHelp for unfamiliar schemas.

Example from workspace root:

```powershell
& work/Invoke-UnityBridge.ps1 -Tool CompilationTools -Method GetCompilationStatus
& work/Invoke-UnityBridge.ps1 -Tool IReflectionService -Method InvokeStaticMethodFromJson -Arguments @{
    typeName='UnityEditor.EditorApplication'; methodName='get_isPlaying'; arguments='{}'
}
```

Reflection can invoke `UnityEditor.EditorApplication.EnterPlaymode` / `ExitPlaymode`, and `Meta.XR.Simulator.Editor.XRSimMenu.ActivateSimulator` / `ValidateDeactivateSimulator`. A restarted Unity editor may need simulator activation even when the simulator process is open. XR Operator generally becomes available only once the XR app is running. A compile/domain reload temporarily interrupts the bridge; allow it to recover instead of flooding retries. Find the `Lab Walk` GameObject anew before reading private fields such as `phase`, `editorPreview`, `trackingHealthy`, `message` or `anchorStatus`; use the short component name `LabWalkApp`.

Simultaneous Unity editors can log bridge-port collisions because the source editor already owns the ports. This happened in the isolated batch build and did not prevent APK creation. A Meta integration error about a missing `claude` executable also appeared; it is not evidence of a Lab Walk C# compilation failure. Separate these messages from actual application errors.

## Suggested continuation brief

When the user asks to proceed with implementation, start by reading this handoff and the current source. Prioritize a complete in-headset open/import flow and model-reference setup, preserving meter scale and anchor integrity. Verify the chosen Quest file-access mechanism before depending on it. Preserve the working package/settings baseline and native import libraries. Build a reviewable APK, document exactly what changed, and report desktop/simulator checks separately from physical headset checks. Request the real lab file/path or physical feedback when necessary, while continuing work that does not depend on them.
