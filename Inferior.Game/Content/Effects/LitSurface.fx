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
    return o;
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

float4 PS_DynamicLit(VertexOutput input) : COLOR0
{
    float3 n   = normalize(input.WorldNormal);
    float  nl  = saturate(dot(n, SunDirection));
    float3 lit = Ambient + SunColour * nl * EclipseFactor;

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
