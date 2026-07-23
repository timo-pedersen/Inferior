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
    // Full width x height of a hull opening ships fly through (e.g. a docking bay's door).
    // Zero for modules with no such opening. Read by both the module's own MeshFactory (so the
    // geometry and this queryable value can never drift apart) and by callers wanting to know
    // door dimensions without generating any geometry (e.g. the system map's station stats).
    public          Vector2                      DoorOpening  { get; init; } = Vector2.Zero;
    // If set, called once per station to create the hull mesh for this module.
    // The factory receives the module's seed and returns a StationModuleMesh with
    // BaseFaceCount already set to the hull face count. StationDecorator.Decorate captures
    // that value into HullFaceCount immediately (Brief F1) before advancing BaseFaceCount
    // further to also cover seam decoration — HullFaceCount is what stays fixed at "the
    // factory's own hull faces" for the rest of the module's lifetime (draw-technique
    // split, AO exclusion); BaseFaceCount is not, once Decorate has run.
    public          Func<int, StationModuleMesh>? MeshFactory { get; init; }
}
