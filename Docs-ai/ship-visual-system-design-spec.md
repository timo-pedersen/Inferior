# Inferior — Ship Visual System Design Specification

> **Status:** Draft 0.1  
> **Scope:** Ship visual architecture, semantic hull geometry, removable armour panels, external engines, cargo-driven hull design, attachment points, lighting metadata, asset catalogue, and shipyard viewer.  
> **Authority:** Explicit decisions in the design session override older ship documents where they conflict.

---

## 1. Purpose

Inferior ships must not be interchangeable science-fiction sculptures with statistics and collision attached afterward.

A ship hull is the visible result of a physical arrangement:

- cargo-container stack or crew volume;
- machinery and structural volumes;
- cockpit position;
- cargo and personnel access;
- engine attachment surfaces;
- landing footprint;
- armour coverage;
- external service and equipment zones.

The ship's silhouette, exposed structure, removable armour, engines, lights, doors, and attachment points must form one coherent physical design.

This specification defines the visual and semantic foundation required for:

- 21 fixed hull types;
- 18 interchangeable engine types;
- removable physical armour panels;
- persistent panel damage and replacement;
- cargo doors protected by removable armour;
- unusual cockpit positions;
- external equipment and container attachment;
- future landing, weapons, sensors, and damage systems;
- a reusable shipyard/debug viewer.

This is an architecture and asset-library effort, not merely a mesh-production task.

---

## 2. Design goals

### 2.1 Physical legibility

A player should be able to infer something about a ship by looking at it:

- where its cargo is stored;
- where the crew sits;
- where engines attach;
- which surfaces are heavily armoured;
- which areas are serviceable or mechanically exposed;
- how it lands;
- where cargo enters and leaves;
- what has been damaged, replaced, or removed.

### 2.2 Persistent physical identity

A hull type is a reusable template. A ship instance is a unique physical object with its own:

- installed engines and equipment;
- panel types and condition;
- wear and repairs;
- ownership and history;
- configuration and tuning.

The visual system must support this distinction. It must not flatten a configured ship into one anonymous merged mesh.

### 2.3 Low-poly geometry as gameplay

The low-poly appearance is not merely an art limitation.

Large readable facets define:

- removable armour elements;
- damage locations;
- protection of internal systems;
- material boundaries;
- service areas;
- silhouette and handling character.

Panels may be triangles, quadrilaterals, pentagons, hexagons, or larger convex polygons. A rendered triangle is not automatically a gameplay panel.

### 2.4 Free hull design

Hull designers may use arbitrary dimensions, angles, and convex panel shapes within the class envelope.

The initial catalogue must not be forced into a small universal set of panel dimensions or preferred angles. Generated panels enter a reusable panel-pattern catalogue, and later hulls may reuse compatible patterns opportunistically.

### 2.5 Configuration changes must remain visible

Changing an engine, removing armour, exposing a cargo door, or replacing panels should alter the visible ship.

Engines, panels, doors, glass, lights, landing feet, and future external modules remain semantically distinct even where rendering later batches them for efficiency.

---

## 3. Explicit non-goals

This specification does not require:

- capital ships;
- player-designed hull geometry;
- a modelled cockpit interior;
- animated landing gear;
- opening cargo doors in the first implementation;
- detailed cargo-hold interiors;
- weapons implementation;
- functional sensor equipment;
- dynamic centre-of-mass calculation from geometry;
- aerodynamic properties derived from geometry;
- full per-panel collision;
- normal maps or advanced material textures;
- LOD generation;
- NPC fleet rendering optimisation;
- global optimisation of panel reuse;
- a physically simulated structural frame;
- merging a configured ship into one permanent mesh.

Aerodynamics remain authored ship parameters. They are not derived from the visual hull.

---

## 4. Catalogue scope

### 4.1 Hulls

The first complete catalogue contains exactly 21 hull types:

| Class | Hull count |
|---|---:|
| Shuttle | 2 |
| Small | 5 |
| Medium | 11 |
| Large | 3 |
| **Total** | **21** |

Capital ships are deferred and have no defined implementation size.

Authoritative class envelopes remain:

| Class | Length | Width | Height | Cargo |
|---|---|---|---|---|
| Shuttle | 10 m maximum; approximately 6 m typical | 4 m maximum | 4 m maximum | Crew only |
| Small | 12–20 m | 6–15 m | 4–6 m | 1–4 containers |
| Medium | 26–36 m | 17.5–36 m | Up to 12 m | 8–30 containers |
| Large | Up to 72 m | Up to 36 m | Up to 20 m | Up to 120 containers |

The envelope is the hard dimensional constraint. Individual hulls need not approach every maximum.

### 4.2 Engines

The first complete catalogue contains 18 external engine types:

| Design grouping | Count |
|---|---:|
| Shuttle-oriented designs | 3 |
| Small-ship-oriented designs | 5 |
| Medium-ship-oriented designs | 5 |
| Large-ship-oriented designs | 5 |
| **Total** | **18** |

These groupings guide asset design only. They are not runtime engine size classes.

Compatibility is determined by physical mount geometry, attachment pose, and clearance—not by a categorical size exception.

A hull supports one to four installed engines.

---

## 5. Coordinate and transform convention

All new ship geometry uses the gameplay-native convention:

- **+X:** right;
- **+Y:** up;
- **-Z:** forward;
- **+Z:** rearward.

The ship origin is defined by the hull type and should normally be near its intended centre of mass, but the exact location is authored per hull.

Every attachment pose contains:

- a position in hull-local space;
- an orientation in hull-local space;
- an outward attachment normal where applicable.

Parent and child attachment transforms must compose explicitly. Attachment normals oppose when two surfaces are joined.

Negative-scale mirroring is not used for production meshes. Mirrored engine variants are generated with corrected:

- vertex positions;
- winding;
- normals;
- text and markings;
- asymmetric light placement.

---

## 6. Core terminology

### Hull type

A fixed ship-class template identified by a stable `HullTypeId`.

It defines semantic geometry, cockpit pose, cargo arrangement, attachment ports, authored flight parameters, component slots, and catalogue metadata.

### Ship instance

A unique persistent universe object using one hull type and carrying its own installed equipment, panels, damage, wear, name, history, and ownership.

### Structural hull

The closed, dark underlying pressure and structural shell. It remains present when armour panels are removed.

### Armour panel

A removable closed solid mounted over a compatible structural face. It carries material, condition, protection, rarity, and visible history.

### Surface role

A capability assigned to a structural polygon, such as accepting armour or functioning as a cargo door, engine mount, glass opening, or service area.

### Assembly

A semantically distinct collection of geometry with its own identity and possible future transform, such as a cargo door, cockpit canopy, engine, or landing foot.

### Attachment port

A stable semantic pose and compatibility declaration used to install an external object.

### Ship render model

The composite visual representation of one configured ship. It preserves distinct parts even when some parts are internally batched.

---

## 7. Composite ship model

A ready-to-render ship is a composition, not one merged asset:

```text
ShipRenderModel
    Structural hull
    Cockpit frame
    Cockpit glass
    Cargo-door assemblies
    Installed armour panels
    Installed engine instances
    Engine attachment stems or fairings
    Landing feet
    Hull-owned marker lights
    Hull-owned beam-light fixtures
    Engine-owned lights
    Future external modules
```

The render layer may cache or batch compatible geometry, but semantic identity must remain recoverable.

The following operations must remain possible without rebuilding an unrelated ship definition:

- hide or remove one panel;
- alter one panel's material or condition;
- swap one engine type;
- render a cargo door without its armour;
- show only the bare structural hull;
- highlight an attachment port;
- select a single assembly in the shipyard viewer.

---

## 8. Stable semantic identity

### 8.1 General form

Hull-owned identities use:

```text
<hull-type-id>.<region>.<subregion>.<number>
```

Examples:

```text
kestrel.top.nose.01
mule.underside.cargo.04
pilgrim.starboard.engine-mount.01
```

The prefix is the stable `HullTypeId`, never:

- the player-assigned ship name;
- a localised display name;
- an array index;
- a generated GPU face number.

### 8.2 Shared `all` namespace

`all` is reserved for reusable definitions whose identity is not owned by one hull:

```text
all.mount.engine-root.01
all.starboard.engine-root.01
all.rear.navigation-light.02
all.underside.landing-foot.01
```

`all` is a literal namespace, not wildcard syntax.

A side-neutral mirrorable definition should use a side-neutral identity. A genuinely handed reusable definition may use `port` or `starboard`.

### 8.3 Stable geometry identities

Where practical, retain stable identities for:

- semantic vertices;
- structural polygons;
- panel slots;
- attachment ports;
- cargo doors;
- cockpit parts;
- landing-foot poses;
- lights.

Triangulation, GPU-buffer ordering, or face-list reordering must not change persistent identity.

A structural polygon and its armour slot may share a semantic location key while remaining distinct typed objects.

---

## 9. Hull design process

Every cargo-capable hull begins with an internal arrangement before the exterior silhouette is sculpted.

### 9.1 Container dimensions

The canonical shipping container is:

```text
2.5 × 2.5 × 6.0 metres
```

Container capacity is physical. Containers are not represented as abstract cargo numbers.

### 9.2 Required design volumes

A hull design starts with:

1. **Container stack**  
   Declared arrangement and count.

2. **Cargo movement clearance**  
   Space required to insert, remove, or transfer the containers.

3. **Cargo-door opening and path**  
   A real geometric route from the exterior to the declared stack.

4. **Crew and cockpit volume**  
   May be central, side-mounted, underslung, raised, or otherwise asymmetric.

5. **Machinery volume**  
   Approximate region for components, power, life support, and structural systems.

6. **Engine mount zones**  
   Flat surfaces and clearance volumes parallel to the intended engine axes.

7. **Landing footprint**  
   Three or four explicit contact locations.

8. **Armour and service zoning**  
   Clean protected surfaces contrasted with exposed mechanical regions.

Only after these volumes are plausible is the exterior shaped.

### 9.3 Cargo layout declaration

A cargo-capable hull records design metadata comparable to:

```text
Container arrangement: 2 × 2 × 3
Capacity: 12
Stack bounds: 5 × 5 × 18 m
Cargo door: rear
Transfer axis: +Z
Clearance corridor: 6 × 6 × 20 m
```

The first implementation does not render the full hold. The declaration exists so the exterior shape and door remain physically justified.

### 9.4 Visual consequence

Different stack arrangements must create visibly different hulls:

- long and train-like;
- broad and flat;
- tall and narrow;
- split around a machinery spine;
- asymmetric with a side cockpit;
- forward-loaded or rear-loaded;
- exposed container racks in later specialised designs.

A larger class is not merely a scaled copy of a smaller hull.

---

## 10. Surface roles

Every structural polygon has an explicit surface role.

Initial roles:

```text
PanelSeat
ExposedStructure
EngineMount
CockpitFrame
CockpitGlass
CargoDoor
ServiceSurface
```

### PanelSeat

Accepts one removable armour panel.

### ExposedStructure

Part of the closed structural shell but intentionally unarmoured. Used for visible frame, machinery backing, recessed areas, and transitions.

### EngineMount

A flat mounting surface for an external engine root or attachment stem. It does not accept armour.

### CockpitFrame

Legacy/integrated structural cockpit surround. Replaceable installed-cockpit framing belongs
to the cockpit module under `ship-cockpits.md`; Aries no longer uses this hull surface role.

### CockpitGlass

Legacy/integrated glass or viewport area. It does not accept armour. Replaceable installed
canopies belong to cockpit-module geometry; Aries no longer uses this hull surface role.

### CargoDoor

Belongs to a movable structural door assembly. The door may carry its own armour slots.

### ServiceSurface

A mechanically accessible area intended for equipment ports, maintenance access, landing-gear roots, sensors, weapons, lights, or other external systems.

Branching and behaviour are based on these capabilities, never on hull names or special-case class checks.

---

## 11. Semantic hull geometry

The authoritative hull definition is CPU-side semantic geometry, not the final GPU mesh.

Conceptually:

```text
HullGeometryDefinition
    Stable vertices
    Stable polygon faces
    Surface roles
    Material groups
    UV orientation metadata
    Panel-slot associations
    Door assemblies
    Attachment ports
    Bounds
```

Each polygon stores:

- stable face identity;
- ordered perimeter vertex identities;
- outward normal or sufficient data to derive and validate it;
- surface role;
- material group;
- UV projection orientation;
- optional panel-slot identity;
- optional assembly identity.

A polygon may contain any number of perimeter vertices but must be:

- planar within tolerance;
- convex for initial panel generation;
- non-self-intersecting;
- consistently wound;
- non-degenerate.

The renderer triangulates polygons only after semantic processing. A quadrilateral, hexagon, or eleven-sided armour location remains one panel regardless of triangle count.

---

## 12. Structural hull

The bare hull is a complete closed shell.

Removing all armour must reveal a plausible dark structural ship rather than open space or an empty collision volume.

The structural hull may visually contain:

- dark pressure-shell plates;
- ribs and frame edges;
- recessed fastening surfaces;
- conduits and equipment backing;
- cargo-door structure;
- exposed service regions;
- engine-root structure.

The initial branch does not require a simulated frame or modelled interior. Structural detail exists to make armour removal physically and visually credible.

The preferred overall visual layering is:

```text
Armour-panel exterior
Panel chamfer and dark edge
Small mounting gap
Dark structural hull
```

---

## 13. Armour-panel system

### 13.1 Panel shape

Initial panel generation supports arbitrary planar convex polygons:

- triangles;
- quads;
- pentagons;
- hexagons;
- larger convex n-gons.

Concave panels, notched panels, holes, and non-planar panels are out of scope for the first system.

### 13.2 Physical construction

Every panel is a closed solid with:

- outside face;
- inside face;
- chamfer/edge faces;
- physical thickness of approximately 5 cm;
- approximately 2 cm edge inset producing the chamfer;
- a small separation from the structural hull to avoid z-fighting and reveal a mounting seam.

Exact values remain tunable constants, not assumptions spread across factories.

### 13.3 Generation algorithm

For each compatible `PanelSeat` polygon:

1. Construct a stable local 2D basis on the polygon plane.
2. Project the ordered perimeter into 2D.
3. Validate planarity, convexity, winding, and minimum edge length.
4. Offset the perimeter inward by the chamfer inset.
5. Reject the panel if the inset collapses or self-intersects.
6. Place the inner polygon slightly above the structural hull.
7. Place the inset outer polygon at the panel's full outward thickness.
8. Connect corresponding perimeter edges with chamfer faces.
9. Add the closed inside face.
10. Generate material groups and UVs for outside, inside, and edge surfaces.
11. Triangulate only for rendering.

Generation failure is reported clearly. It must not silently produce malformed geometry.

### 13.4 Materials

A panel has distinct material regions:

- exterior;
- interior;
- chamfer/edge.

This allows a detached or destroyed panel to show a materially different inside surface.

### 13.5 Panel pattern registry

Every generated panel shape may produce a reusable panel-pattern definition.

Reuse is opportunistic:

- later hulls may use an existing compatible pattern;
- related hull families may deliberately share panels;
- unique transition panels are allowed;
- the first catalogue is not constrained to a tiny universal panel kit.

### 13.6 Persistence

Panel state is stored by stable panel-slot identity, never by triangle or array index.

A ship instance may persist:

- installed panel-pattern identity;
- material and grade;
- integrity;
- wear;
- replacement history;
- exceptional markings or repairs.

Generated baseline geometry is regenerated from the hull type. Persistent deltas store what changed.

---

## 14. Cargo-door assemblies

A cargo door is a structural assembly, not merely a five-centimetre armour panel.

A door definition contains:

- stable assembly identity;
- closed pose;
- future hinge or slide axis;
- structural door-leaf geometry;
- opening polygon;
- movement and clearance volume;
- material groups;
- zero or more armour-panel slots.

The first implementation renders the door permanently closed.

### 14.1 Armoured doors

Cargo-door armour creates layered gameplay:

```text
Removable external armour
    ↓
Structural cargo door
    ↓
Cargo hold and containers
```

A later damage system may distinguish:

- armour removed or destroyed;
- door exposed;
- door damaged;
- door jammed;
- hold breached;
- containers vulnerable.

This branch only establishes the identities and geometry required for that future behaviour.

### 14.2 Access expectations

Initial catalogue guidance:

- Shuttle: personnel/pilot access; no cargo-container door.
- Small: usually one combined cargo and crew entrance.
- Medium: usually one principal cargo door; separate personnel access optional.
- Large: principal cargo door plus at least one smaller personnel or service entrance.

Exceptions are allowed where the hull concept justifies them.

---

## 15. Cockpit system

The cockpit is fixed to the hull type and consists of:

- structural cockpit frame;
- glass or viewport geometry;
- cockpit camera pose.

No cockpit interior is modelled.

`CockpitPose` includes both:

- position;
- orientation.

This supports:

- conventional nose cockpits;
- side-mounted cockpits;
- underslung cockpits;
- raised or rear-mounted bridges;
- angled forward views;
- asymmetric hauler layouts.

Unusual cockpit placement is a primary source of flight character and must not require renderer special cases.

---

## 16. Engine system

### 16.1 General form

Engines are external, separate physical assemblies.

Viewed from above, many engines should resemble a T or mushroom:

- a long nacelle, prism, cylinder, or irregular body parallel to the ship axis;
- a narrower attachment stem or strut connecting it to the hull;
- no requirement for a conventional Newtonian rocket nozzle.

Tall, narrow, asymmetric, and unconventional designs are welcome.

An engine may end in:

- a grille;
- a slot;
- a luminous plane;
- a blunt field surface;
- distributed vents;
- an actual hot-rod-like exhaust pipe;
- another coherent degenerate-matter outlet.

### 16.2 Engine definition

An engine definition includes:

```text
EngineTypeId
Semantic geometry
Attachment plane
Attachment transform
Mount footprint
Physical clearance bounds
Main exhaust emitter region
Mirror policy
Material groups
Optional attachment-stem definition
Optional engine-owned light definitions
```

It does not require:

- a runtime visual size category;
- down-thrust emitter geometry;
- manoeuvring-thruster emitter geometry.

Simulation engine capabilities remain component data and are not inferred from visible exhaust openings.

### 16.3 Mount compatibility

Compatibility is based on:

- attachment-plane dimensions and orientation;
- mount footprint;
- clearance bounds;
- connection policy;
- any later explicit structural limit.

The design grouping under which an engine was authored does not itself prevent installation.

### 16.4 Mirroring

A mirrorable engine uses one source definition and produces corrected left/right geometry variants.

Text, arrows, serial markings, asymmetrical vents, and lights must remain correctly oriented after mirroring.

### 16.5 Engine-owned lights

Engines may contribute:

- nacelle-tip position lights;
- mount service lights;
- exhaust-status glow;
- warning lights;
- manufacturer-specific flash patterns.

Required hull extremity/navigation coverage remains hull-owned so swapping or removing an engine cannot leave the ship without its mandatory position-light arrangement.

---

## 17. Attachment ports and service zones

### 17.1 Generic attachment port

External installations use one semantic attachment-port concept:

```text
AttachmentPort
    PortId
    Pose
    Attachment normal
    Footprint
    Clearance bounds
    Capabilities
```

Initial capabilities may include:

```text
Engine
Weapon
Sensor
Utility
LandingGear
NavigationLight
BeamLight
Container
```

A port may advertise more than one capability where physically sensible. An installed object occupies the relevant port.

### 17.2 No identity special cases

Code acts on capability:

- accepts engine;
- has clearance bounds;
- provides light pose;
- carries panel slot;
- is movable assembly.

It does not branch on:

- hull display name;
- role string;
- first implementation type;
- cargo-ship identity;
- concrete class where a capability expresses the requirement.

### 17.3 Service zones

Weapon, sensor, utility, light, and maintenance ports should cluster in deliberate service zones rather than appearing randomly across armour.

A useful visual tendency is:

- **top/front:** cleaner, more heavily armoured;
- **sides:** mixed armour, cockpit, and engine roots;
- **underside/rear:** cargo access, landing gear, equipment ports, service surfaces, and mechanical exposure.

This supports the established preference for pitch-up manoeuvring and top/front protection without making it a universal silhouette rule.

---

## 18. Engine mounts

An engine mount is a flat structural surface approximately parallel to the installed engine's principal axis.

It:

- has a stable hull-owned port identity;
- does not accept an armour panel;
- defines a connection pose and normal;
- defines available footprint and clearance;
- may be partly hidden by the engine stem or fairing.

Panels terminate cleanly around the mount area. The initial system does not use concave or notched armour to wrap around engine roots.

The attachment stem may widen near the hull and visually conceal the transition.

---

## 19. Landing gear

Initial landing gear consists of three or four simple fixed assemblies:

- short root or strut;
- flat chamfered cuboid skid or foot.

There is no retraction, animation, suspension, or door.

Each hull defines explicit stable landing-foot poses. Artists should place these near:

- panel seams;
- structural corners;
- exposed underside edges;
- service zones.

The geometry may visually pass through or emerge from panel seams. It is not algorithmically derived from mesh edges.

Landing-foot contact points define a future landing footprint more meaningfully than the hull's overall bounding box.

---

## 20. Lighting metadata

### 20.1 Hull-owned marker lights

Hull types define required position and navigation lights with:

- stable identity;
- position;
- orientation or surface normal;
- colour;
- glow size;
- intensity;
- flash pattern.

### 20.2 Engine-owned lights

Engine definitions may add optional lights, but do not replace mandatory hull coverage.

### 20.3 Beam lights

Hull types reserve beam-light mounting poses now.

A beam-light definition requires:

- stable identity;
- position;
- direction;
- cone angle;
- range;
- intensity;
- colour.

Actual illumination of station and ship surfaces may be implemented later. The first branch may render fixture geometry and emissive/glow representation only.

### 20.4 Exhaust emitters

Main exhaust emitter metadata is separate from light metadata. An exhaust surface may glow, but it also describes the region from which future exhaust effects originate.

---

## 21. Materials and UVs

Initial material groups include:

- structural hull;
- panel exterior;
- panel interior;
- panel edge;
- cockpit frame;
- cockpit glass;
- cargo door structure;
- engine casing;
- engine stem/fairing;
- exhaust/emitter;
- landing gear;
- light fixture;
- emissive/glow.

Every semantic polygon receives planar UV projection with a consistent world-space texel scale.

UV orientation metadata must avoid arbitrary rotations between neighbouring faces where a stable orientation can be authored.

Wear and variation should initially come from reusable materials plus deterministic:

- UV placement;
- tint;
- grime or wear modulation;
- markings.

The system should not require one unique texture asset per hull or panel.

---

## 22. Hull families and role bias

The 21 hulls should form related design families rather than 21 unrelated sculptures.

Current recommended distribution:

| Family | Shuttle | Small | Medium | Large |
|---|---:|---:|---:|---:|
| Compact wedges | 1 | 2 | 2 | 0 |
| Industrial slabs | 1 | 1 | 4 | 1 |
| Asymmetric workhorses | 0 | 1 | 3 | 1 |
| Sleek specialists | 0 | 1 | 2 | 1 |
| **Total** | **2** | **5** | **11** | **3** |

Family resemblance may come from:

- shared cross-sections;
- cockpit language;
- structural framing;
- recurring panel patterns;
- engine-mount style;
- door and light design;
- manufacturer-like material treatment.

Roles are design and selection biases, not hard runtime classes.

A catalogue entry may state:

```text
Primary design bias: salvage
Secondary design bias: utility
```

The player remains free to configure the hull differently where its physical slots and capabilities permit.

Working names such as **Kestrel**, **Mule**, and **Pilgrim** are suitable examples of distinct hull or family identities.

---

## 23. Shipyard and debug viewer

A dedicated viewer is a required deliverable, not optional tooling.

It must support:

- cycling all hull types;
- cycling all engine types;
- selecting one to four engine installations;
- mirrored engine variants;
- bare structural hull view;
- fully armoured view;
- deterministic random missing-panel view;
- selecting and highlighting one panel;
- detaching or hiding one panel;
- displaying panel interior and edge materials;
- showing cargo door with and without armour;
- showing attachment ports and capability labels;
- showing landing-foot poses and contact footprint;
- showing cockpit camera pose;
- showing light and exhaust emitter locations;
- showing origin, axes, bounds, and clearance volumes;
- rotating the ship;
- rotating the light;
- changing camera distance;
- capturing screenshots.

Visual catalogue work must be reviewed in this viewer after every small group of assets. The full 21-hull set must not be produced blind and inspected only at the end.

---

## 24. Persistence and deterministic generation

### 24.1 Baseline versus deltas

Hull geometry, default panels, engine geometry, and attachment definitions are regenerated from stable type identity and asset/generator version.

Persistence stores meaningful ship-instance deltas:

- installed engine types and ports;
- panel pattern and material installed in each slot;
- panel integrity and wear;
- missing panels;
- cargo-door state when implemented;
- damage or replacement history where required;
- selected lights or external modules where configurable.

### 24.2 Stable IDs

Persistent state never depends on:

- process-randomised hashing;
- mutable RNG consumption order;
- triangle order;
- GPU-buffer order;
- list index;
- display name.

### 24.3 Resource sharing

Generated GPU resources are cached primarily by reusable type or pattern:

- hull type;
- panel pattern/material state where compatible;
- engine type and mirrored variant;
- shared fixture geometry.

A ship instance holds configuration and state, not needless private copies of every immutable asset.

---

## 25. Validation requirements

Geometry validation should cover:

- no NaN or infinite vertices;
- no near-zero-area triangles;
- planar semantic polygons within tolerance;
- convex panel-seat polygons;
- consistent outward winding;
- valid panel inset;
- no collapsed panel solid;
- closed structural hull where required;
- closed panel solids;
- attachment transforms that coincide within tolerance;
- opposing attachment normals;
- engine clearance contained within or intentionally outside declared design bounds;
- unique stable identities within each namespace;
- every `PanelSeat` has exactly one stable panel slot;
- every installed panel slot resolves to a known panel pattern;
- every required hull light exists regardless of engine configuration;
- every cargo arrangement fits its declared stack and clearance volume.

Automated tests may prove geometry and identity properties. Visual correctness still requires in-engine or viewer confirmation.

---

## 26. Vertical slice

The first implementation slice contains:

### Hulls

1. One shuttle built around crew volume and unusual but usable cockpit placement.
2. One medium cargo-capable hull built around an explicit container stack.

The medium hull is the primary architecture test because it exercises:

- container-driven form;
- cargo door;
- door armour;
- panels;
- exposed structure;
- engine mounts;
- landing footprint;
- cockpit placement;
- service ports;
- lights.

### Engines

Two interchangeable engine designs:

- visibly different silhouettes;
- compatible with at least one shared mount arrangement;
- one mirrorable installation;
- distinct exhaust-emitter treatment;
- at least one engine-owned light.

### Systems demonstrated

The slice must demonstrate:

- semantic hull polygons before triangulation;
- stable hull, face, panel-slot, assembly, and port IDs;
- planar convex n-sided panel generation;
- panel exterior/interior/edge materials;
- bare and armoured hull rendering;
- closed cargo door;
- removable cargo-door armour;
- cockpit frame, glass, and full camera pose;
- engine swapping;
- corrected mirroring;
- fixed landing feet;
- hull and engine lights;
- beam-light mounting poses;
- shipyard viewer;
- hull selection by stable `HullTypeId`.

The slice must be visually confirmed before catalogue production begins.

---

## 27. Catalogue production

After vertical-slice approval:

1. Define the 21-hull catalogue on paper/data first.
2. Record for every hull:
   - stable ID and display name;
   - class and dimensions;
   - container arrangement;
   - cockpit pose;
   - cargo-door placement;
   - engine count and mounts;
   - landing footprint;
   - surface-role map;
   - attachment-port inventory;
   - light layout;
   - role/design biases;
   - family membership.
3. Define all 18 engines with mount, clearance, exhaust, mirror, material, and light metadata.
4. Produce assets in family groups.
5. Review each group in the shipyard viewer.
6. Integrate approved assets into the runtime registry.
7. Update active documentation and current-state records.

Catalogue commits should remain coherent by family or system, not one enormous undifferentiated asset dump.

---

## 28. Current-code migration constraints

The former Type-1 ship path bakes hull, nacelles, and pylons into one fixed factory. The semantic renderer now resolves each snapshot's `HullTypeId` and uses the registered hull's `VisualGeometry` whenever it is present.

Checkpoint Aries rendering note: `type-1` is now semantically registered as Aries and its third-person structural hull is generated from those semantic polygons. The old `Type1HullFactory` remains only as a temporary fallback for registrations that do not yet provide semantic visual geometry. Aries does not use that factory or its legacy 180-degree orientation correction. The fallback mesh is not authoritative for any hull semantics and must be removed once all renderable hull registrations provide `VisualGeometry`.

The current composite path renders the closed semantic shell plus snapshot-published installed engines and cockpit modules. It does not yet render generated armour, landing feet, beam illumination, cargo-door animation, or detailed cockpit interiors. Aries' installed cockpit and ship-forward reticle have been visually accepted; each newly authored hull still requires its own in-engine acceptance.

### 28.1 Aries semantic rendering checkpoint

The CPU-side `SemanticHullMeshBuilder` triangulates validated convex semantic n-gons with a deterministic fan while retaining one `RenderedFaceRange` per semantic face. Aries currently supplies 48 semantic vertices and 24 semantic faces, producing 56 rendered triangles:

| Render group | Faces | Triangles |
|---|---:|---:|
| Structural hull | 21 | 46 |
| Cargo door | 1 | 6 |
| Cockpit frame | 1 | 2 |
| Cockpit glass | 1 | 2 |

Vertices are emitted per triangle with the semantic face's flat outward normal. Planar UVs use a deterministic face-local basis at 2 metres per UV unit. The semantic path uses Aries' native `-Z` forward convention and does not apply the legacy mesh's 180-degree correction.

`ShipMeshRenderer` owns a GPU mesh cache keyed by stable `HullTypeId`. It creates immutable vertex/index buffers on first use, reuses them on later frames, and disposes all cached semantic meshes, the optional legacy fallback mesh, and its debug-line effect with the renderer.

In-engine inspection controls:

- `F3`: toggle third-person view so the ship hull is visible.
- `F4`: toggle between normal materials and semantic surface-role colours. Role mode also draws ship-local axes (`+X` red, `+Y` green, `-Z` cyan) and a white vertex-derived hull bounding box.

The role view draws existing face index ranges with diagnostic colours; it does not create alternate geometry or change the semantic definition. Face-ID cycling was omitted because it was optional and the retained face ranges already provide the required selection foundation.

### 28.2 Asterisk Phase 1 checkpoint

Asterisk (`asterisk`) is an authored semantic hull, not a scaled Aries or a renderer
special case. Its structural envelope is 8.6 m long, 2.8 m wide, and 3.2 m high before
the attached modules. One canonical 2.5 × 2.5 × 6.0 m container occupies the longitudinal
design volume. The bow is a permanently closed cargo-door assembly with separate raised
frame and lock geometry.

Viewed from the front under the native `+X` starboard convention, the starboard cockpit
appears on viewer-left and the single port Mule appears on viewer-right. The hull owns
their opposed C2/H2 sockets and support geometry; the installed modules remain separate.
The Asterisk cockpit camera is a definition-owned child looking 20 degrees toward
starboard, so the generic projected ship-forward reticle appears left of screen centre.
First-person own-ship geometry remains hidden.

### 28.3 Beren Phase 1 checkpoint

Beren (`beren`) is a medium authored semantic hull with a 27 m by 20 m broad, mildly
spade-shaped upper platform and a total structural depth of 6.2 m. Its underside cargo
volume contains a 3 by 3 arrangement of canonical 2.5 m by 2.5 m by 6.0 m containers.
The central aft cargo door is a distinct, permanently closed visual assembly.

Four Needle H2 engines are installed independently on authored attachment ports: an
upper and lower engine on each side. A complete C2 command pod hangs from the forward
downward-facing cockpit socket. Its camera looks 10 degrees below ship-forward, placing
the generic projected ship-forward reticle above screen centre. Own-ship geometry remains
hidden in first-person.

The cockpit CTRL rail includes a `NEXT SHIP` button. It requests the stable cycle
Aries -> Asterisk -> Beren -> Antega -> Aries. `SpaceSimulation` performs the replacement and
preserves the current position, velocity, orientation, and flight state; rendering and
UI do not retain a second mutable ship authority.

### 28.4 Antega Phase 1 checkpoint

Antega (`antega`) is an accepted 99 m massive civilian container hauler. Its physical
design basis is 120 canonical containers arranged 12 fore/aft × 5 across × 2 high,
producing a 12.5 m × 5.0 m × 72.0 m stack volume inside the long faceted cargo body.
The blunt forward end carries a heavily framed ten-segment closed cargo hatch; loading,
container rendering, and hatch animation remain deferred.

Four distinct Atlas Civilian Drive engines are installed through ordinary H10 mounts:
upper and lower units on port and starboard. Each definition-owned engine is 58.4 m long
and remains visibly separated from the cargo hull by substantial forward and aft supports
plus a longitudinal torque beam. All four publish independent presentation transforms and
aft exhaust anchors. The dorsal far-aft command module is a keyed C5 Antega civilian bridge
with `Deg0` as its only allowed installation rotation. Its physical camera looks 5 degrees
down through the forward glazing, so the generic projected ship-forward reticle appears
slightly above screen centre.

Snapshot-published `ShipPresentationBounds` combine authored hull vertices with transformed
installed cockpit and engine geometry. Chase/orbital view targets the composite centre and
derives its minimum radius from the composite bounding sphere; this is generic framing, not
an Antega-specific camera path. Antega, the bridge, four Atlas engines, exhaust glows, scale,
and camera framing were visually accepted by Timo on 2026-07-20.

Propulsion is now instance-aggregated. Atlas owns provisional numeric dry mass, forward
thrust, maneuvering thrust, and active rotational torque; all four installed instances
contribute independently through `ShipPropulsion`. See
`Docs-ai/ship-mass-and-propulsion.md` for current values and deferred work.

The current size-class code must be corrected to:

```text
Shuttle
Small
Medium
Large
```

Capital is removed or safely deferred without corrupting persisted numeric values.

The partially stubbed `ShipBuilder` must eventually derive size and hull data from the authoritative hull definition rather than hard-coding `Medium`.

Migration should first preserve one working ship through the new composite path before the 21-hull catalogue is added.

---

## 29. Deferred future extensions

The architecture should permit, but this branch does not implement:

- cargo-door animation;
- door jamming and breach;
- modelled cargo holds;
- attached physical containers;
- per-panel hit testing and penetration;
- panel detachment physics;
- functional weapons and sensors;
- animated or retractable landing gear;
- beam-light illumination and shadows;
- dynamic ship shadows;
- engine exhaust particles and deposits;
- engine heat and damage visuals;
- shield coverage mapped to hull faces;
- internal component protection mapping;
- exterior module fitting UI;
- ship salvage and visible field repairs;
- LOD and fleet batching;
- carried shuttles or small ships.

---

## 30. Decisions established by this specification

- Ships are designed around their physical contents, especially container stacks.
- A configured ship remains a composite model.
- New geometry uses `-Z` forward.
- The bare hull is a closed structural shell.
- Structural polygons have explicit surface roles.
- Armour panels are arbitrary planar convex n-gons.
- Panels are closed solids with distinct exterior, interior, and edge materials.
- Panel reuse is opportunistic, not a design constraint.
- Stable IDs are semantic and hull-prefixed.
- `all` is the literal namespace for reusable identities.
- Cargo doors are structural movable assemblies, initially rendered closed.
- Cargo doors may carry removable armour panels.
- Engines are independent external assemblies.
- Engines have no runtime visual size category.
- Engine definitions include attachment, clearance, exhaust, material, mirror, stem, and optional light metadata.
- Engine definitions do not require visible down-thrust or manoeuvring emitters.
- Cockpits have an exterior frame, glass, and full camera pose; no interior is modelled.
- Landing gear begins as fixed cuboid feet at explicit authored poses.
- Attachment ports are semantic capability-based geometry.
- Hulls reserve both marker-light and beam-light poses.
- Engines may own additional lights.
- Aerodynamics remain authored scalar parameters and are never derived from geometry.
- The shipyard/debug viewer is mandatory.
- The full catalogue follows a visually approved two-hull/two-engine vertical slice.
