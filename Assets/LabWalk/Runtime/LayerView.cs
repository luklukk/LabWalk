using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace LabWalk
{
    // Which switchable layers are shown, and how. Options in the same group are exclusive: exactly one is
    // selected. Toggles are independent. Any layer with edges can additionally be drawn as a wireframe:
    //   toggle:  off -> hidden, on -> solid or wireframe
    //   option:  selected -> solid or wireframe; not selected -> hidden, or a wireframe overlay for comparison
    // Choices are remembered per model version (fingerprint) on this headset.
    public sealed class LayerView
    {
        public enum Display { Hidden, Solid, Wireframe }

        readonly List<ModelLayer> layers;
        readonly Dictionary<ModelLayer,bool> on=new Dictionary<ModelLayer,bool>(), wire=new Dictionary<ModelLayer,bool>();
        readonly string prefsKey;

        public IReadOnlyList<ModelLayer> Layers => layers;
        public IReadOnlyList<string> OptionGroups { get; }
        public bool Any => layers.Count>0;

        public LayerView(LoadedModel model,string fingerprint)
        {
            layers=model!=null ? model.Layers : new List<ModelLayer>();
            OptionGroups=layers.Where(l=>l.Kind==LayerGroupKind.Option).Select(l=>l.OptionGroup).Distinct().ToList();
            prefsKey="LabWalk.layers."+(fingerprint ?? "none");
            foreach(var l in layers) { on[l]=l.DefaultOn; wire[l]=false; }
            try
            {
                // Lines of "<key>=<on><w?>", e.g. "toggle:Tools=1w". Older entries without "w" are solid.
                foreach(var entry in PlayerPrefs.GetString(prefsKey,"").Split('\n'))
                {
                    var split=entry.LastIndexOf('=');
                    if(split<=0) continue;
                    var layer=layers.FirstOrDefault(l=>l.Key==entry.Substring(0,split));
                    if(layer==null) continue;
                    var value=entry.Substring(split+1);
                    on[layer]=value.StartsWith("1");
                    wire[layer]=value.EndsWith("w") && layer.HasWireframe;
                }
            }
            catch { } // a damaged preference only loses remembered choices
            foreach(var group in OptionGroups)
            {
                var options=Options(group);
                var chosen=options.FirstOrDefault(o=>on[o]) ?? options[0];
                foreach(var o in options) on[o]=o==chosen;
            }
            Apply();
        }

        public bool IsOn(ModelLayer layer) => on.TryGetValue(layer,out var v) && v;
        public bool IsWire(ModelLayer layer) => wire.TryGetValue(layer,out var v) && v;
        public List<ModelLayer> Options(string group) => layers.Where(l=>l.Kind==LayerGroupKind.Option && l.OptionGroup==group).ToList();
        public List<ModelLayer> Toggles => layers.Where(l=>l.Kind==LayerGroupKind.Toggle).ToList();
        public ModelLayer Selected(string group) => Options(group).FirstOrDefault(IsOn);

        public Display DisplayOf(ModelLayer layer)
        {
            if(IsOn(layer)) return IsWire(layer) ? Display.Wireframe : Display.Solid;
            return layer.Kind==LayerGroupKind.Option && IsWire(layer) ? Display.Wireframe : Display.Hidden;
        }

        // Primary action (A): options are selected (the others in their group are deselected); toggles flip.
        public void Activate(ModelLayer layer)
        {
            if(layer.Kind==LayerGroupKind.Option) foreach(var o in Options(layer.OptionGroup)) on[o]=o==layer;
            else on[layer]=!on[layer];
            Changed(layer);
        }

        // Secondary action (X): wireframe on/off. A toggle that is off is turned on as a wireframe.
        public void ToggleWireframe(ModelLayer layer)
        {
            if(!layer.HasWireframe) return;
            wire[layer]=!wire[layer];
            if(layer.Kind==LayerGroupKind.Toggle && wire[layer]) on[layer]=true;
            Changed(layer);
        }

        public ModelLayer NextOption(string group)
        {
            var options=Options(group);
            var index=options.FindIndex(IsOn);
            return options[(index+1)%options.Count];
        }

        void Changed(ModelLayer layer)
        {
            Apply(); Save();
            DiagnosticsLog.Write($"Layer {layer.Label}: {DisplayOf(layer)}");
        }

        void Apply()
        {
            foreach(var l in layers)
            {
                var display=DisplayOf(l);
                if(l.Root && l.Root.activeSelf!=(display!=Display.Hidden)) l.Root.SetActive(display!=Display.Hidden);
                foreach(var r in l.Solid) if(r) r.enabled=display==Display.Solid;
                foreach(var w in l.Wire) if(w && w.activeSelf!=(display==Display.Wireframe)) w.SetActive(display==Display.Wireframe);
            }
        }

        void Save()
        {
            var text=new StringBuilder();
            foreach(var l in layers) text.Append(l.Key).Append('=').Append(on[l] ? '1' : '0').Append(wire[l] ? "w" : "").Append('\n');
            try { PlayerPrefs.SetString(prefsKey,text.ToString()); PlayerPrefs.Save(); } catch { }
        }

        // One line for the status panel, e.g. "Design: Renovation (+ Existing wireframe)".
        public string Summary()
        {
            if(OptionGroups.Count==0) return null;
            return string.Join("  |  ",OptionGroups.Select(g =>
            {
                var selected=Selected(g);
                var overlays=Options(g).Where(o=>o!=selected && IsWire(o)).Select(o=>o.Name).ToList();
                return $"{g}: {selected?.Name}{(selected!=null && IsWire(selected) ? " (wireframe)" : "")}{(overlays.Count>0 ? " + "+string.Join(", ",overlays)+" wireframe" : "")}";
            }));
        }
    }
}
