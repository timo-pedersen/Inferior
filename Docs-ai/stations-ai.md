# Inferior — Station Design Reference

> Compact AI-facing reference.
> Repository code and `architecture-map-ai.md` remain authoritative for exact identifiers and file locations.
> Stations are deterministic procedural universe objects: the same stable identity and generator version
> produce the same baseline station. Persist meaningful deltas, not generated mesh data.

---

## Design identity

Inferior stations are dark, industrial, asymmetrical, and dense with functional-looking detail.

Core visual traits:

- Strong silhouettes and large readable module masses
- Lit windows against mostly dark surfaces
- Pipes, conduits, antennas, vents, hatches, equipment boxes, and structural trim
- Sparse navigation, warning, guidance, and ambient glow
- Low-poly geometry with flat-shaded or deliberately simple material response
- No casual normalization toward bright, clean, generic science-fiction readability

Stations use hyperspace artificial gravity and therefore do not need to rotate for gravity.
Any current runtime rotation is presentation behaviour, not a design requirement.

---

## Architecture overview

Station construction has two principal levels:

**Macro — station growth**

`StationGenerator` and the archetype logic attach modules port-to-port to build the large-scale structure.

**Micro — station decoration**

`StationDecorator` adds independent decoration passes to exposed module faces after structural placement.

Generated baseline data includes:

- Placed modules and transforms
- Module hull geometry
- Windows and glass geometry
- Pipes, cables, antennas, greebles, vents, hatches, trim, markings, and lights
- Landing-pad and docking-bay metadata
- CPU-side mesh data required to create GPU buffers

Rendering is currently still integrated in `SystemSpaceState.Stations.cs`.
Extraction into a dedicated `StationSceneRenderer` remains a planned architectural cleanup.

---

## Determinism

Station identity must not depend on unrelated RNG consumption order.

Use stable semantic seed derivation for independent concerns such as:

- Structure and archetype
- Module geometry
- Windows
- Pipes and cables
- Antennas and greebles
- Lighting
- Wear and markings
- Docking-bay layout

Adding or changing one decoration subsystem should not reshuffle unrelated station structure.

Generated stations are normally regenerated from stable identity. Persist only meaningful deltas such as damage, destruction, ownership, discovered state, or exceptional history.

---

## Station size classes

| Class | Name | Module budget | Approximate pads | Character |
|---|---|---:|---:|---|
| 1 | Outpost | 8–20 | 1–4 | Small frontier facility |
| 2 | Station | 20–60 | 5–20 | Standard system hub |
| 3 | Port | 60–150 | 20–60 | Major traffic and service centre |
| 4 | Megastation | 150–400 | 60–200+ | Faction-scale infrastructure |

Megastation support remains incomplete in several subsystems. Do not assume every designed scale is currently reachable or fully implemented.

---

## Archetypes

### Implemented open archetypes

| Archetype | Character |
|---|---|
| `Cluster` | Organic growth without a dominant axis |
| `LinearSpine` | Long main axis with sparse branches |
| `HubSpoke` | Central mass with radial arms |

### Designed but not implemented

Open:

- `Ring`
- `Monolith`
- `Compound`

Enclosed:

- `Sphere`
- `Pyramid_N`
- `Prism_N`
- `Octahedron`
- `Dodecahedron`
- `ArchimedeanSolid`

Enclosed stations use a large rectangular or chamfered portal and separately generated interiors.
Interior gravity has one canonical down direction by civilisational convention.

---

## Module and port rules

A module definition supplies:

- Stable module identity and category
- Local bounding volume
- Attachment ports
- Scale eligibility
- Selection weight
- Mesh factory

A placed module supplies:

- Module definition
- Local-to-station transform
- Stable module seed
- Depth in the station graph
- Remaining open ports
- Decoration meshes and light registrations

Port invariants:

- `OutwardNormal` is a unit vector.
- `LocalPosition` is relative to the module origin.
- Connected port world positions coincide within tolerance.
- Connected port normals oppose within tolerance.
- Terminal ports are never expanded.
- Docking ports register landing-pad data regardless of module category.
- Twist for box-like modules is restricted to 0°/90°/180°/270° unless a module explicitly supports arbitrary rotation.

Critical attachment relation:

```csharp
Quaternion r1 = RotationBetween(childAttachNormal, -parentPort.WorldNormal);
Quaternion r2 = Quaternion.CreateFromAxisAngle(-parentPort.WorldNormal, twistAngle);
Quaternion combined = Quaternion.Normalize(r2 * r1);

Vector3 rotatedAttachPos =
    Vector3.Transform(childAttachPort.LocalPosition, combined);

Vector3 childOrigin =
    parentPort.WorldPosition - rotatedAttachPos;
```

---

## Representative implemented module families

The exact registry is authoritative in `StationModuleRegistry.cs`. Current station generation includes at least:

- Core hubs, including a large core used for larger stations
- Long and short connectors
- Habitation blocks
- Cargo modules
- Docking arms
- Science modules
- Industrial modules
- A generated hollow `docking-bay` module for Port-scale and larger stations

Do not rely on an old fixed count of eight modules; the registry has expanded.

---

## Docking bay

The current `docking-bay` is the first hollow station module.

Key design:

- Generated from a stable station seed and station scale
- Overall envelope derived from the packed landing-pad layout
- Medium pads: 36×36 m
- Large pads: 36×72 m
- Door width: 40 m
- Door height:
  - 16 m for medium-only bays
  - 24 m when large ships are supported
- Door opening is a seeded chamfered rectangle
- Exterior wall thickness is seeded in the 0.5–1.5 m range
- The door has an actual throat: geometry connects the outer and inner opening perimeters
- Interior side walls are subdivided along depth so the lighting gradient can render spatially
- A reserved approach volume prevents ordinary station growth from blocking the entrance
- Exterior decoration is enabled on the five solid outer walls
- Door-specific guidance lights and warning signage are added separately

Interior lighting is currently a deliberate approximation, not real local-light baking:

- Base ambient floor
- Door-proximity brightness
- Ceiling/floor orientation cue
- Seeded per-face corner variation

This approximation is disposable and may later be replaced by real interior lighting.

---

## Decoration system

`FaceOccupancy` prevents incompatible detail from overlapping. Passes that occupy surface area must reserve it; seams, edge trim, and some pipe systems may deliberately skip occupancy.

Current decoration families include:

- Windows and protruding window frames
- Hatches
- Antennas, dishes, Yagi arrays, and landmark antennas
- Surface pipes and U-brackets
- Edge pipes and clamps
- Cable/conduit routing between connectable endpoints
- Junction lights and bay guidance lights
- Warning strobes and aviation lights
- Chimneys
- Solar-panel arrays
- Panel seams
- Chamfered edge trim
- Horizontal, louvered, and screen-mesh vents
- Junction boxes, equipment housings, conduit entries, sensor pods, tech panels, and valve assemblies
- Navigation lights and ambient markers
- Station text and markings
- Docking-bay-specific signage and throat guidance lights

Decoration ordering matters. Geometry-affecting passes must remain deterministic.

---

## Pipes, cables, and antennas

### Pipes

Prism pipes use separate lateral faces with separate normals so directional lighting reads as rounded:

- 4-sided: square/industrial
- 6-sided: common and visually convincing
- 8-sided: smoother silhouette

Very thin pipe and cable shadows may be unstable or disappear in the current 2048² shadow map. That is acceptable; large structure and medium greeble shadows matter more.

### Cables and conduits

Cable bundles are routed on module faces between connectable endpoints such as:

- Junction boxes
- Antenna bases
- Dish bases
- Conduit entries

They use grid-like routing, fasteners, and edge clamps. They are visual geometry, not simulated electrical networks.

### Yagi antennas

The current Yagi system supports multiple element and base types, tilted booms, solid disc elements, seeded brightness variation, and cable connectivity.

---

## Lighting model

One star is the sole directional light for station exterior lighting.

Scene-level parameters live in `SceneLighting`:

```csharp
public static class SceneLighting
{
    public static Vector3 SunDirection { get; set; }
    public static float   Ambient      { get; set; }
    public static Vector3 SunColour    { get; set; }
}
```

Station surfaces are textured. The effective albedo is:

```text
diffuse texture × vertex colour
```

Vertex colour remains useful for:

- Procedural tint
- Wear/grime modulation
- Pre-baked AO
- Existing baked directional-light contribution where still present
- Other per-face variation

Do not repurpose vertex alpha for shadow or material data. Transparency is reserved for future windows, lamps, and other transparent surfaces.

The preferred material simplification is **one mesh = one material**.

Future specular targets:

| Surface | Character |
|---|---|
| Painted station module | Broad, weak highlight |
| Sand-blasted metal | Moderate width and strength |
| Window/glass | Sharper highlight |
| Emissive lamp/glow | Separate unlit/additive treatment |

No packed material texture is planned. Per-mesh specular parameters are sufficient unless a future concrete need proves otherwise.

---

## Static station shadow map

Static station shadows are now implemented experimentally through a custom shader path.

### Scope

Current shadow map:

- 2048×2048
- One station-only map
- Built from the star direction
- Intended for static module and greeble shadows
- Ships, moving containers, and other dynamic objects are excluded
- Dynamic-object shadows are deferred to a separate later pass

All real static station geometry may act as casters:

- Hull modules
- Protruding windows and frames
- Antennas
- Pipes and conduits
- Greeble boxes
- Docking structures
- Other static decoration

Very small casters may not produce stable shadows at the current map resolution.

### Ownership and lifecycle

`StationShadowMap` is renderer/state-owned GPU state, not part of `StationModel`.

Creation and disposal are tied to station geometry rebuild/disposal. Mid-session system changes rebuild station geometry and the associated shadow maps rather than retaining old GPU resources.

A temporary F9 debug view displays the first station shadow map.

### Coordinate space

The shadow map is built in station-local metre space.

Normal station rendering still uses origin-shifted/render-scaled coordinates. Caster and receiver transforms must remain mathematically consistent despite those two render spaces.

### Depth format and encoding

Current preferred render-target format:

```text
SurfaceFormat.Single
```

Fallback:

```text
HalfSingle
```

Hardware raster depth remains `Depth24`.

Stored shadow depth uses explicit linear normalized orthographic light-view depth:

```text
(-lightViewZ - near) / (far - near)
```

Caster write and receiver comparison must use the same:

- Light-view matrix
- Sign convention
- Near/far values
- Station-local metre coordinate frame

### Bounds and padding

Bounds include:

- Hull AABBs
- Actual decoration vertices
- Actual glass vertices

Current light-space padding:

```text
XY padding: 25 m
Z padding:   2 m
```

XY padding is deliberately generous to avoid clipping antennas and protruding detail.
Z padding is kept much tighter for depth precision.

### Current bias

Current receiver-side bias values:

```text
BaseShadowBias  = 0.00030
SlopeShadowBias = 0.00120
MaxShadowBias   = 0.00150
```

The slope factor is based on the receiver normal and star direction.

These values are experimental and not visually accepted.

### Current regression / active investigation

The precision pass removed coarse banding but produced a finer and less coherent failure pattern:

- Fine striping on only some sun-facing triangles or portions of faces
- Missing shadows in areas that appear occluded
- Greeble shadows appearing inconsistently
- Small-object shadows often absent or visually unrelated to casters
- Behaviour changed materially after replacing 8-bit colour depth with floating-point linear depth

This is no longer assumed to be a simple bias-tuning problem.

Likely diagnostic targets:

- Caster and receiver depth paths not being mathematically identical
- Module/triangle world transform mismatch
- Incorrect or inconsistent receiver normals feeding slope bias
- Bias still representing an excessive physical distance across the fitted Z range
- Actual runtime render-target format differing from the requested format
- Shadow UV/depth derivation mixing station-local metres and render-scaled coordinates

Before further tuning, use diagnostic shader modes:

- Zero-bias binary shadow factor
- Stored caster depth
- Receiver normalized depth
- Receiver minus caster depth
- `dot(normal, lightDirection)` / slope factor
- Runtime log of actual created shadow-map format

Do not increase shadow-map size, exclude pipes, alter station rotation, add filtering, or start specular work until this regression is understood.

---

## Shadow rebuild strategy — deferred

The current implementation assumes the star and station orientation are effectively static relative to one another.

If that assumption changes, possible rebuild policies include:

- Rebuild when station orientation differs from the baked orientation by roughly 1–3°
- Rebuild at a fixed interval, such as every 2–3 seconds
- Rebuild when a station approaches rendering relevance, such as around 20 km
- Combine angular threshold and approach-triggered rebuild

No policy has been selected. Do not make one permanent without a specific brief.

---

## Future dynamic shadows

Ships, moving containers, and other dynamic objects should eventually use a separate local shadow pass rather than forcing the static station map to rebuild every frame.

Likely characteristics:

- Local coverage around the relevant docking/interaction area
- Rebuilt dynamically
- Higher local texel density
- Sampled in addition to the static station map

This is deferred until the static station shadow path is stable.

---

## Glow rendering

Station glows are separate from the lit textured mesh pass.

`StationLightInfo` records include:

- Position
- Colour
- Glow type
- Base intensity
- Rate
- Phase
- Pattern

Patterns include:

- Continuous
- Strobe
- Slow pulse
- Heartbeat

Glow sprites are drawn with additive blending after the 3D station geometry.

Current glow types include navigation, warning, aviation, ambient, and dedicated docking-guidance lights.

---

## Output model

Representative structure:

```csharp
public sealed class StationModel
{
    public string         Id          { get; init; }
    public string         Name        { get; init; }
    public string         ArchetypeId { get; init; }
    public StationScale   SizeClass   { get; init; }
    public PlacedModule[] Modules     { get; init; }

    public LandingPad[]   LandingPads { get; init; }
    public HangarBay[]    HangarBays  { get; init; }

    public StationServices Services   { get; init; }

    public DVec3  OrbitPosition { get; init; }
    public DVec3  OrbitNormal   { get; init; }
    public double OrbitRadius   { get; init; }
}
```

Exact current properties must be verified against code.

---

## Current architectural cleanup

Planned:

- Extract station mesh, dot, glow, and shadow rendering from `SystemSpaceState.Stations.cs`
- Create a dedicated station scene renderer under `Inferior.Rendering`
- Preserve existing station generation ownership in `Inferior.Game/Station`
- Do not move gameplay/domain state into rendering merely to simplify extraction

---

## Known gaps and deferred work

| Feature | Status |
|---|---|
| Static station shadow correctness | Experimental; active regression investigation |
| Station specular highlights | Deferred until shadows are stable |
| Dynamic ship/container shadows | Deferred; separate future pass |
| Station renderer extraction | Planned |
| Weathering pass | Designed direction only |
| More module shape variety | Incomplete |
| Ring/Monolith/Compound archetypes | Designed, not implemented |
| Enclosed archetypes | Designed, not implemented |
| Services/economy integration | Incomplete |
| Station persistence deltas | Not implemented |
| Capital-ship servicing structure | Deferred |
| Real docking-bay interior lighting | Deferred |
| Full transparent windows/lamps | Deferred |

---

## Working rules for station changes

- Preserve deterministic generation.
- Preserve the dark industrial visual identity.
- Do not replace working detail with generic readability.
- Do not build a generic material framework without a concrete need.
- Keep one mesh = one material unless a later requirement proves insufficient.
- Keep shadow-map GPU state out of `StationModel`.
- Do not claim visual success without Timo’s in-engine confirmation.
- For geometry changes, test winding, normals, degeneracy, port alignment, and collision/clearance volumes.
- For shadow changes, verify caster and receiver transforms before tuning bias.
