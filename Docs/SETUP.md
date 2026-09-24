# Setup and build

## Verified dependency choices

Checked against official documentation and package registry/source metadata on 2026-09-22:

| Dependency | Pinned version | Reason |
|---|---|---|
| Unity Editor | 6000.0.66f2, revision b20bc5da3050 | Explicit minimum in Meta XR Core 205 package metadata and current Meta development requirements |
| Meta XR Core | 205.0.0 | Current registry stable release; supplies rig, passthrough, input, anchors and build tooling |
| Unity OpenXR | 1.18.0 | Stable Unity 6 package; Meta XR Feature enabled, Oculus Touch interaction profile enabled |
| XR Plugin Management | 4.6.1 | Stable registry release tag; supports Unity 6 and OpenXR loader assignment |
| Unity glTFast | 6.20.0 | Stable release for Unity 6; asynchronous runtime GLB import |
| Rhino3dm | 8.35.0 | Included managed/Windows libraries and Android ARM64 native build; direct 3DM meshes and cached render meshes |
| Unity UI | 2.0.0 | Required by Meta SDK UI classes, even though Lab Walk uses a minimal text panel |

Unity has resolved this dependency baseline and compiled the project. The manifest explicitly includes Unity UI, Animation and Physics 2D because the Meta Core SDK references their types. The rendering path is the built-in pipeline with glTFast's built-in material shaders; URP is not required by this scaffold. The setup explicitly includes the runtime material shaders to prevent their omission from an Android build.

Primary sources:

- [Meta Unity requirements](https://developers.meta.com/horizon/documentation/unity/unity-development-requirements/)
- [Unity 6000.0.66f2 release](https://unity.com/releases/editor/whats-new/6000.0.66f2)
- [Meta package registry](https://npm.developer.oculus.com/com.meta.xr.sdk.core)
- [Unity OpenXR registry](https://packages.unity.com/com.unity.xr.openxr)
- [Unity glTFast registry](https://packages.unity.com/com.unity.cloud.gltfast)
- [XR management registry](https://packages.unity.com/com.unity.xr.management)
- [Meta spatial anchors](https://developers.meta.com/horizon/documentation/unity/unity-spatial-anchors-persist-content/)
- [glTFast project setup](https://docs.unity3d.com/Packages/com.unity.cloud.gltfast@6.20/manual/ProjectSetup.html)

## Generated settings

The Configure command selects ARM64, IL2CPP, .NET Standard, Vulkan, linear color, single pass instanced rendering, 4x MSAA, Android minimum API 32 / target API 34, GameActivity, new input system, and package ID `com.architecturelab.labwalk`. All quality tiers use the same Quest graphics settings, including disabled shadows, so Android's default tier cannot silently select desktop settings. Android API selections follow the downloaded Meta SDK 205 project rules for this development build. Store submission requirements should be checked separately if distribution is added.

Meta settings target Quest 3S, require passthrough, and enable anchors. The SDK build processor generates the manifest, including `quest3s` and `com.oculus.permission.USE_ANCHOR_API`. Shared anchors, scene reconstruction, boundary suppression and camera image access are disabled. The generated Android manifest was inspected during the build. The XR rig uses the Stage origin and remains at unit scale and world identity.

Configure also generates/updates Meta's Android manifest and selects contextual passthrough for the system loading screen. The Unity splash screen is disabled for a consistent MR startup. OpenXR 1.18 logs a recommendation because the Meta feature declares API 1.1.45 while Unity recommends 1.1.54; Unity reports that it ignores the lower requested patch. This SDK compatibility warning is recorded for device testing; package source has not been modified to suppress it.

`BuildSanitizer` runs after Meta's build hook to disable the unused DevAgent and clear its automatically injected editor connection settings. Keep this hook when rebuilding this standalone viewer. Meta recreates the local DevAgent settings on editor startup; that generated asset is excluded from version control and the source archive. Unity's internal text shader is supplied by the built-in font; it must not be added to Always Included Shaders (doing so caused a confirmed Unity asset-serialization build failure).

The project uses native Quest APIs only in an Android build or an explicitly configured XR editor session. Plain editor Play mode supplies a camera preview; it does not emulate anchor persistence or passthrough. The Windows simulator is configured for development. Direct 3DM import is available in Windows x64 Editor and Android ARM64 builds; see `RHINO_3DM.md`. Included binaries mean no native rebuild is needed for normal Unity builds.

## Build in the editor

1. Import the folder through Unity Hub with the pinned editor and Android modules installed.
2. Wait for package installation and script compilation; fix any Console errors before proceeding.
3. Use the included scene and settings. Run **Lab Walk > 1. Configure project and create scene** only if setup is missing; it reapplies the original presets and can overwrite later project settings. Restart Unity when changing the input backend requires it.
4. Open `Assets/LabWalk/Scenes/LabWalk.unity` and verify the sample in Play mode.
5. Switch to Android in Build Profiles. Review required errors in Meta's Project Setup Tool and OpenXR validation. The generated XR configuration should have **Meta XR Feature** and **Oculus Touch Controller Profile** enabled.
6. Run **Lab Walk > 2. Build Quest APK**. This is a development APK for sideloading; production signing and store publishing are outside this milestone.

If Configure reports that the Meta SDK is still initializing, let the import finish and run it again. Do not delete an existing scene to resolve this. If the setup needs to be recreated later, preserve your edited scene before creating another one.

## Command line build

After importing once and activating Unity, run these in PowerShell from the LabWalk folder. Replace the editor path only if you installed it elsewhere. The separate invocations allow package import and input changes to settle before the build.

```powershell
$editor = 'C:\Program Files\Unity\Hub\Editor\6000.0.66f2\Editor\Unity.exe'
New-Item -ItemType Directory -Force Logs | Out-Null
$projectPath = (Get-Location).Path
$common = @('-batchmode', '-quit', '-projectPath', ('"' + $projectPath + '"'), '-buildTarget', 'Android')
$build = Start-Process -FilePath $editor -ArgumentList ($common + @('-executeMethod', 'LabWalk.Editor.QuestBuild.Build', '-logFile', ('"' + $projectPath + '\Logs\build.log"'))) -WindowStyle Hidden -Wait -PassThru
if ($build.ExitCode -ne 0) { throw 'Build failed; inspect Logs/build.log.' }
```

Close any Unity editor already using this project before running batch mode. These commands wait for each process and check its exit code. An APK is the output only when Unity reports a successful build.

## USB deployment

Enable Quest Developer Mode, connect USB, and accept the headset's debugging prompt. The Unity Android module supplies `adb.exe`:

```powershell
$adb = 'C:\Program Files\Unity\Hub\Editor\6000.0.66f2\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
& $adb devices
.\Tools\Deploy-Quest.ps1 -Adb $adb
```

With more than one Android device, add `-Serial 'DEVICE_SERIAL'`. Open the sideloaded app from Unknown Sources if it does not launch automatically.

To update only the model, place `model.json` and its GLB in one local folder:

```powershell
.\Tools\Deploy-Quest.ps1 -Adb $adb -SkipInstall -ModelFolder 'C:\path\to\lab-export'
```

The script stops the app, copies the GLB or 3DM then its manifest into `/sdcard/Android/data/com.architecturelab.labwalk/files/Models`, and relaunches it. A USB debugging connection is sufficient; the headset app has no cloud account, model server, or storage browser. A side-loaded override remains after APK updates. To return to the bundled model, remove or rename only the override `model.json` in that folder.

Saved placement metadata is `files/placement.json` in the same app data root. It contains an anchor UUID and model fingerprint; the anchor itself is saved through Meta's SDK. Uninstalling the app or clearing its data can lose the association.

## Simulator testing on Windows

Use the installed **Meta XR Simulator 205.0** with Unity 6000.0.66f2. Enable OpenXR, Meta XR Feature and the Oculus Touch controller profile for **Standalone/Windows**, then activate Meta XR Simulator using its Unity menu command. Keep the user's applied Meta project fixes. The project Configure command reapplies its original Android presets; it is not needed for each simulator run.

After reopening Unity, check **Meta > Meta XR Simulator > Activate** before entering Play mode. A running simulator process alone does not mean the editor is using its runtime. The Unity bridge can respond outside Play mode; XR Operator's live session connection requires the OpenXR application to be running. If Operator reports offline, check runtime activation and Play mode first.

Open LabWalk and enter Play mode. The status panel should say **EDITOR XR**, and XR Operator should report a focused session with rendered frames. The simulator defaults to the Quest 3 profile; select Quest 3S when that profile is available and restart Play mode before treating results as profile-specific. The simulator room is synthetic passthrough content, not a scan of the real lab.

Lab Walk now selects its XR path from the initialized XR loader instead of relying only on `XRSettings.isDeviceActive`, which can be false before the first frame. Editor runs also enable background execution so moving focus to the simulator does not stall the app. Both changes were checked in the running simulator.

For the separate **desktop smoke test**, temporarily disable **Initialize XR on Startup** under the Standalone/Windows XR settings, and restore it afterwards. That test deliberately checks the ordinary desktop camera path. Simulator observations are recorded in `SIMULATOR_TEST.md`; they do not replace the physical Quest checklist.

## Controls

| Action | Quest controller | Desktop preview |
|---|---|---|
| Record floor reference | Right trigger | Left mouse click |
| Save placement / retry restoration | A | Enter (save reports hardware required) |
| Start alignment again | B | R |
| Passthrough / immersive view | X | V, after alignment |
| Hide/show model during alignment | Y | H |
| Measure two floor points | Right grip twice | M twice at pointer locations |
| Fine translation in viewing direction | Right stick | Arrow keys |
| Fine yaw / height | Left stick X / Y | Z/C and PageUp/PageDown |
| Show/hide status panel | Left Menu | Tab |
| Move/look in preview | Physical movement on headset | WASD, Q/E; right mouse drag |

Fine adjustments are active only after recording both references and before saving. Maximum rates are 8 cm/s and 6 degrees/s. No joystick locomotion, teleport, world scaling or boundary disabling is implemented.
