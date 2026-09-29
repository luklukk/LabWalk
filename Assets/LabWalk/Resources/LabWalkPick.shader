Shader "Hidden/LabWalk/Pick"
{
    // Object picking (ObjectPicker): renders the solid geometry along the controller ray into a one-pixel target
    // and writes the object index (UV0.x) and the distance from the ray origin. Drawn with explicit matrices
    // from a command buffer, outside any XR camera, so it does not use Unity's camera or stereo matrices.
    SubShader
    {
        Pass
        {
            Cull Off ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4x4 _LabWalkPickViewProj;
            float4 _LabWalkPickOrigin; // xyz = ray origin; w = 1 to pack the index into 8-bit channels
            struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct Output { float4 position:SV_POSITION; float id:TEXCOORD0; float3 world:TEXCOORD1; };
            Output vert(Input v)
            {
                Output o;
                float4 world=mul(unity_ObjectToWorld,float4(v.vertex.xyz,1));
                o.position=mul(_LabWalkPickViewProj,world);
                o.world=world.xyz;
                o.id=v.uv.x;
                return o;
            }
            float4 frag(Output i):SV_Target
            {
                float id=floor(i.id+0.5);
                if(_LabWalkPickOrigin.w>0.5)
                    return float4(fmod(id,256),fmod(floor(id/256),256),floor(id/65536),255)/255;
                return float4(id,distance(i.world,_LabWalkPickOrigin.xyz),0,1);
            }
            ENDCG
        }
    }
}
