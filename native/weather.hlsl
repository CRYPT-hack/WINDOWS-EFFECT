// One instanced triangle strip per mark. Coordinates and window rectangles are in DIPs.
struct Mark {
    float2 center, halfSize, axis;
    uint color, shape, depth;
};
StructuredBuffer<Mark> marks : register(t0);
StructuredBuffer<float4> windows : register(t1);
cbuffer View : register(b0) { float2 extent; float pixelsPerDip; uint windowCount; };
struct Fragment {
    float4 pos : SV_Position;
    float2 uv : TEXCOORD0;
    float2 world : TEXCOORD1;
    nointerpolation float2 halfSize : TEXCOORD2;
    nointerpolation float4 tint : COLOR0;
    nointerpolation uint shape : TEXCOORD3;
    nointerpolation uint depth : TEXCOORD4;
    nointerpolation float2 heights : TEXCOORD5;
};
Fragment Vertex(uint v : SV_VertexID, uint id : SV_InstanceID) {
    Mark m = marks[id];
    Fragment f;
    f.uv = float2(v & 1, v >> 1) * 2 - 1;
    float2 p = f.uv * m.halfSize;
    f.heights=m.axis;
    f.world = m.center + (m.shape==5 ? p : float2(p.x*m.axis.x-p.y*m.axis.y, p.x*m.axis.y+p.y*m.axis.x));
    f.pos = float4(f.world / extent * float2(2,-2) + float2(-1,1), 0, 1);
    uint c = m.color;
    f.tint = float4((c>>16)&255, (c>>8)&255, c&255, c>>24) / 255.0;
    f.halfSize = m.halfSize;
    f.shape = m.shape;
    f.depth = min(m.depth,windowCount);
    return f;
}
float4 Pixel(Fragment f) : SV_Target {
    [loop] for(uint i=0;i<f.depth;i++) {
        float4 box = windows[i];
        if(all(f.world >= box.xy) && all(f.world < box.zw)) discard;
    }
    float d = length(f.uv);
    float edge = max(fwidth(d), .001);
    float coverage;
    if(f.shape == 1) { // line with rounded caps and one pixel of antialiasing
        float2 q = abs(f.uv*f.halfSize);
        float distance = length(float2(max(q.x-(f.halfSize.x-f.halfSize.y),0),q.y))-f.halfSize.y;
        coverage = saturate(-distance*pixelsPerDip);
    } else if(f.shape == 2) coverage = saturate(1-d); // radial glow
    else if(f.shape == 3) { // pointed leaf
        float side=abs(f.uv.x) + f.uv.y*f.uv.y;
        coverage=saturate((1-side)/max(fwidth(side),.001)+.5);
    } else if(f.shape == 4) { // taper to the right (bill)
        float side=abs(f.uv.y)*2+f.uv.x;
        coverage=saturate((1-side)/max(fwidth(side),.001)+.5);
    } else if(f.shape==5) {
        float h=lerp(f.heights.x,f.heights.y,smoothstep(0,1,.5+.5*f.uv.x));
        float elevation=(1-f.uv.y)*f.halfSize.y;
        coverage=saturate((h-elevation)*pixelsPerDip+.5);
        float light=saturate(elevation/max(h,.1));
        f.tint.rgb=lerp(float3(.63,.75,.83),float3(.97,.99,1),light);
    } else coverage=saturate((1-d)/edge+.5);
    float alpha=coverage*f.tint.a;
    return float4(f.tint.rgb*alpha,alpha);
}
