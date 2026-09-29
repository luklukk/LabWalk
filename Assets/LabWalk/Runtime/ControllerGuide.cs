using System.Collections.Generic;
using UnityEngine;

namespace LabWalk
{
    // Button tooltips on the tracked controller models (Meta's OVRControllerPrefab under each controller anchor).
    // Each label sits in a column beside its controller with a thin leader line to the button, faces the viewer,
    // and is shown only while it has text, so the labels always describe what the buttons do right now.
    public sealed class ControllerGuide
    {
        public enum Control { Menu, Y, X, LeftStick, LeftTrigger, LeftGrip, B, A, RightStick, RightTrigger, RightGrip }

        sealed class Tip
        {
            public Control Control;
            public bool Left;
            public string NodeSuffix;
            public float Row;
            public Transform Node, Label;
            public TextMesh Text;
            public Transform Backing, Accent;
            public LineRenderer Line;
            public string Shown;
        }

        // Node names are the button bones of Meta's controller models (Touch Plus: "b_button_x", "left_b_thumbstick", ...).
        static readonly (Control control,bool left,string suffix,float row)[] Layout = {
            (Control.Menu,true,"b_button_oculus",0.072f), (Control.Y,true,"b_button_y",0.048f), (Control.X,true,"b_button_x",0.024f),
            (Control.LeftStick,true,"b_thumbstick",0.000f), (Control.LeftTrigger,true,"b_trigger_front",-0.024f), (Control.LeftGrip,true,"b_trigger_grip",-0.048f),
            (Control.B,false,"b_button_b",0.048f), (Control.A,false,"b_button_a",0.024f),
            (Control.RightStick,false,"b_thumbstick",0.000f), (Control.RightTrigger,false,"b_trigger_front",-0.024f), (Control.RightGrip,false,"b_trigger_grip",-0.048f),
        };
        const float ColumnOffset=0.085f, TextHeight=0.0095f;

        readonly Camera eye;
        readonly Transform leftAnchor, rightAnchor;
        readonly List<Tip> tips=new List<Tip>();
        readonly Dictionary<Control,string> labels=new Dictionary<Control,string>();

        public ControllerGuide(Camera eye,Transform leftAnchor,Transform rightAnchor,Material lineMaterial)
        {
            this.eye=eye; this.leftAnchor=leftAnchor; this.rightAnchor=rightAnchor;
            var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // Drawing-tag look (UiStyle): ink backing, paper text, a thin orange strip on the side facing the controller.
            var backingMaterial=new Material(Shader.Find("Unlit/Color")) {color=UiStyle.Ink};
            var accentMaterial=new Material(Shader.Find("Unlit/Color")) {color=UiStyle.Accent};
            foreach(var (control,left,suffix,row) in Layout)
            {
                var tip=new Tip {Control=control,Left=left,NodeSuffix=suffix,Row=row};
                tip.Label=new GameObject("Tooltip "+control).transform;
                var textObject=new GameObject("Text"); textObject.transform.SetParent(tip.Label,false);
                tip.Text=textObject.AddComponent<TextMesh>();
                tip.Text.font=font; textObject.GetComponent<MeshRenderer>().sharedMaterial=font.material;
                tip.Text.fontSize=48; tip.Text.characterSize=TextHeight*10/48; tip.Text.color=UiStyle.Paper; tip.Text.richText=false;
                tip.Text.anchor=left ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
                var back=GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.Destroy(back.GetComponent<Collider>());
                back.name="Backing"; back.transform.SetParent(tip.Label,false);
                back.GetComponent<Renderer>().sharedMaterial=backingMaterial;
                tip.Backing=back.transform;
                var accent=GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.Destroy(accent.GetComponent<Collider>());
                accent.name="Accent"; accent.transform.SetParent(tip.Label,false);
                accent.GetComponent<Renderer>().sharedMaterial=accentMaterial;
                tip.Accent=accent.transform;
                tip.Line=new GameObject("Leader").AddComponent<LineRenderer>();
                tip.Line.transform.SetParent(tip.Label,false);
                tip.Line.sharedMaterial=lineMaterial; tip.Line.positionCount=2; tip.Line.useWorldSpace=true;
                tip.Line.startWidth=tip.Line.endWidth=0.0012f;
                tip.Line.startColor=tip.Line.endColor=new Color(UiStyle.Rule.r,UiStyle.Rule.g,UiStyle.Rule.b,0.95f);
                tip.Label.gameObject.SetActive(false);
                tips.Add(tip);
            }
        }

        public void Clear() { labels.Clear(); }
        public void Set(Control control,string text) { if(!string.IsNullOrEmpty(text)) labels[control]=text; }

        // Called every frame after tracking updates.
        public void Update(bool visible)
        {
            UpdateModels();
            foreach(var tip in tips)
            {
                var anchor=tip.Left ? leftAnchor : rightAnchor;
                labels.TryGetValue(tip.Control,out var text);
                var tracked=anchor && OVRInput.GetControllerPositionTracked(tip.Left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch);
                var show=visible && tracked && !string.IsNullOrEmpty(text) && FindNode(tip,anchor);
                if(tip.Label.gameObject.activeSelf!=show) tip.Label.gameObject.SetActive(show);
                if(!show) continue;
                if(tip.Shown!=text)
                {
                    tip.Shown=text; tip.Text.text=text;
                    var size=tip.Text.GetComponent<Renderer>().localBounds.size;
                    tip.Backing.localScale=new Vector3(size.x+0.008f,size.y+0.004f,1);
                    tip.Backing.localPosition=new Vector3((tip.Left ? -1 : 1)*size.x/2,0,0.0005f);
                    tip.Accent.localScale=new Vector3(0.0012f,size.y+0.004f,1);
                    tip.Accent.localPosition=new Vector3((tip.Left ? 1 : -1)*0.0046f,0,0.0003f);
                }
                var side=tip.Left ? -1f : 1f;
                var position=anchor.TransformPoint(new Vector3(side*ColumnOffset,tip.Row,0));
                tip.Label.position=position;
                // Face the viewer, upright relative to the head so labels stay readable while the hand turns.
                tip.Label.rotation=Quaternion.LookRotation(position-eye.transform.position,eye.transform.up);
                tip.Line.SetPosition(0,tip.Node.position);
                tip.Line.SetPosition(1,position-tip.Label.right*side*0.002f);
            }
        }

        // The helper activates one model per controller type; find the button bone on the active one.
        // Button bones come from Meta's bundled models. When the system model is shown instead, the bundled
        // model is hidden but its Touch Plus bones still follow the controller, so labels keep their places.
        static bool FindNode(Tip tip,Transform anchor)
        {
            if(tip.Node && tip.Node.IsChildOf(anchor)) return true;
            tip.Node=null;
            Transform fallback=null;
            foreach(var t in anchor.GetComponentsInChildren<Transform>(true))
            {
                if(!t.name.EndsWith(tip.NodeSuffix,System.StringComparison.Ordinal)) continue;
                if(t.gameObject.activeInHierarchy) { tip.Node=t; break; }
                if(!fallback || (!IsTouchPlus(fallback) && IsTouchPlus(t))) fallback=t;
            }
            tip.Node=tip.Node ? tip.Node : fallback;
            return tip.Node;
        }

        static bool IsTouchPlus(Transform t)
        {
            for(var p=t; p; p=p.parent) if(p.name.Contains("TouchPlus")) return true;
            return false;
        }

        // Prefer the system's controller model (OVRRuntimeController, loaded from Horizon OS) over the bundled one.
        readonly Dictionary<Transform,bool> usingSystemModel=new Dictionary<Transform,bool>();
        public void UpdateModels()
        {
            foreach(var anchor in new[]{leftAnchor,rightAnchor})
            {
                if(!anchor) continue;
                var runtime=anchor.GetComponentInChildren<OVRRuntimeController>(true);
                var bundled=anchor.GetComponentInChildren<OVRControllerHelper>(true);
                var loaded=runtime && runtime.GetComponentsInChildren<Renderer>(false).Length>0;
                if(bundled && bundled.gameObject.activeSelf==loaded) bundled.gameObject.SetActive(!loaded);
                if(!usingSystemModel.TryGetValue(anchor,out var was) || was!=loaded)
                {
                    usingSystemModel[anchor]=loaded;
                    DiagnosticsLog.Write($"{anchor.name}: {(loaded ? "system controller model" : "bundled controller model")}");
                }
            }
        }
    }
}
