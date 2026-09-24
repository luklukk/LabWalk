using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;

namespace LabWalk
{
    // Holding the Meta button recenters the view. With Unity OpenXR's default AllowRecentering=true the
    // floor origin is XR LOCAL_FLOOR space, which the recenter moves, so world-placed content jumps.
    // With AllowRecentering=false Unity uses STAGE space, which recentering does not move
    // (Unity OpenXR input docs, "Floor Reference Space" table). Recenter events are still detected
    // so the app can report them and verify that the placement stayed put.
    public static class RecenterGuard
    {
        public static event Action Recentered;
        public static int Count { get; private set; }
        static readonly List<XRInputSubsystem> subsystems=new List<XRInputSubsystem>();
        static readonly HashSet<XRInputSubsystem> hooked=new HashSet<XRInputSubsystem>();

        // Before XR initializes, so the first reference space is already created without recentering.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void DisableRecentering()
        {
            try { OpenXRSettings.SetAllowRecentering(false); }
            catch(Exception e) { Debug.LogWarning("Could not disable OpenXR recentering: "+e.Message); }
        }

        // Called every frame by the app: subsystems can appear after startup.
        public static void Poll()
        {
            SubsystemManager.GetSubsystems(subsystems);
            foreach(var s in subsystems)
                if(hooked.Add(s)) s.trackingOriginUpdated+=OnOriginUpdated;
        }

        public static void Hook()
        {
            if(OVRManager.display!=null) OVRManager.display.RecenteredPose+=OnRecentered;
            DiagnosticsLog.Write($"Recentering allowed: {OpenXRSettings.AllowRecentering}; OVR tracking origin {OVRManager.instance?.trackingOriginType}");
        }

        public static void Unhook()
        {
            if(OVRManager.display!=null) OVRManager.display.RecenteredPose-=OnRecentered;
            foreach(var s in hooked) if(s!=null) s.trackingOriginUpdated-=OnOriginUpdated;
            hooked.Clear();
        }

        static void OnOriginUpdated(XRInputSubsystem s) { DiagnosticsLog.Write("XR tracking origin updated"); Raise(); }
        static void OnRecentered() { DiagnosticsLog.Write("OVR recentered pose event"); Raise(); }
        static void Raise() { Count++; Recentered?.Invoke(); }
    }
}
