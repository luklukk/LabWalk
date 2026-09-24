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

    public sealed class LoadedModel : IDisposable
    {
        public GameObject Root { get; private set; }
        public Bounds BoundsMeters { get; private set; }
        public string ImportSummary { get; private set; }
        public bool Incomplete { get; private set; }
        // Optional file-defined alignment references in model-local meters, and the source unit name.
        public Vector3? ReferenceA, ReferenceB;
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
