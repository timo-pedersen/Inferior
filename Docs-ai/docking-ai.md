# Inferior — Docking System Design

> Design reference for AI.
> Covers docking philosophy, pad types, the docking instrument, navigation,
> landing detection, and development steps.

---

## Design philosophy

Docking is a skill, not a gate. The game never prevents you from attempting a dock —
it just makes some pads harder than others. Rewards follow naturally: frontier outposts
with easy external pads have cheap goods; the hard shelf-dock in a contested system
has the military-grade component you cannot get elsewhere. The player decides when
they are ready.

**No auto-alignment on landing.** The ship is a persistent physical object. It stays
at the angle and position it was landed. A skilled pilot parks it straight in one
clean move. A nervous one wobbles in at 12 degrees and calls it good enough. That
difference is visible to anyone on the station concourse and is part of the game's
character.

**No rotating station ports.** Timing a rotating slot is waiting with consequences,
not skill. There is nothing to learn that makes you genuinely better at it. Docking
skill in Inferior is spatial judgment and control authority — things that improve with
practice and transfer between dock types.

**No shields near or inside stations.** Station will not open docking bay with shields
active. Collisions with station geometry damage hull panels. An aggressive act with
shields up near a station has consequences.

---

## Docking types

Five distinct docking experiences, mapping naturally to station size class and role.

| Type | Character | Difficulty | Notes |
|------|-----------|------------|-------|
| **External pad** | Exposed on hull, open approach, no overhead clearance issue | Easy | Outpost class. Frontier feel. Vulnerable while docked. |
| **Open bay** | Rectangular or octagonal opening, open interior, pad at far end | Medium | Ceiling and wall clearance. Speed and approach angle matter. |
| **Shelf/lateral** | Precision parallel parking into a recessed slot | Hard | Lateral translation skill. Completely different from approach-and-land. |
| **Tunnel** | Narrow approach corridor with turns before reaching interior pad | Hard | Commit on entry — no abort once inside. Security-conscious stations. |
| **Cathedral interior** | Fly through large portal, navigate interior, land at designated pad | Exceptional | Megastation enclosed archetype. Multiple walls of bays, interior lighting, small interior structures. The memorable experience. |

Not all types need to be implemented at once. External pad and open bay are the
initial targets. Shelf, tunnel, and cathedral are later additions.

---

## Pad assignment by station size

Two parallel systems, both coexisting:

**Self-select (small stations):** One or two pads, no traffic management needed.
Player targets a pad directly and lands on it. No ATC involvement.
Applies to: Outpost, small Station class.

**ATC assignment (large stations):** Station assigns a pad. ATC chatter, pad number
appears on instruments. Player cannot freely choose. Prevents chaos in high-traffic
stations with many simultaneous approaches.
Applies to: Port, Megastation.

The boundary between these two modes is a design decision to be made when ATC is
implemented. Station size class is the obvious trigger.

---

## Navigation to pad

Two separate instrument concerns at two different scales:

**Main direction ball** — navigation scale. Shows direction to targeted station.
Relevant from thousands of km out. When within ~2000m of a station, the station
is visually present and the dirball is no longer needed for it. If the player has
nav-targeted a different station while near another, the dirball shows the targeted
one — this is correct and the resulting confusion is self-inflicted.

**Target trackball** — docking scale. Shows direction and distance to the targeted
or assigned landing pad. Activates when a pad is selected or assigned. Relevant
inside ~2000m. This is not a fallback for the station direction — it is a separate
concern.

These must never be combined into a single display. They serve different distance
ranges and cognitive modes.

---

## The docking instrument

A dedicated UI control exists in the cockpit rail central panel. Not yet wired to
anything. It activates when within docking range of a targeted or assigned pad.

The instrument encodes four parameters simultaneously:

| Parameter | Encoding |
|-----------|----------|
| **Lateral offset** (left/right position over pad) | Ship circle position relative to pad circle |
| **Heading alignment** (nose direction vs pad axis) | Crosshair arm direction on ship circle |
| **Height above pad** | Ship circle size (larger = higher) |
| **Pitch/roll deviation** | Circle shape: round = face-on; ellipse = tilted; line = 90° off |

Goal: match ship crosshair circle to pad crosshair circle in all four parameters
simultaneously. When all four match, the ship is aligned, centred, at correct height,
and face-on to the pad. Set down.

**Direction encoding:**
- Ship crosshair: top arm has an arrow indicating nose direction
- Pad crosshair: arrows on both top and bottom arms (either end of pad is a valid
  approach heading — player matches whichever arrow their nose points toward)

**Edge cases:**
- Upside down: ship circle greys out. Unambiguous signal that orientation is
  fundamentally wrong before the player tries to interpret position.
- 90 degrees off (circle = line): position information along one axis is lost.
  This is acceptable — no pilot should be approaching at exactly 90 degrees to a pad,
  and if they are, the grey-out of the ellipse collapse is signal enough to reorient.

---

## Landing detection

Landing is detected when all thresholds are met simultaneously:

| Condition | Threshold | Notes |
|-----------|-----------|-------|
| Height above pad | < landing threshold (TBD, ~1–2m) | Ship geometry touching pad surface |
| Lateral offset | Within pad bounds | Ship footprint over pad area |
| Vertical velocity | Below maximum hard-landing threshold | Avoids instant-dock on high-speed collision |
| Heading deviation | < ±15° from pad axis | Either direction acceptable |
| Pitch/roll deviation | < ±15° from pad plane | |
| Shields | Down | Hard requirement — cannot land with shields active |

**No auto-correction on landing.** Whatever position and angle the ship is at when
thresholds are met is the landed position. This is stored as the ship's docked
orientation in the station. A precise pilot parks it straight. An imprecise one
does not.

Exceeding the hard-landing velocity threshold: the ship lands but takes panel damage
proportional to impact force. A fast enough impact destroys panels outright. The
same physics that applies to station collision applies to pad landing.

---

## Docked state

Deferred — not in initial implementation scope. Notes for when it is designed:

- `Docked` is a distinct `GameState` (already in the planned enum)
- Services available depend on `StationServices` flags on the `StationModel`
- Ship persists at its landed orientation and position within the station
- Shields must be down to enter docked state (enforced by landing detection)
- Undocking: player lifts off manually. No auto-launch sequence. Same skill in reverse.

---

## What already exists

| Element | Status |
|---------|--------|
| `LandingPad[]` on `StationModel` | ✓ Exists — positions from `IsDocking` ports, no visual |
| `HangarBay[]` on `StationModel` | ✓ Exists — no visual |
| `TargetingSystem` | ✓ Done — targets stations; pad targeting not yet extended |
| `DirectionBall` UI control | ✓ Done — shows direction to nav target |
| Docking instrument UI control | ✓ Exists in cockpit rail — not yet wired |
| Station hull markings (name, bay numbers) | ✓ Done — stencil font, rendered on hull |
| Pad geometry (visual) | ✗ Not implemented |
| Pad as targetable object | ✗ Not implemented |
| Landing detection | ✗ Not implemented |
| ATC / pad assignment | ✗ Not implemented |
| Docked game state | ✗ Not implemented |

---

## Development steps

Ordered for incremental, testable progress. Each step produces something
observable before the next begins.

### Step 1 — Pad geometry

Generate a visual landing pad surface at each `LandingPad` position on the station
mesh. External pad (outpost type) first.

Pad surface elements:
- Flat rectangular or circular pad surface, flush with or slightly proud of the
  module face
- Painted markings: chevrons or circle, bay number stencilled using existing
  stencil font system
- Animated lights at pad corners: use existing `StationLightInfo` / `GlowType`
  system; slow pulse pattern; amber or white

No physics or interaction at this step. Just geometry. Test: fly to a station,
see the pad visually.

### Step 2 — Pad as targetable object

Extend `TargetingSystem` to include `LandingPad` instances as targetable objects,
distinct from the station itself.

When a pad is targeted:
- HUD brackets appear on the pad (same system as station targeting)
- Target trackball shows direction and distance to pad
- Label shows pad id / bay number and distance in metres

Test: target a pad, fly toward it, confirm trackball and distance update correctly.

### Step 3 — Docking instrument goes live

Wire the existing docking instrument UI control to the targeted pad.

Instrument activates when:
- A pad is targeted or assigned
- Ship is within docking activation range (suggest 500m, tunable)

Four parameters fed from simulation:
- Pad-relative ship position (lateral offset, longitudinal offset)
- Height above pad surface
- Ship heading vs pad axis
- Ship pitch/roll vs pad plane

Publish these to `DataBus` from a new `DockingApproachSensor` or equivalent.
The instrument subscribes to these topics.

Test: approach a pad slowly, observe all four parameters responding correctly.
Verify circle size changes with height. Verify ellipse deformation with pitch.
Verify arrow alignment with heading.

### Step 4 — Landing detection

Implement landing threshold checks in the simulation loop when a pad is targeted
and the ship is within landing range.

On detection:
- Publish a `Docking.LandingDetected` message to `DataBus`
- Log entry in captain's log
- System console message: "Landed — [pad id]" with heading deviation noted
- No game state change yet

No auto-alignment. Ship position and orientation are simply recorded as-is.

Test: approach pad carefully, meet all thresholds, confirm detection fires.
Test a fast approach: confirm impact damage occurs above velocity threshold.
Test with shields up: confirm landing is rejected.

### Step 5 — Docked game state

Implement the `Docked` `GameState`. Design separately when Step 4 is stable.
Out of scope for this document.

---

## Pad size classes

Two pad sizes exist, matching the `PadSizeClass` enum already on `StationModel`:

| Class | Ships | Notes |
|-------|-------|-------|
| `Small` | Small ships, shuttles | Compact pad, tight clearance |
| `Large` | Medium and large ships | Full-size pad, wider approach corridor |

**Capital ships do not land.** They are too large for any pad. Capital ships requiring
repair or service use a dedicated open scaffold structure — an external docking frame
the ship flies into and is held by. This is a distinct facility type, not a pad.
Design of the scaffold structure is deferred.

A ship attempting to land on a pad it is too large for should be rejected — either
by the station (ATC refuses assignment) or by landing detection (footprint exceeds
pad bounds). A small ship can always use a large pad if one is available.

---

## Open design questions

| Question | Status |
|----------|--------|
| Exact landing threshold values (height, velocity, angle) | To be tuned in Step 4 |
| ATC system design — how assignments are communicated | Deferred to later |
| Interior of open bay / tunnel / cathedral dock types | Deferred — external pad first |
| Shelf/lateral dock — does detection need a different orientation model? | Deferred |
| Pad size classes | **Decided** — see below |
| Undocking procedure | Deferred with docked state |
| Docking fees / economy integration | Deferred |
