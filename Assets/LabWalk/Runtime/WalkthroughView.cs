using UnityEngine;

namespace LabWalk
{
    public sealed class WalkthroughView
    {
        readonly Camera camera;
        readonly OVRPassthroughLayer passthrough;
        readonly TextMesh text;
        readonly Transform panel;
        readonly LineRenderer pointer, reference, ruler;
        readonly Material lineMaterial;
        public bool Immersive { get; private set; }

        public WalkthroughView(Camera camera, OVRPassthroughLayer passthrough)
        {
            this.camera=camera; this.passthrough=passthrough;
            panel=new GameObject("Status panel").transform;
            var label=new GameObject("Text"); label.transform.SetParent(panel,false);
            text=label.AddComponent<TextMesh>();
            text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.GetComponent<MeshRenderer>().sharedMaterial=text.font.material;
            text.fontSize=48; text.characterSize=0.0065f;
            text.anchor=TextAnchor.UpperLeft; text.color=Color.white; text.richText=false;
            // A dark backing keeps status legible against both passthrough and model geometry.
            var back=GameObject.CreatePrimitive(PrimitiveType.Quad);
            back.name="Panel backing"; Object.Destroy(back.GetComponent<Collider>());
            back.transform.SetParent(panel,false);
            back.transform.localPosition=new Vector3(0.43f,-0.29f,0.008f);
            back.transform.localScale=new Vector3(0.91f,0.64f,1);
            var background=new Material(Shader.Find("Unlit/Color"));
            background.color=new Color(0.025f,0.04f,0.055f);
            back.GetComponent<Renderer>().sharedMaterial=background;
            lineMaterial=new Material(Shader.Find("Sprites/Default"));
            pointer=Line("Floor pointer",Color.cyan,0.004f);
            reference=Line("Model reference A to B",Color.yellow,0.015f);
            ruler=Line("Floor measurement",Color.green,0.012f);
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
        public void UpdatePanel(string content,bool visible)
        {
            panel.gameObject.SetActive(visible);
            if(!visible) return;
            text.text=content;
            var size=text.GetComponent<Renderer>().localBounds.size;
            if(size.x>0 && size.y>0) text.transform.localScale=Vector3.one*Mathf.Min(0.86f/size.x,0.58f/size.y);
            FollowCamera();
        }
        public Material LineMaterial => lineMaterial;

        // The status panel rides above the left controller when it is tracked (scaled to hand distance),
        // otherwise it falls back to floating in front of the head so messages are never lost.
        Transform panelAnchor;
        public void MountPanel(Transform leftControllerAnchor) { panelAnchor=leftControllerAnchor; }

        public void FollowCamera()
        {
            if(!panel.gameObject.activeSelf) return;
            var head=camera.transform;
            if(panelAnchor && OVRInput.GetControllerPositionTracked(OVRInput.Controller.LTouch))
            {
                const float scale=0.22f;
                // Backing center is at (0.43,-0.29) in panel space; put its lower edge above the tooltips' top row.
                var center=panelAnchor.position+head.up*(0.32f*scale+0.08f);
                var rotation=Quaternion.LookRotation(center-head.position,head.up);
                panel.localScale=Vector3.one*scale;
                panel.rotation=rotation;
                panel.position=center-rotation*new Vector3(0.43f,-0.29f,0)*scale;
                return;
            }
            panel.localScale=Vector3.one;
            panel.position=head.TransformPoint(new Vector3(-0.46f,0.35f,1.2f));
            panel.rotation=head.rotation;
        }
        // The controller ray is always shown while the controller is tracked. On the floor it runs to the hit point
        // and draws a ring there (the trigger records that point); otherwise it is a short ray that fades out.
        LineRenderer floorRing;
        public void Pointer(Vector3 origin,Vector3 end,bool visible,bool onFloor)
        {
            pointer.enabled=visible;
            if(!floorRing)
            {
                floorRing=Line("Floor pointer ring",Color.cyan,0.004f);
                floorRing.positionCount=32; floorRing.loop=true;
            }
            floorRing.enabled=visible && onFloor;
            if(!visible) return;
            pointer.SetPosition(0,origin); pointer.SetPosition(1,end);
            pointer.startColor=Color.cyan;
            pointer.endColor=onFloor ? Color.cyan : new Color(0,1,1,0);
            if(!onFloor) return;
            // Ring radius grows with distance so it stays visible far away.
            var radius=Mathf.Clamp(Vector3.Distance(origin,end)*0.012f,0.02f,0.15f);
            for(int i=0;i<32;i++)
            {
                var a=i*Mathf.PI*2/32;
                floorRing.SetPosition(i,end+new Vector3(Mathf.Cos(a)*radius,0.005f,Mathf.Sin(a)*radius));
            }
        }
        public void Reference(Vector3 a,Vector3 b,bool visible)
        { reference.enabled=visible; reference.SetPosition(0,a+Vector3.up*0.015f); reference.SetPosition(1,b+Vector3.up*0.015f); }
        public void Measurement(Vector3 a,Vector3 b)
        { ruler.enabled=true; ruler.SetPosition(0,a+Vector3.up*0.02f); ruler.SetPosition(1,b+Vector3.up*0.02f); }
        public void ClearMeasurement() { ruler.enabled=false; }

        // Calibration markers: magenta cross where the placed model expects each QR code, cyan where it is seen.
        readonly System.Collections.Generic.List<LineRenderer> crosses=new System.Collections.Generic.List<LineRenderer>();
        int crossesUsed;
        public void BeginMarkers() { crossesUsed=0; }
        public void Marker(Vector3 position,bool seen)
        {
            var color=seen ? Color.cyan : Color.magenta; var size=seen ? 0.06f : 0.1f;
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
