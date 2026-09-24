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
        // Development build: profiler, debugging and Meta XR Operator (agent control, screen capture) included.
        [MenuItem("Lab Walk/2. Build Quest APK")]
        public static void Build() { BuildApk(BuildOptions.Development,"Builds/LabWalk-Quest3S.apk"); }

        // Release build for distribution (GitHub releases, Device Manager). Meta's own build processor
        // excludes Meta XR Operator and its media-projection service from non-development builds.
        [MenuItem("Lab Walk/2b. Build Quest release APK")]
        public static void BuildRelease() { BuildApk(BuildOptions.None,"Builds/LabWalk-Quest3S-release.apk"); }

        static void BuildApk(BuildOptions options,string output)
        {
            if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.Android)
                throw new BuildFailedException("Switch the active build platform to Android first. For command line use -buildTarget Android.");
            if(!File.Exists(ProjectSetup.ScenePath)) throw new BuildFailedException("Run Lab Walk > Configure project and create scene first.");
            var xr=OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if(xr==null || xr.GetFeature<MetaXRFeature>()==null || !xr.GetFeature<MetaXRFeature>().enabled)
                throw new BuildFailedException("Meta XR Feature is not enabled. Run Configure.");
            Directory.CreateDirectory("Builds");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=new[]{ProjectSetup.ScenePath}, locationPathName=output,
                target=BuildTarget.Android, options=options
            });
            if(report.summary.result!=BuildResult.Succeeded) throw new BuildFailedException("Quest build failed. Check the editor log.");
            Debug.Log("Built "+Path.GetFullPath(output));
        }
    }
}
