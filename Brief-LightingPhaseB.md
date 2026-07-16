# Brief — Lighting pipeline Phase B: StationMap shadows, hull only

```text
Design authority: Docs/station-lighting-pipeline-spec.md (sections 6, 8, 10 Phase B)
Branch:           continue on feature/lighting-pipeline-phase-a (or successor) — Timo's call
Forbidden:        reading or copying code from wip/station-lighting-shadows.
                  EXCEPTION: you may (should) read the archived DESIGN text
                  Docs-archive/Shadow_fail_design_spec.md §"Receiver-plane correction"
                  as the documented derivation to reimplement fresh. Design text, not code.
Gate:             spec verification Stages 1–4 (light camera/coverage, receiver mapping,
                  module self-shadowing, module-to-module occlusion). Do not proceed to
                  greeble casting (Phase C) in this brief under any circumstances.
```

## Goal

One shadow map fitted to the currently-rendered station, station-local metre space,
**module hulls as casters only**, sampled by station surfaces (hull + decoration) in
`LitSurface.fx`. Result: modules shadow themselves and each other. No greeble casting,
no ship/container involvement, no FocusMap, no filtering, no eclipse work.

## Inherited axioms (proven last time — do not re-litigate)

- `SurfaceFormat.Single`, explicitly encoded linear light-view depth.
- Striping on lit faces is same-plane point-sampling error, NOT precision — the fix is
  analytic receiver-plane correction, never a receiver normal offset.
- Zero bias first; a ≤3 mm constant bias only against a demonstrated residual defect.
- One variable at a time. Diagnostics must consume the exact production matrices.

## Design decisions (settled)

### D1. Map and light camera

- 2048×2048 `SurfaceFormat.Single` render target, cleared to far (1.0).
- Depth encoding: `normalisedDepth = (-lightViewZ - near) / (far - near)`.
- Orthographic light camera in **station-local metre space**: transform world
  `SceneLighting.SunDirection` into the station frame using the SAME station rotation
  the visible draw uses (`station.GetOrientation(_gameTimeSeconds)` — one shared
  evaluation per frame, never two).
- Fit: AABB of participating hull geometry in light space + modest padding (~5 m).
  Near/far span likewise fitted. Fitting uses exactly the geometry drawn into the map.

### D2. Casters — hull only, back faces

- Participants: module hull boxes (`BuildHullMesh` output) and the docking-bay hull
  (`DockingBayHull` — it IS the module hull for that module, even though it lives in
  `mod.Mesh`). Nothing else: no decoration meshes, no glass, no containers, no ship.
- **Back-face casting**: render casters with front-face culling (cull the faces whose
  winding is kept in the normal pass). Hull boxes and the bay hull are closed meshes;
  the stored depth is then the caster's far side, which structurally prevents lit-face
  self-shadow acne. Document per-mesh-class: hull = closed = back-face cast.
- Caster shader: minimal dedicated `ShadowCaster.fx` (position-only VS, writes encoded
  linear depth). Do not overload `LitSurface.fx` for this; it needs different vertex
  input assumptions and no lighting state.

### D3. Receivers — station surfaces via LitSurface.fx

- New shadow-sampling variants of both techniques (`BakedColorLitShadowed`,
  `DynamicLitShadowed`) — station deco uses the first, station hull boxes the second.
  Non-station objects (ship, containers, calibration cube) stay on the unshadowed
  techniques this phase.
- Per-draw parameters (every one set explicitly from C#; the EclipseFactor lesson is
  now project policy): shadow map texture (point sampling), station-local→light-UV
  matrix, near/far span, map texel size, and the module's local→station-local
  transform so the pixel shader can reconstruct the station-local position that the
  caster pass used. Caster and receiver must share those module transforms — same
  source, same frame, single evaluation.
- Shadow term: `lit` when receiver depth (after D4 correction) ≤ stored depth, else
  shadowed. Shadowed = the sun term multiplies by 0; ambient and S are untouched.
  Samples outside the map or beyond far = lit.

### D4. Receiver-plane correction, zero bias

- Implement the analytic receiver-plane correction from the archived design text
  (§ reference above): derive the receiver-depth gradient across shadow U/V from the
  light-space receiver normal, the orthographic projection dimensions, the depth span,
  and the actual UV Y-inversion; evaluate receiver depth at the sampled texel centre;
  guarded fallback at near-grazing angles (clamp the correction magnitude).
- Zero constant bias, zero normal offset. If a residual defect appears, STOP, capture
  screenshots + frozen state, and report — do not tune bias past ~3 mm equivalent, and
  never move a contact shadow.

### D5. Rebuild policy (Phase B minimum)

- Regenerate the map every frame for the one rendered station to start; measure and
  report the cost (the map render is expected cheap — hull boxes are ~24 verts each).
- If measurably free, per-frame stays and thresholds are deferred to Phase F. No
  premature caching machinery.

### D6. Diagnostics (small, but real)

All diagnostics read the same matrices/textures production uses. One debug facility:

- **Overlay view** (debug key): draw the shadow map into a screen corner via
  SpriteBatch (remap depth to grayscale) — instant Stage 1 silhouette check.
- **Freeze toggle** (debug key): stop regenerating the map/matrices while the camera
  flies free — Stage 3/4 inspection and crawl detection.
- **Binary shadow view** (debug key): swap the PS shadow term for white/black
  lit/shadowed output — makes banding and boundaries unambiguous in screenshots.
- Key choices: propose and CHECK for conflicts in the input code first (the Ctrl+C
  lesson); report the chosen keys. Log/HUD-message the fitted bounds, span, and map
  cost at first generation and on freeze, via the existing SystemMessage path.

The full diagnostic ladder (UV grids, owner IDs, hull-only isolation) is spec section 8
— build only the three above in this brief; add others only if a gate check fails and
names the need.

## Suggested step order

1. `ShadowCaster.fx` + render-target plumbing + light camera fit; overlay view.
   GATE Stage 1: station silhouette correct in overlay, all modules present,
   orientation matches the rendered station as the station rotates.
2. Receiver path in `LitSurface.fx` WITHOUT correction, binary view, freeze.
   Expect same-plane banding — confirm it looks like the documented failure mode
   (that is the correct intermediate state, not a bug to fix with offsets).
   GATE Stage 2: coherent mapping, no axis inversion, no module-specific mismatch.
3. Analytic receiver-plane correction. GATE Stage 3: banding gone on flat lit faces,
   genuine module shadow boundaries unmoved (frozen A/B screenshots).
4. GATE Stage 4: with the station rotating, module-on-module shadows sweep coherently;
   every large dark region has a plausible occluder along the light ray (visual check
   from the light direction; formal owner-ID diagnostics only if this fails).
5. Measure per-frame map cost; report numbers.

## Verification & reporting

- Builds; full suite passes; per-step gates with screenshots for Timo (binary view
  makes Stage 3 A/B pairs decisive; Ctrl+C + paste is the workflow).
- Honest language: implemented / builds / tests / inspected / needs-Timo.
- Timo's in-engine sign-off list: overlay silhouette; no banding on broad lit faces;
  module shadows attached and direction-correct; nothing crawls while frozen; deco
  (windows, pipes, antennas) correctly RECEIVES module shadows despite not casting.
- Update `Docs-ai/!current-state.md` (Phase B row) and `architecture-map-ai.md`
  (ShadowCaster.fx, shadow-map owner class, new LitSurface techniques).
- Anything ambiguous or contradicting the spec: stop and report, do not improvise.
