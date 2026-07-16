# Brief — Lighting pipeline Phase A: pipeline swap, no shadows

```text
Design authority: Docs/station-lighting-pipeline-spec.md (sections 4, 5, 10 Phase A)
Read first:       Docs-ai/!invariants.md (§11 lighting constraints), this brief
Branch:           new feature branch off master
Forbidden:        reading or copying anything from wip/station-lighting-shadows
```

## Goal

Move the directional lighting term out of bake-time vertex colours and into one custom
HLSL shader (`LitSurface.fx`). After this brief: vertex colour = albedo × AO/self-light
only; normals travel to the GPU; the sun term is computed per frame in the shader.
**No shadow maps, no eclipse term, no specular — those are later phases.**

Gate: visual parity with master at the same sun angle, plus correct lighting when the
station rotates (which baked lighting gets wrong today).

## Current state (verified against master)

Station rendering is three passes in `SystemSpaceState.Stations.cs` (`DrawStations`):

1. **Hull pass** — `VertexPositionNormalTexture`, real-time `BasicEffect` N·L +
   procedural texture. Already dynamic; needs only migration to the new shader.
2. **Decoration pass** — `VertexPositionColorTexture`, `LightingEnabled=false`, texture
   modulates baked vertex colour. The directional term is baked in — this is the pass
   Phase A changes fundamentally.
3. **Glass pass** — baked vertex colour, `White` texture, emissive by design.

The bake chain: `StationGenerator.BakeLighting` → `StationModuleMesh.ApplyLighting`
(per-face `factor = max(dot(worldN, sunDir), ambient)`, colour ×= factor × sunColour;
faces with R+G+B > 370 are skipped as emissive) → `BoostAmbientForFaceRange` (interior
faces: `factor = max(sunDot, interiorBrightness)` rescale). AO
(`StationDecorator.ApplyAmbientOcclusion`) multiplies colours independently — AO stays.

Critically: `StationModuleMesh._verts` is already `VertexPositionNormalColorTexture`
(normals exist CPU-side); `Build()` and `ToArrays()` strip normals when converting to
`VertexPositionColorTexture`. `MergeTransformedAndLit` bakes directional light into
merged geometry (containers/text placed on modules).

Ships: `ShipMeshRenderer` via `MeshRenderer.DrawDynamic` (BasicEffect). Debug
containers: `DrawDynamic`/`DrawDynamicColored`. Both already carry normals.

## Design decisions (settled, do not re-decide)

### D1. Vertex colour semantics

After this brief, for station meshes: vertex colour carries **albedo × AO × deliberate
tints/wear only**. `ApplyLighting`'s directional multiply is deleted. Never bake
`sunColour` or N·L into colour again.

### D2. Self-illumination floor in vertex alpha

Opaque station geometry repurposes vertex colour **alpha** as a self-illumination floor
`S` (0 = fully sun-dependent, 1 = fully emissive):

- normal faces: `S = 0`;
- emissive faces (current R+G+B > 370 rule, applied where `ApplyLighting` applies it
  today): `S = 1`;
- interior-override faces (`AmbientOverrideFaceStart/Count`): `S = interiorBrightness`
  computed exactly as `BoostAmbientForFaceRange` does now (base floor + door proximity
  + overhead cue + corner noise, clamped) — the function stops rescaling colours and
  instead writes `S`.

Shader lighting factor (parity with today's max-semantics):

```hlsl
float nl     = dot(normalize(worldNormal), SunDirection);   // L points toward star
float factor = max(max(nl, Ambient), S);
rgb          = vertexColor.rgb * texture.rgb * factor * SunColour;
alpha        = 1.0;                                          // opaque passes
```

**Verification step required:** before repurposing alpha, confirm no opaque station
mesh (deco or hull-merged geometry) relies on vertex alpha < 255 for blending. Grep
every `Color` construction feeding `StationModuleMesh` and check the deco/glass draw
blend states. Glass is a separate mesh and is NOT converted (see D5). If a real
conflict is found, stop and report — do not invent a different encoding silently.

### D3. One shader, two lighting styles

`Inferior.Game/Content/Effects/LitSurface.fx` (register in `Content.mgcb` like
`Atmosphere.fx`). Two techniques in Phase A:

- `BakedColorLit` — for station deco meshes: vertex format
  `VertexPositionNormalColorTexture`; max-semantics factor per D2.
- `DynamicLit` — for hull/ships/containers: replicates `BasicEffect`'s directional
  model (`ambientColor + sunColour × saturate(N·L)`, additive) so those passes keep
  their current look. Vertex colour term optional (containers use it; hull and ships
  use material/diffuse constant + texture).

Parameters: `World`, `View`, `Projection`, `SunDirection` (world space, TOWARD star —
same convention as `SceneLighting.SunDirection`), `SunColour`, `Ambient`, `Texture`.
World has uniform scale (render scale × rotation × translation): transform normals by
the rotation part or normalize after `mul((float3x3)World, normal)` — document which.
Reserve (declare, unused) parameter slots for shadow matrices/textures and eclipse
factor so later phases don't reshuffle the layout.

### D4. Mesh build changes

- `StationModuleMesh.Build()` / `ToArrays()`: emit `VertexPositionNormalColorTexture`
  (type already exists in `Inferior.Rendering`) — stop stripping normals. Update all
  consumers.
- `ApplyLighting(...)`: delete the directional multiply. What remains of the bake pass
  is: emissive detection → `S=1`, everything else `S=0` (write into alpha). Rename to
  reflect new role (e.g. `ApplyIlluminationFlags`).
- `BoostAmbientForFaceRange`: same brightness computation, writes `S` instead of
  rescaling RGB.
- `MergeTransformedAndLit` → `MergeTransformed`: keep transform + handedness logic,
  drop the lit bake, keep normals, set `S=0` (or propagate caller-specified S).
  Update callers (`StationDecorator` container/text placement).
- `StationGenerator.BakeLighting`: shrinks accordingly. AO passes untouched.
- The Full vs flat (`_decoMeshesFlat`) detail variants both go through the same change.

### D5. Explicitly out of scope / unchanged

- Glass pass: stays exactly as today (baked colour, unlit, separate mesh).
- Screen-space glow sprites (`StationLightInfo` / additive SpriteBatch): unchanged.
- Planets (`CelestialBodyRenderer`), skybox, orbit rings, UI: unchanged, keep
  `BasicEffect`.
- No shadow maps, no eclipse, no specular, no normal maps, no PCF — later phases.
- No change to station generation topology, decoration passes, occupancy, or AO
  values.

### D6. Draw-path changes

- `DrawStations` hull + deco passes switch from `_effect` (BasicEffect) to
  `LitSurface.fx` techniques per D3. World-matrix construction is unchanged.
- `ShipMeshRenderer` and debug containers migrate `DrawDynamic`/`DrawDynamicColored`
  onto `DynamicLit`. `MeshRenderer` keeps its BasicEffect paths for any remaining
  caller; remove modes that end up with zero callers rather than leaving duplicates.

## Suggested step order

1. `LitSurface.fx` + content registration; `DynamicLit` technique; migrate debug
   containers first (smallest surface, easy A/B against BasicEffect look).
2. Migrate ship hull (`ShipMeshRenderer`).
3. Alpha-usage verification (D2); report findings before proceeding.
4. Mesh build changes (D4) + station deco pass on `BakedColorLit`; station hull pass
   onto `DynamicLit`.
5. Delete dead bake code; build; run full test suite.
6. Update `Docs-ai/!current-state.md`: Phase A row (implemented / builds / tests —
   with honest verification language; visual parity is Timo's call, not yours).

## Verification

- Solution builds; `Inferior.Game.Test` passes.
- Screenshot-comparable checks for Timo (he verifies in-engine — do not claim visual
  confirmation yourself):
  - station at same sun angle looks materially identical to master (deco brightness,
    emissive windows/lights still bright, bay interior gradient and ceiling cue
    intact, AO shading intact);
  - a rotating station's lit side follows the sun (new capability — master bakes it
    wrong);
  - ship and containers look unchanged;
  - glass and glow sprites unchanged.
- Known acceptable deviation: baked 8-bit quantisation of the old directional term vs
  float shader math — slightly smoother shading is expected, not a defect.

## Reporting

Report per step: implemented / builds / tests pass / inspected. List every file
touched. Flag anything that contradicts this brief or the spec instead of improvising
around it — especially the D2 alpha verification.
