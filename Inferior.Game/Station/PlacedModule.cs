using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

public sealed class OpenPort
{
    public required PlacedModule ParentModule  { get; init; }
    public required StationPort  Definition    { get; init; }
    public required Vector3      WorldPosition { get; init; }
    public required Vector3      WorldNormal   { get; init; }
    public          int          Depth         { get; init; }
}

public sealed class PlacedModule
{
    public required StationModuleDefinition Definition { get; init; }
    public required Matrix                  Transform  { get; init; }
    public required int                     Seed       { get; init; }
    public          int                     Depth      { get; init; }
    public          Vector3                 AabbMin    { get; init; }
    public          Vector3                 AabbMax    { get; init; }
    public          List<OpenPort>          OpenPorts      { get; } = [];
    public          StationPort?            AttachmentPort { get; set; }
    public          List<StationPort>       ChildPorts     { get; } = [];
    public          StationModuleMesh?      Mesh           { get; set; }
}
