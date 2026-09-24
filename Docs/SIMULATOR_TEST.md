# Simulator test — updated 2026-09-24

Environment: Unity 6000.0.66f2, Meta Core 205, Meta XR Simulator 205.0, OpenXR 1.18.0, Windows. The runtime reported **Meta Quest 3**, not a verified Quest 3S profile. These observations are from a synthetic room and the bundled sample GLB.

## Fixes made

- At scene startup, `XRSettings.isDeviceActive` could still be false even with an initialized XR loader. Lab Walk selected desktop preview and disabled its Quest rig. It now keeps the rig when an XR loader is present.
- Editor runs enable `Application.runInBackground`, allowing frames to advance while the simulator window has focus.
- The status panel identifies this path as **EDITOR XR**. Saving says **Saved locally**.
- Missing-anchor queries distinguish API failure from a successful query that returned no anchor. The latter no longer appends the misleading word “Success.”

The user's recommended Meta project fixes and simulator installation were preserved. No project-wide Configure reset was run.

## Results

| Check | Observed result |
|---|---|
| C# compilation | Clean after startup fix |
| Runtime path | `editorPreview=False`, tracking healthy, XR session FOCUSED |
| Rendering | Frames submitted; alignment panel visible over synthetic passthrough |
| GLB dimensions | Panel reported 6.000 × 3.100 × 8.000 m |
| Reference A | Simulated right trigger advanced Origin → Direction |
| Reference B | Controller moved 2 m; trigger advanced Direction → Adjust without baseline warning |
| Save | A invoked anchor creation/save; SDK reported success; app entered Pinned |
| Immersive view | X displayed the imported sample room; composited image inspected |
| Return to passthrough | X returned to passthrough; placement remained `(0,0,-1.67)` at displayed precision, identity rotation, scale `(1,1,1)` |
| Restore after Play-mode restart | **Not successful:** query returned success but no matching anchor; app entered Recovery |
| Recovery input | B returned Recovery → Origin and requested new reference A |

The floor-reference coordinates above describe synthetic controller placement. They are not measurements of the real lab. The returned controller grip/aim mapping produced an offset from the requested OpenXR pointer pose; both references preserved the intended 2 m separation. Physical controller pointing accuracy still needs testing.

## Follow-up environment check — 2026-09-24

- Unity was open with clean compilation and a responsive Unity bridge. Play mode was stopped, and the simulator runtime was inactive in this editor session even though the simulator process existed.
- Activated the simulator for this session and entered Play mode. XR Operator connected and reported a FOCUSED session with projection and passthrough layers submitted.
- Lab Walk reported `editorPreview=False`, healthy tracking, and **Pinned / Placement restored**. The composited image showed the sample model and its 6.000 × 3.100 × 8.000 m dimensions.
- Exited and re-entered Play mode once more. The saved anchor again restored, with healthy tracking. These are two successful simulator restorations on this date; the earlier empty-query failure above remains unexplained.
- No Quest was listed by Android device discovery. The editor was left running Lab Walk in the simulator.

## Remaining verification

### Direct 3DM follow-up

The startup model was changed to the generated `sample-room.3dm` (Rhino millimeters). In the live simulator it loaded 15 meshes / 180 triangles and displayed bounds of 6 × 3.1 × 8 m. Tracking was healthy and `editorPreview=False`. The old GLB placement was correctly rejected because the file/manifest fingerprint changed.

Simulated trigger presses at two references 2 m apart advanced Origin → Direction → Adjust, without a scale discrepancy warning. A saved a new local anchor and entered Pinned. X entered immersive VR; the Rhino room and its basic-color shader were visible in the composited image. X returned to passthrough. This exercises the Windows native reader and simulator rendering; it does not test the Android native binary on Quest.

The earlier empty-query failure and later successful restorations show mixed simulator results. Reliable persistence across app/headset restarts and physical room relocalization is **not validated on Quest**. Simulator restoration is not evidence of physical registration accuracy.

Still required: Quest 3S installation and cold launch, actual lab GLB, physical scale measurements, room registration, drift, anchor restoration across app/headset restarts, controller comfort, safety boundary behavior, and device frame rate. The simulator's displayed FPS is not a Quest performance benchmark.

## Build distinction

The September 24 development APK includes these source fixes and the user's updated project settings. It was rebuilt successfully and its Android signature verified. See `BUILD_RECORD.md` for its size and SHA-256. Building successfully does not validate behavior on a headset.
