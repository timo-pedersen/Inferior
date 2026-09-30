# Inferior — Shipping Container Design Reference

> Compressed reference for AI.
> Containers are generated objects — universe items, not tied to any parent system.
> Same deterministic generation pipeline as stations.

---

## Overview

Shipping containers are physical universe objects. They appear on station docks, in
cargo bays, attached to ships, and floating free in space. They are never abstracted
to numbers — the container is a thing that exists, has a position, a history, and
contents that the player may or may not be allowed to access.

### W1 world-object implementation

Free/floating containers are the first users of the general world-object layer:

- `Inferior.Core/World/WorldObjectId.cs` defines persistent simulation identity. IDs may be
  generated or derived deterministically from semantic identity; they never contain renderer
  or physics-engine handles.
- `WorldObject` holds the minimal common kinematic state: double-precision position, explicit
  quaternion orientation, linear velocity, and universe-space angular velocity.
- `WorldObjectRegistry` is privately owned by `SpaceSimulation`. Only the simulation mutates
  registered state; its current integrator is deliberately limited to collision-free
  `position += velocity * dt` plus constant angular motion.
- `ShippingContainer` is immutable container-specific domain data and carries the matching
  `WorldObjectId`. Its geometry is a separate `ShippingContainerGeometry` presentation value.
- `SpaceSimulation.SimulationPresentationSnapshot` is the sole volatile presentation boundary.
  Once per completed simulation tick it atomically publishes one generation containing the
  matching `ShipSnapshot` and `WorldPresentationSnapshot`; system/world rebuilds do not publish
  independently. Each `ShippingContainerSnapshot` pairs a `WorldObjectSnapshot` with immutable
  container domain data. `SystemSpaceState` reads the envelope once per update, and
  `SystemSpaceState.Containers.cs` owns only GPU buffers keyed by ID and draws that generation's
  snapshot pose.

A future Jolt body is a temporary runtime representation of a `WorldObject`; it is not the
object's identity or canonical state.

---

## Physical specification

| Property | Value |
|---|---|
| External dimensions | 2.5 × 2.5 × 6.0 m |
| Internal volume | ~30 m³ |
| Edge chamfer | 0.20 m (all 12 edges) |
| Inset zone (long faces) | Centre 4.0 m; 1.0 m plain at each end |
| Inset depth | 3–5 cm (seeded) |
| Groove width | 1–3 cm (seeded) |

The container is **symmetric on all four long sides** — no preferred up or down.
Both end faces are identical. The geometry and all decoration is driven from a single
`SidePatternSeed`; the long faces are identical to each other, and the end faces are
identical to each other.

---

## Geometry — outer shell

A chamfered box. All 12 edges receive a 0.20 m chamfer at 45°. This produces:

- 4 long main faces (Y−, Y+, Z−, Z+ faces; 2.5 × 6.0 m nominal → octagonal after chamfer)
- 2 end main faces (X− and X+; 2.5 × 2.5 m nominal → octagonal after chamfer)
- 12 chamfer strip faces — narrow rectangles along each edge
- 8 corner triangles — one per vertex, where three chamfer faces meet

Total base faces: 26. The same winding order convention used for station modules
applies here — verify per the established project convention.

Chamfer strip width ≈ 0.20√2 ≈ 0.283 m. On long edges, strips run (6.0 − 2 × trim)
in usable length; on short edges, (2.5 − 2 × trim). Corner triangles fill the gaps.

---

## Fasteners

Embedded in the chamfer strips. Must not protrude beyond the adjacent face planes —
two containers placed side by side must sit flush.

| Edge type | Fasteners per edge | Positions |
|---|---|---|
| Long (6.0 m, × 4 edges) | 2 | At 1/3 and 2/3 along length |
| Short (2.5 m, × 8 edges) | 1 | At midpoint |
| **Total** | **16** | |

Fastener geometry at Code's discretion — small rectangular recess or flush-mounted
ring fitting. Suggest a two-quad recess: shallow backing + surround frame, similar in
principle to hatch geometry on station modules.

---

## Surface detail — long face insets

Applied to the **centre 4.0 m** of each long face only. The 1.0 m zones at each end
remain at the base surface level (plain, no insets).

Inset parameters are derived from `SidePatternSeed`:

| Parameter | Range | Notes |
|---|---|---|
| `insetCols` | 1 – 4 | Columns across the 2.1 m face width |
| `insetRows` | 1 – 8 | Rows along the 4.0 m inset zone |
| `insetDepth` | 3 – 5 cm | Uniform across all cells |
| `grooveWidth` | 1 – 3 cm | Between cells and at zone edges |

Style range that naturally emerges from parameter variation:

| insetCols / insetRows | Character |
|---|---|
| 1 × 6–8, shallow | Corrugation analogue — industrial, workhorse |
| 2 × 3–4 | Utilitarian panel look |
| 3–4 × 4–6 | Modular, tech-panel, Star Trek register |
| 2–3 × 2, deep | Structural, heavy-cargo look |

**All four long faces share the identical pattern.** This is lore-consistent: the
container is manufactured in a single press run.

### Inset geometry construction

The inset zone occupies an inner rectangle of the long face. The face tessellation:

1. Plain strip at each end (1.0 m), triangulated as quads
2. Thin frame border around the inset zone (grooveWidth), at base surface level
3. Inter-cell ridges (grooveWidth), at base surface level
4. Per cell: four inset wall faces (N/S/E/W), each `insetDepth` deep; one floor face

The entire assembly stays at surface level or below — nothing protrudes.

---

## Surface detail — end faces (doors)

Both end faces (X− and X+) are identical. Fixed design, no seeded variation. Code
has full discretion on the door aesthetic — two-panel hinged door with visible latch
hardware is the design intent. The result will be reviewed and refined after a first
pass. Should read clearly as "this is the opening end."

---

## Colour, wear, and text

### Colour

One `PrimaryColor` per container. Applied as base vertex colour. No secondary colour
for the base shell; inset floors and wall faces may be slightly darker to differentiate
them from the surface.

### Wear

`Wear` is a float in `[0.0, 1.0]`. Applied to vertex colours at generation time,
same pre-baking approach as station surfaces. Suggested interpretation:

| Wear range | Effect |
|---|---|
| 0.0 – 0.2 | Pristine — even colour, sharp edges |
| 0.2 – 0.5 | Used — slight darkening at edges, minor grime |
| 0.5 – 0.8 | Worn — visible discolouration, edge lightening (exposed substrate) |
| 0.8 – 1.0 | Derelict — heavy grime, significant edge damage, streaks |

### Text

`ManufacturerText` is retained as domain data, but it is not currently baked into the
container mesh. The free-container path still draws the projected-surface-marking proof
decal (`"TEST 123"`) on the Z+ inset face. Manufacturer marking placement remains future
presentation work.

---

## Data model

Container-specific data lives in `Inferior.Game`; general world identity/state lives in
`Inferior.Core/World` so it has no dependency on container, rendering, or a physics engine.

```csharp
public sealed record ShippingContainer
{
    public WorldObjectId    Id                 { get; init; }
    public string           Name               { get; init; }
    public Color            PrimaryColor       { get; init; }
    public float            Wear               { get; init; }  // 0.0–1.0
    public int              SidePatternSeed    { get; init; }
    public string           ManufacturerText   { get; init; }
    public ContainerContents? Contents         { get; init; }
    public LockGrade        Lock               { get; init; }
    public bool             IsLocked           { get; init; }

}

public sealed class WorldObject
{
    public WorldObjectId Id              { get; }
    public DVec3         Position        { get; }
    public Quaternion    Orientation     { get; }
    public DVec3         LinearVelocity  { get; }
    public DVec3         AngularVelocity { get; }
}

public sealed record ContainerContents(CommodityType Type, int Units);

public enum LockGrade { None, Civilian, Military, Vault }
```

W1 has no `Parent` field or relationship enum. Free-container position is absolute
system-space state in `WorldObject`; W1b will introduce attachment/reference-frame data
without merging relationship state with physics activation state.

---

## Factory API

```csharp
public static class ShippingContainerFactory
{
    /// <summary>
    /// Deterministic visual/domain properties for a supplied seed. If objectId is null,
    /// a fresh identity is created for this new object.
    /// </summary>
    public static ShippingContainer Generate(
        Color color,
        float wear,
        int sidePatternSeed,
        string? text = null,
        LockGrade lockGrade = LockGrade.Civilian,
        WorldObjectId? objectId = null,
        string? name = null);

    public static ShippingContainerGeometry GenerateGeometry(
        ShippingContainer container);

    /// <summary>
    /// Deterministic batch visual/domain properties. New object IDs are allocated for
    /// the returned instances; regenerated persistent objects use the single-object API
    /// with supplied IDs.
    /// </summary>
    public static ShippingContainer[] Generate(
        int count,
        int masterSeed,
        Color[] colors,
        (float min, float max) wearRange,
        int[]? sidePatternSeeds = null);

    /// <summary>
    /// Generates a plausible cargo company name from seed. Deterministic.
    /// </summary>
    public static string GenerateManufacturerName(int seed);
}
```

Visual/domain generation is deterministic for the supplied visual seed. Callers that
regenerate a persistent object must also supply its existing or semantically derived
`WorldObjectId`; the factory creates a fresh ID only for genuinely new ad-hoc objects.

---

## Manufacturer name generation

Word-pool approach, all driven from seed via `SeededRandom`. Format:

`[Prefix?] CoreNoun [of PlaceName?] Suffix`

**Word pools (suggested — Code may extend):**

| Pool | Words |
|---|---|
| Prefix (60% chance) | Intergalactic, Galactic, Interstellar, Deep Space, Rapid, Swift, Heavy, Standard, Universal, Frontier, Colonial, Hyperspatial, Outer Rim, Femtometer, Quantum |
| CoreNoun | Shipping, Transport, Transportation, Cargo, Freight, Haulage, Logistics, Forwarding |
| Suffix | Company, Ltd, Corp, Co., Co-operative, Group, Holdings, Associates, Alliance |
| PlaceName (40% chance) | Generated procedurally: 2–3 syllable alien place name from same syllable pools used for star names |

Examples of expected output:
- "Intergalactic Shipping of Andormin Ltd"
- "Femtometer Transportation Co."
- "Rapid Cargo of Vethrix Group"
- "Hyperspatial Forwarding Associates"
- "Freight of Kalund Holdings"

---

## Commodity types (stub)

`CommodityType` is a placeholder enum for now. Will expand when the economy system
is designed. Ship computer tracks density per commodity type to derive mass and volume
from unit count — containers never instantiate individual items.

---

## Lock grades

| Grade | Meaning | Future mechanic |
|---|---|---|
| `None` | No lock — freely accessible | Any player can open |
| `Civilian` | Standard commercial lock | ShippingModule can open |
| `Military` | High-security, licensed | Military-grade ShippingModule only |
| `Vault` | Maximum security | Future: specialist hacking module |

`IsLocked` is the runtime state; `Lock` is the grade. A `None`-grade container can
still be `IsLocked = true` transiently (e.g. magnetically sealed during transit) but
any ShippingModule can release it.

---

## World placement

Independently existing containers are registered in `SpaceSimulation` with absolute
system-space state. Three eventual relationship contexts remain:

| Context | Parent | Notes |
|---|---|---|
| Free-floating | none | Implemented for the current near-station container population |
| Station dockside | Station reference (future) | Part of station scene composition |
| Ship-attached | Ship hardpoint reference (future) | Follows ship; requires parent-delta sync |

The existing station-decoration paths still call the canonical container geometry generator
and merge those meshes into station presentation. They are not independent `WorldObject`
instances in W1. Attachment/anchor work will decide when such presentation becomes a real
object relationship rather than baked decoration.

---

## Ship hardpoints (future — design only)

Ship classes will define named external container hardpoints as position + orientation
offsets (similar to weapon hardpoints). The `ShippingModule` ship component unlocks
the ability to use them. Hardpoint definition on the ship class specifies how many
containers that hull can carry and where they sit. Free-form attachment is not planned.

### ShippingModule (future)

Ship component. Registered on the power bus. Gives player the ability to attach and
detach containers from/to hardpoints. Actions: `Attach(hardpointId, container)`,
`Detach(hardpointId)`. Checks `IsLocked` and `Lock` grade against module capability
before allowing detach. Container ownership tracking lives in the persistence layer.

---

## Assembly location

| Class | Assembly |
|---|---|
| `WorldObjectId`, `WorldObject`, `WorldObjectRegistry`, `WorldObjectSnapshot` | `Inferior.Core` |
| `ShippingContainer`, `ShippingContainerGeometry` | `Inferior.Game` |
| `ContainerContents` | `Inferior.Game` |
| `LockGrade` (enum) | `Inferior.Game` |
| `ShippingContainerFactory` | `Inferior.Game` |
| `CommodityType` (enum, stub) | `Inferior.Game` (move to `Inferior.Gameplay` when economy designed) |

---

## Not yet implemented

| Feature | Notes |
|---|---|
| Ship hardpoints | Defined, not implemented |
| ShippingModule | Designed, not implemented |
| Parent-relative transform (container follows ship) | Deferred — needs entity relationship system |
| Container stacks (ShippingContainerStack) | Deferred — set of containers magnetically locked together |
| Lock hacking module | Deferred — override Vault-grade locks |
| Cargo simulation / economy | Deferred — CommodityType is a stub enum |
| Independent station dockside objects | Deferred — existing station containers are baked decoration, not registered world objects |
| Persistence | Containers near player saved as world exception objects |
| Attachment/reference frames | Deferred to W1b; W1 world state is absolute system space |
| Physics representation | Deferred to W2; no Jolt dependency, body ID, shape, mass, collision, or response exists |
| Coasting/physics-active runtime mode | Deferred; remains orthogonal to future Free/Attached relationships |
