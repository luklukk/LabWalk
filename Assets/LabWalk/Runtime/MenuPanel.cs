using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LabWalk
{
    // The menu (View layers, Models, model review), drawn on top of the model. It rides above the left
    // controller; its title-bar button (or Y) pins it in the room at eye level, where it stays put until it is
    // sent back to the hand. Each row has a label and optional buttons; the View rows' buttons are Solid / Wire /
    // Off with the current one lit, plus "›" to open a folder. Rows are chosen by pointing with the right
    // controller or with the sticks; the focused row and button are highlighted.
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
        public bool Pinned { get; private set; }  // placed in the room instead of riding on the hand
        public bool PinFocused;                    // the title-bar pin button is pointed at
        public const int PinRow=-2;                // Hit's row for the title-bar pin button
        const float HandScale=0.55f;

        const float Width=0.70f, RowHeight=0.056f, TitleHeight=0.105f, FooterHeight=0.05f, Margin=0.022f;
        const float LabelTextHeight=0.026f, ButtonTextHeight=0.021f, ButtonHeight=0.042f;
        const int MaxRows=9, FontSize=UiStyle.FontSize;
        static readonly Color Background=UiStyle.Ink, RowFocus=UiStyle.InkRaised, FocusOutline=UiStyle.Pointer;

        readonly Transform root;
        readonly Font font;
        readonly Material fontMaterial;
        readonly Shader overlay;
        readonly Dictionary<(Color,int),Material> materials=new Dictionary<(Color,int),Material>();
        readonly Transform background, border, rule, tickLeft, tickRight, footerRule;
        readonly TextMesh brand, title, footer;
        readonly List<MeshRenderer> quads=new List<MeshRenderer>();
        readonly List<TextMesh> texts=new List<TextMesh>();
        int first, quadsUsed, textsUsed;
        readonly List<(int row,int column,Rect rect)> targets=new List<(int,int,Rect)>();
        Rect pinRect;
        float height=0.4f;

        public MenuPanel()
        {
            root=new GameObject("Menu panel").transform;
            font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            overlay=Resources.Load<Shader>("LabWalkOverlay");
            // Text on top of everything too: the built-in text shader honors unity_GUIZTestMode.
            fontMaterial=new Material(font.material) {renderQueue=4010};
            fontMaterial.SetInt("unity_GUIZTestMode",(int)CompareFunction.Always);
            // Drawing-sheet frame: thin border, brand line, title, and a rule with 45° ticks like a dimension line.
            border=CreateQuad("Border",UiStyle.Rule,3999).transform;
            border.localPosition=new Vector3(0,0,0.011f);
            background=CreateQuad("Background",Background,4000).transform;
            background.localPosition=new Vector3(0,0,0.01f);
            rule=CreateQuad("Title rule",UiStyle.Rule,4001).transform;
            tickLeft=CreateQuad("Title rule tick",UiStyle.Rule,4001).transform;
            tickRight=CreateQuad("Title rule tick",UiStyle.Rule,4001).transform;
            footerRule=CreateQuad("Footer rule",UiStyle.Rule,4001).transform;
            brand=CreateText("Brand",0.017f);
            brand.text=UiStyle.Spaced("Lab Walk"); brand.color=UiStyle.Accent; brand.anchor=TextAnchor.MiddleLeft;
            title=CreateText("Title",LabelTextHeight*1.05f);
            footer=CreateText("Footer",ButtonTextHeight*0.9f);
            root.gameObject.SetActive(false);
        }

        // Pins the panel in the room in front of the head at eye level, upright and facing it. It stays there.
        public void Pin(Transform head)
        {
            var forward=Vector3.ProjectOnPlane(head.forward,Vector3.up);
            if(forward.sqrMagnitude<1e-4f) forward=Vector3.ProjectOnPlane(head.up,Vector3.up);
            forward.Normalize();
            var position=head.position+forward*0.62f;
            root.localScale=Vector3.one;
            root.SetPositionAndRotation(position,Quaternion.LookRotation(position-head.position,Vector3.up));
            Pinned=true;
        }

        public void Unpin() { Pinned=false; }

        // While not pinned: rides above the hand (left controller), scaled down, facing the head.
        public void FollowHand(Transform hand,Transform head)
        {
            if(!Visible || Pinned || !hand) return;
            root.localScale=Vector3.one*HandScale;
            var center=hand.position+head.up*(height*HandScale/2+0.1f); // clear of the controller's button labels
            root.SetPositionAndRotation(center,Quaternion.LookRotation(center-head.position,head.up));
        }

        public void Show() { Visible=true; root.gameObject.SetActive(true); }
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
            if(pinRect.Contains(new Vector2(local.x,local.y))) { row=PinRow; column=0; return true; }
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
            background.localScale=new Vector3(Width-0.004f,height-0.004f,1);
            border.localScale=new Vector3(Width,height,1);
            brand.transform.localPosition=new Vector3(-Width/2+Margin,top-0.024f,0);
            var ruleY=top-TitleHeight+0.008f;
            rule.localPosition=new Vector3(0,ruleY,0.004f); rule.localScale=new Vector3(Width-2*Margin,0.0022f,1);
            foreach(var (tick,x) in new[]{(tickLeft,-Width/2+Margin),(tickRight,Width/2-Margin)})
            { tick.localPosition=new Vector3(x,ruleY,0.004f); tick.localRotation=Quaternion.Euler(0,0,45); tick.localScale=new Vector3(0.003f,0.02f,1); }
            footerRule.localPosition=new Vector3(0,-top+FooterHeight+Margin/3-0.004f,0.004f); footerRule.localScale=new Vector3(Width-2*Margin,0.0012f,1);
            quadsUsed=textsUsed=0; targets.Clear();
            // Title-bar button: pin in the room / back to the hand.
            var pinText=Pinned ? "To hand" : "Pin here";
            pinRect=new Rect(Width/2-Margin-0.12f,top-0.06f-ButtonHeight/2,0.12f,ButtonHeight);
            if(PinFocused) PlaceQuad(pinRect.center.x,pinRect.center.y,pinRect.width+0.01f,pinRect.height+0.01f,FocusOutline,4002);
            PlaceQuad(pinRect.center.x,pinRect.center.y,pinRect.width,pinRect.height,Colors(Style.Plain).fill,4003);
            SetText(NextText(ButtonTextHeight),pinText,pinRect.center.x,pinRect.center.y,TextAnchor.MiddleCenter,Colors(Style.Plain).ink,pinRect.width-0.008f);
            SetText(title,Title+(Rows.Count>MaxRows ? $"   ({first+1}-{first+shown} of {Rows.Count})" : ""),-Width/2+Margin,top-0.06f,TextAnchor.MiddleLeft,UiStyle.Paper,Width-2*Margin-0.13f);
            SetText(footer,Footer,-Width/2+Margin,-top+FooterHeight/2+Margin/3,TextAnchor.MiddleLeft,UiStyle.Muted,Width-2*Margin);
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
                SetText(NextText(LabelTextHeight),row.Label,labelLeft,y,TextAnchor.MiddleLeft,row.Dim ? UiStyle.Muted : UiStyle.Paper,labelWidth);
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
                case Style.Solid: return (UiStyle.Paper,UiStyle.Ink);
                case Style.Wire: return (UiStyle.Accent,new Color(0.08f,0.04f,0f));
                case Style.Off: return (new Color(0.34f,0.36f,0.39f),UiStyle.Paper);
                case Style.Action: return (new Color(0.17f,0.33f,0.45f),UiStyle.Paper);
                default: return (UiStyle.InkRaised,UiStyle.Muted);
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
            text.font=font; text.fontSize=FontSize; text.characterSize=textHeight*10/FontSize; text.richText=false; text.color=UiStyle.Paper;
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
