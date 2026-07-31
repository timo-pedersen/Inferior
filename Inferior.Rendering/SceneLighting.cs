using Microsoft.Xna.Framework;

namespace Inferior.Rendering;

/// <summary>
/// Scene-level directional light parameters shared by all 3D rendering passes.
/// Updated once per frame from the star's actual position; used both by BasicEffect
/// (module boxes, planets) and for baking into decoration vertex colours.
/// </summary>
public static class SceneLighting
{
    // Direction FROM the scene TOWARD the star (conventional L vector).
    // dot(faceNormal, SunDirection) > 0 → face is lit.
    // BasicEffect convention: DirectionalLight0.Direction = -SunDirection (incident direction).
    public static Vector3 SunDirection { get; set; } = Vector3.Normalize(new Vector3(0.5f, 1f, 0.3f));

    // Minimum light factor on the shadow side — keeps un-lit faces from going pure black.
    public static float Ambient { get; set; } = 0.09f;

    // Slightly warm sun tint — multiplied into the diffuse colour. Default matches the
    // pre-Brief-B1 hardcoded constant this system replaces per-system (see
    // SunColourForStar) — kept as the fallback for any caller that never wires a real star
    // (e.g. the object designer tool).
    public static Vector3 SunColour { get; set; } = new Vector3(1.0f, 0.97f, 0.88f);

    // Pre-computed LightFactor for a world-space normal.
    public static float LightFactor(Vector3 worldNormal)
        => MathF.Max(Vector3.Dot(worldNormal, SunDirection), Ambient);

    // Brief B1 Fix 2: normalises a star's LightColor to a fixed target luminance while
    // preserving hue — "this system feels red/blue/warm," not "this system is dim." Passing
    // Star.LightColor raw would make an M-class red dwarf's system dramatically darker than
    // a G-class system (dark-red LightColor has much lower luminance than the near-white
    // default), compounding the station-brightness problem the next brief addresses
    // (D-Bright measurements 3/5). TargetSunLuminance is the pre-B1 hardcoded default's OWN
    // luminance (0.299*1.0 + 0.587*0.97 + 0.114*0.88 = 0.968) — chosen so a Sol-like G star
    // ends up close to what was already tuned, while every other class gets the SAME
    // luminance with a different hue. A star with zero luminance (BlackHole's LightColor is
    // literally black) is left at zero — genuinely no light is the physically correct
    // outcome there, not something to floor.
    public const float TargetSunLuminance = 0.968f;

    public static Vector3 SunColourForStar(Color lightColor)
    {
        Vector3 rgb = lightColor.ToVector3();
        float   lum = Vector3.Dot(rgb, new Vector3(0.299f, 0.587f, 0.114f));
        return lum < 0.001f ? Vector3.Zero : rgb * (TargetSunLuminance / lum);
    }
}
