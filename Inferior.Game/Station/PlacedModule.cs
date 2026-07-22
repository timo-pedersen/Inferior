using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.StationGen;

// DockGuidance is distinct from AmbientMarker so its glow sprite can be sized independently
// (see the baseSize switch in SystemSpaceState.Stations.cs) — bumping AmbientMarker itself
// would also enlarge every unrelated ambient position marker on the station.
public enum GlowType { NavigationLight, WarningStrobe, AviationWarning, AmbientMarker, DockGuidance }

public enum LightPattern
{
    Continuous,   // always on at BaseIntensity
    Strobe,       // brief bright flash, long off — aviation warning style
    SlowPulse,    // smooth sine wave — dock guidance
    Heartbeat,    // double-flash, long pause — scientific/exotic
}

// Station-relative position (already transformed by the module's local Transform).
// Rendered by adding the station's universe-relative camera vector.
public sealed record StationLightInfo(
    Vector3      WorldPosition,
    Color        Colour,
    GlowType     Type,
    float        BaseIntensity = 0.55f,
    float        Rate          = 0f,         // Hz; 0 = continuous, no animation
    float        Phase         = 0f,         // 0.0–1.0 seed-derived phase offset
    LightPattern Pattern       = LightPattern.Continuous
);

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
    public required StationModuleDefinition Definition   { get; init; }
    public required Matrix                  Transform    { get; init; }
    public required int                     Seed         { get; init; }
    // Chamfer bevel depth (5–50cm), seeded per module — single source of truth for the
    // hull panel inset (BuildHullMesh), edge trim geometry (GenerateEdgeTrimStrips), panel
    // seam length (GeneratePanelSeams), and container placement margin (PlaceContainer).
    // Computed once at construction (StationGenerator), not re-derived per consumer.
    public required float                   ChamferDepth { get; init; }
    public          int                     Depth        { get; init; }
    public          Vector3                 AabbMin    { get; init; }
    public          Vector3                 AabbMax    { get; init; }
    public          List<OpenPort>          OpenPorts      { get; } = [];
    public          StationPort?            AttachmentPort { get; set; }
    public          List<StationPort>       ChildPorts     { get; } = [];
    public          StationModuleMesh?      Mesh            { get; set; }
    public          StationModuleMesh?      GlassMesh       { get; set; }
    public          Texture2D?             TextureInstance  { get; set; }
    // Brief S2c-1: parallel material map (RGBA — R height, reserved for S2c-2, neutral
    // for now; G gloss, this brief; B/A reserved, 0) for the SAME variant as
    // TextureInstance. Assigned once per module in StationGenerator.AssignTextures and
    // never touched again — unlike TextureInstance, the core module's name-overlay swap
    // only replaces the albedo, so this must not be re-derived from TextureInstance.
    public          Texture2D?             MaterialInstance { get; set; }
    public          List<StationLightInfo>  GlowLights      { get; } = [];
}
