Shader "LabWalk/Wire"
{
    // Unlit lines for wireframe display. The depth offset pulls lines slightly toward the viewer so edges lying
    // on a surface (e.g. an existing wall outlined on top of a renovation) are not lost to depth fighting.
    // _WireAll = 1: the whole layer is a wireframe. 0: only objects switched to wireframe by pointing are drawn.
    Properties { _Color ("Color", Color) = (1,0.55,0.1,1) _WireAll ("Whole layer", Float) = 1 }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" }
        Pass
        {
            Offset -1, -8
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "LabWalkObjects.cginc"
            struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Output { float4 position:SV_POSITION; float highlight:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            fixed4 _Color;
            float _WireAll;
            Output vert(Input v)
            {
                Output o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(Output,o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float4 state=LabWalkObjectState(v.uv.x);
                o.position=_WireAll>0.5 || state.r>0.5 ? UnityObjectToClipPos(v.vertex) : LABWALK_HIDDEN_POSITION;
                o.highlight=state.g;
                return o;
            }
            fixed4 frag(Output i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return fixed4(lerp(_Color.rgb,float3(1,1,1),i.highlight*0.7),1);
            }
            ENDCG
        }
    }
}
