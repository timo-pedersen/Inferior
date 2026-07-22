# Report S2a — Texture Generation Investigation

> Read-only investigation for Brief S2a. No code was changed while producing this report.
> All file/line references are against `feature/shadow-resolution` as of this report.

---

## 1. Output shape

**Per station module, one 512×512 `Texture2D`, built at runtime via `SetData(Color[])` —
not loaded content, and not per-face or per-station.**

- Generator: `StationTextureRegistry.Generate` (`Inferior.Game/Station/StationTextureRegistry.cs:70-168`).
  `Size = 512` (`:68`). Builds a `Color[262144]` CPU-side buffer, then `new Texture2D(gd, 512, 512); tex.SetData(pixels);` (`:165-167`).
- One texture is shared across **all six faces** of a module's hull (`BuildHullMesh`,
  `Inferior.Game/States/SystemSpaceState.Stations.cs:246-295`) via a **tiling** UV
  projection — `UvScale = 5.0f` (`:249`), i.e. 1 UV unit = 5 metres, and the sampler wraps
  (`AddressU = Wrap, AddressV = Wrap`, `LitSurface.fx:80-81`). A 20 m face therefore tiles
  the same 512×512 image 4×4 times, identically on every face regardless of orientation.
- **The texture is not actually per-module either — it's cached and shared across modules,
  and across stations, keyed only by `(SurfaceTexture, palette-hash)`, not by module or
  station identity.** `GetOrCreate` (`StationTextureRegistry.cs:51-64`):
  ```csharp
  int hash = HashPalette(palette, surface);   // palette + surface only — no module/station id
  if (_cache.TryGetValue((surface, hash), out var cached)) return cached;
  var tex = Generate(gd, surface, palette, seed);   // seed only matters on a cache miss
  _cache[(surface, hash)] = tex;
  ```
  `TexturePalette.From(StationProfile profile)` (`TexturePalette.cs:18-97`) is a pure
  function of `profile.Economy` alone (7 possible values, all reachable — see the §5
  correction) — it does not vary by seed, station name, or age. So the cache key
  collapses to `(SurfaceTexture, Economy)`:
  **at most ~28 distinct textures (4 reachable surfaces × 7 economies, see §5)
  exist for the entire galaxy, for the whole process lifetime.** The very first module of
  a given (surface, economy) combination to call `GetOrCreate` bakes the pattern (using
  its own seed for the RNG stream); every other module of that combination — on that
  station or any other station in the galaxy — gets the *same* `Texture2D` object handed
  back, unrelated to its own `mod.Seed`.
  - One exception: the station's core module (`modules[0]`) always gets its own
    non-cached copy, because `GenerateNameFaceTexture` (`StationGenerator.cs:88-122`)
    reads the cached texture's pixels via `GetData`, draws the station name over a copy,
    and assigns a **new** `Texture2D` to `modules[0].TextureInstance` only
    (`StationGenerator.cs:83-85`) — it does not mutate the shared cache entry.
- Format: `Texture2D` built via `SetData(Color[])` only — no `.png` content is used for
  the *procedural* per-module texture. Five `.png` files *are* loaded as content
  (`Content.mgcb:47-117`, `SystemSpaceState.cs:389-400`) and registered into
  `StationTextureRegistry`'s separate flat/fallback dictionary — see §5, this path is
  effectively dead.
- **Channels:** RGB carries the panel albedo (base colour + noise + panels + seams + wear
  effects, all in §2/§4). **Alpha is written as opaque (255) everywhere and never read
  by any shader technique** — confirmed by grepping every `tex2D(TextureSampler, ...)`
  read site in `LitSurface.fx` (lines 277, 300, 332, 355): all four techniques
  (`BakedColorLit`, `BakedColorLitShadowed`, `DynamicLit`, `DynamicLitShadowed`) only ever
  read `tex.rgb`. **The alpha channel of the panel texture is a genuinely free, unused
  8 bits today** — one real channel S2b could claim without a second texture, *if* the
  shared-cache problem above is solved first (see §6).
- **Vertex format has no spare room.** `VertexPositionNormalColorTexture`
  (`Inferior.Rendering/VertexPositionNormalColorTexture.cs`) is Position/Normal/Color(RGBA8)/
  TexCoord(Vec2) — Color's alpha is already spoken for (self-illumination floor `S`, see
  §5), TexCoord is a plain UV pair. Any *per-vertex* channel would need a struct change
  (new `VertexElement`, new shader input) — there is no free lane in the current format.
- **Ships and containers carry no real texture at all.** Every `MeshRenderer.DrawDynamicLit*`
  call site for ships (`Inferior.Rendering/ShipMeshRenderer.cs` — `DrawSemanticHull`,
  `DrawInstalledEngines`, `DrawInstalledCockpit`, `DrawLegacyFallback`), containers
  (`SystemSpaceState.Containers.cs:62`), and the calibration cube
  (`SystemSpaceState.CalibrationCube.cs:136`) omits the optional `texture` parameter,
  which defaults to `MeshRenderer`'s 1×1 white stand-in (`MeshRenderer.cs:19,25-26,58`).
  Their entire surface appearance is **vertex colour only** (`part.MaterialColour`,
  `EngineMaterialColour(...)`, per-face constants from `ShippingContainerFactory`). **Only
  station hulls sample a real, generated bitmap.** This bounds S2b's realistic scope
  tightly: a texture-space height/gloss channel is meaningful for station hulls today and
  for nothing else — extending it to ships/containers is a "build a texture pipeline for
  them first" problem, not a "grow two channels" problem.
- Station **decoration** (windows, pipes, greebles, etc., `BakedColorLit*`) reuses the
  *same* `mod.TextureInstance` as the hull (`SystemSpaceState.Stations.cs:160`:
  `Texture2D tex = mod.TextureInstance ?? StationTextureRegistry.Get(mod.Mesh!.Texture);`),
  with its own independent per-quad UV projection (`StationModuleMesh.AddQuad`, 5 m/tile,
  `StationModuleMesh.cs:121-146`). There is no dedicated decoration texture.

---

## 2. Provenance

**Base albedo is fully procedural — the loaded `.png` files are not actually reachable
for normal generation.** `AssignTextures` (`StationGenerator.cs:71-86`) unconditionally
assigns `mod.TextureInstance = StationTextureRegistry.GetOrCreate(...)` for **every**
module in the list, with no branch that skips it. `StationTextureRegistry.Get(SurfaceTexture)`
— the accessor backed by the loaded PNGs — is called from exactly one place in the whole
codebase: the decoration-pass fallback `mod.TextureInstance ?? StationTextureRegistry.Get(...)`
(`SystemSpaceState.Stations.cs:160`), which structurally cannot fire given the guarantee
above. See §5 for the "ghost" write-up; in short, **the primitive Gimp textures Timo
recalls are loaded, held in GPU memory, and never actually drawn.**

**The procedural texture generator (`StationTextureRegistry.Generate`,
`StationTextureRegistry.cs:70-168`) is a pipeline of CPU-side pixel operations on one
`Color[512×512]` buffer, applied in this order:**

| Step | Function | What it does |
|---|---|---|
| 1 | `FillBaseNoise` (`:172-180`) | Per-pixel luminance jitter around `palette.BaseColour` |
| 2–3 | `BuildGrid` ×2 + `ApplySubPanels` (`:184-234`) | Random grid lines (economy-dependent count), per-cell brightness offset |
| 3 | `ApplySeamLines` (`:250-271`) | 1–2px seam colour drawn at each grid line |
| 4b | Weathering streaks (`:88-114`, inline) | 3–15 drifting vertical streaks, gated on `GrimeStrength > 0.15` |
| 4c | Oxidation patches (`:116-140`, inline) | 2–6 elliptical rust blobs, gated on `GrimeStrength > 0.40` |
| 4 | `ApplyEdgeGrime` (`:273-295`) | Distance-to-seam falloff darkening, gated on `GrimeStrength` |
| 5a | `AddScratchLines` (`:311-334`) | 4–20 random scratch segments, gated on `GrimeStrength > 0.25` |
| 5b | Military stencil fragments (`:149-163`, inline) | `TextPainter.DrawText` stamps ("A7", "RESTRICTED", etc.), Military economy only |
| — | `GenerateNameFaceTexture` (`StationGenerator.cs:88-122`) | Station name overlay, core module only, applied *after* `Generate` returns |

Every one of these is a **per-pixel, coordinate-aware** operation — see §3.

**The decoration passes (`StationDecorator.cs`, all 17+ passes listed in
`Docs-ai/stations-ai.md`) are 100% geometry, not texture.** Grepping every
`Generate*`/`Place*`/`Run*` function in `StationDecorator.cs` for pixel-buffer operations
(`pixels[`, `SetData`, `GetData`, `Color[]`) finds **zero matches** outside one unrelated
container-palette array (`ContainerColorsBase`, `:3057`, a list of `Color` values for
vertex tinting, not a texture). Windows, hatches, antennas, pipes, panel seams (the
*geometry* kind — see below), edge trim, vent grilles, greebles, tanks, lights, and
placed containers are all `StationModuleMesh.AddQuad`/`AddOrientedBox`/`AddPrismPipe`/
`MergeTransformed` calls — real 3D geometry with baked-vertex-colour albedo×AO, exactly as
`stations-ai.md` already documents for `GeneratePanelSeams`. This is **project-wide**, not
an exception for panel seams specifically.

**"Seams and ridges" is two unrelated concepts with the same name — draw the line
explicitly, since S2a was asked to:**

| Name | Kind | Where | Coordinate space |
|---|---|---|---|
| `ApplySeamLines` (base panel texture) | **Texture** — 1–2px coloured lines | `StationTextureRegistry.cs:250-271` | 512×512 pixel space, at `gridX`/`gridY` line positions |
| `GeneratePanelSeams` (raised strips) | **Geometry** — real inset prism quads (`AddSeamStrip` → `mesh.AddQuad`, Z-offset 0.028 m) | `StationDecorator.cs:1615-1700` | Module-local 3D face space (`u0`/`u1`/`centerOffset`) |

These two "seam" systems run **independently**, with independent RNG streams and no
shared coordinate frame — the texture's seam grid and the geometry's raised seam strips
are not required to (and generally will not) line up. Same story for "wear/ridges" in
general: `StationTextureRegistry`'s streaks/oxidation/scratches (texture, §1 table above)
and `ShippingContainerFactory.ApplyWear`'s per-face vertex-colour multiply (geometry, §4)
are two more independent systems sharing only a name. **S2b's height channel — "base
manufacturing pattern... colour stays uniform" — only concerns the texture-space
effects.** The geometry ones (real prism ridges) already self-shadow under the S1/E1
lighting pipeline and need no bump term.

---

## 3. Coordinate awareness — the load-bearing question

**Yes, unambiguously, for the base panel texture generator.** Every effect in
`StationTextureRegistry.Generate` operates by iterating explicit `(x, y)` pixel
coordinates it has just computed or chosen, not by an opaque whole-image filter:

- `ApplySeamLines` (`:250-271`) iterates the *exact* `gridX`/`gridY` line positions it
  built in `BuildGrid` and writes those specific columns/rows.
- `ApplyEdgeGrime` (`:273-295`) precomputes a per-column/per-row `SeamDistance` array
  (`:298-309`) and, for every pixel, knows its distance to the *nearest known seam
  position* before deciding how much to darken it.
- The weathering streaks (`:92-113`) track an explicit `(sx, sy)` origin, `length`,
  `width`, and per-step `drift`, writing `pixels[py*Size+px]` at computed coordinates
  every step.
- Oxidation patches (`:121-139`) have an explicit `(cx, cy)` centre and `(rx, ry)` radii,
  iterating only the pixel rectangle that could plausibly contain the ellipse and testing
  each one's normalized distance.
- Scratch lines (`:316-333`) walk an explicit `(x0, y0)` + angle + length, writing
  `pixels[py*Size+px]` per step of the line.
- The military stencil (`:154-162`) and station-name overlay
  (`StationGenerator.cs:102-118`) call `TextPainter.DrawText`
  (`Inferior.Game/Station/TexturePainter.cs:15-57`), which walks known glyph-cell pixel
  coordinates (`px = cx + col*pixelScale + sx`, `:43-44`) — this is the clearest possible
  case of "the code knows exactly where it just drew."

**Every one of these already has its placement information sitting in local variables at
the moment of the write — nothing is discarded after the albedo write. A parallel
`float[] height` / `float[] gloss` buffer, written inside the same loops using the same
`(x, y)` (or `px, py`), is a mechanical, non-invasive addition** for the base-texture
layer specifically. This is the good news the report was asked to surface.

**The nuance the brief's premise doesn't fully anticipate: coordinate-aware ≠
correspondence to a fixed physical location.** Because the texture (a) tiles 4×4 or more
across a typical 20 m face (`UvScale = 5.0f`, §1) and (b) is shared/cached across
unrelated modules and stations (§1, §5), a given pixel coordinate inside the 512×512
buffer does not correspond to one place on one station — it corresponds to a repeating
tile pattern reused wherever that `(SurfaceTexture, Economy)` pair recurs across the
galaxy. The generator knows where *within its own buffer* each feature is; it has no
concept of where in the universe that buffer is currently being displayed, and the same
buffer is displayed in many unrelated places at once. This matters for S2b only in that
dynamic combat damage (explicitly deferred, but flagged in the brief as the thing not to
foreclose on) **cannot be written against the current cache** — see §6.

**Decoration geometry is coordinate-aware in a completely different, disconnected
space.** `StationDecorator` passes know exactly where their geometry sits in
module-local 3D coordinates (e.g. `GeneratePanelSeams`'s `u0`/`centerOffset` in
`StationDecorator.cs:1640-1657`) — but that's a different coordinate system from the
shared texture's pixel space, with an independent RNG stream and no coupling between the
two. A window placed at 3D position X does not know or care what the underlying tiled
texture looks like at the UV coordinate it happens to land on, and vice versa. If S2b
ever wanted geometry-placed decoration to leave a mark in the height/gloss channels (as
opposed to just the base-texture effects), that would require bridging two currently
disconnected coordinate systems — out of scope for what the brief calls Path 2 today, but
worth naming since "seam" and "wear" already mean two different things in this codebase.

**One-line answer: yes, the generator that actually produces pixels knows exactly where
its own features are — but that same generator's output is shared across many unrelated
surfaces, so "knowing where a feature is" does not currently mean "knowing whose surface
it's on."**

---

## 4. Wear system — what's actually there

**Found: two unrelated wear implementations, not one.**

**(A) Station panel wear — `TexturePalette.GrimeStrength`, texture-space, economy-keyed.**
`GrimeStrength` (`TexturePalette.cs:14`, range 0–1) is set once per economy by
`TexturePalette.From` (`:18-97`) — e.g. Industrial = 0.38, Military = 0.15, Luxury = 0.04,
Independent = 0.30. It gates and scales, inside `StationTextureRegistry.Generate`:
streak count (`3 + GrimeStrength*12`, `:91`), oxidation-patch eligibility (`>0.40`,
`:117`), edge-grime intensity (`ApplyEdgeGrime`, `:279,291`), and scratch-line count
(`GrimeStrength*18+4`, `:313`, gated on `>0.25`). **It modulates colour only** — there is
no separate channel it writes to, no geometry it affects, nothing outside the RGB pixel
buffer. **Deterministic**: seeded via `new System.Random(seed ^ HashPalette(palette, surface))`
(`:77`), where `seed` is whichever module first won the cache race for that
(surface, economy) pair (§1) — reproducible given the same galaxy seed, but **not
per-station or per-module** in the way a reader would expect from the `mod.Seed`
parameter name. **Not tied to a station's age at all**: `StationProfile.Age`/`Wealth`/
`Population` (`StationProfile.cs:19-21`) are generated per station but never read
anywhere outside `StationProfile.cs` itself (confirmed by grep — zero other references in
the whole `Inferior.Game` project). A 5-year-old and a 200-year-old station of the same
economy render **identically worn**. This is the strongest candidate hook for S2b's gloss
channel (its existing dials already point at "how worn is this"), but it inherits the
cache-sharing problem from §1/§6 and currently has no connection to station age/history
despite the data existing.

**(B) Container wear — `ShippingContainerFactory.ApplyWear`, per-face vertex colour,
geometry-space, completely separate.** `ApplyWear`
(`Inferior.Game/Containers/ShippingContainerFactory.cs:587-619`) multiplies vertex RGB by
a wear-derived factor **per face index** (`mesh.MultiplyFaceColor(f, edgeMul)`, `:607`) —
not per-texel, since containers have no texture to begin with (§1). It targets "the first
20 faces" as a hardcoded approximation of "edge chamfers + corner triangles," with a
comment (`:591-599`) that **the code itself flags as fragile**: *"Hardcoded face-index
wear targeting is fragile in general — a proper fix would have each Build* function tag
which face indices it added, so `ApplyWear` can target semantic groups instead of guessing
numbers."* Deterministic (seeded `SeededRandom(seed+1)`, `:611`), driven by a `wear`
float (0–1) passed in by the caller, unrelated to `TexturePalette.GrimeStrength` and
unrelated to the station wear system entirely. **This is a different implementation of
the same idea, not a shared one** — see §5/§6.

An unrelated third "wear" exists in the simulation domain
(`EngineInstance.WearFraction`, `Inferior.Gameplay/Engines/EngineInstance.cs:18,29-30`) —
mechanical engine wear affecting performance, nothing to do with visual surface
appearance. Named for disambiguation only; not a candidate for anything in S2b.

---

## 5. Debt & ghosts

**Confirmed dead/orphaned code and content in the texture path:**

1. **The five loaded `.png` "Gimp" textures are unreachable in normal play.**
   `cleanpanel.png`, `techpanel.png`, `industrialpanel.png`, `cargopanel.png`,
   `wornpanel.png` are loaded and registered (`SystemSpaceState.cs:389-400`) into
   `StationTextureRegistry`'s `_textures` dictionary, overwriting the flat-colour
   placeholders `Initialize()` set up. The only reader of that dictionary,
   `StationTextureRegistry.Get(SurfaceTexture)`, is called from exactly one site
   (`SystemSpaceState.Stations.cs:160`, a `mod.TextureInstance ?? ...` fallback) that
   cannot fire given `AssignTextures` unconditionally sets `TextureInstance` for every
   module (`StationGenerator.cs:77-81`). `StationTextureRegistry.GetColor` (`:40`) has
   **zero callers anywhere in the codebase** despite its own comment describing it as
   used for "the base box DiffuseColor pass" — that pass no longer exists or never called
   it. This is very likely what Timo remembers as "primitive Gimp textures" — they're
   real, loaded, and dead.
2. **`testpanel1.png` is compiled into content and never loaded by any code at all**
   (`Content.mgcb:95-105`; zero matches for `testpanel1` anywhere in `*.cs`). Not even a
   dead fallback — just orphaned content.
3. **`SurfaceTexture.WornPanel` is unreachable.** `SurfaceFor(string category)`
   (`StationGenerator.cs:125-132`) maps every module category to `CleanPanel`,
   `TechPanel`, `IndustrialPanel`, or `CargoPanel` — `WornPanel` is never returned, despite
   having its own registry entry, colour, and loaded `.png`. Ironically, the one surface
   named for the wear system is the one no module ever gets assigned.
4. **Correction (added during Brief S2b-2): the claim below was wrong.**
   ~~`StationEconomy.Independent` (the 7th enum value) is unreachable.~~
   `StationProfile.Generate` (`StationProfile.cs:23-38`) picks
   `(StationEconomy)rng.NextInt(0, economyCount - 1)` — this report assumed
   `NextInt(min, max)` was exclusive on the upper bound, matching bare
   `System.Random.Next(min, max)`. It isn't: `SeededRandom.NextInt(int min, int max)` is
   documented and implemented as **[min, max] inclusive** (`_rng.Next(min, max + 1)`,
   `Inferior.Core/Random/SeededRandom.cs:82-84`). With `economyCount = 7`,
   `NextInt(0, 6)` = `_rng.Next(0, 7)`, reaching every index 0..6 — `Independent` included.
   There is no off-by-one bug; all 7 economies are reachable. §1's "~24 distinct textures"
   figure (4 reachable surfaces × 6 reachable economies) should read **≤28** (4×7) —
   moot now regardless, since Brief S2b-1 replaced the shared static cache this figure
   described with per-station ownership.
5. **`StationProfile.Age`/`Wealth`/`Population` are generated and never read** (§4) —
   dead data that looks load-bearing (it's *right there* next to `Economy`, which *is*
   read) but isn't.
6. **No leftover baked-shadow-system residue was found in the texture-generation path.**
   Grepped `StationTextureRegistry.cs`, `TexturePalette.cs`, `TexturePainter.cs`, and
   `StationModuleMesh.cs` for shadow/normal-map/bump/height/gloss-adjacent naming — zero
   matches. Cross-checked against `Docs-archive/Shadow_fail_retrospective.md`: that
   experiment lived entirely in a separate shadow-map render target and its own
   diagnostic ladder, never touched station panel/material generation. **Timo's "tangled"
   suspicion is well-founded, but the tangle is the fallback-PNG/cache/wear duplication
   above, not shadow-system leftovers.**
7. **Naming mismatch, cosmetic:** `Inferior.Game/Station/TexturePainter.cs` defines a
   class called `TextPainter`, not `TexturePainter` — harmless, but a `grep -r
   "TexturePainter"` for the class itself will fail; only the filename matches.

**Duplicate implementations found (this project's recurring hazard, per the brief):**

- **Wear is implemented twice**, independently, in different spaces (§4): texture-pixel
  wear for stations (`StationTextureRegistry`, colour-only, economy-keyed, ignores
  station age) vs. per-face vertex-colour wear for containers
  (`ShippingContainerFactory.ApplyWear`, self-documented as fragile). No shared code, no
  shared parameterization, no shared RNG convention.
- **"Panel seams" is two unrelated systems sharing one name** (§2): texture pixel lines
  (`StationTextureRegistry.ApplySeamLines`) vs. raised geometry strips
  (`StationDecorator.GeneratePanelSeams`).
- Texture generation is **not** forked between stations and ships/containers in the sense
  of "two competing implementations of the same thing" — it's forked in the more basic
  sense that **ships/containers have no texture-generation implementation at all** (§1).
  There is exactly one texture generator in the codebase (`StationTextureRegistry`), and
  it is currently used by station hulls/decoration only.
- **`ChamferDepthForSeed` (`StationGenerator.cs:34-35`), the brief's own named example of
  a past ×3 duplicate, is confirmed already resolved** — single function, called from
  `StationGenerator.cs` (core + regular modules) and `DockingBayHull.cs`, with the result
  stored once on `PlacedModule.ChamferDepth` (`PlacedModule.cs:49`) and read from four
  other sites via that property. Flagging as *resolved*, not live, since the brief cited
  it as an example and an accurate status matters more than repeating stale context.

**Existing precedent worth citing for S2b's design (not a bug, a template):**

- `StationModuleMesh` already has a working "generic bake-time per-face scalar override"
  mechanism: `AmbientOverrideFaceStart`/`AmbientOverrideFaceCount`
  (`StationModuleMesh.cs:111-119`), consumed by `StationGenerator.BoostAmbientForFaceRange`,
  used today by `DockingBayHull`'s interior faces. It's per-*face*, not per-*texel*, and
  it's a single override value, not two channels — but it's a real, shipped instance of
  "a MeshFactory module flags a face range wanting different treatment than the rest of
  the mesh, and generation-time code applies it" that S2b could study or extend rather
  than inventing a new mechanism from nothing.
- Similarly, the self-illumination floor `S` (vertex alpha, written by
  `StationModuleMesh.ApplyIlluminationFlags`, `:604-613`) is the project's only existing
  working example of "a baked-at-generation-time scalar channel read by the shader every
  frame" — but it lives in **vertex alpha** (coarse, per-vertex resolution), not a texture
  channel. S2b's height/gloss, if it wants finer-than-vertex resolution (implied by "old
  chips and marks" and derivative bump), cannot reuse this path directly and must go
  through the texture-pixel side instead — which is where the cache problem lives.

---

## 6. Growability verdict

**Two different, currently-independent axes need different answers:**

1. **Can the base-panel-texture generator (`StationTextureRegistry.Generate`) grow two
   parallel scalar output arrays, written by the same loops that already place its
   pixels?** — **Yes, cleanly, with no restructure.** Every effect already iterates known
   `(x, y)` coordinates (§3); adding `float[] height` and `float[] gloss` arrays alongside
   `Color[] pixels`, written in the same loops, then packed into (for example) the
   texture's currently-unused alpha channel plus a second texture (or a second render
   target/array if two full channels are wanted at full precision) is a mechanical
   extension of one file. This is "one shader brief" territory for the base texture
   alone.

2. **Can that same growth be written into per-instance, later-runtime-writable storage —
   the actual point of doing this (per the brief's stated goal and the [OPEN] dynamic-
   damage note)?** — **No, not without a restructure, because the required uniqueness
   doesn't exist today.** The generated texture is cached and shared across every module
   of the same (surface, economy) pair, across the whole galaxy (§1). Baking real
   per-instance detail (a specific decoration's wear, and eventually a specific combat
   hit) into "the" texture would silently corrupt every other unrelated module and
   station sharing that same `Texture2D` object. `mod.Seed` is threaded through
   `GetOrCreate`'s signature in a way that strongly implies per-module uniqueness that
   does not actually exist — the cache key ignores it.

**The smallest restructure that unblocks S2b (sketch only, not a plan to execute):**
change `StationTextureRegistry`'s cache key from `(SurfaceTexture, palette-hash)` to
include the module's own identity (e.g. `mod.PersistenceId` or a stable per-module key),
so every module gets its own generated texture instance again driven by its own
`mod.Seed` (which the API already accepts and threads through, just doesn't use for
uniqueness). This is a **small, mechanical** change — it does not touch the pixel-drawing
code in §2/§3 at all, only the caching layer around it — but it has a real, measurable
cost: memory that is currently shared 1×-per-(surface,economy) becomes 1×-per-module
(512×512×4 bytes ≈ 1 MB per module before adding height/gloss; a large station can have
dozens to low hundreds of modules). Whether that cost is acceptable is a design/budget
call for Timo, not something this report should decide — it's exactly the kind of
question S2b's design pass should resolve before writing shader code, since it changes
memory scaling from "bounded by economy count" to "linear in station module count."

Two smaller, independent cleanups worth doing in the same pass if a restructure happens
(not required for S2b, but cheap while already touching this file): drop the five dead
`.png` loads and the unreachable `WornPanel`/`Get`/`GetColor` path (§5.1–5.3), since S2b
would otherwise be adding new channels next to code that's already provably unused.

**One-line answer to the load-bearing question:** the generator knows exactly where every
feature is *within its own buffer*, but that buffer is currently shared identically
across many unrelated modules and stations — so height/gloss can grow cleanly as *pixel
data*, but not yet as *per-instance* pixel data, which is what the brief's stated goal
(and the deferred dynamic-damage idea) actually needs.

**Verdict: S2 is a cleanup brief (cache-key/identity restructure, small and mechanical)
followed by a shader brief (the height/gloss emission itself), not one combined brief.**
The cleanup is narrowly scoped to `StationTextureRegistry`'s caching layer and does not
require touching `StationDecorator`, `StationModuleMesh`, or any geometry pass — those are
already fine as-is and orthogonal to this problem.
