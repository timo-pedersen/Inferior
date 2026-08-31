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
