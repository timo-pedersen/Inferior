# Projected Surface Markings — Implementation Brief

> Original design brief from Timo (2026-09-13), preserved verbatim for future reference.
> Implementation status: see `Docs-ai/!current-state.md`'s "Projected surface markings" entry.

## Purpose

Implement the first generic projected surface marking / decal system for Inferior.

The system allows text or graphics defined in a two-dimensional projector frame to be applied
across arbitrary receiving geometry without modifying the underlying object mesh.

Initial proof case: Project `TEST 123` across one long face of a standard shipping container,
including its recessed cells, grooves, inset walls and existing triangulation.

The important result is not typography quality. The important result is proving that one
marking can visually span many unrelated receiver triangles and changes in surface depth.

This is intended as a generic visual building block for later use on:

- containers;
- ships and armour panels;
- stations and megastations;
- landing districts;
- Bolon structures;
- logos and faction markings;
- warning symbols;
- wear, grime, rust and damage markings.

## Existing systems

Do not replace or expand `PlanarTextGeometry` to perform this job. `PlanarTextGeometry`
remains the authority for literal bitmap-font geometry placed on a known plane. The current
project state explicitly treats font-atlas/decal typography as a separate concern.

Containers are a useful test object because their long faces already consist of plain outer
regions plus recessed cells 3-5 cm deep, with inset walls and floors.

Avoid per-container or per-marking texture creation. Previous profiling established repeated
`Texture2D.SetData` synchronization as an important upload cost.

## Core model

A projected marking is defined independently from the receiving mesh.

Conceptually:

```text
ProjectedSurfaceMarking
    transform / projector frame
        origin
        right
        up
        projection direction

    width
    height
    projection depth

    atlas region
    colour / tint

    receiver policy
```

Exact type names and integration points should be chosen after auditing the current code.

The projector defines a 2D coordinate system plus a finite depth volume. The underlying
receiver mesh remains unchanged.

## Geometry generation

This is a CPU-generated overlay mesh, not a runtime screen-space/deferred decal system.

For every eligible receiver triangle intersecting the projector volume:

1. Transform the triangle into projector-local space.
2. Clip it against the marking's width, height and projection-depth bounds.
3. Generate only the surviving polygon.
4. Triangulate that clipped polygon as decal geometry.
5. Preserve the physical receiver surface position and receiver normal.
6. Generate marking UVs from projector-space coordinates.
7. Add the result to a decal/marking mesh.

The receiver triangle is never split, replaced or rewritten. Only the new overlay geometry is
clipped/tessellated.

Conceptually:

```text
receiver mesh                       marking mesh

triangle A -------------------+
triangle B -------------------+--> clipped overlay triangles
triangle C -------------------+
inset wall -------------------+
inset floor ------------------+
```

## Projection behaviour

This is not a physical spray-paint simulation. Do not reject surfaces according to incidence
angle.

A marking should continue over:

- flat outer surfaces;
- recessed floors;
- bevels;
- grooves;
- inset side walls;
- surfaces approximately 90 degrees from the primary decal plane.

For a wall exactly perpendicular to the primary surface, the projected coordinate naturally
becomes constant in one axis. The edge colour therefore carries down the wall. That is desired.

The intended visual interpretation is: the marking belongs to and follows the physical object's
surface. Not: a perfectly flat image floats slightly above the object.

Do not introduce a normal-angle cutoff in this first implementation.

## Receiver filtering

The system must eventually support semantic receiver filtering, but keep the first
implementation small.

The architecture must not assume every triangle in the projection volume is a valid receiver.

The eventual model should allow surfaces such as these to receive markings:

```text
container shell
structural hull
armour panel
painted structural surface
inset floor / wall
```

while allowing things such as these to reject them:

```text
glass
windows
lights
cables
pipes
antennas
free-standing machinery
```

For the initial container proof, use the smallest coherent receiver-selection mechanism
compatible with the existing container geometry. Do not create container-ID-specific behaviour.

## Atlas and alpha

Use a shared RGBA texture/atlas. Do not generate a unique texture for `TEST 123` or for each
object.

For text:

```text
RGB   = glyph/marking colour
Alpha = glyph coverage
```

Transparent texels must expose the underlying object normally.

This same architecture should later permit partially transparent surface effects such as:

- faded paint;
- rust;
- grime;
- soot;
- scorch marks;
- removed/overpainted lettering.

Do not implement those effects now. Only ordinary alpha-blended marking rendering is required
for this pass.

## Typography

Initial content: `TEST 123`.

Use an existing suitable bitmap-font atlas if practical. The test text should be deliberately
large enough to cross:

- multiple generated container cells;
- inset walls;
- inset floors;
- grooves/ridges;
- multiple existing triangles.

Do not spend time creating final fonts, icons or graphic assets. Typography and art variety
come after the projection mechanism is accepted.

## Depth / Z fighting

The marking geometry will lie effectively on the receiver surface. Use the least intrusive
reliable solution for avoiding Z fighting.

Preferred investigation order:

1. appropriate rasterizer/depth bias;
2. if that proves unreliable under the current MonoGame/DesktopGL path, an extremely small
   receiver-normal offset on decal vertices.

Any geometric offset must be visually negligible and must follow the receiver normal, not the
projector direction. Do not visibly separate the marking from the surface.

## Lighting

The overlay should visually belong to the receiver. Preserve/use the receiver's surface normal
for generated decal vertices so a marking crossing a bevel or inset wall receives the same
directional-light orientation as that physical surface. The decal should not appear self-lit.
No new runtime light system is required.

## Batching and lifetime

Treat the generated marking triangles like other generated presentation geometry.

The longer-term intended architecture is:

```text
marking descriptions
        ↓
text / icon / graphic composition
        ↓
projector
        ↓
receiver clipping
        ↓
combined decal mesh
        ↓
normal render path
```

Do not create one draw call per character or one GPU resource per decal if the existing
architecture allows compatible geometry to be combined. Inferior already deliberately batches
substantial generated megastation presentation geometry rather than introducing large numbers
of per-object draws. For this first pass, optimize only enough to preserve that architectural
direction.

## Determinism

If the proof marking is attached through procedural generation, preserve Inferior's
deterministic-generation rules. Do not consume unrelated RNG streams or use runtime-unstable
hashing. The marking system itself should not require randomness.

## Non-goals

Do not implement:

- final font catalogue;
- Bolon font;
- icon library;
- logos;
- faction graphics;
- rust;
- grime;
- scorch marks;
- dynamic bullet-hole decals;
- animated decals;
- runtime moving projectors;
- arbitrary decal editing UI;
- texture generation per object;
- modifications to the receiver mesh;
- general restructuring of station/container rendering;
- replacement of `PlanarTextGeometry`.

Those are later applications of the same substrate.

## Tests

Add focused tests for the projection/clipping geometry. At minimum verify:

- generated vertices are finite;
- generated triangles have valid non-zero area;
- UV coordinates remain finite;
- a projector crossing multiple receiver triangles produces geometry on all relevant pieces;
- clipping does not emit geometry outside projector width/height/depth;
- perpendicular inset walls are not rejected;
- receiver normals are preserved correctly;
- winding matches the project's rendering convention;
- deterministic input produces deterministic output;
- receiver geometry itself is unchanged.

Use small synthetic fixtures for most geometry tests rather than production-scale container
generation where possible.

## Visual acceptance test

Use one ordinary shipping container. Place a large `TEST 123` across a long side. Choose or
force a container pattern where the text visibly crosses several recessed cells. Inspect
close-up from oblique angles.

### Pass criteria

The pass is accepted when:

1. `TEST 123` reads as one continuous marking across the whole target region.
2. It follows plain surfaces, inset floors and inset walls.
3. Existing container tessellation is not visually apparent through discontinuities in the
   marking.
4. There is no obvious Z fighting.
5. There is no visible floating gap between marking and receiver.
6. Lighting changes naturally as the marking crosses differently oriented surfaces.
7. Transparent parts of the atlas reveal the original material normally.
8. The container base mesh is not modified.
9. No unique per-container texture upload is introduced.
10. The implementation is generic enough that the same projector can later target ship or
    station receiver geometry without knowing what a container is.

## Stop point

Once `TEST 123` works convincingly across the recessed container side, stop. Do not immediately
add variety.

The next design step will be based on the visual result and can then address:

- text composition;
- shared font catalogue;
- icons;
- logos;
- semantic receiver classes;
- multiple blend modes;
- very large station markings;
- Bolon glyph presentation;
- surface-weathering decals.
