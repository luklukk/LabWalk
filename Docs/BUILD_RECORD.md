# Build record — 2026-09-24

**Updated APK:** includes direct `.3dm` import, the ARM64 Rhino reader, import diagnostics and the Rhino sample as the startup model, plus the earlier XR startup and anchor fixes. Built with the user's updated project settings without re-running Configure. See `RHINO_3DM.md` and `SIMULATOR_TEST.md`.

## Delivered APK

- File: `Builds/LabWalk-Quest3S.apk`
- Size: 95,756,524 bytes (about 95.8 MB)
- SHA-256: `5A9BBEF5D81BC7B98F107B065279D2090D473B42F578C480D36FBFBB293577A8`
- Package: `com.architecturelab.labwalk`, version `0.1.0`, version code `1`
- Development build for USB sideloading, signed with the local Android development key. Production signing is not configured.

## Confirmed on this computer

- Unity **6000.0.66f2** completed the updated Android build and exited successfully.
- APK contents verified: `lib/arm64-v8a/librhino3dm_native.so`, Rhino sample and dependency notices.
- Rhino reader integration tests passed, including cached Breps/extrusions, units/axes, mirrored block winding, visibility, missing geometry and cancellation. Unity loaded and rendered the Rhino sample at 6 × 3.1 × 8 m; preview inspected. See `rhino-import.txt`, `rhino-import.png` and `rhino-reader-tests.txt`.
- The Rhino sample completed two-point alignment, simulated anchor save and immersive rendering in the XR simulator. Physical Android execution is still untested.
- IL2CPP ARM64, Vulkan, Android minimum API 32 / target API 34.
- Android `apksigner verify --verbose`: **Verifies**, APK signature scheme v2, one signer.
- Packaged manifest: GameActivity, `quest3s`, required passthrough, `USE_ANCHOR_API`, Horizon minimum 65 / target 205, contextual passthrough system loading screen.
- Meta's required GameActivity and MR system-loading-screen warnings resolved. OpenXR's recommendation about Meta's declared API patch version remains; see SETUP.md.
- Build hook confirmed clearing unused DevAgent connection settings before packaging and after the build.
- Earlier desktop Play-mode smoke test **PASS** (September 23): actual asynchronous GLB import; 15 renderers; 6 × 3.1 × 8 m bounds; both two-point placement references mapped correctly; unit placement scale; placement unchanged across view switches; aligned model remains visible after runtime frames.
- The final desktop preview image was inspected. Test result: `Docs/editor-smoke.txt`; image: `Docs/sample-preview.png`.

The build was performed in an isolated project copy while the original project was open. Current Assets, Packages and ProjectSettings were copied into that build project, and only the resulting APK was copied back. The user's live settings were preserved. Unity caches and raw editor logs are excluded from the archive.

## Requires a physical Quest 3S

No authorized headset was connected over USB. APK installation/launch, native Android 3DM import, controller inputs, passthrough, real-room registration, spatial-anchor save/restore, perceived physical scale, drift, frame rate and walking comfort are **not tested on a physical headset**. The real Rhino lab file was unavailable, so the bundled original sample room was used.

Connect a Quest in Developer Mode, accept USB debugging, then follow `Docs/SETUP.md` and record results in `Docs/VALIDATION.md`.

## Source changes after the recorded APK (2026-09-24)

The APK above was built before the 3DM reader began merging geometry per color. Source now differs from that APK: current source loads the architecture lab file, which the recorded APK rejects (10,019 meshes over the former 10,000-mesh cap). Rebuild before testing the lab file on Quest and update this record and the packaging hash.


## In-headset import build (2026-09-24, not yet installed on a Quest)

| Property | Value |
|---|---|
| Filename | `Builds/LabWalk-Quest3S-import.apk` (the earlier `LabWalk-Quest3S.apk` is left unchanged) |
| Size | 117,730,085 bytes (contents match the earlier build within 0.1 MB; the rest is unused space left by incremental Gradle packaging) |
| SHA-256 | `EF871BFC35B3D7FCADA2942578BD06CDB767F2C130F4C398570A9ADE1C3B5A15` |
| Application ID / version | `com.architecturelab.labwalk` / `0.1.0` code `1` (installs over the earlier build with `adb install -r`) |
| Build | Staging project `work/QuestBuild`, batch `LabWalk.Editor.QuestBuild.Build`, Unity 6000.0.66f2, exit 0 |
| Signing | Local development key; apksigner v2 verified |
| Contents checked | `LabWalkFilePicker` class present (`classes4.dex`); `librhino3dm_native.so`; bundled `sample-room.3dm` |

Adds: per-color mesh merging (the lab file loads), labels/curves no longer marked INCOMPLETE, in-headset **Models** menu (left grip) with Import folder, system picker and bundled sample, review/confirm, crash-safe activation, and landmark points read from Rhino files. See [IMPORT.md](IMPORT.md). `work/Package-Quest.ps1` still validates the earlier APK hash; update it if this build replaces `LabWalk-Quest3S.apk`.

## Release v0.2.0 (2026-09-24, not yet installed on a Quest)

Published at https://github.com/luklukk/LabWalk/releases/tag/v0.2.0. Clean staging build of commit `ea6e228`.

| Property | Value |
|---|---|
| APK link (Device Manager) | https://github.com/luklukk/LabWalk/releases/download/v0.2.0/LabWalk-Quest3S-0.2.0.apk |
| Size | 95,817,022 bytes |
| SHA-256 | `63523e7682b017492062925f5969414cbac295859e3fd964be8f56a44abaef59` |
| MD5 | `23a973be6a725b95a1c8f592590fd71a` |
| Version | 0.2.0, version code 2 |
| Signing | Unity default development key on the build workstation; apksigner v2 verified. Later releases must use the same key and a higher version code. |
| Also attached | `Handley_B1_16_LabWalk.3dm` (SHA-256 `c063ecf4...2db9`), `checksums.txt` |

The APK link was downloaded without authentication and matched the SHA-256 above. This supersedes `LabWalk-Quest3S-import.apk` (same code, version code 1).

## Release v0.3.0 (2026-09-24, not yet installed on a Quest)

https://github.com/luklukk/LabWalk/releases/tag/v0.3.0, release build (`LabWalk.Editor.QuestBuild.BuildRelease`) of commit `e56d9e6`.

| Property | Value |
|---|---|
| APK link (Device Manager) | https://github.com/luklukk/LabWalk/releases/download/v0.3.0/LabWalk-Quest3S-0.3.0.apk |
| Size | 88,886,375 bytes |
| SHA-256 | `c10c2e229acdf364f901c3013565070c3b9dd7825aa3886c4310b957922c3326` |
| MD5 | `9ff4e14c768ff0480e3c48cefbd2c3a3` |
| Version | 0.3.0, version code 4 |
| Checked | apksigner v2; not debuggable; no Meta XR Operator components; `USE_SCENE` present; `libmrutilitykitshared.so` and `librhino3dm_native.so` packaged; anonymous download matched SHA-256 |

0.2.1 (version code 3) was built but never published; its changes are included here.

## Release v0.5.0 (2026-09-28, not yet installed on a Quest)

Release build of the commit that sets version 0.5.0. Includes the unpublished 0.4.0 work (controller models and tooltips) plus switchable layers and wireframe.

| Property | Value |
|---|---|
| Permanent link (newest release) | https://github.com/luklukk/LabWalk/releases/latest/download/LabWalk-Quest3S.apk |
| This release | https://github.com/luklukk/LabWalk/releases/download/v0.5.0/LabWalk-Quest3S.apk |
| Size | 102,047,525 bytes |
| SHA-256 | `adf72d6a5949cb6ea7d3d2fa6431ff59436a6d9d7ee3f23dd62fd2fddd627f19` |
| MD5 | `e7f107bcf20a08685455235fc41bf04b` |
| Version | 0.5.0, version code 6 |
| Checked | apksigner v2; not debuggable; no Meta XR Operator components; picker, Rhino and MRUK libraries packaged |

From v0.5.0 every release names its APK `LabWalk-Quest3S.apk` so the permanent link keeps working. Device Manager does not re-check links by itself; redeploy after each release. Each release must raise the version code.

## Release v0.5.1 (2026-09-28, not yet installed on a Quest)

Release build of commit `8030ee3`: QR payload fix and diagnostics log, system controller models, boundaryless passthrough with session anchors, controller pointer at any angle.

| Property | Value |
|---|---|
| Permanent link (newest release) | https://github.com/luklukk/LabWalk/releases/latest/download/LabWalk-Quest3S.apk |
| This release | https://github.com/luklukk/LabWalk/releases/download/v0.5.1/LabWalk-Quest3S.apk |
| Size | 102,055,461 bytes |
| SHA-256 | `c22345d0c78f20dd3c8f5825f3e9763b241a0b7c22696e2fabeee89acd95c3db` |
| MD5 | `e532211dcdcb0a09f2f6d51252144974` |
| Version | 0.5.1, version code 7 |
| Checked | apksigner v2, same certificate as 0.5.0; not debuggable; no Meta XR Operator components; `BOUNDARYLESS_APP` and `RENDER_MODEL` features; `USE_SCENE`, `USE_ANCHOR_API`, `RENDER_MODEL`, `BOUNDARY_VISIBILITY` permissions |
## Release v0.5.2 (2026-09-28, not yet installed on a Quest)

Release build of commit `0f7d4ac`: retries MRUK's QR tracker start while it is off and shows the tracker state on the marker panel.

| Property | Value |
|---|---|
| Device Manager link (this release) | https://github.com/luklukk/LabWalk/releases/download/v0.5.2/LabWalk-Quest3S.apk |
| Permanent link (newest release) | https://github.com/luklukk/LabWalk/releases/latest/download/LabWalk-Quest3S.apk |
| Size | 102,058,877 bytes |
| SHA-256 | `2a6c8140fc32afab95354b7b7e6c3956d61cde076005bf1b2130024116d65fed` |
| MD5 | `cf1eb0cd3564f5fd40521b8b77021b66` |
| Version | 0.5.2, version code 8 |
| Checked | apksigner v2, same certificate as 0.5.0 and 0.5.1; not debuggable |

Correction to the v0.5.0 note: Meta documents that self-hosted APKs are not updated from a link Device Manager already knows. Give Device Manager each release's own link (`/releases/download/vX.Y.Z/LabWalk-Quest3S.apk`); the permanent link is for sideloading.

## Release v0.6.0 (2026-09-29, not yet installed on a Quest)

Release build of commit `1b9e577`: QR markers read from the passthrough camera image (Passthrough Camera API, ZXing.Net 0.16.10, pose-based sampling) instead of Horizon OS QR tracking. Desktop tests: `dotnet run --project Tools/ScanTests` (synthetic camera images).

| Property | Value |
|---|---|
| Device Manager link (this release) | https://github.com/luklukk/LabWalk/releases/download/v0.6.0/LabWalk-Quest3S.apk |
| Permanent link (newest release) | https://github.com/luklukk/LabWalk/releases/latest/download/LabWalk-Quest3S.apk |
| Size | 102,688,045 bytes |
| SHA-256 | `33da1760a562e46ef6956dc22ed9a96fd50eae607995141d7f8b90ec7dc9cd4b` |
| MD5 | `288303e52159f52ac122ab3a200ae2e6` |
| Version | 0.6.0, version code 9 |
| Checked | apksigner v2, same certificate as 0.5.x; not debuggable; `HEADSET_CAMERA` permission; ZXing, `QrFrameDecoder`, `CameraMarkerScanner` and `PassthroughCameraAccess` present in IL2CPP metadata |