using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LabWalk
{
    // Finds the model object along a ray (the controller pointer): the visible solid geometry is drawn with a
    // picking shader into a one-pixel target looking down the ray, and the object index (mesh UV0.x) and hit
    // distance are read back asynchronously. The result arrives a frame or two later; nothing is read from the
    // CPU copies of the meshes, which are released after upload.
    public sealed class ObjectPicker
    {
        static readonly int ViewProj=Shader.PropertyToID("_LabWalkPickViewProj"), Origin=Shader.PropertyToID("_LabWalkPickOrigin");
        readonly Material material;
        readonly RenderTexture target;
        readonly CommandBuffer buffer=new CommandBuffer {name="Lab Walk object pick"};
        readonly bool packed; // no float render targets: index in 8-bit channels, no distance
        bool pending;
        Ray pendingRay;

        public bool Available => material!=null;
        public bool Busy => pending;
        public int Index { get; private set; }         // object under the ray at the last pick (0 = none)
        public float Distance { get; private set; }    // meters along the ray, if known
        public bool HasDistance { get; private set; }
        public Ray Ray { get; private set; }
        public int Version { get; private set; }       // increments with every completed pick

        public ObjectPicker()
        {
            var shader=Resources.Load<Shader>("LabWalkPick");
            if(!shader || !shader.isSupported) { Debug.LogWarning("Object picking unavailable: pick shader missing or unsupported."); return; }
            material=new Material(shader) {name="Lab Walk pick"};
            packed=!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat);
            target=new RenderTexture(1,1,24,packed ? RenderTextureFormat.ARGB32 : RenderTextureFormat.ARGBFloat) {name="Lab Walk pick target"};
            target.Create();
        }

        // Starts a pick along ray against the given renderers (only enabled, active ones are drawn).
        public bool Request(Ray ray,IEnumerable<Renderer> renderers,float maxDistance=40)
        {
            if(material==null || pending) return false;
            var view=Matrix4x4.Inverse(Matrix4x4.TRS(ray.origin,Quaternion.LookRotation(ray.direction),new Vector3(1,1,-1)));
            var projection=GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(0.25f,1,0.02f,maxDistance),true);
            buffer.Clear();
            buffer.SetRenderTarget(target);
            buffer.ClearRenderTarget(true,true,Color.clear);
            buffer.SetGlobalMatrix(ViewProj,projection*view);
            buffer.SetGlobalVector(Origin,new Vector4(ray.origin.x,ray.origin.y,ray.origin.z,packed ? 1 : 0));
            // Only batches whose bounds the ray passes through can be hit; skipping the rest keeps each pick cheap.
            foreach(var r in renderers)
                if(r && r.enabled && r.gameObject.activeInHierarchy && r.bounds.IntersectRay(ray,out var near) && near<=maxDistance)
                    buffer.DrawRenderer(r,material,0,0);
            Graphics.ExecuteCommandBuffer(buffer);
            pending=true; pendingRay=ray;
            AsyncGPUReadback.Request(target,0,OnReadback);
            return true;
        }

        void OnReadback(AsyncGPUReadbackRequest request)
        {
            pending=false;
            if(request.hasError) return;
            if(packed)
            {
                var c=request.GetData<Color32>()[0];
                Index=c.r+c.g*256+c.b*65536; HasDistance=false; Distance=0;
            }
            else
            {
                var c=request.GetData<Color>()[0];
                Index=Mathf.RoundToInt(c.r); Distance=c.g; HasDistance=Index>0;
            }
            Ray=pendingRay; Version++;
        }

        public void Release()
        {
            if(target) { target.Release(); Object.Destroy(target); }
            if(material) Object.Destroy(material);
            buffer.Release();
        }
    }
}
