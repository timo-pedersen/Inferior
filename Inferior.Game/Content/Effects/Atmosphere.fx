// Atmosphere.fx — analytical chord-length shell model
//
// Ray-sphere intersection determines how much of each pixel ray passes through
// the atmosphere SHELL (between PlanetRadius and AtmosRadius).
// density = pow(shellChord / maxChord, 0.6) — smooth, broad glow curve.
//
// AtmosRadius is inflated 1.15× the physical atmosphere so the mesh boundary
// sits outside the region where the fade reaches zero naturally.
//
// Camera is at render-space origin (origin-shift rendering).
// LightDirection: FROM star TOWARD scene — same sign as BasicEffect.

float4x4 ViewProjection; // camera.ViewMatrix * camera.ProjectionMatrix
float3   PlanetCenter;   // planet centre in render space (camera at origin)
float    PlanetRadius;   // planet surface radius (render units)
float    AtmosRadius;    // outer draw radius — inflated 1.15× physical
float3   AtmosphereColor;
float    Opacity;
float3   LightDirection; // FROM star TOWARD scene

struct VertexInput
{
    float4 Position : POSITION;
    float2 TexCoord : TEXCOORD0; // unused — required by VertexPositionTexture
};

struct VertexOutput
{
    float4 Position : POSITION;
    float3 WorldPos : TEXCOORD0;
};

VertexOutput VS(VertexInput input)
{
    VertexOutput o;
    o.Position = mul(float4(input.Position.xyz, 1.0), ViewProjection);
    o.WorldPos  = input.Position.xyz;
    return o;
}

// Returns (tNear, tFar) or (-1,-1) on miss.
float2 raySphere(float3 P, float3 D, float3 centre, float radius)
{
    float3 oc   = P - centre;
    float  b    = dot(oc, D);
    float  c    = dot(oc, oc) - radius * radius;
    float  disc = b * b - c;
    if (disc < 0) return float2(-1, -1);
    float s = sqrt(disc);
    return float2(-b - s, -b + s);
}

float4 PS(VertexOutput input) : COLOR0
{
    float3 D = normalize(input.WorldPos);
    float3 P = float3(0, 0, 0);

    float2 atmos  = raySphere(P, D, PlanetCenter, AtmosRadius);
    float2 planet = raySphere(P, D, PlanetCenter, PlanetRadius);

    // Clamp atmosphere interval to in front of camera.
    float tMin = max(0.0, atmos.x);
    float tMax = atmos.y;

    // Subtract planet body from the interval.
    if (planet.x > 0)            tMax = min(tMax, planet.x); // stop at planet near face
    else if (planet.y > 0)       tMin = max(tMin, planet.y); // camera inside planet: start after exit

    float chordLength = max(0.0, tMax - tMin);

    // Maximum possible chord: tangent ray at planet surface.
    float maxChord = 2.0 * sqrt(max(0.0, AtmosRadius * AtmosRadius - PlanetRadius * PlanetRadius));
    if (maxChord <= 0.0 || atmos.y <= 0.0) return float4(0, 0, 0, 0);

    float depth = saturate(chordLength / maxChord);
    float alpha = pow(depth, 1.5) * Opacity;

    // Edge fade: starts at max(PlanetRadius, 0.9*AtmosRadius) so the limb is never
    // attenuated, then smooth-zeros at AtmosRadius with zero derivative.
    float impactParam = length(PlanetCenter - dot(PlanetCenter, D) * D);
    float fadeStart   = max(PlanetRadius, AtmosRadius * 0.9);
    float edgeFade    = 1.0 - smoothstep(fadeStart, AtmosRadius, impactParam);
    alpha *= edgeFade;

    // Day/night with Rayleigh twilight reddening.
    float3 entryPt = P + D * tMin;
    float3 N       = normalize(entryPt - PlanetCenter);
    float  sunDot  = dot(N, -LightDirection);

    // Twilight colour: peaks near the terminator (sunDot≈0), only on the lit side.
    float  twilight     = saturate(1.0 - abs(sunDot) * 2.5) * saturate(sunDot + 0.3);
    float3 warmColor    = float3(1.0, 0.45, 0.15);
    float3 scatterColor = lerp(AtmosphereColor, warmColor, twilight);

    // Bright on day side, very faint ambient on night side.
    float sun = saturate(sunDot) * 0.85 + 0.05;

    return float4(scatterColor * sun, alpha);
}

technique Atmosphere
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS();
        PixelShader  = compile ps_3_0 PS();
    }
}
