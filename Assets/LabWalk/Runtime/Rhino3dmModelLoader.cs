using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace LabWalk
{
    public sealed class Rhino3dmModelLoader : IModelLoader
    {
        readonly bool documentUnits;
        public Rhino3dmModelLoader(bool documentUnits=true) { this.documentUnits=documentUnits; }

        public async Task<LoadedModel> LoadAsync(byte[] data,Transform parent,float scale,CancellationToken token)
        {
            RhinoModelData decoded;
            try { decoded=await Task.Run(()=>RhinoModelReader.Read(data,documentUnits ? (double?)null : scale,token),token); }
            catch(DllNotFoundException e) { throw new InvalidOperationException("Rhino native importer is unavailable on this platform. Use Windows x64 Editor or the ARM64 Quest build.",e); }
            token.ThrowIfCancellationRequested();
            var root=new GameObject("Imported Rhino model (meters)");
            root.transform.SetParent(parent,false);
            var resources=new Resources();
            try
            {
                var shader=ResourcesShader();
                if(!shader) throw new InvalidDataException("Rhino material shader was stripped from this build.");
                var materials=new Dictionary<Color,Material>();
                var bounds=new Bounds(); bool first=true;
                foreach(var part in decoded.Meshes)
                {
                    token.ThrowIfCancellationRequested();
                    var mesh=new Mesh {name=part.Name,indexFormat=IndexFormat.UInt32};
                    resources.Items.Add(mesh);
                    mesh.vertices=Vectors(part.Positions);
                    mesh.triangles=part.Triangles;
                    if(part.Normals!=null) mesh.normals=Vectors(part.Normals); else mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                    if(first) { bounds=mesh.bounds; first=false; } else bounds.Encapsulate(mesh.bounds);
                    var color=new Color(part.R,part.G,part.B,1);
                    if(!materials.TryGetValue(color,out var material))
                    {
                        material=new Material(shader) {name="Rhino basic color",color=color};
                        materials.Add(color,material); resources.Items.Add(material);
                    }
                    var child=new GameObject(part.Name);
                    child.transform.SetParent(root.transform,false);
                    child.AddComponent<MeshFilter>().sharedMesh=mesh;
                    child.AddComponent<MeshRenderer>().sharedMaterial=material;
                    mesh.UploadMeshData(true);
                    await Task.Yield();
                }
                token.ThrowIfCancellationRequested();
                foreach(var warning in decoded.Warnings) Debug.LogWarning("3DM import: "+warning);
                Debug.Log(decoded.Summary+" Hidden objects: "+decoded.Hidden);
                var loaded=new LoadedModel(root,bounds,resources,decoded.Summary,decoded.Skipped>0) {SourceUnits=decoded.Units};
                if(decoded.ReferenceA!=null && decoded.ReferenceB!=null)
                {
                    loaded.ReferenceA=new Vector3(decoded.ReferenceA[0],decoded.ReferenceA[1],decoded.ReferenceA[2]);
                    loaded.ReferenceB=new Vector3(decoded.ReferenceB[0],decoded.ReferenceB[1],decoded.ReferenceB[2]);
                }
                foreach(var marker in decoded.Markers) loaded.Markers[marker.Key]=new Vector3(marker.Value[0],marker.Value[1],marker.Value[2]);
                return loaded;
            }
            catch { UnityEngine.Object.Destroy(root); resources.Dispose(); throw; }
        }

        static Vector3[] Vectors(float[] values)
        {
            var result=new Vector3[values.Length/3];
            for(int i=0;i<result.Length;i++) result[i]=new Vector3(values[i*3],values[i*3+1],values[i*3+2]);
            return result;
        }

        static Shader ResourcesShader() { return UnityEngine.Resources.Load<Shader>("RhinoBasic"); }

        sealed class Resources : IDisposable
        {
            public readonly List<UnityEngine.Object> Items=new List<UnityEngine.Object>();
            public void Dispose() { foreach(var item in Items) if(item) UnityEngine.Object.Destroy(item); Items.Clear(); }
        }
    }

    public static class ModelLoaderFactory
    {
        public static IModelLoader Create(ModelManifest manifest)
        {
            if(Path.GetExtension(manifest.file).Equals(".3dm",StringComparison.OrdinalIgnoreCase))
                return new Rhino3dmModelLoader(manifest.coordinateUnits=="rhinoDocument");
            return new GlbModelLoader();
        }
    }
}
