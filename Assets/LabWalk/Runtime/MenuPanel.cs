using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LabWalk
{
    // The menu (View layers, Models, model review): a large panel floating in front of the user, drawn on top
    // of the model. Each row has a label and optional buttons; the View rows' buttons are Solid / Wire / Off
    // with the current one lit, plus "›" to open a folder. Rows are chosen by pointing with the right controller
    // or with the sticks; the focused row and button are highlighted.
    public sealed class MenuPanel
    {
        public enum Style { Plain, Solid, Wire, Off, Action }
        public sealed class Button { public string Text; public Style Style; public Action Press; }
        public sealed class Row
        {
            public string Label;
            public bool Dim;          // informational text
            public Action Open;       // pressing the label (folders, list entries); null: the label is not a target
            public bool ArrowSlot;    // keep room for a "›" button so button columns line up with folder rows
            public readonly List<Button> Buttons=new List<Button>();
        }

        public string Title="", Footer="";
        public readonly List<Row> Rows=new List<Row>();
        public int FocusRow, FocusColumn; // column -1 = the label
        public bool Visible { get; private set; }

        const float Width=0.70f, RowHeight=0.056f, TitleHeight=0.07f, FooterHeight=0.05f, Margin=0.022f;
        const float LabelTextHeight=0.026f, ButtonTextHeight=0.021f, ButtonHeight=0.042f;
        const int MaxRows=9, FontSize=64;
        static readonly Color Background=new Color(0.03f,0.045f,0.06f), RowFocus=new Color(0.13f,0.19f,0.26f), FocusOutline=new Color(0.55f,0.85f,1f);

        readonly Transform root;
        readonly Font font;
        readonly Material fontMaterial;
        readonly Shader overlay;
        readonly Dictionary<(Color,int),Material> materials=new Dictionary<(Color,int),Material>();
        readonly Transform background;
        readonly TextMesh title, footer;
        readonly List<MeshRenderer> quads=new List<MeshRenderer>();
        readonly List<TextMesh> texts=new List<TextMesh>();
        int first, quadsUsed, textsUsed;
        readonly List<(int row,int column,Rect rect)> targets=new List<(int,int,Rect)>();
        float height;

        public MenuPanel()
        {
            root=new GameObject("Menu panel").transform;
            font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            overlay=Resources.Load<Shader>("LabWalkOverlay");
            // Text on top of everything too: the built-in text shader honors unity_GUIZTestMode.
            fontMaterial=new Material(font.material) {renderQueue=4010};
            fontMaterial.SetInt("unity_GUIZTestMode",(int)CompareFunction.Always);
            background=CreateQuad("Background",Background,4000).transform;
            background.localPosition=new Vector3(0,0,0.01f);
            title=CreateText("Title",LabelTextHeight*1.05f);
            footer=CreateText("Footer",ButtonTextHeight*0.9f);
            root.gameObject.SetActive(false);
        }

        // Puts the panel in front of the head (slightly below eye level), facing it.
        public void Place(Transform head)
        {
            var forward=Vector3.ProjectOnPlane(head.forward,Vector3.up);
            if(forward.sqrMagnitude<1e-4f) forward=Vector3.ProjectOnPlane(head.up,Vector3.up);
            forward.Normalize();
            var position=head.position+forward*0.62f-Vector3.up*0.1f;
            root.SetPositionAndRotation(position,Quaternion.LookRotation(position-head.position,Vector3.up));
        }

        // Keeps the panel in view: if the user has turned or walked well away from it, bring it back in front.
        public void Follow(Transform head)
        {
            if(!Visible) return;
            var toPanel=root.position-head.position;
            if(Vector3.Angle(Vector3.ProjectOnPlane(head.forward,Vector3.up),Vector3.ProjectOnPlane(toPanel,Vector3.up))>55 || toPanel.magnitude>1.6f) Place(head);
        }

        public void Show(Transform head) { if(!Visible) Place(head); Visible=true; root.gameObject.SetActive(true); }
        public void Hide() { Visible=false; root.gameObject.SetActive(false); }

        int MinColumn(Row row) => row.Open!=null || row.Buttons.Count==0 ? -1 : 0;

        public void ClampFocus()
        {
            if(Rows.Count==0) { FocusRow=0; FocusColumn=-1; return; }
            FocusRow=Mathf.Clamp(FocusRow,0,Rows.Count-1);
            var row=Rows[FocusRow];
            FocusColumn=Mathf.Clamp(FocusColumn,MinColumn(row),row.Buttons.Count-1);
        }

        // Moves the focus: rows up/down (wrapping); left/right through the label and the buttons.
        public void Move(int rows,int columns)
        {
            if(Rows.Count==0) return;
            if(rows!=0) FocusRow=(FocusRow+rows+Rows.Count)%Rows.Count;
            if(columns!=0) FocusColumn+=columns;
            ClampFocus();
        }

        // Presses the focused label or button.
        public void Press()
        {
            if(FocusRow<0 || FocusRow>=Rows.Count) return;
            var row=Rows[FocusRow];
            if(FocusColumn>=0 && FocusColumn<row.Buttons.Count) row.Buttons[FocusColumn].Press?.Invoke();
            else row.Open?.Invoke();
        }

        // Where a ray meets the panel: the row and column under it (column -1 = label). Returns whether the ray
        // hits the panel at all; row is -1 when it is on the panel but not on a row.
        public bool Hit(Ray ray,out int row,out int column,out Vector3 point)
        {
            row=-1; column=-1; point=default;
            if(!Visible) return false;
            var plane=new Plane(-root.forward,root.position);
            if(!plane.Raycast(ray,out var distance) || distance<=0 || distance>5) return false;
            point=ray.GetPoint(distance);
            var local=root.InverseTransformPoint(point);
            if(Mathf.Abs(local.x)>Width/2 || Mathf.Abs(local.y)>height/2) return false;
            // Buttons are listed after their row's label area, so check them first.
            for(int i=targets.Count-1;i>=0;i--)
                if(targets[i].rect.Contains(new Vector2(local.x,local.y))) { row=targets[i].row; column=targets[i].column; return true; }
            return true;
        }

        // Rebuilds the visuals from Title, Rows, Footer and the focus.
        public void Render()
        {
            ClampFocus();
            if(FocusRow<first) first=FocusRow;
            if(FocusRow>=first+MaxRows) first=FocusRow-MaxRows+1;
            first=Mathf.Clamp(first,0,Mathf.Max(0,Rows.Count-MaxRows));
            var shown=Mathf.Min(MaxRows,Rows.Count);
            height=TitleHeight+shown*RowHeight+FooterHeight+Margin;
            var top=height/2;
            background.localScale=new Vector3(Width,height,1);
            SetText(title,Title+(Rows.Count>MaxRows ? $"   ({first+1}-{first+shown} of {Rows.Count})" : ""),-Width/2+Margin,top-TitleHeight/2,TextAnchor.MiddleLeft,Color.white,Width-2*Margin);
            SetText(footer,Footer,-Width/2+Margin,-top+FooterHeight/2+Margin/3,TextAnchor.MiddleLeft,new Color(0.65f,0.72f,0.8f),Width-2*Margin);
            quadsUsed=textsUsed=0; targets.Clear();
            for(int i=0;i<shown;i++)
            {
                var index=first+i; var row=Rows[index];
                var y=top-TitleHeight-(i+0.5f)*RowHeight;
                var focused=index==FocusRow;
                if(focused) PlaceQuad(0,y,Width-Margin,RowHeight-0.006f,RowFocus,4001);
                // Buttons right-aligned; the label takes the rest.
                var x=Width/2-Margin;
                if(row.ArrowSlot && (row.Buttons.Count==0 || row.Buttons[row.Buttons.Count-1].Text!="›")) x-=0.05f+0.008f;
                var rects=new Rect[row.Buttons.Count];
                for(int b=row.Buttons.Count-1;b>=0;b--)
                {
                    var length=row.Buttons[b].Text.Length;
                    var w=length<=2 ? 0.05f : length<=5 ? 0.088f : 0.03f+length*0.0135f;
                    rects[b]=new Rect(x-w,y-ButtonHeight/2,w,ButtonHeight);
                    x-=w+0.008f;
                }
                var labelLeft=-Width/2+Margin*1.4f;
                var labelWidth=x-labelLeft-0.01f;
                if(row.Open!=null) targets.Add((index,-1,new Rect(-Width/2,y-RowHeight/2,x+Width/2,RowHeight)));
                if(focused && FocusColumn<0 && row.Open!=null) PlaceQuad(labelLeft+labelWidth/2,y,labelWidth+0.016f,ButtonHeight+0.008f,FocusOutline,4002,0.35f);
                SetText(NextText(LabelTextHeight),row.Label,labelLeft,y,TextAnchor.MiddleLeft,row.Dim ? new Color(0.7f,0.75f,0.8f) : Color.white,labelWidth);
                for(int b=0;b<row.Buttons.Count;b++)
                {
                    var button=row.Buttons[b]; var r=rects[b];
                    targets.Add((index,b,r));
                    if(focused && FocusColumn==b) PlaceQuad(r.center.x,r.center.y,r.width+0.01f,r.height+0.01f,FocusOutline,4002);
                    var (fill,ink)=Colors(button.Style);
                    PlaceQuad(r.center.x,r.center.y,r.width,r.height,fill,4003);
                    SetText(NextText(ButtonTextHeight),button.Text,r.center.x,r.center.y,TextAnchor.MiddleCenter,ink,r.width-0.008f);
                }
            }
            for(int i=quadsUsed;i<quads.Count;i++) quads[i].gameObject.SetActive(false);
            for(int i=textsUsed;i<texts.Count;i++) texts[i].gameObject.SetActive(false);
        }

        static (Color fill,Color ink) Colors(Style style)
        {
            switch(style)
            {
                case Style.Solid: return (new Color(0.86f,0.88f,0.9f),new Color(0.04f,0.05f,0.06f));
                case Style.Wire: return (Rhino3dmModelLoader.WireColor,new Color(0.08f,0.04f,0f));
                case Style.Off: return (new Color(0.36f,0.38f,0.42f),Color.white);
                case Style.Action: return (new Color(0.16f,0.42f,0.66f),Color.white);
                default: return (new Color(0.1f,0.13f,0.16f),new Color(0.62f,0.68f,0.74f));
            }
        }

        // ---- Visual pieces (pooled) ----

        MeshRenderer CreateQuad(string name,Color color,int queue)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name=name; UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root,false);
            var renderer=go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial=MaterialFor(color,queue);
            return renderer;
        }

        Material MaterialFor(Color color,int queue)
        {
            if(!materials.TryGetValue((color,queue),out var m))
            {
                m=new Material(overlay ? overlay : Shader.Find("Unlit/Color")) {color=color,renderQueue=queue};
                materials[(color,queue)]=m;
            }
            return m;
        }

        TextMesh CreateText(string name,float textHeight)
        {
            var go=new GameObject(name);
            go.transform.SetParent(root,false);
            var text=go.AddComponent<TextMesh>();
            text.font=font; text.fontSize=FontSize; text.characterSize=textHeight*10/FontSize; text.richText=false;
            go.GetComponent<MeshRenderer>().sharedMaterial=fontMaterial;
            return text;
        }

        // alpha < 1: an outline-like tint (drawn behind the label).
        void PlaceQuad(float x,float y,float w,float h,Color color,int queue,float alpha=1)
        {
            if(alpha<1) color=Color.Lerp(RowFocus,color,alpha);
            if(quadsUsed==quads.Count) quads.Add(CreateQuad("Menu quad",color,queue));
            var quad=quads[quadsUsed++];
            quad.gameObject.SetActive(true);
            quad.sharedMaterial=MaterialFor(color,queue);
            quad.transform.localPosition=new Vector3(x,y,0.005f);
            quad.transform.localScale=new Vector3(w,h,1);
        }

        TextMesh NextText(float textHeight)
        {
            if(textsUsed==texts.Count) texts.Add(CreateText("Menu text",textHeight));
            var text=texts[textsUsed++];
            text.gameObject.SetActive(true);
            text.characterSize=textHeight*10/FontSize;
            return text;
        }

        // Long text is shrunk a little (to 85%), then shortened with an ellipsis, so rows keep a readable size.
        static void SetText(TextMesh text,string value,float x,float y,TextAnchor anchor,Color color,float maxWidth)
        {
            text.anchor=anchor; text.color=color; text.text=value ?? "";
            text.transform.localPosition=new Vector3(x,y,0);
            text.transform.localScale=Vector3.one;
            float Width() => text.GetComponent<Renderer>().localBounds.size.x;
            var width=Width();
            if(width<=maxWidth || width<=0) return;
            const float MinScale=0.85f;
            if(width*MinScale<=maxWidth) { text.transform.localScale=Vector3.one*(maxWidth/width); return; }
            text.transform.localScale=Vector3.one*MinScale;
            var full=text.text;
            for(int length=full.Length-1;length>1;length--)
            {
                text.text=full.Substring(0,length).TrimEnd()+"…";
                if(Width()*MinScale<=maxWidth) return;
            }
        }
    }
}
