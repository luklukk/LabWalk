using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LabWalk.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace LabWalk
{
    public sealed class LabWalkApp : MonoBehaviour
    {
        public OVRCameraRig rig;
        enum Phase { Loading, Origin, Direction, Adjust, Saving, Pinned, Recovery, Error, Importing, Review }
        Phase phase=Phase.Loading;
        readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        readonly MetaAnchorService anchors=new MetaAnchorService();
        PlacementStore store;
        ModelManifest manifest;
        LoadedModel model;
        Transform placement;
        OVRSpatialAnchor activeAnchor;
        WalkthroughView view;
        Camera eye;
        string fingerprint, message="Loading model...", anchorStatus="Not placed", measurement="Grip: measure a known floor distance";
        Vector3 realA, measuredA;
        bool firstMeasure, busy, editorPreview, panelVisible=true, showModel=true, trackingHealthy;
        float averageFrameTime=1f/72, panelTime;
        AlignmentResult alignment;

        sealed class MenuItem { public string Label; public Action Run; }
        ModelCandidate candidate;
        CancellationTokenSource importCancel;
        readonly List<MenuItem> menuItems=new List<MenuItem>();
        bool menuOpen, waitingForPicker;
        int menuIndex;
        float menuRepeat, pickerPoll;
        string menuNote="";
        Phase resumePhase;

        async void Start()
        {
            // The simulator is a separate window: keep editor frames advancing while it has focus.
            if(Application.isEditor) Application.runInBackground=true;
            store=new PlacementStore();
            placement=new GameObject("Model placement (rigid pose)").transform;
            try { ModelImport.RecoverInterruptedSwitch(); ModelImport.EnsureFolders(); }
            catch(Exception e) { Debug.LogWarning("Import folder setup failed: "+e.Message); }
            // OpenXR can have a loaded provider before its first visible frame. Checking only
            // isDeviceActive here incorrectly disables the rig during simulator startup.
            var xrLoader=XRGeneralSettings.Instance?.Manager?.activeLoader;
            editorPreview=Application.isEditor && xrLoader==null && !XRSettings.isDeviceActive;
            if(editorPreview)
            {
                rig.gameObject.SetActive(false);
                eye=new GameObject("Desktop preview camera").AddComponent<Camera>();
                eye.transform.SetPositionAndRotation(new Vector3(0,1.65f,-2),Quaternion.Euler(20,0,0));
            }
            else eye=rig.centerEyeAnchor.GetComponent<Camera>();
            eye.nearClipPlane=0.05f; eye.farClipPlane=150;
            view=new WalkthroughView(eye,editorPreview ? null : rig.GetComponent<OVRPassthroughLayer>());
            busy=true;
            try
            {
                var folder=ModelFiles.Folder;
                ModelCandidate loaded; string warning=null;
                try { loaded=await ModelImport.LoadFolderAsync(folder,lifetime.Token); }
                catch(Exception e) when(!(e is OperationCanceledException) && folder!=ModelImport.BundledFolder)
                {
                    // A broken imported selection must not strand the app: fall back to the bundled sample.
                    Debug.LogException(e);
                    loaded=await ModelImport.LoadFolderAsync(ModelImport.BundledFolder,lifetime.Token);
                    warning="Imported model failed to load ("+e.Message+"). Showing the bundled sample; left grip opens Models.";
                }
                if(!this) { loaded.Dispose(); return; }
                Adopt(loaded);
                if(editorPreview) BeginAlignment(); else await RestoreAsync();
                if(warning!=null) message=warning;
            }
            catch(OperationCanceledException) { }
            catch(Exception e) { if(this) { phase=Phase.Error; message=e.Message+" Left grip opens Models."; Debug.LogException(e); } }
            finally { busy=false; }
        }

        // Makes a loaded candidate the active model. Placement keeps its pose; the saved anchor is
        // only reused when the fingerprint matches (RestoreAsync), otherwise alignment starts.
        void Adopt(ModelCandidate next)
        {
            var previous=model;
            model=next.Model; manifest=next.Manifest; fingerprint=next.Fingerprint;
            model.Root.transform.SetParent(placement,false);
            model.Root.SetActive(false);
            next.Model=null; next.Dispose();
            previous?.Dispose();
        }

        async Task RestoreAsync()
        {
            busy=true; phase=Phase.Loading; anchorStatus="Finding saved placement";
            try
            {
                var saved=store.Read();
                if(saved==null) { BeginAlignment(); return; }
                if(saved.modelFingerprint!=fingerprint)
                { BeginAlignment(); message="Export or settings changed. Align this version again."; return; }
                message="Look around the original room while the anchor localizes.";
                // Switching back to the model that owns the saved anchor can reuse the live anchor.
                if(!(activeAnchor && activeAnchor.Uuid.ToString()==saved.anchorUuid))
                {
                    var restored=await anchors.RestoreAsync(saved.anchorUuid,lifetime.Token);
                    if(!this) { Destroy(restored.gameObject); return; }
                    var previous=activeAnchor;
                    activeAnchor=restored;
                    if(previous) { placement.SetParent(null,true); Destroy(previous.gameObject); }
                }
                placement.SetParent(activeAnchor.transform,false);
                placement.localPosition=saved.localPosition; placement.localRotation=saved.localRotation;
                placement.localScale=Vector3.one;
                phase=Phase.Pinned; anchorStatus="Restored; verify against physical landmarks";
                message="Placement restored. Check alignment in passthrough before X for VR.";
            }
            catch(OperationCanceledException) { }
            catch(Exception e) { if(this) { phase=Phase.Recovery; anchorStatus="Restore failed"; message=e.Message; Debug.LogWarning(e); } }
            finally { busy=false; }
        }

        void BeginAlignment()
        {
            if(model==null) return;
            view.SetImmersive(false);
            placement.SetParent(null,true);
            // Keep the old anchor until a replacement and its metadata have both been saved.
            phase=Phase.Origin; showModel=false; firstMeasure=false; view.ClearMeasurement();
            anchorStatus="Aligning; previous saved placement retained";
            message="Point at physical floor reference A; right trigger to record.";
        }

        void RecordReference(Vector3 point)
        {
            if(phase==Phase.Origin)
            {
                realA=point; phase=Phase.Direction;
                message="Point at physical floor reference B; right trigger to record.";
            }
            else if(phase==Phase.Direction)
            {
                try
                {
                    alignment=AlignmentMath.Solve(ModelManifest.ToPoint(manifest.referenceA),ModelManifest.ToPoint(manifest.referenceB),ModelManifest.ToPoint(realA),ModelManifest.ToPoint(point));
                    placement.SetPositionAndRotation(ModelManifest.ToVector(alignment.Translation),Quaternion.Euler(0,(float)(alignment.YawRadians*180/Math.PI),0));
                    placement.localScale=Vector3.one;
                    phase=Phase.Adjust; showModel=true;
                    message=alignment.RelativeBaselineError>0.05 ? "Reference lengths differ >5%. Check units/points before saving." : "Fine tune with sticks. A saves placement; X enters VR without saving; B starts alignment again.";
                }
                catch(Exception e) { message=e.Message; }
            }
        }

        async Task SaveAsync()
        {
            if(editorPreview) { message="Editor preview: persistent anchors require a Quest. Placement is unsaved."; return; }
            if(!trackingHealthy) { message="Wait for headset tracking before saving."; return; }
            if(alignment.RelativeBaselineError>0.05) { message="Reference lengths differ >5%. B to realign or correct the model units."; return; }
            busy=true; phase=Phase.Saving; anchorStatus="Creating and saving anchor";
            OVRSpatialAnchor candidate=null;
            try
            {
                candidate=await anchors.CreateAndSaveAsync(new Pose(placement.position,placement.rotation),lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested();
                var previous=activeAnchor;
                SavedPlacement prior=null;
                try { prior=store.Read(); } catch(Exception e) { Debug.LogWarning("Replacing unreadable placement: "+e.Message); }
                placement.SetParent(candidate.transform,true);
                var saved=new SavedPlacement {
                    anchorUuid=candidate.Uuid.ToString(), modelFingerprint=fingerprint,
                    savedUtc=DateTime.UtcNow.ToString("O"), localPosition=placement.localPosition, localRotation=placement.localRotation
                };
                store.Write(saved);
                activeAnchor=candidate; candidate=null;
                phase=Phase.Pinned; anchorStatus="Saved locally";
                message="Verify physical landmarks, then X to enter VR. B realigns.";
                if(previous) Destroy(previous.gameObject);
                if(prior!=null && prior.anchorUuid!=saved.anchorUuid)
                {
                    try
                    {
                        var erased=await OVRSpatialAnchor.EraseAnchorsAsync(null,new[]{Guid.Parse(prior.anchorUuid)});
                        if(!erased.Success) Debug.LogWarning("New placement saved; old anchor cleanup failed: "+erased.Status);
                    }
                    catch(Exception e) { Debug.LogWarning("New placement saved; old anchor cleanup failed: "+e.Message); }
                }
            }
            catch(OperationCanceledException) { }
            catch(Exception e)
            {
                if(this) { phase=Phase.Adjust; anchorStatus="Save failed; placement is unsaved"; message=e.Message+" X still enters VR for this session."; Debug.LogException(e); }
            }
            finally
            {
                if(candidate)
                {
                    if(placement) placement.SetParent(null,true);
                    try { await candidate.EraseAnchorAsync(); }
                    catch(Exception e) { Debug.LogWarning("Failed to clean up unsaved anchor: "+e.Message); }
                    finally { if(candidate) Destroy(candidate.gameObject); }
                }
                busy=false;
            }
        }

        // ---- Models menu: import folder files, the system picker and the bundled sample ----

        void OpenMenu()
        {
            menuItems.Clear();
            if(ModelImport.SystemPickerAvailable) menuItems.Add(new MenuItem {Label="Browse headset files...",Run=StartPicker});
            foreach(var f in ModelImport.ListImportFiles())
            {
                var path=f.FullName;
                menuItems.Add(new MenuItem {Label=$"{f.Name}  ({f.Length/1048576.0:F1} MB)",Run=()=>_=ImportAsync(path,false)});
            }
            menuItems.Add(new MenuItem {Label="Bundled sample room",Run=()=>_=ImportAsync(null,true)});
            menuIndex=0; menuOpen=true; panelVisible=true;
            menuNote=menuItems.Count>(ModelImport.SystemPickerAvailable ? 2 : 1) ? "" : "No .3dm/.glb files in the import folder yet.";
        }

        void StartPicker()
        {
            try { ModelImport.OpenSystemPicker(); waitingForPicker=true; pickerPoll=0; menuNote="System file picker open: choose a .3dm or .glb file. B stops waiting."; }
            catch(Exception e) { menuNote="System file picker unavailable: "+e.Message; }
        }

        void PollPicker()
        {
            if(Time.unscaledTime<pickerPoll) return;
            pickerPoll=Time.unscaledTime+0.25f;
            ModelImport.PickerResult result;
            try { result=ModelImport.PollPicker(); }
            catch(Exception e) { waitingForPicker=false; menuNote="Picker result unreadable: "+e.Message; return; }
            if(result==null) return;
            waitingForPicker=false;
            if(result.status=="copied") _=ImportAsync(result.file,false);
            else if(result.status=="cancelled") menuNote="No file chosen.";
            else menuNote="Could not copy the chosen file: "+result.error;
        }

        // Loads the chosen model hidden, then waits for the user to review it. The active model,
        // its saved selection and its anchor are untouched until ConfirmAsync succeeds.
        async Task ImportAsync(string path,bool bundled)
        {
            if(busy || candidate!=null) return;
            menuOpen=false; busy=true;
            resumePhase=phase; phase=Phase.Importing; view.SetImmersive(false); panelVisible=true;
            message=bundled ? "Loading the bundled sample... B cancels." : $"Loading {Path.GetFileName(path)}... B cancels.";
            importCancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            try
            {
                var loaded=bundled ? await ModelImport.LoadFolderAsync(ModelImport.BundledFolder,importCancel.Token) : await ModelImport.LoadFileAsync(path,importCancel.Token);
                if(!this) { loaded.Dispose(); return; }
                candidate=loaded; phase=Phase.Review;
            }
            catch(OperationCanceledException) { if(this) { phase=resumePhase; message="Import cancelled. The current model is unchanged."; } }
            catch(Exception e) { if(this) { phase=resumePhase; message="Import failed: "+e.Message+" The current model is unchanged."; Debug.LogException(e); } }
            finally { importCancel?.Dispose(); importCancel=null; busy=false; }
        }

        async Task ConfirmAsync()
        {
            var next=candidate;
            if(next==null || busy) return;
            busy=true; phase=Phase.Importing; message="Saving the model selection...";
            try
            {
                if(next.Bundled) await ModelImport.ActivateBundledAsync();
                else await ModelImport.ActivateAsync(next.SourcePath,next.Manifest,next.ManifestBytes,lifetime.Token);
                if(!this) return;
                candidate=null;
                Adopt(next);
                view.ClearMeasurement(); firstMeasure=false; measurement="Grip: measure a known floor distance";
                busy=false;
                if(editorPreview) BeginAlignment(); else await RestoreAsync();
            }
            catch(OperationCanceledException) { }
            catch(Exception e)
            {
                if(this) { candidate=null; next.Dispose(); phase=resumePhase; message="Could not save the selection: "+e.Message+" The current model is unchanged."; Debug.LogException(e); }
            }
            finally { busy=false; }
        }

        void CancelReview()
        {
            candidate?.Dispose(); candidate=null;
            phase=resumePhase; message="Import cancelled. The current model is unchanged.";
        }

        void HandleModelUi(bool accept,bool back)
        {
            if(phase==Phase.Importing) { if(back) importCancel?.Cancel(); return; }
            if(phase==Phase.Review) { if(accept) _=ConfirmAsync(); else if(back) CancelReview(); return; }
            if(waitingForPicker)
            {
                PollPicker();
                if(back && waitingForPicker) { waitingForPicker=false; menuNote="Stopped waiting for the picker."; }
                return;
            }
            var move=MenuMove();
            if(move!=0 && menuItems.Count>0) menuIndex=(menuIndex+move+menuItems.Count)%menuItems.Count;
            if(accept && menuItems.Count>0) menuItems[menuIndex].Run();
            else if(back) menuOpen=false;
        }

        int MenuMove()
        {
            if(editorPreview)
            {
                var k=Keyboard.current; if(k==null) return 0;
                return k.downArrowKey.wasPressedThisFrame ? 1 : k.upArrowKey.wasPressedThisFrame ? -1 : 0;
            }
            var y=OVRInput.Get(OVRInput.RawAxis2D.RThumbstick).y+OVRInput.Get(OVRInput.RawAxis2D.LThumbstick).y;
            if(Mathf.Abs(y)<0.5f) { menuRepeat=0; return 0; }
            if(Time.unscaledTime<menuRepeat) return 0;
            menuRepeat=Time.unscaledTime+0.35f;
            return y>0 ? -1 : 1;
        }

        string ModelUiText()
        {
            var text=new StringBuilder();
            if(phase==Phase.Review && candidate!=null)
            {
                var m=candidate.Model; var size=m.BoundsMeters.size; var largest=Mathf.Max(size.x,size.y,size.z);
                text.Append($"REVIEW MODEL\n{candidate.Manifest.displayName}\nFile: {candidate.Manifest.file}\n");
                text.Append($"Units: {m.SourceUnits} -> meters (true size)\nSize: {size.x:F2} x {size.y:F2} x {size.z:F2} m (X/Y/Z)\n");
                if(largest>300 || largest<0.3f) text.Append("CHECK UNITS: this size is unusual for a room.\n");
                text.Append(Wrap(m.ImportSummary,66)).Append('\n').Append(Wrap(candidate.Notes,66)).Append('\n');
                if(m.Incomplete) text.Append("WARNING: some geometry could not be imported (see summary).\n");
                text.Append(candidate.Bundled ? "\nA: use the bundled sample | B: cancel" : "\nA: use this model (saved for next launch) | B: cancel");
                return text.ToString();
            }
            if(phase==Phase.Importing) return $"MODELS\n{Wrap(message,66)}";
            text.Append($"MODELS\nCurrent: {manifest?.displayName ?? "none"}\n\n");
            for(int i=0;i<menuItems.Count;i++) text.Append(i==menuIndex ? "> " : "   ").Append(menuItems[i].Label).Append('\n');
            text.Append($"\nImport folder (USB / MQDH / adb):\n{ModelImport.DeviceImportPath}\n");
            if(menuNote.Length>0) text.Append(Wrap(menuNote,66)).Append('\n');
            text.Append(editorPreview ? "\nUp/Down: choose | Enter: open | Esc: close" : "\nStick: choose | A: open | B: close");
            return text.ToString();
        }

        void Update()
        {
            if(view==null || eye==null) return;
            averageFrameTime=Mathf.Lerp(averageFrameTime,Time.unscaledDeltaTime,0.04f);
            trackingHealthy=editorPreview || (OVRManager.isHmdPresent && OVRManager.hasInputFocus &&
                OVRManager.tracker != null && OVRManager.tracker.isPositionTracked);
            var anchorTracked=phase!=Phase.Pinned || (activeAnchor && activeAnchor.IsTracked);
            if((!trackingHealthy || !anchorTracked) && view.Immersive)
            {
                view.SetImmersive(false);
                message="Tracking interrupted. Verify alignment before entering VR again.";
            }
            if(editorPreview) MoveDesktopCamera();
            var ray=editorPreview ? eye.ScreenPointToRay(Mouse.current==null ? new Vector2(Screen.width/2f,Screen.height/2f) : Mouse.current.position.ReadValue())
                : new Ray(rig.rightControllerAnchor.position,rig.rightControllerAnchor.forward);
            var controllerTracked=editorPreview || OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch);
            var floor=new Plane(Vector3.up,Vector3.zero);
            var hit=floor.Raycast(ray,out var distance) && distance>0 && distance<10 && ray.direction.y < -0.08f && controllerTracked;
            var point=ray.GetPoint(hit ? distance : 1);
            view.Pointer(ray.origin,point,hit && !view.Immersive && trackingHealthy);
            // The Models menu and import review take all input while open; they work without a model
            // (e.g. after a load error) and without tracking, since they only show the panel.
            var modelUi=menuOpen || phase==Phase.Importing || phase==Phase.Review;
            {
                var k=Keyboard.current;
                bool accept=editorPreview ? k!=null && k.enterKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.A);
                bool back=editorPreview ? k!=null && (k.escapeKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame) : OVRInput.GetDown(OVRInput.RawButton.B);
                bool openModels=editorPreview ? k!=null && k.oKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.LHandTrigger);
                if(modelUi) HandleModelUi(accept,back);
                else if(openModels && !busy && phase!=Phase.Saving) OpenMenu();
            }
            if(model!=null && !busy && trackingHealthy && !modelUi && !menuOpen)
            {
                var keys=Keyboard.current;
                bool trigger=editorPreview ? Mouse.current!=null && Mouse.current.leftButton.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger);
                bool save=editorPreview ? keys!=null && keys.enterKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.A);
                bool realign=editorPreview ? keys!=null && keys.rKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.B);
                bool toggle=editorPreview ? keys!=null && keys.vKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.X);
                bool hide=editorPreview ? keys!=null && keys.hKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.Y);
                bool measure=editorPreview ? keys!=null && keys.mKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.RHandTrigger);
                bool menu=editorPreview ? keys!=null && keys.tabKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.Start);
                if(realign) BeginAlignment();
                else if(trigger && hit) RecordReference(point);
                if(save && phase==Phase.Adjust) _=SaveAsync();
                else if(save && phase==Phase.Recovery) _=RestoreAsync();
                // VR is allowed from an aligned but unsaved placement (session only): spatial anchors may be
                // unavailable, e.g. on headsets in shared mode. The same reference-length check as saving applies.
                var sessionOnly=phase==Phase.Adjust && alignment.RelativeBaselineError<=0.05;
                if(toggle && (phase==Phase.Pinned || sessionOnly) && anchorTracked)
                {
                    view.SetImmersive(!view.Immersive); panelVisible=!view.Immersive; showModel=true;
                    if(sessionOnly && view.Immersive && !editorPreview) message="VR with an unsaved placement: it will not be restored next launch.";
                }
                if(hide && !view.Immersive) showModel=!showModel;
                if(menu) panelVisible=!panelVisible;
                if(measure && hit && !view.Immersive)
                {
                    if(!firstMeasure) { measuredA=point; firstMeasure=true; measurement="Grip at the other end of the known floor distance."; }
                    else { firstMeasure=false; measurement=$"Measured {Vector3.Distance(measuredA,point):F3} m | expected {manifest.knownDistanceMeters:F3} m"; view.Measurement(measuredA,point); }
                }
                if(phase==Phase.Adjust) FineTune();
            }
            if(model!=null)
            {
                var visible=showModel && trackingHealthy && anchorTracked && (phase==Phase.Adjust || phase==Phase.Pinned || phase==Phase.Saving);
                model.Root.SetActive(visible);
                view.Reference(placement.TransformPoint(manifest.referenceA),placement.TransformPoint(manifest.referenceB),visible && !view.Immersive);
            }
            if(Time.unscaledTime>=panelTime && (menuOpen || phase==Phase.Importing || phase==Phase.Review))
            {
                panelTime=Time.unscaledTime+0.15f;
                view.UpdatePanel(ModelUiText(),true);
            }
            else if(Time.unscaledTime>=panelTime)
            {
                panelTime=Time.unscaledTime+0.15f;
                var size=model==null ? "--" : $"{model.BoundsMeters.size.x:F3} x {model.BoundsMeters.size.y:F3} x {model.BoundsMeters.size.z:F3} m (X/Y/Z)";
                var referenceText=phase==Phase.Adjust ? $"Reference: model {alignment.ModelBaselineMeters:F3} m / room {alignment.PhysicalBaselineMeters:F3} m" : "Yellow line marks model reference A to B.";
                var controls=editorPreview ? "Click: point | Enter: save/retry | R: realign | O: models\nV: VR preview | H: hide model | M: measure | Tab: panel\nWASD/QE: camera | right mouse: look | arrows: nudge\nZ/C: yaw | PageUp/PageDown: height" : "Right trigger: point | A: save/retry | B: realign\nX: passthrough/VR | Y: hide model | right grip: measure\nRight stick: slide | left stick: yaw / height\nLeft grip: models | Menu: panel | physical walking only";
                var status=!trackingHealthy ? "Tracking unavailable" : !anchorTracked ? "Anchor tracking lost; use B to realign" : anchorStatus;
                view.UpdatePanel($"LAB WALK  |  {(editorPreview ? "EDITOR PREVIEW" : Application.isEditor ? "EDITOR XR" : "QUEST")}\n{manifest?.displayName}\n{phase} | {1f/averageFrameTime:F0} FPS\n{size}\n{Wrap(model?.ImportSummary,66)}\nAnchor: {status}\n{Wrap(message,66)}\n{referenceText}\n{measurement}\n\n{controls}",panelVisible || !trackingHealthy || !anchorTracked || phase==Phase.Error || (model?.Incomplete ?? false));
            }
        }

        void FineTune()
        {
            Vector2 slide=Vector2.zero,turn=Vector2.zero;
            if(editorPreview)
            {
                var k=Keyboard.current; if(k==null) return;
                slide=new Vector2((k.rightArrowKey.isPressed?1:0)-(k.leftArrowKey.isPressed?1:0),(k.upArrowKey.isPressed?1:0)-(k.downArrowKey.isPressed?1:0));
                turn=new Vector2((k.cKey.isPressed?1:0)-(k.zKey.isPressed?1:0),(k.pageUpKey.isPressed?1:0)-(k.pageDownKey.isPressed?1:0));
            }
            else { slide=OVRInput.Get(OVRInput.RawAxis2D.RThumbstick); turn=OVRInput.Get(OVRInput.RawAxis2D.LThumbstick); }
            if(slide.magnitude<0.2f) slide=Vector2.zero;
            if(turn.magnitude<0.2f) turn=Vector2.zero;
            var forward=Vector3.ProjectOnPlane(eye.transform.forward,Vector3.up).normalized;
            var right=Vector3.Cross(Vector3.up,forward);
            placement.position+=(right*slide.x+forward*slide.y+Vector3.up*turn.y)*0.08f*Time.unscaledDeltaTime;
            placement.RotateAround(placement.TransformPoint(manifest.referenceA),Vector3.up,turn.x*6*Time.unscaledDeltaTime);
        }
        void MoveDesktopCamera()
        {
            var k=Keyboard.current; if(k==null) return;
            var delta=new Vector3((k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),(k.eKey.isPressed?1:0)-(k.qKey.isPressed?1:0),(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0));
            eye.transform.Translate(delta*Time.unscaledDeltaTime);
            if(Mouse.current!=null && Mouse.current.rightButton.isPressed)
            { var move=Mouse.current.delta.ReadValue(); var e=eye.transform.eulerAngles; eye.transform.rotation=Quaternion.Euler(e.x-move.y*0.1f,e.y+move.x*0.1f,0); }
        }
        static string Wrap(string text,int width)
        {
            if(string.IsNullOrEmpty(text)) return "";
            var output=new StringBuilder(); var column=0;
            foreach(var word in text.Split(' ')) { if(column+word.Length>width) { output.Append('\n'); column=0; } output.Append(word).Append(' '); column+=word.Length+1; }
            return output.ToString().TrimEnd();
        }
        void OnApplicationPause(bool paused) { if(paused && view!=null) view.SetImmersive(false); }
        void LateUpdate() { view?.FollowCamera(); }
        void OnDestroy() { lifetime.Cancel(); candidate?.Dispose(); model?.Dispose(); lifetime.Dispose(); }
    }
}
