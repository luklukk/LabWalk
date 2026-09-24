using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace LabWalk
{
    // A fully loaded model that is not yet the active one. It lives under an inactive staging
    // object so nothing half-loaded is ever visible, and is adopted only after its selection is saved.
    public sealed class ModelCandidate
    {
        public string SourcePath, Notes;
        public bool Bundled, FileLandmarks;
        public ModelManifest Manifest;
        public byte[] ManifestBytes;
        public string Fingerprint;
        public LoadedModel Model;
        public Transform Staging;
        public void Dispose()
        {
            Model?.Dispose(); Model=null;
            if(!Staging) return;
            if(Application.isPlaying) UnityEngine.Object.Destroy(Staging.gameObject); else UnityEngine.Object.DestroyImmediate(Staging.gameObject);
        }
    }

    // In-app import: finding files, the Android system picker, generated configuration and the
    // switch of the active model folder. The active model is always a complete Models folder
    // (file + model.json) under persistentDataPath, or the bundled sample when there is none.
    public static class ModelImport
    {
        public const long MaxBytes=150L*1024*1024;
        // Users copy .3dm/.glb files here (USB file transfer, MQDH or adb); picked files are copied here too.
        public static string ImportFolder => Path.Combine(Application.persistentDataPath,"Import");
        public static string ActiveFolder => Path.Combine(Application.persistentDataPath,"Models");
        public static string BundledFolder => Application.streamingAssetsPath+"/Models";
        public static string DeviceImportPath => "Android/data/"+Application.identifier+"/files/Import";
        static string IncomingFolder => Path.Combine(Application.persistentDataPath,"Models.incoming");
        static string PreviousFolder => Path.Combine(Application.persistentDataPath,"Models.previous");
        static string PickerResultPath => Path.Combine(ImportFolder,".picker-result.json");

        public static bool SystemPickerAvailable => Application.platform==RuntimePlatform.Android;

        public static void EnsureFolders()
        {
            Directory.CreateDirectory(ImportFolder);
            var readme=Path.Combine(ImportFolder,"README.txt");
            if(!File.Exists(readme))
                File.WriteAllText(readme,"Copy .3dm or .glb models into this folder, then open them in Lab Walk (left grip > Models).\n"+
                    "Rhino: add point objects named \"LabWalk reference A\" and \"LabWalk reference B\" on the floor at two marked room landmarks.\n");
        }

        // A switch interrupted between its two folder moves leaves Models missing and Models.previous intact.
        public static void RecoverInterruptedSwitch()
        {
            if(!Directory.Exists(ActiveFolder) && File.Exists(Path.Combine(PreviousFolder,"model.json")))
                Directory.Move(PreviousFolder,ActiveFolder);
            if(Directory.Exists(IncomingFolder)) Directory.Delete(IncomingFolder,true);
        }

        public static List<FileInfo> ListImportFiles()
        {
            try
            {
                return new DirectoryInfo(ImportFolder).GetFiles()
                    .Where(f=>f.Extension.Equals(".3dm",StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".glb",StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f=>f.LastWriteTimeUtc).Take(20).ToList();
            }
            catch(Exception e) { Debug.LogWarning("Import folder unreadable: "+e.Message); return new List<FileInfo>(); }
        }

        public static void OpenSystemPicker()
        {
            File.Delete(PickerResultPath);
            if(!SystemPickerAvailable) throw new InvalidOperationException("The system file picker is only available on the headset.");
            using(var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using(var activity=player.GetStatic<AndroidJavaObject>("currentActivity"))
            using(var picker=new AndroidJavaClass("com.architecturelab.labwalk.LabWalkFilePicker"))
                picker.CallStatic("open",activity,ImportFolder,PickerResultPath);
        }

        [Serializable] public sealed class PickerResult { public string status, file, error; }

        // Null while the picker is open or the copy is still running.
        public static PickerResult PollPicker()
        {
            if(!File.Exists(PickerResultPath)) return null;
            var result=JsonUtility.FromJson<PickerResult>(File.ReadAllText(PickerResultPath));
            File.Delete(PickerResultPath);
            return result;
        }

        static Transform NewStaging()
        {
            var staging=new GameObject("Model candidate (hidden)");
            staging.SetActive(false);
            return staging.transform;
        }

        // Loads a Models folder (model.json + file), as used at startup and for the bundled sample.
        public static async Task<ModelCandidate> LoadFolderAsync(string folder,CancellationToken token)
        {
            var result=new ModelCandidate {Bundled=folder==BundledFolder,Staging=NewStaging()};
            try
            {
                result.ManifestBytes=await ModelFiles.ReadAsync(folder,"model.json",token);
                result.Manifest=JsonUtility.FromJson<ModelManifest>(Encoding.UTF8.GetString(result.ManifestBytes).TrimStart('﻿'));
                if(result.Manifest==null) throw new InvalidOperationException("Missing model manifest.");
                var scale=result.Manifest.ValidateAndGetScale();
                var bytes=await ModelFiles.ReadAsync(folder,result.Manifest.file,token);
                result.Fingerprint=ModelFiles.Fingerprint(bytes,result.ManifestBytes);
                result.Model=await ModelLoaderFactory.Create(result.Manifest).LoadAsync(bytes,result.Staging,scale,token);
                result.Notes=$"Landmarks: from configuration, {Vector3.Distance(result.Manifest.referenceA,result.Manifest.referenceB):F3} m apart";
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        // Loads any .3dm/.glb file and generates its configuration; nothing is saved until the user confirms.
        public static async Task<ModelCandidate> LoadFileAsync(string path,CancellationToken token)
        {
            var result=new ModelCandidate {SourcePath=path,Staging=NewStaging()};
            try
            {
                result.Manifest=ProvisionalManifest(path);
                var scale=result.Manifest.ValidateAndGetScale();
                var bytes=await ReadFileAsync(path,token);
                result.Model=await ModelLoaderFactory.Create(result.Manifest).LoadAsync(bytes,result.Staging,scale,token);
                result.Notes=CompleteManifest(result.Manifest,result.Model,result.Staging,out result.FileLandmarks);
                result.ManifestBytes=ManifestBytes(result.Manifest);
                result.Fingerprint=ModelFiles.Fingerprint(bytes,result.ManifestBytes);
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        public static async Task<byte[]> ReadFileAsync(string path,CancellationToken token)
        {
            var info=new FileInfo(path);
            if(!info.Exists) throw new FileNotFoundException("File no longer exists.",path);
            if(info.Length>MaxBytes) throw new IOException("File is larger than 150 MB. Simplify or split the model.");
            return await File.ReadAllBytesAsync(path,token);
        }

        // Loader settings come from the file type; references and names are filled in after the model loads.
        public static ModelManifest ProvisionalManifest(string sourcePath)
        {
            var file=SafeFileName(Path.GetFileName(sourcePath));
            var rhino=file.EndsWith(".3dm",StringComparison.OrdinalIgnoreCase);
            if(!rhino && !file.EndsWith(".glb",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Choose a .3dm or .glb file.");
            var name=Path.GetFileNameWithoutExtension(file);
            return new ModelManifest {
                modelId=Regex.Replace(name.ToLowerInvariant(),"[^a-z0-9]+","-").Trim('-'),
                displayName=name, file=file,
                sourceUnits=rhino ? "fromDocument" : "meters",
                coordinateUnits=rhino ? "rhinoDocument" : "gltfMeters",
            };
        }

        // Uses landmarks defined in the file; otherwise the two floor corners along the model's X edge
        // so the model can be viewed, with a clear note that real landmarks are still needed.
        public static string CompleteManifest(ModelManifest manifest,LoadedModel model,Transform parent,out bool fileLandmarks)
        {
            if(string.IsNullOrEmpty(manifest.modelId)) manifest.modelId="model";
            Vector3? a=model.ReferenceA, b=model.ReferenceB;
            if(a==null || b==null)
            {
                // GLB: nodes named like the Rhino points.
                foreach(var t in model.Root.GetComponentsInChildren<Transform>(true))
                {
                    if(t.name==RhinoModelData.ReferenceNameA) a=parent.InverseTransformPoint(t.position);
                    if(t.name==RhinoModelData.ReferenceNameB) b=parent.InverseTransformPoint(t.position);
                }
            }
            fileLandmarks=a!=null && b!=null;
            var bounds=model.BoundsMeters;
            manifest.referenceA=fileLandmarks ? a.Value : new Vector3(bounds.min.x,bounds.min.y,bounds.min.z);
            manifest.referenceB=fileLandmarks ? b.Value : new Vector3(bounds.max.x,bounds.min.y,bounds.min.z);
            manifest.knownDistanceMeters=Vector3.Distance(manifest.referenceA,manifest.referenceB);
            manifest.ValidateAndGetScale();
            return fileLandmarks ? $"Landmarks: from file, {manifest.knownDistanceMeters:F3} m apart" :
                $"Landmarks: NONE IN FILE - using model bounding-box floor corners ({manifest.knownDistanceMeters:F2} m apart). Add Rhino points \"{RhinoModelData.ReferenceNameA}\"/\"B\" for real alignment.";
        }

        public static byte[] ManifestBytes(ModelManifest manifest) => Encoding.UTF8.GetBytes(JsonUtility.ToJson(manifest,true));

        // Copies the model and its generated manifest into a fresh folder, then swaps it in.
        // The previous Models folder is removed only after the new one is in place.
        // Unity paths are resolved here on the main thread; the file work runs on a worker thread.
        public static async Task ActivateAsync(string sourcePath,ModelManifest manifest,byte[] manifestBytes,CancellationToken token)
        {
            string incoming=IncomingFolder, active=ActiveFolder, previous=PreviousFolder, file=manifest.file;
            await Task.Run(()=>
            {
                if(Directory.Exists(incoming)) Directory.Delete(incoming,true);
                Directory.CreateDirectory(incoming);
                File.Copy(sourcePath,Path.Combine(incoming,file));
                token.ThrowIfCancellationRequested();
                File.WriteAllBytes(Path.Combine(incoming,"model.json"),manifestBytes); // written last: marks the folder complete
                Swap(incoming,active,previous);
            },token);
        }

        // Returns to the bundled sample by removing the imported selection (imported sources stay in Import).
        public static async Task ActivateBundledAsync()
        {
            string active=ActiveFolder, previous=PreviousFolder;
            await Task.Run(()=>Swap(null,active,previous));
        }

        static void Swap(string incoming,string active,string previous)
        {
            if(Directory.Exists(previous)) Directory.Delete(previous,true);
            if(Directory.Exists(active)) Directory.Move(active,previous);
            if(incoming!=null) Directory.Move(incoming,active);
            if(Directory.Exists(previous)) Directory.Delete(previous,true);
        }

        static string SafeFileName(string name)
        {
            foreach(var c in Path.GetInvalidFileNameChars().Concat(new[]{'/','\\',':'})) name=name.Replace(c,'_');
            return string.IsNullOrWhiteSpace(name) ? "model" : name;
        }
    }
}
