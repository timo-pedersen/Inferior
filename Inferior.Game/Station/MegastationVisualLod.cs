namespace Inferior.Game.StationGen;

internal enum MegastationVisualLod
{
    Point,
    Macro,
    Complete,
}

/// <summary>
/// Single presentation policy for projected-size megastation LOD selection.
/// Render depth tiers remain independent. Hysteresis keeps both the point/macro
/// and macro/complete boundaries from flickering as the apparent size changes.
/// </summary>
internal sealed record MegastationVisualLodPolicy(
    double MacroEnterDiameterPixels = 2.0,
    double MacroExitDiameterPixels = 1.25,
    double CompleteEnterDiameterPixels = 35.0,
    double CompleteExitDiameterPixels = 25.0)
{
    public static MegastationVisualLodPolicy Default { get; } = new();

    public MegastationVisualLodPolicy Validate()
    {
        if (!double.IsFinite(MacroExitDiameterPixels)
            || !double.IsFinite(MacroEnterDiameterPixels)
            || !double.IsFinite(CompleteExitDiameterPixels)
            || !double.IsFinite(CompleteEnterDiameterPixels)
            || MacroExitDiameterPixels < 0.0
            || MacroEnterDiameterPixels <= MacroExitDiameterPixels
            || CompleteExitDiameterPixels <= MacroEnterDiameterPixels
            || CompleteEnterDiameterPixels <= CompleteExitDiameterPixels)
            throw new ArgumentOutOfRangeException(nameof(MacroEnterDiameterPixels));
        return this;
    }
}

internal sealed class MegastationVisualLodState(
    MegastationVisualLodPolicy policy)
{
    private readonly MegastationVisualLodPolicy _policy = policy.Validate();
    private bool _macroDesired;
    private bool _completeDesired;

    public MegastationVisualLod Current { get; private set; }
        = MegastationVisualLod.Point;
    public bool CompleteResidencyDesired => _completeDesired;

    public MegastationVisualLod Update(
        double apparentDiameterPixels,
        bool macroAvailable,
        bool completeAvailable)
    {
        double diameter = double.IsNaN(apparentDiameterPixels)
            ? 0.0
            : Math.Max(apparentDiameterPixels, 0.0);

        _macroDesired = _macroDesired
            ? diameter > _policy.MacroExitDiameterPixels
            : diameter >= _policy.MacroEnterDiameterPixels;
        _completeDesired = _completeDesired
            ? diameter > _policy.CompleteExitDiameterPixels
            : diameter >= _policy.CompleteEnterDiameterPixels;

        Current = !_macroDesired || !macroAvailable
            ? MegastationVisualLod.Point
            : _completeDesired && completeAvailable
                ? MegastationVisualLod.Complete
                : MegastationVisualLod.Macro;
        return Current;
    }

    public void Reset()
    {
        _macroDesired = false;
        _completeDesired = false;
        Current = MegastationVisualLod.Point;
    }
}

internal static class StationProjectedSize
{
    /// <summary>
    /// Conservative projected diameter of a station-local bounding sphere. The
    /// projection scale is Matrix.M22 and the result is independent of depth tier.
    /// </summary>
    public static double DiameterPixels(
        double radiusMeters,
        double centreDistanceMeters,
        float verticalProjectionScale,
        int viewportHeight)
    {
        if (!double.IsFinite(radiusMeters) || radiusMeters <= 0.0
            || viewportHeight <= 0 || !float.IsFinite(verticalProjectionScale)
            || verticalProjectionScale <= 0f)
            return 0.0;
        if (!double.IsFinite(centreDistanceMeters)
            || centreDistanceMeters <= radiusMeters)
            return double.PositiveInfinity;
        return radiusMeters / centreDistanceMeters
            * verticalProjectionScale * viewportHeight;
    }
}
