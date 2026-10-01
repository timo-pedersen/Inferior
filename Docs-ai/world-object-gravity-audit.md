# W1g — Free-object gravity audit and implementation

> W1g-B audit revision: `b5b5d1d` (2026-10-01).
> W1g-C result: **PASS — rail-coherent free-object gravity implemented and test-verified.**

## Approved dynamics contract

The W1g-B audit established that generated planet, moon, and station rails are internally
consistent under their hierarchical two-body model. W1g-C therefore uses the rail model as the
authority for active free-object translation:

```text
A(object, parent, t)
    = RailAcceleration(parent, t)
    + PointMassAcceleration(parentPosition(t), parentMass, objectPosition)
```

The operation recurses through the hierarchy. For a star-parented object, the star is fixed at
system origin and contributes no parent-rail acceleration. This is a patched-conic contract, not
an all-body N-body field.

`StarSystem` now supplies the authoritative hierarchy operations:

- `GetBodyPosition` and `GetBodyVelocity` recursively compose generated rails;
- `GetRailAcceleration` recursively composes a body's parent acceleration and local two-body
  acceleration;
- `GetStationRailAcceleration` exposes the equivalent represented station acceleration;
- `GetRailCoherentAcceleration` evaluates a free object's selected patched-conic acceleration;
- `SelectDynamicsParent` independently selects the physical parent from Hill-scaled domains;
- `GetStationWorldOffset` and `GetStationPointVelocity` supply oriented release position and
  station-centre velocity plus `omega x r`.

No parallel orbital model was added.

## Dynamics-parent state and transitions

`SpaceSimulation` owns an `OrbitalBody?` dynamics parent for every active free `WorldObject`.
Null denotes the system star. This state is deliberately separate from:

- attachment/relationship parent;
- X-Stop and zero-speed control reference;
- station release frame;
- immutable presentation snapshots.

A station has no modeled mass and is never a dynamics parent. Containers created around a station
inherit `station.OrbitParent`; a star-orbiting station therefore yields the null/star convention.
Missing state uses the same selector from the star root.

Selection begins at the system star and descends to the deepest containing generated body. The
Schmitt band is dimensionless and explicit:

```text
enter child: distance <= 0.90 * HillSphereRadius
retain child: distance <= 1.10 * HillSphereRadius
```

Crossing a boundary changes only the acceleration source identity. World position and velocity are
not transformed, so the transition is kinematically continuous. Tests cover entry, retention,
exit, and preservation of physical state. X-Stop/reference-source changes are separately proven
not to affect world-object trajectories.

## Tick-time and integration rule

The simulation advances `GameClock` before `TickPhysics`, but a pre-step world object still
represents the preceding completed tick. Sampling rails at the already-advanced clock creates a
one-step phase lead and kilometre-scale long-horizon error.

`SpaceSimulation` therefore retains `_worldObjectStateTime` explicitly. Acceleration is evaluated
at that timestamp, then active free objects advance in double precision using semi-implicit Euler:

```text
a = RailCoherentAcceleration(position, dynamicsParent, stateTime)
v += a * dt
p += v * dt
```

The existing universe-space angular/orientation integration is unchanged. A focused regression
computes both current-state-time and advanced-clock predictions and proves the simulation follows
the former.

## Release kinematics

The W1 creation path has a meaningful deterministic offset around each source station. That offset
is now treated as station-local, transformed by the station orientation at creation time, and used
for both position and point velocity:

```text
release velocity = station rail velocity + station omega x oriented offset
```

There is no explicit release/impulse velocity in W1, so none is invented. Attachment/detachment
semantics remain deferred to W1b.

## Verification results

Generated period tests verify moon and station periods directly against `GM/r^2`/Kepler's third
law. Instantaneous acceleration tests cover a planet, moon, station around a planet, and station
around a moon.

A zero-offset particle initialized at an exact generated station rail position and velocity was
integrated against the W1g-C contract:

| Timestep | 10 minutes | 1 hour | 6 hours |
|---|---:|---:|---:|
| 60 Hz | 1.033 m | 6.532 m | 61.287 m |
| 30 Hz | — | — | 123.006 m |

The 60 Hz six-hour result is in the accepted tens-of-metres numerical-error range, and halving the
timestep approximately halves the error. This is qualitatively different from W1g-B's all-body
field probe, which separated by 1.386–5.789 km after six hours despite identical initial state.

Other focused regressions cover station point-velocity inheritance, dynamics-parent transition
continuity, explicit state-time use, X-Stop/reference independence, and existing world-object
snapshot/coherence behavior.

## GravityAt remains separate

`Inferior.Gameplay/SensorData/GravityCalculations.GravityAt` remains an all-body sensor/control
field query. It is intentionally not used by free-object dynamics. Its fixed 1,000 km centre cutoff
is still unresolved, as is the separate zero-speed gravity-angle normalization regression.
Neither was changed in W1g-C.

## In-engine acceptance and deferred work

Still out of scope: Jolt, collision response, W1b attachments, weapons, MATCH TARGET VELOCITY,
dormant long-term propagation, and rendering/interpolation changes.

Timo confirmed W1g-C as a definite in-engine pass on 2026-10-01. Existing free containers remain
in plausible orbital motion near their source stations rather than flying away along the tangent.
