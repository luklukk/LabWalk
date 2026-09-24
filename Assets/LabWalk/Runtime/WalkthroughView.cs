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
        public void FollowCamera()
        {
            if(!panel.gameObject.activeSelf) return;
            panel.position=camera.transform.TransformPoint(new Vector3(-0.46f,0.35f,1.2f));
            panel.rotation=camera.transform.rotation;
        }
        public void Pointer(Vector3 origin,Vector3 end,bool visible)
        { pointer.enabled=visible; pointer.SetPosition(0,origin); pointer.SetPosition(1,end); }
        public void Reference(Vector3 a,Vector3 b,bool visible)
        { reference.enabled=visible; reference.SetPosition(0,a+Vector3.up*0.015f); reference.SetPosition(1,b+Vector3.up*0.015f); }
        public void Measurement(Vector3 a,Vector3 b)
        { ruler.enabled=true; ruler.SetPosition(0,a+Vector3.up*0.02f); ruler.SetPosition(1,b+Vector3.up*0.02f); }
        public void ClearMeasurement() { ruler.enabled=false; }
    }
}
