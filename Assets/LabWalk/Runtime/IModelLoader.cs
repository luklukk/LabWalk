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

    // A switchable layer of the loaded model (Rhino "Toggle:"/"Option:" layers, or GLB nodes with those names).
    // Showing it activates Root; a nested layer is only visible while its enclosing layer is visible too.
    public sealed class ModelLayer
    {
        public LayerGroupKind Kind;
        public string OptionGroup, Name;
        public bool DefaultOn;
        public ModelLayer Parent;
        public GameObject Root;
        public string Key => (Parent!=null ? Parent.Key+" > " : "")+(Kind==LayerGroupKind.Option ? $"option:{OptionGroup}/{Name}" : $"toggle:{Name}");
        public string Label => Parent==null ? Name : $"{Parent.Name} > {Name}";
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
