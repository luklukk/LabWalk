using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Meta.XR;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace LabWalk.Editor
{
    public static class ProjectSetup
    {
        public const string ScenePath="Assets/LabWalk/Scenes/LabWalk.unity";

        [MenuItem("Lab Walk/1. Configure project and create scene")]
        public static void Configure()
        {
            if(!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory("Assets/LabWalk/Scenes");
            Directory.CreateDirectory("Assets/LabWalk/Settings");
            AssetDatabase.Refresh();
            PlayerSettings.companyName="Architecture Lab";
            PlayerSettings.productName="Lab Walk";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.architecturelab.labwalk");
            PlayerSettings.bundleVersion="0.2.0";
            PlayerSettings.Android.bundleVersionCode=2; // Must increase for every release installed over a previous one.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Android,ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion=(AndroidSdkVersions)32;
            PlayerSettings.Android.targetSdkVersion=(AndroidSdkVersions)34;
            PlayerSettings.Android.applicationEntry=AndroidApplicationEntry.GameActivity;
            PlayerSettings.defaultInterfaceOrientation=UIOrientation.LandscapeLeft;
            PlayerSettings.colorSpace=ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,new[]{GraphicsDeviceType.Vulkan});
            PlayerSettings.gpuSkinning=true;
            PlayerSettings.SplashScreen.show=false;
            // The app deliberately uses the built-in render pipeline and glTFast's built-in shaders.
            GraphicsSettings.defaultRenderPipeline=null;
            // Android can select a different quality tier than the editor. Configure every tier
            // in this Quest-only project so the platform default cannot restore desktop settings.
            var previousQuality=QualitySettings.GetQualityLevel();
            for(var i=0;i<QualitySettings.names.Length;i++)
            {
                QualitySettings.SetQualityLevel(i,false);
                QualitySettings.vSyncCount=0;
                QualitySettings.antiAliasing=4;
                QualitySettings.shadows=ShadowQuality.Disable;
                QualitySettings.pixelLightCount=1;
                QualitySettings.renderPipeline=null;
            }
            QualitySettings.SetQualityLevel(previousQuality,false);
            var settings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input=settings.FindProperty("activeInputHandler");
            if(input!=null) { input.intValue=1; settings.ApplyModifiedPropertiesWithoutUndo(); }

            ConfigureXR();
            var meta=OVRProjectConfig.CachedProjectConfig;
            if(meta==null) throw new InvalidOperationException("Meta SDK is still initializing. Run Configure again after the editor is ready.");
            meta.targetDeviceTypes=new List<OVRProjectConfig.DeviceType>{OVRProjectConfig.DeviceType.Quest3S};
            meta.anchorSupport=OVRProjectConfig.AnchorSupport.Enabled;
            meta.insightPassthroughSupport=OVRProjectConfig.FeatureSupport.Required;
            meta.systemLoadingScreenBackground=OVRProjectConfig.SystemLoadingScreenBackground.ContextualPassthrough;
            meta.handTrackingSupport=OVRProjectConfig.HandTrackingSupport.ControllersOnly;
            meta.sharedAnchorSupport=OVRProjectConfig.FeatureSupport.None;
            meta.sceneSupport=OVRProjectConfig.FeatureSupport.None;
            meta.boundaryVisibilitySupport=OVRProjectConfig.FeatureSupport.None;
            meta.minHorizonOsSdkVersion=65;
            meta.targetHorizonOsSdkVersion=205;
            OVRProjectConfig.CommitProjectConfig(meta);
            OVRManifestPreprocessor.GenerateOrUpdateAndroidManifest(true);
            IncludeRuntimeShaders();
            if(!File.Exists(ScenePath)) CreateScene();
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            BuildSanitizer.ClearUnusedConnectionSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("Lab Walk configured. Restart Unity once if input backend changed. Open LabWalk scene, preview, then build Android.");
        }

        static void ConfigureXR()
        {
            XRGeneralSettingsPerBuildTarget targets;
            if(!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,out targets))
            {
                targets=ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(targets,"Assets/LabWalk/Settings/XRGeneralSettings.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey,targets,true);
            }
            if(!targets.HasSettingsForBuildTarget(BuildTargetGroup.Android)) targets.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
            if(!targets.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android)) targets.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            var android=targets.SettingsForBuildTarget(BuildTargetGroup.Android);
            android.InitManagerOnStart=true;
            if(!XRPackageMetadataStore.AssignLoader(android.Manager,"UnityEngine.XR.OpenXR.OpenXRLoader",BuildTargetGroup.Android))
                throw new InvalidOperationException("Could not assign the Android OpenXR loader.");
            // This public helper also creates the OpenXR settings asset on a fresh project.
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            var xr=OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if(xr==null) throw new InvalidOperationException("Install Android Build Support, then configure again.");
            xr.renderMode=OpenXRSettings.RenderMode.SinglePassInstanced;
            // Meta XR Feature supplies the Meta OpenXR integration. Avoid enabling a second provider feature.
            foreach(var feature in xr.GetFeatures<OpenXRFeature>())
                feature.enabled=feature is MetaXRFeature || feature is OculusTouchControllerProfile;
            if(!xr.GetFeature<MetaXRFeature>() || !xr.GetFeature<OculusTouchControllerProfile>())
                throw new InvalidOperationException("Meta XR Feature or Oculus Touch profile is missing.");
            EditorUtility.SetDirty(xr); EditorUtility.SetDirty(android); EditorUtility.SetDirty(android.Manager); EditorUtility.SetDirty(targets);
        }

        static void CreateScene()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab");
            if(!prefab) throw new FileNotFoundException("Meta OVRCameraRig prefab not found. Wait for package import.");
            var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name="Quest rig (never scaled or moved)";
            var rig=go.GetComponent<OVRCameraRig>();
            var manager=go.GetComponent<OVRManager>() ?? go.AddComponent<OVRManager>();
            manager.trackingOriginType=OVRManager.TrackingOrigin.Stage;
            manager.isInsightPassthroughEnabled=true;
            var passthrough=go.AddComponent<OVRPassthroughLayer>();
            passthrough.overlayType=OVROverlay.OverlayType.Underlay;
            passthrough.textureOpacity=1;
            var camera=rig.centerEyeAnchor.GetComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.clear;
            camera.allowHDR=false; camera.allowMSAA=true;
            var app=new GameObject("Lab Walk").AddComponent<LabWalkApp>(); app.rig=rig;
            var light=new GameObject("Simple daylight").AddComponent<Light>();
            light.type=LightType.Directional; light.intensity=0.9f; light.shadows=LightShadows.None;
            light.transform.rotation=Quaternion.Euler(45,-30,0);
            RenderSettings.ambientMode=AmbientMode.Flat;
            RenderSettings.ambientLight=new Color(0.65f,0.65f,0.65f);
            EditorSceneManager.SaveScene(scene,ScenePath);
        }

        static void IncludeRuntimeShaders()
        {
            var graphics=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var included=graphics.FindProperty("m_AlwaysIncludedShaders");
            // The built-in font supplies its internal text shader. Listing that DontSave
            // resource explicitly makes Unity fail while serializing unity_builtin_extra.
            for(var i=included.arraySize-1;i>=0;i--)
            {
                var entry=included.GetArrayElementAtIndex(i);
                if(entry.objectReferenceValue is Shader existing && existing.name=="GUI/Text Shader")
                { entry.objectReferenceValue=null; included.DeleteArrayElementAtIndex(i); }
            }
            foreach(var name in new[]{"glTF/PbrMetallicRoughness","glTF/PbrSpecularGlossiness","glTF/Unlit","Unlit/Color","Sprites/Default"})
            {
                var shader=Shader.Find(name);
                if(!shader) throw new InvalidOperationException("Required runtime shader missing: "+name);
                var found=false;
                for(var i=0;i<included.arraySize;i++) if(included.GetArrayElementAtIndex(i).objectReferenceValue==shader) found=true;
                if(!found) { var i=included.arraySize; included.InsertArrayElementAtIndex(i); included.GetArrayElementAtIndex(i).objectReferenceValue=shader; }
            }
            graphics.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
