using System;
using System.IO;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace LabWalk.Editor
{
    // Exercises the in-app import path without XR: load + generated configuration, activation,
    // reload as after an app restart, fingerprint stability, rejection of a bad file and return to the
    // bundled sample. The editor's own imported selection (persistentDataPath/Models) is restored afterwards.
    public static class ImportPipelineTest
    {
        public static string LastResult="Not run";
        const string Report="TestResults/import-pipeline.txt";

        [MenuItem("Lab Walk/6. Test in-app import pipeline...")]
        static void Menu()
        {
            var path=EditorUtility.OpenFilePanel("Model to import","","3dm,glb");
            if(!string.IsNullOrEmpty(path)) Run(path);
        }

        public static async void Run(string path)
        {
            var log=new StringBuilder();
            var active=ModelImport.ActiveFolder; var backup=active+".testbackup";
            ModelCandidate imported=null, reloaded=null, again=null, bundled=null;
            try
            {
                if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
                Directory.CreateDirectory("TestResults"); File.WriteAllText(Report,"Running: "+path);
                if(Directory.Exists(backup)) throw new InvalidOperationException("Leftover "+backup+" from an earlier run; restore it manually.");
                if(Directory.Exists(active)) Directory.Move(active,backup);
                Require(ModelFiles.Folder==ModelImport.BundledFolder,"Bundled sample is selected with no imported model.");

                var watch=System.Diagnostics.Stopwatch.StartNew();
                imported=await ModelImport.LoadFileAsync(path,CancellationToken.None);
                var m=imported.Manifest; var size=imported.Model.BoundsMeters.size;
                log.AppendLine($"Loaded {Path.GetFileName(path)} in {watch.Elapsed.TotalSeconds:F1} s; staged hidden: {!imported.Staging.gameObject.activeSelf}");
                log.AppendLine($"Units {imported.Model.SourceUnits}; size {size.x:F3} x {size.y:F3} x {size.z:F3} m; incomplete {imported.Model.Incomplete}");
                log.AppendLine(imported.Model.ImportSummary);
                log.AppendLine(imported.Notes);
                log.AppendLine($"Reference A {m.referenceA:F3}  B {m.referenceB:F3}  distance {m.knownDistanceMeters:F3} m; file landmarks {imported.FileLandmarks}");
                log.AppendLine("Generated model.json:\n"+Encoding.UTF8.GetString(imported.ManifestBytes));
                Require(!imported.Staging.gameObject.activeSelf,"Candidate must stay hidden until adopted.");
                Require(ModelFiles.Folder==ModelImport.BundledFolder,"Loading alone must not change the active selection.");

                await ModelImport.ActivateAsync(imported.SourcePath,m,imported.ManifestBytes,CancellationToken.None);
                Require(ModelFiles.Folder==active && File.Exists(Path.Combine(active,m.file)),"Activated model is selected.");

                reloaded=await ModelImport.LoadFolderAsync(ModelFiles.Folder,CancellationToken.None);
                Require(reloaded.Fingerprint==imported.Fingerprint,"Fingerprint must survive a restart so the saved anchor restores.");
                Require(Vector3.Distance(reloaded.Model.BoundsMeters.size,size)<0.001f,"Reloaded size matches.");
                again=await ModelImport.LoadFileAsync(path,CancellationToken.None);
                Require(again.Fingerprint==imported.Fingerprint,"Re-importing the same file keeps its placement.");
                log.AppendLine("Activated, reloaded from app storage, fingerprint stable across restart and re-import.");

                var bogus=Path.Combine(Path.GetTempPath(),"labwalk-bogus.3dm");
                File.WriteAllBytes(bogus,new byte[256]);
                bool rejected=false;
                try { (await ModelImport.LoadFileAsync(bogus,CancellationToken.None)).Dispose(); } catch(InvalidDataException) { rejected=true; }
                File.Delete(bogus);
                Require(rejected && ModelFiles.Folder==active,"Unreadable file rejected; active selection unchanged.");
                log.AppendLine("Unreadable file rejected without changing the selection.");

                await ModelImport.ActivateBundledAsync();
                Require(ModelFiles.Folder==ModelImport.BundledFolder && !Directory.Exists(active),"Returned to bundled sample.");
                bundled=await ModelImport.LoadFolderAsync(ModelFiles.Folder,CancellationToken.None);
                Require(bundled.Bundled,"Bundled sample loads.");
                log.AppendLine("Returned to the bundled sample.");

                LastResult="PASS: in-app import pipeline";
                File.WriteAllText(Report,LastResult+"\n"+log); Debug.Log(LastResult+"\n"+log);
            }
            catch(Exception e) { LastResult="FAIL: "+e.Message; File.WriteAllText(Report,"FAIL: "+e+"\n"+log); Debug.LogException(e); }
            finally
            {
                imported?.Dispose(); reloaded?.Dispose(); again?.Dispose(); bundled?.Dispose();
                try
                {
                    if(Directory.Exists(active)) Directory.Delete(active,true);
                    if(Directory.Exists(backup)) Directory.Move(backup,active);
                }
                catch(Exception e) { Debug.LogError("Could not restore the editor's model selection: "+e.Message); }
            }
        }

        static void Require(bool value,string message) { if(!value) throw new Exception(message); }
    }
}
