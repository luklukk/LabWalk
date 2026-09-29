# Lab Walk

A small Unity app for a standalone Quest 3S architectural walkthrough: load a Rhino `.3dm` or exported `.glb` at 1:1 scale, place it automatically from printed QR codes, and switch from passthrough to immersive VR.

**Status:** imported, compiled and built for Android in Unity 6000.0.66f2. Built APKs are published as **GitHub Releases** (not committed); each release lists the APK's SHA-256 and MD5. For organization-managed headsets, add this permanent link in Meta Horizon Device Manager; it always downloads the newest release (from v0.5.0): https://github.com/luklukk/LabWalk/releases/latest/download/LabWalk-Quest3S.apk Apps added by link do not update themselves: add each new release's link again. Every release must raise the Android version code and be signed with the same key. The actual runtime loader passes desktop checks for sample dimensions, rigid alignment and placement preservation when switching views. Physical Quest behavior is untested. See [validation](Docs/VALIDATION.md) for completed checks and remaining device tests.

## Start here

For another development agent, read the [project handoff](Docs/HANDOFF.md): current behavior, verified progress, proposed next steps, and build/connection details. The in-headset import it lists as missing now exists; see the [import guide](Docs/IMPORT.md).

**Direct Rhino import:** the bundled startup sample is now `.3dm`. Units are read from the Rhino document; meshes, saved Brep/extrusion render meshes and embedded blocks are supported. Materials use basic colors, and missing geometry is reported. Follow the [3DM import guide](Docs/RHINO_3DM.md) to add the lab. The Android reader is built into the included APK; physical Quest testing is still pending.

**Simulator update:** XR startup, background execution and anchor diagnostics are fixed in the source and included APK. Controller-driven alignment, simulated saving, and view switching were exercised. Anchor restoration initially failed, then succeeded on two subsequent simulator launches. See [simulator results](Docs/SIMULATOR_TEST.md) and the September 24 [build record](Docs/BUILD_RECORD.md). Physical Quest testing is still required.

1. Install **Unity 6000.0.66f2** through Unity Hub, including **Android Build Support**, **Android SDK & NDK Tools**, and **OpenJDK**. Activate a Unity license appropriate to your use. [Official editor release](https://unity.com/releases/editor/whats-new/6000.0.66f2).
2. In Unity Hub choose **Add > Add project from disk** and select this `LabWalk` directory. Let Package Manager finish resolving the pinned dependencies. Internet access is needed for this first import.
3. The included scene and settings are already configured. Open `Assets/LabWalk/Scenes/LabWalk.unity`. Use **Lab Walk > 1. Configure project and create scene** only to recreate missing setup: it preserves an existing scene but reapplies the original project presets, including XR settings. Restart the editor if Unity requests it after an input setting change.
4. Open the generated scene and press Play. With no XR device active, the desktop preview supports importing the GLB, placing references and measuring on the floor. Persistent anchoring is intentionally unavailable in desktop preview.
5. Switch the build platform to **Android** in **File > Build Profiles**. Inspect **Meta XR > Project Setup Tool** for required issues, then select **Lab Walk > 2. Build Quest APK**. The output is `Builds/LabWalk-Quest3S.apk`.
6. Enable Developer Mode on your Quest, connect USB, and accept its USB debugging prompt. Follow [setup and deployment](Docs/SETUP.md) to install the APK.

Generated scene/settings, asset metadata and `Packages/packages-lock.json` are included. Keep these files in version control to preserve the resolved configuration. If this project was already open during setup, close and reopen it to load the updated dependencies and settings.

## First walkthrough

**Placement (0.8.0):** the model places itself. When the app starts or a model is loaded, the passthrough camera looks for the printed QR codes; after two are found the model appears, locked to the room. More sightings keep refining it. Nothing else about placement is shown unless it is needed.

**Controls once placed** (labels on the controllers show them; the **Menu** button hides or shows the labels):

| Button | Action |
|---|---|
| Left grip | Menu: View (layers), Fine-tune placement, Change model |
| Right trigger | Point at an item (a tool, a bench, a duct run) to make it wireframe, or solid again |
| X | Enter VR / back to passthrough |
| Y | Hide / show the model |
| Left stick click | Next design option (e.g. Existing / Renovation) |

**Fine-tune placement** (the first row of the View menu) holds everything else about placing the model: the right stick slides the model, the left stick turns and raises it, **B** re-snaps to the QR markers, the right trigger places it with two floor points instead, the right grip measures a floor distance, and the status panel shows each marker and the fit. **A** is done.

A model without QR markers opens straight into floor-point placement: aim at floor reference A and pull the right trigger, then B. Placement is not saved between launches; the QR codes place the model again each time. On tracking loss the app returns to passthrough; VR must be re-entered.
## Add the real lab

**In the headset (no rebuild):** copy the `.3dm` into the app's Import folder or pick it with **Browse headset files**, then press the **left grip** to open **Models**, review and confirm. Name two Rhino floor points `LabWalk reference A` / `B` for alignment. See the [import guide](Docs/IMPORT.md), which also describes the prepared architecture-lab file `Handley_B1_16_LabWalk.3dm`.

**Automatic placement:** tape printed QR codes to the walls and add matching `LabWalk marker <ID>` points to the Rhino file; the headset then places the model itself. See the [marker guide](Docs/MARKERS.md).

**Layers, design options and wireframes:** the left grip opens **View**, which shows the Rhino layer tree with **Solid / Wire / Off** for every layer; name layers `Option: Existing` / `Option: Renovation` to make them exclusive design options. Point the right controller at any object and pull the trigger to make just that object a wireframe. See the [layer guide](Docs/LAYERS.md).

**Developer route (bundled or pushed manifest):** for direct import, put `lab.3dm` in `Assets/StreamingAssets/Models` and copy `lab.3dm.example.json` over `model.json`. Set the two floor references using the [3DM geometry and units guide](Docs/RHINO_3DM.md). To switch sample formats, copy `sample-room.3dm.json` or `sample-room.glb.json` over `model.json`.

Follow [Rhino export and scale](Docs/RHINO_EXPORT.md). Put your self-contained `lab.glb` beside `Assets/StreamingAssets/Models/model.json` and edit that manifest, using `lab.example.json` as a guide. Reference A and B must correspond to physical floor landmarks in the real lab. They are specified in imported Unity-local **meters**.

For later model changes without rebuilding the APK, use `Tools/Deploy-Quest.ps1 -ModelFolder ...`. The app first checks its local `files/Models/model.json`; when absent, it uses the bundled sample. Only one model and one saved placement are supported in this milestone.

## Project map

```text
Assets/
  LabWalk/
    Core/          Engine-independent unit conversion and rigid alignment
    Runtime/       GLB loading, anchor persistence, controls, status display
    Editor/        Scene/settings generation and Android build command
    Scenes/        Generated by Configure
    Settings/      Generated by Configure
  StreamingAssets/Models/
    sample-room.glb
    sample-room.3dm
    model.json
    lab.example.json
Packages/manifest.json
ProjectSettings/ProjectVersion.txt
Docs/              Setup, Rhino export, architecture, validation/checklist
Tools/             Sample generator, math tests, USB deployment
```

Run `pwsh -File Tools/Test-Core.ps1` from the project folder for the standalone math tests. `node Tools/Generate-Sample.mjs` regenerates the GLB sample. `Tools/RhinoImportTests` contains real 3DM round-trip tests and the Rhino sample generator; see the 3DM guide for instructions.

The [architecture note](Docs/ARCHITECTURE.md) describes extension points. The [milestone checklist](Docs/VALIDATION.md) records verification and physical Quest testing still required. **Lab Walk > 3. Run desktop smoke test** loads the bundled sample, checks dimensions and alignment, and writes a preview image and result in `TestResults`. Run this before replacing the sample with the real lab.
