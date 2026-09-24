# In-headset model import

Added 2026-09-24. Status: implemented and checked on the desktop (edit-mode pipeline test on the real lab file). **Not yet run on a Quest.** The Android system file picker in particular is unverified on Horizon OS.

## Using it

1. Get the model onto the headset, either:
   - **Import folder (always works):** copy a `.3dm` or `.glb` into `Android/data/com.architecturelab.labwalk/files/Import` with Windows file transfer over USB, Meta Quest Developer Hub's file manager, or `adb push <file> /sdcard/Android/data/com.architecturelab.labwalk/files/Import/`. The folder exists after the app's first launch (it contains a README).
   - **Browse headset files:** opens Android's system document picker. The chosen file is copied into the Import folder first. Meta does not document this picker for Quest; if it fails, the menu shows the error and the Import folder still works.
2. In the headset press the **left grip** to open **Models**. Move with either stick, **A** opens, **B** closes. The desktop preview uses **O**, arrows, Enter and Esc.
3. The model loads hidden, in passthrough (**B** cancels while loading). A review screen shows the name, source units, true-size dimensions, import summary and where the landmarks came from.
4. **A** makes it the active model; **B** cancels. Nothing is saved or replaced before A.
5. After A, the app looks for a saved placement for this exact file. If there is none, alignment starts at reference A as usual.

Choose **Bundled sample room** in the same menu to return to the sample. Imported source files stay in the Import folder.

## Landmarks (alignment references)

Put two **point objects** in the Rhino file named exactly `LabWalk reference A` and `LabWalk reference B`, on the floor at two physical features you can mark in the room, ideally 2 m or more apart. Any layer works, including hidden layers. Their coordinates become the alignment references automatically.

Without these points the app uses two floor corners of the model's bounding box and says so on the review screen. That lets you look at the model, but it is not a real alignment. GLB files can use nodes with the same names.

## What happens underneath

- `ModelImport.LoadFileAsync` reads the file (150 MB limit), loads it under an inactive staging object, reads the landmarks and generates `model.json` (units from the file; no hand-editing).
- On confirm, `ModelImport.ActivateAsync` copies the file and manifest into `files/Models.incoming`, writes `model.json` last, then swaps it in as `files/Models`. The old folder is deleted only after the swap. `RecoverInterruptedSwitch` repairs an interrupted swap at startup.
- The previous model stays active and visible to the code until the swap succeeds; a failed or cancelled import leaves it, its saved placement and its anchor untouched.
- The fingerprint is SHA-256 of model bytes plus the generated manifest. It is identical after a restart or a re-import of the same file, so the saved anchor restores. A different file or landmarks forces realignment. The old anchor is erased only after a new placement is saved (unchanged behavior).
- If the imported selection fails to load at startup, the app loads the bundled sample and says why.
- Peak memory during an import holds the old and new model together (the lab is roughly 620k triangles; not yet measured on device).

## Architecture lab file

`Handley_B1_16_LabWalk.3dm` (in the lab model's `Rhino Model/outputs` folder) was built from `Handley_B1_15_Duct_Connections.3dm` by `work/LabClean` (offline, Rhino3dm). The source file was not modified.

- Kept: walls, floor, glazing, windows, tool closet, all corrected tool/equipment layers, woodshop details/corrections, duct corrections, and the **Overhead 08** layers (beams, ceiling grid, lights, ducts, pipes, ceiling panels), all turned on.
- Removed: all `Archive*` layers, hidden layout/reference layers (plan reference, photo stations, Layout 01-08, ceiling references), text-dot ID layers, the tool-closet curve diagram, empty layers, and every curve, text dot and annotation. 2,517 objects on 40 layers (from 3,899 on 75); 49 MB.
- The 92 ceiling panels had no render meshes; they now carry them.
- Landmarks on layer `LabWalk - alignment landmarks`, with a small gold floor disc at each:
  - **A**: workshop west doorway, north jamb corner, workshop side: Rhino (25.013, 38.363, 0) ft.
  - **B**: workshop east doorway (the one onto the south wall), east jamb corner, workshop side: Rhino (46.074, 32.386, 0) ft.
  - Model distance A to B: 21.89 ft = **6.673 m**. Tape-measure this in the room before relying on it: the model's walls are partly assumed, and a mismatch over 5% blocks saving.

## Checks run (desktop only)

- Reader tests: landmark points in millimeters on a hidden layer, conversion, duplicate-name rejection (`Tools/RhinoImportTests`).
- **Lab Walk > 6. Test in-app import pipeline...** on the lab file: loads hidden (4.6 s in the Editor), landmarks A (7.624, 0, 11.693) and B (14.043, 0, 9.871) m, 6.673 m apart; activation; reload from app storage with identical fingerprint; re-import gives the same fingerprint; bad file rejected without changing the selection; return to bundled sample. Also run on the file without landmarks (bounding-box fallback).
- Not covered: the headset menu itself in Play mode, the Android picker, GLB through the edit-mode test (glTFast needs Play mode), device memory and load time.
