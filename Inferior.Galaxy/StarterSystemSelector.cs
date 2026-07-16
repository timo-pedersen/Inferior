using System.Linq;
using Inferior.Core.Math;

namespace Inferior.Galaxy;

/// <summary>
/// Single canonical implementation of "which star/station does a new game start at."
/// Replaces the two independent nearest-G/K-star implementations that used to live in
/// InferiorGame and GalaxyMapState, and the by-name ("Far Station") starter-station
/// lookup that broke whenever the seed or galaxy changed.
/// </summary>
public static class StarterSystemSelector
{
    // A starter system needs at least this many stations to be a useful testing target
    // (multiple docking bays, multiple approach vectors).
    public const int MinStationCount = 3;

    // How many nearest G/K candidates to check before falling back. StarSystem.Generate
    // is cheap (no meshes, no GraphicsDevice), so scanning this many is fine at startup.
    public const int DefaultCandidateCap = 200;

    public readonly record struct StarResult(Star Star, string? Diagnostic);

    /// <summary>
    /// Nearest G/K star to the galactic origin whose generated system has at least
    /// MinStationCount stations, searched among the nearest candidateCap G/K stars by
    /// distance. Falls back to the plain nearest G/K star (regardless of station count)
    /// if none qualify within the cap; Diagnostic is set only on that fallback path.
    /// </summary>
    public static StarResult SelectStar(Star[] galaxy, int candidateCap = DefaultCandidateCap)
    {
        var anchor = DVec3.Zero;

        Star[] candidates = galaxy
            .Where(star => star.SpectralClass is SpectralClass.G or SpectralClass.K)
            .OrderBy(star => (star.GalacticPos - anchor).Length)
            .Take(candidateCap)
            .ToArray();

        foreach (var star in candidates)
        {
            var system = StarSystem.Generate(star, GalaxyGenerator.SystemSeed(star));
            if (system.Stations.Count >= MinStationCount)
                return new StarResult(star, null);
        }

        Star fallback = candidates.Length > 0 ? candidates[0] : galaxy[0];
        string diagnostic =
            $"No G/K star among the nearest {candidateCap} to galactic origin has " +
            $"{MinStationCount}+ stations; falling back to the nearest G/K star ({fallback.Name}).";
        return new StarResult(fallback, diagnostic);
    }

    /// <summary>
    /// Deterministic starter station within an already-generated system's station list:
    /// largest StationSize first, tie-broken by ordinal PersistenceId sort. Null only if
    /// the list is empty (a generated system always has at least one station today, but
    /// callers taking externally-provided lists, e.g. tests, should not assume that).
    /// </summary>
    public static Station? SelectStarterStation(IReadOnlyList<Station> stations)
        => stations
            .OrderByDescending(station => station.Size)
            .ThenBy(station => station.PersistenceId, StringComparer.Ordinal)
            .FirstOrDefault();
}
