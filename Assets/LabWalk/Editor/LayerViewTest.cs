using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace LabWalk.Editor
{
    // Loads a model through the runtime import path and exercises the View state: the layer tree, Solid / Wire / Off
    // per layer, folders limiting their contents, exclusive design options, single objects switched to wireframe,
    // and remembering all of it. Optionally renders views (cameraSpec) to TestResults.
    public static class LayerViewTest
    {
        const string Report="TestResults/layer-view.txt";
        const LayerView.Display Solid=LayerView.Display.Solid, Wire=LayerView.Display.Wireframe, Off=LayerView.Display.Hidden;

        // cameraSpec (optional): "x,y,z,yawDegrees" in model meters, e.g. "11.3,1.6,12.5,180".
        public static async void Run(string path,string cameraSpec)
        {
            var log=new StringBuilder();
            ModelCandidate candidate=null; GameObject cameraObject=null, lightObject=null;
            LayerView view=null;
            var fingerprint="layer-view-test";
            try
            {
                Directory.CreateDirectory("TestResults"); File.WriteAllText(Report,"Running: "+path);
                PlayerPrefs.DeleteKey("LabWalk.layers."+fingerprint); PlayerPrefs.DeleteKey("LabWalk.layers."+fingerprint+".objects");
                candidate=await ModelImport.LoadFileAsync(path,CancellationToken.None);
                var model=candidate.Model;
                candidate.Staging.gameObject.SetActive(true); // edit mode: make the staged model renderable
                log.AppendLine(model.ImportSummary);
                view=new LayerView(model,fingerprint);
                void Tree(ModelLayer l,string indent)
                {
                    log.AppendLine($"{indent}{l.Name}  [{l.Kind}{(l.OptionGroup!=null ? " "+l.OptionGroup : "")}]  {view.Own(l)}  renderers {l.Solid.Count}, line meshes {l.Wire.Count}");
                    foreach(var c in l.Children) Tree(c,indent+"   ");
                }
                foreach(var r in view.Roots) Tree(r,"");
                log.AppendLine($"Objects: {model.Objects.Count-1}");
                var shots=cameraSpec!=null ? cameraSpec.Split(',').Select(float.Parse).ToArray() : null;

                // Options: exclusive while solid; a wireframe overlay stays.
                Require(view.OptionGroups.Count>0,"Model has no option layers.");
                var group=view.OptionGroups[0];
                var options=view.Options(group);
                Require(options.Count(o=>view.Own(o)==Solid)==1,"Exactly one solid option at start.");
                foreach(var option in options)
                {
                    view.Set(option,Solid);
                    foreach(var o in options) Require((view.Own(o)==Solid)==(o==option) && o.Root.activeSelf==(o==option),$"Only {option.Name} solid in {group}.");
                    if(shots!=null) Capture(model,shots,$"TestResults/layer-view-{Safe(option.Name)}.png",ref cameraObject,ref lightObject);
                }
                var solidOption=options[options.Count-1]; var overlay=options[0];
                if(overlay!=solidOption)
                {
                    view.Set(solidOption,Solid); view.Set(overlay,Wire);
                    Require(view.Own(solidOption)==Solid && view.Effective(overlay)==Wire && overlay.Root.activeSelf,"Other option overlaid as wireframe.");
                    Require(overlay.Solid.All(r=>!r.enabled) && overlay.Wire.All(w=>w.activeSelf),"Overlay draws lines only.");
                    if(shots!=null) Capture(model,shots,$"TestResults/layer-view-{Safe(solidOption.Name)}-with-{Safe(overlay.Name)}-wireframe.png",ref cameraObject,ref lightObject);
                    log.AppendLine($"{solidOption.Name} solid + {overlay.Name} wireframe. Summary: {view.Summary()}");
                    view.Set(overlay,Off);
                    Require(!overlay.Root.activeSelf,"Overlay off again.");
                }

                // A folder limits what is inside it: Wire draws its children as wireframes, Off hides them.
                var folder=model.Layers.FirstOrDefault(l=>l.Children.Count>0 && view.Effective(l)==Solid && l.Children.Any(c=>c.Solid.Count>0));
                if(folder!=null)
                {
                    var child=folder.Children.First(c=>c.Solid.Count>0);
                    view.Set(child,Solid); view.Set(folder,Wire);
                    Require(view.Own(child)==Solid && view.Effective(child)==Wire && child.Solid.All(r=>!r.enabled) && child.Wire.All(w=>w.activeSelf),"Folder set to Wire draws its children as wireframes.");
                    if(shots!=null) Capture(model,shots,$"TestResults/layer-view-{Safe(folder.Name)}-wireframe.png",ref cameraObject,ref lightObject);
                    view.Set(folder,Off);
                    Require(view.Effective(child)==Off && !folder.Root.activeSelf,"Folder set to Off hides its children.");
                    view.Set(folder,Solid);
                    Require(view.Effective(child)==Solid && child.Solid.All(r=>r.enabled),"Folder back to Solid: children decide again.");
                    log.AppendLine($"Folder {folder.Path}: Wire / Off / Solid limit child {child.Name} as expected.");
                }

                // A single object switched to wireframe: its layer's line meshes holding it are drawn, lines limited to it.
                var pointed=model.Objects.Skip(1).FirstOrDefault(o=>o.Layer!=null && view.Effective(o.Layer)==Solid && o.Layer.WireObjects.Any(s=>s.Contains(o.Index)));
                if(pointed!=null)
                {
                    view.ToggleObject(pointed.Index);
                    Require(view.IsObjectWire(pointed.Index) && view.WireObjectCount==1,"Object marked wireframe.");
                    var lines=pointed.Layer.Wire.Where((w,i)=>pointed.Layer.WireObjects[i].Contains(pointed.Index)).ToList();
                    Require(lines.All(w=>w.activeSelf),"Line meshes of the pointed object are drawn.");
                    var block=new MaterialPropertyBlock(); lines[0].GetComponent<Renderer>().GetPropertyBlock(block);
                    Require(block.GetFloat("_WireAll")==0,"Only the pointed object's lines, not the whole layer.");
                    var state=Shader.GetGlobalTexture("_LabWalkObjectState") as Texture2D;
                    Require(state!=null && state.GetPixel(pointed.Index%256,pointed.Index/256).r>0.5f,"Object state texture marks it.");
                    if(shots!=null) Capture(model,shots,$"TestResults/layer-view-object-{Safe(pointed.Label)}-wireframe.png",ref cameraObject,ref lightObject);
                    log.AppendLine($"Object {pointed.Label} ({pointed.Layer.Path}) switched to wireframe.");
                }

                // Remembered across a reload.
                view.Set(options[0],Solid);
                var reopened=new LayerView(model,fingerprint);
                Require(reopened.Own(options[0])==Solid,"Selected option remembered.");
                Require(model.Layers.All(l=>reopened.Own(l)==view.Own(l)),"Every layer setting remembered.");
                Require(pointed==null || reopened.IsObjectWire(pointed.Index),"Pointed object remembered.");
                reopened.ClearObjects();
                Require(reopened.WireObjectCount==0,"Make all solid clears pointed objects.");
                reopened.Release();
                log.AppendLine("Choices remembered across a reload of the view state.");
                File.WriteAllText(Report,"PASS: layer view\n"+log); Debug.Log("PASS: layer view\n"+log);
            }
            catch(Exception e) { File.WriteAllText(Report,"FAIL: "+e+"\n"+log); Debug.LogException(e); }
            finally
            {
                PlayerPrefs.DeleteKey("LabWalk.layers."+fingerprint); PlayerPrefs.DeleteKey("LabWalk.layers."+fingerprint+".objects");
                view?.Release();
                candidate?.Dispose();
                if(cameraObject) UnityEngine.Object.DestroyImmediate(cameraObject);
                if(lightObject) UnityEngine.Object.DestroyImmediate(lightObject);
            }
        }

        static void Capture(LoadedModel model,float[] spec,string file,ref GameObject cameraObject,ref GameObject lightObject)
        {
            foreach(var r in model.Root.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer=30;
            if(!lightObject)
            {
                lightObject=new GameObject("Layer test light") {hideFlags=HideFlags.HideAndDontSave};
                var light=lightObject.AddComponent<Light>(); light.type=LightType.Directional; light.intensity=0.9f; light.cullingMask=1<<30;
                light.transform.rotation=Quaternion.Euler(55,30,0);
            }
            if(!cameraObject)
            {
                cameraObject=new GameObject("Layer test camera") {hideFlags=HideFlags.HideAndDontSave};
                var cam=cameraObject.AddComponent<Camera>(); cam.cullingMask=1<<30; cam.clearFlags=CameraClearFlags.SolidColor;
                cam.backgroundColor=new Color(0.18f,0.2f,0.24f); cam.fieldOfView=75; cam.nearClipPlane=0.05f;
            }
            var eye=cameraObject.GetComponent<Camera>();
            eye.transform.SetPositionAndRotation(model.Root.transform.TransformPoint(new Vector3(spec[0],spec[1],spec[2])),Quaternion.Euler(8,spec[3],0));
            var target=new RenderTexture(1280,960,24); var pixels=new Texture2D(1280,960,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;
            try
            {
                eye.targetTexture=target; eye.Render();
                RenderTexture.active=target; pixels.ReadPixels(new Rect(0,0,1280,960),0,0); pixels.Apply();
                File.WriteAllBytes(file,pixels.EncodeToPNG());
            }
            finally { eye.targetTexture=null; RenderTexture.active=previous; UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels); }
        }

        static string Safe(string s) => new string(s.Select(c=>char.IsLetterOrDigit(c) ? c : '-').ToArray());
        static void Require(bool value,string message) { if(!value) throw new Exception(message); }
    }
}
