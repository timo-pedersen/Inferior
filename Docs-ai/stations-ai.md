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

### Megastation prototypes

The megastation generator is an explicit alternate station-generation path, not a replacement for
the ordinary port/module growth path. Ordinary stations still use `StationGenerator` /
`StationGrowthEngine`-style port attachment plus `StationDecorator`; occupancy-generated
megastations are a separate macro path and are not an implementation of the old module-budget
table.

Prototype A produced one large filled cuboid structural volume with one dense positive-Y urban
face and five plain structural faces. Timo visually accepted that one-face result in-engine.

Prototype B wraps the accepted city identity around the whole cuboid: all six faces grow city
interiors, all twelve edges are shared generated regions, and all eight corners are shared
generated regions. Timo visually accepted Prototype B in-engine after checking multiple seeds,
all faces, shared edges/corners, close and distant views, silhouette readability, immense scale,
organic departure from the cuboid, and mixed small/large masses. Its raw occupied massing is
frozen unless a later explicit brief reopens it. The positive-Y face preserves Prototype A's
accepted seed path.

Prototype C0 is implemented and merged on `mega-stations`. It adds deterministic topology
regularisation after raw Prototype B massing, producing a separate regularised structural solid
for rendering and later boundary/chamfer work. Timo has visually confirmed C0. The 24-station
deterministic/manifold sweep passed: no edge-critical or vertex-critical configurations remained,
no material was removed, connected-component and sealed-cavity state stayed unchanged, and the
ordinary sharp boundary mesh was manifold for every checked station.

Implementation:

- `Inferior.Game/Station/Megastations/SliceGrid.cs` owns one non-uniform rectilinear grid with
  deterministic X/Y/Z slice widths, explicit core ranges, exterior growth layers, and centralized
  cell coordinate helpers.
- `StructuralOccupancy` stores compact per-cell flags for structural mass, urban mass,
  externally accessible empty space, generation owner (`StructuralCore`, `FaceInterior`,
  `EdgeRegion`, `CornerRegion`, `TopologyRegularisation`), and a stable region id.
- `CuboidStructuralVolumeGenerator` fills the structural core only. Later rectilinear/Boolean
  generators should produce the same occupancy shape rather than changing urban growth.
- `ExteriorSpace` flood-fills empty cells from the generation boundary. A solid face is external
  only when adjacent to externally accessible empty space, so sealed cavities are not treated as
  outside hull.
- `SurfacePatchFinder` discovers connected coplanar exposed face patches with stable geometric
  identities, outward normals, and patch-local U/V axes.
- `MegastationUrbanStyle` derives station-wide density/depth/tower/trench/courtyard/edge/corner
  tendencies from stable station identity. Each non-accepted face gets deterministic patch-local
  modifiers from its patch id; `PositiveY` keeps the old `root -> "district layout"` seed path.
- `CornerRegionGenerator` plans eight corner regions first as coherent stepped octant masses.
- `EdgeRegionGenerator` plans twelve edge profiles using the adjacent corner endpoint depths.
  Edge profiles include strong spine, broken spine, low structural band, irregular towers, and
  mostly-open edge summaries. Edge generation also fills face-region support shoulders along
  reserved perimeters so edge/corner mass is six-neighbor connected without changing face depth
  maps.
- `UrbanGrowth` runs on all six major patches through each patch's local U/V basis, reserves a
  perimeter band for shared edge/corner work, BSP-splits usable area into rectilinear districts,
  assigns coherent district depth, broad tower attractors, trenches/courtyards, and a small
  cleanup pass, then writes monotonic outward occupancy from layer 1 through target depth.
- `TopologyRegulariser` derives a valid structural solid from the raw accepted occupancy using
  material addition only. It audits edge-diagonal and vertex-only contacts, preserves the raw
  occupancy separately, and records repair counts, critical configurations, connected components,
  sealed-cavity state, and owner-pair summaries.
- `BoundaryTopologyBuilder` builds a canonical CPU-side boundary graph from exact integer grid
  identities on `RegularisedOccupancy`: boundary faces, canonical edge segments, grid vertices,
  edge classes, vertex classes, conservative chamfer eligibility, and per-edge clamp widths.
- `BoundaryMeshValidator` validates sharp and final boundary meshes from the exact rendered
  vertex/index arrays for finite vertices, bounds, degenerate triangles, duplicate triangles,
  open edges, non-manifold edge incidence, axis-aligned T-junctions, and isolated sliver
  components.
- `MegastationPrototypeMeshBuilder` consumes `BoundaryTopology` and emits the final exterior
  boundary mesh through `StationModuleMesh`; it uses the existing station hull lighting/render
  path and does not add a prototype shader. Optional mesh colouring supports structural-vs-urban,
  region-owner, outward-normal, edge-classification, chamfer-eligibility, vertex-complexity, and
  run-validation debug modes. Complete convex runs are merged from canonical edge segments by
  axis and incident surface pair, use the minimum safe clamped width across the run, and render
  as continuous bevels with corner caps or tapered endpoints at deliberately sharp simple
  corners. Complex/concave endpoints remain suppressed.
- `MegastationMassingSignatureBuilder` computes GraphicsDevice-free SHA-256 regression
  signatures over canonical bytes, not GPU buffers. The long-lived raw massing signature covers
  seed compatibility, raw massing algorithm versions, slice widths, core ranges, station-wide
  style, raw per-cell occupied/owner/region data, face depth maps, edge profiles, and corner
  plans. The separate regularised structural-solid signature covers the authoritative mesh input.

Authoritative megastation pipeline:

```text
Raw deterministic urban massing
-> topology regularisation
-> regularised structural solid
-> boundary extraction and sharp mesh validation
-> chamfer eligibility and final mesh validation
```

Development controls:

- `MegastationPrototypeSettings.DevelopmentSelection` is the one source location.
- Current active development setting: `Frequent`, `MegastationProbability = 0.50`,
  `ForceStarterStation = true`.
- `Canonical` remains supported by changing that value; generator identity and geometry do not
  depend on selection mode.

Versioning:

- `GeneratorVersion = 4` is the current C2 generator/output version reported in diagnostics and
  included in complete regression signatures.
- `SeedCompatibilityVersion = 1` is intentionally retained for accepted massing. The root seed is
  derived from this compatibility version, not from the diagnostic generator version, so C0
  version reporting does not alter accepted raw Prototype B massing.
- `TopologyRegularisationAlgorithmVersion = 1` is the current regularisation algorithm version.
- `BoundaryTopologyAlgorithmVersion = 1` is the current exact-grid boundary topology algorithm version.
- `StructuralChamferAlgorithmVersion = 1` is the current conservative chamfer eligibility/final mesh algorithm version.
- `PositiveYUrbanSeedVersion`, `FaceUrbanAlgorithmVersion`, `EdgeAlgorithmVersion`, and
  `CornerAlgorithmVersion` are explicit version declarations for future intentional revisions.

Diagnostics are published as a `SystemMessage` whenever a prototype is generated: station
persistent identity, generator version/root seed, topology regularisation, boundary topology, and
chamfer algorithm versions, slice counts, grid cells, raw structural/urban occupied cells,
regularised occupied cells, repair additions and removals, urbanized face count,
face/edge/corner occupied cells, total district count, maximum face depth, per-face summaries,
per-edge profile summaries, per-corner extent summaries, raw and regularised connected
components, sealed-cavity state, edge/vertex critical counts before and after regularisation,
boundary face/edge/vertex class counts, mesh path, rendered vertex/triangle counts,
eligible/suppressed chamfer segment counts, accepted/suppressed run counts, rendered
bevel/corner-cap counts, topology signature, sharp/final validation reports, exposed quads,
mesh pages, topology/mesh timings, and generation time.

Measured Prototype B CPU stats from `MegastationPrototypeGenerator.GenerateCpu` on this branch:

| Config | Slices | Cells | Structural | Urban | Face | Edge | Corner | Districts | Quads | Tris | Verts | Time |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Default prototype | 41x28x36 | 41,328 | 8,100 | 12,003 | 6,326 | 5,375 | 302 | 49 | 11,678 | 23,356 | 46,712 | 283 ms |
| Stress | 67x41x59 | 162,073 | 30,096 | 45,779 | 24,784 | 20,088 | 907 | 81 | 32,968 | 65,936 | 131,872 | 859 ms |

Prototype B is visually accepted and frozen at the raw occupied-massing layer. C0 is visually
accepted and frozen as the topology-regularised baseline. Prototypes A, B, C0, and C2 are merged
and pushed to `mega-stations`. C2's exact-grid boundary topology, deterministic signatures,
sharp/final-array validation, and chamfer diagnostics are retained. Its rendered chamfers were
visually rejected as sparse and tapering into sharp vertices. Production therefore uses the
clean sharp manifold mesh. Complete visual edge treatment is deferred, and chamfers must not be
reopened without an explicit new brief. Deferred by design:
semantic module partitioning, windows, lights, greeble, pipes, tanks, antennas,
attached annexes, Boolean cuts, O/L/T shapes, jagged structural-core erosion, bridges, overhangs,
docking bays, interiors, final megastation rarity, LOD redesign, and shadow changes.

### Detailed visual residency

Station identity, orbit, map/radar/targeting data, and distant-dot presentation are lightweight
system data and remain available for every station. Detailed visual data is proximity-resident
presentation state: `SystemSpaceState` owns zero or one `StationVisualPackage`.

`StationVisualResidencyPolicy` is the single threshold owner. Its defaults are a 100,000 m load
distance and 150,000 m unload distance, measured from a conservative station visual envelope.
The policy is keyed by `StationVisualClassification`, so megastations and future visual classes
can receive larger overrides without checking station identity, name, category strings, or
persistence ids.

When no visual is resident, the nearest eligible surface/envelope distance wins, with ordinal
persistent identity as the final tie-breaker. A resident remains until its unload boundary,
system change, state exit, generation failure, or an explicit starter/system-map/debug-cycle
arrival supersedes it. A nearer station does not displace a valid resident. Normal navigation
target selection does not request a mesh.

`StationGenerator.PrepareCpu` prepares module/decor geometry, megastation geometry, AO variants,
and procedural texture pixels away from the render thread. `GraphicsDevice` texture/buffer
creation and disposal happen only on the game/render thread. Request sequences prevent stale
preparation results from uploading or installing. The installed package owns modules, CPU mesh
references, station textures, hull/deco/flat/glass GPU buffers, shadow casters/bounds, the
station-specific shadow target/context, generation diagnostics, and actual bounds through one
idempotent disposal path. Detailed draw and shadow passes read only this package; actual bounds
gate which depth tiers can intersect it. Dots and orbital positions do not consult the package.

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
