// LitSurface.fx — Phase A lit-surface shader (Docs/station-lighting-pipeline-spec.md)
//
// Two techniques, one vertex format (VertexPositionNormalColorTexture) for every
// participant this phase migrates: station deco, station hull, ship hull/nacelle/pylon,
// and debug containers.
//
//   BakedColorLit — vertex colour carries albedo x AO (+ deliberate tint/wear/interior
//                   overrides, see StationDecorator.ApplyAmbientOcclusion). Vertex alpha
//                   carries a self-illumination floor S (see
//                   StationModuleMesh.ApplyIlluminationFlags / StationGenerator.
//                   BoostAmbientForFaceRange). No directional term is ever baked into
//                   colour — the sun term is computed here, every frame, from the real
//                   world normal, so a rotating station is lit correctly.
//   DynamicLit    — replicates BasicEffect's ambient + saturate(N.L) additive model, for
//                   geometry that has no bake step at all. MaterialColor is the flat
//                   per-draw tint used by hull/ship (vertex colour left white); debug
//                   containers instead vary vertex colour per-vertex and leave
//                   MaterialColor white — the two channels multiply, either can carry it.
//
// SunDirection convention: FROM the scene TOWARD the star — same as
// Inferior.Rendering.SceneLighting.SunDirection.
//
// World carries render-scale x rotation x translation (uniform scale only, never shear
// or non-uniform scale) — normals are transformed by the upper 3x3 and renormalized
// rather than an inverse-transpose, since a uniform scale changes a normal's length but
// never its direction.
//
// EclipseFactor is a real, referenced, defaulted-to-1 multiplier on the sun term
// (matches the spec's section-1 formula slot) — Phase E wires it to the planetary-eclipse
// scalar. Shadow-map matrices/textures are deliberately NOT declared here: an
// unreferenced texture/matrix parameter is stripped by the MonoGame effect compiler, so
// declaring one now would give no real forward-compatibility guarantee — Phase B adds
// them together with the sampling code that uses them. Specular/normal-map slots are
// likewise deferred to their own phases (spec section 11).

float4x4 World;
float4x4 View;
float4x4 Projection;

float3   SunDirection;          // world space, FROM scene TOWARD star
float3   SunColour;
float    Ambient;               // scalar floor, matches SceneLighting.Ambient
float    EclipseFactor = 1.0;   // reserved for Phase E; 1.0 = no eclipse

float3   MaterialColor = float3(1, 1, 1);   // DynamicLit only — flat per-draw tint

float4x4 ModuleToStationLocal;
float4x4 StationLocalToLightView;
float2   ShadowMinXY;
float2   ShadowInvSize;
float    ShadowNear;
float    ShadowDepthSpan;
float2   ShadowTexelSize;
float    ShadowCorrectionLimit = 0.01;
// No HLSL initializer, deliberately: project policy since the EclipseFactor incident —
// every parameter a technique reads gets an explicit C# set, every draw call
// (SystemSpaceState.Stations.cs computes this from StationShadowBiasMetres each time).
float    ShadowBiasDepth;
float    ShadowBinaryView = 0.0;
float    ShadowDeltaView = 0.0;

texture ShadowMap;
sampler ShadowSampler = sampler_state
{
    Texture   = <ShadowMap>;
    MinFilter = Point;
    MagFilter = Point;
    MipFilter = Point;
    AddressU  = Clamp;
    AddressV  = Clamp;
};

texture  Texture;
sampler  TextureSampler = sampler_state
{
    Texture   = <Texture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU  = Wrap;
    AddressV  = Wrap;
};

struct VertexInput
{
    float4 Position : POSITION0;
    float3 Normal   : NORMAL0;
    float4 Color    : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

struct VertexOutput
{
    float4 Position    : POSITION0;
    float3 WorldNormal : TEXCOORD0;
    float4 Color       : COLOR0;
    float2 TexCoord    : TEXCOORD1;
    float3 StationPos  : TEXCOORD2;
    float3 StationNorm : TEXCOORD3;
};

VertexOutput VS(VertexInput input)
{
    VertexOutput o;
    float4 worldPos = mul(input.Position, World);
    float4 viewPos  = mul(worldPos, View);
    o.Position    = mul(viewPos, Projection);
    o.WorldNormal = normalize(mul(input.Normal, (float3x3)World));
    o.Color       = input.Color;
    o.TexCoord    = input.TexCoord;
    o.StationPos  = mul(input.Position, ModuleToStationLocal).xyz;
    o.StationNorm = normalize(mul(input.Normal, (float3x3)ModuleToStationLocal));
    return o;
}

// Returns true if stationPos maps inside the shadow map's XY and depth range, and
// outputs the receiver-minus-stored depth delta in METRES (after the same
// receiver-plane correction, evaluated at the sampled texel centre) — positive means the
// receiver is behind the stored (occluding) depth, i.e. in shadow; negative means in
// front, i.e. lit. Shared by StationShadowTerm and the delta diagnostic view (F6) so both
// read the exact same comparison — the whole point of the diagnostic is to show what the
// real shadow term is actually doing, not a second, possibly-diverging computation.
bool StationShadowDeltaMetres(float3 stationPos, float3 stationNormal, out float deltaMetres)
{
    float4 lightView = mul(float4(stationPos, 1.0), StationLocalToLightView);
    float2 uv = (lightView.xy - ShadowMinXY) * ShadowInvSize;
    uv.y = 1.0 - uv.y;

    deltaMetres = 0.0;
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        return false;

    float receiverDepth = (-lightView.z - ShadowNear) / ShadowDepthSpan;
    if (receiverDepth < 0.0 || receiverDepth > 1.0)
        return false;

    float2 texel = floor(uv / ShadowTexelSize) * ShadowTexelSize + ShadowTexelSize * 0.5;
    float2 deltaUv = texel - uv;

    float3 lightNormal = normalize(mul(stationNormal, (float3x3)StationLocalToLightView));
    float nz = lightNormal.z;
    if (abs(nz) > 1e-4)
    {
        float width  = 1.0 / ShadowInvSize.x;
        float height = 1.0 / ShadowInvSize.y;
        float dDepthDU = (lightNormal.x * width) / (nz * ShadowDepthSpan);
        float dDepthDV = (-lightNormal.y * height) / (nz * ShadowDepthSpan);
        float correction = dDepthDU * deltaUv.x + dDepthDV * deltaUv.y;
        correction = clamp(correction, -ShadowCorrectionLimit, ShadowCorrectionLimit);
        receiverDepth += correction;
    }

    float storedDepth = tex2D(ShadowSampler, texel).r;
    // Constant tie-break bias, applied here so the shadow term AND the delta diagnostic
    // agree on exactly the same biased comparison — ShadowBiasDepth is already in
    // normalized depth units (C# divides the metres constant by ShadowDepthSpan), so it
    // subtracts directly from receiverDepth before the metres conversion below.
    deltaMetres = (receiverDepth - ShadowBiasDepth - storedDepth) * ShadowDepthSpan;
    return true;
}

float StationShadowTerm(float3 stationPos, float3 stationNormal)
{
    float delta;
    if (!StationShadowDeltaMetres(stationPos, stationNormal, delta))
        return 1.0;   // outside the map, or beyond its depth range: lit
    return delta <= 0.0 ? 1.0 : 0.0;
}

// Colour ramp for the delta diagnostic view: green = 0, red = positive (in shadow),
// blue = negative (lit), saturating at +-0.5m. Grey = outside the map/depth range (no
// data — StationShadowDeltaMetres returned false). Banded along the texel grid in this
// view points at an alignment/correction issue; a smooth gradient points at a
// caster/receiver transform mismatch; uncorrelated per-pixel noise points at precision.
float4 ShadowDeltaColour(float deltaMetres, bool inMap)
{
    if (!inMap)
        return float4(0.5, 0.5, 0.5, 1.0);

    float t = clamp(deltaMetres / 0.5, -1.0, 1.0);
    float3 colour = t >= 0.0
        ? lerp(float3(0, 1, 0), float3(1, 0, 0),  t)
        : lerp(float3(0, 1, 0), float3(0, 0, 1), -t);
    return float4(colour, 1.0);
}

float4 PS_BakedColorLit(VertexOutput input) : COLOR0
{
    float3 n      = normalize(input.WorldNormal);
    float  nl     = dot(n, SunDirection);
    float  s      = input.Color.a;   // self-illumination floor, 0 = sun-dependent, 1 = emissive
    // EclipseFactor multiplies only the sun term, not Ambient/S — an eclipsed station still
    // has its ambient floor and any self-illumination, it just loses direct sun.
    float  factor = max(max(nl * EclipseFactor, Ambient), s);

    float4 tex = tex2D(TextureSampler, input.TexCoord);
    float3 rgb = input.Color.rgb * tex.rgb * factor * SunColour;
    return float4(rgb, 1.0);
}

float4 PS_BakedColorLitShadowed(VertexOutput input) : COLOR0
{
    if (ShadowDeltaView > 0.5)
    {
        float delta;
        bool inMap = StationShadowDeltaMetres(input.StationPos, input.StationNorm, delta);
        return ShadowDeltaColour(delta, inMap);
    }

    float3 n      = normalize(input.WorldNormal);
    float  nl     = dot(n, SunDirection);
    float  s      = input.Color.a;
    float  shadow = StationShadowTerm(input.StationPos, input.StationNorm);
    if (ShadowBinaryView > 0.5)
        return float4(shadow, shadow, shadow, 1.0);

    float  factor = max(max(nl * shadow * EclipseFactor, Ambient), s);

    float4 tex = tex2D(TextureSampler, input.TexCoord);
    float3 rgb = input.Color.rgb * tex.rgb * factor * SunColour;
    return float4(rgb, 1.0);
}

float4 PS_DynamicLit(VertexOutput input) : COLOR0
{
    float3 n   = normalize(input.WorldNormal);
    float  nl  = saturate(dot(n, SunDirection));
    float3 lit = Ambient + SunColour * nl * EclipseFactor;

    float4 tex = tex2D(TextureSampler, input.TexCoord);
    float3 rgb = tex.rgb * MaterialColor * input.Color.rgb * lit;
    return float4(rgb, 1.0);
}

float4 PS_DynamicLitShadowed(VertexOutput input) : COLOR0
{
    if (ShadowDeltaView > 0.5)
    {
        float delta;
        bool inMap = StationShadowDeltaMetres(input.StationPos, input.StationNorm, delta);
        return ShadowDeltaColour(delta, inMap);
    }

    float3 n      = normalize(input.WorldNormal);
    float  nl     = saturate(dot(n, SunDirection));
    float  shadow = StationShadowTerm(input.StationPos, input.StationNorm);
    if (ShadowBinaryView > 0.5)
        return float4(shadow, shadow, shadow, 1.0);

    float3 lit = Ambient + SunColour * nl * shadow * EclipseFactor;

    float4 tex = tex2D(TextureSampler, input.TexCoord);
    float3 rgb = tex.rgb * MaterialColor * input.Color.rgb * lit;
    return float4(rgb, 1.0);
}

technique BakedColorLit
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS();
        PixelShader  = compile ps_3_0 PS_BakedColorLit();
    }
}

technique DynamicLit
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS();
        PixelShader  = compile ps_3_0 PS_DynamicLit();
    }
}

technique BakedColorLitShadowed
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS();
        PixelShader  = compile ps_3_0 PS_BakedColorLitShadowed();
    }
}

technique DynamicLitShadowed
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS();
        PixelShader  = compile ps_3_0 PS_DynamicLitShadowed();
    }
}
