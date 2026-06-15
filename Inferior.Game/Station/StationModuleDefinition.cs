using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

public enum StationScale { Outpost, Station, Port, Megastation }

public sealed class StationModuleDefinition
{
    public required string                       Id          { get; init; }
    public required string                       Category    { get; init; }
    public required Vector3                      BoundingBox { get; init; }
    public          StationScale                 MinScale     { get; init; } = StationScale.Outpost;
    public required StationPort[]                Ports        { get; init; }
    public          float                        SelectWeight { get; init; } = 1f;
    // If set, called once per station to create the hull mesh for this module.
    // The factory receives the module's seed and returns a StationModuleMesh
    // with BaseFaceCount already set to the hull face count.
    public          Func<int, StationModuleMesh>? MeshFactory { get; init; }
}
