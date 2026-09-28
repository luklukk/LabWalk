using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LabWalk.Editor
{
    // Controller models: Meta's OVRControllerPrefab under each controller anchor. OVRControllerHelper
    // picks the model for the connected controller type (Touch Plus on Quest 3/3S) and animates its buttons.
    public static class ControllerSetup
    {
        const string PrefabPath="Packages/com.meta.xr.sdk.core/Prefabs/OVRControllerPrefab.prefab";
        const string RuntimePrefabPath="Packages/com.meta.xr.sdk.core/Prefabs/OVRRuntimeControllerPrefab.prefab";

        // Adds one controller prefab under each controller anchor of the scene's OVRCameraRig (idempotent) and saves the scene.
        [MenuItem("Lab Walk/7. Add controller models")]
        public static void AddControllerModels()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if(!prefab) throw new FileNotFoundException("Meta XR Core controller prefab not found",PrefabPath);
            var rig=Object.FindFirstObjectByType<OVRCameraRig>();
            if(!rig) throw new System.InvalidOperationException("Open the Lab Walk scene first (no OVRCameraRig found).");
            foreach(var (anchor,controller) in new[]{(rig.leftControllerAnchor,OVRInput.Controller.LTouch),(rig.rightControllerAnchor,OVRInput.Controller.RTouch)})
            {
                if(anchor.GetComponentInChildren<OVRControllerHelper>(true)) continue;
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,anchor);
                instance.name=controller==OVRInput.Controller.LTouch ? "Left controller model" : "Right controller model";
                instance.transform.localPosition=Vector3.zero; instance.transform.localRotation=Quaternion.identity; instance.transform.localScale=Vector3.one;
                var helper=instance.GetComponent<OVRControllerHelper>();
                helper.m_controller=controller;
                EditorUtility.SetDirty(helper);
            }
            // The system's own controller model (XR_FB_render_model), which matches the physical controller as
            // shown in Horizon OS. Lab Walk hides the bundled model above once this one has loaded.
            var runtimePrefab=AssetDatabase.LoadAssetAtPath<GameObject>(RuntimePrefabPath);
            var shader=Shader.Find("Meta/Lit") ?? Shader.Find("Unlit/Texture");
            if(runtimePrefab)
                foreach(var (anchor,controller) in new[]{(rig.leftControllerAnchor,OVRInput.Controller.LTouch),(rig.rightControllerAnchor,OVRInput.Controller.RTouch)})
                {
                    var existing=anchor.GetComponentInChildren<OVRRuntimeController>(true);
                    if(!existing)
                    {
                        var instance=(GameObject)PrefabUtility.InstantiatePrefab(runtimePrefab,anchor);
                        instance.name=controller==OVRInput.Controller.LTouch ? "Left system controller model" : "Right system controller model";
                        instance.transform.localPosition=Vector3.zero; instance.transform.localRotation=Quaternion.identity; instance.transform.localScale=Vector3.one;
                        existing=instance.GetComponent<OVRRuntimeController>();
                    }
                    existing.m_controller=controller;
                    existing.m_controllerModelShader=shader; // referenced here so the shader is included in builds
                    EditorUtility.SetDirty(existing);
                }
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            Debug.Log("Controller models added under "+rig.name);
        }

        // Writes the rendered size of each controller model in the prefab (meters, relative to the prefab root).
        public static void MeasureModels()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var instance=(GameObject)Object.Instantiate(prefab);
            var text=new StringBuilder();
            try
            {
                foreach(Transform model in instance.transform)
                {
                    model.gameObject.SetActive(true);
                    var renderers=model.GetComponentsInChildren<Renderer>(true);
                    if(renderers.Length==0) continue;
                    var b=renderers[0].bounds; foreach(var r in renderers) b.Encapsulate(r.bounds);
                    text.AppendLine($"{model.name}: size {b.size.x*100:F1} x {b.size.y*100:F1} x {b.size.z*100:F1} cm, center {b.center*100:F1} cm, lossyScale {model.lossyScale}");
                }
            }
            finally { Object.DestroyImmediate(instance); }
            Directory.CreateDirectory("TestResults");
            File.WriteAllText("TestResults/controller-sizes.txt",text.ToString());
        }

        // Writes every node of the controller prefab with its position relative to the prefab root.
        public static void DumpHierarchy()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var text=new StringBuilder();
            if(!prefab) text.AppendLine("Missing "+PrefabPath);
            else
            {
                var instance=(GameObject)Object.Instantiate(prefab);
                try
                {
                    foreach(var t in instance.GetComponentsInChildren<Transform>(true))
                    {
                        var depth=0; for(var p=t; p!=instance.transform; p=p.parent) depth++;
                        var local=instance.transform.InverseTransformPoint(t.position);
                        text.AppendLine($"{new string(' ',depth*2)}{t.name}  pos {local.x:F4},{local.y:F4},{local.z:F4}  active {t.gameObject.activeSelf}  [{string.Join(",",System.Array.ConvertAll(t.GetComponents<Component>(),c=>c ? c.GetType().Name : "missing"))}]");
                    }
                }
                finally { Object.DestroyImmediate(instance); }
            }
            Directory.CreateDirectory("TestResults");
            File.WriteAllText("TestResults/controller-hierarchy.txt",text.ToString());
        }
    }
}
