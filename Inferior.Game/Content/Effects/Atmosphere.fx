// Atmosphere.fx
// Rim/fresnel atmosphere shell.
//
// Alpha is 0 at the disc centre (you look through a thin air column) and rises to
// Opacity at the limb (you look tangentially through the full atmospheric depth).
// The day-side term brightens the lit hemisphere and dims the dark side so the
// halo fades where the star's light doesn't reach.
//
// LightDirection: FROM the star TOWARD the scene — same sign convention as
// BasicEffect.DirectionalLight0.Direction.

float4x4 WorldViewProjection;
float4x4 World;
float3   AtmosphereColor;
float    Opacity;     // overall alpha cap — rim effect always fades the centre
float    RimPower;    // fresnel exponent: higher = sharper / narrower halo
float3   LightDirection;

struct VertexInput
{
    float4 Position : POSITION;
    float3 Normal   : NORMAL;
    float2 TexCoord : TEXCOORD0;
};

struct VertexOutput
{
    float4 Position : POSITION;
    float3 WorldPos : TEXCOORD0;
    float3 Normal   : TEXCOORD1;
};

VertexOutput VS(VertexInput input)
{
    VertexOutput output;
    output.Position = mul(input.Position, WorldViewProjection);
    // Render-space position for view-direction and sun-angle calculations in PS.
    output.WorldPos = mul(input.Position, World).xyz;
    // Uniform scale: normalize removes the scale magnitude, leaving the direction.
    output.Normal   = normalize(mul(float4(input.Normal, 0), World).xyz);
    return output;
}

float4 PS(VertexOutput input) : COLOR0
{
    float3 N   = normalize(input.Normal);
    float3 V   = normalize(-input.WorldPos);           // toward camera (always at origin)
    float  rim = 1.0 - saturate(dot(N, V));            // 0 at disc centre, 1 at limb

    // Day-side: lit hemisphere at full brightness, dark side at 40%.
    float  sun = saturate(dot(N, -LightDirection)) * 0.6 + 0.4;

    float  alpha = pow(max(rim, 0.00001), RimPower) * Opacity;
    return float4(AtmosphereColor * sun, alpha);
}

technique Atmosphere
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS();
        PixelShader  = compile ps_3_0 PS();
    }
}
