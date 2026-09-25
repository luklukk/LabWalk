using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Logging;
using UnityEngine;

namespace LabWalk
{
    public sealed class GlbModelLoader : IModelLoader
    {
        public async Task<LoadedModel> LoadAsync(byte[] data, Transform parent, float scale, CancellationToken token)
        {
            if (data.Length<12 || BitConverter.ToUInt32(data,0)!=0x46546c67 || BitConverter.ToUInt32(data,4)!=2)
                throw new InvalidDataException("Expected a binary glTF 2.0 (.glb) file.");
            var gltf=new GltfImport(logger:new ConsoleLogger());
            var root=new GameObject("Imported model (meters)");
            root.transform.SetParent(parent,false);
            try
            {
                token.ThrowIfCancellationRequested();
                if (!await gltf.Load(data,cancellationToken:token)) throw new InvalidDataException("GLB import failed. See the Unity/device log for details.");
                token.ThrowIfCancellationRequested();
                var instantiator=new GameObjectInstantiator(gltf,root.transform,settings:new InstantiationSettings { Mask=ComponentType.Mesh });
                if (!await gltf.InstantiateMainSceneAsync(instantiator,token)) throw new InvalidDataException("GLB has no usable main scene.");
                token.ThrowIfCancellationRequested();
                root.transform.localScale=Vector3.one*scale;
                // Compute bounds in placement-local coordinates; world yaw must never affect dimensions.
                var bounds=new Bounds(); var first=true;
                foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var b=renderer.localBounds;
                    for(var x=-1;x<=1;x+=2) for(var y=-1;y<=1;y+=2) for(var z=-1;z<=1;z+=2)
                    {
                        var p=parent.InverseTransformPoint(renderer.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3(x,y,z))));
                        if(first) { bounds=new Bounds(p,Vector3.zero); first=false; } else bounds.Encapsulate(p);
                    }
                }
                if(first) throw new InvalidDataException("GLB contains no renderable meshes.");
                var loaded=new LoadedModel(root,bounds,gltf);
                // Nodes named like Rhino switchable layers ("Toggle: Tools", "Option: Renovation") become switches.
                var byNode=new System.Collections.Generic.Dictionary<Transform,ModelLayer>();
                foreach(var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if(t==root.transform || !LayerGroupNames.TryParse(t.name,out var kind,out var optionGroup,out var name)) continue;
                    ModelLayer parentLayer=null;
                    for(var p=t.parent; p && p!=root.transform && parentLayer==null; p=p.parent) byNode.TryGetValue(p,out parentLayer);
                    var layer=new ModelLayer {Kind=kind,OptionGroup=optionGroup,Name=name,DefaultOn=t.gameObject.activeSelf,Parent=parentLayer,Root=t.gameObject};
                    byNode[t]=layer;
                    if(t.GetComponentInChildren<Renderer>(true)) loaded.Layers.Add(layer);
                }
                return loaded;
            }
            catch { UnityEngine.Object.Destroy(root); gltf.Dispose(); throw; }
        }
    }
}
