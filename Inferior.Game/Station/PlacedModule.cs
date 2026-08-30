using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.StationGen;

// DockGuidance is distinct from AmbientMarker so its glow sprite can be sized independently
// (see the baseSize switch in SystemSpaceState.Stations.cs) — bumping AmbientMarker itself
// would also enlarge every unrelated ambient position marker on the station.
public enum GlowType
{
    NavigationLight,
    WarningStrobe,
    AviationWarning,
    AmbientMarker,
    DockGuidance,
    MegastationEntranceGuidance,
}

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
)
{
    // Optional presentation metadata for lights mounted directly on a known surface.
    // Ordinary station lights predate this field and retain their existing behaviour.
    public Vector3? SurfaceNormal { get; init; }
    public float? PresentationSizePixels { get; init; }
    public float? PresentationFadeStartMeters { get; init; }
    public float? PresentationFadeEndMeters { get; init; }
}

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
    // Capability marker for a combined native megastation decoration mesh. Upload
    // diagnostics use this rather than station/module identity checks.
    public          bool                    HasNativeMegastationInfrastructure { get; init; }
    // Capability marker for the separately batched mega-greeble presentation layer.
    public          bool                    HasNativeMegastationMegaGreeble { get; init; }
    // Explicit capability for batched presentation-only modules which intentionally
    // have decoration geometry but no load-bearing hull of their own.
    public          bool                    IsHullLessPresentationLayer { get; init; }
    // CPU-only diagnostic line list. Never uploaded or drawn unless the runtime
    // G2 debug toggle is enabled.
    public          VertexPositionColor[]?  NativeInfrastructureDebugLines { get; init; }
    public          VertexPositionColor[]?  NativeMegaGreebleDebugLines { get; init; }
    public          bool                    HasNativeMegastationFabric { get; init; }
    // Debug-only combined candidate/accepted footprint lines for the Fabric layer.
    public          VertexPositionColor[]?  NativeFabricDebugLines { get; init; }
    public          bool                    HasNativeMegastationServiceChannels { get; init; }
    public          bool                    HasNativeMegastationInterior { get; init; }
    // H1/H1a structural hulls opt into the vertex-alpha artificial readability floor.
    // Ordinary DynamicLit hulls leave it disabled, preserving their established response.
    public          bool                    UsesHullVertexIllumination { get; init; }
    // H1b luminous navigation geometry uses the same general vertex-alpha capability
    // while its ordinary liner/rib faces retain alpha zero and normal stellar lighting.
    public          bool                    UsesDecorationVertexIllumination { get; init; }
    // Presentation-only geometry sharing an authoritative structural surface can opt
    // into a minute clip-depth offset without changing station-local geometry/clearance.
    public          bool                    UsesCoplanarStructuralOverlay { get; init; }
    // Debug-only SC1 footprint and local-axis lines. Absent from Release builds.
    public          VertexPositionColor[]?  NativeServiceChannelDebugLines { get; init; }
    // Debug-only H1 portal/throat/flight-volume/interior-boundary line overlay.
    public          VertexPositionColor[]?  NativeInteriorDebugLines { get; init; }
    // H1e's tiny combined additive beam primitive. Station-local CPU vertices are reused
    // directly by the existing depth-tier draw path; no texture or per-beam GPU object.
    public          VertexPositionColor[]?  NativeApproachBeamVertices { get; init; }
    // Brief U1: only ever set for MeshFactory modules (a box module's hull is built
    // procedurally by StationGenerator.PrepareBoxHullMesh from Definition/ChamferDepth
    // alone, so it never needs storing here). Load-bearing hull geometry
    // only — drawn DynamicLit, never AO'd. Decoration (including any structural content
    // the factory itself seeds, e.g. a docking bay's door frame/chamfer/interior walls)
    // lives in Mesh, same as a box module's.
    public          StationModuleMesh?      HullMesh        { get; set; }
    // Optional deliberately filtered hull caster. H1 keeps exterior and entrance-throat
    // structure in the global station map while excluding deep interior faces.
    public          StationModuleMesh?      HullShadowMesh  { get; set; }
    public          StationModuleMesh?      GlassMesh       { get; set; }
    // Borrowed system materials are resolved by family at draw time. These ranges refer
    // to the grouped visible index buffer and never transfer Texture2D ownership into a
    // station package.
    public          IReadOnlyList<Megastations.SystemMaterialDrawRange> HullMaterialRanges { get; set; } = [];
    public          IReadOnlyList<Megastations.SystemMaterialDrawRange> DecorationMaterialRanges { get; set; } = [];
    public          Texture2D?             TextureInstance  { get; set; }
    // Parallel material map (RGBA: R height, G gloss, B/A reserved) for the SAME variant as
    // TextureInstance. Assigned once per module in StationGenerator.AssignTextures and
    // never touched again — unlike TextureInstance, the core module's name-overlay swap
    // only replaces the albedo, so this must not be re-derived from TextureInstance.
    public          Texture2D?             MaterialInstance { get; set; }
    public          List<StationLightInfo>  GlowLights      { get; } = [];
}
