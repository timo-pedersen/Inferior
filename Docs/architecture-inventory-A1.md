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

## 0. Status update — findings acted on (2026-09-11)

Per Timo's instruction, five findings were acted on. Everything else in this document is
still an open finding, not a task queue — nothing else was changed as a result of this
inventory.

| Finding | Action taken | Result |
|---|---|---|
| §2 Deterministic string/seed hashing — `SeededRandom.StableStringHash` vs `StationGenerator.NameHash` | `NameHash` now delegates to `SeededRandom`/`StableStringHash` instead of its own hand-rolled polynomial hash (`StationGenerator.cs`). `StationTextureRegistry.HashPalette` (the `GetHashCode()`-chain flagged separately in the same section) was **not** touched — still open. | Done. Deliberately reshuffles every existing station's generated layout (procedural baselines are regenerated, not persisted — expected, not a regression, per `!invariants.md`). Downstream pinned-fixture tests that assumed the old seed were refreshed to match (`StationTextureCompactionTests`, several `Megastation*Tests`). |
| §2 Triangle winding — `StationModuleMesh.AddQuad` had no correction or validation | Added finite-vertex, degenerate-first-triangle, degenerate-second-triangle, and winding-consistency checks; throws instead of silently accepting bad input. Does **not** auto-correct like `AddQuadProjected`/`ChamferedBox.WindFace` — validates-and-throws, a third posture (see §2/§8, still not unified with the other five). Per Timo: needed during development, to be gated behind a debug flag later — not done this pass. | Done (gates only, no debug-flag gating yet). Verified against the full Fast suite: no existing production call site trips the new checks. |
| §2 UI text measurement — `FontHelper.Measure` bypassed at 8+ call sites | Fixed all of them: `TextBox.cs` (`MeasureWidth`, used on arbitrary player-typed substrings for cursor placement — the most plausible actual bypass exception source — plus 3 `"A"`-glyph sites), `TextBlock.cs` (2 sites), `SystemConsole.cs` (1 site), `LedIndicator.cs` (2 sites), `UIRenderer.cs` (2 sites, already-sanitised input so lower risk but now consistent). | Done. Solution-wide grep for `.MeasureString(` outside `FontHelper.cs`/tests now returns nothing. |
| §3/§4 `MeshFactory.CreateBox`/`CreateQuad` dead ends — "perhaps replaced?" | Investigated via `git log --all --oneline --follow`; traced to the earliest "Phase 2" commit, predates station generation entirely. `StationGenerator.PrepareBoxHullMesh` (not `MeshFactory`) is the real, current box-hull builder. | Answered, not replaced — confirmed genuinely dead code, not superseded by something central. No removal action taken; still listed in §4. |
| §5 `architecture-map-ai.md` drift | DataBus row corrected (8→11 channels); `## Inferior.Rendering` section completed (10→21 files, with dead-code/duplication notes); `Station/Megastations/` section added from scratch (was entirely absent — now ~49 files across Structural/Zoning/Flight-interior/Landing/Bolon subsections, explicitly flagged as name/class-derived locators, not individually deep-read). | Done for this pass. Timo noted he'll revisit for a fuller regeneration later — this was "get it in order now," not the wholesale regeneration the doc's own header describes. |

**Not requested, not touched:** everything else in §1/§2 (basis-from-normal, the other four
winding postures, UV-sphere duplication, GPU-upload duplication, `BasicEffect` preset
duplication, `MeshRenderer` parameter-block repetition, `DVec3→Vector3` narrowing,
`StationTextureRegistry.HashPalette`, the text-mirroring root-cause candidate) remains
exactly as originally found. See §8 for the updated canonical-paths table reflecting only
the five changes above.

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
| Basis/frame construction from a normal | 6 independent implementations | Divergent behaviour (different threshold constants, different validation) | Medium | `PlanarTextGeometry.DeriveFrame`'s pattern (explicit input contract + throw-on-reflection) as the model; needs a shared low-level `Frame.FromNormal` |
| Triangle winding: trust vs. correct vs. validate | 5 independent postures across otherwise-similar "build mesh from triangles/quad" code. **Updated 2026-09-11:** `StationModuleMesh.AddQuad` moved from "trust" to a 6th posture, "validate-and-throw" — still not unified with the other five. | Divergent behaviour | Medium-high (touches hot generation paths) | `ChamferedBox.WindFace` (auto-correct + comment explaining why) as the model |
| UV sphere tessellation | 2 (`MeshFactory.CreateSphere`, `CelestialBodyRenderer.BuildPlanetSphere`) | Pure duplication (same ring/segment math, different vertex format) | Low | `MeshFactory.CreateSphere`'s loop, parameterised on vertex-build delegate |
| CPU-mesh → GPU-buffer upload | 3 near-identical (`SemanticHullGpuMesh`, `CockpitGpuMesh`, `EngineGpuMesh`) | Pure duplication | Low | Shared generic uploader |
| `BasicEffect` unlit/vertex-colour preset | 4 independent constructions (identical property values) | Cosmetic duplication | Low | A `BasicEffectPresets.UnlitVertexColour(gd)` factory |
| `MeshRenderer` per-draw-call parameter blocks | Repeated ~8-line `fx.Parameters[...].SetValue(...)` block across 6 `Draw*` overloads | Cosmetic duplication | Low | Private `SetCoreParameters` helper (partially already true for shadow/specular; core World/View/Projection/Sun block isn't factored) |
| Deterministic string/seed hashing | ~~2 independent hash functions~~ **Fixed 2026-09-11:** `NameHash` now delegates to `SeededRandom`. 1 `GetHashCode()`-chain (`StationTextureRegistry.HashPalette`) still open. | Divergent behaviour (different hash values for the same string) | Medium (touches save-compatible seeds) | `SeededRandom.StableStringHash`/`.Derive(string)` — **now the only station-generation hash** |
| `DVec3` → `Vector3` narrowing (no scale) | 3+ independent one-liners (static method, extension method, inline cast) | Cosmetic duplication | Low | A single extension method, used everywhere |
| UI text measurement | ~~`FontHelper.Measure` (sanitised) vs. 8+ raw `font.MeasureString()` call sites~~ **Fixed 2026-09-11:** all 8+ sites now go through `FontHelper.Measure` | Divergent behaviour (unsanitised path can throw/blank on unsupported glyphs) | Low-medium | `FontHelper.Measure`/`.Draw` |
| Sun/ambient lighting factor | 2 (`SceneLighting.LightFactor`, inline copy in `CelestialBodyRenderer.BuildPlanetSphere`) | Pure duplication (identical formula) | Low | `SceneLighting.LightFactor` |
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
| `StationModuleMesh.AddQuad` | ~~No correction, no degenerate check; **trusts** input winding entirely~~ **Updated 2026-09-11:** now checks finite vertices, both triangles for near-zero area, and winding consistency between the two triangles — **throws** rather than trusting, but still does not auto-correct like `AddQuadProjected` |
| `StationModuleMesh.AddQuadProjected` | Computes, compares to `expectedNormal`, **auto-flips** — different posture from its sibling `AddQuad` in the *same class* |

The `AddQuad`/`AddQuadProjected` split is worth calling out specifically: two methods on the
same type, doing conceptually the same job, with different safety postures. `AddQuad` is by
far the more heavily used of the two (base geometry for the large majority of station
decoration, including every glyph quad `PlanarTextGeometry` emits).

**Update (2026-09-11):** Per Timo, `AddQuad` now validates rather than trusting — the
"gates" were needed for development and do pop up from time to time. This narrows the
`AddQuad`/`AddQuadProjected` gap (both now reject bad input instead of one silently
accepting it) but doesn't close it (one throws, the other self-corrects) — that's still an
open decision for §8. Timo's stated intent is to put these gates behind a debug flag later
rather than have them throw in production release builds; that gating has not been done
yet. Verified against the full Fast test suite (851 tests at the time) that no existing
production call site currently trips the new checks.

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
| Orthonormal frame from one direction | *(none yet — 6 candidates in §2)* | Pick one input contract (arbitrary-reference-axis vs. authored-tangent vs. reading-direction) per use case; standardise the reference-axis threshold if that variant is kept. |
| Triangle winding correction | `ChamferedBox.WindFace`'s auto-flip pattern | Decide once whether "trust caller" (`CockpitMeshBuilder`), "validate-and-throw" (`SemanticHullMeshBuilder`, and `AddQuad` as of 2026-09-11), or "auto-correct" (`GeometryBuilder`, `ChamferedBox`, `AddQuadProjected`) is the house style — right now all three exist for no documented reason. `AddQuad`'s gates are meant to move behind a debug flag eventually per Timo, not become the permanent production posture as-is. |
| UV sphere mesh | `MeshFactory.CreateSphere`, generalised with a per-vertex delegate | `BuildPlanetSphere`'s checkerboard/lighting bake would need to become that delegate. |
| CPU mesh → GPU buffers | A new shared generic uploader | Keep per-caller vertex struct differences (Semantic/Cockpit/Engine) as the delegate's output type, not baked into three copies of the allocate/SetData scaffolding. |
| `BasicEffect` unlit/vertex-colour debug preset | New `BasicEffectPresets.UnlitVertexColour(gd)` in `Inferior.Rendering` | Don't fold in `SystemSpaceState.cs`'s *different* lit/no-vertex-colour preset by mistake — that one is genuinely distinct. |
| Deterministic string hash / seed derivation | `SeededRandom.StableStringHash` / `.Derive(string)` | ~~`StationGenerator.NameHash` produces different values for the same input — migrating it is a determinism-affecting change..., needs an explicit compatibility decision~~ **Done 2026-09-11**, with Timo's explicit authorization: `NameHash` now delegates to `SeededRandom`. Existing generated station layouts reshuffled as an accepted consequence (procedural baselines are regenerated, not persisted). `StationTextureRegistry.HashPalette` was intentionally left untouched — still a separate, unresolved case. |
| `DVec3` → `Vector3` narrowing (no scale) | The existing `DVec3.ToVector3()` extension method | Keep `EngineMeshBuilder`'s mirroring sign-flip as a separate, explicitly-named step, not fused into the conversion itself. |
| UI text measurement | `FontHelper.Measure` | ~~`UIRenderer.MeasureText` and the raw `font.MeasureString()` call sites in `TextBlock`/`TextBox`/`SystemConsole`/`LedIndicator` all need to move over~~ **Done 2026-09-11** — all of them now go through `FontHelper.Measure`; a solution-wide grep for `.MeasureString(` outside `FontHelper.cs`/tests returns nothing. |
| Ambient/sun lighting factor | `SceneLighting.LightFactor` | Trivial: replace the one inline copy in `BuildPlanetSphere`. |

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
