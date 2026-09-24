using System;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace LabWalk.Editor
{
    public static class RhinoImportSmokeTest
    {
        public static string LastResult="Not run";
        [MenuItem("Lab Walk/4. Test 3DM import")]
        public static async void Run()
        {
            GameObject parent=null, cameraObject=null, lightObject=null;
            LoadedModel loaded=null;
            RenderTexture target=null;
            Texture2D pixels=null;
            var previous=RenderTexture.active;
            int exit=1;
            try
            {
                if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before this import test.");
                LastResult="Running";
                Directory.CreateDirectory("TestResults");
                var manifest=new ModelManifest {file="sample-room.3dm",coordinateUnits="rhinoDocument",sourceUnits="fromDocument"};
                var scale=manifest.ValidateAndGetScale();
                parent=new GameObject("3DM test placement"); parent.hideFlags=HideFlags.HideAndDontSave;
                parent.transform.SetPositionAndRotation(new Vector3(12,3,-7),Quaternion.Euler(0,37,0));
                var pose=new Pose(parent.transform.position,parent.transform.rotation);
                loaded=await ModelLoaderFactory.Create(manifest).LoadAsync(File.ReadAllBytes("Assets/StreamingAssets/Models/sample-room.3dm"),parent.transform,scale,CancellationToken.None);
                Require(Vector3.Distance(loaded.BoundsMeters.size,new Vector3(6,3.1f,8))<0.002f,"Unexpected 3DM dimensions.");
                Require(loaded.Root.GetComponentsInChildren<Renderer>().Length==5,"Expected 5 merged Rhino sample meshes (one per color).");
                Require(parent.transform.position==pose.position && parent.transform.rotation==pose.rotation,"Import moved placement.");
                Require(loaded.Root.transform.localScale==Vector3.one && !loaded.Incomplete,"Unexpected scale or partial import.");
                foreach(var renderer in loaded.Root.GetComponentsInChildren<Renderer>())
                {
                    renderer.gameObject.layer=30;
                    Require(renderer.sharedMaterial.shader.name=="LabWalk/RhinoBasic" && renderer.sharedMaterial.shader.isSupported,"Rhino shader unavailable.");
                }
                parent.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                cameraObject=new GameObject("Rhino test camera"); cameraObject.hideFlags=HideFlags.HideAndDontSave;
                var eye=cameraObject.AddComponent<Camera>(); eye.cullingMask=1<<30; eye.nearClipPlane=0.05f; eye.clearFlags=CameraClearFlags.SolidColor;
                eye.transform.SetPositionAndRotation(new Vector3(-0.2f,1.65f,0.5f),Quaternion.Euler(7,4,0));
                lightObject=new GameObject("Rhino test daylight"); lightObject.hideFlags=HideFlags.HideAndDontSave;
                var light=lightObject.AddComponent<Light>(); light.type=LightType.Directional; light.intensity=0.8f; light.cullingMask=1<<30;
                light.transform.rotation=Quaternion.Euler(45,25,0);
                target=new RenderTexture(960,720,24); eye.targetTexture=target; eye.Render();
                RenderTexture.active=target; pixels=new Texture2D(960,720,TextureFormat.RGB24,false);
                pixels.ReadPixels(new Rect(0,0,960,720),0,0); pixels.Apply();
                File.WriteAllBytes("TestResults/rhino-import.png",pixels.EncodeToPNG());
                LastResult="PASS: direct runtime 3DM loader; 5 merged meshes (one per color); 6 x 3.1 x 8 meters; unchanged placement and unit scale; shader supported; preview rendered.";
                File.WriteAllText("TestResults/rhino-import.txt",LastResult+Environment.NewLine+loaded.ImportSummary);
                Debug.Log(LastResult); exit=0;
            }
            catch(Exception e)
            {
                LastResult="FAIL: "+e;
                Directory.CreateDirectory("TestResults"); File.WriteAllText("TestResults/rhino-import.txt",LastResult); Debug.LogException(e);
            }
            finally
            {
                RenderTexture.active=previous;
                loaded?.Dispose();
                foreach(var obj in new UnityEngine.Object[]{cameraObject,lightObject,parent,target,pixels}) if(obj) UnityEngine.Object.DestroyImmediate(obj);
                if(Application.isBatchMode) EditorApplication.Exit(exit);
            }
        }
        static void Require(bool value,string message) { if(!value) throw new Exception(message); }

        [MenuItem("Lab Walk/5. Check a 3DM file...")]
        static void CheckFileMenu()
        {
            var path=EditorUtility.OpenFilePanel("Check 3DM import","","3dm");
            if(!string.IsNullOrEmpty(path)) CheckFile(path);
        }

        // Loads any 3DM through the runtime loader in document units and writes a report plus top/eye-level previews.
        // Nothing is added to the open scene. Also callable through the Unity bridge.
        public static async void CheckFile(string path)
        {
            GameObject parent=null, cameraObject=null, lightObject=null;
            LoadedModel loaded=null;
            var report="TestResults/model-check.txt";
            try
            {
                if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before this check.");
                Directory.CreateDirectory("TestResults");
                File.WriteAllText(report,"Running: "+path);
                var bytes=File.ReadAllBytes(path);
                parent=new GameObject("3DM check placement"); parent.hideFlags=HideFlags.HideAndDontSave;
                var watch=System.Diagnostics.Stopwatch.StartNew();
                loaded=await new Rhino3dmModelLoader(true).LoadAsync(bytes,parent.transform,1,CancellationToken.None);
                var seconds=watch.Elapsed.TotalSeconds;
                var renderers=loaded.Root.GetComponentsInChildren<Renderer>();
                foreach(var renderer in renderers) renderer.gameObject.layer=30;
                var b=loaded.BoundsMeters;
                lightObject=new GameObject("3DM check light"); lightObject.hideFlags=HideFlags.HideAndDontSave;
                var light=lightObject.AddComponent<Light>(); light.type=LightType.Directional; light.intensity=0.9f; light.cullingMask=1<<30;
                light.transform.rotation=Quaternion.Euler(55,30,0);
                cameraObject=new GameObject("3DM check camera"); cameraObject.hideFlags=HideFlags.HideAndDontSave;
                var eye=cameraObject.AddComponent<Camera>(); eye.cullingMask=1<<30; eye.clearFlags=CameraClearFlags.SolidColor; eye.backgroundColor=new Color(0.18f,0.2f,0.24f);
                // Top view: orthographic, looking down with Unity +Z (Rhino +Y) toward the top of the image.
                eye.orthographic=true; eye.orthographicSize=Mathf.Max(b.size.z,b.size.x*0.75f)*0.55f;
                eye.nearClipPlane=0.01f; eye.farClipPlane=b.size.y+20;
                eye.transform.SetPositionAndRotation(new Vector3(b.center.x,b.max.y+10,b.center.z),Quaternion.Euler(90,0,0));
                Capture(eye,"TestResults/model-check-top.png");
                // Eye level: standing at the floor center, 1.6 m above the lowest geometry, looking toward +Z.
                eye.orthographic=false; eye.fieldOfView=75; eye.nearClipPlane=0.05f; eye.farClipPlane=200;
                eye.transform.SetPositionAndRotation(new Vector3(b.center.x,b.min.y+1.6f,b.center.z),Quaternion.Euler(8,0,0));
                Capture(eye,"TestResults/model-check-eye.png");
                var text=$"PASS: {Path.GetFileName(path)} ({bytes.Length:N0} bytes) loaded in {seconds:F1} s (Editor, desktop).\n"+
                    $"Renderers: {renderers.Length}; incomplete: {loaded.Incomplete}\n"+
                    $"Bounds min {b.min:F3} max {b.max:F3} size {b.size:F3} meters (Unity: Y up)\n"+loaded.ImportSummary;
                File.WriteAllText(report,text); Debug.Log(text);
            }
            catch(Exception e) { File.WriteAllText(report,"FAIL: "+e); Debug.LogException(e); }
            finally
            {
                loaded?.Dispose();
                foreach(var obj in new UnityEngine.Object[]{cameraObject,lightObject,parent}) if(obj) UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        static void Capture(Camera eye,string file)
        {
            var previous=RenderTexture.active;
            var target=new RenderTexture(1280,960,24); var pixels=new Texture2D(1280,960,TextureFormat.RGB24,false);
            try
            {
                eye.targetTexture=target; eye.Render();
                RenderTexture.active=target; pixels.ReadPixels(new Rect(0,0,1280,960),0,0); pixels.Apply();
                File.WriteAllBytes(file,pixels.EncodeToPNG());
            }
            finally { eye.targetTexture=null; RenderTexture.active=previous; UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels); }
        }
    }
}
