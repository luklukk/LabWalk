using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using Meta.XR;

namespace LabWalk.Editor
{
    public static class QuestBuild
    {
        [MenuItem("Lab Walk/2. Build Quest APK")]
        public static void Build()
        {
            if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.Android)
                throw new BuildFailedException("Switch the active build platform to Android first. For command line use -buildTarget Android.");
            if(!File.Exists(ProjectSetup.ScenePath)) throw new BuildFailedException("Run Lab Walk > Configure project and create scene first.");
            var xr=OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if(xr==null || xr.GetFeature<MetaXRFeature>()==null || !xr.GetFeature<MetaXRFeature>().enabled)
                throw new BuildFailedException("Meta XR Feature is not enabled. Run Configure.");
            Directory.CreateDirectory("Builds");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=new[]{ProjectSetup.ScenePath}, locationPathName="Builds/LabWalk-Quest3S.apk",
                target=BuildTarget.Android, options=BuildOptions.Development
            });
            if(report.summary.result!=BuildResult.Succeeded) throw new BuildFailedException("Quest build failed. Check the editor log.");
            Debug.Log("Built "+Path.GetFullPath("Builds/LabWalk-Quest3S.apk"));
        }
    }
}
