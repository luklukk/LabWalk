# QR marker calibration

Added in 0.3.0. Since 0.6.0, Lab Walk reads the codes from the passthrough camera image itself. Before that it relied on Horizon OS's built-in QR tracking, which reported no codes on the lab headsets. **Confirmed working on the lab Quest 3S headsets with 0.6.0 (2026-09-29).** The decoder and 3D locator also have desktop tests with synthetic camera images (`dotnet run --project Tools/ScanTests`).

## How it works

1. Printed QR codes are taped to walls. Each code's text is its ID (`LW1`, `LW2`, `LW3`).
2. The Rhino model has a point named `LabWalk marker <ID>` at each code's center, on the wall surface.
3. On the headset, Lab Walk reads one frame from the left passthrough camera four times a second. It uses Meta's Passthrough Camera API through MRUK's `PassthroughCameraAccess` (Quest 3/3S, Horizon OS v74+). Decoding runs on a background thread (`QrFrameDecoder`, using ZXing.Net):
   - ZXing finds each code's three corner squares.
   - `QrPose` works out the code's 3D position from them and the printed size (15.0 cm).
   - The modules are read through that pose, which also handles codes seen at an angle.
   - The camera's position when that frame was taken puts the code into room coordinates.
4. Each code's position is averaged over many sightings, leaving out stray readings. Once two or more codes are steady (at least 5 sightings within 3 cm), Lab Walk fits the model to them: yaw plus a 3D translation, never scale. The panel shows each code's status (with its measured height), the fit error, and the camera scan state.
5. When the app starts or a model is loaded, the camera looks for the codes on its own; once two are steady the model appears. It keeps refining with more sightings (silently), then stops scanning once every code is steady, until the placement is fine-tuned by hand.
6. **Fine-tune placement** (first row of the View menu) shows each marker's status and the fit, lets the sticks nudge the model, and **B** re-snaps to the markers.
**Scanning tips:** stand about 0.5 to 1.5 m from a code and roughly face it (within about 35 degrees). Hold still for a second or two. Good light helps. In desktop tests the 3D position is within about 1 cm up to 1.2 m and 1 to 2 cm at 2 m, before averaging. Codes turned more than about 40 degrees away are not found.

While placing or fine-tuning, a **magenta cross** marks where the placed model expects each code and a **cyan cross** where the camera sees it. When they coincide on the paper, the placement is right.

The app asks for **headset camera** access (`horizonos.permission.HEADSET_CAMERA`) the first time it looks for markers. Camera frames are only processed on the headset, in memory, and are never stored or sent anywhere. The app does not need a room scan (Space Setup).

## Recentering (Meta button)

0.3.0 turns off Unity OpenXR recentering (`OpenXRSettings.SetAllowRecentering(false)`), so the app uses the boundary's stage space, which a recenter does not move (Unity OpenXR input docs). Recenter events are still detected. The app reports whether the tracking origin jumped, and writes before/after poses to `Android/data/com.architecturelab.labwalk/files/labwalk-log.txt`. Marker samples are discarded after a recenter.

## Printing

Print the **PDFs** in `Docs/Markers`: `LabWalk-markers.pdf` has all three codes, and `LabWalk-marker-LW1.pdf` (and LW2, LW3) has one each. Browsers often rescale HTML pages, so use the PDFs for printing.

- Print at **Actual size / 100%**, never "Fit to page" or "Shrink". The PDFs ask viewers to print at actual size, but check the print dialog.
- The black square must measure **15.0 cm**. Each page has a **10 cm** bar to check with a ruler.
- US Letter paper (the code and its white margin also fit on A4). Matte paper is best.
- Checked by rendering the PDFs with PDFium at 300 DPI: every page decodes to its ID, and the code measures 15.00 × 15.00 cm with 3.3 cm of white paper at the sides.

Regenerate or add codes with:

```
dotnet run --project Tools/MarkerSheet -- Docs/Markers LW1 LW2 LW3
```
## Placing the codes in the Digital Tools room (LL106, northwest)

In the model this room runs from the exterior **west wall** (inside face x = 0) to two angled east walls, between the **south wall** (inside face y ≈ 33.04 ft) and the **north window wall** (inside face y = 57.35 ft). Put codes on the three straight walls, so each position is two tape measurements:

| Code | Suggested spot | Why |
|---|---|---|
| LW1 | **West wall**, about 4 ft south of the northwest corner | Near a corner that is easy to measure from |
| LW2 | **North wall pier between windows D2 and D3** (model x 12.77 to 15.18 ft, center 13.98) | Far from LW1 across the room; clear of glass |
| LW3 | **South wall**, about 8 ft east of the southwest corner (avoid the recessed printer built-in) | Gives the widest spread (about 22 ft to LW1) |

Keep all three roughly **5 ft (1.5 m) above the floor** at their centers, where you can see them from the middle of the room. Move a code if equipment blocks it. The CNC stands in front of the pier between D1 and D2, which is why that pier isn't suggested. Avoid the window glass and backlight. Codes must not all be on one straight line; the layout above is a wide triangle.

**For each code, record** (to the nearest 1/8 in or 3 mm):
1. which wall it is on and which corner you measured from;
2. the horizontal distance from that corner to the code's center (side edge + 7.5 cm);
3. the height of the code's center above the floor (bottom edge + 7.5 cm).

The printed sheet repeats these steps. Measure from the real room corners: the markers then tie the model to the room through those corners, even where the model's walls are approximate.

## Model coordinates (Rhino feet, Z up)

| Wall | Code center |
|---|---|
| West wall, measured `d` from the NW corner | (0, 57.351 − d, h) |
| West wall, measured `d` from the SW corner | (0, 33.037 + d, h) |
| North wall, measured `d` from the NW corner | (d, 57.351, h) |
| South wall, measured `d` from the SW corner | (d, 33.037, h) |

`d` and `h` in feet. Windows on the north wall: D1 x 2.51 to 6.36, D2 8.87 to 12.77, D3 15.18 to 19.13 ft (sill 2.5 ft, head 7.5 ft). The points go on a layer of their own, named `LabWalk marker LW1` and so on. Any layer works, including hidden ones.
