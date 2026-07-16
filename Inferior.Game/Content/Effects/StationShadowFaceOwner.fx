float4x4 World;
float4x4 StationLocalWorld;
float4x4 View;
float4x4 Projection;
float4x4 LightViewProjection;

texture FaceOwnerTexture;

sampler FaceOwnerSampler = sampler_state
{
    Texture = <FaceOwnerTexture>;
    AddressU = Clamp;
    AddressV = Clamp;
    MinFilter = Point;
    MagFilter = Point;
    MipFilter = Point;
};

struct HullVertexInput
{
    float4 Position : POSITION0;
    float3 Normal   : NORMAL0;
    float2 TexCoord : TEXCOORD0;
};

struct BakedVertexInput
{
    float4 Position : POSITION0;
    float3 Normal   : NORMAL0;
    float4 Color    : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

struct VertexOutput
{
    float4 Position    : POSITION0;
    float4 ShadowCoord : TEXCOORD0;
};

float2 ShadowUv(float4 shadowCoord)
{
    float3 proj = shadowCoord.xyz / shadowCoord.w;
    return float2(proj.x * 0.5 + 0.5, -proj.y * 0.5 + 0.5);
}

VertexOutput HullVS(HullVertexInput input)
{
    VertexOutput o;
    float4 worldPos = mul(input.Position, World);
    float4 stationLocalPos = mul(input.Position, StationLocalWorld);
    o.Position = mul(mul(worldPos, View), Projection);
    o.ShadowCoord = mul(stationLocalPos, LightViewProjection);
    return o;
}

VertexOutput BakedVS(BakedVertexInput input)
{
    VertexOutput o;
    float4 worldPos = mul(input.Position, World);
    float4 stationLocalPos = mul(input.Position, StationLocalWorld);
    o.Position = mul(mul(worldPos, View), Projection);
    o.ShadowCoord = mul(stationLocalPos, LightViewProjection);
    return o;
}

float4 FaceOwnerPS(VertexOutput input) : COLOR0
{
    float2 uv = ShadowUv(input.ShadowCoord);
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        return float4(0.0, 0.0, 0.0, 1.0);

    float3 faceOwner = tex2D(FaceOwnerSampler, uv).rgb;
    if (dot(faceOwner, faceOwner) < 0.0001)
        return float4(0.0, 0.0, 0.0, 1.0);

    return float4(faceOwner, 1.0);
}

technique StationHullFaceOwner
{
    pass P0
    {
        VertexShader = compile vs_3_0 HullVS();
        PixelShader  = compile ps_3_0 FaceOwnerPS();
    }
}

technique StationBakedFaceOwner
{
    pass P0
    {
        VertexShader = compile vs_3_0 BakedVS();
        PixelShader  = compile ps_3_0 FaceOwnerPS();
    }
}
