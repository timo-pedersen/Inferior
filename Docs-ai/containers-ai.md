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

`ManufacturerText` is rendered on **two opposing long faces** (e.g. Y+ and Y−) using
the existing station text pipeline. Position: lower quarter of the face, spanning the
inset zone width. Same font atlas geometry as station markings.

---

## Data model

Lives in `Inferior.Game`, same assembly as `StationModel`. If simulation needs to
query containers (sensors, physics), extract the data model to `Inferior.Gameplay`
at that point — not preemptively.

```csharp
public sealed class ShippingContainer
{
    public string           Id                 { get; init; }
    public Color            PrimaryColor       { get; init; }
    public float            Wear               { get; init; }  // 0.0–1.0
    public int              SidePatternSeed    { get; init; }
    public string           ManufacturerText   { get; init; }
    public ContainerContents? Contents         { get; init; }
    public LockGrade        Lock               { get; init; }
    public bool             IsLocked           { get; init; }

    // World state
    public DVec3            WorldPosition      { get; set; }
    public Quaternion       Orientation        { get; set; }
    public object?          Parent             { get; set; }  // null = free-floating

    // Rendered mesh (generated at factory time, pre-baked lighting)
    public VertexPositionColorTexture[] Vertices { get; init; }
    public short[]          Indices            { get; init; }
}

public sealed record ContainerContents(CommodityType Type, int Units);

public enum LockGrade { None, Civilian, Military, Vault }
```

`Parent` will eventually reference a ship hardpoint or station dock slot. For now,
null (free-floating) is the only production case. Position is always absolute world
position — parent-relative transform is a future concern.

---

## Factory API

```csharp
public static class ShippingContainerFactory
{
    /// <summary>
    /// Fully deterministic single container. If text is null, GenerateManufacturerName
    /// is called with sidePatternSeed and the result stored in ManufacturerText.
    /// </summary>
    public static ShippingContainer Generate(
        Color color,
        float wear,
        int sidePatternSeed,
        string? text = null);

    /// <summary>
    /// Deterministic batch. masterSeed drives all randomness — colours, wear, and
    /// pattern seeds are all derived from it. If sidePatternSeeds is provided, those
    /// seeds override per-container; the same selected seed is used for both pattern
    /// and manufacturer text (consistent company per batch).
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

Containers are placed in world space with a position and orientation. Three contexts:

| Context | Parent | Notes |
|---|---|---|
| Free-floating | null | Debris, ejected cargo, decoration |
| Station dockside | Station reference (future) | Part of station scene composition |
| Ship-attached | Ship hardpoint reference (future) | Follows ship; requires parent-delta sync |

For station decoration, the `StationDecorator` calls `ShippingContainerFactory.Generate`
using seeds derived from the station seed, placing clusters on cargo bays and docking
arms as a decoration pass. The containers become part of the station's rendered scene.

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
| `ShippingContainer` | `Inferior.Game` |
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
| Station dockside placement pass | Deferred — decorator pass to place container clusters on cargo/docking modules |
| Persistence | Containers near player saved as world exception objects |
