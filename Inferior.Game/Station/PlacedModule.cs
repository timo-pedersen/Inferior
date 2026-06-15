using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

public enum GlowType { NavigationLight, WarningStrobe, AviationWarning, AmbientMarker }

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
    public          StationModuleMesh?      GlassMesh      { get; set; }
    public          List<StationLightInfo>  GlowLights     { get; } = [];
}
