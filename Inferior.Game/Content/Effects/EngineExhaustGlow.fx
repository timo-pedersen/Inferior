float4x4 ViewProjection;

float3 GlowCenter;
float3 CameraRight;
float3 CameraUp;
float  GlowRadius;
float3 GlowColor;

float IdleIntensity;
float ThrustIntensity;
float BrakeIntensity;
float BoostIntensity;
float FlickerAmount;

float EngineOutput;
float EngineBrake;
float EngineBoost;
float VisualTime;

struct VertexInput
{
    float4 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct VertexOutput
{
    float4 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

VertexOutput VS(VertexInput input)
{
    VertexOutput output;
    float3 worldPosition =
        GlowCenter
        + CameraRight * (input.Position.x * GlowRadius)
        + CameraUp * (input.Position.y * GlowRadius);
    output.Position = mul(float4(worldPosition, 1.0), ViewProjection);
    output.TexCoord = input.TexCoord;
    return output;
}

float4 PS(VertexOutput input) : COLOR0
{
    float2 fromCenter = input.TexCoord * 2.0 - 1.0;
    float radialDistance = length(fromCenter);
    clip(1.0 - radialDistance);

    float falloff = saturate(1.0 - radialDistance);
    falloff = falloff * falloff * (3.0 - 2.0 * falloff);

    float intensity = lerp(IdleIntensity, ThrustIntensity, saturate(EngineOutput));
    intensity = lerp(intensity, BrakeIntensity, saturate(EngineBrake));
    intensity = lerp(intensity, BoostIntensity, saturate(EngineBoost));

    // Braking is deliberately slow and uneven: layered periods create recognizable
    // pulses, deep dips, and occasional surges instead of a fine random shimmer.
    float brakePulse = saturate(
        0.50
        + 0.26 * sin(VisualTime * 4.1)
        + 0.16 * sin(VisualTime * 2.3 + 1.7)
        + 0.10 * sin(VisualTime * 7.3 + 0.6));
    float brakeFactor = 0.28 + brakePulse * 1.18;
    intensity *= lerp(
        1.0,
        brakeFactor,
        saturate(EngineBrake) * FlickerAmount);

    // Boost stays consistently brightest but retains a restrained energetic ripple.
    float boostRipple = 1.0 + sin(VisualTime * 13.0) * 0.08 * FlickerAmount;
    intensity *= lerp(1.0, boostRipple, saturate(EngineBoost));

    float whiteCore =
        saturate((intensity - 1.0) * 0.5)
        * falloff * falloff;
    float3 color = lerp(GlowColor, float3(1.0, 1.0, 1.0), whiteCore);
    return float4(color * intensity * falloff, falloff);
}

technique EngineExhaustGlow
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS();
        PixelShader  = compile ps_3_0 PS();
    }
}
