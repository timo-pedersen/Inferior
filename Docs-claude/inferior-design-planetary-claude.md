# Inferior — Planetary Flight Design Reference

> Compressed reference for Claude and Claude Code.
> Covers atmospheric and surface-proximity flight: forces, modes, and rendering
> changes that apply when the ship is near a planet or moon.

---

## Architecture: FlightMode, not a GameState

Atmospheric flight is **not a separate GameState**. It is a `FlightMode` within
`SystemSpaceState`. The sim thread runs continuously; the same ship, position,
velocity, and camera are used throughout. `FlightMode` controls which forces the
sim applies and which render passes are active.

```csharp
public enum FlightMode { Space, Atmosphere }
```

`FlightMode` lives on `SpaceSimulation` (or `SystemSpaceState` — wherever the sim
tick is driven). Transition is a threshold check each tick. No state machine
involvement. No cross-boundary data migration.

### Transition triggers

| Body type | Enter `FlightMode.Atmosphere` | Exit `FlightMode.Atmosphere` |
|---|---|---|
| Body with atmosphere | Altitude < 80% of `AtmosphereCeilingAltitude` | Altitude > `AtmosphereCeilingAltitude` |
| Airless body | Distance from surface < 50 000 m | Distance from surface > 50 000 m |

`AtmosphereCeilingAltitude` is a property on `OrbitalBody`; 0 for airless bodies.
The 50 km airless threshold is a placeholder — to be tuned after test play.

---

## OrbitalBody — new properties required

| Property | Type | Notes |
|---|---|---|
| `RotationPeriod` | double (s) | Seconds per full rotation; 0 = tidally locked |
| `RotationAxis` | DVec3 | Unit vector; world-space axis of rotation |
| `AtmosphereCeilingAltitude` | double (m) | Altitude of atmosphere top; 0 for airless bodies |
| `AtmosphereSurfaceDensity` | double | Relative to reference (1.0 = Earth sea level); 0 for airless |
| `AtmosphereScaleHeight` | double (m) | Altitude at which density falls to 1/e of surface value |

Atmospheric density at altitude h:

```
density(h) = AtmosphereSurfaceDensity × exp(-h / AtmosphereScaleHeight)
```

---

## Ship hull — new aerodynamic properties

Per-hull-class parameters (not components):

| Property | Type | Notes |
|---|---|---|
| `AerodynamicLift` | double | Upward force coefficient (ship-relative up) per unit density per (m/s)² of forward speed |
| `AerodynamicBrakeFront` | double | Drag coefficient for motion in ship-forward/backward direction |
| `AerodynamicBrakeLateral` | double | Drag coefficient for motion in ship-up/down/left/right directions; always ≥ 2× BrakeFront |

Hulls with no atmospheric design intent have all three at 0.

---

## Reference frame and zero speed

In `FlightMode.Atmosphere`, speed displayed on instruments is relative to the ground
point directly below the ship. Zero speed = the galactic velocity of that point.

```
groundVelocity = planet.OrbitalVelocity(simTime)
               + ω × (shipPosition − planetCentre)

ω = (2π / RotationPeriod) × RotationAxis
```

Surface speed varies with latitude — maximum at the equator, zero at the poles.
This is not an approximation; it is the correct physical consequence of planet rotation.
`OrbitalBody` must expose a method to compute its current galactic velocity at `simTime`.

---

## Gravity and downward thrust

Planet gravity is calculated from planet mass and ship altitude via the existing
`Environment` / `GravitySensor` infrastructure. No new gravity model needed.

Engine `MaxDownThrust` (newtons) opposes gravity. Net acceleration:

```
a_vertical = (MaxDownThrust − ship_mass × g_planet) / ship_mass
```

Positive = rising, negative = falling.

**Tilt:** only the component of downward thrust opposing the gravity vector is
effective. At 30° pitch the hover authority is `cos(30°) ≈ 87%` of maximum.
Managing orientation for stable hover is an intentional skill element — no
auto-levelling is implemented here.

Downward thrust consumes metal rods at approximately 10% of the rate equivalent
forward thrust would consume. Tune per feel; this is a rough-order guideline.

---

## Flight Assist

Battery-backed module (ship computer; no power bus draw; no metal rod consumption).
Toggled by **V**. Has a `StartupTimer` before it becomes active, analogous to the
shield startup delay.

| Behaviour | Detail |
|---|---|
| Active | Continuously commands upward thrust (ship-relative) equal to measured gravity at ship position |
| Thrust limit | Capped at engine `MaxDownThrust`; cannot compensate more than the engine can supply |
| Tilt | No auto-levelling — compensation reduces with tilt. Player is responsible for orientation. |
| Power | Battery-backed; remains active through full bus failure |
| Auto-levelling | Deferred to future iteration |

Flight Assist is not autopilot. It holds altitude when the ship is level; nothing more.

---

## Aerodynamics

Active only in `FlightMode.Atmosphere` when `AtmosphereSurfaceDensity > 0`.

### Atmospheric drag

Quadratic in speed, split by axis in ship-local space:

```
F_drag_front   = AerodynamicBrakeFront   × density(h) × v_forward²   (opposes v_forward)
F_drag_lateral = AerodynamicBrakeLateral × density(h) × v_lateral²   (opposes v_lateral)
```

`v_lateral` = magnitude of velocity in the ship's up/down/left/right plane.
Terminal velocity emerges from the balance of thrust and drag. No hard cap.

### Aerodynamic lift

```
F_lift = AerodynamicLift × density(h) × v_forward²
```

Direction: ship-relative up. Inverted flight generates downward force. Zero lift
when stationary.

---

## Slipstream mode

**Key: G**

### Lore

A hyperspace cocoon is generated around the hull. Atmosphere molecules along the
ship's leading edges are compactified into a hyperspace plane, then un-compactified
at the rear. Air behaves as though nothing passed — zero drag. Slipstream and shields
cannot coexist: both are hyperspace field emitters occupying the same spatial envelope.
(V-band proximity provides a secondary lore justification.)

### Constraints

| Constraint | Value |
|---|---|
| Shields active | Slipstream unavailable |
| Atmospheric density | Must be ≥ 0.05 (5% of reference pressure); below this the cocoon cannot form |
| Minimum entry speed | None — slipstream can engage at any speed and accelerates to minimum |

### Activation sequence

1. Player presses G
2. Slipstream module charges (startup timer; system log message on completion)
3. Cocoon forms; ship accelerates automatically to minimum slipstream speed in current forward direction
4. Atmospheric drag drops to zero; lift also drops to zero

### Speed in slipstream

Slipstream operates in a defined speed window. Placeholder values — tune for feel:

| Parameter | Placeholder |
|---|---|
| Minimum slipstream speed | 1 000 m/s |
| Maximum slipstream speed | 10 000 m/s |

Normal engine thrust does not apply in slipstream. Speed within the window is managed
by the slipstream system. Higher altitude may permit higher maximum speed — to be tuned.

### Turning in slipstream

Angular authority is heavily reduced. All three axes (pitch/yaw/roll) are available
but slow. Gyro component improves turning rate in slipstream.

### Exiting slipstream

Press G to exit. Full atmospheric drag resumes immediately.

**High-speed exit tumble:** exiting at or near maximum slipstream speed imposes a violent
aerodynamic event — asymmetric drag forces produce large uncontrolled torques until
speed falls to a range where the ship can recover. This is **explicit design**, not
an emergent edge case. The pilot must manage slipstream entry and exit speed deliberately.
Gyro quality determines recovery time.

---

## Shields in atmosphere

Shield capacitor depletion rate scales with atmospheric density:

```
depletionRate ∝ density(h) × currentCapacitor
```

At density 1.0 (reference pressure) the capacitor empties in seconds.
Bus draw from the shield converter increases as capacitor depletes (consistent with
existing shield model). Shield generator heat rises sharply under atmospheric depletion —
at high rates the generator approaches thermal limits quickly.

Depletion is visible on the shield capacitor gauge before it becomes critical.
Recommended procedure: deactivate shields before deep atmospheric entry.

---

## Sky rendering

In `FlightMode.Atmosphere`, the sky sphere renders visible celestial bodies using
their current orbital positions from `Environment`:

- Parent star(s) — disc size and colour from star type
- Other planets — angular-size-scaled disc
- Moons of current body — visible on facing hemisphere
- Parent planet (if current body is a moon) — large disc when on facing hemisphere

Positions are projected from galactic `DVec3` coordinates to sky azimuth/elevation
using ship position and planet rotation state. Rise and set are natural consequences
of the projection — no special handling.

---

## Terrain

Terrain rendering is **deferred**. Planets are spheres. Surface = planet_radius from
planet centre. This is the collision boundary.

Future terrain will be a seeded procedural height function. That system has its own
design brief.

---

## Collision detection

```
if (distanceFromPlanetCentre < planet_radius + ship_collision_radius)
    → abrupt stop; no damage
```

Development placeholder. No crash, no damage. When terrain is implemented, the
sphere check is replaced by a height-function query.

The terrain height function must be callable from the sim thread, not just the renderer.

---

## Landing instrument

**Deferred.** Requires a dedicated design doc with visual sketches. Planned location:
cockpit rail central panel, tab "Landing".

---

## Key bindings (additions)

| Key | Action |
|---|---|
| V | Toggle Flight Assist |
| G | Toggle Slipstream |

---

## Out of scope for this brief

| Item | Status |
|---|---|
| Auto-levelling (Flight Assist evolution) | Future iteration |
| Full autopilot | Not designed |
| `PlanetApproach` transition state | Removed from design |
| `Surface` (on-foot) | Future GameState |
| Terrain height map + LOD rendering | Separate brief |
| Landing radar instrument | Separate design doc |
| Ocean rendering | Deferred |
| Atmospheric visual effects (clouds, haze, re-entry glow) | Not yet designed |
