# Inferior — Station Design Reference

> Living design document. Station generation architecture, module system,
> archetype definitions, decoration passes, and output model.
> Code-level class sketches are here alongside the design rationale.

---

## Overview and philosophy

Space stations are persistent physical objects in the universe — generated once
from the system seed and saved. The same seed always produces the same station.
Stations are not procedurally regenerated on each visit.

- Emergent variety from seeded parameters — no hand-crafted stations
- Every station feels inhabited, not assembled — decoration passes matter as much as structure
- Consistent visual language: a rectangular portal always means "fly in here"
- Size and complexity scale with system importance, economy type, and faction
- No spinning or rotation — hyperspace artificial gravity is universal
- Interior gravity is single-direction (one "down" per station) — standardised for
  infrastructure and practicality regardless of station shape

Stations orbit stars (rare), planets, and moons. Multiple stations per body are
possible and normal at major worlds.

> *Lore note: Hyperspace gravity (H-sw sub-band) has a useful reach of roughly 2–10 m,
> then falls off sharply due to the half-life of the exotic particles carrying the
> attractive force. This means multiple independent gravity fields can coexist close
> together without interference — a sphere station could in principle have radial floor
> gravity on every interior surface. The design choice of single-direction "down" is
> therefore not a physical constraint but a civilisational one: standard infrastructure,
> plumbing, drainage, and construction practice all assume a canonical vertical.
> An unusual station built with radial gravity would be a notable landmark.*

---

## Station size classes

Four size classes control module budget, archetype availability, service tier,
and landing pad count.

| Class | Name | Module budget | Approx pads | Archetype access |
|-------|------|--------------|-------------|-----------------|
| 1 | Outpost | 8–20 | 1–4 | Cluster, Monolith, short LinearSpine |
| 2 | Station | 20–60 | 5–20 | All open archetypes; small enclosed solids |
| 3 | Port | 60–150 | 20–60 | All archetypes including large enclosed solids |
| 4 | Megastation | 150–400 | 60–200+ | All archetypes plus Compound |

Capital ships do not use landing pads. They rely on shuttles or small ships that
land normally. A future `CapitalDockTunnel` object type — a large rectangular shaft
the capital flies into for repair and resupply — is noted here as a placeholder but
not designed yet.

---

## Archetype reference

An archetype defines the station's primary form and directs the growth engine.
The system seed selects an archetype, weighted by size class, economy type, and faction.
Archetypes fall into two families with fundamentally different generation pipelines.

### Open archetypes

Ships dock on external pads. No fly-in required. The growth engine builds the structure
outward from a core module via port-based expansion.

| Archetype | Primary form | Character |
|-----------|-------------|-----------|
| `LinearSpine` | Central tube with perpendicular branches | ISS-like; spindly; industrial |
| `HubSpoke` | Central mass with radiating arms | Symmetrical; prestigious; clean |
| `Cluster` | Organic growth, no dominant axis | Cramped; practical; varied |
| `Ring` | Non-rotating ring, population on inner face | Imposing; rare; slow to construct |
| `Monolith` | Single large block or tower | Minimal; military or corporate |
| `Compound` | Two or more sub-stations joined by a spine | Megastation only; vast |

### Enclosed archetypes

Ships fly into the structure through a rectangular portal. The outer solid geometry
is fixed by the archetype; the interior is populated by a separate generation pass.
All enclosed archetypes share the same portal specification and interior pipeline.

| Archetype | Form | Entry face |
|-----------|------|-----------|
| `Sphere` | Approximate sphere (subdivided icosahedron) | Rectangular cut at designated pole |
| `Pyramid_N` | N-sided pyramid, N ∈ {3, 4, 5, 6} | Rectangular portal on one lateral face |
| `Prism_N` | N-sided prism, N ∈ {3, 5, 6} | Rectangular portal on one lateral rectangular face |
| `Octahedron` | 8 triangular faces | Rectangular portal on one face; equatorial orientation |
| `Dodecahedron` | 12 pentagonal faces | Rectangular portal inset in one pentagonal face |
| `ArchimedeanSolid` | TBD selection — truncated icosahedron first | Rectangular portal on largest flat face |

The N=4 prism (a box) is not listed — this is covered by `Monolith`. The N=4 pyramid
(Egyptian form) is included and visually distinct.

---

## The rectangular portal

The rectangular portal is the universal entry point for all enclosed archetypes.
It is the visual language of "fly in here" — a player who learns it once recognises
it on any enclosed station.

**Dimensions by size class:**

| Size class | Width | Height | Notes |
|------------|-------|--------|-------|
| Outpost | 60 m | 45 m | Small ships only |
| Station | 120 m | 80 m | Small and medium ships |
| Port | 220 m | 140 m | Up to large ships |
| Megastation | 400 m | 260 m | Up to large (capital via tunnel) |

The portal is cut perpendicular to the chosen entry axis and has a structural frame —
a thick bevelled border in a contrasting colour. Frame lighting encodes entry status:

- **Green continuous** — open, cleared for approach
- **Amber slow pulse** — traffic in transit; queue and hold
- **Red** — closed, locked, or hostile

Approach axis: each station defines a single preferred approach vector. Ships are
expected to enter aligned with it. Diverging significantly triggers traffic control
warnings. The preferred approach axis is stored on `StationModel` and used by the
autopilot system.

Artificial gravity inside defines "down." The portal is oriented so that a ship
flying in nose-first, level, emerges with the correct internal orientation — gravity
pulls toward the interior floor from the moment of entry.

---

## The module system

### Module categories

| Category | Description | Exterior character |
|----------|-------------|-------------------|
| `core` | Reactor block, command centre, primary hub | Largest module; many pipes and conduit runs |
| `hab` | Crew quarters, passenger areas, life support | Windows, airlocks, vents |
| `cargo` | Container storage, freight handling | Container racks, crane arms, loading ports |
| `docking` | External landing pad arms and platforms | Open structure, pad lighting, approach markers |
| `hangar` | Enclosed sub-bay within open station | Large opening, bay lighting inside |
| `connector` | Corridors, spines, ISS-style tubes | Thin, long; dense conduit coverage |
| `science` | Research labs, sensor arrays, observatories | Dense antenna clusters, large windows, dishes |
| `military` | Weapons platforms, armoured sections | Heavy plating, weapon emplacements, slit windows |
| `industrial` | Manufacturing, processing, repair facilities | Exhausts, chimneys, crane arms, heavy equipment |
| `luxury` | High-end commercial and entertainment | Large windows, clean lines, decorative panels |
| `agriculture` | Growing sections, life support farms | Greenhouse material; distinct surface treatment |
| `fuel` | Fuel storage, refinery | Large cylindrical tanks, pipe clusters |

### Module definition

```csharp
public sealed class StationModuleDefinition
{
    public required string   Id              { get; init; }   // "hab-block-a", "docking-arm-large"
    public required string   Category        { get; init; }   // see categories above
    public          Vector3  BoundingBox     { get; init; }   // metres — AABB for intersection check
    public required Port[]   Ports           { get; init; }   // attachment and docking sockets
    public          float    SelectWeight    { get; init; } = 1.0f;  // weighted random bias
    public required StationScale MinScale    { get; init; }   // smallest station class that uses this
    public required Func<int, StationModuleMesh> MeshFactory { get; init; }
    // seed → geometry; called once at generation time, result cached per-instance
}

public sealed class StationModuleMesh
{
    public Vector3[] Vertices   { get; init; } = [];
    public int[]     Indices    { get; init; } = [];
    public Vector3[] Normals    { get; init; } = [];
    public Color[]   FaceColors { get; init; } = [];   // per-face flat shading
    public DecalTag[] Decals    { get; init; } = [];   // text and markings, added by pass 6
    public AnimTag[]  AnimTags  { get; init; } = [];   // animated elements, driven by renderer
}
```

### Port definition

```csharp
public sealed class Port
{
    public required string   Id                  { get; init; }   // unique within module
    public required Vector3  LocalPosition       { get; init; }   // relative to module origin
    public required Vector3  OutwardNormal       { get; init; }   // direction away from module
    public          PortSize Size                { get; init; } = PortSize.Medium;
    public          string[] AcceptsCategories   { get; init; } = [];  // empty = accepts any
    public          bool     IsDocking           { get; init; }   // registers as a landing pad
    public          bool     IsHangarEntrance    { get; init; }   // enclosed sub-bay entrance
    public          bool     IsTerminal          { get; init; }   // structural dead-end; never expanded
}

public enum PortSize { Small, Medium, Large, Massive }
```

### Placed module

```csharp
public sealed class PlacedModule
{
    public required StationModuleDefinition Definition { get; init; }
    public required Matrix4x4              Transform  { get; init; }  // world position + orientation
    public required int                    Seed       { get; init; }  // mesh-level variation seed
    public          int                    Depth      { get; init; }  // distance from core in graph
    public          List<OpenPort>         OpenPorts  { get; } = [];  // unattached ports after growth
}
```

---

## The growth engine

### Open station growth

The growth engine builds outward from a placed core module. All port-based expansion
follows this path.

```csharp
public sealed class StationGrowthEngine
{
    private readonly int                   _seed;
    private readonly IStationArchetype     _archetype;
    private readonly StationModuleRegistry _registry;
    private readonly List<PlacedModule>    _placed   = [];
    private readonly Queue<OpenPort>       _frontier = [];

    public PlacedModule[] Grow()
    {
        var core = PlaceCore();
        EnqueueOpenPorts(core);

        while (_frontier.TryDequeue(out var port) && _placed.Count < _archetype.ModuleBudget)
        {
            if (ShouldTerminate(port)) continue;

            var definition = SelectModule(port);
            if (definition == null) continue;

            var placed = TryPlace(definition, port);  // null on AABB intersection failure
            if (placed == null) continue;

            _placed.Add(placed);
            EnqueueOpenPorts(placed);
        }

        return [.. _placed];
    }

    // Termination probability rises with depth and budget consumption,
    // approaching 1.0 at the archetype's MaxDepth or when budget is nearly full
    private bool ShouldTerminate(OpenPort port)
    {
        float depthFactor  = port.Depth / (float)_archetype.MaxDepth;
        float budgetFactor = _placed.Count / (float)_archetype.ModuleBudget;
        float probability  = Math.Max(depthFactor, budgetFactor);
        return _rng.NextDouble() < probability;
    }
}
```

Archetype-specific rules are injected via `IStationArchetype`:

```csharp
public interface IStationArchetype
{
    int                     ModuleBudget      { get; }   // total module count ceiling
    int                     MaxDepth          { get; }   // max chain length from core
    float                   BranchProbability { get; }   // secondary port expansion rate
    Dictionary<string, float> CategoryWeights { get; }  // "cargo" → 2.5, "science" → 0.3 etc.
    string                  PreferredAxis     { get; }   // "X", "Y", "Z", or "None"
}
```

Key archetype personalities:

- `Cluster`: `PreferredAxis = "None"`, even weights — the most organic outcome
- `LinearSpine`: strong Y-axis bias, low branch probability — grows long, not wide
- `HubSpoke`: radial bias from core, arms do not re-branch back inward
- `Ring`: growth constrained to a circular path with fixed segment angle increment
- `Monolith`: single large-volume core with terminal ports on all faces except top and bottom

### Enclosed station interior generation

Enclosed archetypes do not use port-based growth for their interiors. After the solid
shell geometry is generated, a separate interior generation pass populates the enclosed
volume.

**Gravity direction:** interior gravity is single-direction — "down" is always toward
the station's primary floor, regardless of the outer solid's shape. This is a design
decision, not a physics constraint (see lore note in Overview). A sphere station has a
flat floor across its bottom third; the curved upper interior walls are structural
surface, not habitable floor.

```
1. Compute interior bounding volume (solid geometry minus shell thickness, ~8–15 m)
2. Define the interior floor plane — perpendicular to gravity "down", at lowest usable point
3. Project a 2D grid onto the floor (20 m × 20 m cells)
4. Run WFC on the floor grid using interior tile types (see table below)
5. Raise additional floor levels if interior height permits (Port and Megastation class)
   Each level is a copy of the floor grid at +N metres, narrowed to fit within the solid
6. Generate interior geometry per resolved tile
7. Apply interior decoration (separate, lighter decoration passes)
```

WFC is used *only here* — for interior tile resolution. The outer structure of every
archetype uses the growth engine. Interior tile adjacency rules for WFC:

| Tile | Can be adjacent to |
|------|-------------------|
| `pad-small` | `walkway`, `service`, `open` |
| `pad-large` | `walkway`, `service`, `open` |
| `walkway` | anything |
| `service` | `walkway`, `pad-small`, `pad-large` |
| `building-small` | `walkway` |
| `building-large` | `walkway` (occupies 2×2+ cells) |
| `open` | `pad-small`, `pad-large`, `walkway`, `open` |

WFC constraints ensure every pad is reachable from a walkway, services cluster near pads,
and large clear taxiway areas exist for ship movement between the portal and the pads.

---

## Decoration passes

Decoration runs after structure generation over all exposed surfaces. Passes are
independent and composable. Each receives the full `PlacedModule[]` array and adds
geometry in place. All passes are seeded — the same station seed produces the same
decoration result.

### Pass 1 — Windows

Applied to faces tagged `hab`, `luxury`, `science`, or `core`. Window distribution
pattern is seeded: rows-and-columns, irregular scatter, or strip windows. Interior
light states per window:

- **Warm yellow** — occupied crew or passenger space
- **Cold blue** — machinery or automated systems
- **Dark** — vacant, sealed, or on night cycle

Window density varies by economy type and category. Military modules use narrow slit
windows or none. Industrial modules use none. Agricultural modules use large greenhouse
panels — a distinct surface material rather than individual windows.

### Pass 2 — Hatches and airlocks

Service hatches (rectangular with compression ring detail), emergency airlocks
(circular, brighter surround frame), maintenance access panels (grid of bolts,
recessed handle). Distributed across non-window, non-port faces. Tagged
`animatable: hatch` where the renderer can open them during docking sequences.

### Pass 3 — Antennas and sensors

Placed on outward-facing, unoccluded surfaces — preferring `science`, `military`,
and `core` modules. Element types:

- **Parabolic dish** — static, or tagged `Rotate` with slow continuous rotation
- **Navigation beacon** — vertical spike, strobe light at tip
- **Hyperspace sensor** — lore-flavoured irregular geometry (crystalline, asymmetric shapes)
- **Communication array** — cluster of thin vertical elements at varied heights
- **Targeting array** — military modules only; directed geometry, distinct from navigation

All animated antennas carry an `AnimTag` (type `Rotate`, axis, rate in radians/sec,
seed-derived phase offset so they don't rotate in lockstep).

### Pass 4 — Pipes, cables, and conduits

Pipe runs along module edges and across connector modules. Three diameter classes:

- **Conduit** — 0.2 m diameter — data, low-power distribution
- **Pipe** — 0.5 m — coolant, life support, compressed gas
- **Duct** — 1.2 m — degenerate matter exhaust, major HVAC

Pipes cross between modules where modules share an edge, with fitting geometry
(flanges, clamps) at the junction. Ducts favour `industrial` and `core` modules.
This pass contributes more perceived visual density than any other single pass —
a station with pipes looks inhabited; without them it looks like a prototype.

### Pass 5 — Lights

- **Navigation lights** — red port, green starboard, white aft — at station extremities
- **Warning strobes** — amber, slow pulse — on faces near docking approach paths
- **Bay guidance** — white strip lights leading toward pad centres; on docking modules
- **Edge lighting** — dim continuous strips along module junction edges
- **Portal lighting** — on enclosed archetypes: coloured frame lighting per entry status

All lights carry an `AnimTag` with type `Strobe`, `Pulse`, or `Continuous`, a colour,
and a frequency. The renderer drives them from `GameClock.SimTime`. Nothing is baked.

### Pass 6 — Text and markings

Text is geometry — raised or inset quad faces driven by a font atlas. Not a texture.
This means text is visible at any angle and is consistent with the flat-shaded aesthetic.

- **Station name** — large block letters on the most prominent flat face facing the
  nominal approach direction; size proportional to face area
- **Bay numbers** — on docking approach faces and pad surfaces: "BAY 04", "PAD 12"
- **Warning text** — "CAUTION THRUSTER WASH", "RESTRICTED", "AIRLOCK — SUIT REQUIRED",
  "CLEARANCE HEIGHT 35 M"
- **Faction livery** — faction crest or emblem, procedurally simplified to flat geometry
- **Container labels** — generated per container if pass 7 places container stacks

Language is the in-universe lingua franca (a form of English). Numerals are arabic
throughout the galaxy — a First Age standardisation legacy so embedded it no longer
feels cultural.

### Pass 7 — Cargo and exterior equipment

Applied heavily on `cargo` and `industrial` modules, lightly elsewhere.

- **Container stacks** — rectangular cargo containers in clusters; held in nets,
  clamp rings, or simply resting against a surface (artificial gravity means stacking
  is unconstrained by orientation). Containers receive their own pass 6 labels.
- **Solar panels** — flat arrays on sun-facing surfaces. Lore basis: electricity still
  has uses for some station systems. Visually distinctive — thin flat planes offset
  from the hull.
- **Degenerate matter exhaust stacks** — vertical chimneys or horizontal ducts on
  `industrial` modules; a particle effect stub is noted for future renderer support.
- **Fuel tanks** — cylindrical, clustered, pipe-connected; on `fuel` modules.
- **Equipment crates and bundled storage** — ad hoc boxes, net-wrapped cargo.
- **Crane arms** — on `cargo` modules; articulated geometry, tagged `animatable: crane`.

### Pass 8 — Wear and history

Conveys age, traffic load, and faction history. Parameters driven by a `StationAge`
value (generated per station from system seed) and economy type.

- **Scorch marks** — near docking approach faces; from thruster wash and minor incidents
- **Patch repairs** — hull sections replaced at different times; slightly different base
  colour with visible seam geometry
- **Faded paint** — text and livery degrade over time; implemented as `colour × fade_factor`
- **Corrosion blooms** — geometry around pipe joints and conduit runs on older stations
- **Micro-impact pitting** — subtle surface variation on very old outer hull faces
- **Mismatched modules** — on expanded stations, later additions carry a different base
  colour suggesting a different build era or builder

Young outposts are clean. High-traffic trade ports are battered. Abandoned stations
are heavily worn, sparsely lit, and partially patched.

---

## Output model

```csharp
public sealed class StationModel
{
    // Identity
    public required string         Id              { get; init; }
    public required string         Name            { get; init; }
    public          StationScale   SizeClass       { get; init; }
    public required string         ArchetypeId     { get; init; }

    // Structure
    public required PlacedModule[] Modules         { get; init; }
    public          bool           IsEnclosed      { get; init; }

    // Docking
    public required LandingPad[]   LandingPads     { get; init; }
    public          HangarBay[]    HangarBays      { get; init; } = [];
    public          PortalSpec?    EnclosedPortal  { get; init; }  // null for open archetypes

    // Services
    public required StationServices Services       { get; init; }

    // Orbital position — relative to parent body centre
    public required DVec3          OrbitPosition   { get; init; }
    public required DVec3          OrbitNormal     { get; init; }  // orbit plane normal
    public          double         OrbitRadius     { get; init; }  // metres
}

public sealed class LandingPad
{
    public required int          PadNumber       { get; init; }
    public required Vector3      WorldPosition   { get; init; }
    public required Vector3      SurfaceNormal   { get; init; }  // "up" from pad surface
    public required Vector3      ApproachVector  { get; init; }  // direction ship arrives from
    public required PadSizeClass SizeClass       { get; init; }
    public          bool         IsInterior      { get; init; }  // inside an enclosed station
}

public sealed class HangarBay
{
    public required string       BayId             { get; init; }
    public required Vector3      EntrancePosition  { get; init; }
    public required Vector3      EntranceNormal    { get; init; }  // inward — approach direction
    public required Vector3      BayDimensions     { get; init; }  // metres
    public required LandingPad[] Pads              { get; init; }
    public          PadSizeClass MaxShipSize       { get; init; }
}

public sealed class PortalSpec
{
    public required Vector3 Centre  { get; init; }
    public required Vector3 Normal  { get; init; }  // inward approach direction
    public          float   Width   { get; init; }  // metres
    public          float   Height  { get; init; }  // metres
}

public enum PadSizeClass  { Small, Large }     // Large also accepts Medium ships
public enum StationScale  { Outpost, Station, Port, Megastation }
```

---

## Station services

Flag set on the station record. Service availability is determined by size class,
economy type, and faction. Not yet designed in detail — listed here for completeness.

```csharp
[Flags]
public enum StationServices
{
    None              = 0,
    Consumables       = 1 << 0,   // metal rods, coolant — always present at any station
    Refuel            = 1 << 1,   // reactor fuel
    Repair            = 1 << 2,
    Outfitter         = 1 << 3,   // components and equipment
    Shipyard          = 1 << 4,   // hull purchase and transfer
    Trader            = 1 << 5,   // commodities market
    MissionBoard      = 1 << 6,
    Cartography       = 1 << 7,   // navigation data broker
    InformationBroker = 1 << 8,
    Medical           = 1 << 9,
    Military          = 1 << 10,  // faction military presence; restricted docking
    BlackMarket       = 1 << 11,  // hidden; must be discovered in play
}
```

Consumables are always present at any station. Outposts typically add Refuel and Repair.
Full service tiers appear at Port class and above. `BlackMarket` is never visible on
station listings — it surfaces through gameplay only.

---

## Rendering integration

Station meshes are flat-shaded low-poly, consistent with the ship aesthetic. All
decoration geometry is part of the station mesh — no separate render passes or special
materials. The one exception is animated elements, which carry `AnimTag` data.

### Animation tags

```csharp
public sealed class AnimTag
{
    public required string   Id        { get; init; }
    public required AnimType Type      { get; init; }
    public required int[]    VertexIds { get; init; }   // affected vertices in module mesh
    public          Vector3  Axis      { get; init; }   // rotation axis (for Rotate)
    public          float    Rate      { get; init; }   // radians/sec or cycles/sec
    public          float    Phase     { get; init; }   // seed-derived offset; prevents lockstep
    public          Color    ColorA    { get; init; }   // on-state colour (for Strobe/Pulse)
    public          Color    ColorB    { get; init; }   // off-state colour
}

public enum AnimType { Rotate, Strobe, Pulse, Continuous }
```

The renderer applies transforms to tagged vertices each frame using `GameClock.SimTime`.
Animation tags are only processed at LOD 0 (close range).

### LOD

| LOD | Distance | Rendered content |
|-----|----------|-----------------|
| 0 | < 2 km | Full decoration geometry; animation tags active |
| 1 | 2–10 km | Base module geometry; pass 5 lights; pass 6 name text only |
| 2 | > 10 km | Single merged mesh, coloured by module category; no detail |

LOD 2 is generated at station creation time and cached. LOD 1 and 0 are built on
demand when the player enters range. Large stations at LOD 2 still read clearly from
distance because module category colours are perceptibly distinct — `military` dark
grey, `hab` warm off-white, `cargo` industrial yellow-brown, and so on.

---

## Implementation order

Each phase produces something renderable and testable before the next begins.

**Phase 1 — Infrastructure**
`StationModuleDefinition`, `Port`, `PlacedModule`, AABB intersection check.
One archetype (`Cluster`). Two module categories (`core`, `hab`) with placeholder
box meshes. Growth engine skeleton. Render the output as flat-shaded coloured blocks.
This is the foundation everything else rests on.

**Phase 2 — Archetypes and categories**
Add `LinearSpine` and `HubSpoke` archetypes. Add `connector`, `cargo`, and `docking`
categories. Register landing pads from docking ports. `StationModel` output complete
with `LandingPad[]`. Traffic control placeholder (pad number, approach vector usable
by autopilot).

**Phase 3 — Decoration passes 1–5**
Windows, hatches, antennas, pipes, lights. This is the phase that makes stations
feel inhabited. The `AnimTag` system enters here. By end of phase 3 stations are
visually distinguishable and lively.

**Phase 4 — Text and cargo (passes 6–7)**
Station name geometry, bay numbers, container stacks. Requires font atlas geometry
pipeline. Stations become individually recognisable and legible at approach distances.

**Phase 5 — Enclosed archetypes**
`Sphere` first — simplest interior volume to define. Add `Pyramid_4` and `Prism_6`.
Rectangular portal spec and portal lighting. Interior WFC grid. Add `Octahedron` and
`Dodecahedron` once the enclosed pipeline is stable. Archimedean solids last.

**Phase 6 — Wear and faction variation (pass 8)**
Age parameter, wear geometry. Economy-type category weights. Faction livery extension
in pass 6. `Ring` archetype (constrained radial growth). `Compound` archetype.

**Phase 7 — Services and persistence**
`StationServices` flags wired to economy and faction. Station records saved per-system.
`Megastation` size class full implementation.

---

## Open questions

| Question | Status |
|----------|--------|
| Capital ship tunnel docking — geometry and gameplay spec | Deferred |
| Archimedean solid selection — which beyond truncated icosahedron | Deferred |
| `Ring` archetype — partial ring permitted, or full ring only? Segment count range? | Open |
| Enclosed station multi-storey — vertical access geometry (lifts, ramps?) | Open |
| Station-to-station tethers or docking tubes between co-orbiting stations | Open |
| Traffic control — queuing, landing permission, ATC message flow | Deferred |
| Hostile station behaviour — portal lock, weapon deployment | Deferred |
| Station damage model — hull elements as for ships, or simplified per-module integrity? | Open |
| Economy type → archetype weight table — values TBD | Open |
| Faction → module category weight table — values TBD | Open |
| `BlackMarket` service — discovery mechanic design | Open |
| Interior multi-storey gravity — radial or single-direction? | **Resolved: single-direction.** Standardisation and infrastructure practicality. Radial gravity reserved as a rare landmark station trait. |

---

## Changelog

| Date | Change |
|------|--------|
| 2026-06-14 | Initial document — growth engine architecture, module system, archetype reference (open and enclosed), rectangular portal specification, all 8 decoration passes, WFC interior layout, output model, animation tags, LOD strategy, implementation order |
| 2026-06-14 | Interior gravity resolved: single-direction "down" by civilisational standardisation. Lore note added to Overview. Interior generation section updated with explicit gravity direction. Open question closed. |
