using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LabWalk.Editor
{
    // Core 205 injects editor connection credentials even when its DevAgent is disabled.
    // This offline viewer does not use that feature. Run after the SDK's order-1 hook.
    public sealed class BuildSanitizer : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 10000;
        public void OnPreprocessBuild(BuildReport report) { ClearUnusedConnectionSettings(); }
        public void OnPostprocessBuild(BuildReport report) { ClearUnusedConnectionSettings(); }

        public static void ClearUnusedConnectionSettings()
        {
            var asset=AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Resources/DevAgentSettings.asset");
            if(!asset) return;
            var settings=new SerializedObject(asset);
            settings.FindProperty("enabled").boolValue=false;
            foreach(var field in new[]{"serverAddress","accessToken","witClientAccessToken"})
                settings.FindProperty(field).stringValue=string.Empty;
            settings.FindProperty("witConfiguration").objectReferenceValue=null;
            settings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(asset);
            Debug.Log("Lab Walk: unused DevAgent disabled and connection settings cleared.");
        }
    }
}
