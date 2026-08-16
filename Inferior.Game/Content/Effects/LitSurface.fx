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
// them together with the sampling code that uses them. Normal-map slots are still
// deferred to their own phase (spec section 11, Brief S2). Specular (Brief S1) is
// implemented below, DynamicLit*/station-hulls only — BakedColorLit*/station decoration
// is untouched until S2, which bundles it with bump since both need a perturbed normal.

float4x4 World;
float4x4 View;
float4x4 Projection;

float3   SunDirection;          // world space, FROM scene TOWARD star
float3   SunColour;
float    Ambient;               // scalar floor, matches SceneLighting.Ambient
float    EclipseFactor = 1.0;   // reserved for Phase E; 1.0 = no eclipse

float3   MaterialColor = float3(1, 1, 1);   // DynamicLit only — flat per-draw tint

float3   DebugFlatColor = float3(1, 1, 1);  // debug-only semantic surface override

// Brief S1: single-source Blinn-Halfway specular, DynamicLit*/station-hulls only. No HLSL
// initializers — same policy as the shadow parameters below (project policy since the
// EclipseFactor incident); MeshRenderer.cs sets all three explicitly every DynamicLit*
// draw call. EyePositionWorld is always Vector3.Zero in practice (every World matrix in
// this codebase places geometry relative to the same camera whose View looks from
// Vector3.Zero — see MeshRenderer.cs), but it's still a real parameter, not a hardcoded
// shader constant, in case that render-space convention ever changes.
float3   EyePositionWorld;
float    SpecularStrength;
float    SpecularShininess;

// Brief S2c-2: derivative-bump strength. 0 = off — and mathematically exact, not just
// visually close: PerturbNormalFromHeight scales baseNormal by abs(det) before
// normalizing, which never changes its direction, so a zeroed gradient term reproduces
// the unperturbed normal exactly (same "off is exact" pattern as SpecularStrength/
// ShadowKernelRadius). No HLSL initializer, same policy as the other tunables above; set
// explicitly every DynamicLit* draw call (non-station callers — ships/containers/the
// calibration cube — pass 0, and are additionally structurally immune regardless: their
// neutral MaterialMap is a flat 1x1 texture, so ddx/ddy of its sampled height is always
// exactly zero no matter what BumpStrength is).
float    BumpStrength;

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
// Step 2 (Brief E1): manual PCF kernel radius in whole texels — 0 = single tap (Off),
// 1 = 3x3, 2 = 5x5. No HLSL initializer, same policy as the other shadow parameters
// above; set explicitly every shadowed draw call (SystemSpaceState.Stations.cs).
float    ShadowKernelRadius;

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

// Brief S2c-1: station-hull-panel material map (RGBA) — R height (reserved for S2c-2,
// neutral for now), G gloss (this brief, modulates SpecularHighlight below), B/A
// reserved. Same UV as Texture/TextureSampler (albedo), same filtering/wrap so gloss
// transitions read as smoothly as the albedo they pair with. DynamicLit*/station-hulls
// only (SpecularHighlight's only callers) — never sampled from BakedColorLit*.
// MeshRenderer binds a neutral 1x1 (height=128, gloss=255) stand-in for every
// DynamicLit* caller that has no real material map (ships, containers, calibration
// cube) — full gloss reproduces pre-S2c-1 behaviour exactly, untouched by this brief.
texture  MaterialMap;
sampler  MaterialSampler = sampler_state
{
    Texture   = <MaterialMap>;
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
    // Render-space position (World-transformed, pre-View) — Brief S1's view vector needs
    // the surface position in the same space EyePositionWorld is defined in. Interpolated,
    // so the PS re-derives V per-pixel rather than per-vertex (see SpecularHighlight).
    float3 RenderPos   : TEXCOORD4;
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
    o.RenderPos   = worldPos.xyz;
    return o;
}

// Shared projection/correction setup: station-space position/normal to the light-space
// UV, the texel-snapped sample point, and the receiver-plane-corrected receiver depth
// (still in normalized depth units, bias NOT yet applied). Returns false if stationPos
// falls outside the map's XY or depth range. Both StationShadowDeltaMetres (single-tap
// delta diagnostic, F6) and StationShadowTerm (production term, PCF-wrapped) read this
// exact same setup — a debug capture must describe the real comparison, not a second,
// possibly-diverging one. The correction itself stays single-tap even under PCF (Step 2
// non-goal: no per-tap slope correction, see LitSurface.fx history/brief) — only the
// stored-depth fetch varies per tap.
bool StationShadowSetup(float3 stationPos, float3 stationNormal, out float2 texel, out float receiverDepth)
{
    float4 lightView = mul(float4(stationPos, 1.0), StationLocalToLightView);
    float2 uv = (lightView.xy - ShadowMinXY) * ShadowInvSize;
    uv.y = 1.0 - uv.y;

    texel = float2(0.0, 0.0);
    receiverDepth = 0.0;
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        return false;

    receiverDepth = (-lightView.z - ShadowNear) / ShadowDepthSpan;
    if (receiverDepth < 0.0 || receiverDepth > 1.0)
        return false;

    texel = floor(uv / ShadowTexelSize) * ShadowTexelSize + ShadowTexelSize * 0.5;
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
    return true;
}

// Returns true if stationPos maps inside the shadow map's XY and depth range, and
// outputs the receiver-minus-stored depth delta in METRES (single centre tap) — positive
// means the receiver is behind the stored (occluding) depth, i.e. in shadow; negative
// means in front, i.e. lit. Deliberately not PCF-averaged: this is the classification
// diagnostic (F6 delta view), and Step 2 is explicit that PCF blurs genuine edges, not
// classification — softening this view would hide the thing it exists to show.
bool StationShadowDeltaMetres(float3 stationPos, float3 stationNormal, out float deltaMetres)
{
    float2 texel;
    float  receiverDepth;
    deltaMetres = 0.0;
    if (!StationShadowSetup(stationPos, stationNormal, texel, receiverDepth))
        return false;

    // tex2Dlod, not tex2D: ShadowSampler has no mips (Point MipFilter, single level), so
    // there's no gradient to compute; an explicit LOD 0 says that outright instead of
    // leaving the compiler to infer it (matters once StationShadowTapLit below puts the
    // same fetch in a variable-offset loop — tex2D there triggers a "gradient instruction
    // in a loop" compiler warning even though the map has nothing to take a gradient of).
    float storedDepth = tex2Dlod(ShadowSampler, float4(texel, 0.0, 0.0)).r;
    // Constant tie-break bias, applied here so the shadow term AND the delta diagnostic
    // agree on exactly the same biased comparison — ShadowBiasDepth is already in
    // normalized depth units (C# divides the metres constant by ShadowDepthSpan), so it
    // subtracts directly from receiverDepth before the metres conversion below.
    deltaMetres = (receiverDepth - ShadowBiasDepth - storedDepth) * ShadowDepthSpan;
    return true;
}

// One biased tap: fetch stored depth at `texel` (already texel-centre snapped, optionally
// offset by whole texels for a PCF neighbour) and compare against the once-computed,
// already-corrected receiverDepth. Same comparison StationShadowDeltaMetres does for its
// single centre tap, factored out so the PCF loop below is a literal wrap, not a fork.
float StationShadowTapLit(float2 texel, float receiverDepth)
{
    float storedDepth = tex2Dlod(ShadowSampler, float4(texel, 0.0, 0.0)).r;
    float delta = (receiverDepth - ShadowBiasDepth - storedDepth) * ShadowDepthSpan;
    return delta <= 0.0 ? 1.0 : 0.0;
}

// Production shadow term: 1.0 = fully lit, 0.0 = fully shadowed, in between = PCF
// penumbra fraction. ShadowKernelRadius selects Off (1x1, byte-identical to pre-Step-2)
// / 3x3 / 5x5 — every tap reuses the single centre-tap receiverDepth (already bias- and
// slope-corrected), varying only which stored-depth texel it samples.
float StationShadowTerm(float3 stationPos, float3 stationNormal)
{
    float2 texel;
    float  receiverDepth;
    if (!StationShadowSetup(stationPos, stationNormal, texel, receiverDepth))
        return 1.0;   // outside the map, or beyond its depth range: lit

    if (ShadowKernelRadius < 0.5)
        return StationShadowTapLit(texel, receiverDepth);

    if (ShadowKernelRadius < 1.5)
    {
        float lit = 0.0;
        for (int i = -1; i <= 1; i++)
        for (int j = -1; j <= 1; j++)
            lit += StationShadowTapLit(texel + float2(i, j) * ShadowTexelSize, receiverDepth);
        return lit / 9.0;
    }

    {
        float lit = 0.0;
        for (int i = -2; i <= 2; i++)
        for (int j = -2; j <= 2; j++)
            lit += StationShadowTapLit(texel + float2(i, j) * ShadowTexelSize, receiverDepth);
        return lit / 25.0;
    }
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

// Brief S2c-2: perturb a geometric normal using only screen-space derivatives (no
// precomputed tangent vertex channel) — Mikkelsen's "bump mapping unparametrized
// surfaces" method. sigmaX/sigmaY are the screen-space derivatives of render-space
// position; r1/r2 form a per-pixel basis orthogonal to the geometric normal; dHdx/dHdy
// are the screen-space derivative of the SAMPLED height value, which already carries the
// texcoord-to-screen mapping through the chain rule, so no separate ddx/ddy(TexCoord)
// decomposition is needed. sign(det) compensates for arbitrary UV winding/handedness;
// abs(det) scales the base normal without ever flipping its direction, which is exactly
// why bumpStrength == 0 collapses this to normalize(baseNormal) — mathematically exact,
// not an approximation. height is the material map's R channel: a byte around
// MaterialNeutralHeight is the flat reference plane, only its LOCAL VARIATION matters
// here (a constant offset has zero screen-space derivative, so the neutral-fill 1x1
// MaterialMap used by ships/containers/the calibration cube is structurally immune to
// this regardless of bumpStrength).
//
// Gate-fix (blinking/near-black patches, no gradation between presets): renderPos is
// world metres * Camera3D.RenderScale (1e-9, Camera3D.cs) — its screen-space derivative
// is ~1e-9x smaller than a metre-scale one, and det (built from TWO factors carrying
// that tiny scale) collapsed to ~1e-18, utterly negligible next to bumpStrength*surfGrad
// (which only carries ONE power of it). The result was really just
// normalize(-surfGrad): direction decided almost entirely by height derivatives that
// spike at seams/panel edges, with no floor from the true geometric normal — hence dark
// flicker exactly at those edges, and every non-zero bumpStrength looking identical
// (surfGrad dominates regardless of its own scale, and normalize() removes overall
// scale). RenderScaleReciprocal undoes the shrink before the derivatives are taken, so
// det and surfGrad land back in a comparable, sane range.
static const float RenderScaleReciprocal = 1e9; // 1 / Camera3D.RenderScale

float3 PerturbNormalFromHeight(float3 baseNormal, float3 renderPos, float height, float bumpStrength)
{
    float3 sigmaX = ddx(renderPos) * RenderScaleReciprocal;
    float3 sigmaY = ddy(renderPos) * RenderScaleReciprocal;
    float3 r1     = cross(sigmaY, baseNormal);
    float3 r2     = cross(baseNormal, sigmaX);
    float  det    = dot(sigmaX, r1);

    float dHdx = ddx(height);
    float dHdy = ddy(height);
    float3 surfGrad = sign(det) * (dHdx * r1 + dHdy * r2);

    return normalize(abs(det) * baseNormal - bumpStrength * surfGrad);
}

// Brief S1: single-source Blinn-Halfway specular. Per-pixel, not per-vertex — low-poly
// hulls have few vertices, so a per-vertex specular would smear/wander across big flat
// panels; re-normalizing the interpolated normal here matters more than for diffuse,
// since pow() amplifies interpolation error. shadowTerm is 1.0 on the unshadowed
// technique (no gate) and the real StationShadowTerm on the shadowed one, so a fragment
// in station shadow gets no glint — the highlight lives inside the lit region, not on
// top of it. Not tinted by albedo (a specular lobe is the light's colour, SunColour, not
// the surface's) and not folded into EclipseFactor — S1's formula gates on sun-facing and
// shadow only; revisit alongside Phase E if an eclipsed sun should kill the glint too.
// Brief S2c-1: gloss (MaterialSampler.g, sampled by the caller so BakedColorLit* never
// touches MaterialMap) scales SpecularStrength only — matte texels (gloss~0) read as no
// glint, glossy ones (gloss~1) as full strength. Not also mapping gloss to
// SpecularShininess: strength alone already breaks up a uniform slab (the money test),
// and one modulated parameter is simpler to read/tune than two moving together;
// shininess-modulation is a plausible follow-up tweak if strength alone doesn't read
// tactile enough, not added here.
float3 SpecularHighlight(float3 worldNormal, float3 renderPos, float shadowTerm, float gloss)
{
    float3 n = normalize(worldNormal);
    float3 l = SunDirection;
    float3 v = normalize(EyePositionWorld - renderPos);
    float3 h = normalize(l + v);

    float nl   = saturate(dot(n, l));
    float spec = pow(saturate(dot(n, h)), SpecularShininess) * nl * shadowTerm;
    return SpecularStrength * gloss * spec * SunColour;
}

float4 PS_DynamicLit(VertexOutput input) : COLOR0
{
    float4 material = tex2D(MaterialSampler, input.TexCoord);
    float  gloss     = material.g;

    // Brief S2c-2: perturb once, feed both the diffuse N.L and SpecularHighlight — never
    // recompute per-use, and never let shadow sampling see this (it stays on the
    // unperturbed StationNorm, below).
    float3 n = PerturbNormalFromHeight(normalize(input.WorldNormal), input.RenderPos, material.r, BumpStrength);

    float  nl  = saturate(dot(n, SunDirection));
    float3 lit = Ambient + SunColour * nl * EclipseFactor;

    float4 tex = tex2D(TextureSampler, input.TexCoord);
    float3 rgb = tex.rgb * MaterialColor * input.Color.rgb * lit;
    rgb += SpecularHighlight(n, input.RenderPos, 1.0, gloss);
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

    float4 material = tex2D(MaterialSampler, input.TexCoord);
    float  gloss     = material.g;

    // Shadow sampling deliberately keeps using the GEOMETRIC input.StationNorm, never
    // this perturbed normal — bump is a shading-only effect, not a shadow-casting one.
    float3 n      = PerturbNormalFromHeight(normalize(input.WorldNormal), input.RenderPos, material.r, BumpStrength);
    float  nl     = saturate(dot(n, SunDirection));
    float  shadow = StationShadowTerm(input.StationPos, input.StationNorm);
    if (ShadowBinaryView > 0.5)
        return float4(shadow, shadow, shadow, 1.0);

    float3 lit = Ambient + SunColour * nl * shadow * EclipseFactor;

    float4 tex = tex2D(TextureSampler, input.TexCoord);
    float3 rgb = tex.rgb * MaterialColor * input.Color.rgb * lit;
    rgb += SpecularHighlight(n, input.RenderPos, shadow, gloss);
    return float4(rgb, 1.0);
}

float4 PS_DebugFlatColor(VertexOutput input) : COLOR0
{
    return float4(DebugFlatColor, 1.0);
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

technique DebugFlatColor
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS();
        PixelShader  = compile ps_3_0 PS_DebugFlatColor();
    }
}
