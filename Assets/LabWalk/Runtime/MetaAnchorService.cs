using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace LabWalk
{
    public sealed class MetaAnchorService
    {
        public async Task<OVRSpatialAnchor> CreateAndSaveAsync(Pose pose, CancellationToken token)
        {
            var go=new GameObject("Lab spatial anchor");
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
                token.ThrowIfCancellationRequested();
                var saved=await anchor.SaveAnchorAsync();
                if(!saved.Success) throw new InvalidOperationException("Anchor save failed: "+saved.Status);
                if(token.IsCancellationRequested) { await anchor.EraseAnchorAsync(); token.ThrowIfCancellationRequested(); }
                return anchor;
            }
            catch { if(go) UnityEngine.Object.Destroy(go); throw; }
        }

        public async Task<OVRSpatialAnchor> RestoreAsync(string uuid, CancellationToken token)
        {
            var unbound=new List<OVRSpatialAnchor.UnboundAnchor>();
            var result=await OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[]{Guid.Parse(uuid)},unbound);
            token.ThrowIfCancellationRequested();
            if(!result.Success) throw new InvalidOperationException("Anchor query failed: "+result.Status+". Use Retry or Realign.");
            if(unbound.Count!=1) throw new InvalidOperationException("Saved anchor was not returned by this XR runtime. Use Retry or Realign.");
            var item=unbound[0];
            if(!await item.LocalizeAsync(20)) throw new InvalidOperationException("Anchor could not localize. Look around the original room, then use Retry or Realign.");
            token.ThrowIfCancellationRequested();
            var deadline=Time.realtimeSinceStartup+10;
            Pose pose;
            while(!item.TryGetPose(out pose))
            {
                token.ThrowIfCancellationRequested();
                if(Time.realtimeSinceStartup>deadline) throw new TimeoutException("Anchor pose unavailable. Use Retry or Realign.");
                await Task.Yield();
            }
            var go=new GameObject("Restored lab anchor");
            go.transform.SetPositionAndRotation(pose.position,pose.rotation);
            var anchor=go.AddComponent<OVRSpatialAnchor>();
            try { item.BindTo(anchor); return anchor; }
            catch { UnityEngine.Object.Destroy(go); throw; }
        }
    }
}
