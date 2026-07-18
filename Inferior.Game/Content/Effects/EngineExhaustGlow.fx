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

    float flickerWave =
        0.5
        + 0.30 * sin(VisualTime * 31.0)
        + 0.20 * sin(VisualTime * 53.0 + 1.7);
    float instability = saturate(EngineBrake + EngineBoost * 0.25);
    intensity *= 1.0 - FlickerAmount * instability * saturate(flickerWave);

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
