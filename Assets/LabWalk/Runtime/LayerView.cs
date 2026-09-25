using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace LabWalk
{
    // Which switchable layers are shown. Options in the same group are exclusive (exactly one on);
    // toggles are independent. Choices are remembered per model version (fingerprint) on this headset.
    public sealed class LayerView
    {
        readonly List<ModelLayer> layers;
        readonly Dictionary<ModelLayer,bool> on=new Dictionary<ModelLayer,bool>();
        readonly string prefsKey;

        public IReadOnlyList<ModelLayer> Layers => layers;
        public IReadOnlyList<string> OptionGroups { get; }
        public bool Any => layers.Count>0;

        public LayerView(LoadedModel model,string fingerprint)
        {
            layers=model!=null ? model.Layers : new List<ModelLayer>();
            OptionGroups=layers.Where(l=>l.Kind==LayerGroupKind.Option).Select(l=>l.OptionGroup).Distinct().ToList();
            prefsKey="LabWalk.layers."+(fingerprint ?? "none");
            foreach(var l in layers) on[l]=l.DefaultOn;
            try
            {
                foreach(var entry in PlayerPrefs.GetString(prefsKey,"").Split('\n'))
                {
                    var split=entry.LastIndexOf('=');
                    if(split<=0) continue;
                    var layer=layers.FirstOrDefault(l=>l.Key==entry.Substring(0,split));
                    if(layer!=null) on[layer]=entry.Substring(split+1)=="1";
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
        public List<ModelLayer> Options(string group) => layers.Where(l=>l.Kind==LayerGroupKind.Option && l.OptionGroup==group).ToList();
        public List<ModelLayer> Toggles => layers.Where(l=>l.Kind==LayerGroupKind.Toggle).ToList();
        public ModelLayer Selected(string group) => Options(group).FirstOrDefault(IsOn);

        // Options are selected (the others in their group turn off); toggles flip.
        public void Activate(ModelLayer layer)
        {
            if(layer.Kind==LayerGroupKind.Option) foreach(var o in Options(layer.OptionGroup)) on[o]=o==layer;
            else on[layer]=!on[layer];
            Apply(); Save();
            DiagnosticsLog.Write($"Layer {(layer.Kind==LayerGroupKind.Option ? "option selected" : on[layer] ? "shown" : "hidden")}: {layer.Label}");
        }

        public ModelLayer NextOption(string group)
        {
            var options=Options(group);
            var index=options.FindIndex(IsOn);
            return options[(index+1)%options.Count];
        }

        void Apply()
        {
            foreach(var l in layers) if(l.Root && l.Root.activeSelf!=on[l]) l.Root.SetActive(on[l]);
        }

        void Save()
        {
            var text=new StringBuilder();
            foreach(var l in layers) text.Append(l.Key).Append('=').Append(on[l] ? '1' : '0').Append('\n');
            try { PlayerPrefs.SetString(prefsKey,text.ToString()); PlayerPrefs.Save(); } catch { }
        }

        // One line for the status panel, e.g. "Design: Renovation | Furniture: Layout B".
        public string Summary()
        {
            if(OptionGroups.Count==0) return null;
            return string.Join("  |  ",OptionGroups.Select(g=>$"{g}: {Selected(g)?.Name}"));
        }
    }
}
