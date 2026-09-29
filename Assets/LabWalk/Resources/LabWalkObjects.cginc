// Per-object display state for Rhino models. Every vertex carries its object's index in UV0.x (0 = none).
// _LabWalkObjectState holds one texel per index (LayerView): r = drawn as wireframe instead of solid,
// g = highlighted (pointed at). _LabWalkObjectStateSize = (width, height) of that texture.
#ifndef LABWALK_OBJECTS_INCLUDED
#define LABWALK_OBJECTS_INCLUDED

sampler2D _LabWalkObjectState;
float4 _LabWalkObjectStateSize;

float4 LabWalkObjectState(float id)
{
    float width=max(_LabWalkObjectStateSize.x,1), height=max(_LabWalkObjectStateSize.y,1);
    float x=fmod(id,width), y=floor(id/width);
    return tex2Dlod(_LabWalkObjectState,float4((x+0.5)/width,(y+0.5)/height,0,0));
}

// A clip position outside the view: every vertex of a hidden object goes here, so its triangles or lines vanish.
#define LABWALK_HIDDEN_POSITION float4(2,2,2,1)

#endif
