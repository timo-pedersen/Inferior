namespace Inferior.Core.World;

/// <summary>
/// Derived stellar properties — calculated from mass and class, never stored.
/// Used by Environment and the Star Siphon depth mechanic.
/// Stub — formulae to be filled in when the physics layer is implemented.
/// </summary>
public static class StarPhysics
{
    /// <summary>Approximate core pressure in Pascals. Stub — returns 0.</summary>
    /// <param name="mass">Star mass in kg.</param>
    /// <param name="starClass">Spectral class index (0 = O, 6 = M, etc.).</param>
    public static double CorePressure(double mass, int starClass)
        => 0.0; // TODO: hydrostatic equilibrium approximation

    /// <summary>Approximate core temperature in Kelvin. Stub — returns 0.</summary>
    public static double CoreTemperature(double mass, int starClass)
        => 0.0; // TODO: stellar structure approximation
}
