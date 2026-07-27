# Inferior — Station Design Reference

> Compressed reference for AI.
> Full design history in session briefs (Docs-ai outputs).
> Stations are generated once from system seed and saved — same seed = same station.

---

## Architecture overview

Two-level model:

**Macro** — `StationGrowthEngine`: port-based module attachment builds the outer structure.
**Micro** — `StationDecorator`: independent decoration passes applied to every exposed face.

Decoration geometry (windows, hatches, antennas, pipes, greebles) is generated once at
generation time and never rebuilt. Vertex colour is baked albedo×AO only (+ a
self-illumination floor S in alpha); the directional sun term is computed every frame in
`LitSurface.fx`, not baked — a rotating station is lit correctly. Screen-space glow is a
separate `SpriteBatch` pass using `BlendState.Additive`.

> **Lighting pipeline Phase A implemented** (`Docs/station-lighting-pipeline-spec.md`):
> normals are kept through `Build()`, no directional term is ever baked into vertex colour.
> Station shadows still do **not** exist on master/this branch; the failed first shadow
> experiment is quarantined on `wip/station-lighting-shadows`
> (`Docs-archive/Shadow_fail_retrospective.md`). Next spec phase (B) adds the shadow map.

### Megastation Prototype A

Prototype A is an explicit alternate station-generation path, not a replacement for the ordinary
port/module growth path. It currently produces one large filled cuboid structural volume with
one dense positive-Y urban face and five plain structural faces.

Implementation:

- `Inferior.Game/Station/Megastations/SliceGrid.cs` owns one non-uniform rectilinear grid with
  deterministic X/Y/Z slice widths, explicit core ranges, exterior growth layers, and centralized
  cell coordinate helpers.
- `StructuralOccupancy` stores compact per-cell flags for structural mass, urban mass, and
  externally accessible empty space.
- `CuboidStructuralVolumeGenerator` fills the structural core only. Later rectilinear/Boolean
  generators should produce the same occupancy shape rather than changing urban growth.
- `ExteriorSpace` flood-fills empty cells from the generation boundary. A solid face is external
  only when adjacent to externally accessible empty space, so sealed cavities are not treated as
  outside hull.
- `SurfacePatchFinder` discovers connected coplanar exposed face patches with stable geometric
  identities, outward normals, and patch-local U/V axes.
- `UrbanGrowth` selects the configured major patch (`PositiveY` by default), reserves a perimeter
  band, BSP-splits the usable area into rectilinear districts, assigns coherent district depth,
  broad tower attractors, trenches/courtyards, and a small cleanup pass, then writes monotonic
  outward occupancy from layer 1 through target depth.
- `MegastationPrototypeMeshBuilder` emits an unchamfered exterior boundary mesh from the final
  union occupancy through `StationModuleMesh`; it uses the existing station hull lighting/render
  path and does not add a prototype shader.

Development controls:

- Environment variable `INFERIOR_MEGASTATION_PROTOTYPE` unset/unknown: `Canonical`, ordinary
  stations unchanged.
- `INFERIOR_MEGASTATION_PROTOTYPE=force` (also `force-starter` or `starter`): force the canonical
  starter station to use Prototype A.
- `INFERIOR_MEGASTATION_PROTOTYPE=frequent` (also `many`): stable roughly-one-third prototype
  selection for seed/system-transition testing.

Diagnostics are published as a `SystemMessage` whenever a prototype is generated: station
persistent identity, generator version/root seed, slice counts, grid cells, structural/urban
occupied cells, district count, maximum urban depth, exposed quads, triangles, vertices, mesh
pages, and generation time.

Measured CPU stats from `MegastationPrototypeGenerator.GenerateCpu` on this branch:

| Config | Slices | Cells | Structural | Urban | Quads | Tris | Verts | Pages | Time |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Small test | 15x12x14 | 2,520 | 540 | 76 | 506 | 1,012 | 2,024 | 1 | 52 ms |
| Default prototype | 41x28x36 | 41,328 | 8,100 | 1,188 | 3,680 | 7,360 | 14,720 | 1 | 108 ms |
| Stress | 62x36x54 | 120,528 | 30,096 | 4,028 | 8,940 | 17,880 | 35,760 | 1 | 323 ms |

Not visually accepted yet. Deferred by design: growth on all exposed surfaces, shared edges and
corners, Boolean cuts, open interiors, bridges/overhangs, jagged boundaries, docking bays,
historical annexes, topology-derived chamfers, semantic module partitioning, final windows,
greeble, lights, pipes, antennas, and final galaxy-wide rarity.

---

## Station size classes

| Class | Name | Module budget | Approx pads | Notes |
|-------|------|--------------|-------------|-------|
| 1 | Outpost | 8–20 | 1–4 | Repair shop, mining camp |
| 2 | Station | 20–60 | 5–20 | Standard hub |
| 3 | Port | 60–150 | 20–60 | Major system hub |
| 4 | Megastation | 150–400 | 60–200+ | Faction capital |

---

## Archetypes

### Open archetypes (implemented)

| Archetype | Character |
|-----------|-----------|
| `Cluster` | Organic growth, no axis bias |
| `LinearSpine` | Spine along Z axis, sparse branches |
| `HubSpoke` | Central mass with radial arms |

### Open archetypes (designed, not implemented)

`Ring`, `Monolith`, `Compound`

### Enclosed archetypes (designed, not implemented)

Ships fly in through a rectangular portal. Interior generated separately via WFC.
Types: `Sphere`, `Pyramid_N` (N=3–6), `Prism_N` (N=3,5,6), `Octahedron`,
`Dodecahedron`, `ArchimedeanSolid`.

Interior gravity: single-direction "down" by civilisational standardisation —
not a physics constraint. Radial gravity reserved as a rare landmark trait.

---

## Module registry (8 modules implemented)

| Id | Category | BoundingBox | Key ports |
|----|----------|-------------|-----------|
| `core-hub` | core | 20×20×20 | 6 faces; top accepts connector/science/military |
| `connector-long` | connector | 40×8×8 | 2 ends + 1 top branch |
| `connector-short` | connector | 16×6×6 | 2 ends only |
| `hab-block` | hab | 18×14×18 | 4 lateral + 1 top |
| `cargo-bay` | cargo | 24×12×20 | 2 ends + 2 sides (cargo/connector only) |
| `docking-arm` | docking | 32×5×5 | attach end + IsDocking terminal end |
| `science-block` | science | 14×14×14 | 1 attach + 1 side + 1 top terminal |
| `industrial-block` | industrial | 22×18×22 | 4 lateral (Large) + 1 top terminal |

**Port rules:** `OutwardNormal` must be a unit vector. `LocalPosition` is relative
to module centroid. `IsTerminal` ports are never expanded. `IsDocking` ports
register as `LandingPad` on the output model.

---

## Growth engine

```csharp
// Frontier is List<OpenPort> — weighted selection, not FIFO queue
// Per iteration:
// 1. SelectPortFromFrontier() — axis-biased for LinearSpine/HubSpoke
// 2. ShouldTerminate() — probability rises with depth and budget consumption
// 3. SelectCategory() — weighted by IStationArchetype.CategoryWeights
// 4. SelectModule() — filtered by MinScale, compatibility, weighted by SelectWeight
// 5. TryAttach() — ComputeAttachmentTransform + AABB intersection check
// 6. RegisterLandingPads() — IsDocking ports → LandingPad list
// 7. EnqueueOpenPorts() — non-terminal, non-attachment ports → frontier
```

**Port alignment — critical math:**

```csharp
// Child's attachment port must meet parent port flush, normals opposing.
Quaternion r1 = RotationBetween(childAttachNormal, -parentPort.WorldNormal);
Quaternion r2 = Quaternion.CreateFromAxisAngle(-parentPort.WorldNormal, twistAngle);
Quaternion combined = Quaternion.Normalize(r2 * r1);
Vector3 rotatedAttachPos = Vector3.Transform(childAttachPort.LocalPosition, combined);
Vector3 childOrigin = parentPort.WorldPosition - rotatedAttachPos;  // subtraction critical
```

Twist is restricted to 0°/90°/180°/270° for box modules (grid alignment).

---

## Decoration passes (in execution order)

`FaceOccupancy` tracks placed elements per face. Passes that place elements
call `occupancy.TryOccupy()` before placing. Passes that don't block others
(seams, trim strips, pipes) skip occupancy.

| Pass | Function | Notes |
|------|----------|-------|
| 1 | `GenerateWindows` | Rectangular + octagon portholes; 3 density tiers; 20% blank faces; category colour palette; pane braces on 55% |
| 2 | `GenerateHatches` | 1–3 per face; two-quad surround+panel |
| 3 | `GenerateAntennas` | Upward-facing faces of core/science/military/connector; 3 types (spike, dish+stem, cluster); landmark antenna once per station |
| 4 | `GenerateSurfacePipes` | 45% of faces ≥40 m²; 1–3 runs; pipes raised on U-brackets (3.5–5.5 m spacing) |
| 5 | `GeneratePipes` (edge) | Edge pipes via `AddPrismPipe`; N-sided cross-sections (4/6/8); junction edges 75%, free edges 35% |
| 6 | `PlaceJunctionStripLights` | Thin amber strips at module-to-module seams |
| 7 | `PlaceWarningStobes` | Docking modules; perpendicular faces to approach; AnimTag stub |
| 8 | `PlaceBayGuidanceLights` | Two white strips on docking arm surface |
| 9 | `GenerateChimneys` | Industrial 75%, core 35%; stack + nozzle types |
| 10 | `RunSolarPanelPass` | 20% of stations; 1–3 arrays on sun-facing core/connector faces |
| 11 | `GeneratePanelSeams` | Faces ≥25 m²; 1–2 H + 1–2 V seams; factor 0.72; offset 0.028 |
| 12 | `GenerateEdgeTrimStrips` | 45° trim on all fully-exposed edges; `LightenColor` 1.12× |
| 13 | `GenerateVentGrilles` | 3 types: HorizontalBars, Louvered, ScreenMesh; checks occupancy |
| 14 | `GenerateGreebles` | 6 types: JunctionBox, EquipmentHousing, ConduitEntry, SensorPod, TechPanel, ValveAssembly; checks occupancy |
| 15 | `PlaceNavigationLights` | 3 extremity lights (red/green/blue-white); registered in `GlowLights` |
| 16 | `RegisterModuleAmbientLights` | 60% of modules; 1–2 dim colour markers; continuous |
| 17 | `PlaceLandmarkAntenna` | One tall antenna (18–27 m) per station on best science/core top face |
| — | `ApplyIlluminationFlags` | Runs AFTER all decoration; writes the self-illumination floor S=0 into vertex alpha for every face (no RGB change — the sun term is real-time now, see `LitSurface.fx`) |
| — | `ApplyAmbientOcclusion` | Runs after illumination flags; **base faces only** (`BaseFaceCount`); 0–3 enclosed adjacent faces → 0–40% darkening (RGB only, alpha/S untouched) |

**Decoration pass ordering is fixed.** AO and illumination-flags must always be last.

### Pipe geometry

```csharp
// AddPrismPipe: N-sided prism; each lateral face has own outward normal
// → directional lighting shades each face differently → simulates roundness
int sides = rng.NextDouble() < 0.40 ? 4   // square
          : rng.NextDouble() < 0.75 ? 6   // hexagonal (most convincing)
          :                           8;  // octagonal
```

### Vent types

- **HorizontalBars** (45%) — dark backing + parallel bars + frame
- **Louvered** (35%) — angled slats implying airflow direction
- **ScreenMesh** (20%) — fine grid of thin wires in both directions

### Greeble types by category

Industrial/core prefer `ValveAssembly`, `ConduitEntry`. Science prefers `SensorPod`,
`EquipmentHousing`. All categories get some `JunctionBox`, `TechPanel`.

---

## Output model

```csharp
public sealed class StationModel
{
    public string           Id, Name, ArchetypeId  { get; init; }
    public StationScale     SizeClass               { get; init; }
    public PlacedModule[]   Modules                 { get; init; }
    public bool             IsEnclosed              { get; init; }
    public LandingPad[]     LandingPads             { get; init; }
    public HangarBay[]      HangarBays              { get; init; }
    public PortalSpec?      EnclosedPortal          { get; init; }
    public StationServices  Services                { get; init; }
    public List<StationLightInfo> GlowLights        { get; }      // for screen-space glow pass
    public DVec3            OrbitPosition           { get; init; }
    public DVec3            OrbitNormal             { get; init; }
    public double           OrbitRadius             { get; init; }
}

public sealed record StationLightInfo(
    Vector3      WorldPosition,
    Color        Colour,
    GlowType     Type,
    float        BaseIntensity = 0.55f,
    float        Rate          = 0f,      // Hz; 0 = continuous
    float        Phase         = 0f,      // 0–1 seed-derived offset
    LightPattern Pattern       = LightPattern.Continuous);

public enum GlowType    { NavigationLight, WarningStrobe, AviationWarning, AmbientMarker }
public enum LightPattern { Continuous, Strobe, SlowPulse, Heartbeat }
public enum PadSizeClass { Small, Large }
public enum StationScale  { Outpost, Station, Port, Megastation }
```

---

## Lighting parameters

```csharp
public static class SceneLighting
{
    public static Vector3 SunDirection { get; set; } = Vector3.Normalize(new Vector3(1f, 2f, 0.5f));
    public static float   Ambient      { get; set; } = 0.09f;   // low — space is dark
    public static Vector3 SunColour    { get; set; } = new Vector3(1.0f, 0.97f, 0.88f);
}
```

---

## Rendering integration

**Glow pass** — called after 3D scene, before or after HUD:

```csharp
// DrawStationGlows uses BlendState.Additive SpriteBatch
// ComputeGlowIntensity uses GameClock.SimTime + light.Phase + light.Rate
// Strobe: brief flash (t < 0.12) at BaseIntensity, then 0.03
// SlowPulse: sin wave
// Heartbeat: double-flash pattern
```

**AnimTag stubs** — warning strobes and aviation lights have AnimTags recorded.
Renderer does not yet process them. When implemented: update vertex colours from
`GameClock.SimTime` per-frame for tagged vertices.

---

## Known issues / Session 7 incomplete items

Verify with Code which of these are done:

- `AddOrientedBox` may not generate correct per-face normals for all 6 faces (all faces may share same normal → flat greebles/brackets). Fix: compute perpendicular frame from long axis, assign distinct normal per face.
- AO may not yet be restricted to `BaseFaceCount` — decoration faces should not receive AO.
- Aviation warning lights (red strobes on antenna/chimney tips) may not be implemented.
- Blinking glow animation (`ComputeGlowIntensity` using `GameClock.SimTime`) may not be implemented.
- New vent types (Louvered, ScreenMesh) may not be implemented.

---

## Not yet implemented (designed)

| Feature | Notes |
|---------|-------|
| Text/markings pass | Station name on hull, bay numbers; requires font atlas geometry pipeline |
| Module shape variety | Octagonal/hexagonal module cross-sections; requires generalising BoxEdges and FaceInfo to support non-box face lists |
| Weathering pass | Age, traffic, faction history → scorch marks, patch repairs, faded paint |
| Enclosed archetypes | Sphere/Pyramid/Prism exterior + WFC interior layout |
| Multiple station archetypes | Ring, Monolith, Compound |
| Services wired to economy | StationServices flags driven by economy/faction type |
| Station persistence | Save/load StationModel per system |
| Capital dock tunnels | Special fly-in docks for capital ships |

---

## Changelog

| Date | Change |
|------|--------|
| 2026-06-14 | Initial design — growth engine, archetypes, module system, 8 decoration passes |
| 2026-06-14 | Interior gravity resolved: single-direction |
| 2026-06-15 | Sessions 3–7: directional lighting, AO, window variety, pipes (prism), surface runs, brackets, solar panels, chimneys, greebles (6 types), 3 vent types, edge trim strips, panel seams, glow system, blinking lights |
| 2026-06-15 | Session 7 interrupted — several items completion status unknown |
| 2026-06-15 | Document trimmed and consolidated into this reference |
