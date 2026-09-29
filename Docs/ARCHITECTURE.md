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

## Placement and anchoring

Since 0.8.0 placement is not persisted. `MarkerCalibrator` (camera QR scanning) places the model at every launch: `LabWalkApp` starts in `Searching`, applies the markers' fit once two are steady, and keeps refining silently until every marker is steady or the placement is fine-tuned by hand. Models without markers use two floor points (`PointA`/`PointB`), also reachable from **Fine-tune placement**.

`MetaAnchorService.CreateAsync` makes an unsaved (session) `OVRSpatialAnchor` at the placement, and the placement is parented to it, so the model stays locked to the room without a boundary (stage space) and through recenters. It is released when placement restarts.

Tracking loss exits immersive mode and hides the model until tracking returns; VR must be re-entered explicitly. One anchor holds the whole model; accuracy across the large lab should be checked against the markers (Fine-tune shows the fit).
## Extension points

- Extend 3DM material fidelity or add a separate meshing stage for files without cached render meshes. Direct 3DM mesh reading is implemented; general NURBS tessellation is not.
- Add a model picker by generalizing `ModelFiles` and using a placement record per fingerprint.
- Change calibration capture independently of the rigid alignment solver if controller-touch references or scene-floor support become useful.

These are future options. The current slice includes one selected model (GLB or 3DM), one alignment, one persisted anchor and two viewing modes.
