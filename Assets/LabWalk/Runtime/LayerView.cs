using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace LabWalk
{
    // How the model's layer tree and individual objects are shown.
    // Each layer has its own setting: Solid, Wireframe or Off. A layer is shown at most as much as its parent:
    // a folder set to Wireframe draws everything inside it as wireframe, Off hides it, Solid lets each child decide.
    // "Option:" layers in the same group are exclusive while solid: setting one Solid turns the other solid ones off
    // (wireframes stay, so one design can be overlaid on another). Separately, single objects can be switched to
    // wireframe by pointing at them. All choices are remembered per model version (fingerprint) on this headset.
    public sealed class LayerView
    {
        public enum Display { Hidden=0, Wireframe=1, Solid=2 }

        readonly List<ModelLayer> layers;
        readonly List<ModelObject> objects;
        readonly Dictionary<ModelLayer,Display> own=new Dictionary<ModelLayer,Display>();
        readonly HashSet<int> wireObjects=new HashSet<int>();
        readonly string prefsKey;
        int hovered;

        public IReadOnlyList<ModelLayer> Layers => layers;
        public IReadOnlyList<string> OptionGroups { get; }
        public List<ModelLayer> Roots => layers.Where(l=>l.Parent==null || !own.ContainsKey(l.Parent)).OrderBy(l=>l.Order).ToList();
        public bool Any => layers.Count>0;
        public int WireObjectCount => wireObjects.Count;

        public LayerView(LoadedModel model,string fingerprint)
        {
            layers=model!=null ? model.Layers : new List<ModelLayer>();
            objects=model!=null ? model.Objects : new List<ModelObject>{ null };
            OptionGroups=layers.Where(l=>l.Kind==LayerGroupKind.Option).Select(l=>l.OptionGroup).Distinct().ToList();
            prefsKey="LabWalk.layers."+(fingerprint ?? "none");
            foreach(var l in layers) own[l]=l.DefaultOn ? Display.Solid : Display.Hidden;
            var remembered=false;
            try
            {
                // Lines of "<key>=<s|w|o>". Older versions wrote "1", "1w" (on), "0" (off), "0w" (option overlay).
                foreach(var entry in PlayerPrefs.GetString(prefsKey,"").Split('\n'))
                {
                    var split=entry.LastIndexOf('=');
                    if(split<=0) continue;
                    var layer=layers.FirstOrDefault(l=>l.Key==entry.Substring(0,split));
                    if(layer==null) continue;
                    var value=entry.Substring(split+1);
                    own[layer]=value=="s" || value=="1" ? Display.Solid : value=="w" || value.EndsWith("w") ? Display.Wireframe : Display.Hidden;
                    if(own[layer]==Display.Wireframe && !HasWireframe(layer)) own[layer]=Display.Solid;
                    remembered=true;
                }
                var ids=new HashSet<string>(PlayerPrefs.GetString(prefsKey+".objects","").Split('\n').Where(s=>s.Length>0));
                foreach(var o in objects) if(o!=null && ids.Contains(o.Id)) wireObjects.Add(o.Index);
            }
            catch { } // a damaged preference only loses remembered choices
            foreach(var group in OptionGroups)
            {
                // At most one solid option; with nothing remembered, show one.
                var options=Options(group);
                var solid=options.Where(o=>own[o]==Display.Solid).ToList();
                foreach(var extra in solid.Skip(1)) own[extra]=Display.Hidden;
                if(!remembered && options.All(o=>own[o]==Display.Hidden)) own[options[0]]=Display.Solid;
            }
            BuildStateTexture();
            Apply();
        }

        public List<ModelLayer> Options(string group) => layers.Where(l=>l.Kind==LayerGroupKind.Option && l.OptionGroup==group).ToList();
        public ModelLayer Selected(string group) => Options(group).FirstOrDefault(o=>own[o]==Display.Solid) ?? Options(group).FirstOrDefault(o=>own[o]==Display.Wireframe);
        public Display Own(ModelLayer layer) => own.TryGetValue(layer,out var d) ? d : Display.Hidden;

        // What the layer's own geometry shows: its setting, limited by every enclosing layer.
        public Display Effective(ModelLayer layer)
        {
            var d=Own(layer);
            for(var p=layer.Parent; p!=null && own.ContainsKey(p); p=p.Parent) if(own[p]<d) d=own[p];
            return d;
        }

        // Whether the layer or anything inside it has lines to draw as a wireframe.
        public static bool HasWireframe(ModelLayer layer) => layer.HasWireframe || layer.Children.Any(HasWireframe);

        public void Set(ModelLayer layer,Display display)
        {
            if(display==Display.Wireframe && !HasWireframe(layer)) return;
            own[layer]=display;
            if(layer.Kind==LayerGroupKind.Option && display==Display.Solid)
                foreach(var o in Options(layer.OptionGroup)) if(o!=layer && own[o]==Display.Solid) own[o]=Display.Hidden;
            Apply(); Save();
            DiagnosticsLog.Write($"Layer {layer.Path}: {display}");
        }

        // Left stick click: the next design option becomes the solid one.
        public ModelLayer NextOption(string group)
        {
            var options=Options(group);
            var index=options.FindIndex(o=>own[o]==Display.Solid);
            return options[(index+1)%options.Count];
        }

        // ---- Single objects switched to wireframe by pointing at them ----

        public bool IsObjectWire(int index) => wireObjects.Contains(index);
        public ModelObject Object(int index) => index>0 && index<objects.Count ? objects[index] : null;

        public void ToggleObject(int index)
        {
            var o=Object(index);
            if(o==null) return;
            if(!wireObjects.Remove(index)) wireObjects.Add(index);
            SetTexel(index); Apply(); Save();
            DiagnosticsLog.Write($"Object {o.Label} ({o.Layer?.Path}): {(wireObjects.Contains(index) ? "wireframe" : "solid")}");
        }

        public void ClearObjects()
        {
            if(wireObjects.Count==0) return;
            var cleared=wireObjects.ToList();
            wireObjects.Clear();
            foreach(var index in cleared) SetTexel(index);
            Apply(); Save();
            DiagnosticsLog.Write($"Cleared {cleared.Count} object wireframes");
        }

        public void SetHover(int index)
        {
            if(index==hovered) return;
            var previous=hovered; hovered=index;
            SetTexel(previous); SetTexel(index);
        }

        // One texel per object index, read by the model shaders (LabWalkObjects.cginc).
        Texture2D stateTexture;
        Color32[] texels;
        const int TextureWidth=256;
        static Texture2D emptyState;

        void BuildStateTexture()
        {
            var height=Mathf.Max(1,(objects.Count+TextureWidth-1)/TextureWidth);
            stateTexture=new Texture2D(TextureWidth,height,TextureFormat.RGBA32,false,true) {filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,name="Lab Walk object state"};
            texels=new Color32[TextureWidth*height];
            foreach(var index in wireObjects) texels[index]=Texel(index);
            stateTexture.SetPixels32(texels); stateTexture.Apply(false);
            Shader.SetGlobalTexture("_LabWalkObjectState",stateTexture);
            Shader.SetGlobalVector("_LabWalkObjectStateSize",new Vector4(TextureWidth,height,0,0));
        }

        Color32 Texel(int index) => new Color32(wireObjects.Contains(index) ? (byte)255 : (byte)0,index==hovered && index>0 ? (byte)255 : (byte)0,0,0);

        void SetTexel(int index)
        {
            if(stateTexture==null || index<=0 || index>=texels.Length) return;
            texels[index]=Texel(index);
            stateTexture.SetPixel(index%TextureWidth,index/TextureWidth,texels[index]);
            stateTexture.Apply(false);
        }

        // Before any model loads (and for models without object data) no object is hidden or highlighted.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ClearGlobalState()
        {
            if(!emptyState) { emptyState=new Texture2D(1,1,TextureFormat.RGBA32,false,true) {name="Lab Walk no object state"}; emptyState.SetPixel(0,0,new Color(0,0,0,0)); emptyState.Apply(false); }
            Shader.SetGlobalTexture("_LabWalkObjectState",emptyState);
            Shader.SetGlobalVector("_LabWalkObjectStateSize",new Vector4(1,1,0,0));
        }

        public void Release()
        {
            if(stateTexture) { if(Application.isPlaying) UnityEngine.Object.Destroy(stateTexture); else UnityEngine.Object.DestroyImmediate(stateTexture); }
            ClearGlobalState();
        }

        // ---- Applying and remembering ----

        static readonly int WireAll=Shader.PropertyToID("_WireAll");
        MaterialPropertyBlock block;

        void Apply()
        {
            block ??= new MaterialPropertyBlock();
            foreach(var l in layers)
            {
                var display=Effective(l);
                if(l.Root && l.Root.activeSelf!=(display!=Display.Hidden)) l.Root.SetActive(display!=Display.Hidden);
                foreach(var r in l.Solid) if(r) r.enabled=display==Display.Solid;
                for(int i=0;i<l.Wire.Count;i++)
                {
                    var w=l.Wire[i]; if(!w) continue;
                    // Solid layers draw lines only for objects switched to wireframe by pointing.
                    var pointed=display==Display.Solid && i<l.WireObjects.Count && l.WireObjects[i].Overlaps(wireObjects);
                    var active=display==Display.Wireframe || pointed;
                    if(w.activeSelf!=active) w.SetActive(active);
                    if(!active) continue;
                    var renderer=w.GetComponent<Renderer>();
                    block.SetFloat(WireAll,display==Display.Wireframe ? 1 : 0);
                    renderer.SetPropertyBlock(block);
                }
            }
        }

        void Save()
        {
            var text=new StringBuilder();
            foreach(var l in layers) text.Append(l.Key).Append('=').Append(own[l]==Display.Solid ? "s" : own[l]==Display.Wireframe ? "w" : "o").Append('\n');
            var ids=string.Join("\n",wireObjects.Select(i=>Object(i)?.Id).Where(id=>id!=null));
            try { PlayerPrefs.SetString(prefsKey,text.ToString()); PlayerPrefs.SetString(prefsKey+".objects",ids); PlayerPrefs.Save(); } catch { }
        }

        // One line for the status panel, e.g. "Design: Renovation + Existing wireframe | 2 objects wireframe".
        public string Summary()
        {
            var parts=OptionGroups.Select(g =>
            {
                var options=Options(g);
                var solid=options.Where(o=>own[o]==Display.Solid).Select(o=>o.Name).ToList();
                var wire=options.Where(o=>own[o]==Display.Wireframe).Select(o=>o.Name).ToList();
                var text=solid.Count>0 ? string.Join(", ",solid) : "nothing solid";
                return $"{g}: {text}{(wire.Count>0 ? " + "+string.Join(", ",wire)+" wireframe" : "")}";
            }).ToList();
            if(wireObjects.Count>0) parts.Add($"{wireObjects.Count} object{(wireObjects.Count==1 ? "" : "s")} wireframe");
            return parts.Count==0 ? null : string.Join("  |  ",parts);
        }
    }
}
