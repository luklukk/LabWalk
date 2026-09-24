# QR marker calibration

Added in 0.3.0. **Not yet run on a Quest.** Everything below was checked on the desktop only (math tests, reader tests, compile).

## How it works

1. Printed QR codes are taped to walls. Each code's text is its ID (`LW1`, `LW2`, `LW3`).
2. The Rhino model has a point named `LabWalk marker <ID>` at each code's center, on the wall surface.
3. On the headset, Horizon OS itself detects and tracks the codes (Meta MR Utility Kit QR code tracking, Horizon OS v78+, Quest 3/3S). Lab Walk only reads their positions. It never processes camera images.
4. Each code's position is averaged over many updates. Once two or more codes are steady (at least 8 updates, spread under 2 cm), Lab Walk fits the model to them: yaw plus a 3D translation, never scale. The panel shows each code's status and the fit error.
5. If Lab Walk is waiting for alignment, the model is placed automatically, then keeps refining while more samples arrive until you nudge it by hand. **A** saves the placement (spatial anchor) as before. **X** enters VR, even unsaved.
6. With a saved placement, the app only compares. If the codes show the model is more than 3 cm off, it says so; the **left trigger** re-snaps.

In passthrough, a **magenta cross** marks where the placed model expects each code and a **cyan cross** where the headset sees it. When they coincide on the paper, the placement is right.

The app asks for the **spatial data** permission (`com.oculus.permission.USE_SCENE`) the first time a model with markers loads. It does not need a room scan (Space Setup).

## Recentering (Meta button)

0.3.0 turns off Unity OpenXR recentering (`OpenXRSettings.SetAllowRecentering(false)`), so the app uses the boundary's stage space, which a recenter does not move (Unity OpenXR input docs). Recenter events are still detected. The app reports whether the tracking origin jumped, and writes before/after poses to `Android/data/com.architecturelab.labwalk/files/labwalk-log.txt`. Marker samples are discarded after a recenter.

## Printing

Print `Docs/Markers/LabWalk-markers.html` from a browser at **100% / actual size**. The black square must measure **15 cm**; each page has a 10 cm check bar. Matte paper is best. Regenerate or add codes with:

```
dotnet run --project Tools/MarkerSheet -- Docs/Markers/LabWalk-markers.html LW1 LW2 LW3
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
