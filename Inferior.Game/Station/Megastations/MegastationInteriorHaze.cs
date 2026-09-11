using Inferior.Rendering;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public enum MegastationInteriorHazeStrength
{
    Off,
    Subtle,
    Medium,
    Strong,
}

/// <summary>
/// H1h presentation policy. It resolves camera membership and mouth-entry blending on
/// the CPU; LitSurface additionally clips receiver pixels to CavityEnvelope.
/// </summary>
public static class MegastationInteriorHaze
{
    public const float MouthTransitionMetres = 60f;
    public const float StartDistanceMetres = 75f;
    public const float EndDistanceMetres = 900f;
    public const float MaximumBlend = .20f;
    // Luminance-relative cool-grey multiplier. This is deliberately not an absolute fog
    // colour: zero-lit structure remains black instead of acquiring a blue glow.
    // Weighted luminance is approximately 1.0, so the proof reads primarily as a cool
    // blue-grey separation rather than general distance darkening.
    public static readonly Vector3 Colour = new(.82f, 1.02f, 1.28f);

    public static float MaximumBlendFor(MegastationInteriorHazeStrength strength)
        => strength switch
        {
            MegastationInteriorHazeStrength.Off => 0f,
            MegastationInteriorHazeStrength.Subtle => .20f,
            MegastationInteriorHazeStrength.Medium => .40f,
            MegastationInteriorHazeStrength.Strong => .65f,
            _ => 0f,
        };

    public static float ResolveActivation(
        Vector3 cameraStationLocal,
        MegastationInteriorPlan interior,
        bool enabled = true)
        => ResolveActivation(
            cameraStationLocal,
            interior.CavityEnvelope.Minimum,
            interior.CavityEnvelope.Maximum,
            interior.EntrancePrecinct.AssemblyMinimum,
            interior.EntrancePrecinct.AssemblyMaximum,
            interior.EntrancePrecinct.OuterMouthCentre,
            interior.OutwardNormal,
            enabled);

    public static float ResolveActivation(
        Vector3 cameraStationLocal,
        Vector3 cavityMinimum,
        Vector3 cavityMaximum,
        Vector3 throatMinimum,
        Vector3 throatMaximum,
        Vector3 outerMouthCentre,
        Vector3 outwardNormal,
        bool enabled = true)
    {
        if (!enabled
            || !IsFinite(cameraStationLocal)
            || !IsFinite(outwardNormal)
            || outwardNormal.LengthSquared() < 1e-8f
            || !(Contains(cameraStationLocal, cavityMinimum, cavityMaximum)
                || Contains(cameraStationLocal, throatMinimum, throatMaximum)))
            return 0f;

        Vector3 inward = -Vector3.Normalize(outwardNormal);
        float inwardDepth = Vector3.Dot(
            cameraStationLocal - outerMouthCentre,
            inward);
        return SmoothStep01(inwardDepth / MouthTransitionMetres);
    }

    public static float ResolveDistanceBlend(float distanceMetres, float activation)
    {
        if (!float.IsFinite(distanceMetres) || !float.IsFinite(activation))
            return 0f;
        float range = EndDistanceMetres - StartDistanceMetres;
        float distanceFactor = SmoothStep01((distanceMetres - StartDistanceMetres) / range);
        return MathHelper.Clamp(activation, 0f, 1f) * MaximumBlend * distanceFactor;
    }

    public static InteriorHazeParameters Resolve(
        Vector3 cameraStationLocal,
        MegastationInteriorPlan interior,
        bool enabled = true,
        float maximumBlend = MaximumBlend)
    {
        float activation = ResolveActivation(cameraStationLocal, interior, enabled);
        // Include the authoritative boundary faces themselves despite ordinary float
        // interpolation at the shared structural plane.
        Vector3 receiverMargin = new(1f);
        return new InteriorHazeParameters(
            activation,
            interior.CavityEnvelope.Minimum - receiverMargin,
            interior.CavityEnvelope.Maximum + receiverMargin,
            Colour,
            StartDistanceMetres,
            EndDistanceMetres,
            MathHelper.Clamp(maximumBlend, 0f, 1f));
    }

    private static bool Contains(Vector3 point, Vector3 minimum, Vector3 maximum)
        => point.X >= minimum.X && point.X <= maximum.X
            && point.Y >= minimum.Y && point.Y <= maximum.Y
            && point.Z >= minimum.Z && point.Z <= maximum.Z;

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static float SmoothStep01(float value)
    {
        float t = MathHelper.Clamp(value, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
