float4x4 World;
float4x4 StationLocalWorld;
float4x4 View;
float4x4 Projection;
float4x4 LightView;
float4x4 LightViewProjection;

float3 SunDirection;
float3 SunColour;
float  Ambient;
float  BaseShadowBias;
float  SlopeShadowBias;
float  MaxShadowBias;
float  NormalShadowOffsetMetres;
float  LightDepthNear;
float  LightDepthFar;
int    ShadowDebugMode;
float  ShadowDebugDifferenceScale;
float  EmissiveSurface;

texture DiffuseTexture;
texture ShadowMap;

sampler DiffuseSampler = sampler_state
{
    Texture = <DiffuseTexture>;
    AddressU = Wrap;
    AddressV = Wrap;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
};

sampler ShadowSampler = sampler_state
{
    Texture = <ShadowMap>;
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

struct StationVertexOutput
{
    float4 Position    : POSITION0;
    float3 Normal      : TEXCOORD0;
    float2 TexCoord    : TEXCOORD1;
    float4 ShadowCoord : TEXCOORD2;
    float  LightDepth  : TEXCOORD3;
    float4 BiasedShadowCoord : TEXCOORD4;
    float  BiasedLightDepth  : TEXCOORD5;
    float4 Color       : COLOR0;
};

struct DepthVertexOutput
{
    float4 Position : POSITION0;
    float  Depth    : TEXCOORD0;
};

float NormalizeLightDepth(float lightViewZ)
{
    return saturate((-lightViewZ - LightDepthNear) / max(LightDepthFar - LightDepthNear, 0.000001));
}

float2 ShadowUv(float4 shadowCoord)
{
    float3 proj = shadowCoord.xyz / shadowCoord.w;
    return float2(proj.x * 0.5 + 0.5, -proj.y * 0.5 + 0.5);
}

bool IsInsideShadowMap(float2 uv, float receiverDepth)
{
    return uv.x >= 0.0 && uv.x <= 1.0
        && uv.y >= 0.0 && uv.y <= 1.0
        && receiverDepth >= 0.0 && receiverDepth <= 1.0;
}

float ShadowVisibility(float4 shadowCoord, float receiverDepth, float3 normal)
{
    float2 uv = ShadowUv(shadowCoord);

    if (!IsInsideShadowMap(uv, receiverDepth))
        return 1.0;

    float storedDepth = tex2D(ShadowSampler, uv).r;
    float ndotl = saturate(dot(normalize(normal), normalize(SunDirection)));
    float slopeFactor = 1.0 - ndotl;
    float bias = min(BaseShadowBias + SlopeShadowBias * slopeFactor, MaxShadowBias);
    return receiverDepth - bias <= storedDepth ? 1.0 : 0.0;
}

float4 ShadowDebugOutput(float4 shadowCoord, float receiverDepth, float3 normal)
{
    float ndotl = saturate(dot(normalize(normal), normalize(SunDirection)));
    float slopeFactor = 1.0 - ndotl;

    if (ShadowDebugMode == 5)
        return float4(slopeFactor.xxx, 1.0);

    float2 uv = ShadowUv(shadowCoord);
    if (!IsInsideShadowMap(uv, receiverDepth))
        return float4(1.0, 1.0, 1.0, 1.0);

    float storedDepth = tex2D(ShadowSampler, uv).r;

    if (ShadowDebugMode == 1)
    {
        float shadow = receiverDepth <= storedDepth ? 1.0 : 0.0;
        return float4(shadow.xxx, 1.0);
    }
    if (ShadowDebugMode == 2)
        return float4(receiverDepth.xxx, 1.0);
    if (ShadowDebugMode == 3)
        return float4(storedDepth.xxx, 1.0);
    if (ShadowDebugMode == 4)
    {
        float difference = receiverDepth - storedDepth;
        float v = saturate(0.5 + difference * ShadowDebugDifferenceScale);
        return float4(v.xxx, 1.0);
    }

    return float4(1.0, 0.0, 1.0, 1.0);
}

StationVertexOutput HullVS(HullVertexInput input)
{
    StationVertexOutput o;
    float4 worldPos = mul(input.Position, World);
    float4 stationLocalPos = mul(input.Position, StationLocalWorld);
    float4 lightViewPos = mul(stationLocalPos, LightView);
    float3 worldNormal = normalize(mul(float4(input.Normal, 0.0), World).xyz);
    float3 stationLocalNormal = normalize(mul(float4(input.Normal, 0.0), StationLocalWorld).xyz);
    float ndotl = saturate(dot(worldNormal, normalize(SunDirection)));
    float normalOffset = NormalShadowOffsetMetres * (1.0 - ndotl);
    float4 biasedStationLocalPos = stationLocalPos + float4(stationLocalNormal * normalOffset, 0.0);
    float4 biasedLightViewPos = mul(biasedStationLocalPos, LightView);
    o.Position = mul(mul(worldPos, View), Projection);
    o.Normal = worldNormal;
    o.TexCoord = input.TexCoord;
    o.ShadowCoord = mul(stationLocalPos, LightViewProjection);
    o.LightDepth = NormalizeLightDepth(lightViewPos.z);
    o.BiasedShadowCoord = mul(biasedStationLocalPos, LightViewProjection);
    o.BiasedLightDepth = NormalizeLightDepth(biasedLightViewPos.z);
    o.Color = float4(1, 1, 1, 1);
    return o;
}

StationVertexOutput BakedVS(BakedVertexInput input)
{
    StationVertexOutput o;
    float4 worldPos = mul(input.Position, World);
    float4 stationLocalPos = mul(input.Position, StationLocalWorld);
    float4 lightViewPos = mul(stationLocalPos, LightView);
    float3 worldNormal = normalize(mul(float4(input.Normal, 0.0), World).xyz);
    float3 stationLocalNormal = normalize(mul(float4(input.Normal, 0.0), StationLocalWorld).xyz);
    float ndotl = saturate(dot(worldNormal, normalize(SunDirection)));
    float normalOffset = NormalShadowOffsetMetres * (1.0 - ndotl);
    float4 biasedStationLocalPos = stationLocalPos + float4(stationLocalNormal * normalOffset, 0.0);
    float4 biasedLightViewPos = mul(biasedStationLocalPos, LightView);
    o.Position = mul(mul(worldPos, View), Projection);
    o.Normal = worldNormal;
    o.TexCoord = input.TexCoord;
    o.ShadowCoord = mul(stationLocalPos, LightViewProjection);
    o.LightDepth = NormalizeLightDepth(lightViewPos.z);
    o.BiasedShadowCoord = mul(biasedStationLocalPos, LightViewProjection);
    o.BiasedLightDepth = NormalizeLightDepth(biasedLightViewPos.z);
    o.Color = input.Color;
    return o;
}

DepthVertexOutput DepthVS(HullVertexInput input)
{
    DepthVertexOutput o;
    float4 stationLocalPos = mul(input.Position, StationLocalWorld);
    float4 lightViewPos = mul(stationLocalPos, LightView);
    o.Position = mul(stationLocalPos, LightViewProjection);
    o.Depth = NormalizeLightDepth(lightViewPos.z);
    return o;
}

float4 HullPS(StationVertexOutput input) : COLOR0
{
    float3 normal = normalize(input.Normal);
    if (ShadowDebugMode == 6)
    {
        float shadow = ShadowVisibility(input.BiasedShadowCoord, input.BiasedLightDepth, normal);
        return float4(shadow.xxx, 1.0);
    }
    if (ShadowDebugMode != 0)
        return ShadowDebugOutput(input.ShadowCoord, input.LightDepth, normal);

    float nDotL = saturate(dot(normal, normalize(SunDirection)));
    float shadow = ShadowVisibility(input.BiasedShadowCoord, input.BiasedLightDepth, normal);
    float3 albedo = tex2D(DiffuseSampler, input.TexCoord).rgb;
    float3 lighting = Ambient.xxx + SunColour * nDotL * shadow;
    return float4(albedo * lighting, 1.0);
}

float4 BakedPS(StationVertexOutput input) : COLOR0
{
    float3 normal = normalize(input.Normal);
    if (ShadowDebugMode == 6)
    {
        float shadow = ShadowVisibility(input.BiasedShadowCoord, input.BiasedLightDepth, normal);
        return float4(shadow.xxx, 1.0);
    }
    if (ShadowDebugMode != 0)
        return ShadowDebugOutput(input.ShadowCoord, input.LightDepth, normal);

    float nDotL = saturate(dot(normal, normalize(SunDirection)));
    float shadow = ShadowVisibility(input.BiasedShadowCoord, input.BiasedLightDepth, normal);
    float4 albedo = tex2D(DiffuseSampler, input.TexCoord) * input.Color;
    if (EmissiveSurface > 0.5)
        return albedo;

    float3 lighting = Ambient.xxx + SunColour * nDotL * shadow;
    return float4(albedo.rgb * lighting, albedo.a);
}

float4 DepthPS(DepthVertexOutput input) : COLOR0
{
    return float4(input.Depth, input.Depth, input.Depth, 1.0);
}

technique StationHull
{
    pass P0
    {
        VertexShader = compile vs_3_0 HullVS();
        PixelShader  = compile ps_3_0 HullPS();
    }
}

technique StationBaked
{
    pass P0
    {
        VertexShader = compile vs_3_0 BakedVS();
        PixelShader  = compile ps_3_0 BakedPS();
    }
}

technique ShadowDepth
{
    pass P0
    {
        VertexShader = compile vs_3_0 DepthVS();
        PixelShader  = compile ps_3_0 DepthPS();
    }
}
