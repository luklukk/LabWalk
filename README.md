# Lab Walk

A small Unity app for a standalone Quest 3S architectural walkthrough: load a Rhino `.3dm` or exported `.glb` at 1:1 scale, align two floor references, save a spatial anchor, and switch from passthrough to immersive VR.

**Status:** imported, compiled and built for Android in Unity 6000.0.66f2. Built APKs are published as **GitHub Releases** (not committed); each release lists the APK's SHA-256 and MD5. For organization-managed headsets, add the release's APK download link in Meta Horizon Device Manager. Apps added by link do not update themselves: add each new release's link again. Every release must raise the Android version code and be signed with the same key. The actual runtime loader passes desktop checks for sample dimensions, rigid alignment and placement preservation when switching views. Physical Quest behavior is untested. See [validation](Docs/VALIDATION.md) for completed checks and remaining device tests.

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

The original sample room has a 6 m by 8 m footprint, 3 m walls, a desk, and a 1 m calibration cube. Its two yellow reference markers are at model coordinates `(0, 0, 0)` and `(0, 0, 2)` in Unity meters. The floor slab extends 0.1 m below the floor, so the overall reported Y bound is 3.1 m.

1. Place two physical floor marks 2 m apart within your usable boundary. Set the headset floor level correctly.
2. In passthrough, aim the cyan pointer at the first mark and press the **right trigger**; repeat at the second mark. The pointer intersects the headset's horizontal floor plane. It does not scan surfaces.
3. Use the **right stick** to slide the model relative to where you face. Use the **left stick horizontally** for yaw and **vertically** for height. Adjustments move the model; scale stays fixed. **Y** hides/shows the model to inspect the physical room.
4. Press the **right grip** at each end of a known floor distance. The green measurement and model bounds provide a scale check. For the sample, compare the two yellow marks against a tape-measured 2 m span.
5. Press **A** to save. A reference-distance discrepancy over 5% blocks saving and asks for realignment or corrected units. This is a basic mistake check, not a precision guarantee.
6. Inspect physical landmarks, then press **X** to enter VR. Walk physically within the system boundary. Press X again for passthrough, **Menu** for the status panel, or **B** to realign.

After a restart, the app attempts to restore the saved anchor, starting in passthrough. If it cannot localize, **A** retries and **B** starts a new alignment. A changed model or manifest requires realignment. On tracking loss the app returns to passthrough and hides content whose placement cannot be trusted; VR must be explicitly re-entered.

## Add the real lab

**In the headset (no rebuild):** copy the `.3dm` into the app's Import folder or pick it with **Browse headset files**, then press the **left grip** to open **Models**, review and confirm. Name two Rhino floor points `LabWalk reference A` / `B` for alignment. See the [import guide](Docs/IMPORT.md), which also describes the prepared architecture-lab file `Handley_B1_16_LabWalk.3dm`.

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
