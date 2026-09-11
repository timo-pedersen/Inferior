# Megastation Service Channels

## Status

SC1's false-trench visual language, winding, full-length cable bundles, materials, and selective
shadows are visually accepted. SC2 planar service-channel networks are visually promising. SC2a
junction-node presentation and channel-rich surface density are implemented in the current
uncommitted working tree and await Timo's in-engine visual acceptance.

Current SC2 uses the canonical planar-region substrate after accepted Fabric planning. Each
selected surface receives one deterministic rectilinear network with a long primary trunk,
secondary branches, occasional turns and T/four-way junctions, intentional dead-end treatments,
and sparse service bridges. Planning rejects existing G1/G2/mega-greeble/Fabric/window/light
content. Geometry remains one material-grouped visible mesh plus one selective caster mesh,
borrows M1 materials, and owns zero textures. It does not modify structural occupancy, topology,
collision, or any accepted upstream plan.

SC2a classifies selected networks as light or channel-rich without changing semantic surface-role
weighting. Channel-rich networks retain the SC2 primary trunk and add deterministic secondary
route opportunities. Most supported T junctions receive one of three substantial covered utility
node variants; four-way junctions use the same grammar. The planned node record owns authoritative
orientation and dimensions, and full housing footprints reject exact-mask/upstream/node collisions.
Covered machinery hides the internal cable split, remains in the combined material mesh, and adds
only major masses to the existing selective caster. Bridges reject covered-node footprints.

This concept is intentionally separate from structural megastation topology.

## Motivation

Megastations contain enormous planar surfaces. Even ordinary station modules tens of metres across can look tiny when attached to a megastation slab hundreds of metres wide.

Windows, lighting, attached buildings, and local machinery help, but another architectural scale is useful for breaking very large surfaces into readable regions.

The visual inspiration is the large dark greeble channels used on science-fiction spacecraft and structures: broad service/infrastructure corridors that visually divide otherwise flat hull surfaces.

These should be called **service channels**, not trenches.

## Core principle

Do not cut trenches into `StructuralOccupancy` or modify accepted megastation topology.

A service channel should be a **surface-built visual construction**.

Conceptually:

    raised surface band
    + dark service strip
    + raised surface band

rather than:

    subtract/inset structural geometry

Raised lips, plates, edge walls and dark material can create the visual impression of a shallow recessed channel without requiring Boolean subtraction or topology changes.

This avoids the difficult problems previously encountered with chamfers and structural geometry:

- routing around corners;
- resolving concavity;
- passing towers;
- preserving manifold topology;
- modifying occupancy;
- maintaining regularisation invariants.

## Scale

Service channels operate at district/architectural scale.

Typical conceptual dimensions:

- length: tens to hundreds of metres;
- width: several to tens of metres;
- apparent depth: roughly 1–5 m;
- detail inside: simplified infrastructure rather than thousands of tiny greebles.

Exact dimensions require visual tuning.

## Local planar networks, not global routing

Service channels form coherent networks only within one canonical planar region. They do not form
a station-wide graph, cross region boundaries, wrap corners, or use general pathfinding.

They may:

- cross part of a terrace;
- run along the foot of a tower;
- traverse an Industrial slab;
- terminate at a wall;
- terminate at a machinery installation;
- end at a service node;
- simply stop with an appropriate end piece.

A channel should not be required to continue around a corner or negotiate arbitrary megastation topology.

This is a major simplification.

## Visual vocabulary

A channel may contain:

- dark floor/material strip;
- raised edge lips or parapets;
- simplified cable bundles;
- simplified pipes;
- grille sections;
- access panels/hatches;
- utility boxes;
- sparse warning/work lights;
- machinery/service nodes;
- terminal/end-cap structures.

The contents can be deliberately coarse.

For example, cable bundles may be represented as simple partially exposed cylinders or other low-cost "Lego brick" geometry rather than detailed cable meshes.

At megastation scale, silhouette, grouping and contrast matter more than micro-detail.

## Bridges

Service channels create natural opportunities for crossings.

Possible bridges include:

- road/service bridges;
- maintenance walkways;
- structural bridges;
- pipe bridges;
- cable bridges;
- G1 module-like structures spanning the channel.

Bridges do not need gameplay semantics initially.

They are architectural features that increase depth, layering and visual scale.

## Relationship to existing systems

The intended hierarchy becomes:

    megastation structural mass
    → Z1 semantic districts
    → service channels
    → G1 attached module buildings
    → G2 infrastructure clusters
    → windows / operational lights

Service channels can become organizing structures for the other decoration layers.

Examples:

- G1 modules placed alongside a channel;
- G2 tank/machinery clusters concentrated near it;
- future pipes/cables routed inside it;
- warning lights marking channel edges;
- bridges connecting areas across it.

Instead of placing decoration randomly on an enormous slab, the channel creates a believable designed subspace to decorate.

## Semantic use

Likely strongest roles:

### Industrial

Frequent candidate.

Can contain machinery, pipe/cable runs, vents and work infrastructure.

### Utilities

Frequent candidate.

Especially suitable for dark service corridors and dense infrastructure.

### Logistics

Moderate candidate.

May form broad service/transport lanes or equipment corridors.

### Habitation

Sparse.

Possible technical/service strips between inhabited structures.

### Strategic

Sparse and specialized.

### Structural

Usually absent.

Large Structural surfaces should remain quiet.

## Topology use

Prefer large coherent planar regions.

Service-channel generation should remain local to one suitable planar region.

Potentially useful contexts:

- broad terraces;
- long walls;
- shelves;
- canyon floors;
- tower bases.

Do not require cross-region continuity.

If a planar region ends, the service channel can end.

## End pieces

Every channel termination should look intentional.

Possible terminal treatments:

- large service housing;
- end cap;
- machinery block;
- vent bank;
- dark sealed wall;
- junction node;
- cable/pipe termination structure.

This avoids visual impressions of geometry simply being cut off.

## Performance philosophy

Service channels should provide broad visual complexity cheaply.

Prefer:

- a few long structural/detail bands;
- combined batched geometry;
- simplified repeated cable/pipe shapes;
- shared materials;
- little or no unique texture generation.

Avoid filling a channel with thousands of individually complex greeble objects.

A single 150 m channel containing several coarse infrastructure runs may provide more useful visual scale than hundreds of independent small decorations.

## Shadows

Raised channel lips, larger machinery and bridges are good shadow candidates.

Tiny cables, grilles and surface detail need not necessarily cast shadows.

Shadow participation should remain selective, particularly with the accepted 8192² megastation shadow baseline.

## Future relationship to G2b routing

Service channels provide a natural future substrate for routed infrastructure.

Instead of:

    random pipe from arbitrary point A to arbitrary point B

future routing can become:

    infrastructure node
    → service channel
    → another node / terminal

This gives pipes and cables a visual reason to exist and greatly constrains their routing problem.

## Important invariant

Service channels are presentation/secondary geometry.

They do not alter:

- structural occupancy;
- topology regularisation;
- `BoundaryTopology`;
- collision authority;
- accepted structural mesh.

If a future design requires true structural recesses, that should be treated as a separate topology project rather than silently expanding service channels into subtractive geometry.

## Design objective

The purpose of service channels is to turn:

    one enormous megastation slab

into:

    several readable architectural regions separated by infrastructure corridors

while preserving the accepted structural generator.

They should create the science-fiction "greeble trench" visual language without requiring actual trenches.
