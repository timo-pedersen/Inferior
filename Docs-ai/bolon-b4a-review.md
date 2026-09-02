# Bolon B4a / B4a.1 — ambassador bay review

Status: implemented and visually accepted by Timo on 2026-08-31 ("Definite pass!"). Collision explicitly
deferred by Timo: neither the existing station hull nor this bay stops ships.
No docking, pads, gameplay pressure, or artificial-light simulation was added.

## B4a.1 accepted correction

- The bevel is half the original axial depth and transverse inset. A short straight
  outer reveal retains the removed axial depth: the clear mouth, 5 m main throat,
  chamber position and all octagonal chamber dimensions stay frozen.
- The reveal/bevel reuse the surrounding Bolon hull's surface-history resolver,
  material family, tint calculation and physical UV projection. No contrasting
  brushed recipe or artificial illumination override remains on the chamfer.
- The original four flat luminous point-marker patches are removed. The shared H1e
  fixture planner supplies four mount/housing/barrel/emitter assemblies directly on
  the C60 facet, with its existing source halos and six-fin soft beam renderer.
  Human H1e retains its accepted parameters, identities, positions and palettes.
  Blue is entrance-local up, amber down. Beams are 1,400–1,600 m long with the
  accepted 0.7–1.2 degree half-angle, depth-tested/additive and not world lights.
- The rear wall now has a centered 20 x 8 m floor-touching opening, 0.75 m inward
  bevel, 7 m corridor stub and closed dark termination. The clear stub section is
  18.5 x 7.25 m; its floor continues the bay floor without a step. No decoration.
  Collision remains explicitly deferred; the dark end is opaque geometry only.
- Rear-wall geometry uses an explicit three-region notch partition, avoiding
  coincident-floor clipping slivers without changing the octagonal chamber.

Current Nova correction measurements: outer mouth 243.54 x 25 m, unchanged clear
slot 240.54 x 22 m, bevel 1.94 m, outer reveal 1.94 m, main throat 5 m. The frozen
B4a spatial-plan signature below is unchanged. Visible hull is now 266,482 vertices
and 96,980 triangles; ambassador architecture contributes 416 triangles including
the reused fixtures and finely sampled hull-material chamfer. Beams add 1,440
CPU vertices / 480 triangles through the existing H1e draw path. Still no new
station-owned textures or buffer resources. A measured Release test run reported
57.5 ms whole-Bolon planning and 772.4 ms whole-Bolon mesh construction.

## Architecture

`BolonAmbassadorBayPlanner` consumes only the accepted B1 structural plan. Its
`bolon-ambassador-bay:v1` seed domain selects one substantial vessel, unattached
hexagonal face, and opposite-corner axis. The resulting plan owns the station-local
Right/Up/Outward frame, mouth, chamber, reservation, guidance and signature.

Candidates must clear a broad 1,500 m outward corridor against conservative vessel
spheres and connector segments. Width is solved against actual C60 face planes,
not an inscribed circular opening. The chamber retains 16 m clearance from other
hull planes, including room for existing inward B3a iris recesses.

B2 plans are generated unchanged, then the entrance host's aperture/vent group is
removed during CPU composition, without filling its place elsewhere. Surviving
groups, surface history, the molecular graph and B3a fixtures are unchanged.
Effective aperture signatures consequently change if the host previously had a group.

The existing convex hull cutout code removes the rectangular mouth. The same
clipping implementation makes the chamber's front bulkhead minus its passage.
There is no cap in the flight path. All new triangles join the existing combined
hull/material/shadow buffers; no new mesh upload operation, texture or shader.

The existing DynamicLit vertex-alpha floor lights the throat and chamber. Exterior
alpha is first set to zero so opting the combined mesh into that capability does
not brighten the old hull. Shared brushed metal for the interior, cold-white tones
and a darker floor retain shape/material distinctions. This is a readability
prototype, not H1c baked lighting. B4a.1 replaces the initial point patches with
shared H1e physical emitters/beams, without adding a crown.

## Original B4a Nova fixture (before the correction above)

Identity: `Oranae:Oranae I:Nova Anchorage`, explicitly generated as Bolon for tests.
This does not change the runtime station's archetype selection.

- Vessel 0, hex face 14.
- Clear slot: 240.54 x 22 m; outer rectangular mouth: 246.54 x 28 m.
- Inward chamfer: 3.88 m; straight throat: 5 m.
- Bay: 336.52 m maximum width x 66 m height x 378.84 m length.
- Full bay height starts immediately; width expands over the first 40 m.
- Conservative other-vessel approach clearance: 499.15 m.
- Added architecture: 74 triangles, including the four guidance patches.
- Final combined hull: 265,650 vertices / 96,638 triangles / 10,723,056 bytes.
- Same hull used for the existing shadow upload; optical mesh remains separate.
- Additional GPU buffer count: 0; additional owned textures / SetData calls: 0.
- One measured Release run: total Bolon planning 77 ms, mesh build 1,200 ms.
  These are whole-generator test-process timings, not isolated B4a or GPU timings.

Plan signature:
`22F53C19F98CA8BEA09D96226B3CCC3DEBB4AD993F943F28A5B3BB4C45018373`

Antega's assembled presentation bounds, including bridge and engines, are
34.10 x 17.08 x 99.22 m. It fits level without increasing the slot height.
A separate 34 m-wide / 20 m-high design-envelope check fits at 3 degrees roll but
exceeds 22 m at 5 degrees. Do not confuse that design-envelope result with the
shorter current Antega's roll clearance; there is no collision response yet.

## Review instructions

Verification: Debug and Release builds clean; full Debug suite 917/917 passed
(Game 787, Gameplay 6, UI 41, ObjectDesigner 83). Focused Debug Bolon/H1 interior
checks passed 67/67; Release ambassador-bay checks passed 9/9. Coverage includes
24 Bolon/Red Bolon plans, winding/containment, slot-to-bay and rear-port rays,
fixture mounting/beam handoff, hull-matched bevel materials, resource policy,
the frozen Nova spatial signature, unchanged unreserved plans and current
assembled Antega clearance. `git diff --check` clean.

Visit a Bolon or Red Bolon station using the existing station-cycle/map workflow.
Look for one cold-white rectangular incision, H1e blue beams above and amber below.
`[BolonAmbassadorBay]` reports vessel/face, station-local mouth position, outward
axis, local down, dimensions and signature in Debug output and the system feed.

Check at kilometre range, then approach head-on and obliquely:

- Pure rectangular mouth within a recognizable hexagon; no projecting lip.
- Smaller hull-matched chamfer/outer reveal and the unchanged short throat.
- Four physical face-mounted emitters; distinct blue/amber beams, no crown.
- Immediate broad octagonal chamber, legible floor/ceiling and cold-white light.
- No original hull triangles, bulkhead caps, cracks or backward walls in the route.
- Level entry with Antega, turnaround inside, and exit; remember walls are non-solid.
- Centered floor-level rear access port, shallow bevel, short stub and dark end.
- Existing exterior apertures, vents, utilities and metallic history elsewhere unchanged.

Timo has now given this correction an in-engine visual pass. The accepted 8192-square
shadow baseline and human entrances are untouched. Collision remains deferred.
