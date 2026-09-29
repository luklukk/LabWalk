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
        // Searching: the camera looks for the QR markers. PointA/PointB: placing by two floor points (models without
        // markers, or from Fine-tune). Placed: the model is shown and world-locked for this session.
        enum Phase { Loading, Searching, PointA, PointB, Placed, Error, Importing, Review }
        Phase phase=Phase.Loading;
        readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        readonly MetaAnchorService anchors=new MetaAnchorService();
        ModelManifest manifest;
        LoadedModel model;
        Transform placement;
        WalkthroughView view;
        Camera eye;
        string fingerprint, message="Loading model...", measurement=DefaultMeasurement;
        float messageTime=-100;
        string notice="";   // heading of the last short message (the status panel's title while placed)
        const string DefaultMeasurement="";
        ControllerGuide guide;
        Vector3 realA, measuredA;
        bool firstMeasure, busy, editorPreview, help=true, showModel=true, trackingHealthy;
        float averageFrameTime=1f/72, panelTime;
        AlignmentResult alignment;

        LayerView layerView;
        MenuPanel menu;
        readonly MarkerCalibrator markers=new MarkerCalibrator();
        bool markerPlaced, userAdjusted, tuning, hasPlacement, placedByFloorPoints;
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
                StartPlacement();
                if(warning!=null) Notify("Showing the sample model",warning);
            }
            catch(OperationCanceledException) { }
            catch(Exception e) { if(this) { phase=Phase.Error; message=e.Message+" Left grip opens Models."; Debug.LogException(e); } }
            finally { busy=false; }
        }

        // Makes a loaded candidate the active model; StartPlacement then places it.
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

        // ---- Placement: automatic from the QR markers; everything else is behind "Fine-tune placement" ----

        // A message shown on the status panel for a few seconds (the panel is otherwise hidden once placed).
        void Notify(string heading,string detail="") { notice=heading; message=detail; messageTime=Time.unscaledTime; }
        bool Notifying => Time.unscaledTime-messageTime<6;

        // Places the model from scratch: the camera looks for the QR markers (the model appears when 2 are found);
        // a model without markers is placed with two floor points instead.
        void StartPlacement()
        {
            if(model==null) return;
            view.SetImmersive(false);
            ReleaseSessionAnchor();
            placement.SetParent(null,true);
            hasPlacement=markerPlaced=userAdjusted=placedByFloorPoints=false; tuning=false;
            firstMeasure=false; view.ClearMeasurement(); measurement=DefaultMeasurement;
            if(markers.Active && !editorPreview)
            {
                phase=Phase.Searching;
                message="Face each printed code from about 1 m away. The model appears once two are found.";
            }
            else
            {
                tuning=true; phase=Phase.PointA;
                message=markers.Active ? "Desktop preview: place the model with two floor points (click the floor)." :
                    "This model has no QR markers. Point at floor reference A and pull the right trigger.";
            }
        }

        // Fine-tune (View menu): sticks nudge, B re-snaps, trigger marks floor points, grip measures, A is done.
        void StartFineTune()
        {
            CloseMenu();
            tuning=true;
            if(!hasPlacement) { phase=Phase.PointA; message="Point at floor reference A and pull the right trigger. B cancels."; }
            else message=markers.Active ? "Nudge the model with the sticks; B snaps it back to the QR markers. Or mark two floor points with the right trigger."
                : "Nudge the model with the sticks, or mark two floor points with the right trigger.";
            DiagnosticsLog.Write("Fine-tune placement started");
        }

        void EndFineTune()
        {
            tuning=false;
            if(phase==Phase.PointA || phase==Phase.PointB) phase=hasPlacement ? Phase.Placed : Phase.Searching;
            firstMeasure=false;
            Notify(userAdjusted ? "Placement adjusted" : "Placement unchanged",userAdjusted ? "Fine-tune again and press B to return to the QR markers." : "");
            DiagnosticsLog.Write($"Fine-tune done: {DiagnosticsLog.Pose(placement)}; adjusted {userAdjusted}");
        }

        string PlacementSummary()
        {
            if(phase==Phase.Searching) return markers.Active ? "looking for the QR markers" : "not placed";
            if(!hasPlacement) return "not placed";
            if(placedByFloorPoints) return userAdjusted ? "from floor points, adjusted by hand" : "from floor points";
            if(userAdjusted) return "adjusted by hand";
            return markerPlaced ? $"from {markers.SolutionMarkerCount} QR markers, fit {alignment.RmsResidualMeters*100:F1} cm" : "placed";
        }

        void RecordReference(Vector3 point)
        {
            if(phase==Phase.PointA || phase==Phase.Placed)
            {
                realA=point; phase=Phase.PointB;
                message="Point at floor reference B and pull the right trigger. B cancels.";
            }
            else if(phase==Phase.PointB)
            {
                try
                {
                    alignment=AlignmentMath.Solve(ModelManifest.ToPoint(manifest.referenceA),ModelManifest.ToPoint(manifest.referenceB),ModelManifest.ToPoint(realA),ModelManifest.ToPoint(point));
                    ReleaseSessionAnchor();
                    placement.SetParent(null,true);
                    placement.SetPositionAndRotation(ModelManifest.ToVector(alignment.Translation),Quaternion.Euler(0,(float)(alignment.YawRadians*180/Math.PI),0));
                    placement.localScale=Vector3.one;
                    phase=Phase.Placed; hasPlacement=true; showModel=true; markerPlaced=false; placedByFloorPoints=true; userAdjusted=false;
                    AnchorPlacementForSession();
                    DiagnosticsLog.Write($"Placed from floor points: model baseline {alignment.ModelBaselineMeters:F3} m, room {alignment.PhysicalBaselineMeters:F3} m; {DiagnosticsLog.Pose(placement)}");
                    message=alignment.RelativeBaselineError>0.05 ? "Placed, but the two points are more than 5% apart from the model's: check the points or the model units."
                        : "Placed from floor points. Sticks fine-tune; A: done.";
                }
                catch(Exception e) { message=e.Message; phase=Phase.PointA; }
            }
        }

        void CancelFloorPoints()
        {
            phase=hasPlacement ? Phase.Placed : Phase.PointA;
            message=hasPlacement ? "Floor points cancelled; placement unchanged." : "Point at floor reference A and pull the right trigger.";
            if(!hasPlacement && markers.Active && !editorPreview) { tuning=false; phase=Phase.Searching; message="Face each printed code from about 1 m away. The model appears once two are found."; }
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
            ShowMenuPanel();
        }

        // A newly opened menu rides on the left hand (the desktop preview has no hand: pinned in front instead).
        void ShowMenuPanel()
        {
            if(menu.Visible) return;
            menu.Show();
            if(editorPreview) menu.Pin(eye.transform); else menu.Unpin();
        }

        void TogglePin()
        {
            if(menu.Pinned && !editorPreview) menu.Unpin(); else menu.Pin(eye.transform);
            menuDirty=true;
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
                menu.Title="Review  ›  "+UiStyle.Title(candidate.Manifest.displayName);
                Row($"File  ·  {candidate.Manifest.file}",null,true);
                Row($"Units  ·  {m.SourceUnits}, shown at true size",null,true);
                Row($"Size  ·  {size.x:F2} × {size.z:F2} m, {size.y:F2} m tall",null,true);
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
                menu.Title="Models  ›  Loading";
                Lines(message,false);
                Add(Row(""),"Cancel",MenuPanel.Style.Plain,()=>importCancel?.Cancel());
                menu.Footer="B: cancel";
                return;
            }
            if(page==Page.Models)
            {
                menu.Title="Models";
                if(manifest!=null) Row("Showing: "+UiStyle.Title(manifest.displayName),null,true);
                if(layerView!=null && layerView.Any) Row("‹  Back to View",()=>{ page=Page.View; folder=null; ShowMenu(); });
                if(ModelImport.SystemPickerAvailable) Row("Browse headset files…",StartPicker);
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
            menu.Title=folder==null ? UiStyle.Title(manifest?.displayName ?? "View") : UiStyle.Path(folder.Path);
            if(folder!=null) Row(folder.Parent==null ? "‹  All layers" : "‹  "+folder.Parent.Name,()=>OpenFolder(folder.Parent));
            else if(model!=null) Add(Row("Placement  ·  "+PlacementSummary()),"Fine-tune",MenuPanel.Style.Action,StartFineTune);
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
                if(pointed>0) Add(Row($"Items shown as wireframe  ·  {pointed}"),"Make all solid",MenuPanel.Style.Plain,()=>{ layerView.ClearObjects(); menuDirty=true; });
                else if(model!=null && model.Objects.Count>1) Row("Point at an item and pull the right trigger to see through it.",null,true);
                Row("Change model…",OpenModels);
            }
            menu.Footer=editorPreview ? "Mouse: point + click   ·   Arrows + Enter   ·   Esc: back" : "Point + trigger  ·  or stick + A     B: back";
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
            catch(OperationCanceledException) { if(this) { phase=resumePhase; Notify("Import cancelled","The current model is unchanged."); } }
            catch(Exception e) { if(this) { phase=resumePhase; Notify("Import failed",e.Message+" The current model is unchanged."); Debug.LogException(e); } }
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
                StartPlacement();
            }
            catch(OperationCanceledException) { }
            catch(Exception e)
            {
                if(this) { candidate=null; next.Dispose(); phase=resumePhase; Notify("Could not switch models",e.Message+" The current model is unchanged."); Debug.LogException(e); }
            }
            finally { busy=false; }
        }

        void CancelReview()
        {
            candidate?.Dispose(); candidate=null;
            phase=resumePhase; Notify("Import cancelled","The current model is unchanged.");
        }

        void HandleModelUi(bool accept,bool back,bool press,bool pin,Ray pointer,bool pointerTracked)
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
            var onPanel=pointerTracked && menu.Hit(pointer,out row,out column,out _);
            var onPin=onPanel && row==MenuPanel.PinRow;
            var onTarget=onPanel && row>=0;
            if(menu.PinFocused!=onPin) { menu.PinFocused=onPin; menuDirty=true; }
            var pointed=onTarget ? (row,column) : (-1,-1);
            if(pointed!=lastPointed)
            {
                lastPointed=pointed;
                if(onTarget) { menu.FocusRow=row; menu.FocusColumn=column; menuDirty=true; }
            }
            if((press && onPin) || pin) { TogglePin(); return; }
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
            if(!menu.Visible) { ShowMenuPanel(); menuDirty=true; }
            if(phase!=menuPhase) { menuPhase=phase; menuDirty=true; menu.FocusRow=0; menu.FocusColumn=-1; focusChoice=phase==Phase.Review; }
            if(phase==Phase.Importing && Time.unscaledTime>=menuRefresh) { menuRefresh=Time.unscaledTime+0.3f; menuDirty=true; }
            if(!menuDirty) return;
            menuDirty=false;
            BuildMenuRows();
            if(focusChoice) { focusChoice=false; menu.FocusRow=menu.Rows.Count-1; menu.FocusColumn=0; }
            menu.Render();
        }

        // After tracking updates: the unpinned menu rides on the left controller.
        void PlaceMenu()
        {
            if(menu==null || !menu.Visible || menu.Pinned || editorPreview) return;
            if(OVRInput.GetControllerPositionTracked(OVRInput.Controller.LTouch)) menu.FollowHand(rig.leftControllerAnchor,eye.transform);
        }

        // ---- Pointing at model objects: a label names the object; the right trigger switches it to wireframe and back ----

        ObjectPicker picker;
        readonly List<Renderer> pickRenderers=new List<Renderer>();
        int hoverIndex, pickVersion;
        float nextPick;
        bool clickPending;

        bool CanPick => model!=null && layerView!=null && !busy && trackingHealthy && showModel && !menuOpen && !tuning &&
            phase==Phase.Placed && model.Objects.Count>1 && picker!=null && picker.Available;

        void UpdatePicking(Ray ray,bool tracked,bool click)
        {
            if(!CanPick || !tracked) { SetHover(0); clickPending=false; view.Hover(null,null,null,default,false); return; }
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
                        Notify(layerView.IsObjectWire(hoverIndex) ? "Wireframe" : "Solid again",
                            layerView.IsObjectWire(hoverIndex) ? o.Label+"\nPoint at it again to make it solid." : o.Label);
                    }
                }
            }
            if(!picker.Busy && Time.unscaledTime>=nextPick) { nextPick=Time.unscaledTime+0.08f; picker.Request(ray,pickRenderers); }
            var hovered=layerView.Object(hoverIndex);
            if(hovered==null) { view.Hover(null,null,null,default,false); return; }
            var at=picker.HasDistance ? picker.Ray.GetPoint(picker.Distance) : ray.GetPoint(1.5f);
            var name=string.IsNullOrEmpty(hovered.Name) ? (hovered.Layer?.Name ?? "Unnamed item") : hovered.Name;
            view.Hover(name,UiStyle.Path(hovered.Layer?.Path),layerView.IsObjectWire(hoverIndex) ? "wireframe" : null,at,true);
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
                var anchorMoved=sessionAnchor ? Vector3.Distance(sessionAnchor.transform.position,lastAnchorPosition) : 0;
                var originJumped=maxHeadStep>0.05f || maxHeadTurn>3;
                DiagnosticsLog.Write($"Recenter settled: largest one-frame head step {maxHeadStep*100:F1} cm / {maxHeadTurn:F1} deg (origin jump: {originJumped}); anchor moved {anchorMoved*100:F1} cm; placement {DiagnosticsLog.Pose(placement)}; anchor {DiagnosticsLog.Pose(sessionAnchor ? sessionAnchor.transform : null)}");
                if(originJumped) Notify("View recentered","The model stays locked to the room.");
            }
            if(recenterFollowUp<0)
            {
                lastPlacementPosition=placement.position; lastPlacementYaw=placement.eulerAngles.y;
                if(sessionAnchor) { lastAnchorPosition=sessionAnchor.transform.position; lastAnchorYaw=sessionAnchor.transform.eulerAngles.y; }
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
            var anchorTracked=!sessionAnchor || sessionAnchor.IsTracked;
            if((!trackingHealthy || !anchorTracked) && view.Immersive)
            {
                view.SetImmersive(false);
                Notify("Tracking interrupted","Back to passthrough. Check the model against the room before entering VR again.");
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
                bool pin=editorPreview ? k!=null && k.pKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.Y);
                if(modelUi) HandleModelUi(accept,back,trigger,pin,ray,controllerTracked);
                else if(openModels && !busy) OpenMenu();
            }
            UpdateMenu();
            var pickClick=false;
            if(model!=null && !busy && trackingHealthy && !modelUi && !menuOpen)
            {
                var keys=Keyboard.current;
                bool buttonA=editorPreview ? keys!=null && keys.enterKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.A);
                bool buttonB=editorPreview ? keys!=null && keys.rKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.B);
                bool toggle=editorPreview ? keys!=null && keys.vKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.X);
                bool hide=editorPreview ? keys!=null && keys.hKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.Y);
                bool measure=editorPreview ? keys!=null && keys.mKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.RHandTrigger);
                bool helpButton=editorPreview ? keys!=null && keys.tabKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.Start);
                bool nextDesign=editorPreview ? keys!=null && keys.lKey.wasPressedThisFrame : OVRInput.GetDown(OVRInput.RawButton.LThumbstick);
                if(helpButton) help=!help;
                if(hide && !view.Immersive && hasPlacement) showModel=!showModel;
                if(tuning)
                {
                    // Fine-tune placement: everything about placing the model lives here.
                    var floorPoints=phase==Phase.PointA || phase==Phase.PointB;
                    if(buttonA && !(floorPoints && !hasPlacement)) EndFineTune();
                    else if(buttonB && floorPoints) CancelFloorPoints();
                    else if(buttonB && markers.HasSolution) { ApplyMarkerFit(true); tuning=true; }
                    else if(trigger && hit) RecordReference(point);
                    if(measure && hit)
                    {
                        if(!firstMeasure) { measuredA=point; firstMeasure=true; measurement="Measuring: right grip at the other end of a known distance."; }
                        else { firstMeasure=false; measurement=$"Measured {Vector3.Distance(measuredA,point):F3} m | model reference {manifest.knownDistanceMeters:F3} m"; view.Measurement(measuredA,point); }
                    }
                    if(phase==Phase.Placed) FineTune();
                }
                else if(phase==Phase.Placed)
                {
                    if(nextDesign && layerView!=null && layerView.OptionGroups.Count>0)
                    {
                        var next=layerView.NextOption(layerView.OptionGroups[0]);
                        layerView.Set(next,LayerView.Display.Solid);
                        Notify(next.Name,$"{layerView.OptionGroups[0]} option shown.");
                    }
                    if(trigger) pickClick=true;
                    if(toggle && anchorTracked) { view.SetImmersive(!view.Immersive); showModel=true; }
                }
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
                var visible=showModel && trackingHealthy && anchorTracked && hasPlacement && (phase==Phase.Placed || phase==Phase.PointA || phase==Phase.PointB);
                model.Root.SetActive(visible);
                // Placement aids only while placing or fine-tuning: reference line, and marker crosses
                // (magenta where the model expects each code, cyan where the camera sees it).
                var aids=!view.Immersive && trackingHealthy && (tuning || phase==Phase.Searching);
                view.Reference(placement.TransformPoint(manifest.referenceA),placement.TransformPoint(manifest.referenceB),visible && aids && tuning);
                view.BeginMarkers();
                if(markers.Active && aids)
                    foreach(var (expected,seen) in markers.Visuals(placement))
                    {
                        if(visible) view.Marker(expected,false);
                        if(seen.HasValue) view.Marker(seen.Value,true);
                    }
                view.EndMarkers();
            }
            if(menu.Visible) view.UpdatePanel(null,null,null,false); // the menu panel replaces the status panel while open
            else if(Time.unscaledTime>=panelTime)
            {
                panelTime=Time.unscaledTime+0.15f;
                // Once placed, the status panel only appears for a short message or a problem; placing and
                // fine-tuning show what is needed for that.
                var problem=!trackingHealthy || !anchorTracked || phase==Phase.Error || (model?.Incomplete ?? false);
                var placing=phase!=Phase.Placed || tuning;
                var heading=placing ? PhaseName() : Notifying ? notice : !trackingHealthy ? "Tracking unavailable" : !anchorTracked ? "Room lock lost" : "Import incomplete";
                var text=new StringBuilder();
                if((placing || Notifying) && !string.IsNullOrEmpty(message)) text.Append(Wrap(message,46)).Append('\n');
                if(!trackingHealthy) text.Append("Headset tracking is unavailable.\n");
                else if(!anchorTracked) text.Append("The model's room lock is lost. Look around the room.\n");
                if(model?.Incomplete ?? false) text.Append("Some geometry could not be imported.\n");
                if(markers.Active && (phase==Phase.Searching || tuning)) text.Append('\n').Append(markers.Checklist()).Append('\n');
                if(tuning)
                {
                    text.Append("\nPlacement  ·  ").Append(PlacementSummary()).Append('\n');
                    if(placedByFloorPoints) text.Append($"Floor points  ·  model {alignment.ModelBaselineMeters:F2} m, room {alignment.PhysicalBaselineMeters:F2} m\n");
                    if(measurement!=DefaultMeasurement) text.Append(measurement).Append('\n');
                    if(markers.Active) text.Append(markers.HasSolution ? $"QR fit  ·  {markers.Solution.RmsResidualMeters*100:F1} cm rms\n" : "").Append(Wrap(markers.CameraStatus,60)).Append('\n');
                }
                if(editorPreview)
                    text.Append("\nClick: point · Enter: done · R: re-snap · O: menu · V: VR · H: hide · M: measure · Tab: labels\nWASD/QE: move · right mouse: look · arrows: nudge · Z/C: turn · PgUp/PgDn: raise");
                view.UpdatePanel(UiStyle.Title(manifest?.displayName),heading,text.ToString().TrimEnd(),placing || Notifying || problem);
            }
        }
        // ---- Session anchor: world-locks the placement for this session ----
        // Without a boundary the tracking origin (stage space) is not stable, so content must follow an anchor
        // from the moment it is placed (Meta boundaryless guidance). Nothing is saved: the QR markers place
        // the model again at every launch.
        OVRSpatialAnchor sessionAnchor;
        int sessionAnchorRequest;

        async void AnchorPlacementForSession()
        {
            if(editorPreview) return;
            var request=++sessionAnchorRequest;
            try
            {
                var anchor=await anchors.CreateAsync(new Pose(placement.position,placement.rotation),"Session anchor (unsaved)",lifetime.Token);
                if(!this || request!=sessionAnchorRequest || phase!=Phase.Placed || placement.parent)
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

        void UpdateMarkers(bool modelUi)
        {
            // The camera looks for codes while placing or fine-tuning, and after placement until every code is
            // steady (refining the fit); then it stops to save power. Only with passthrough, tracking and no menu.
            var refining=phase==Phase.Placed && markerPlaced && !userAdjusted && !markers.AllSteady;
            var scan=model!=null && !modelUi && trackingHealthy && !view.Immersive && (phase==Phase.Searching || tuning || refining);
            markers.Update(scan);
            if(!markers.HasSolution || model==null || busy || modelUi || !trackingHealthy) return;
            if(phase==Phase.Searching) { ApplyMarkerFit(true); return; }
            // Keep refining a marker placement while more sightings arrive, until it is adjusted by hand.
            if(phase==Phase.Placed && markerPlaced && !userAdjusted)
            {
                var fit=markers.Solution;
                var target=ModelManifest.ToVector(fit.Translation);
                var yaw=(float)(fit.YawRadians*180/Math.PI);
                if(Vector3.Distance(placement.position,target)>0.002f || Mathf.Abs(Mathf.DeltaAngle(placement.eulerAngles.y,yaw))>0.1f)
                    ApplyMarkerFit(false);
            }
        }

        // Places the model from the markers' fit. announce: say so on the panel (first placement, re-snap).
        void ApplyMarkerFit(bool announce)
        {
            var fit=markers.Solution;
            placement.SetPositionAndRotation(ModelManifest.ToVector(fit.Translation),Quaternion.Euler(0,(float)(fit.YawRadians*180/Math.PI),0));
            placement.localScale=Vector3.one;
            var first=!hasPlacement;
            alignment=fit; phase=Phase.Placed; hasPlacement=true; markerPlaced=true; userAdjusted=false; placedByFloorPoints=false;
            if(first) showModel=true;
            if(!placement.parent) AnchorPlacementForSession();
            var warning=fit.MaxResidualMeters>0.05 ? $" The markers disagree by up to {fit.MaxResidualMeters*100:F0} cm: check their measured positions." :
                fit.RelativeBaselineError>0.05 ? " Marker spacing differs >5% from the model: check units and measurements." : "";
            if(announce) Notify("Model placed",$"From {markers.SolutionMarkerCount} QR markers, fit {fit.RmsResidualMeters*100:F1} cm.{warning}");
            if(announce) DiagnosticsLog.Write($"Placed from markers: {DiagnosticsLog.Pose(placement)}; rms {fit.RmsResidualMeters*100:F1} cm");
        }
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
                    guide.Set(ControllerGuide.Control.RightTrigger,"Press");
                    guide.Set(ControllerGuide.Control.Y,menu.Pinned ? "Menu to hand" : "Pin menu here");
                    var top=page==Page.View ? folder==null : !(layerView!=null && layerView.Any);
                    guide.Set(ControllerGuide.Control.B,top ? "Close" : "Back");
                    guide.Set(ControllerGuide.Control.RightStick,"Choose"); guide.Set(ControllerGuide.Control.LeftStick,"Choose");
                }
                if(phase==Phase.Review) guide.Set(ControllerGuide.Control.RightTrigger,"Press");
                return;
            }
            guide.Set(ControllerGuide.Control.Menu,"Hide labels");
            if(!busy) guide.Set(ControllerGuide.Control.LeftGrip,"Menu");
            if(busy || model==null || phase==Phase.Error) return;
            var passthrough=!view.Immersive;
            if(hasPlacement && passthrough) guide.Set(ControllerGuide.Control.Y,showModel ? "Hide model" : "Show model");
            if(tuning)
            {
                var floorPoints=phase==Phase.PointA || phase==Phase.PointB;
                if(!(floorPoints && !hasPlacement)) guide.Set(ControllerGuide.Control.A,"Done");
                if(floorPoints) guide.Set(ControllerGuide.Control.B,"Cancel floor points");
                else if(markers.HasSolution) guide.Set(ControllerGuide.Control.B,"Re-snap to QR markers");
                guide.Set(ControllerGuide.Control.RightTrigger,phase==Phase.PointB ? "Mark floor point B" : "Mark floor point A");
                guide.Set(ControllerGuide.Control.RightGrip,firstMeasure ? "Measure: end point" : "Measure floor");
                if(phase==Phase.Placed) { guide.Set(ControllerGuide.Control.RightStick,"Slide model"); guide.Set(ControllerGuide.Control.LeftStick,"Turn / raise"); }
                return;
            }
            if(phase!=Phase.Placed) return;
            guide.Set(ControllerGuide.Control.X,passthrough ? "Enter VR" : "Passthrough");
            if(layerView!=null && layerView.OptionGroups.Count>0) guide.Set(ControllerGuide.Control.LeftStick,"Click: show "+layerView.NextOption(layerView.OptionGroups[0]).Name);
            if(CanPick && hoverIndex>0) guide.Set(ControllerGuide.Control.RightTrigger,layerView.IsObjectWire(hoverIndex) ? "Make solid" : "Make wireframe");
        }

        string PhaseName()
        {
            switch(phase)
            {
                case Phase.Loading: return "Loading the model";
                case Phase.Searching: return markers.FoundCount==0 ? "Finding the QR markers" : $"Found {markers.FoundCount} of {markers.Count} markers";
                case Phase.PointA: return "Floor point A";
                case Phase.PointB: return "Floor point B";
                case Phase.Placed: return tuning ? "Fine-tune placement" : view.Immersive ? "VR" : "Placed";
                case Phase.Error: return "Could not load the model";
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
            PlaceMenu();
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
