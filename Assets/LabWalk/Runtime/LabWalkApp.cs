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
        string fingerprint, message="Loading model...", anchorStatus="Not placed", measurement=DefaultMeasurement;
        const string DefaultMeasurement="";
        ControllerGuide guide;
        Vector3 realA, measuredA;
        bool firstMeasure, busy, editorPreview, help=true, showModel=true, trackingHealthy;
        float averageFrameTime=1f/72, panelTime;
        AlignmentResult alignment;

        LayerView layerView;
        MenuPanel menu;
        readonly MarkerCalibrator markers=new MarkerCalibrator();
        bool markerPlaced, userAdjusted;
        ModelCandidate candidate;
        CancellationTokenSource importCancel;
        bool menuOpen, waitingForPicker;
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
            RecenterGuard.Hook();
            RecenterGuard.Recentered+=OnRecentered;
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
            menu=new MenuPanel();
            picker=new ObjectPicker();
            if(!editorPreview)
            {
                view.MountPanel(rig.leftControllerAnchor);
                guide=new ControllerGuide(eye,rig.leftControllerAnchor,rig.rightControllerAnchor,view.LineMaterial);
            }
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
            DiagnosticsLog.Write($"Active model: {manifest.displayName} ({manifest.file}). {model.ImportSummary}");
            model.Root.transform.SetParent(placement,false);
            model.Root.SetActive(false);
            markers.SetMarkers(manifest.markers);
            SetHover(0);
            layerView?.Release();
            layerView=new LayerView(model,fingerprint);
            pickRenderers.Clear();
            foreach(var layer in model.Layers) pickRenderers.AddRange(layer.Solid);
            markerPlaced=false;
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
                phase=Phase.Pinned; anchorStatus="Restored; verify against physical landmarks"; ReleaseSessionAnchor(); DiagnosticsLog.Write($"Placement restored from anchor {saved.anchorUuid}: {DiagnosticsLog.Pose(placement)}");
                message="Placement restored. Check alignment in passthrough before X for VR.";
            }
            catch(OperationCanceledException) { }
            catch(Exception e) { if(this) { phase=Phase.Recovery; anchorStatus="Restore failed"; message=e.Message; DiagnosticsLog.Write("Restore failed: "+e.Message); } }
            finally { busy=false; }
        }

        void BeginAlignment()
        {
            if(model==null) return;
            view.SetImmersive(false);
            placement.SetParent(null,true);
            ReleaseSessionAnchor();
            // Keep the old anchor until a replacement and its metadata have both been saved.
            phase=Phase.Origin; showModel=false; firstMeasure=false; view.ClearMeasurement();
            anchorStatus="Aligning; previous saved placement retained";
            message=markers.Active ? "Look at 2 or more QR markers from about 1 m, facing them; the model places itself. Or point at floor reference A and pull the right trigger."
                : "Point at physical floor reference A; right trigger to record.";
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
                    phase=Phase.Adjust; showModel=true; markerPlaced=false;
                    AnchorPlacementForSession();
                    DiagnosticsLog.Write($"Aligned: model baseline {alignment.ModelBaselineMeters:F3} m, room {alignment.PhysicalBaselineMeters:F3} m; {DiagnosticsLog.Pose(placement)}");
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
                phase=Phase.Pinned; anchorStatus="Saved locally"; ReleaseSessionAnchor(); DiagnosticsLog.Write($"Placement saved to anchor {saved.anchorUuid}: {DiagnosticsLog.Pose(placement)}");
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

        // ---- Menu: View (the model's layer tree), Models (import) and model review, on the MenuPanel ----

        enum Page { View, Models }
        Page page;
        ModelLayer folder;   // View folder being shown (null: top level)
        bool menuDirty, focusChoice;
        Phase menuPhase;
        float menuRefresh;
        readonly List<ModelLayer> rowLayers=new List<ModelLayer>();

        void OpenMenu()
        {
            page=layerView!=null && layerView.Any ? Page.View : Page.Models;
            folder=null; menuNote="";
            ShowMenu();
        }

        void ShowMenu()
        {
            menuOpen=true; menuDirty=true;
            menu.FocusRow=0; menu.FocusColumn=-1;
            menu.Show(eye.transform);
        }

        void CloseMenu() { menuOpen=false; menu.Hide(); }

        void OpenFolder(ModelLayer next)
        {
            var previous=folder; folder=next;
            BuildMenuRows();
            var back=previous!=null && previous.Parent==next ? rowLayers.IndexOf(previous) : -1;
            menu.FocusRow=back>=0 ? back : 0; menu.FocusColumn=-1;
            menuDirty=true;
        }

        void OpenModels()
        {
            page=Page.Models;
            menuNote=ModelImport.ListImportFiles().Count==0 ? "No .3dm/.glb files in the import folder yet." : "";
            ShowMenu();
        }

        void SetLayer(ModelLayer layer,LayerView.Display display) { layerView.Set(layer,display); menuDirty=true; }

        void BuildMenuRows()
        {
            menu.Rows.Clear(); rowLayers.Clear();
            MenuPanel.Row Row(string label,Action open=null,bool dim=false)
            { var r=new MenuPanel.Row {Label=label,Open=open,Dim=dim}; menu.Rows.Add(r); rowLayers.Add(null); return r; }
            void Add(MenuPanel.Row row,string text,MenuPanel.Style style,Action press)
            { row.Buttons.Add(new MenuPanel.Button {Text=text,Style=style,Press=press}); }
            void Lines(string text,bool dim=true) { foreach(var line in Wrap(text,58).Split('\n')) if(line.Length>0) Row(line,null,dim); }

            if(phase==Phase.Review && candidate!=null)
            {
                var m=candidate.Model; var size=m.BoundsMeters.size; var largest=Mathf.Max(size.x,size.y,size.z);
                menu.Title="Review model: "+candidate.Manifest.displayName;
                Row($"File: {candidate.Manifest.file}",null,true);
                Row($"Units: {m.SourceUnits} to meters (true size)",null,true);
                Row($"Size: {size.x:F2} x {size.y:F2} x {size.z:F2} m (X/Y/Z)",null,true);
                if(largest>300 || largest<0.3f) Row("CHECK UNITS: this size is unusual for a room.");
                Lines(m.ImportSummary); Lines(candidate.Notes);
                if(m.Incomplete) Row("Some geometry could not be imported (see the summary).");
                var choice=Row(candidate.Bundled ? "Replaces the imported model." : "Kept for the next launch.");
                Add(choice,"Use this model",MenuPanel.Style.Action,()=>_=ConfirmAsync());
                Add(choice,"Cancel",MenuPanel.Style.Plain,CancelReview);
                menu.Footer=editorPreview ? "Enter: use   Esc: cancel" : "A: use this model   B: cancel";
                return;
            }
            if(phase==Phase.Importing)
            {
                menu.Title="Models";
                Lines(message,false);
                Add(Row(""),"Cancel",MenuPanel.Style.Plain,()=>importCancel?.Cancel());
                menu.Footer="B: cancel";
                return;
            }
            if(page==Page.Models)
            {
                menu.Title=$"Models   (current: {manifest?.displayName ?? "none"})";
                if(layerView!=null && layerView.Any) Row("‹  Back to View",()=>{ page=Page.View; folder=null; ShowMenu(); });
                if(ModelImport.SystemPickerAvailable) Row("Browse headset files...",StartPicker);
                foreach(var f in ModelImport.ListImportFiles())
                {
                    var path=f.FullName;
                    Row($"{f.Name}  ({f.Length/1048576.0:F1} MB)",()=>_=ImportAsync(path,false));
                }
                Row("Bundled sample room",()=>_=ImportAsync(null,true));
                if(menuNote.Length>0) Lines(menuNote);
                menu.Footer="Import folder: "+ModelImport.DeviceImportPath;
                return;
            }
            // View: one folder of the model's layer tree, as in Rhino.
            menu.Title=folder==null ? "View" : "View  ›  "+folder.Path.Replace(" > ","  ›  ");
            if(folder!=null) Row("‹  Back",()=>OpenFolder(folder.Parent));
            foreach(var layer in folder==null ? layerView.Roots : folder.Children)
            {
                var l=layer;
                var own=layerView.Own(l);
                var limited=layerView.Effective(l)<own;
                var row=Row(l.Name+(limited ? "  (limited by folder)" : ""),l.Children.Count>0 ? ()=>OpenFolder(l) : (Action)null);
                rowLayers[rowLayers.Count-1]=l;
                row.ArrowSlot=true;
                Add(row,"Solid",own==LayerView.Display.Solid ? MenuPanel.Style.Solid : MenuPanel.Style.Plain,()=>SetLayer(l,LayerView.Display.Solid));
                if(LayerView.HasWireframe(l)) Add(row,"Wire",own==LayerView.Display.Wireframe ? MenuPanel.Style.Wire : MenuPanel.Style.Plain,()=>SetLayer(l,LayerView.Display.Wireframe));
                Add(row,"Off",own==LayerView.Display.Hidden ? MenuPanel.Style.Off : MenuPanel.Style.Plain,()=>SetLayer(l,LayerView.Display.Hidden));
                if(l.Children.Count>0) Add(row,"›",MenuPanel.Style.Action,()=>OpenFolder(l));
            }
            if(folder==null)
            {
                var pointed=layerView.WireObjectCount;
                if(pointed>0) Add(Row($"Objects made wireframe by pointing: {pointed}"),"Make all solid",MenuPanel.Style.Plain,()=>{ layerView.ClearObjects(); menuDirty=true; });
                else if(model!=null && model.Objects.Count>1) Row("Tip: point at an object + right trigger: wireframe",null,true);
                Row("Change model...",OpenModels);
            }
            menu.Footer=editorPreview ? "Mouse: point + click   Arrows + Enter   Esc: back" : "Point + right trigger, or stick + A   |   B: back";
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
            resumePhase=phase; phase=Phase.Importing; view.SetImmersive(false); help=true;
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
                view.ClearMeasurement(); firstMeasure=false; measurement=DefaultMeasurement;
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

        void HandleModelUi(bool accept,bool back,bool press,Ray pointer,bool pointerTracked)
        {
            if(phase==Phase.Importing) { if(back) importCancel?.Cancel(); return; }
            if(waitingForPicker)
            {
                PollPicker();
                if(back && waitingForPicker) { waitingForPicker=false; menuNote="Stopped waiting for the picker."; }
                menuDirty=true;
                return;
            }
            // Pointing: the row and button under the right controller's ray take the focus; the trigger presses them.
            // (Only when the ray moves onto another target, so the sticks can still move the focus while it rests.)
            int row=-1, column=-1;
            var onTarget=pointerTracked && menu.Hit(pointer,out row,out column,out _) && row>=0;
            var pointed=onTarget ? (row,column) : (-1,-1);
            if(pointed!=lastPointed)
            {
                lastPointed=pointed;
                if(onTarget) { menu.FocusRow=row; menu.FocusColumn=column; menuDirty=true; }
            }
            if(phase==Phase.Review)
            {
                if(accept) _=ConfirmAsync();
                else if(back) CancelReview();
                else if(press && onTarget) menu.Press();
                return;
            }
            var (rows,columns)=MenuMove();
            if(rows!=0 || columns!=0) { menu.Move(rows,columns); menuDirty=true; }
            if(accept || (press && onTarget)) { menu.Press(); menuDirty=true; }
            else if(back)
            {
                if(page==Page.Models && layerView!=null && layerView.Any) { page=Page.View; folder=null; ShowMenu(); }
                else if(page==Page.View && folder!=null) OpenFolder(folder.Parent);
                else CloseMenu();
            }
        }

        (int row,int column) lastPointed=(-1,-1);

        (int rows,int columns) MenuMove()
        {
            if(editorPreview)
            {
                var k=Keyboard.current; if(k==null) return (0,0);
                return (k.downArrowKey.wasPressedThisFrame ? 1 : k.upArrowKey.wasPressedThisFrame ? -1 : 0,
                    k.rightArrowKey.wasPressedThisFrame ? 1 : k.leftArrowKey.wasPressedThisFrame ? -1 : 0);
            }
            var stick=OVRInput.Get(OVRInput.RawAxis2D.RThumbstick)+OVRInput.Get(OVRInput.RawAxis2D.LThumbstick);
            if(stick.magnitude<0.5f) { menuRepeat=0; return (0,0); }
            if(Time.unscaledTime<menuRepeat) return (0,0);
            menuRepeat=Time.unscaledTime+0.3f;
            return Mathf.Abs(stick.y)>=Mathf.Abs(stick.x) ? (stick.y>0 ? -1 : 1,0) : (0,stick.x>0 ? 1 : -1);
        }

        // Shows the menu panel while a menu, an import or a model review is active, and redraws it when needed.
        void UpdateMenu()
        {
            var ui=menuOpen || phase==Phase.Importing || phase==Phase.Review;
            if(!ui) { if(menu.Visible) menu.Hide(); menuPhase=phase; return; }
            if(!menu.Visible) { menu.Show(eye.transform); menuDirty=true; }
            if(phase!=menuPhase) { menuPhase=phase; menuDirty=true; menu.FocusRow=0; menu.FocusColumn=-1; focusChoice=phase==Phase.Review; }
            menu.Follow(eye.transform);
            if(phase==Phase.Importing && Time.unscaledTime>=menuRefresh) { menuRefresh=Time.unscaledTime+0.3f; menuDirty=true; }
            if(!menuDirty) return;
            menuDirty=false;
            BuildMenuRows();
            if(focusChoice) { focusChoice=false; menu.FocusRow=menu.Rows.Count-1; menu.FocusColumn=0; }
            menu.Render();
        }

        // ---- Pointing at model objects: a label names the object; the right trigger switches it to wireframe and back ----

        ObjectPicker picker;
        readonly List<Renderer> pickRenderers=new List<Renderer>();
        int hoverIndex, pickVersion;
        float nextPick;
        bool clickPending;

        bool CanPick => model!=null && layerView!=null && !busy && trackingHealthy && showModel && !menuOpen &&
            (phase==Phase.Adjust || phase==Phase.Pinned) && model.Objects.Count>1 && picker!=null && picker.Available;

        void UpdatePicking(Ray ray,bool tracked,bool click)
        {
            if(!CanPick || !tracked) { SetHover(0); clickPending=false; view.Hover(null,default,false); return; }
            if(click) { clickPending=true; nextPick=0; }
            if(picker.Version!=pickVersion)
            {
                pickVersion=picker.Version;
                SetHover(picker.Index);
                if(clickPending)
                {
                    clickPending=false;
                    var o=layerView.Object(hoverIndex);
                    if(o!=null)
                    {
                        layerView.ToggleObject(hoverIndex);
                        message=layerView.IsObjectWire(hoverIndex) ? $"{o.Label}: wireframe. Point + trigger again for solid; View menu: Make all solid."
                            : $"{o.Label}: solid again.";
                    }
                }
            }
            if(!picker.Busy && Time.unscaledTime>=nextPick) { nextPick=Time.unscaledTime+0.08f; picker.Request(ray,pickRenderers); }
            var hovered=layerView.Object(hoverIndex);
            if(hovered==null) { view.Hover(null,default,false); return; }
            var at=picker.HasDistance ? picker.Ray.GetPoint(picker.Distance) : ray.GetPoint(1.5f);
            var name=string.IsNullOrEmpty(hovered.Name) ? "(unnamed object)" : hovered.Name;
            view.Hover($"{name}\n{hovered.Layer?.Path}\nTrigger: {(layerView.IsObjectWire(hoverIndex) ? "make solid" : "make wireframe")}",at,true);
        }

        void SetHover(int index)
        {
            if(index==hoverIndex) return;
            hoverIndex=index;
            layerView?.SetHover(index);
        }
        // Recenter diagnostics: log where the placement and anchor were just before the event and once
        // the (possibly several) origin updates have settled. With recentering disabled both should match.
        // A recenter that moves the XR origin shows up as a one-frame jump in the head pose (people cannot
        // move 5 cm or turn 3 degrees in one frame); the placement's Unity pose alone cannot reveal it.
        float recenterFollowUp=-1, maxHeadStep, maxHeadTurn;
        Vector3 lastPlacementPosition, lastAnchorPosition, lastHeadPosition;
        float lastPlacementYaw, lastAnchorYaw, lastHeadYaw;

        // XR reports origin updates while it starts; only events after tracking has been steady are recenters.
        float firstTrackedTime=-1;

        void OnRecentered()
        {
            markers.Reset("tracking origin update");
            if(firstTrackedTime<0 || Time.unscaledTime<firstTrackedTime+3)
            {
                DiagnosticsLog.Write("Tracking origin update during startup (not a recenter)");
                return;
            }
            if(recenterFollowUp<0)
            {
                DiagnosticsLog.Write($"Recenter #{RecenterGuard.Count} in {phase}: before placement pos {lastPlacementPosition:F3} yaw {lastPlacementYaw:F2}; anchor pos {lastAnchorPosition:F3} yaw {lastAnchorYaw:F2}; head pos {lastHeadPosition:F3} yaw {lastHeadYaw:F2}");
                maxHeadStep=0; maxHeadTurn=0;
            }
            recenterFollowUp=Time.unscaledTime+1.5f;
        }

        void TrackRecenter()
        {
            RecenterGuard.Poll();
            var head=eye.transform;
            if(recenterFollowUp>=0)
            {
                maxHeadStep=Mathf.Max(maxHeadStep,Vector3.Distance(head.position,lastHeadPosition));
                maxHeadTurn=Mathf.Max(maxHeadTurn,Mathf.Abs(Mathf.DeltaAngle(head.eulerAngles.y,lastHeadYaw)));
            }
            lastHeadPosition=head.position; lastHeadYaw=head.eulerAngles.y;
            if(recenterFollowUp>=0 && Time.unscaledTime>=recenterFollowUp)
            {
                recenterFollowUp=-1;
                var anchorMoved=activeAnchor ? Vector3.Distance(activeAnchor.transform.position,lastAnchorPosition) : 0;
                var originJumped=maxHeadStep>0.05f || maxHeadTurn>3;
                DiagnosticsLog.Write($"Recenter settled: largest one-frame head step {maxHeadStep*100:F1} cm / {maxHeadTurn:F1} deg (origin jump: {originJumped}); anchor moved {anchorMoved*100:F1} cm; placement {DiagnosticsLog.Pose(placement)}; anchor {DiagnosticsLog.Pose(activeAnchor ? activeAnchor.transform : null)}");
                message=originJumped ? "View recentered and the tracking origin jumped. Check the landmarks; B realigns if the model moved."
                    : "View recentered; tracking origin unchanged. The model should not have moved.";
            }
            if(recenterFollowUp<0)
            {
                lastPlacementPosition=placement.position; lastPlacementYaw=placement.eulerAngles.y;
                if(activeAnchor) { lastAnchorPosition=activeAnchor.transform.position; lastAnchorYaw=activeAnchor.transform.eulerAngles.y; }
            }
        }

        void Update()
        {
            if(view==null || eye==null) return;
            TrackRecenter();
            UpdateBoundary();
            averageFrameTime=Mathf.Lerp(averageFrameTime,Time.unscaledDeltaTime,0.04f);
            trackingHealthy=editorPreview || (OVRManager.isHmdPresent && OVRManager.hasInputFocus &&
                OVRManager.tracker != null && OVRManager.tracker.isPositionTracked);
            if(trackingHealthy && firstTrackedTime<0) firstTrackedTime=Time.unscaledTime;
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
            // Floor hits count out to 20 m; past that (or pointing level/up) the ray is shown as a short fading beam.
            var hit=floor.Raycast(ray,out var distance) && distance>0 && distance<20 && ray.direction.y < -0.02f && controllerTracked;
            var point=ray.GetPoint(hit ? distance : 2);
            bool trigger=editorPreview ? Mouse.current!=null && Mouse.current.leftButton.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger);
            // The menus and import review take all input while open; they work without a model
            // (e.g. after a load error) and without tracking, since they only show the panel.
            var modelUi=menuOpen || phase==Phase.Importing || phase==Phase.Review;
            {
                var k=Keyboard.current;
                bool accept=editorPreview ? k!=null && k.enterKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.A);
                bool back=editorPreview ? k!=null && (k.escapeKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame) : OVRInput.GetDown(OVRInput.RawButton.B);
                bool openModels=editorPreview ? k!=null && k.oKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.LHandTrigger);
                if(modelUi) HandleModelUi(accept,back,trigger,ray,controllerTracked);
                else if(openModels && !busy && phase!=Phase.Saving) OpenMenu();
            }
            UpdateMenu();
            var pickClick=false;
            if(model!=null && !busy && trackingHealthy && !modelUi && !menuOpen)
            {
                var keys=Keyboard.current;
                bool save=editorPreview ? keys!=null && keys.enterKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.A);
                bool realign=editorPreview ? keys!=null && keys.rKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.B);
                bool toggle=editorPreview ? keys!=null && keys.vKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.X);
                bool hide=editorPreview ? keys!=null && keys.hKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.Y);
                bool measure=editorPreview ? keys!=null && keys.mKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.RHandTrigger);
                bool helpButton=editorPreview ? keys!=null && keys.tabKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.Start);
                bool snap=editorPreview ? keys!=null && keys.kKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.LIndexTrigger);
                bool nextDesign=editorPreview ? keys!=null && keys.lKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.LThumbstick);
                if(nextDesign && layerView!=null && layerView.OptionGroups.Count>0)
                {
                    var next=layerView.NextOption(layerView.OptionGroups[0]);
                    layerView.Set(next,LayerView.Display.Solid);
                    message=$"Showing {layerView.OptionGroups[0]}: {next.Name}";
                }
                if(realign) BeginAlignment();
                else if(trigger && (phase==Phase.Adjust || phase==Phase.Pinned)) pickClick=true;
                else if(trigger && hit) RecordReference(point);
                else if(snap && markers.HasSolution) ApplyMarkerFit("Re-snapped to markers");
                if(save && phase==Phase.Adjust) _=SaveAsync();
                else if(save && phase==Phase.Recovery) _=RestoreAsync();
                // VR is allowed from an aligned but unsaved placement (session only): spatial anchors may be
                // unavailable, e.g. on headsets in shared mode. The same reference-length check as saving applies.
                var sessionOnly=phase==Phase.Adjust && alignment.RelativeBaselineError<=0.05;
                if(toggle && (phase==Phase.Pinned || sessionOnly) && anchorTracked)
                {
                    view.SetImmersive(!view.Immersive); help=!view.Immersive; showModel=true;
                    if(sessionOnly && view.Immersive && !editorPreview) message="VR with an unsaved placement: it will not be restored next launch.";
                }
                if(hide && !view.Immersive) showModel=!showModel;
                if(helpButton) help=!help;
                if(measure && hit && !view.Immersive)
                {
                    if(!firstMeasure) { measuredA=point; firstMeasure=true; measurement="Measuring: right grip at the other end of the known distance."; }
                    else { firstMeasure=false; measurement=$"Measured {Vector3.Distance(measuredA,point):F3} m | expected {manifest.knownDistanceMeters:F3} m"; view.Measurement(measuredA,point); }
                }
                if(phase==Phase.Adjust) FineTune();
            }
            UpdateMarkers(modelUi);
            UpdatePicking(ray,controllerTracked,pickClick);
            // Pointer: to the menu panel, to a pointed model object, or to the floor.
            {
                var end=point; var onFloor=hit; var show=controllerTracked && trackingHealthy && !view.Immersive;
                if(modelUi && menu.Hit(ray,out _,out _,out var menuPoint)) { end=menuPoint; onFloor=false; show=controllerTracked; }
                else if(hoverIndex>0 && picker.HasDistance) { end=picker.Ray.GetPoint(picker.Distance); onFloor=false; show=controllerTracked && trackingHealthy; }
                view.Pointer(ray.origin,end,show && !editorPreview,onFloor);
            }
            if(model!=null)
            {
                var visible=showModel && trackingHealthy && anchorTracked && (phase==Phase.Adjust || phase==Phase.Pinned || phase==Phase.Saving);
                model.Root.SetActive(visible);
                view.Reference(placement.TransformPoint(manifest.referenceA),placement.TransformPoint(manifest.referenceB),visible && !view.Immersive);
                view.BeginMarkers();
                if(markers.Active && !view.Immersive && trackingHealthy)
                    foreach(var (expected,seen) in markers.Visuals(placement))
                    {
                        if(visible) view.Marker(expected,false);
                        if(seen.HasValue) view.Marker(seen.Value,true);
                    }
                view.EndMarkers();
            }
            if(menu.Visible) view.UpdatePanel(null,false); // the menu panel replaces the status panel while open
            else if(Time.unscaledTime>=panelTime)
            {
                panelTime=Time.unscaledTime+0.15f;
                var size=model==null ? "--" : $"{model.BoundsMeters.size.x:F3} x {model.BoundsMeters.size.y:F3} x {model.BoundsMeters.size.z:F3} m (X/Y/Z)";
                var referenceText=phase==Phase.Adjust ? $"Reference: model {alignment.ModelBaselineMeters:F3} m / room {alignment.PhysicalBaselineMeters:F3} m" : "Yellow line marks model reference A to B.";
                var status=!trackingHealthy ? "Tracking unavailable" : !anchorTracked ? "Anchor tracking lost; use B to realign" : anchorStatus;
                // Button help lives on the controller tooltips; the panel keeps state and messages only.
                var text=new StringBuilder($"{manifest?.displayName ?? "LAB WALK"}  |  {PhaseName()}\n{Wrap(message,52)}\nAnchor: {status}");
                var designs=layerView?.Summary();
                if(designs!=null) text.Append('\n').Append(Wrap(designs,52));
                if(markers.Active) text.Append('\n').Append(Wrap(markers.Status,52));
                if(measurement!=DefaultMeasurement) text.Append('\n').Append(measurement);
                if(help)
                    text.Append($"\n\n{1f/averageFrameTime:F0} FPS  |  {size}\n{Wrap(model?.ImportSummary,52)}\n{referenceText}");
                else if(!editorPreview)
                    text.Append("\nMenu button (left): show button help");
                if(editorPreview)
                    text.Append("\n\nClick: point | Enter: save/retry | R: realign | O: models | K: snap to markers\nV: VR preview | H: hide model | M: measure | Tab: help\nWASD/QE: camera | right mouse: look | arrows: nudge | Z/C: yaw | PageUp/PageDown: height");
                view.UpdatePanel(text.ToString(),!view.Immersive || help || !trackingHealthy || !anchorTracked || phase==Phase.Error || (model?.Incomplete ?? false));
            }
        }

        // ---- Session anchor: world-locks an aligned but unsaved placement ----
        // Without a boundary the tracking origin (stage space) is not stable, so content must follow an anchor
        // from the moment it is placed, not only after it is saved (Meta boundaryless guidance).
        OVRSpatialAnchor sessionAnchor;
        int sessionAnchorRequest;

        async void AnchorPlacementForSession()
        {
            if(editorPreview) return;
            var request=++sessionAnchorRequest;
            try
            {
                var anchor=await anchors.CreateAsync(new Pose(placement.position,placement.rotation),"Session anchor (unsaved)",lifetime.Token);
                if(!this || request!=sessionAnchorRequest || phase!=Phase.Adjust || placement.parent)
                { if(anchor) Destroy(anchor.gameObject); return; }
                ReleaseSessionAnchor();
                sessionAnchor=anchor;
                placement.SetParent(anchor.transform,true);
                DiagnosticsLog.Write("Placement attached to a session anchor (unsaved)");
            }
            catch(OperationCanceledException) { }
            catch(Exception e) { DiagnosticsLog.Write("Session anchor failed: "+e.Message); }
        }

        void ReleaseSessionAnchor()
        {
            sessionAnchorRequest++;
            if(!sessionAnchor) return;
            if(placement.parent==sessionAnchor.transform) placement.SetParent(null,true);
            Destroy(sessionAnchor.gameObject); // never saved, so nothing persists
            sessionAnchor=null;
        }

        // ---- Boundary: suppressed while passthrough is visible (walking between rooms); back in full VR ----
        bool boundaryHooked;
        void UpdateBoundary()
        {
            if(editorPreview || !OVRManager.instance) return;
            if(!boundaryHooked)
            {
                boundaryHooked=true;
                OVRManager.BoundaryVisibilityChanged+=visibility=>DiagnosticsLog.Write("System boundary visibility: "+visibility);
            }
            var suppress=!view.Immersive;
            if(OVRManager.instance.shouldBoundaryVisibilityBeSuppressed!=suppress)
            {
                OVRManager.instance.shouldBoundaryVisibilityBeSuppressed=suppress;
                DiagnosticsLog.Write(suppress ? "Requesting no boundary (passthrough visible)" : "Requesting boundary (full VR)");
            }
        }

        // ---- QR marker calibration ----

        float markerWarnTime;
        void UpdateMarkers(bool modelUi)
        {
            // The camera looks for codes only while it is useful: passthrough visible, tracking healthy, no menu.
            var scanPhase=phase==Phase.Origin || phase==Phase.Direction || phase==Phase.Adjust || phase==Phase.Pinned || phase==Phase.Recovery;
            markers.Update(model!=null && scanPhase && !modelUi && trackingHealthy && !view.Immersive);
            if(!markers.HasSolution || model==null || busy || modelUi || !trackingHealthy) return;
            // Direction means the user has started a manual two-point alignment; let them finish it.
            var waiting=phase==Phase.Origin || phase==Phase.Recovery;
            if(waiting) { ApplyMarkerFit("Placed automatically from markers"); return; }
            // Keep refining a marker placement while more samples arrive, until the user nudges it by hand.
            if(phase==Phase.Adjust && markerPlaced && !userAdjusted)
            {
                var fit=markers.Solution;
                var target=ModelManifest.ToVector(fit.Translation);
                var yaw=(float)(fit.YawRadians*180/Math.PI);
                if(Vector3.Distance(placement.position,target)>0.002f || Mathf.Abs(Mathf.DeltaAngle(placement.eulerAngles.y,yaw))>0.1f)
                    ApplyMarkerFit("Placed automatically from markers");
                return;
            }
            // A saved or hand-adjusted placement that the markers contradict: say so, do not move it silently.
            if((phase==Phase.Pinned || phase==Phase.Adjust) && Time.unscaledTime>=markerWarnTime)
            {
                markerWarnTime=Time.unscaledTime+2;
                var off=markers.Deviation(placement);
                if(off>0.03f) message=$"Markers show the model is off by {off*100:F1} cm. Left trigger re-snaps to the markers.";
            }
        }

        void ApplyMarkerFit(string reason)
        {
            var fit=markers.Solution;
            var wasPinned=phase==Phase.Pinned;
            // Detach from a saved anchor (kept until a new placement is saved); a session anchor can stay.
            if(placement.parent && (!sessionAnchor || placement.parent!=sessionAnchor.transform)) placement.SetParent(null,true);
            placement.SetPositionAndRotation(ModelManifest.ToVector(fit.Translation),Quaternion.Euler(0,(float)(fit.YawRadians*180/Math.PI),0));
            placement.localScale=Vector3.one;
            alignment=fit; phase=Phase.Adjust; showModel=true; markerPlaced=true; userAdjusted=false;
            if(!placement.parent) AnchorPlacementForSession();
            if(wasPinned) anchorStatus="Re-snapped; previous saved placement retained until A";
            var warning=fit.MaxResidualMeters>0.05 ? $" Markers disagree by up to {fit.MaxResidualMeters*100:F0} cm: check their measured positions." :
                fit.RelativeBaselineError>0.05 ? " Marker spacing differs >5% from the model: check units and measurements." : "";
            message=$"{reason}: {markers.SolutionMarkerCount} markers, fit {fit.RmsResidualMeters*100:F1} cm.{warning} A saves; X enters VR; left trigger re-snaps.";
            if(reason.StartsWith("Re-snapped") || !markerPlacedLogged) DiagnosticsLog.Write($"{reason}: {DiagnosticsLog.Pose(placement)}; rms {fit.RmsResidualMeters*100:F1} cm");
            markerPlacedLogged=true;
        }
        bool markerPlacedLogged;

        // ---- Controller tooltips: labels describe what each button does in the current state ----

        void UpdateTooltips()
        {
            guide.Clear();
            if(menuOpen || phase==Phase.Importing || phase==Phase.Review)
            {
                if(phase==Phase.Importing) guide.Set(ControllerGuide.Control.B,"Cancel loading");
                else if(phase==Phase.Review) { guide.Set(ControllerGuide.Control.A,"Use this model"); guide.Set(ControllerGuide.Control.B,"Cancel"); }
                else if(waitingForPicker) guide.Set(ControllerGuide.Control.B,"Stop waiting");
                else
                {
                    guide.Set(ControllerGuide.Control.A,"Press");
                    guide.Set(ControllerGuide.Control.RightTrigger,"Press (point)");
                    var top=page==Page.View ? folder==null : !(layerView!=null && layerView.Any);
                    guide.Set(ControllerGuide.Control.B,top ? "Close" : "Back");
                    guide.Set(ControllerGuide.Control.RightStick,"Move"); guide.Set(ControllerGuide.Control.LeftStick,"Move");
                }
                if(phase==Phase.Review) guide.Set(ControllerGuide.Control.RightTrigger,"Press (point)");
                return;
            }
            guide.Set(ControllerGuide.Control.Menu,"Hide help");
            if(!busy && phase!=Phase.Saving) guide.Set(ControllerGuide.Control.LeftGrip,layerView!=null && layerView.Any ? "View / models" : "Models");
            // Left stick click cycles the first design option; the adjust label is combined with it below.
            var cycle=layerView!=null && layerView.OptionGroups.Count>0 && model!=null ? "Click: "+layerView.NextOption(layerView.OptionGroups[0]).Name : null;
            if(cycle!=null && phase!=Phase.Adjust) guide.Set(ControllerGuide.Control.LeftStick,cycle);
            if(busy || model==null) return;
            var passthrough=!view.Immersive;
            switch(phase)
            {
                case Phase.Origin: guide.Set(ControllerGuide.Control.RightTrigger,"Mark floor point A"); break;
                case Phase.Direction: guide.Set(ControllerGuide.Control.RightTrigger,"Mark floor point B"); guide.Set(ControllerGuide.Control.B,"Start over"); break;
                case Phase.Adjust:
                    guide.Set(ControllerGuide.Control.A,"Save placement");
                    guide.Set(ControllerGuide.Control.B,"Realign");
                    guide.Set(ControllerGuide.Control.RightStick,"Slide model");
                    guide.Set(ControllerGuide.Control.LeftStick,cycle!=null ? "Turn / raise  |  "+cycle : "Turn / raise");
                    if(alignment.RelativeBaselineError<=0.05) guide.Set(ControllerGuide.Control.X,passthrough ? "Enter VR (unsaved)" : "Passthrough");
                    break;
                case Phase.Pinned:
                    guide.Set(ControllerGuide.Control.X,passthrough ? "Enter VR" : "Passthrough");
                    guide.Set(ControllerGuide.Control.B,"Realign");
                    break;
                case Phase.Recovery: guide.Set(ControllerGuide.Control.A,"Retry restore"); guide.Set(ControllerGuide.Control.B,"Realign"); break;
                case Phase.Error: return;
            }
            if(passthrough && (phase==Phase.Adjust || phase==Phase.Pinned)) guide.Set(ControllerGuide.Control.Y,showModel ? "Hide model" : "Show model");
            if(CanPick && hoverIndex>0) guide.Set(ControllerGuide.Control.RightTrigger,layerView.IsObjectWire(hoverIndex) ? "Make solid" : "Make wireframe");
            if(passthrough && phase!=Phase.Loading) guide.Set(ControllerGuide.Control.RightGrip,firstMeasure ? "Measure: end point" : "Measure floor");
            if(markers.HasSolution && (phase==Phase.Adjust || phase==Phase.Pinned)) guide.Set(ControllerGuide.Control.LeftTrigger,"Snap to markers");
        }

        string PhaseName()
        {
            switch(phase)
            {
                case Phase.Origin: return markers.Active ? "Look at markers or mark point A" : "Align: mark point A";
                case Phase.Direction: return "Align: mark point B";
                case Phase.Adjust: return "Adjust, not saved";
                case Phase.Pinned: return view.Immersive ? "VR" : "Placed";
                case Phase.Recovery: return "Restore failed";
                default: return phase.ToString();
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
            if(slide!=Vector2.zero || turn!=Vector2.zero) userAdjusted=true;
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
        void LateUpdate()
        {
            view?.FollowCamera();
            if(guide!=null) { UpdateTooltips(); guide.Update(help && trackingHealthy); }
        }
        void OnDestroy()
        {
            RecenterGuard.Recentered-=OnRecentered; RecenterGuard.Unhook();
            lifetime.Cancel(); candidate?.Dispose(); model?.Dispose(); lifetime.Dispose();
            picker?.Release(); layerView?.Release();
        }
    }
}
