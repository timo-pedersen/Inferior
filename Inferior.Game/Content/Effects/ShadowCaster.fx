// ShadowCaster.fx - StationMap Phase B hull-only shadow caster.
//
// Input meshes use VertexPositionNormalColorTexture, but only Position is consumed.
// Depth is explicitly encoded as normalized linear light-view depth into a
// SurfaceFormat.Single render target cleared to 1.0.

float4x4 ModuleToLightView;
float4x4 LightProjection;
float    ShadowNear;
float    ShadowDepthSpan;

struct VertexInput
{
    float4 Position : POSITION0;
    float3 Normal   : NORMAL0;
    float4 Color    : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

struct VertexOutput
{
    float4 Position : POSITION0;
    float  Depth    : TEXCOORD0;
};

VertexOutput VS(VertexInput input)
{
    VertexOutput o;
    float4 lightView = mul(input.Position, ModuleToLightView);
    o.Position = mul(lightView, LightProjection);
    o.Depth = saturate((-lightView.z - ShadowNear) / ShadowDepthSpan);
    return o;
}

float4 PS(VertexOutput input) : COLOR0
{
    return float4(input.Depth, input.Depth, input.Depth, 1.0);
}

technique ShadowCaster
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS();
        PixelShader  = compile ps_3_0 PS();
    }
}
