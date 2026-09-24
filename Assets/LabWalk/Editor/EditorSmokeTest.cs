using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LabWalk.Editor
{
    // Runs the actual runtime GLB loader and scene in Play mode; never substitutes for headset tests.
    [InitializeOnLoad]
    public static class EditorSmokeTest
    {
        const string Pending="LabWalk.SmokeTest.Pending";
        const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
        static double deadline;
        static int frames;
        static float readyTime;
        static bool checkedScene;
        static EditorSmokeTest()
        {
            EditorApplication.playModeStateChanged+=OnPlayMode;
            if(SessionState.GetBool(Pending,false) && EditorApplication.isPlaying)
            { deadline=EditorApplication.timeSinceStartup+90; EditorApplication.update+=Tick; }
        }
        [MenuItem("Lab Walk/3. Run desktop smoke test")]
        public static void Run()
        {
            if(!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ProjectSetup.ScenePath);
            SessionState.SetBool(Pending,true);
            EditorApplication.EnterPlaymode();
        }
        static void OnPlayMode(PlayModeStateChange state)
        {
            if(!SessionState.GetBool(Pending,false)) return;
            if(state==PlayModeStateChange.EnteredPlayMode)
            {
                deadline=EditorApplication.timeSinceStartup+90;
                frames=0; checkedScene=false;
                EditorApplication.update-=Tick;
                EditorApplication.update+=Tick;
            }
        }
        static T Field<T>(LabWalkApp app,string name) { return (T)typeof(LabWalkApp).GetField(name,Fields).GetValue(app); }
        static void Call(LabWalkApp app,string name,params object[] values)
        { typeof(LabWalkApp).GetMethod(name,Fields).Invoke(app,values); }
        static void Require(bool valid,string message) { if(!valid) throw new Exception(message); }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline) throw new TimeoutException("Runtime scene did not finish loading.");
                if(!EditorApplication.isPlaying) return;
                EditorApplication.QueuePlayerLoopUpdate();
                var app=UnityEngine.Object.FindFirstObjectByType<LabWalkApp>();
                if(!app) return;
                var phase=typeof(LabWalkApp).GetField("phase",Fields).GetValue(app).ToString();
                if(phase=="Error") throw new Exception("Runtime startup failed: "+Field<string>(app,"message"));
                var model=Field<LoadedModel>(app,"model");
                if(model==null) return;
                Require(Field<bool>(app,"editorPreview"),"This smoke test requires desktop preview without an XR headset.");
                if(!checkedScene)
                {
                    Require(Vector3.Distance(model.BoundsMeters.size,new Vector3(6,3.1f,8))<0.002f,"Unexpected sample model dimensions: "+model.BoundsMeters.size);
                    Require(model.Root.GetComponentsInChildren<Renderer>(true).Length==15,"Expected 15 sample mesh instances.");
                    Call(app,"RecordReference",new Vector3(1,0,2));
                    Call(app,"RecordReference",new Vector3(3,0,2));
                    var placement=Field<Transform>(app,"placement");
                    Require(Vector3.Distance(placement.TransformPoint(Vector3.zero),new Vector3(1,0,2))<0.001f,"Reference A mismatch.");
                    Require(Vector3.Distance(placement.TransformPoint(new Vector3(0,0,2)),new Vector3(3,0,2))<0.001f,"Reference B mismatch.");
                    Require(placement.localScale==Vector3.one,"Placement must remain at scale one.");
                    var pose=new Pose(placement.position,placement.rotation);
                    var view=Field<WalkthroughView>(app,"view");
                    view.SetImmersive(true); view.SetImmersive(false);
                    Require(placement.position==pose.position && placement.rotation==pose.rotation,"Mode switching changed placement.");
                    Call(app,"BeginAlignment");
                    Call(app,"RecordReference",Vector3.zero);
                    Call(app,"RecordReference",new Vector3(0,0,2));
                    typeof(LabWalkApp).GetField("panelVisible",Fields).SetValue(app,false);
                    view.SetImmersive(true);
                    var camera=Field<Camera>(app,"eye");
                    camera.transform.SetPositionAndRotation(new Vector3(-0.2f,1.65f,0.5f),Quaternion.Euler(7,4,0));
                    checkedScene=true;
                    frames=Time.frameCount;
                    readyTime=Time.realtimeSinceStartup;
                }
                if(Time.frameCount-frames<10 || Time.realtimeSinceStartup-readyTime<0.5f) return;
                Require(model.Root.activeInHierarchy,"Aligned model is not visible after runtime Update.");
                var eye=Field<Camera>(app,"eye");
                Directory.CreateDirectory("TestResults");
                var texture=new RenderTexture(1280,960,24);
                eye.targetTexture=texture;
                eye.Render();
                var previous=RenderTexture.active; RenderTexture.active=texture;
                var image=new Texture2D(1280,960,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,1280,960),0,0); image.Apply();
                File.WriteAllBytes("TestResults/sample-preview.png",image.EncodeToPNG());
                eye.targetTexture=null; RenderTexture.active=previous;
                UnityEngine.Object.DestroyImmediate(image); texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
                File.WriteAllText("TestResults/editor-smoke.txt","PASS: actual runtime "+Field<ModelManifest>(app,"manifest").file+" load; 15 renderers; dimensions 6 x 3.1 x 8 m; two-point placement; fixed scale; invariant placement across view modes.\nDesktop preview only; no headset/anchor claim.\n");
                Finish(0);
            }
            catch(Exception e)
            {
                Directory.CreateDirectory("TestResults");
                File.WriteAllText("TestResults/editor-smoke.txt","FAIL: "+e);
                Debug.LogException(e); Finish(1);
            }
        }
        static void Finish(int code)
        {
            SessionState.SetBool(Pending,false);
            EditorApplication.update-=Tick;
            if(Application.isBatchMode) EditorApplication.Exit(code);
            else EditorApplication.ExitPlaymode();
        }
    }
}
