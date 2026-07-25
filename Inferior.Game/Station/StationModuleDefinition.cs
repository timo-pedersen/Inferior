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
    // If set, called once per station to create this module's hull. Brief U1: returns two
    // meshes, not one — Hull (load-bearing exterior geometry only; arbitrary, since only
    // the factory can produce it) and Deco (any structural-but-decoration content the
    // factory itself needs to seed, e.g. DockingBayHull's door frame/chamfer/interior
    // walls — StationDecorator.Decorate's own passes append to this the same way they
    // build up a box module's mod.Mesh from nothing). This mirrors the box-module shape
    // exactly: a separate hull (SystemSpaceState.BuildHullMesh for box modules, this
    // factory's Hull for MeshFactory modules), drawn DynamicLit, never AO'd; a decoration
    // mesh (mod.Mesh either way), drawn BakedColorLit, AO'd. Deco may be empty (the
    // octagonal hull factories return a fresh, empty one — their entire mesh is hull).
    public          Func<int, (StationModuleMesh Hull, StationModuleMesh Deco)>? MeshFactory { get; init; }
}
