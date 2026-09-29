using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace LabWalk
{
    public interface IModelLoader
    {
        Task<LoadedModel> LoadAsync(byte[] data, Transform parent, float metersPerCoordinateUnit, CancellationToken token);
    }

    // A switchable layer of the loaded model: a Rhino layer ("Option:" layers are exclusive choices), or a GLB
    // node named "Toggle: ..."/"Option: ...". Showing it activates Root; a nested layer is only visible while its
    // enclosing layer is visible too.
    public sealed class ModelLayer
    {
        public LayerGroupKind Kind;
        public string OptionGroup, Name;
        public bool DefaultOn;
        public int Order;             // listing order (Rhino layer table order)
        public ModelLayer Parent;
        public readonly System.Collections.Generic.List<ModelLayer> Children=new System.Collections.Generic.List<ModelLayer>();
        public GameObject Root;
        // This layer's own geometry (not that of nested switchable layers): solid meshes and wireframe lines.
        public readonly System.Collections.Generic.List<Renderer> Solid=new System.Collections.Generic.List<Renderer>();
        public readonly System.Collections.Generic.List<GameObject> Wire=new System.Collections.Generic.List<GameObject>();
        // For each Wire entry: the object indices whose lines it holds (empty for models without object data).
        public readonly System.Collections.Generic.List<System.Collections.Generic.HashSet<int>> WireObjects=new System.Collections.Generic.List<System.Collections.Generic.HashSet<int>>();
        public bool HasWireframe => Wire.Count>0;
        public string Key => (Parent!=null ? Parent.Key+" > " : "")+(Kind==LayerGroupKind.Option ? $"option:{OptionGroup}/{Name}" : $"toggle:{Name}");
        public string Label => Parent==null ? Name : $"{Parent.Name} > {Name}";
        public string Path => Parent==null ? Name : Parent.Path+" > "+Name;
    }

    // A top-level model object that can be pointed at (Rhino objects; a block instance is one object).
    public sealed class ModelObject
    {
        public int Index;       // value carried by its vertices (mesh UV0.x)
        public string Id, Name;
        public ModelLayer Layer;
        public string Label => string.IsNullOrEmpty(Name) ? Layer?.Name ?? "Object" : Name;
    }

    public sealed class LoadedModel : IDisposable
    {
        public readonly System.Collections.Generic.List<ModelLayer> Layers=new System.Collections.Generic.List<ModelLayer>();
        public GameObject Root { get; private set; }
        public Bounds BoundsMeters { get; private set; }
        public string ImportSummary { get; private set; }
        public bool Incomplete { get; private set; }
        // Optional file-defined alignment references in model-local meters, and the source unit name.
        public Vector3? ReferenceA, ReferenceB;
        // Optional calibration markers (QR code centers) by ID, in model-local meters.
        public readonly System.Collections.Generic.Dictionary<string,Vector3> Markers=new System.Collections.Generic.Dictionary<string,Vector3>();
        // Objects by index (index 0 = none); empty for models without per-object data (GLB).
        public readonly System.Collections.Generic.List<ModelObject> Objects=new System.Collections.Generic.List<ModelObject>{ null };
        public string SourceUnits="meters";
        readonly IDisposable resources;
        public LoadedModel(GameObject root, Bounds bounds, IDisposable resources,string summary="GLB",bool incomplete=false)
        { Root=root; BoundsMeters=bounds; this.resources=resources; ImportSummary=summary; Incomplete=incomplete; }
        public void Dispose()
        {
            if (Root) { Root.SetActive(false); UnityEngine.Object.Destroy(Root); }
            resources.Dispose();
        }
    }
}
