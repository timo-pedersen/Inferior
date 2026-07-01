using Inferior.Core.Math;

namespace Inferior.Galaxy;

/// <summary>
/// Snapshot of a star system visible from the jump-destination screen.
/// Extend this record as more pre-jump intel becomes available (faction, legal status, etc).
/// </summary>
public sealed record SystemData(
    Star          Star,
    string        Name,
    SpectralClass SpectralClass,
    double        DistanceLY);

/// <summary>
/// Read-only query layer over the galaxy star array.
/// Construct once from <see cref="GalaxyGenerator.Generate"/> and update
/// <see cref="CurrentStar"/> whenever the player enters a new system.
/// </summary>
public sealed class StarMap
{
    private readonly Star[] _stars;

    /// <summary>
    /// The star the player is currently in. Set on every system entry.
    /// Used as the implicit origin for the single-argument overloads.
    /// </summary>
    public Star? CurrentStar { get; set; }

    public StarMap(Star[] stars) => _stars = stars;

    // ── Radius query ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all stars within <paramref name="radiusLY"/> light-years of
    /// <paramref name="origin"/> (or <see cref="CurrentStar"/> when null),
    /// sorted nearest-first. The origin star itself is excluded.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <paramref name="origin"/> is null and <see cref="CurrentStar"/>
    /// has not been set.
    /// </exception>
    public IReadOnlyList<SystemData> GetSystemsWithinRadius(double radiusLY, Star? origin = null)
    {
        var pivot = origin ?? CurrentStar
            ?? throw new InvalidOperationException(
                "StarMap.CurrentStar must be set before calling GetSystemsWithinRadius without an explicit origin.");

        var results = new List<SystemData>();

        foreach (var star in _stars)
        {
            if (star.GalaxyIndex == pivot.GalaxyIndex) continue;
            double dist = (star.GalacticPos - pivot.GalacticPos).Length;
            if (dist <= radiusLY)
                results.Add(new SystemData(star, star.Name, star.SpectralClass, dist));
        }

        results.Sort((a, b) => a.DistanceLY.CompareTo(b.DistanceLY));
        return results;
    }

    // ── Nearest-N query ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the <paramref name="count"/> nearest stars to <paramref name="origin"/>
    /// (or <see cref="CurrentStar"/> when null), sorted nearest-first.
    /// </summary>
    public IReadOnlyList<SystemData> GetNearestSystems(int count, Star? origin = null)
    {
        var pivot = origin ?? CurrentStar
            ?? throw new InvalidOperationException(
                "StarMap.CurrentStar must be set before calling GetNearestSystems without an explicit origin.");

        var all = new List<(double dist, SystemData data)>(_stars.Length);

        foreach (var star in _stars)
        {
            if (star.GalaxyIndex == pivot.GalaxyIndex) continue;
            double dist = (star.GalacticPos - pivot.GalacticPos).Length;
            all.Add((dist, new SystemData(star, star.Name, star.SpectralClass, dist)));
        }

        all.Sort((a, b) => a.dist.CompareTo(b.dist));

        var results = new List<SystemData>(count);
        for (int i = 0; i < Math.Min(count, all.Count); i++)
            results.Add(all[i].data);
        return results;
    }

    // ── Name lookup ───────────────────────────────────────────────────────────

    /// <summary>
    /// Finds a star by exact name (case-insensitive). Returns null if not found.
    /// </summary>
    public Star? FindByName(string name)
    {
        foreach (var star in _stars)
            if (string.Equals(star.Name, name, StringComparison.OrdinalIgnoreCase))
                return star;
        return null;
    }

    /// <summary>
    /// Searches for stars whose name contains <paramref name="query"/> (case-insensitive),
    /// sorted so prefix matches come first, then alphabetically.
    /// </summary>
    public IReadOnlyList<Star> SearchByName(string query)
    {
        var results = new List<Star>();
        foreach (var star in _stars)
            if (star.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                results.Add(star);

        results.Sort((a, b) =>
        {
            bool aPrefix = a.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase);
            bool bPrefix = b.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase);
            if (aPrefix != bPrefix) return aPrefix ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return results;
    }

    // ── Distance helper ───────────────────────────────────────────────────────

    /// <summary>
    /// Distance in light-years between two stars.
    /// </summary>
    public static double DistanceLY(Star a, Star b)
        => (a.GalacticPos - b.GalacticPos).Length;
}
