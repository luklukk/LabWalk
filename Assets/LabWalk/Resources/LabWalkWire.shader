Shader "LabWalk/Wire"
{
    // Unlit lines for wireframe layers. The depth offset pulls lines slightly toward the viewer so edges lying
    // on a surface (e.g. an existing wall outlined on top of a renovation) are not lost to depth fighting.
    Properties { _Color ("Color", Color) = (1,0.55,0.1,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" }
        Pass
        {
            Offset -1, -8
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct Input { float4 vertex:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Output { float4 position:SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            fixed4 _Color;
            Output vert(Input v)
            {
                Output o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(Output,o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position=UnityObjectToClipPos(v.vertex);
                return o;
            }
            fixed4 frag(Output i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return _Color;
            }
            ENDCG
        }
    }
}
