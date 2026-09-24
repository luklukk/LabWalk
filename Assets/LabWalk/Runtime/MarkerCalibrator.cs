using System.Collections.Generic;
using System.Linq;
using System.Text;
using LabWalk.Core;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.Android;

namespace LabWalk
{
    // Automatic placement from printed QR codes. Horizon OS (through MR Utility Kit) detects and tracks the
    // codes; this class only matches their text to the model's "LabWalk marker <ID>" points, averages each
    // code's position over many updates and fits yaw + translation (never scale) to all steady markers.
    // Needs Quest 3/3S, Horizon OS v78+ and the spatial data (USE_SCENE) permission; not available in the Editor.
    public sealed class MarkerCalibrator
    {
        const int MaxSamples=90, MinSamples=8;
        const float MaxSpread=0.02f;

        sealed class Track
        {
            public readonly List<Vector3> Samples=new List<Vector3>();
            public Vector3 Mean;
            public float Spread;
            public bool Tracked;
            public bool Steady => Samples.Count>=MinSamples && Spread<=MaxSpread;
        }

        readonly Dictionary<string,Track> tracks=new Dictionary<string,Track>();
        readonly List<MRUKTrackable> trackables=new List<MRUKTrackable>();
        MarkerPoint[] markers=new MarkerPoint[0];
        MRUK mruk;
        bool permissionRequested;
        int solvedSampleCount=-1;

        public string Status { get; private set; }="Markers: none in this model";
        public bool Active => markers.Length>=2;
        public bool HasSolution { get; private set; }
        public AlignmentResult Solution { get; private set; }
        public int SolutionMarkerCount { get; private set; }

        public void SetMarkers(MarkerPoint[] modelMarkers)
        {
            markers=modelMarkers ?? new MarkerPoint[0];
            Reset("model changed");
            Status=markers.Length>=2 ? "Markers: starting QR tracking" : markers.Length==1 ? "Markers: only 1 in model (need 2+)" : "Markers: none in this model";
            if(Active) StartTracking();
        }

        // Samples from before a recenter or model change are in a different frame; drop them.
        public void Reset(string reason)
        {
            tracks.Clear(); HasSolution=false; solvedSampleCount=-1;
            if(Active) DiagnosticsLog.Write("Marker samples cleared: "+reason);
        }

        void StartTracking()
        {
            if(mruk) return;
            if(Application.isEditor) { Status="Markers: QR tracking runs on the headset only"; return; }
            if(!Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission) && !permissionRequested)
            {
                permissionRequested=true;
                Permission.RequestUserPermission(OVRPermissionsRequester.ScenePermission);
            }
            // Configure before Awake: no room scan loading and no MRUK world locking, QR codes only.
            var go=new GameObject("MRUK (QR calibration markers)");
            go.SetActive(false);
            mruk=go.AddComponent<MRUK>();
            mruk.EnableWorldLock=false;
            mruk.SceneSettings=new MRUK.MRUKSettings {
                LoadSceneOnStartup=false,
                TrackerConfiguration=new OVRAnchor.TrackerConfiguration {QRCodeTrackingEnabled=true}
            };
            go.SetActive(true);
            DiagnosticsLog.Write($"QR tracking requested for markers {string.Join(", ",markers.Select(m=>m.id))}");
        }

        public void Update()
        {
            if(!Active || !mruk) return;
            if(!Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission))
            { Status="Markers: allow spatial data access in the permission prompt or headset settings"; return; }
            if(!mruk.QRCodeTrackingSupported) { Status="Markers: QR tracking not supported (needs Horizon OS v78+)"; return; }

            foreach(var t in tracks.Values) t.Tracked=false;
            mruk.GetTrackables(trackables);
            foreach(var trackable in trackables)
            {
                if(!trackable || trackable.TrackableType!=OVRAnchor.TrackableType.QRCode || !trackable.IsTracked) continue;
                var id=trackable.MarkerPayloadString?.Trim();
                if(string.IsNullOrEmpty(id) || !markers.Any(m=>m.id==id)) continue;
                if(!tracks.TryGetValue(id,out var track)) { tracks[id]=track=new Track(); DiagnosticsLog.Write("QR marker detected: "+id); }
                var center=trackable.PlaneRect.HasValue ? trackable.transform.TransformPoint(trackable.PlaneRect.Value.center) : trackable.transform.position;
                track.Tracked=true;
                track.Samples.Add(center);
                if(track.Samples.Count>MaxSamples) track.Samples.RemoveAt(0);
                track.Mean=Vector3.zero;
                foreach(var s in track.Samples) track.Mean+=s;
                track.Mean/=track.Samples.Count;
                float sum=0; foreach(var s in track.Samples) sum+=(s-track.Mean).sqrMagnitude;
                track.Spread=Mathf.Sqrt(sum/track.Samples.Count);
            }
            Solve();
            Status=Describe();
        }

        void Solve()
        {
            var steady=markers.Where(m=>tracks.TryGetValue(m.id,out var t) && t.Steady).ToArray();
            var sampleCount=steady.Sum(m=>tracks[m.id].Samples.Count);
            if(steady.Length<2) { HasSolution=false; return; }
            if(sampleCount==solvedSampleCount) return;
            solvedSampleCount=sampleCount;
            try
            {
                var fit=AlignmentMath.SolveFromPoints(steady.Select(m=>ModelManifest.ToPoint(m.position)).ToArray(),
                    steady.Select(m=>ModelManifest.ToPoint(tracks[m.id].Mean)).ToArray());
                var first=!HasSolution || SolutionMarkerCount!=steady.Length;
                Solution=fit; SolutionMarkerCount=steady.Length; HasSolution=true;
                if(first) DiagnosticsLog.Write($"Marker fit from {steady.Length} ({string.Join(", ",steady.Select(m=>m.id))}): rms {fit.RmsResidualMeters*100:F1} cm, max {fit.MaxResidualMeters*100:F1} cm, spread model {fit.ModelBaselineMeters:F3} m / room {fit.PhysicalBaselineMeters:F3} m");
            }
            catch(System.ArgumentException e) { HasSolution=false; Status="Markers: "+e.Message; }
        }

        // Largest distance between where the placed model expects each steady marker and where it is seen.
        public float Deviation(Transform placement)
        {
            float worst=0;
            foreach(var m in markers)
                if(tracks.TryGetValue(m.id,out var t) && t.Steady)
                    worst=Mathf.Max(worst,Vector3.Distance(placement.TransformPoint(m.position),t.Mean));
            return worst;
        }

        public IEnumerable<(Vector3 expected,Vector3? seen)> Visuals(Transform placement)
        {
            foreach(var m in markers)
                yield return (placement.TransformPoint(m.position),tracks.TryGetValue(m.id,out var t) && t.Samples.Count>0 ? t.Mean : (Vector3?)null);
        }

        string Describe()
        {
            var text=new StringBuilder("Markers: ");
            foreach(var m in markers)
            {
                if(!tracks.TryGetValue(m.id,out var t)) text.Append(m.id).Append(" not seen  ");
                else text.Append(m.id).Append(t.Steady ? " ok" : t.Tracked ? $" {t.Samples.Count}/{MinSamples}" : " lost").Append(t.Samples.Count>=2 ? $" ±{t.Spread*100:F1}cm  " : "  ");
            }
            if(HasSolution) text.Append($"| fit {Solution.RmsResidualMeters*100:F1} cm rms");
            else text.Append("| look at 2+ codes");
            return text.ToString();
        }
    }
}
