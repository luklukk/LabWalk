using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace LabWalk
{
    public sealed class MetaAnchorService
    {
        // An unsaved anchor: world-locks content for this session only (e.g. an aligned but unsaved placement).
        public async Task<OVRSpatialAnchor> CreateAsync(Pose pose, string name, CancellationToken token)
        {
            var go=new GameObject(name);
            go.transform.SetPositionAndRotation(pose.position,pose.rotation);
            var anchor=go.AddComponent<OVRSpatialAnchor>();
            try
            {
                var deadline=Time.realtimeSinceStartup+20;
                while(anchor && (!anchor.Created || !anchor.Localized))
                {
                    token.ThrowIfCancellationRequested();
                    if(Time.realtimeSinceStartup>deadline) throw new TimeoutException("Anchor creation timed out. Check tracking and try again.");
                    await Task.Yield();
                }
                if(!anchor) throw new InvalidOperationException("The headset could not create an anchor.");
                return anchor;
            }
            catch { if(go) UnityEngine.Object.Destroy(go); throw; }
        }
    }
}
