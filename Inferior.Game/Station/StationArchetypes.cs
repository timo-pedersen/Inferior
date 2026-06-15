using Microsoft.Xna.Framework;
using Inferior.Core.Random;

namespace Inferior.Game.StationGen;

// Controls how the growth engine selects ports and module categories.
// Each archetype produces a distinct spatial character for the station.
public interface IStationArchetype
{
    string Id { get; }

    // Priority score for choosing this port from the frontier next.
    // Higher = expanded sooner. Return 0 to suppress.
    float ScorePort(OpenPort port, int placedCount);

    // Weight multiplier applied on top of a module's SelectWeight when it
    // is being considered for placement at the given frontier depth.
    float CategoryBias(string category, int depth);
}

// Equal-probability spread in all directions. Default / fallback archetype.
public sealed class ClusterArchetype : IStationArchetype
{
    public string Id => "cluster";

    public float ScorePort(OpenPort port, int placedCount) => 1f;

    public float CategoryBias(string category, int depth) => category switch
    {
        "connector"  => 2f,
        "hab"        => 3f,
        "cargo"      => 2f,
        "docking"    => 1.5f,
        _            => 1f,
    };
}

// Prefers extending along one axis to build a long linear layout.
public sealed class LinearSpineArchetype : IStationArchetype
{
    public string Id => "linear-spine";

    public float ScorePort(OpenPort port, int placedCount)
    {
        // Prefer ports whose normal is mostly along the world Z axis
        float zDot = MathF.Abs(Vector3.Dot(port.WorldNormal, Vector3.UnitZ));
        return 0.5f + zDot * 3f;
    }

    public float CategoryBias(string category, int depth) => category switch
    {
        "connector"  => 5f,
        "hab"        => depth > 2 ? 3f : 1f,
        "cargo"      => depth > 3 ? 2f : 1f,
        "docking"    => depth > 4 ? 2f : 0.5f,
        _            => 1f,
    };
}

// Radiates short arms from a central hub before branching outward.
public sealed class HubSpokeArchetype : IStationArchetype
{
    public string Id => "hub-spoke";

    public float ScorePort(OpenPort port, int placedCount)
        => port.Depth <= 1 ? 4f : 0.75f;

    public float CategoryBias(string category, int depth) => category switch
    {
        "connector"  => depth <= 1 ? 4f : 1f,
        "docking"    => 3f,
        "cargo"      => depth >= 2 ? 3f : 1f,
        "industrial" => depth >= 2 ? 2f : 1f,
        _            => 1f,
    };
}

public static class StationArchetypeRegistry
{
    private static readonly IStationArchetype[] All =
    [
        new ClusterArchetype(),
        new LinearSpineArchetype(),
        new HubSpokeArchetype(),
    ];

    public static IStationArchetype Pick(SeededRandom rng)
        => All[rng.NextInt(All.Length - 1)];
}
