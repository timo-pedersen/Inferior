# Station lighting pipeline — design specification (v2)

```text
Status: Proposed (replaces the abandoned shadow experiment)
Implementation: fresh, on a new branch off master — nothing is salvaged
                from wip/station-lighting-shadows (read-only history)
Related history: Docs-archive/Shadow_fail_retrospective.md
                 Docs-archive/Shadow_fail_design_spec.md
```

## 1. What this spec is

A single lighting pipeline for stations, ships, containers, and other nearby
objects, replacing per-mode `BasicEffect` lighting with one custom HLSL
lit-surface shader. Shadow mapping is one phase of that pipeline, not a
bolt-on.

The pipeline owns four lighting terms, introduced in phases:

```text
finalColor = vertexColor(albedo × AO)
           × ( ambient
             + sunColour × N·L × shadowTerm × eclipseTerm )
           [ + specularTerm ]        // reserved, later phase
           [ N from normal map ]     // reserved, later phase
```

There is no caster/receiver system. In this design every opaque lit object
both draws into the shadow map and samples it. The only configuration is a
per-mesh-class participation flag (section 6). The terms "caster" and
"receiver" appear below only to name the two ends of one depth comparison.

## 2. Conclusions inherited from the failed experiment (axioms, not re-derived)

Proven and adopted:

- shadow-map concept, light camera, and transform model were correct;
- `SurfaceFormat.Single` with explicitly encoded linear depth is the format;
- the striping was same-plane point-sampling error, **not** precision —
  analytic receiver-plane correction removed it in frozen comparisons;
- zero bias and a 3 mm safety bias were visually equivalent once the
  comparison was geometrically correct;
- the frozen-state diagnostic facility was the tool that produced every
  reliable conclusion.

Proven failures, now forbidden:

- large receiver normal offsets as an acne workaround (caused metre-scale
  shadow detachment, peter-panning, silhouette leakage);
- raising precision/resolution as a substitute for fixing comparison error;
- changing format, fitting, bias, and offset in the same step;
- letting diagnostic and production code intertwine;
- debugging small greeble before hull-level behaviour is settled.

## 3. Scene assumptions (new, simplify everything)

- **Exactly one station is fully rendered at a time.** Station orbits are
  separated by ≥ 100 km (enforced at system generation — separate small
  brief, see section 12). Beyond ~1 000 000 km a station is not rendered at
  all; between "near" and that limit it is a dot/sprite with no shadows.
- Therefore: one shadow context, fitted to one station, in **station-local
  metre space**. No multi-station budget, no atlas, no scheduling.
- Ships and containers near the station live inside the same shadow context.
- Stations are up to 2 km. Low poly count, dense greeble; detail comes from
  shaders, not tessellation.

## 4. Vertex data and bake-semantics change (the pivotal change)

Current state: `StationModuleMesh` accumulates
`VertexPositionNormalColorTexture` (normals exist CPU-side), then `Build()` /
`ToArrays()` strips normals and bakes `N·L × sunColour` into vertex color.

New rule:

```text
Vertex color  = albedo × ambient occlusion (+ deliberate tint/wear/interior
                overrides). NO directional term is ever baked.
Vertex normal = kept through Build() into the GPU buffer.
Directional light, shadow, eclipse, and (later) specular are computed in
the shader every frame.
```

Consequences:

- rotating stations are lit correctly without rebaking;
- `ApplyLighting()`'s directional part is deleted; AO and ambient-override
  passes (`ApplyAmbientOcclusion`, `BoostAmbientForFaceRange`) remain as the
  sole bake-time contributors;
- `MergeTransformedAndLit` becomes `MergeTransformed` (keeps normals, no lit
  bake);
- ships (`VertexPositionNormalTexture`) and containers
  (`VertexPositionNormalColorTexture`) already carry normals — they migrate
  onto the same shader with albedo in vertex color / material constant.

`BasicEffect` remains for planets, skybox, UI, and anything not in scope
here. It is retired from station/ship/container rendering only.

## 5. The lit-surface shader

One `.fx` file, `LitSurface.fx`, techniques selected by object needs:

- input: `VertexPositionNormalColorTexture` (one vertex format for all
  participants — ships migrate to it or get a colored variant technique);
- uniforms: world/view/projection, sun direction (object-local or world,
  one convention picked at implementation), sun colour, ambient,
  eclipse factor (scalar), shadow matrices + textures + params;
- output per section 1's formula.

Rules:

- the shader consumes the **same** authoritative transforms as the visible
  draw — presentation must not invent station transforms;
- shadow sampling may be compiled out (technique without shadow term) so
  Phase A runs the pipeline before any shadow map exists;
- specular and normal-map slots are reserved in the parameter layout now,
  implemented later (section 11).

### Transparency and alpha ownership

The interpretation of vertex-colour alpha is owned by the shader technique.
Opaque geometry never encodes opacity.

- **Opaque techniques:** alpha carries the self-illumination floor `S`
  (0 = fully sun-dependent, 1 = fully emissive, in-between = interior/fake-light
  floor). Output alpha is forced to 1. Lighting factor: `max(N·L·shadow, ambient, S)`.
- **Transparent technique [OPEN, future]:** alpha = opacity. Rendered as a separate
  pass regardless of encoding: alpha blending on, depth-write off, drawn after all
  opaque geometry, approximately back-to-front. Self-illumination, if needed, comes
  from a material constant, not the vertex.

A vertex is only ever drawn by one technique, so the channel never means two things
at once. The current glass pass already follows this shape (separate mesh, separate
pass, emissive).

Related rules:

- Transparent panes do not cast opaque shadows (see Participation) and do not write
  into shadow maps by default.
- If a surface ever needs BOTH per-vertex opacity and per-vertex self-illumination,
  the escape hatch is a custom vertex declaration with one extra float channel —
  do not build this before a concrete case exists.

## 6. Shadow mapping

### 6.1 Maps

Two maps, both fitted in station-local metre space, both
`SurfaceFormat.Single`, explicit normalised linear light-view depth, cleared
to far, point-sampled until correctness is proven:

```text
StationMap  2048², fitted to the whole station's participating bounds
            (2 km station → ~1 m/texel; depth span ~2.2 km → float
            precision is a non-issue)
FocusMap    2048², fitted to a region (~100–200 m, tunable) around the
            camera's point of interest (ship / docking approach)
            → ~5–10 cm/texel: contact shadows for greeble, bay interiors
```

Receiver logic samples FocusMap when the fragment lies inside its bounds
(with a small blend band at the edge), else StationMap. Map sizes and focus
extent are tuning constants, not architecture.

### 6.2 Participation

Everything opaque participates in both directions by default:

```text
Draws into maps:   hull, structural decoration, pipes, supports, containers,
                   equipment, antennas, dishes, window frames, docked/nearby
                   ships.
Samples maps:      every surface drawn with LitSurface.fx.
Excluded:          transparent panes (explicit policy per class), distant
                   LOD sprites, UI/debug geometry.
```

Every greeble class gets a documented casting policy. "Some instances happen
to cast" remains unacceptable. Because the ship is drawn into the same maps
that the station samples (and vice versa), ship-on-station and
station-on-ship shadows both fall out of one mechanism.

### 6.3 Acne strategy (strict ladder, one rung at a time)

1. **Back-face casting** for closed meshes: render back faces into the shadow
   map (cull front faces). For closed low-poly hulls this eliminates lit-face
   acne structurally — the stored occluder depth is the *far* side of the
   caster, so a surface never shadows itself. Per-mesh-class flag; greeble
   that is not closed (dishes, panels, antennas) casts front faces.
2. **Analytic receiver-plane correction** (re-derived per the failed spec's
   §Receiver-plane correction — the derivation is documented and was proven;
   the code is rewritten, not copied): evaluate receiver depth at the sampled
   texel centre; guarded fallback near grazing angles.
3. **Constant safety bias** ≤ a few mm equivalent, only against a
   demonstrated residual defect, expressed in both normalised units and
   metres.

Forbidden: receiver normal offsets that move the UV lookup by a visible
fraction of a texel; any bias that visibly moves a contact shadow.

### 6.4 Rebuild policy

- FocusMap: regenerated **every frame** the ship/camera is inside shadow
  range (it contains the moving ship; generation is measured-cheap).
- StationMap: regenerated when the station-local sun direction changes past
  an angular threshold, the station rotates past a threshold, caster
  geometry changes, or the shadowed station changes. If per-frame proves
  cheap enough, thresholds simplify to "every frame" — measure first.
- Shadow context active only while the station renders as geometry;
  FocusMap only below a closeness threshold (tunable).

## 7. Planetary / moon eclipse

Never in the shadow map — wrong scale by orders of magnitude. Analytic:

```text
For the station position each frame:
  αs = angular radius of star            (Rstar / distance)
  αp = angular radius of occluding body  (Rbody / distance)
  δ  = angular separation of centres
  δ ≥ αs+αp → eclipseTerm = 1 (no eclipse)
  δ ≤ |αp−αs| and αp ≥ αs → 0 (total)
  else → covered-fraction of the solar disc (standard circle-overlap area)
```

- evaluated per shadowed object (station, ship) — a scalar uniform, not
  per-pixel; smooth penumbra falls out of the overlap function;
- checked against the planets and moons that can plausibly occlude
  (coarse cone pre-test keeps it a handful of dot products);
- computed from authoritative simulation snapshot positions.

**[OPEN] Atmospheric reddening:** while the sun's disc grazes a body with
atmosphere, tint `sunColour` toward red/orange as a function of penumbra
depth. Cheap (colour ramp on the same overlap data), visually strong, but
deferred until the plain eclipse term is verified.

## 8. Diagnostics (rebuilt fresh, same catalogue)

The failed experiment's diagnostic ladder is re-specified wholesale — it was
the part that worked. Reimplement (fresh code) with a hard rule: diagnostics
read the exact frozen matrices/textures the production comparison uses, and
live behind a debug switch that compiles/strips cleanly.

Retained catalogue: light-camera silhouette; caster coverage; receiver UV
grid; receiver depth; sampled caster depth; depth delta; slope factor;
module / mesh-class / caster-owner ID views; hull-only isolated pass;
isolated bias modes; corrected binary comparison; candidate-logic shaded
preview; **frozen shadow generation** (textures, matrices, span, sun
direction preserved while the camera flies freely). Debug camera views of
the light frustum as per existing debug-camera docs.

## 9. Quality targets

Unchanged in substance from the previous spec:

- module shadows directionally and geometrically correct at inspection
  distances; contact shadows attached; no repeating bands on broad faces;
  no crawling while frozen; small greeble casts footprints proportionate to
  silhouette; equivalent instances behave identically; no bias-induced
  detachment; every dark region attributable to a real light-space occluder;
- shadows visibly OK, not perfect: bay-opening interiors read as shadowed
  volumes; greeble reads as attached to the surface;
- at distance, stability over detail; greeble shadows may drop out by LOD
  policy, never inconsistently.

## 10. Phases (each has a verification gate; do not proceed past a failing gate)

```text
Phase A  Pipeline swap, no shadows
         Normals kept through Build(); directional bake removed;
         LitSurface.fx (ambient + N·L only) renders station, ship,
         containers. GATE: visual parity with current master at the
         same sun angle + correct lighting under station rotation.

Phase B  StationMap, hull only
         Back-face casting, plane correction, zero bias, point sampling.
         GATE: previous spec's Stage 1–4 checks (silhouette, coverage,
         UV mapping, self-shadow, module-to-module ownership).

Phase C  Greeble by class
         One class at a time (supports/pipes → containers/equipment →
         dishes/antennas → window frames). GATE per class: caster
         presence + receiver result on representative instances.

Phase D  FocusMap + dynamic objects
         Tight map, per-frame regen, ship drawn into maps and sampling
         them. GATE: ship shadow slides over station; bay interior
         shadows at docking range; station shadows the ship.

Phase E  Planetary eclipse term
         GATE: station visibly darkens through umbra transit with smooth
         penumbra; timing agrees with system-map geometry.

Phase F  Quality pass
         PCF / small filtering, map-size tuning, rebuild thresholds,
         LOD policy, optional safety bias — one variable at a time.

Phase G  Specular highlights          [reserved slot, own brief]
Phase H  Normal/bump maps             [reserved slot, own brief]
Phase I  Atmospheric eclipse tint     [OPEN, after E]
```

## 11. Future slots (designed for now, built later)

- **Specular:** per-mesh-class material constants (broad weak lobe on
  structural metal, sharp on glass/polished); slots into section 1's
  formula. Needs nothing new from the vertex format.
- **Normal maps:** perturbs N before all lighting terms; requires tangent
  frame — [OPEN] whether tangents are baked into a wider vertex format or
  derived in-shader from screen-space derivatives (preferred first try;
  no format change).

## 12. Related but separate briefs

- **System generation:** enforce ≥ 100 km minimum separation between any two
  station orbits (no crossings within 100 km) at generation time.
- **Docs cleanup:** execute Shadow_fail_docs_cleanup.md against master,
  with this spec replacing the deferred-design placeholders.

## 13. Invariants

```text
- One coordinate model: caster, receiver, and visible draw share the same
  authoritative station-local transforms; sun direction transformed into
  the same frame; presentation never invents transforms.
- Vertex color never contains a directional term.
- Receiver bias must never visibly move a contact shadow.
- Diagnostics and production share frozen state exactly.
- One variable changes at a time during shadow correctness work.
- Every mesh class has an explicit, documented shadow participation policy.
- Visual systems are never authoritative for position/orientation/velocity.
```
