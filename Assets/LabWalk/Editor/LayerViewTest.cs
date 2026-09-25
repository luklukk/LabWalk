using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace LabWalk.Editor
{
    // Loads a model with "Option:"/"Toggle:" layers through the runtime import path, switches options and toggles,
    // checks which layer objects are active and that choices are remembered, and renders one view per option.
    public static class LayerViewTest
    {
        const string Report="TestResults/layer-view.txt";

        // cameraSpec (optional): "x,y,z,yawDegrees" in model meters, e.g. "11.3,1.6,12.5,180".
        public static async void Run(string path,string cameraSpec)
        {
            var log=new StringBuilder();
            ModelCandidate candidate=null; GameObject cameraObject=null, lightObject=null;
            var fingerprint="layer-view-test";
            try
            {
                Directory.CreateDirectory("TestResults"); File.WriteAllText(Report,"Running: "+path);
                PlayerPrefs.DeleteKey("LabWalk.layers."+fingerprint);
                candidate=await ModelImport.LoadFileAsync(path,CancellationToken.None);
                var model=candidate.Model;
                candidate.Staging.gameObject.SetActive(true); // edit mode: make the staged model renderable
                log.AppendLine(model.ImportSummary);
                foreach(var l in model.Layers) log.AppendLine($"Layer: {l.Kind} {(l.OptionGroup!=null ? l.OptionGroup+" / " : "")}{l.Label}  default {(l.DefaultOn ? "on" : "off")}  renderers {l.Root.GetComponentsInChildren<Renderer>(true).Length}");
                var view=new LayerView(model,fingerprint);
                Require(view.OptionGroups.Count>0,"Model has no option layers.");
                var group=view.OptionGroups[0];
                var options=view.Options(group);
                Require(options.Count(view.IsOn)==1,"Exactly one option on at start.");
                foreach(var o in options) Require(o.Root.activeSelf==view.IsOn(o),"Option objects follow state.");
                var shots=cameraSpec!=null ? cameraSpec.Split(',').Select(float.Parse).ToArray() : null;
                foreach(var option in options)
                {
                    view.Activate(option);
                    foreach(var o in options) Require(o.Root.activeSelf==(o==option),$"Only {option.Name} active in {group}.");
                    if(shots!=null) Capture(model,shots,$"TestResults/layer-view-{Safe(option.Name)}.png",ref cameraObject,ref lightObject);
                    log.AppendLine($"Selected {group}: {option.Name} -> active: {string.Join(", ",options.Where(o=>o.Root.activeSelf).Select(o=>o.Name))}");
                }
                var toggle=view.Toggles.FirstOrDefault();
                if(toggle!=null)
                {
                    var before=view.IsOn(toggle);
                    view.Activate(toggle);
                    Require(view.IsOn(toggle)!=before && toggle.Root.activeSelf==view.IsOn(toggle),"Toggle flips its object.");
                    log.AppendLine($"Toggled {toggle.Name}: {(before ? "on" : "off")} -> {(view.IsOn(toggle) ? "on" : "off")}");
                }
                view.Activate(options[0]);
                var reopened=new LayerView(model,fingerprint);
                Require(reopened.Selected(group)==options[0],"Selected option remembered.");
                Require(toggle==null || reopened.IsOn(toggle)==view.IsOn(toggle),"Toggle state remembered.");
                log.AppendLine("Choices remembered across a reload of the view state.");
                File.WriteAllText(Report,"PASS: layer view\n"+log); Debug.Log("PASS: layer view\n"+log);
            }
            catch(Exception e) { File.WriteAllText(Report,"FAIL: "+e+"\n"+log); Debug.LogException(e); }
            finally
            {
                PlayerPrefs.DeleteKey("LabWalk.layers."+fingerprint);
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
