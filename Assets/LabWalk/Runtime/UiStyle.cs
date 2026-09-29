using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace LabWalk
{
    // One visual language for every panel, label and marker: a drawing sheet. Ink-dark panels with a thin border,
    // warm paper-white text, spaced capitals for titles, rules with 45° ticks like dimension lines, and the
    // same orange as the wireframes as the accent. Panels and labels draw on top of the model (overlay).
    public static class UiStyle
    {
        public static readonly Color Ink=new Color(0.035f,0.045f,0.058f);          // panel fill
        public static readonly Color InkRaised=new Color(0.075f,0.095f,0.12f);     // focused row, inactive buttons
        public static readonly Color Rule=new Color(0.46f,0.52f,0.58f);           // borders and rules
        public static readonly Color Paper=new Color(0.95f,0.93f,0.88f);          // main text
        public static readonly Color Muted=new Color(0.64f,0.67f,0.70f);          // secondary text
        public static readonly Color Accent=Rhino3dmModelLoader.WireColor;         // brand, wireframes, tags
        public static readonly Color Pointer=new Color(0.35f,0.85f,1f);           // controller ray, seen markers
        public const int FontSize=64;

        static Font font;
        public static Font Font => font ? font : font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        static Shader overlay;
        static readonly Dictionary<(Color,int),Material> quads=new Dictionary<(Color,int),Material>();
        static readonly Dictionary<int,Material> texts=new Dictionary<int,Material>();

        // Flat color on top of everything, ordered among overlay parts by queue.
        public static Material Overlay(Color color,int queue)
        {
            if(quads.TryGetValue((color,queue),out var m) && m) return m;
            if(!overlay) overlay=Resources.Load<Shader>("LabWalkOverlay");
            m=new Material(overlay ? overlay : Shader.Find("Unlit/Color")) {color=color,renderQueue=queue,name="Lab Walk overlay"};
            quads[(color,queue)]=m;
            return m;
        }

        // Text on top of everything (the built-in text shader honors unity_GUIZTestMode).
        public static Material OverlayText(int queue=4010)
        {
            if(texts.TryGetValue(queue,out var m) && m) return m;
            m=new Material(Font.material) {renderQueue=queue,name="Lab Walk overlay text"};
            m.SetInt("unity_GUIZTestMode",(int)CompareFunction.Always);
            texts[queue]=m;
            return m;
        }

        public static MeshRenderer Quad(string name,Transform parent,Color color,int queue)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name=name; Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent,false);
            var renderer=go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial=Overlay(color,queue);
            return renderer;
        }

        public static TextMesh Text(string name,Transform parent,float height,Color color,TextAnchor anchor,int queue=4010)
        {
            var go=new GameObject(name);
            go.transform.SetParent(parent,false);
            var text=go.AddComponent<TextMesh>();
            text.font=Font; text.fontSize=FontSize; text.characterSize=height*10/FontSize;
            text.color=color; text.anchor=anchor; text.richText=false;
            go.GetComponent<MeshRenderer>().sharedMaterial=OverlayText(queue);
            return text;
        }

        // "Lab Walk" -> "L A B   W A L K": the spaced capitals of drawing titles.
        public static string Spaced(string text)
        {
            if(string.IsNullOrEmpty(text)) return "";
            var output=new StringBuilder();
            foreach(var word in text.ToUpperInvariant().Split(' '))
            {
                if(word.Length==0) continue;
                if(output.Length>0) output.Append("   ");
                output.Append(string.Join(" ",word.ToCharArray()));
            }
            return output.ToString();
        }

        // A model file name as a title: "Handley_B1_38_LabWalk" -> "Handley B1 38 LabWalk".
        public static string Title(string name) => string.IsNullOrEmpty(name) ? "" : name.Replace('_',' ').Trim();

        // A layer path with drawing-style separators: "Renovation > Equipment" -> "Renovation › Equipment".
        public static string Path(string path) => string.IsNullOrEmpty(path) ? "" : path.Replace(" > ","  ›  ");

        // A rule with 45° ticks at both ends, like a dimension line: three thin quads in panel space.
        public static void DimensionRule(Transform parent,float left,float right,float y,float thickness,Color color,int queue)
        {
            var line=Quad("Rule",parent,color,queue);
            line.transform.localPosition=new Vector3((left+right)/2,y,0.004f);
            line.transform.localScale=new Vector3(right-left,thickness,1);
            foreach(var x in new[]{left,right})
            {
                var tick=Quad("Rule tick",parent,color,queue);
                tick.transform.localPosition=new Vector3(x,y,0.004f);
                tick.transform.localRotation=Quaternion.Euler(0,0,45);
                tick.transform.localScale=new Vector3(thickness*1.4f,thickness*9,1);
            }
        }
    }
}
