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

    // Slightly warm sun tint — multiplied into the diffuse colour.
    public static Vector3 SunColour { get; set; } = new Vector3(1.0f, 0.97f, 0.88f);

    // Pre-computed LightFactor for a world-space normal.
    public static float LightFactor(Vector3 worldNormal)
        => MathF.Max(Vector3.Dot(worldNormal, SunDirection), Ambient);
}
