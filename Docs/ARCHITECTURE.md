# Architecture

The scene contains one stationary Quest rig, a light and `LabWalkApp`. Unity editor setup creates it using the Meta SDK's official rig prefab. The runtime app has a small state machine: loading → first reference → second reference → adjustment → saving → pinned. Restoration failure enters a recovery state with retry and realign controls.

## Coordinate ownership

```text
Scene world / Quest Stage origin
  Quest rig                       always identity, scale 1
  OVRSpatialAnchor                native tracking updates its world pose
    Model placement               saved pose relative to anchor, scale 1
      Imported model              one explicit unit conversion
        Model meshes              importer handles handedness and object transforms
```

During alignment `Model placement` is detached from the old anchor while preserving its world pose. `AlignmentMath` solves yaw and translation from two corresponding floor points. It never fits scale, pitch, or roll. Fine yaw adjustments pivot around model reference A. The floor pointer intersects Stage's y=0 plane; floor height is initially supplied by headset setup and can be corrected by moving the model.

Switching passthrough/VR changes the passthrough layer visibility and camera background only. The rig and model poses remain untouched. Physical head movement provides locomotion. The app does not disable the system boundary, reconstruct rooms, detect physical obstacles, or provide redirected walking.

## Loading and units

`ModelFiles` selects an app-local `Models` override or bundled StreamingAssets. It reads the manifest and one GLB or 3DM. `ModelLoaderFactory` selects an `IModelLoader`: glTFast for GLB, or `Rhino3dmModelLoader` for direct Rhino import. The Rhino reader extracts mesh data on a worker thread and creates Unity meshes on the main thread. Imported cameras and lights cannot replace the application camera or lighting. Resources live as long as `LoadedModel` and are disposed on teardown. [3DM scope and native dependencies](RHINO_3DM.md).

The loader reports bounds in model-placement coordinates after unit conversion, independent of world alignment. `coordinateUnits` explicitly distinguishes standard meter GLBs from source-unit coordinates; rhinoDocument reads units from a 3DM. Reference points remain in Unity-local meters under either import policy. The file-size cap is 150 MB, a guard for this initial loader; it is not a Quest memory or performance guarantee. Textures and decoded geometry can require substantially more memory than the file size.

## Anchor persistence and recovery

`MetaAnchorService` uses the Core 205 API: `OVRSpatialAnchor`, `SaveAnchorAsync`, `LoadUnboundAnchorsAsync`, `LocalizeAsync`, `TryGetPose`, and `BindTo`. Creation waits for a created/localized anchor. Restoration first localizes the native anchor and obtains its pose; it never reuses coordinates from an old Unity tracking origin as a substitute.

`PlacementStore` writes an anchor UUID, a model fingerprint, a relative pose, and a timestamp. SHA-256 covers the exact model and manifest bytes, so changing either forces alignment again. The native anchor is saved before metadata is committed. The previous placement record remains until the new record is written through a temporary file and replace operation. Previous anchors are erased after the new placement succeeds; cleanup failures are logged without undoing the new save. This is one placement per headset, with no sharing or account system.

A restored model starts in passthrough for inspection. Tracking loss exits immersive mode and hides the model until tracking returns. The user must explicitly re-enter VR. Small tracking drift is not automatically measurable with this app; B restarts manual alignment. The scaffold uses one anchor for the whole model. Accuracy across a large lab must be measured before considering a multiple-anchor design.

## Extension points

- Extend 3DM material fidelity or add a separate meshing stage for files without cached render meshes. Direct 3DM mesh reading is implemented; general NURBS tessellation is not.
- Add a model picker by generalizing `ModelFiles` and using a placement record per fingerprint.
- Change calibration capture independently of the rigid alignment solver if controller-touch references or scene-floor support become useful.

These are future options. The current slice includes one selected model (GLB or 3DM), one alignment, one persisted anchor and two viewing modes.
