# Brief — Starter algorithm, real containers, calibration cube, screenshot helper

```text
Branch:  continue on the Phase A branch or a fresh branch off it — Timo's call.
Scope:   four independent tasks; commit each separately. No lighting/shader work here.
Docs:    read Docs-ai/!invariants.md §6 (deterministic seeding) before Task 2.
```

## Task 1 — Deterministic starter-system algorithm

Current state: starter star selection is **duplicated** (`InferiorGame.FindStartStar`
and `GalaxyMapState.FindStartingSystem` — nearest G/K star to galactic origin), and the
starter station is found **by name** (`"Far Station"`, `SystemSpaceState.Helpers.cs`),
which breaks the moment the seed or galaxy changes.

New algorithm, one canonical implementation (suggested home: `Inferior.Galaxy`, since
it needs only galaxy + system data generation — no meshes, no GraphicsDevice):

1. Anchor point: galactic origin (unchanged).
2. Iterate stars ordered by distance from the anchor, filtered to spectral class G/K
   (keep the current filter). For each candidate, generate its `StarSystem` data and
   check `Stations.Count >= 3`. Data generation is cheap (no meshes); cap the search
   (e.g. nearest 200 candidates) with a fallback to the current nearest-G/K behaviour
   plus a logged diagnostic if nothing qualifies.
3. Starter station within that system: deterministic order — largest `StationSize`
   first (better testing target: docking bay), tie-broken by ordinal `PersistenceId`
   sort. If Timo prefers plain first-by-PersistenceId, that is one line.
4. Spawn via the existing canonical relocation path at
   `InitialStarterStationStandOffMeters` (500 m) — replace the name-lookup plan in
   `CreateInitialStarterStationRelocationPlan` with the computed station's
   `PersistenceId`. The `"Far Station"` name constant and both duplicate find-star
   methods are deleted; all callers use the new shared implementation.

Tests: fixed seed → same star + same station every run; selected system has ≥3
stations; relocation plan resolves to a non-empty `PersistenceId`. Update
`StarterStationRelocationTests` accordingly.

## Task 2 — Containers are real objects (explicit, non-negotiable)

Containers have repeatedly drifted into special-case treatment. This task ends that.
The rule, stated once and binding: **a container is an ordinary world object. It goes
through the same generation conventions, the same rendering pipeline, and the same
world-object bookkeeping as everything else. Nothing about a container is "debug".**

They are placed around stations for testing purposes — the *placement policy* is for
testing; the *objects* are real. Concretely, in `SystemSpaceState.DebugContainers.cs`:

- Rename everything: `SpawnTestContainers` / `TestContainerEntry` / `_testContainers` /
  `DrawTestContainers` lose all `Test`/`Debug` naming, the two `TODO: remove` comments
  go away, and the file is renamed (e.g. `SystemSpaceState.Containers.cs`).
- **Fix the seeding invariant violation**: `station.Name.GetHashCode() ^ ...` is
  process-dependent hashing, forbidden by `!invariants.md` §6. Derive the seed from the
  station's `PersistenceId` through the project's stable hash/seed derivation, salted
  semantically (e.g. `"containers"`), so container placement never reshuffles because
  of unrelated RNG or runtime changes. Same for the per-container tumble stream
  (currently seeded by global spawn index — derive from container identity instead).
- **Kinematics on rails, not mutable state**: containers do not move. Position =
  station position + fixed offset; orientation = initial orientation advanced by
  seeded angular velocity as a **pure function of sim time** (quaternion from
  axis × rate × simTime), evaluated at draw/query time. Delete the per-frame
  orientation mutation. This matches how stations orbit and keeps them out of the
  sim-thread ownership question entirely — no snapshot machinery needed until a
  gameplay action can move one.
- Rendering stays exactly the standard path (`MeshRenderer.DrawDynamicLit`,
  factory-generated mesh with wear/pattern/lock grade — already correct). No bespoke
  shading, no special draw ordering, no container-specific render flags — now or in
  future briefs.
- Radar/targeting continue to see them as before (adapt `FeedRadarContacts` to the
  rename only).

## Task 3 — Calibration cube (real object, AI-screenshot ground truth)

A rotating cube near the starter station, built for visual analysis by AI from
screenshots. Uses the same rails mechanism as Task 2 (fixed position + seeded/constant
angular velocity as a function of sim time) — if a small shared "world prop" helper
falls out of Task 2, use it; do not build a framework for two objects.

Specification:

- 10 m cube, positioned 100 m in front of the player's spawn pose at the starter
  station (compute once from the relocation result; it does not follow the player).
- **Six distinct matte face albedos, axis-coded**: +X red, −X dark red, +Y green,
  −Y dark green, +Z blue, −Z dark blue. Face labels ("+X", "−X", …) in white via the
  existing bitmap-text geometry (`AddTextGeometry` technique). Purpose: any screenshot
  reveals cube orientation, sun direction (which faces are lit and how strongly),
  shading falloff, and winding/culling correctness at a glance — this is a lighting
  test card, so albedo must be flat per face: no wear, no panel texture.
- Rotation: constant angular velocity, axis ≈ normalize(0.3, 1.0, 0.2), ~0.05 rad/s
  (full revolution ≈ 2 min) — slow enough that a screenshot pair taken seconds apart
  still shows coherent movement, fast enough that a session samples all face angles.
- Standard rendering path (same as Task 2). Radar visibility: fine if it appears;
  no special handling either way.

## Task 4 — In-game screenshots (Ctrl+C) + host-system helper

New helper in `Inferior.Game` for host-system concerns (screenshots now; file
dialogs/IO helpers may join later). Suggested: `Inferior.Game/Platform/HostServices.cs`
(static class). Do not spread OS-specific code outside this home.

Screenshot capture:

- `HostServices.SaveScreenshot(GraphicsDevice gd)`: read the backbuffer
  (`gd.GetBackBufferData`), wrap in a `Texture2D`, save PNG via `SaveAsPng`, dispose.
- Output: `Screenshots/` next to the executable (create if missing), filename
  `yyyyMMdd_HHmmss_fff.png`. Log the saved path to console/log.
- File write may run on a background task, but the backbuffer read must happen on the
  render path.
- Trigger: **Ctrl+C**, rising edge (same chord pattern as `StationCycleController`'s
  Ctrl+F12), detected globally in `InferiorGame.Update` so it works in every game
  state. Set a flag; capture at the **end of `InferiorGame.Draw`** for the same frame,
  then clear the flag.
- Ctrl+C has no current in-game binding conflict — verify that claim against the input
  code before wiring; report if wrong.

Note: Timo reports OS-level screenshotting misbehaves while the game runs; this
in-engine path is the workaround and the diagnosis of that bug is NOT in scope.

## Verification (all tasks)

- Builds; full test suite passes; new tests for Task 1.
- Honest verification language: what is implemented/tested vs. what needs Timo
  in-engine (starter spawn location, container look, cube readability, screenshot
  files appearing).
- Update `Docs-ai/!current-state.md` (starter algorithm, container promotion, cube,
  screenshot helper) and `architecture-map-ai.md` (new/renamed files, deleted
  duplicate find-star methods).
