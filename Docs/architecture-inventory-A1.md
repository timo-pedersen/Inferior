# A1 — Parallel Code Path Inventory

> Originally a read-only inventory — no code changes were made while producing it. Run on
> `master` (`b592af9`) after the mega-station/GPU-streaming branch merge, per the brief's
> precondition.
>
> **Coverage note (per the brief's own escape hatch):** `Inferior.Rendering` (21 source
> files) was read in full. The two named "known examples" (text-on-surface, container
> duplication) were traced across `Inferior.Game` to their actual call sites, since neither
> lives in Rendering. Beyond that, `Inferior.Game`, `Inferior.Gameplay`, `Inferior.Galaxy`,
> `Inferior.Core` and `Inferior.UI` got targeted searches (the specific probes the brief
> lists — `Vector3.Cross`, `new BasicEffect`, `1e-9`, `new Random`, hash functions,
> `MeasureString`, `SetData`, etc.) rather than a full file-by-file read — `Inferior.Game`
> alone is ~250 files including the entire `Station/Megastations/` subsystem, too large to
> read exhaustively in this pass. See §9 for exactly what that leaves unchecked.
>
> **Update (2026-09-11):** Timo authorized action on five of this inventory's findings; see
> §0 for what actually changed. The findings below are left as originally written (the
> as-found record) with inline "**Update:**" notes marking what moved; §0, §1, §5, §6 and §8
> carry the current status.

---

## 0. Status update — findings acted on (2026-09-11 – 2026-09-12)

Per Timo's instruction, eleven findings were acted on across two sessions (five, then three
more low-risk clusters, then two final follow-ups, all on 2026-09-11; the basis-from-normal
cluster on 2026-09-12 after in-engine confirmation of the earlier work). Everything else in
this document is still an open finding, not a task queue — nothing else was changed as a
result of this inventory.

| Finding | Action taken | Result |
|---|---|---|
| §2 Deterministic string/seed hashing — `SeededRandom.StableStringHash` vs `StationGenerator.NameHash` | `NameHash` now delegates to `SeededRandom`/`StableStringHash` instead of its own hand-rolled polynomial hash (`StationGenerator.cs`). | Done. Deliberately reshuffles every existing station's generated layout (procedural baselines are regenerated, not persisted — expected, not a regression, per `!invariants.md`). Downstream pinned-fixture tests that assumed the old seed were refreshed to match (`StationTextureCompactionTests`, several `Megastation*Tests`). |
| §2 Deterministic string/seed hashing — `StationTextureRegistry.HashPalette`'s `GetHashCode()` chain | Follow-up to the above, closing the cluster fully: `HashPalette` now mixes each field (surface enum, `Color.PackedValue`, and `BitConverter.SingleToInt32Bits` of each float) through `SeededRandom`'s own `Derive` chain instead of `h = h*31 + value.GetHashCode()`. | Done. Reshuffles the RNG-driven pixel noise for every station's generated textures (the seed `GeneratePixels` mixes in changes) — not the pixel *counts*/dedup already refreshed for the `NameHash` change, a separate pinned pixel-checksum fixture (`SystemMaterialLibraryTests.OrdinaryStationTextureFixtureRemainsByteIdentical`) needed refreshing too. |
| §2 Triangle winding — `StationModuleMesh.AddQuad` had no correction or validation | Added finite-vertex, degenerate-first-triangle, degenerate-second-triangle, and winding-consistency checks. Follow-up: converted from unconditional `throw` to `Debug.Assert`, matching the codebase's existing dev-only-check convention (`MegastationInteriors.cs` etc.) — closes the "gate behind a debug flag later" promise from the original request. Still does **not** auto-correct like `AddQuadProjected`/`ChamferedBox.WindFace` — a third posture (see §2/§8, still not unified with the other five). | Done. **Behavioural note:** in a Release build (no `DEBUG` symbol) these checks now compile away entirely — bad geometry propagates silently instead of throwing. That's the deliberate trade-off Timo asked for (a dev-time diagnostic, not a production contract), flagged here since it's a real behaviour change, not pure refactoring. |
| §2 UI text measurement — `FontHelper.Measure` bypassed at 8+ call sites | Fixed all of them: `TextBox.cs` (`MeasureWidth`, used on arbitrary player-typed substrings for cursor placement — the most plausible actual bypass exception source — plus 3 `"A"`-glyph sites), `TextBlock.cs` (2 sites), `SystemConsole.cs` (1 site), `LedIndicator.cs` (2 sites), `UIRenderer.cs` (2 sites, already-sanitised input so lower risk but now consistent). | Done. Solution-wide grep for `.MeasureString(` outside `FontHelper.cs`/tests now returns nothing. |
| §3/§4 `MeshFactory.CreateBox`/`CreateQuad` dead ends — "perhaps replaced?" | Investigated via `git log --all --oneline --follow`; traced to the earliest "Phase 2" commit, predates station generation entirely. `StationGenerator.PrepareBoxHullMesh` (not `MeshFactory`) is the real, current box-hull builder. | Answered, not replaced — confirmed genuinely dead code, not superseded by something central. No removal action taken; still listed in §4. |
| §5 `architecture-map-ai.md` drift | DataBus row corrected (8→11 channels); `## Inferior.Rendering` section completed (10→21 files, with dead-code/duplication notes); `Station/Megastations/` section added from scratch (was entirely absent — now ~49 files across Structural/Zoning/Flight-interior/Landing/Bolon subsections, explicitly flagged as name/class-derived locators, not individually deep-read). | Done for this pass. Timo noted he'll revisit for a fuller regeneration later — this was "get it in order now," not the wholesale regeneration the doc's own header describes. |
| §2 CPU-mesh → GPU-buffer upload — `SemanticHullGpuMesh`/`CockpitGpuMesh`/`EngineGpuMesh` each hand-rolled the same buffer allocation | New `Inferior.Rendering/GpuBufferFactory.cs`: a small `Create(graphicsDevice, vertices, indices)` helper doing just the `VertexBuffer`/`IndexBuffer` allocate-and-`SetData` step. All three `Create` methods now call it; each type's own per-part mapping/record shape and `SemanticHullGpuMesh`'s empty-part skip stayed untouched (that skip is a real behavioural difference from Cockpit/Engine, not silently unified). | Done. |
| §2 `BasicEffect` unlit/vertex-colour preset — 4 identical constructions | New `Inferior.Rendering/BasicEffectPresets.UnlitVertexColour(gd)`. Replaced at `GridHyperspaceSheetRenderer`, `SystemSpaceState.ShipPositionMarker`, `ShipMeshRenderer._debugLineEffect`, `ObjectDesignerGame._lineEffect`. `SystemSpaceState.cs`'s genuinely-different lit/no-vertex-colour preset left alone, as the inventory itself flagged. | Done. |
| §2 `MeshRenderer` per-draw-call parameter blocks | New private `SetCoreParameters` helper for the 9-parameter block (`World`/`View`/`Projection`/`SunDirection`/`SunColour`/`Ambient`/`MaterialColor`/`Texture`/`VertexIlluminationScale`) shared by `DrawDynamicLit`/`DrawDynamicLitRange`/`DrawDynamicLitShadowed`/`DrawDynamicLitShadowedRange`. `ModuleToStationLocal` (set differently per variant) and the `BakedColorLit*` techniques (a genuinely different parameter set) were left alone. | Done. |
| §2 Sun/ambient lighting factor — `CelestialBodyRenderer.BuildPlanetSphere` reimplemented `SceneLighting.LightFactor` inline | Replaced the inline `MathF.Max(Vector3.Dot(normal, sunDir), ambient)` with a call to `SceneLighting.LightFactor(normal)`; removed the now-unused local `sunDir`/`ambient` variables. | Done. |
| §2 `DVec3` → `Vector3` narrowing — 3 independent implementations | `SemanticHullMeshBuilder`'s private duplicate `ToVector3(DVec3)` removed; all 7 call sites now call `DVec3.ToVector3()` directly. `EngineMeshBuilder.ToVector3(value, mirroredAcrossHullX)` kept (public API, 6 external call sites across `ShipMeshRenderer` and a test) but now internally delegates to `DVec3.ToVector3()` and applies the mirror sign-flip as an explicit separate step, per the inventory's own suggested fix, instead of fusing both into one set of casts. `CockpitMeshBuilder` was already on the canonical extension method — untouched. | Done. |
| §2 Basis/frame construction from a normal | Turned out much bigger than this inventory originally scoped it: not 6 implementations but **~20**, once `Inferior.Game`'s full text was actually searched (the inventory's own §9 flagged this gap — `Inferior.Game` wasn't read exhaustively the first time) — see the inventory's own new prose note below for the full accounting and why only the "arbitrary reference axis" one-liner was consolidated, not the full basis construction. New `Inferior.Rendering/ArbitraryReferenceAxis.For(direction, threshold)`; 17 production call sites now call it, each keeping its **own existing threshold** (0.85 / 0.9 / 0.99, unchanged) and its own downstream cross-product order/handedness (deliberately not touched — see below). 3 independent copies in test files left alone (verification code, not production). | Done, narrower in scope than the inventory's original framing but by design, not by half-measure — see below. Behaviour-preserving: Fast suite 709/709 with **zero** pinned-fixture changes needed (unlike the hash-consolidation work), confirming the refactor changed no generated output. |

**Not requested, not touched:** everything else in §1/§2 (the higher-level basis-construction
shape at each of those ~20 sites, the other four winding postures, the text-mirroring
root-cause candidate — moot for containers specifically now that container text was removed,
but the same `PlanarTextGeometry` call-site pattern is still live at other text placements)
remains exactly as originally found. UV-sphere duplication (`MeshFactory.CreateSphere` vs
`CelestialBodyRenderer.BuildPlanetSphere`) was considered and explicitly deferred — Timo
doesn't want to touch planet sphere generation now, since planets are getting an overhaul
eventually and this would just be code to throw away or rebase. See §8 for the updated
canonical-paths table.

**Why only the one-liner, not full basis unification (2026-09-12):** reading every one of
the ~20 sites (`git grep` for the exact `MathF.Abs(x.Y) < threshold ? UnitY : UnitX` shape,
not just the inventory's original small sample) showed the "arbitrary reference axis" pick
really is the same operation everywhere — but what each site builds from it is **not**. Two
concrete examples of a real, not cosmetic, difference: `StationModuleMesh.AddQuad`/
`AddTriangle`/etc. compute `uAxis = Cross(normal, arb)` then `vAxis = Cross(normal, uAxis)`
(both crosses take the normal first), while `StationModuleMesh.AddPrismPipe` computes
`right = Cross(dir, arb)` then `up = Cross(right, dir)` (the second cross takes `dir`
*second*) — working through the sign algebra, that's not the same basis with different
names, `up` comes out the negation of what the first pattern's `vAxis` would be for the same
inputs. Forcing every site through one shared 2-axis-basis function would have silently
flipped handedness/orientation at some of them — geometry risk with no actual duplication
benefit, since the real (safe, meaning-preserving) duplication was only ever the one-line
reference-axis pick. The three different threshold constants (0.85/0.9/0.99) were
deliberately **not** unified onto one value either, for the same reason stated when this
cluster was first scoped: a different threshold can change which axis gets picked for a
near-vertical input, which is a visual/behavioural decision belonging to Timo, not something
to fold in silently while removing duplication. Still open, if Timo wants it: picking one
canonical threshold, and/or introducing a validated 2-axis `Frame.FromNormal`-style
primitive (the inventory's original suggestion) as the model for *new* code, without forcing
every existing site to migrate onto it.

**Incidental consequence of the hash change:** because `NameHash` now produces different
seeds, every megastation's generated layout reshuffled. Working through the resulting
Slow-suite regressions surfaced and fixed three unrelated pre-existing bugs downstream
(none part of this inventory's original findings, recorded here only for traceability):
a missing `structuralSolids` argument to `MegastationArtificialOcclusion.Build` in three
tests (a real determinism gap between test and production occlusion), an incomplete
`GlowLights`/caster-vertex accounting that never counted Mega Shelf structural/truss
geometry in a few tests' independent-formula checks, and the landing-site generator not
guaranteeing Timo's stated "≥3 total sites per landing bay" floor (now fixed in
`MegastationLandingDistrict.cs`). One further pre-existing bug was found but deliberately
**not** fixed this pass — `MegastationMegaShelfTests.LargeBayCanAcceptMoreThanTwoBayWideShelvesWithoutOverlap`:
a `WallIntegrated` shelf landing site's envelope can sit up to `SiteSeparation/2` (14
units) outside its shelf surface's usable bounds, because that candidate frame is
expressed in the host wall's local axes while `surface.Usable` is expressed in canonical
station axes. A first attempt at fixing it (tightening the containment check) regressed
six other tests and was reverted; the real fix needs the `WallIntegrated` branch reworked
to reserve its separation margin in the wall's own local frame. Documented in code at
`MegastationLandingDistrict.cs` (`TryAddCandidate`) as a scoped follow-up, not queued here.

---

## 1. Summary table

| Cluster | Implementations | Severity | Consolidation risk | Canonical candidate |
|---|---|---|---|---|
| Text on 3D surfaces | 1 primitive (`PlanarTextGeometry`) + ~10 call sites that each choose their own `readingDirection`/`surfaceNormal` | Divergent behaviour (in caller inputs, not the primitive) | Low (primitive already consolidated) | `PlanarTextGeometry.Add`/`DeriveFrame` — already canonical |
| Container mesh generation | 1 (`ShippingContainerFactory.GenerateVertices`) | — (already consolidated) | — | Already canonical |
| Basis/frame construction from a normal | ~~6 independent implementations~~ **Corrected 2026-09-12:** actually ~20 once fully searched — see §0. **Partially fixed:** the shared "arbitrary reference axis" one-liner they all had in common is now `ArbitraryReferenceAxis.For`; the higher-level basis each site builds from it (which genuinely differs — see §0/§2) is untouched. | Divergent behaviour (different threshold constants, different validation) | Medium | `PlanarTextGeometry.DeriveFrame`'s pattern (explicit input contract + throw-on-reflection) as the model for *new* code; **not** a retrofit target for the ~20 existing sites, which build genuinely different basis shapes on top of the now-shared reference-axis pick |
| Triangle winding: trust vs. correct vs. validate | 5 independent postures across otherwise-similar "build mesh from triangles/quad" code. **Updated 2026-09-11:** `StationModuleMesh.AddQuad` moved from "trust" to a 6th posture, "validate via `Debug.Assert`, dev-builds-only" — still not unified with the other five. | Divergent behaviour | Medium-high (touches hot generation paths) | `ChamferedBox.WindFace` (auto-correct + comment explaining why) as the model |
| UV sphere tessellation | 2 (`MeshFactory.CreateSphere`, `CelestialBodyRenderer.BuildPlanetSphere`) | Pure duplication (same ring/segment math, different vertex format) | Low | `MeshFactory.CreateSphere`'s loop, parameterised on vertex-build delegate |
| CPU-mesh → GPU-buffer upload | ~~3 near-identical~~ **Fixed 2026-09-11:** the allocate/`SetData` step now shared via `GpuBufferFactory.Create` | Pure duplication | Low | Shared generic uploader — **now `GpuBufferFactory`** |
| `BasicEffect` unlit/vertex-colour preset | ~~4 independent constructions~~ **Fixed 2026-09-11:** all 4 now call `BasicEffectPresets.UnlitVertexColour(gd)` | Cosmetic duplication | Low | A `BasicEffectPresets.UnlitVertexColour(gd)` factory — **now exists** |
| `MeshRenderer` per-draw-call parameter blocks | ~~Repeated ~8-line block across 6 `Draw*` overloads~~ **Fixed 2026-09-11:** the 4 `DynamicLit*` overloads' shared 9-parameter block now factored into `SetCoreParameters`; `BakedColorLit*` (a genuinely different parameter set) left alone | Cosmetic duplication | Low | Private `SetCoreParameters` helper — **now exists** |
| Deterministic string/seed hashing | ~~2 independent hash functions + 1 `GetHashCode()`-chain~~ **Fixed 2026-09-11:** `NameHash` now delegates to `SeededRandom`; `StationTextureRegistry.HashPalette` now mixes fields through `SeededRandom.Derive` instead of `GetHashCode()`. | Divergent behaviour (different hash values for the same string) | Medium (touches save-compatible seeds) | `SeededRandom.StableStringHash`/`.Derive(string)` — **now the only station/texture-generation hash path** |
| `DVec3` → `Vector3` narrowing (no scale) | ~~3+ independent one-liners~~ **Fixed 2026-09-11:** `SemanticHullMeshBuilder`'s duplicate removed (calls `DVec3.ToVector3()` directly); `EngineMeshBuilder.ToVector3` kept as public API but now delegates to `DVec3.ToVector3()` with the mirror flip as a separate step | Cosmetic duplication | Low | A single extension method, used everywhere — **now the case, mirroring kept as an explicit add-on** |
| UI text measurement | ~~`FontHelper.Measure` (sanitised) vs. 8+ raw `font.MeasureString()` call sites~~ **Fixed 2026-09-11:** all 8+ sites now go through `FontHelper.Measure` | Divergent behaviour (unsanitised path can throw/blank on unsupported glyphs) | Low-medium | `FontHelper.Measure`/`.Draw` |
| Sun/ambient lighting factor | ~~2 (`SceneLighting.LightFactor`, inline copy in `CelestialBodyRenderer.BuildPlanetSphere`)~~ **Fixed 2026-09-11:** `BuildPlanetSphere` now calls `SceneLighting.LightFactor` | Pure duplication (identical formula) | Low | `SceneLighting.LightFactor` |
| Debug-only pixel text | 2 (`ShipMeshRenderer`'s baked 5×3 line-glyph debug labels, `BitmapFonts`/`PlanarTextGeometry`'s real font) | Cosmetic (debug-only, not player-visible) | Low, and arguably not worth merging | N/A — different purpose (line debug overlay vs. real mesh text) |

---

## 2. Per-cluster sections

### Text on 3D surfaces (required cluster)

**Primitive:** `Inferior.Game/Station/PlanarTextGeometry.cs:18` (`Add`) and `:67` (`DeriveFrame`).
This is the sole low-level bitmap-font-to-mesh authority (current-state.md's own claim
verified against the code — grep found no other `AddTextGeometry`-style implementation left
anywhere). `DeriveFrame` takes a caller-supplied `surfaceNormal` + `readingDirection`,
projects `readingDirection` onto the surface plane for `right`, derives `up =
Cross(normal, right)`, and **throws `InvalidOperationException`** if the resulting frame is
reflected (`Dot(Cross(right,up), normal) <= 0`). This makes the primitive itself
provably non-reflective — it cannot silently emit mirrored glyph geometry.

**Where mirroring can still arise:** not in the primitive, in the ~10 call sites that each
pick their own `readingDirection`/`surfaceNormal` by hand. The clearest example is
`ShippingContainerFactory.AddContainerText` (`Containers/ShippingContainerFactory.cs:551-559`):
the Y+ face uses `surfaceNormal=+Y, readingDirection=+X`; the Y- face (viewed from
underneath) uses `surfaceNormal=-Y, readingDirection=-X` — a **manually reasoned sign flip**,
justified only by a one-line comment ("opposite reading direction so the label reads
correctly from below"). Working through `DeriveFrame`'s math for both cases: `up` comes out
as `-Z` for *both* faces, and `right` flips between `+X`/`-X`. Whether that's the correct
compensation depends on which axis the viewer is actually looking along when reading the
underside label — geometry I did not render, so I can't independently confirm it's right,
only that it's exactly the kind of by-hand, per-call-site sign reasoning that's easy to get
backwards, and the most plausible root of "text keeps coming back mirrored" if it recurs:
the primitive can't protect against a caller who *consistently* derives the wrong
`readingDirection` for a given face, since that still produces a valid (non-reflected) frame
by the primitive's own check — just one that reads backwards to a human. Other call sites
(`StationDecorator.Tanks.cs:83,90`; `MegastationLandingDistrict.cs` ×4) pass an
already-established `right`/`forward`/`up` triple through from one shared local frame per
placement, rather than reasoning about two symmetric faces independently — lower risk by
construction, since there's only one sign decision instead of two that must agree.

**Also relevant:** `PlanarTextGeometry.Add` calls `StationModuleMesh.AddQuad` per glyph
pixel — and `AddQuad` computes its own normal via `Cross(edge0,edge1)` with **no
correction or validation against the caller's intent** (see the winding-posture cluster
below). Because `DeriveFrame` already guarantees a proper-handed `(right, up, normal)`
triple, `AddQuad`'s lack of a safety net is not currently a hazard for text specifically —
but it means the guarantee lives entirely in `DeriveFrame`, once, and nothing downstream
double-checks it.

### Container mesh generation (required cluster) — already consolidated

`Inferior.Game/Containers/ShippingContainerFactory.GenerateVertices` is the only container
geometry generator; `grep`-ing every call site (`StationDecorator.Containers.cs:237`,
`MegastationLandingDistrict.cs:2896`, `SystemSpaceState.Containers.cs`,
`MegastationLandingDistrictTests.cs`) confirms all of them call it, none reimplement it.
`StationDecorator.Containers.cs`'s own `BuildContainerMesh` (added by the mega-station-merge
side, kept during the master↔mega-stations conflict resolution) is a thin wrapper that
builds the placement transform and calls `GenerateVertices` — not a second geometry path.
No further action needed here; recorded per the brief's explicit "confirm it's already
consolidated" instruction.

### Basis/frame construction from a normal

At least 6 independent "build an orthonormal frame given one direction" implementations,
each with its own threshold/validation choice:

1. `PlanarTextGeometry.DeriveFrame` — input is `(normal, readingDirection)`, both
   caller-supplied; throws if reflected. Most defensive.
2. `StationModuleMesh.AddQuad` (`Station/StationModuleMesh.cs:170`) — arbitrary reference
   axis (`|normal.Y|<0.85 ? UnitY : UnitX`), no validation.
3. `StationDecorator.Tanks.cs`'s `GetPrismRings` — same `0.85` threshold, independently
   written (not shared with #2).
4. `SkyboxRenderer.Build` (`Inferior.Rendering/SkyboxRenderer.cs:94`) — same idea, threshold
   `0.99` instead of `0.85`.
5. `SemanticHullMeshBuilder.BuildUvBasis` — tangent from the **first non-degenerate
   authored polygon edge**, not an arbitrary world axis; `bitangent = Cross(normal, tangent)`.
6. `AddQuadProjected` (`StationModuleMesh.cs:210`) — takes `canonicalU`/`canonicalV`
   directly from the caller (no derivation at all), but does validate/flip the *normal*
   against an `expectedNormal`.

None of these are provably wrong in isolation, and the two different threshold constants
(0.85 vs 0.99) only change behaviour within a narrow band of near-vertical normals — but six
textually-independent implementations of the same idea is exactly the drift risk the brief
is asking about: a future fix to one (like the mirrored-text fix already applied to
`PlanarTextGeometry`) has no mechanism to propagate to the other five.

**Update (2026-09-12):** The "6 implementations" count above was itself stale/incomplete —
this pass's own §9 had already flagged that `Inferior.Game` wasn't searched exhaustively for
the first draft, and this cluster is exactly where that gap bit. Grepping the actual shape
(`MathF.Abs(x.Y) < threshold ? UnitY : UnitX`) across the whole solution found **~20**
production call sites, not 6 — #3 above (`StationDecorator.Tanks.cs`'s `GetPrismRings`) no
longer exists by that name (likely moved/renamed during the mega-stations merge); the real
current equivalent is `StationIndustrialPrimitives.PrismRings`. The other ~17 sites span
`StationModuleMesh.cs` (6 of its own: `AddQuad`, `AddOrientedBox`, `AddPrismPipe`,
`AddTriangle`, `AddTriangleGradient`, `AddQuadGradient`), `StationYagiAntenna.cs` (2),
`StationDecorator.Antennas.cs` (2), `StructuralTrussFactory.cs` (1, a degenerate-input
fallback path), `MegastationLandingDistrict.cs` (1), and the Bolon files
`BolonMegastations.cs` (2) / `BolonMegastationSurfaces.cs` (1) — plus `SkyboxRenderer.cs`
(#4 above). Three thresholds are in active use, not two: 0.85, **0.9**, and 0.99.

Consolidated the genuinely-identical part only: the one-line "pick UnitY unless the
direction is too close to vertical, then UnitX" switch is now
`Inferior.Rendering/ArbitraryReferenceAxis.For(direction, threshold)`, called from all 17
production sites (3 independent copies in test files — verification code computing an
expected value independently of production — deliberately left alone). What each site
builds from that reference axis is **not** the same operation everywhere and was
deliberately left untouched: e.g. `AddQuad`'s `vAxis = Cross(normal, uAxis)` vs.
`AddPrismPipe`'s `up = Cross(right, dir)` differ in which operand comes first, and are not
interchangeable — forcing them through one shared 2-axis-basis function would have silently
flipped orientation/handedness at some call sites. Each site also kept its own existing
threshold (0.85/0.9/0.99) rather than being silently retuned onto one "canonical" value,
since that changes which axis gets picked for a near-vertical input — a visual/behavioural
decision, not pure refactoring. See §0 for the full rationale and what's still open.

### Triangle winding: trust vs. correct vs. validate

Five different postures for "does this vertex order actually wind outward," on
otherwise-comparable "build a mesh from a list of triangles/quads" code:

| Implementation | Posture |
|---|---|
| `GeometryBuilder.AddTriangle` | Computes, compares to expected (centroid or explicit), **auto-flips** silently |
| `ChamferedBox.WindFace` | Same auto-flip pattern, explicitly documented as replacing old hand-derived sign branching |
| `SemanticHullMeshBuilder.Build` | Computes, compares to author-declared `OutwardNormal`, **throws** if wrong or near-zero-area |
| `EngineMeshBuilder.Build` | No correction; **conditionally swaps** `(b,c)` only when `mirroredAcrossHullX` is true |
| `CockpitMeshBuilder.Build` | No correction, only a degenerate-triangle throw; **trusts** input winding entirely |
| `StationModuleMesh.AddQuad` | ~~No correction, no degenerate check; **trusts** input winding entirely~~ **Updated 2026-09-11:** now checks finite vertices, both triangles for near-zero area, and winding consistency between the two triangles via `Debug.Assert` (dev-builds-only, compiles away in Release) — still does not auto-correct like `AddQuadProjected` |
| `StationModuleMesh.AddQuadProjected` | Computes, compares to `expectedNormal`, **auto-flips** — different posture from its sibling `AddQuad` in the *same class* |

The `AddQuad`/`AddQuadProjected` split is worth calling out specifically: two methods on the
same type, doing conceptually the same job, with different safety postures. `AddQuad` is by
far the more heavily used of the two (base geometry for the large majority of station
decoration, including every glyph quad `PlanarTextGeometry` emits).

**Update (2026-09-11):** Per Timo, `AddQuad` now validates rather than trusting — the
"gates" were needed for development and do pop up from time to time. This narrows the
`AddQuad`/`AddQuadProjected` gap (both now reject bad input instead of one silently
accepting it) but doesn't close it (one throws, the other self-corrects) — that's still an
open decision for §8. Verified against the full Fast test suite (851 tests at the time) that
no existing production call site currently trips the new checks.

**Follow-up update (2026-09-11):** the checks are now gated behind `Debug.Assert` instead of
unconditional `throw`, closing the "put these behind a debug flag later" intent from the
original request — matches the same convention already used throughout
`MegastationInteriors.cs` and elsewhere (active in Debug builds, compiled away entirely when
`DEBUG` isn't defined, per `[Conditional("DEBUG")]` on `Debug.Assert` itself). Real
consequence: a Release build no longer enforces these checks at all — bad geometry
(NaN vertices, a degenerate or twisted quad) now propagates through silently in Release,
where before this follow-up it would have thrown there too. That's the deliberate trade-off
Timo described (a development-time diagnostic, not a production input-validation contract).

### UV sphere tessellation

`Inferior.Rendering/MeshFactory.cs:20` (`CreateSphere`) and
`Inferior.Rendering/CelestialBodyRenderer.cs:723` (`BuildPlanetSphere`) both implement the
identical rings/segments polar sweep (`phi = π·ring/rings`, `theta = 2π·seg/segments`,
same `a,b,c,d` quad→2-triangle index pattern) independently. They differ only in vertex
format and in what's baked into the vertex colour (`CreateSphere` leaves colour for the
caller; `BuildPlanetSphere` bakes a checkerboard + lit colour per vertex). `CreateSphere` is
used once (the star's sphere, via `CelestialBodyRenderer`'s own constructor); the file that
calls it also independently reimplements the same tessellation loop for planets 700 lines
later. Low risk to consolidate: extract the ring/segment loop with a per-vertex delegate.

### CPU-mesh → GPU-buffer upload

`SemanticHullGpuMesh.Create`, `CockpitGpuMesh.Create`, `EngineGpuMesh.Create`
(`Inferior.Rendering/*.cs`) are three ~40-line near-duplicates: iterate CPU parts, map to a
GPU vertex struct, allocate `VertexBuffer`/`IndexBuffer`, `SetData`, skip empty parts. Only
the vertex-mapping lambda and the source/result record types differ. Straightforward to
collapse into one generic helper parameterised on the CPU vertex → GPU vertex projection.

**Update (2026-09-11):** Fixed the buffer-allocation half of the duplication specifically —
new `GpuBufferFactory.Create(graphicsDevice, vertices, indices)` does just the
`VertexBuffer`/`IndexBuffer` allocate-and-`SetData` step, called from all three `Create`
methods. Left each type's own per-part vertex mapping and result-record shape alone (they
genuinely differ: `SemanticHullGpuMesh` carries `RenderGroup`/`MaterialGroup`/
`MaterialColour`/`FaceRanges` and skips empty parts; `Cockpit`/`EngineGpuMesh` carry
`PartId`/`Material` and don't skip). A single fully-generic uploader parameterised on the
CPU→GPU vertex projection (the inventory's original "canonical candidate") was judged not
worth building for three call sites with this much per-type variation — the allocate/SetData
step was the actual duplication, and it's now shared.

### `BasicEffect` unlit/vertex-colour preset

Four independent `new BasicEffect(gd) { VertexColorEnabled = true, LightingEnabled = false,
TextureEnabled = false }` constructions, byte-for-byte identical property sets:
`Inferior.Game/Hyperspace/GridHyperspaceSheetRenderer.cs:30`,
`Inferior.Game/States/SystemSpaceState.ShipPositionMarker.cs:24`,
`Inferior.Rendering/ShipMeshRenderer.cs:66` (`_debugLineEffect`), and
`Inferior.ObjectDesigner/ObjectDesignerGame.cs:110` (`_lineEffect`). A fifth,
`SystemSpaceState.cs:379`, constructs the *different* lit/no-vertex-colour preset used for
celestial bodies — not a duplicate of the above, a genuinely different configuration. A
`BasicEffectPresets` static factory in `Inferior.Rendering` (used across `Game` and
`ObjectDesigner`) would remove the first four.

**Update (2026-09-11):** Fixed exactly as proposed — `BasicEffectPresets.UnlitVertexColour
(gd)` now exists in `Inferior.Rendering` and all four sites call it. `SystemSpaceState.cs`'s
distinct lit preset was left untouched, as flagged above.

### `MeshRenderer` per-draw-call parameter blocks

Not a bug, just repetition worth flagging per the brief's own "repeated
parameter-setting code" prompt: `DrawDynamicLit`/`DrawDynamicLitRange` and
`DrawDynamicLitShadowed`/`DrawDynamicLitShadowedRange` each restate the same ~8-line
`World`/`View`/`Projection`/`SunDirection`/`SunColour`/`Ambient`/`MaterialColor`/`Texture`/
`VertexIlluminationScale`/`ModuleToStationLocal` block (`MeshRenderer.cs:90-101`,
`123-134`, `178-188`, `221-233`). Shadow parameters and specular parameters are *already*
factored into `SetShadowParameters`/`SetSpecularParameters`; the core block never was.
Cosmetic, but four copies of the same nine `SetValue` calls is real drift surface (a tenth
parameter added to one and not the others would be easy to miss).

**Update (2026-09-11):** Factored the shared 9-parameter block (`World`/`View`/`Projection`/
`SunDirection`/`SunColour`/`Ambient`/`MaterialColor`/`Texture`/`VertexIlluminationScale`) out
of the 4 `DynamicLit*` overloads into a private `SetCoreParameters` helper, matching the
existing `SetShadowParameters`/`SetSpecularParameters` pattern. `ModuleToStationLocal` stayed
out of it deliberately — the non-shadowed variants default it to `Identity`, the shadowed
variants already set it inside `SetShadowParameters`, so it's not actually common to all
four. The separate `BakedColorLit*` techniques (`DecorationBrightness` instead of
`MaterialColor`/`VertexIlluminationScale`) were left alone — a genuinely different parameter
set, not the block this finding was about.

### Deterministic string/seed hashing

`Inferior.Core/Random/SeededRandom.cs:56` (`StableStringHash`, djb2-xor:
`h=5381; h=((h<<5)+h)^c`) and `Inferior.Game/Station/StationGenerator.cs:1468` (`NameHash`,
Java-style polynomial: `h=17; h=h*31+c`) are two textually-independent deterministic string
hashes. Both avoid `string.GetHashCode()`/`HashCode.Combine` (correctly, per
`!invariants.md`), so neither is *non-deterministic* — but they are **not interchangeable**:
`NameHash("X") != StableStringHash("X")` for the same input. `StationGenerator` derives its
entire station-generation seed from `NameHash`, independently of the `SeededRandom.Derive
(string)` convention the rest of procedural generation (galaxy, systems) uses. Not
necessarily a bug — `NameHash`'s own doc comment shows the author was specifically alert to
seed-derivation drift (it exists as `internal` precisely so a regression test can reconstruct
the same value rather than re-deriving it) — but the two hash *functions themselves* were
never unified.

**Update (2026-09-11):** Per Timo's explicit authorization, this was consolidated — removing
the mega-station-side hash. `StationGenerator.NameHash` now reads:

```csharp
private const int StationSeedRoot = 0x53544154; // "STAT"

internal static int NameHash(string name)
    => new SeededRandom(StationSeedRoot).Derive(name).Seed;
```

`StationSeedRoot` gives station generation its own namespace under `SeededRandom`'s
`Derive(string)` convention rather than colliding with any other subsystem's derivations.
All 6 call sites (`StationGenerator.cs`) were left unchanged — they still call `NameHash`,
now indirectly through `SeededRandom`.

This deliberately changes the seed every existing station generates from. Per
`!invariants.md`, procedural station layouts are regenerated baselines, not persisted state
— this is expected to reshuffle the galaxy's generated station layouts, not a regression.
Several tests had pinned fixture values (texture-binding counts, upload byte totals,
generated-signature strings) that assumed the old seed; those were refreshed to match the
new deterministic output, not loosened.

Separately, `StationTextureRegistry.HashPalette` (`Station/StationTextureRegistry.cs:690`)
builds a hash via a chain of `h = h*31 + value.GetHashCode()` over a `TexturePalette`'s
colour/float fields, then XORs it into a station's seed for `new System.Random(seed ^
HashPalette(...))` (`StationTextureRegistry.cs:317`). `float`/`Color.PackedValue.GetHashCode
()` are not process-randomized in current .NET (unlike `string.GetHashCode()`), so this
isn't the specific class of bug `!invariants.md` names — but it's still a `GetHashCode()`
chain feeding a generation seed, which is fragile in spirit even if not proven broken today:
nothing guarantees a future runtime keeps `float.GetHashCode()`'s bit pattern stable, and the
project's own stated policy is "must not use ... runtime object hashes." Flagged for Timo's
judgment rather than treated as settled either way — I can't tell from reading alone whether
this has ever caused an observable problem.

**Update (2026-09-11):** Closed, as a follow-up to the `NameHash` consolidation above.
`HashPalette` now reads:

```csharp
private const int PaletteSeedRoot = 0x50414C54; // "PALT"

private static int HashPalette(TexturePalette p, SurfaceTexture surface)
    => new SeededRandom(PaletteSeedRoot)
        .Derive((int)surface)
        .Derive(unchecked((int)p.BaseColour.PackedValue))
        .Derive(unchecked((int)p.AccentColour.PackedValue))
        .Derive(unchecked((int)p.GrimeColour.PackedValue))
        .Derive(BitConverter.SingleToInt32Bits(p.NoiseStrength))
        .Derive(BitConverter.SingleToInt32Bits(p.SubPanelContrast))
        .Derive(BitConverter.SingleToInt32Bits(p.GrimeStrength))
        .Seed;
```

`Color.PackedValue` is already a plain `uint` — no hashing needed, just a cast. Each `float`
goes through `BitConverter.SingleToInt32Bits`, a spec-guaranteed IEEE-754 bit
reinterpretation (not an implementation-defined hash like `float.GetHashCode()`), and the
whole thing is mixed through `SeededRandom`'s own `Derive` chain (`MixSeeds`/
boost::hash_combine) rather than a hand-rolled `h*31+` accumulator. This reshuffles the
RNG-driven pixel noise `GeneratePixels` produces for every station's generated textures
(the mixed value feeds `new System.Random(seed ^ HashPalette(...))`) — a separate, further
pinned fixture (`SystemMaterialLibraryTests.OrdinaryStationTextureFixtureRemainsByteIdentical`,
a SHA-256 over exact pixel bytes) needed refreshing on top of the ones already refreshed for
the `NameHash` change.

### `DVec3` → `Vector3` narrowing (no scale)

Distinct from `Camera3D.ToRenderSpace` (which applies `RenderScale` *and* subtracts camera
position — a different operation entirely, and the one everyone correctly uses for universe
coordinates). This smaller cluster is pure unit-preserving narrowing, used for already-local
mesh-space geometry: `SemanticHullMeshBuilder.ToVector3` (private static),
`EngineMeshBuilder.ToVector3` (public static, also handles the `mirroredAcrossHullX` sign
flip), and a `DVec3.ToVector3()` extension method used by `CockpitMeshBuilder` and
`ShipMeshRenderer.BuildEngineModuleDebugLines`. Functionally harmless (three ways to write
`(float)v.X, (float)v.Y, (float)v.Z`) but worth collapsing onto the extension method alone —
`EngineMeshBuilder.ToVector3`'s mirroring behaviour is a different *concern* bolted onto the
same conversion and would read more clearly split apart.

**Update (2026-09-11):** `SemanticHullMeshBuilder`'s private `ToVector3(DVec3)` removed
entirely; its 7 call sites now call `value.ToVector3()` directly. `EngineMeshBuilder.ToVector3
(value, mirroredAcrossHullX)` was **not** removed — it's public and has 6 external call sites
(`ShipMeshRenderer`, `AriesCoordinateConventionTests`), so folding it away would be an API
change beyond this cleanup's scope. Instead, per the inventory's own suggested fix, its body
now calls `value.ToVector3()` and applies the mirror sign-flip as its own explicit step,
rather than fusing both into one set of `(float)` casts. `CockpitMeshBuilder` was already
calling the canonical extension method directly — untouched.

### UI text measurement

`Inferior.UI/FontHelper.cs` exists specifically to wrap `SpriteFont.MeasureString`/
`SpriteBatch.DrawString` with character sanitisation (`FontHelper.Sanitize`) — its own doc
comment says "use these instead of font.MeasureString / sb.DrawString directly." Grep for
`MeasureString` shows it is *not* consistently followed: `Controls/TextBlock.cs` (2 sites),
`Controls/TextBox.cs` (7 sites), `Controls/SystemConsole.cs` (3 sites),
`Controls/LedIndicator.cs` (2 sites), and `UIRenderer.MeasureText` (its own, fourth
measurement path, `UIRenderer.cs:108`) all call `font.MeasureString` directly, unsanitised.
`UIRenderer.DrawText`/`DrawTextWithBackground` (the two call sites right above
`MeasureText`) *do* call `FontHelper` internally — so the convention is followed for drawing
but not consistently for measuring. Medium severity: an unsanitised `MeasureString` call on
text containing a glyph outside the theme font's character set can throw or silently
mis-measure, depending on the MonoGame/font-backend behaviour — I did not chase this to a
concrete repro, so recording it as a real, not yet confirmed, risk rather than a bug.

**Update (2026-09-11):** Fixed, per Timo ("very much needed, and should not be bypassed").
All identified bypasses now route through `FontHelper.Measure`: `TextBox.MeasureWidth`
(called repeatedly on arbitrary player-typed substrings for cursor placement, selection
width, and click-to-position — the most likely actual source of the "lots of exceptions"
Timo mentioned, since it's the one path exercised on unsanitised live input rather than
fixed UI strings) plus 3 `"A"`-glyph line-height sites in the same file; `TextBlock.cs` (2
sites); `SystemConsole.cs` (1 site); `LedIndicator.cs` (2 sites); `UIRenderer.cs` (2 sites in
`DrawTextCentred`/`DrawTextLeft` — these already received an already-sanitised `safe`
string, so lower risk, but now consistent with the rest). A solution-wide grep for
`.MeasureString(` outside `FontHelper.cs` and test files now returns nothing.

### Sun/ambient lighting factor

`Inferior.Rendering/SceneLighting.cs:27` (`LightFactor(normal) => Max(Dot(normal,
SunDirection), Ambient)`) is reimplemented inline, formula-for-formula identical, inside
`CelestialBodyRenderer.BuildPlanetSphere` (`CelestialBodyRenderer.cs:757`:
`lightFactor = MathF.Max(Vector3.Dot(normal, sunDir), ambient)`) rather than calling the
shared helper that already exists in the same assembly. Trivial, low-risk fix (call the
existing method) but it's the kind of "two copies of a formula silently drift apart" case
`CelestialBodyRenderer`'s *own* comments elsewhere (on `ProjScale`) explicitly warn about
having happened before (Brief D-SunSize's tan(60°) vs tan(30°) bug).

**Update (2026-09-11):** Fixed exactly as proposed — `BuildPlanetSphere` now calls
`SceneLighting.LightFactor(normal)`; the local `sunDir`/`ambient` variables it used to read
`SceneLighting.SunDirection`/`Ambient` into (purely to avoid repeated property access across
~8,300 vertices) were removed as unused.

---

## 3. Convention audit

Spatial paths actually read, with their handedness/winding/UV/units choices as found:

| Path | Handedness / winding | UV origin & orientation | Units / precision |
|---|---|---|---|
| `Camera3D.ToRenderSpace` | N/A (position only) | N/A | `double` universe metres → `float` render units via `RenderScale=1e-9` |
| `GeometryBuilder.BuildDynamic`/`BuildBaked` | CCW-from-outside computed, indices emitted `(0,2,1)` for CW-front under `CullCounterClockwise` | none (UV = `Vector2.Zero`) | `float`, local mesh space |
| `ChamferedBox.Build` | Same CW-front convention, auto-corrected per face | none | `float`, local box space |
| `SemanticHullMeshBuilder.Build` | Authored CCW-from-outside required; emitted CW (`baseVertex, +2, +1`) | tangent = first non-degenerate authored edge; 1 UV unit = `MetresPerUvUnit` = **2 m** | `DVec3` authoring positions narrowed to `float` per vertex |
| `StationModuleMesh.AddQuad` | ~~Trusts caller winding~~ **Updated 2026-09-11:** validates finite vertices, both triangles for degeneracy, and winding consistency — throws rather than trusting (still no auto-flip). CW-front emission (`b,b+2,b+1, b,b+3,b+2`) | tangent = arbitrary-axis-derived (`0.85` threshold); 1 UV unit = `CurrentUvScaleMeters` (caller-set, station decoration typically **5 m** per earlier session notes — not re-verified numerically this pass) | `float` |
| `MeshFactory.CreateBox` | Hand-authored per-face CW-front, no shared helper | manual per-face `(0,1)(1,1)(1,0)(0,0)` | `float`; **dead code**, see §4 |
| `PlanarTextGeometry.DeriveFrame` | Explicit proper-handed guarantee, throws if reflected | N/A (delegates to `AddQuad` per glyph pixel) | `float` |
| `EngineMeshBuilder.Build` | Conditional swap on `mirroredAcrossHullX` | none | `DVec3` authoring → `float`, mirrored sign flip on X |
| `CelestialBodyRenderer` (star/planet spheres) | `MeshFactory.CreateSphere`'s CCW/CW convention | none (colour-only) | `float`; `RenderScale`-derived radii |
| `SkyboxRenderer.Build` | Explicit CW quad list per glow billboard | none | `float`; positions on a fixed `SkyboxRadius` shell |

Two units/scale points worth a second look, not asserted as bugs: `SemanticHullMeshBuilder`'s
authored-hull UVs use 2 m/unit while ordinary station-module UVs use a separately-configured
`CurrentUvScaleMeters` — different texture systems, so not necessarily wrong, but the two
numbers were never cross-checked against each other in this pass.

---

## 4. Dead code

- `Inferior.Rendering/MeshFactory.cs`: `CreateBox` and `CreateQuad` — zero callers anywhere
  in the solution (checked non-test and test code). `CreateSphere` is the only method of
  this class actually used.
- `Inferior.Rendering/GeometryBuilder.cs`: `BuildBaked` — zero callers; already flagged as
  such in its own XML doc comment and in `architecture-map-ai.md`, so not new information,
  just confirmed still true.

No other dead code was confirmed in the areas read this pass; I did not do a solution-wide
unused-symbol sweep (that needs a tool like a Roslyn analyzer, not manual grep, to be
trustworthy at this scale).

**Update (2026-09-11):** Timo asked whether `MeshFactory.CreateBox`/`CreateQuad` might have
been superseded by something more central — traced via `git log --all --oneline --follow`
to the earliest "Phase 2" commit, which predates station generation entirely. The real,
current box-hull builder is `StationGenerator.PrepareBoxHullMesh`, not `MeshFactory` — these
two methods were never replaced, they're simply dead. No removal was requested or done this
pass; still listed here as confirmed-dead.

---

## 5. Documentation drift

- **`Docs-ai/architecture-map-ai.md`, DataBus row (line 30):** ~~lists "8 named `Bus<T>`
  instances...~~ **Fixed 2026-09-11.** The actual `Inferior.Core/DataBus/DataBus.cs` has 11
  channels (`SystemMessages, ScalarTelemetry, VectorTelemetry, SpectrumTelemetry,
  TelemetryInfo, DeviceInfo, DeviceState, ShipSystemsTopology, Radar, RadarLost, Target`) —
  this was the same staleness already found and fixed in `!current-state.md` during the
  mega-station merge; `architecture-map-ai.md` had not been updated at the same time. Now
  corrected to match.
- **`Docs-ai/architecture-map-ai.md`, `## Inferior.Rendering` section:** ~~lists 10 of the
  assembly's 21 real source files~~ **Fixed 2026-09-11** — all 21 files now listed
  (`ChamferedBox.cs`, `DetailLevel.cs`, `DynamicLitMaterialSettings.cs`,
  `EngineMeshBuilder.cs`/`EngineGpuMesh.cs`, `SemanticHullMeshBuilder.cs`/
  `SemanticHullGpuMesh.cs`, `StationBrightnessTuning.cs`, `SunTuning.cs`,
  `VertexPositionNormalColorTexture.cs` added), plus notes on this inventory's own findings
  (dead `CreateBox`/`CreateQuad`, `CelestialBodyRenderer`'s internal UV-sphere duplication).
- **`Docs-ai/architecture-map-ai.md`** more broadly almost certainly predates the
  mega-station merge entirely: ~~it has no `Station/Megastations/` section at all~~
  **Fixed 2026-09-11** — added from scratch (20→49 files under `Station/Megastations/`
  overall), organized into Structural massing / Zoning-substrate-materials / Flight interior
  (H1) / Landing infrastructure (L series) / Bolon subsections, with entries explicitly
  flagged as name/class-derived locators rather than individually deep-read where that's the
  case. This was "get it in order now," not the doc's own header's "regenerate wholesale"
  bar — Timo has said he'll revisit for a fuller pass later, so treat this as a big
  improvement on the prior near-total absence, not a claim of completeness.
- **`stations-ai.md`/`ship-ai.md`:** grepped for the specific terms this inventory
  surfaced (`AddTextGeometry`, `GetHashCode`, `StableStringHash`, `NameHash`) — neither
  document mentions any of them, so there's no *contradiction*, just an absence: neither doc
  records the two-hash-functions finding above. Not treated as drift (they never claimed
  otherwise), noted as a possible addition once Timo decides on a canonical hash.

---

## 6. Bugs noticed (recorded, not fixed)

- **Text mirroring, root-cause candidate:** see §2's text-on-3D-surfaces section in full —
  the container Y+/Y- face pair's hand-reasoned `readingDirection` sign flip is the most
  concrete candidate for where a mirroring regression could come from, but I could not
  confirm in-engine whether it's currently correct or wrong. This is the item Timo's gate
  specifically asks the inventory to explain, not resolve.
- **`StationModuleMesh.AddQuad` vs `AddQuadProjected` inconsistency** (§2, winding cluster):
  two sibling methods on the same type with different winding-safety postures. Not
  necessarily causing any current visible bug (most callers apparently pass correct
  winding), but is a latent trap: a future caller of `AddQuad` that gets vertex order wrong
  will get a silently backward-facing or inside-out-lit face with no exception, whereas the
  same mistake through `AddQuadProjected` self-corrects. **Partially addressed 2026-09-11:**
  `AddQuad` now throws on bad winding instead of silently accepting it (closing the "no
  exception" half of the trap), but it still doesn't auto-correct like `AddQuadProjected` —
  the postures remain different, just both defensive now instead of one being silent.
- **`UIRenderer.MeasureText`/several controls bypass `FontHelper`'s sanitisation** (§2, UI
  text cluster) — plausible source of a crash or mis-measurement on out-of-glyph-set text,
  not confirmed reproduced. **Fixed 2026-09-11** — see §2's UI text measurement section.

---

## 7. Incidental performance notes

Not a profiling pass — only what was obvious in passing while reading for duplication:

- `CelestialBodyRenderer.BuildPlanetSphere` allocates a full `Rings×Segments` (64×128 =
  8,321 vertices) `VertexPositionColor[]`/`int[]` pair per planet at `OnEnter`/system-change
  time, once per planet, cached in `_planetSpheres` — not a per-frame cost, but worth noting
  next to the file's own already-documented known gap that a mid-session `EnterSystem` leak
  existed and was fixed for exactly this dictionary; the fix is in place, this is just
  flagging the allocation size for whoever eventually profiles system-entry stalls.
- `MeshRenderer`'s `Draw*` methods set 8-12 effect parameters unconditionally on every call,
  even when consecutive draws share the same value (e.g. `SunDirection`/`SunColour`/
  `Ambient` rarely change within one frame's station-decoration pass). MonoGame's `Effect`
  parameter `SetValue` calls are cheap individually; whether the aggregate matters at real
  station module counts is a real profiling question, not answered here.

---

## 8. Canonical-paths seed (DRAFT — for Timo's decision)

| Operation | Proposed canonical function | Known trap to avoid |
|---|---|---|
| Universe position → render-space `Vector3` | `Camera3D.ToRenderSpace` | Never hardcode `1e-9`; always go through `Camera3D.RenderScale`/`ToRenderSpace` so origin-shift and scale can't drift apart. (Checked: no live violations found — every `1e-9` outside `Camera3D.cs` was an unrelated near-zero epsilon, confirmed by reading each site.) |
| Text on a 3D surface | `PlanarTextGeometry.Add`/`DeriveFrame` | The primitive is safe; the caller-chosen `readingDirection`/`surfaceNormal` for each face is not automatically checked against "does this actually read correctly to a viewer" — that's still a per-call-site judgment call. |
| Container geometry | `ShippingContainerFactory.GenerateVertices` | Already the only path; keep it that way — do not let a future megastation-scale container variant reimplement inline. |
| Orthonormal frame from one direction | ~~*(none yet — 6 candidates in §2)*~~ **Partially done 2026-09-12:** the shared reference-axis pick is `ArbitraryReferenceAxis.For(direction, threshold)`, used at all 17 production arbitrary-reference-axis sites (real count ~20, not the original 6 — see §0/§2). The full 2-axis basis is still built independently per site — genuinely different cross-product order/handedness at some of them, not safely unifiable without a design decision. | Pick one input contract (arbitrary-reference-axis vs. authored-tangent vs. reading-direction) per use case; standardise the reference-axis threshold if that variant is kept — still an open decision, deliberately not made silently. `PlanarTextGeometry.DeriveFrame`'s throw-on-reflection discipline remains the model for *new* code in this space, not a retrofit target for the ~20 existing sites. |
| Triangle winding correction | `ChamferedBox.WindFace`'s auto-flip pattern | Decide once whether "trust caller" (`CockpitMeshBuilder`), "validate-and-throw" (`SemanticHullMeshBuilder`), "validate via `Debug.Assert`, dev-only" (`AddQuad` as of 2026-09-11's follow-up), or "auto-correct" (`GeometryBuilder`, `ChamferedBox`, `AddQuadProjected`) is the house style — right now all four exist for no documented reason. `AddQuad`'s gates are now dev-builds-only per Timo's original intent — Release builds don't enforce them at all. |
| UV sphere mesh | `MeshFactory.CreateSphere`, generalised with a per-vertex delegate | `BuildPlanetSphere`'s checkerboard/lighting bake would need to become that delegate. |
| CPU mesh → GPU buffers | ~~A new shared generic uploader~~ **Partially done 2026-09-11:** `GpuBufferFactory.Create` shares the allocate/SetData scaffolding; the per-caller vertex struct/record differences (Semantic/Cockpit/Engine) were judged genuine enough to leave as three separate mapping steps calling the one shared factory, rather than building a fully generic delegate-based uploader for three call sites. | Keep per-caller vertex struct differences (Semantic/Cockpit/Engine) as separate mapping code, not baked into three copies of the allocate/SetData scaffolding — the latter is now shared, the former deliberately isn't. |
| `BasicEffect` unlit/vertex-colour debug preset | ~~New `BasicEffectPresets.UnlitVertexColour(gd)` in `Inferior.Rendering`~~ **Done 2026-09-11** — exists, all 4 sites use it. | Don't fold in `SystemSpaceState.cs`'s *different* lit/no-vertex-colour preset by mistake — that one is genuinely distinct. (Confirmed left alone.) |
| Deterministic string hash / seed derivation | `SeededRandom.StableStringHash` / `.Derive(string)` | ~~`StationGenerator.NameHash` produces different values for the same input... `StationTextureRegistry.HashPalette` was intentionally left untouched~~ **Done 2026-09-11 (both halves)**: `NameHash` delegates to `SeededRandom`; `HashPalette` mixes its fields through `SeededRandom.Derive` instead of `GetHashCode()`. Existing generated station layouts and texture-palette pixel noise both reshuffled as an accepted consequence (procedural baselines are regenerated, not persisted). This cluster is now fully closed. |
| `DVec3` → `Vector3` narrowing (no scale) | The existing `DVec3.ToVector3()` extension method | ~~Keep `EngineMeshBuilder`'s mirroring sign-flip as a separate, explicitly-named step, not fused into the conversion itself.~~ **Done 2026-09-11** — `SemanticHullMeshBuilder`'s duplicate removed; `EngineMeshBuilder.ToVector3` kept (public, external callers) but now delegates to `DVec3.ToVector3()` with the mirror flip separated out. |
| UI text measurement | `FontHelper.Measure` | ~~`UIRenderer.MeasureText` and the raw `font.MeasureString()` call sites in `TextBlock`/`TextBox`/`SystemConsole`/`LedIndicator` all need to move over~~ **Done 2026-09-11** — all of them now go through `FontHelper.Measure`; a solution-wide grep for `.MeasureString(` outside `FontHelper.cs`/tests returns nothing. |
| `MeshRenderer` per-draw-call parameters | New private `SetCoreParameters` helper | ~~Cosmetic, but four copies of the same nine `SetValue` calls is real drift surface~~ **Done 2026-09-11** for the 4 `DynamicLit*` overloads; `BakedColorLit*`'s different parameter set deliberately left separate. |
| Ambient/sun lighting factor | `SceneLighting.LightFactor` | ~~Trivial: replace the one inline copy in `BuildPlanetSphere`.~~ **Done 2026-09-11.** |

---

## 9. What this pass did not cover

Per the brief's own permission to stop rather than skim: this inventory read
`Inferior.Rendering` in full (21/21 files) and traced the two required clusters plus every
lead the targeted cross-cutting greps turned up, but did **not** do a file-by-file read of:

- `Inferior.Game/Station/Megastations/` (~50 files) — the largest single subsystem in the
  codebase and the most likely place to hold *more* instances of the clusters already found
  above (frame-from-normal, winding posture, text placement), given it's newer code built
  fast. Worth a dedicated A1-follow-up pass focused specifically on this directory.
- `Inferior.Gameplay` and `Inferior.Galaxy` beyond the targeted greps (determinism,
  `1e-9`/`RenderScale`, `DataBus`/`CommandBus` boundary) — no file-by-file read of e.g. the
  physics/component/sensor code for its own internal duplication.
- `Inferior.UI` beyond the text-measurement cluster — no read of the layout/hit-testing/
  control-composition code for duplication.
- A systematic constants audit (the brief's "same constant defined in several places") —
  only checked the ones already surfaced by other clusters (UV scale, frame thresholds); did
  not grep for e.g. repeated magic colour/size literals across station decoration.
- A solution-wide dead-code sweep — §4 only lists what fell out of tracing the clusters
  above, not an exhaustive unused-symbol pass.
