### Station lighting and shadow design specification

## Status

**Deferred experimental feature.**

The current gameplay branch does not depend on station shadows. Prior experimental work is preserved separately and may be resumed later.

## Purpose

Station lighting should make large orbital structures readable as three-dimensional objects without sacrificing the stark visual character of space.

The system should provide:

- clear large-scale light and dark structure;
- attached shadows from modules and surface detail;
- stable visual behaviour at station-interaction distances;
- predictable results across generated station layouts;
- low runtime cost compared with dynamic per-light shadowing.

## Visual intent

Stations are predominantly hard-surface industrial structures illuminated by a distant star.

Desired characteristics:

- one dominant directional light;
- crisp but not necessarily perfectly hard shadows;
- readable module overlap and station silhouette;
- small surface detail visible through contact shadows;
- broad, weak specular response on sand-blasted structural metal;
- sharper highlights on windows and selected polished surfaces;
- shadow darkness that preserves enough ambient readability for navigation.

The system should avoid:

- flickering or crawling shadow bands;
- visible receiver acne;
- detached or floating contact shadows;
- light leaking around large silhouettes;
- large regions shadowed by geometrically impossible occluders;
- missing shadows from whole classes of greeble;
- changes in shadow position caused only by receiver bias.

## Scope

Initial scope:

- static station hull modules;
- static decoration and greeble;
- windows and other station-attached meshes where appropriate;
- one star-direction shadow map for the selected or nearby station.

Deferred scope:

- dynamic ship shadows;
- moving station components;
- planetary shadows;
- multiple local shadow-casting lights;
- soft-shadow filtering beyond a minimal validated method;
- distant stations that occupy only a few screen pixels.

## Coordinate and authority rules

All station-shadow calculations must use a single coherent coordinate model.

Required invariants:

- caster and receiver use the same station-local transforms;
- the shadow camera uses the same station orientation as the rendered station;
- the star direction is transformed into the same station-local frame;
- shadow-map fitting uses the same geometry that participates in casting;
- presentation must not invent an independent station transform;
- station shadow state is derived from authoritative station and system state, not camera-relative approximations.

Station-local metre space is preferred because it keeps values compact and gives direct physical meaning to bounds and offsets.

## Basic shadow-map algorithm

### Shadow generation

For a station selected for shadowing:

1. Determine the star direction in station-local space.
2. Construct an orthographic light camera aimed along that direction.
3. Compute bounds for all participating station caster geometry.
4. Fit the orthographic projection to those bounds with modest safety padding.
5. Render caster geometry into a single-channel floating-point texture.
6. Store explicitly normalised linear light-view depth.

Conceptually:

```text
normalisedDepth =
    (-lightViewZ - nearDistance) /
    (farDistance - nearDistance)
```

The texture is cleared to the far-depth value before rendering.

### Shadow reception

For each station receiver fragment:

1. Transform the receiver position into light space.
2. Convert projected X/Y into shadow texture UV.
3. Reject samples outside the valid map.
4. Compute receiver depth using the same linear-depth convention.
5. Sample the stored caster depth.
6. Compare receiver and caster depth.
7. Modulate direct star lighting when the receiver lies behind the stored caster.

The receiver comparison must account for the fact that a point-sampled depth value represents a texel-centre location, while the visible fragment may lie elsewhere within that texel.

For planar receivers under an orthographic shadow camera, a receiver-plane correction may adjust receiver depth to the sampled texel centre before comparison.

## Receiver-plane correction

The purpose of receiver-plane correction is to remove same-plane aliasing without moving the receiver’s UV lookup.

The correction should:

- derive the receiver-depth gradient across shadow U and V;
- evaluate receiver depth at the centre of the sampled texel;
- compare that corrected depth against stored caster depth;
- leave the sampled UV and therefore genuine shadow position unchanged.

This is preferable to moving the receiver along its surface normal.

The correction must include:

- the actual projection dimensions;
- the depth span;
- shadow-UV axis conventions;
- the receiver normal in light-view space;
- a stable fallback for near-degenerate orientations.

Near-grazing receiver planes may require a guarded fallback rather than an unbounded correction.

## Bias policy

Bias exists only to cover numerical uncertainty after the geometric comparison is correct.

Design rules:

- begin with zero receiver normal offset;
- begin with zero or extremely small constant depth bias;
- add bias only when a specific residual defect is demonstrated;
- express bias in both normalised depth units and equivalent metres;
- validate bias at the current fitted depth span;
- never use a large receiver normal offset as the primary acne solution;
- never accept a bias that visibly moves a contact shadow.

A receiver normal offset that moves lookup coordinates by a noticeable fraction of a shadow texel should be treated as suspect.

## Shadow-map format and resolution

Initial target:

- 2048×2048;
- single-channel floating-point depth;
- explicitly encoded linear depth;
- point sampling during initial correctness work.

Resolution should remain fixed while geometry, projection, and comparison correctness are being validated. Increasing resolution must not be used as a substitute for solving self-shadow comparison errors.

Later quality work may consider:

- adaptive resolution;
- limited PCF;
- better fitting;
- per-distance quality levels.

These should be introduced only after the unfiltered result is geometrically correct.

## Rebuild policy

The star is effectively directional and distant, but its local direction may change with simulation time and system geometry.

A station shadow map may be regenerated when:

- the selected station changes;
- its caster geometry changes;
- the station orientation changes;
- the local star direction changes beyond a defined angular threshold;
- the player approaches a station requiring a higher-quality map;
- the previous generation becomes too old for the current simulation state.

Candidate policy:

- rebuild immediately on station selection or major transform change;
- rebuild after a small star-direction angular change;
- optionally limit refresh frequency while changes remain small;
- avoid rebuilding every frame for static station geometry.

The final threshold and interval must be determined visually and by performance testing.

## Geometry participation

Caster coverage must be explicit by mesh class.

Expected participants:

- hull;
- structural decoration;
- pipes and supports;
- containers and equipment;
- antennas and dishes;
- window frames where their depth is meaningful.

Transparent panes may require separate treatment and should not automatically behave as opaque casters.

Every visible static greeble class should have a documented casting policy. “Some instances happen to cast” is not acceptable.

## Quality targets

At normal station inspection distances:

- large module shadows must be directionally and geometrically correct;
- contact shadows must remain visually attached;
- broad lit faces must not show repeating bands;
- shadow boundaries must not crawl while the map is frozen;
- small box-like greeble should cast a footprint proportionate to its projected silhouette;
- equivalent instances should behave consistently;
- there must be no metre-scale detachment caused by bias;
- shadow ownership across modules must correspond to an actual light-space occluder.

At longer distances:

- stability is more important than tiny detail;
- small greeble shadows may be omitted deliberately, but not inconsistently;
- transitions between shadow quality levels must avoid popping where practical.

## Diagnostics

The following diagnostics were proven useful and should be retained in any resumed implementation:

- light-camera solid silhouette;
- caster coverage;
- receiver UV grid;
- receiver depth;
- sampled caster depth;
- receiver-minus-caster depth delta;
- slope or grazing factor;
- module identifier;
- mesh-class identifier;
- caster-owner identifier;
- same-owner versus other-owner display;
- selected-module hull-only caster pass;
- isolated bias and normal-offset modes;
- analytic-corrected binary comparison;
- normal shaded preview using candidate receiver logic;
- frozen shadow-generation state.

Freeze must retain:

- shadow textures;
- light-view and projection matrices;
- near/far span;
- local star direction;
- diagnostic ownership textures.

The camera may continue moving while shadow generation is frozen.

## Verification procedure

Verification should proceed from large-scale geometry to small detail.

### Stage 1: light camera and coverage

Confirm:

- correct station silhouette;
- no missing large modules;
- caster bounds cover the intended geometry;
- station orientation agrees between rendering and shadow map.

### Stage 2: receiver mapping

Confirm:

- coherent UVs;
- consistent receiver depth;
- no axis inversion;
- no module-specific transform discrepancy.

### Stage 3: module self-shadowing

Use isolated hull-only passes.

Confirm:

- planar faces do not produce repeating bands;
- receiver-plane correction removes self-aliasing;
- genuine shadow boundaries do not move.

### Stage 4: module-to-module occlusion

Use caster ownership.

For suspicious dark regions, identify:

- receiving module;
- owning caster module;
- whether a straight light-space ray intersects the alleged occluder.

### Stage 5: greeble completeness

Test representative:

- box;
- pipe;
- dish;
- antenna;
- window frame;
- container.

Confirm both caster presence and receiver result.

### Stage 6: dynamic behaviour

Unfreeze and verify:

- changing star direction;
- station selection;
- map regeneration;
- approach and departure;
- multiple generated station layouts.

## Staged rollout

### Phase A — hull-only correctness

- module hulls only;
- floating-point linear depth;
- analytic receiver-plane correction;
- no normal offset;
- no filtering.

### Phase B — structural decoration

Add larger supports, frames, and pipes. Verify caster completeness before adding smaller detail.

### Phase C — small greeble

Add containers, dishes, antennas, and equipment by class. Each class receives explicit tests.

### Phase D — quality and performance

Only after correctness:

- small safety bias if needed;
- filtering;
- adaptive rebuild timing;
- resolution policy;
- distance-based detail.

### Phase E — dynamic casters

Add ship or moving components separately rather than folding them into the static-station map prematurely.

## Lessons and constraints from the abandoned implementation

- A visually working low-precision baseline is valuable and should be preserved before precision work begins.
- Increasing texture precision can reveal rather than solve comparison errors.
- Change one variable at a time: target format, depth encoding, projection fitting, bias, and normal offset must not all change together.
- Large receiver normal offsets are forbidden as an acne workaround.
- A binary shadow result is not sufficient; ownership and caster coverage must also be inspectable.
- Diagnostics must use exactly the same frozen matrices and texture generation as the production comparison.
- Small greeble problems should not be investigated until hull and module-to-module behaviour is correct.
- Experimental code must remain isolated from the stable gameplay branch.

---

