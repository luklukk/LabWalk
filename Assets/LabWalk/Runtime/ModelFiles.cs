using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace LabWalk
{
    public static class ModelFiles
    {
        public static string Folder
        {
            get
            {
                var external=Path.Combine(Application.persistentDataPath,"Models");
                return File.Exists(Path.Combine(external,"model.json")) ? external : Application.streamingAssetsPath+"/Models";
            }
        }
        public static async Task<byte[]> ReadAsync(string folder, string filename, CancellationToken token)
        {
            var path=folder.TrimEnd('/','\\')+"/"+filename;
            if (!path.Contains("://"))
            {
                var info=new FileInfo(path);
                if(!info.Exists) throw new FileNotFoundException("Missing model file",path);
                if(info.Length>150*1024*1024) throw new IOException("First milestone supports files up to 150 MB. Simplify the export.");
                return await File.ReadAllBytesAsync(path,token);
            }
            // Android StreamingAssets live in the APK and require UnityWebRequest (jar:file://...).
            using(var request=UnityWebRequest.Get(path))
            {
                request.timeout=60;
                var operation=request.SendWebRequest();
                while(!operation.isDone)
                {
                    if(token.IsCancellationRequested) { request.Abort(); token.ThrowIfCancellationRequested(); }
                    if(request.downloadedBytes>150UL*1024*1024) { request.Abort(); throw new IOException("Model exceeds 150 MB limit."); }
                    await Task.Yield();
                }
                if(request.result!=UnityWebRequest.Result.Success) throw new IOException(filename+": "+request.error);
                return request.downloadHandler.data;
            }
        }
        public static string Fingerprint(byte[] glb, byte[] manifest)
        {
            using(var sha=SHA256.Create())
            {
                sha.TransformBlock(glb,0,glb.Length,null,0);
                sha.TransformFinalBlock(manifest,0,manifest.Length);
                return BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();
            }
        }
    }
}
