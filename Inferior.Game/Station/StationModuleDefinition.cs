using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

public enum StationScale { Outpost, Station }

public sealed class StationModuleDefinition
{
    public required string         Id          { get; init; }
    public required string         Category    { get; init; }
    public required Vector3        BoundingBox { get; init; }
    public          StationScale   MinScale     { get; init; } = StationScale.Outpost;
    public required StationPort[]  Ports        { get; init; }
    public          float          SelectWeight { get; init; } = 1f;
}
