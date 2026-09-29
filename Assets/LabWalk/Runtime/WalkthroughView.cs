using System.Text.RegularExpressions;
using UnityEngine;

namespace LabWalk
{
    // What the app draws besides the model and the menu: the status panel (a small title block above the left
    // controller), the controller ray with its floor reticle, the pointed-item callout, and placement aids.
    public sealed class WalkthroughView
    {
        readonly Camera camera;
        readonly OVRPassthroughLayer passthrough;
        readonly LineRenderer pointer, reference, ruler;
        readonly Material lineMaterial;
        public bool Immersive { get; private set; }
        public Material LineMaterial => lineMaterial;

        public WalkthroughView(Camera camera, OVRPassthroughLayer passthrough)
        {
            this.camera=camera; this.passthrough=passthrough;
            lineMaterial=new Material(Shader.Find("Sprites/Default"));
            pointer=Line("Controller ray",UiStyle.Pointer,0.004f);
            reference=Line("Model reference A to B",new Color(1f,0.85f,0.25f),0.012f);
            ruler=Line("Floor measurement",new Color(0.45f,0.95f,0.55f),0.01f);
            BuildPanel();
            SetImmersive(false);
        }

        LineRenderer Line(string name,Color color,float width)
        {
            var line=new GameObject(name).AddComponent<LineRenderer>();
            line.sharedMaterial=lineMaterial; line.positionCount=2;
            line.startWidth=width; line.endWidth=width;
            line.startColor=color; line.endColor=color;
            line.useWorldSpace=true; line.enabled=false; return line;
        }

        public void SetImmersive(bool value)
        {
            Immersive=value;
            if(passthrough) passthrough.hidden=value;
            camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=value ? new Color(0.04f,0.055f,0.07f,1) : Color.clear;
        }

        // ---- Status panel: a title block (brand, model, what is happening) above the left controller ----

        const float PanelWidth=0.9f, Pad=0.035f, BodyTextHeight=0.036f, HandScale=0.22f;
        Transform panel, border, fill;
        TextMesh brand, model, title, body;
        float panelHeight=0.3f;
        string shown;

        void BuildPanel()
        {
            panel=new GameObject("Status panel").transform;
            border=UiStyle.Quad("Border",panel,UiStyle.Rule,4000).transform;
            fill=UiStyle.Quad("Fill",panel,UiStyle.Ink,4001).transform;
            brand=UiStyle.Text("Brand",panel,0.028f,UiStyle.Accent,TextAnchor.UpperLeft);
            brand.text=UiStyle.Spaced("Lab Walk");
            brand.transform.localPosition=new Vector3(Pad,-Pad,0);
            model=UiStyle.Text("Model",panel,0.028f,UiStyle.Muted,TextAnchor.UpperRight);
            model.transform.localPosition=new Vector3(PanelWidth-Pad,-Pad,0);
            title=UiStyle.Text("Title",panel,0.058f,UiStyle.Paper,TextAnchor.UpperLeft);
            title.transform.localPosition=new Vector3(Pad,-Pad-0.052f,0);
            UiStyle.DimensionRule(panel,Pad,PanelWidth-Pad,-Pad-0.14f,0.004f,UiStyle.Rule,4002);
            body=UiStyle.Text("Body",panel,BodyTextHeight,UiStyle.Paper,TextAnchor.UpperLeft);
            body.transform.localPosition=new Vector3(Pad,-Pad-0.17f,0);
            panel.gameObject.SetActive(false);
        }

        public void UpdatePanel(string modelName,string heading,string content,bool visible)
        {
            panel.gameObject.SetActive(visible);
            if(!visible) return;
            var key=modelName+"\u0001"+heading+"\u0001"+content;
            if(key!=shown)
            {
                shown=key;
                model.text=modelName ?? "";
                Fit(model,PanelWidth*0.45f);
                title.text=heading ?? "";
                Fit(title,PanelWidth-2*Pad);
                body.text=content ?? "";
                body.transform.localScale=Vector3.one;
                var size=body.GetComponent<Renderer>().localBounds.size;
                if(size.x>PanelWidth-2*Pad && size.x>0) body.transform.localScale=Vector3.one*((PanelWidth-2*Pad)/size.x);
                var bodyHeight=string.IsNullOrEmpty(content) ? 0 : size.y*body.transform.localScale.y+0.02f;
                panelHeight=Pad+0.165f+bodyHeight+Pad*0.6f;
                border.localPosition=new Vector3(PanelWidth/2,-panelHeight/2,0.01f); border.localScale=new Vector3(PanelWidth,panelHeight,1);
                fill.localPosition=new Vector3(PanelWidth/2,-panelHeight/2,0.009f); fill.localScale=new Vector3(PanelWidth-0.008f,panelHeight-0.008f,1);
            }
            FollowCamera();
        }

        static void Fit(TextMesh text,float maxWidth)
        {
            text.transform.localScale=Vector3.one;
            var width=text.GetComponent<Renderer>().localBounds.size.x;
            if(width>maxWidth && width>0) text.transform.localScale=Vector3.one*(maxWidth/width);
        }

        // The status panel rides above the left controller when it is tracked (scaled to hand distance),
        // otherwise it floats in front of the head so messages are never lost.
        Transform panelAnchor;
        public void MountPanel(Transform leftControllerAnchor) { panelAnchor=leftControllerAnchor; }

        public void FollowCamera()
        {
            if(!panel.gameObject.activeSelf) return;
            var head=camera.transform;
            float scale; Vector3 center;
            if(panelAnchor && OVRInput.GetControllerPositionTracked(OVRInput.Controller.LTouch))
            { scale=HandScale; center=panelAnchor.position+head.up*(panelHeight*scale/2+0.1f); } // clear of the button labels
            else
            { scale=0.5f; center=head.TransformPoint(new Vector3(-0.25f,0.12f,0.9f)); }
            var rotation=Quaternion.LookRotation(center-head.position,head.up);
            panel.localScale=Vector3.one*scale;
            panel.rotation=rotation;
            panel.position=center-rotation*new Vector3(PanelWidth/2,-panelHeight/2,0)*scale;
        }

        // ---- Controller ray: to the floor (with a survey-point reticle where the trigger would record),
        // to a pointed item or the menu, or a short fading beam ----

        LineRenderer floorRing, crossA, crossB;
        public void Pointer(Vector3 origin,Vector3 end,bool visible,bool onFloor)
        {
            pointer.enabled=visible;
            if(!floorRing)
            {
                floorRing=Line("Floor reticle",UiStyle.Pointer,0.004f);
                floorRing.positionCount=40; floorRing.loop=true;
                crossA=Line("Floor reticle cross",UiStyle.Pointer,0.003f);
                crossB=Line("Floor reticle cross",UiStyle.Pointer,0.003f);
            }
            floorRing.enabled=crossA.enabled=crossB.enabled=visible && onFloor;
            if(!visible) return;
            pointer.SetPosition(0,origin); pointer.SetPosition(1,end);
            pointer.startColor=UiStyle.Pointer;
            pointer.endColor=onFloor ? UiStyle.Pointer : new Color(UiStyle.Pointer.r,UiStyle.Pointer.g,UiStyle.Pointer.b,0);
            if(!onFloor) return;
            // Reticle size grows with distance so it stays visible far away.
            var radius=Mathf.Clamp(Vector3.Distance(origin,end)*0.012f,0.02f,0.15f);
            var lift=Vector3.up*0.005f;
            for(int i=0;i<40;i++)
            {
                var a=i*Mathf.PI*2/40;
                floorRing.SetPosition(i,end+lift+new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius));
            }
            crossA.SetPosition(0,end+lift+Vector3.right*radius*1.7f); crossA.SetPosition(1,end+lift-Vector3.right*radius*1.7f);
            crossB.SetPosition(0,end+lift+Vector3.forward*radius*1.7f); crossB.SetPosition(1,end+lift-Vector3.forward*radius*1.7f);
        }

        public void Reference(Vector3 a,Vector3 b,bool visible)
        { reference.enabled=visible; reference.SetPosition(0,a+Vector3.up*0.015f); reference.SetPosition(1,b+Vector3.up*0.015f); }
        public void Measurement(Vector3 a,Vector3 b)
        { ruler.enabled=true; ruler.SetPosition(0,a+Vector3.up*0.02f); ruler.SetPosition(1,b+Vector3.up*0.02f); }
        public void ClearMeasurement() { ruler.enabled=false; }

        // ---- Pointed item: a drawing callout with a leader line and a keynote tag ----
        // Names like "W201 SawStop aligned cabinet saw" show the tag (W201) in an outlined box, as on a drawing.

        static readonly Regex TagPattern=new Regex(@"^([A-Z]{1,4}\d{1,4}[A-Z]?)\s+(.+)$");
        Transform callout, calloutBorder, calloutFill, tagBorder, tagFill, dot;
        TextMesh tagText, nameText, pathText;
        LineRenderer leader;
        string calloutShown;
        Vector2 calloutSize;

        public void Hover(string name,string path,string action,Vector3 position,bool visible)
        {
            if(!visible) { if(callout) { callout.gameObject.SetActive(false); leader.enabled=false; dot.gameObject.SetActive(false); } return; }
            if(!callout) BuildCallout();
            callout.gameObject.SetActive(true); leader.enabled=true; dot.gameObject.SetActive(true);
            var key=name+"\u0001"+path+"\u0001"+action;
            if(key!=calloutShown) { calloutShown=key; LayoutCallout(name,path,action); }
            var head=camera.transform;
            var distance=Mathf.Clamp(Vector3.Distance(head.position,position),0.3f,8f);
            // The card sits up and to the right of the pointed spot; the leader runs from the spot to its corner.
            var corner=position+(head.right*0.05f+head.up*0.07f)*distance;
            callout.localScale=Vector3.one*distance;
            callout.rotation=Quaternion.LookRotation(corner-head.position,head.up);
            callout.position=corner;
            leader.startWidth=leader.endWidth=0.0015f*distance;
            leader.SetPosition(0,position); leader.SetPosition(1,corner);
            dot.position=position; dot.rotation=callout.rotation; dot.localScale=Vector3.one*0.008f*distance;
        }

        void BuildCallout()
        {
            callout=new GameObject("Pointed item callout").transform;
            calloutBorder=UiStyle.Quad("Border",callout,UiStyle.Rule,4000).transform;
            calloutFill=UiStyle.Quad("Fill",callout,UiStyle.Ink,4001).transform;
            tagBorder=UiStyle.Quad("Tag border",callout,UiStyle.Accent,4002).transform;
            tagFill=UiStyle.Quad("Tag fill",callout,UiStyle.Ink,4003).transform;
            tagText=UiStyle.Text("Tag",callout,0.021f,UiStyle.Accent,TextAnchor.MiddleCenter);
            nameText=UiStyle.Text("Name",callout,0.024f,UiStyle.Paper,TextAnchor.MiddleLeft);
            pathText=UiStyle.Text("Path",callout,0.017f,UiStyle.Muted,TextAnchor.MiddleLeft);
            leader=new GameObject("Callout leader").AddComponent<LineRenderer>();
            leader.sharedMaterial=UiStyle.Overlay(UiStyle.Paper,3999); leader.positionCount=2; leader.useWorldSpace=true;
            dot=UiStyle.Quad("Callout dot",null,UiStyle.Accent,4004).transform;
        }

        void LayoutCallout(string name,string path,string action)
        {
            const float pad=0.011f;
            var match=TagPattern.Match(name ?? "");
            var tag=match.Success ? match.Groups[1].Value : null;
            var description=match.Success ? match.Groups[2].Value : name;
            float x=pad;
            tagBorder.gameObject.SetActive(tag!=null); tagFill.gameObject.SetActive(tag!=null); tagText.gameObject.SetActive(tag!=null);
            var line1=0.047f; // center of the first line, measured from the card's bottom edge
            if(tag!=null)
            {
                tagText.text=tag;
                var w=tagText.GetComponent<Renderer>().localBounds.size.x+0.014f; const float h=0.032f;
                tagBorder.localPosition=new Vector3(x+w/2,line1,0.002f); tagBorder.localScale=new Vector3(w,h,1);
                tagFill.localPosition=new Vector3(x+w/2,line1,0.001f); tagFill.localScale=new Vector3(w-0.003f,h-0.003f,1);
                tagText.transform.localPosition=new Vector3(x+w/2,line1,0);
                x+=w+0.008f;
            }
            nameText.text=description ?? "";
            nameText.transform.localPosition=new Vector3(x,line1,0);
            pathText.text=string.IsNullOrEmpty(action) ? path : path+"    ·    "+action;
            pathText.transform.localPosition=new Vector3(pad,0.017f,0);
            var width=Mathf.Max(x+nameText.GetComponent<Renderer>().localBounds.size.x,pad+pathText.GetComponent<Renderer>().localBounds.size.x)+pad;
            calloutSize=new Vector2(width,0.068f);
            calloutBorder.localPosition=new Vector3(width/2,calloutSize.y/2,0.004f); calloutBorder.localScale=new Vector3(width,calloutSize.y,1);
            calloutFill.localPosition=new Vector3(width/2,calloutSize.y/2,0.003f); calloutFill.localScale=new Vector3(width-0.002f,calloutSize.y-0.002f,1);
        }

        // ---- Placement aids: an orange cross where the placed model expects each QR code, a blue one where
        // the camera sees it ----

        readonly System.Collections.Generic.List<LineRenderer> crosses=new System.Collections.Generic.List<LineRenderer>();
        int crossesUsed;
        public void BeginMarkers() { crossesUsed=0; }
        public void Marker(Vector3 position,bool seen)
        {
            var color=seen ? UiStyle.Pointer : UiStyle.Accent; var size=seen ? 0.06f : 0.1f;
            foreach(var axis in new[]{Vector3.right,Vector3.up,Vector3.forward})
            {
                if(crossesUsed==crosses.Count) crosses.Add(Line("Marker cross",color,0.006f));
                var line=crosses[crossesUsed++];
                line.startColor=color; line.endColor=color; line.enabled=true;
                line.SetPosition(0,position-axis*size); line.SetPosition(1,position+axis*size);
            }
        }
        public void EndMarkers() { for(int i=crossesUsed;i<crosses.Count;i++) crosses[i].enabled=false; }
    }
}
