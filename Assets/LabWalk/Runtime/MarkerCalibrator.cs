using System.Collections.Generic;
using System.Linq;
using System.Text;
using LabWalk.Core;
using UnityEngine;

namespace LabWalk
{
    // Automatic placement from printed QR codes. The passthrough camera scanner (CameraMarkerScanner) reads and
    // locates the codes; this class matches their text to the model's "LabWalk marker <ID>" points, keeps a
    // robust average of each code's position over many sightings and fits yaw + translation (never scale) to
    // all steady markers.
    public sealed class MarkerCalibrator
    {
        const int MaxSamples=60, MinSamples=5;
        const float MaxSpread=0.03f, OutlierDistance=0.06f, MaxSightingDistance=3f, LostAfter=1.5f;

        sealed class Track
        {
            public readonly List<Vector3> Samples=new List<Vector3>();
            public Vector3 Mean;
            public float Spread, LastSeen=-100;
            public int Used;
            public bool Steady => Used>=MinSamples && Spread<=MaxSpread;
        }

        readonly Dictionary<string,Track> tracks=new Dictionary<string,Track>();
        readonly CameraMarkerScanner scanner=new CameraMarkerScanner();
        readonly List<CameraMarkerScanner.Sighting> sightings=new List<CameraMarkerScanner.Sighting>();
        MarkerPoint[] markers=new MarkerPoint[0];
        int changes, solvedChanges=-1;

        public string Status { get; private set; }="Markers: none in this model";
        public bool Active => markers.Length>=2;
        public bool HasSolution { get; private set; }
        public AlignmentResult Solution { get; private set; }
        public int SolutionMarkerCount { get; private set; }
        // Every marker in the model is steady: more sightings no longer change the fit.
        public bool AllSteady => markers.Length>0 && markers.All(m=>tracks.TryGetValue(m.id,out var t) && t.Steady);

        public void SetMarkers(MarkerPoint[] modelMarkers)
        {
            markers=modelMarkers ?? new MarkerPoint[0];
            Reset("model changed");
            Status=markers.Length>=2 ? Describe() : markers.Length==1 ? "Markers: only 1 in model (need 2+)" : "Markers: none in this model";
        }

        // Positions from before a recenter or model change are in a different frame; drop them.
        public void Reset(string reason)
        {
            tracks.Clear(); HasSolution=false; solvedChanges=-1; changes++;
            if(Active) DiagnosticsLog.Write("Marker positions cleared: "+reason);
        }

        // scanning: whether the camera should look for codes now (passthrough visible, tracking healthy).
        public void Update(bool scanning)
        {
            if(!Active) return;
            scanner.Update(scanning,sightings);
            var now=Time.realtimeSinceStartup;
            foreach(var s in sightings)
            {
                var marker=markers.FirstOrDefault(m=>m.id==s.Id);
                if(marker==null || s.Distance>MaxSightingDistance) continue;
                if(!tracks.TryGetValue(s.Id,out var track))
                {
                    tracks[s.Id]=track=new Track();
                    DiagnosticsLog.Write($"QR marker {s.Id} seen at {s.Center:F3} ({s.Distance:F2} m away, {s.Center.y:F2} m high; model height {marker.position.y:F2} m)");
                }
                track.LastSeen=now;
                track.Samples.Add(s.Center);
                if(track.Samples.Count>MaxSamples) track.Samples.RemoveAt(0);
                var wasSteady=track.Steady;
                Average(track);
                if(track.Steady && !wasSteady) DiagnosticsLog.Write($"QR marker {s.Id} steady at {track.Mean:F3} (±{track.Spread*100:F1} cm from {track.Used} sightings)");
                changes++;
            }
            Solve();
            Status=Describe();
        }

        // Median-centered mean: sightings far from the median (a misread pose) are left out.
        static void Average(Track track)
        {
            var s=track.Samples;
            var median=new Vector3(Median(s.Select(p=>p.x)),Median(s.Select(p=>p.y)),Median(s.Select(p=>p.z)));
            var sum=Vector3.zero; var used=0;
            foreach(var p in s) if(Vector3.Distance(p,median)<=OutlierDistance) { sum+=p; used++; }
            if(used==0) { track.Used=0; return; }
            track.Mean=sum/used; track.Used=used;
            float spread=0; foreach(var p in s) if(Vector3.Distance(p,median)<=OutlierDistance) spread+=(p-track.Mean).sqrMagnitude;
            track.Spread=Mathf.Sqrt(spread/used);
        }
        static float Median(IEnumerable<float> values)
        {
            var sorted=values.OrderBy(v=>v).ToArray();
            return sorted.Length==0 ? 0 : sorted.Length%2==1 ? sorted[sorted.Length/2] : (sorted[sorted.Length/2-1]+sorted[sorted.Length/2])/2;
        }

        void Solve()
        {
            if(changes==solvedChanges) return;
            solvedChanges=changes;
            var steady=markers.Where(m=>tracks.TryGetValue(m.id,out var t) && t.Steady).ToArray();
            if(steady.Length<2) { HasSolution=false; return; }
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
                yield return (placement.TransformPoint(m.position),tracks.TryGetValue(m.id,out var t) && t.Used>0 ? t.Mean : (Vector3?)null);
        }

        // For the status panel while placing: each code, whether it is found, and how high to look for it,
        // e.g. "●  LW1   5' up        ○  LW2   5' up        ○  LW3   3' up  (2/5)".
        public string Checklist()
        {
            return string.Join("        ",markers.Select(m =>
            {
                tracks.TryGetValue(m.id,out var t);
                var found=t!=null && t.Steady;
                var progress=t!=null && !found && t.Used>0 ? $"  ({t.Used}/{MinSamples})" : "";
                return $"{(found ? "●" : "○")}  {m.id}   {Feet(m.position.y)} up{progress}";
            }));
        }

        static string Feet(float meters)
        {
            var inches=Mathf.RoundToInt(meters/0.0254f);
            return inches%12==0 ? $"{inches/12}'" : $"{inches/12}' {inches%12}\"";
        }

        public int FoundCount => markers.Count(m=>tracks.TryGetValue(m.id,out var t) && t.Steady);
        public int Count => markers.Length;
        public string CameraStatus => scanner.Status;

        string Describe()
        {
            var now=Time.realtimeSinceStartup;
            var text=new StringBuilder("Markers: ");
            foreach(var m in markers)
            {
                text.Append(m.id);
                if(!tracks.TryGetValue(m.id,out var t)) text.Append(" -  ");
                else if(t.Steady) text.Append($" ok {t.Mean.y:F2}m  ");
                else text.Append(now-t.LastSeen<LostAfter ? $" {t.Used}/{MinSamples}  " : " lost  ");
            }
            if(HasSolution) text.Append($"| fit {Solution.RmsResidualMeters*100:F1} cm rms");
            text.Append('\n').Append(scanner.Status);
            return text.ToString();
        }
    }
}
