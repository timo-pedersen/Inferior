using Inferior.Galaxy;

namespace Inferior.Game.StationGen.Megastations;

public readonly record struct MegastationSelection(
    bool IsMegastation,
    MegastationArchetype Archetype)
{
    public string DisplayName => !IsMegastation
        ? ""
        : Archetype switch
        {
            MegastationArchetype.Bolon => "Bolon Mega Station",
            MegastationArchetype.RedBolon => "Red Bolon Mega Station",
            _ => "Mega Station",
        };
}

/// <summary>
/// One authority for the development-time ordinary/megastation decision. The
/// station-owned archetype is a separate deterministic decision and never feeds
/// back into the probability that a station becomes a megastation.
/// </summary>
public static class MegastationDevelopmentPolicy
{
    /// <summary>
    /// Resolves the system-wide megastation category while enforcing the universe
    /// invariant that a star system contains at most one megastation-class station.
    /// Individual eligibility remains stable per station; when development settings
    /// make several stations eligible, the lowest stable probability (then identity)
    /// wins. A forced starter station has priority over every probabilistic candidate.
    /// </summary>
    public static IReadOnlyDictionary<Station, MegastationSelection> ResolveSystem(
        IReadOnlyList<Station> stations,
        Station? starterStation,
        MegastationDevelopmentSelection selection)
    {
        var resolved = stations.ToDictionary(
            station => station,
            station => Resolve(station, starterStation, selection));
        Station[] selected = stations
            .Where(station => resolved[station].IsMegastation)
            .ToArray();
        if (selected.Length <= 1)
            return resolved;

        Station winner = starterStation != null
            && selected.Contains(starterStation)
                ? starterStation
                : selected
                    .OrderBy(station => StableProbability(
                        station.PersistenceId ?? station.Name))
                    .ThenBy(station => station.PersistenceId ?? station.Name,
                        StringComparer.Ordinal)
                    .First();

        foreach (Station station in selected)
            if (!ReferenceEquals(station, winner))
                resolved[station] = new(false, resolved[station].Archetype);
        return resolved;
    }

    public static MegastationSelection Resolve(
        Station station,
        Station? starterStation,
        MegastationDevelopmentSelection selection)
    {
        bool selected = selection.ForceStarterStation
            && starterStation != null
            && ReferenceEquals(station, starterStation);
        if (!selected)
        {
            selected = selection.Mode switch
            {
                MegastationPrototypeSelectionMode.Frequent =>
                    StableProbability(station.PersistenceId ?? station.Name)
                        < selection.MegastationProbability,
                MegastationPrototypeSelectionMode.ForceStarterStation =>
                    starterStation != null && ReferenceEquals(station, starterStation),
                _ => false,
            };
        }

        return new(
            selected,
            selection.ForcedArchetype ?? station.MegastationArchetype);
    }

    public static double StableProbability(string value)
    {
        unchecked
        {
            uint hash = 2166136261u;
            foreach (char c in value)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return (hash % 10_000u) / 10_000.0;
        }
    }
}
