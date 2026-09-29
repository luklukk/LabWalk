using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LabWalk.Core;
using Meta.XR;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Rendering;

namespace LabWalk
{
    // Reads the printed QR markers from the headset's passthrough camera image (Passthrough Camera API, through
    // MRUK's PassthroughCameraAccess), the way phone and store QR scanner apps do, instead of Horizon OS's
    // built-in QR tracking (which found no codes on the lab headsets). A few times a second one camera frame is
    // copied from the GPU without stalling, decoded and located on a worker thread (QrFrameDecoder), and each
    // code's center is put into world space with the camera pose at that frame's capture time.
    // Needs Quest 3/3S, Horizon OS v74+ and the headset camera permission; not available in the Editor.
    public sealed class CameraMarkerScanner
    {
        public const double CodeSizeMeters=0.15; // printed code edge, modules only (Tools/MarkerSheet, Docs/MARKERS.md)
        const float ScanInterval=0.25f;

        public struct Sighting { public string Id; public Vector3 Center; public float Distance; }

        PassthroughCameraAccess camera;
        bool supportChecked, supported, permissionRequested, readbackPending, rowsTopDown;
        Task<List<QrFinding>> decoding;
        Pose framePose;
        PinholeCamera frameCamera;
        byte[] frame, luminance;
        float nextScan, retryTime, decodeStarted, decodeSeconds;
        int scans, mirrored, upright;
        string lastSeen="";

        public string Status { get; private set; }="Camera scan: off";

        public void Update(bool wanted, List<Sighting> sightings)
        {
            sightings.Clear();
            if(Application.isEditor) { Status="Camera scan runs on the headset only"; return; }
            if(!wanted)
            {
                if(camera && camera.enabled) camera.enabled=false; // stops the camera stream
                Status="Camera scan: paused";
                return;
            }
            if(!supportChecked)
            {
                supportChecked=true;
                try { supported=PassthroughCameraAccess.IsSupported; } catch(Exception e) { supported=false; DiagnosticsLog.Write("Camera support check failed: "+e.Message); }
                DiagnosticsLog.Write($"Passthrough camera access supported: {supported} ({SystemInfo.deviceModel}, {SystemInfo.operatingSystem})");
            }
            if(!supported) { Status="Camera scan: needs Quest 3/3S with Horizon OS v74 or newer"; return; }
            var permission=OVRPermissionsRequester.PassthroughCameraAccessPermission;
            if(!Permission.HasUserAuthorizedPermission(permission))
            {
                if(!permissionRequested)
                {
                    permissionRequested=true;
                    Permission.RequestUserPermission(permission);
                    DiagnosticsLog.Write("Requesting headset camera permission");
                }
                Status="Camera scan: allow headset camera access (prompt, or Settings > Apps > Lab Walk > Permissions)";
                return;
            }
            if(!camera) Create();
            if(!camera.enabled)
            {
                // Disabled by us while not wanted, or by PassthroughCameraAccess itself when the camera failed to start.
                if(Time.realtimeSinceStartup>=retryTime) { retryTime=Time.realtimeSinceStartup+5; camera.enabled=true; }
                Status="Camera scan: starting camera";
                return;
            }
            if(!camera.IsPlaying) { Status="Camera scan: starting camera"; return; }
            Collect(sightings);
            if(!readbackPending && decoding==null && Time.realtimeSinceStartup>=nextScan) StartScan();
            Status=$"Camera scan: {scans} frames, {decodeSeconds*1000:F0} ms each{lastSeen}";
        }

        void Create()
        {
            var go=new GameObject("Passthrough camera (QR markers)");
            go.SetActive(false);
            camera=go.AddComponent<PassthroughCameraAccess>();
            camera.CameraPosition=PassthroughCameraAccess.CameraPositionType.Left;
            camera.RequestedResolution=new Vector2Int(1280,960);
            go.SetActive(true);
            DiagnosticsLog.Write("Passthrough camera requested (left, 1280x960)");
        }

        void StartScan()
        {
            var texture=camera.GetTexture();
            if(!texture) return;
            // The texture read below and the pose both belong to the latest image (PassthroughCameraAccess runs first).
            framePose=camera.GetCameraPose();
            frameCamera=Pinhole(camera);
            readbackPending=true;
            nextScan=Time.realtimeSinceStartup+ScanInterval;
            AsyncGPUReadback.Request(texture,0,TextureFormat.RGBA32,OnReadback);
        }

        void OnReadback(AsyncGPUReadbackRequest request)
        {
            readbackPending=false;
            if(request.hasError || decoding!=null) return;
            var data=request.GetData<byte>();
            var size=frameCamera.Width*frameCamera.Height*4;
            if(data.Length<size) return;
            if(frame==null || frame.Length!=size) frame=new byte[size];
            Unity.Collections.NativeArray<byte>.Copy(data,frame,size);
            var cameraModel=frameCamera; var topDown=rowsTopDown;
            decodeStarted=Time.realtimeSinceStartup;
            decoding=Task.Run(()=>QrFrameDecoder.Decode(frame,cameraModel,CodeSizeMeters,ref luminance,topDown));
        }

        void Collect(List<Sighting> sightings)
        {
            if(decoding==null || !decoding.IsCompleted) return;
            var task=decoding; decoding=null;
            scans++;
            decodeSeconds=Mathf.Lerp(decodeSeconds>0 ? decodeSeconds : Time.realtimeSinceStartup-decodeStarted,Time.realtimeSinceStartup-decodeStarted,0.2f);
            if(task.IsFaulted) { DiagnosticsLog.Write("QR decode failed: "+task.Exception?.GetBaseException().Message); return; }
            foreach(var f in task.Result)
            {
                // Printed codes are never mirror images: if they read mirrored, the camera rows are the other way up.
                if(f.Mirrored) mirrored++; else upright++;
                if(mirrored>=3 && mirrored>upright*2)
                {
                    rowsTopDown=!rowsTopDown; mirrored=upright=0;
                    DiagnosticsLog.Write($"QR codes read mirrored: switching camera image row order (top-down={rowsTopDown})");
                    sightings.Clear();
                    return;
                }
                if(f.Mirrored) continue;
                var local=new Vector3((float)f.Pose.Center.X,(float)f.Pose.Center.Y,(float)f.Pose.Center.Z);
                var world=framePose.position+framePose.rotation*local;
                sightings.Add(new Sighting { Id=f.Text, Center=world, Distance=(float)f.Pose.Distance });
                lastSeen=$", last {f.Text} at {f.Pose.Distance:F1} m";
            }
        }

        // PassthroughCameraAccess's pinhole model (see its ViewportPointToLocalRay), in pixels of the current image.
        static PinholeCamera Pinhole(PassthroughCameraAccess camera)
        {
            var intrinsics=camera.Intrinsics;
            var sensor=(Vector2)intrinsics.SensorResolution; var current=(Vector2)camera.CurrentResolution;
            var scale=current/sensor; scale/=Mathf.Max(scale.x,scale.y);
            var crop=new Rect(sensor.x*(1-scale.x)*0.5f,sensor.y*(1-scale.y)*0.5f,sensor.x*scale.x,sensor.y*scale.y);
            return new PinholeCamera {
                Width=camera.CurrentResolution.x, Height=camera.CurrentResolution.y,
                Fx=intrinsics.FocalLength.x*current.x/crop.width, Fy=intrinsics.FocalLength.y*current.y/crop.height,
                Cx=(intrinsics.PrincipalPoint.x-crop.x)*current.x/crop.width, Cy=(intrinsics.PrincipalPoint.y-crop.y)*current.y/crop.height,
            };
        }
    }
}
