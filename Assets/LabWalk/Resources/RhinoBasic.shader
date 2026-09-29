Shader "LabWalk/RhinoBasic"
{
    // Solid Rhino geometry. Objects switched to wireframe by pointing at them are left out here (their lines
    // are drawn by LabWalk/Wire); the object being pointed at is tinted.
    Properties { _Color ("Color", Color) = (0.8,0.8,0.8,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "LabWalkObjects.cginc"
            struct Input { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Output { float4 position:SV_POSITION; float3 normal:TEXCOORD0; float3 world:TEXCOORD1; float highlight:TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            fixed4 _Color;
            Output vert(Input v)
            {
                Output o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(Output,o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float4 state=LabWalkObjectState(v.uv.x);
                o.position=state.r>0.5 ? LABWALK_HIDDEN_POSITION : UnityObjectToClipPos(v.vertex);
                o.world=mul(unity_ObjectToWorld,v.vertex).xyz;
                o.normal=UnityObjectToWorldNormal(v.normal);
                o.highlight=state.g;
                return o;
            }
            fixed4 frag(Output i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n=normalize(i.normal);
                float3 lighting=max(0.15,ShadeSH9(float4(n,1)))+_LightColor0.rgb*saturate(dot(n,normalize(UnityWorldSpaceLightDir(i.world))));
                float3 color=_Color.rgb*lighting;
                return fixed4(lerp(color,float3(0.35,0.85,1),i.highlight*0.45),1);
            }
            ENDCG
        }
    }
}
