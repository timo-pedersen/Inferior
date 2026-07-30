# Ship Mass and Translational Propulsion

This is the active implementation reference for configured ship mass, engine harmony,
and installed-engine propulsion. Broader rationale and future centre-of-mass design remain in
`Docs/inferior-design-docs-side-track/ship-mass-and-propulsion.md`.

## Authority and ownership

The simulation-owned `Ship` is the live physical assembly. `ShipPropulsion.Resolve(Ship)`
derives propulsion from its installed `EngineInstance` objects and their installation
transforms. Rendering and UI consume `ShipPropulsionSnapshot`; they do not recalculate
mass, force, or acceleration.

## Current configured mass

```text
Ship.Mass
    = Ship.HullMass
    + Ship.ComponentMass
    + sum(installed engine DryMassKg)
```

`ComponentMass` currently contains the existing transitional component contribution.
Default construction installs a 120 MW `PowerReactor`; `Ship.Install` contributes
`MaxPower * 0.00001`, which is 1,200 kg. This contributor is preserved.

Engine mass applies to every installed engine even when its propulsion output is zero.
Cargo capacity metadata does not yet add cargo or container mass.

## Engine aggregation

For every installed engine:

1. Add `EngineDefinition.DryMassKg`.
2. Resolve its operational factor as `1 - DamageFraction`.
3. Resolve the instance's selected harmony through its definition-owned quadratic curve.
4. Derive forward, reverse, lateral, lift, and rotational maxima from the harmony-scaled
   maximum forward thrust and authored directional fractions.
5. Transform the engine-local force through installation orientation.
6. Apply explicit hull-owned designed-single-engine efficiencies where authored.

There is no hull-ID propulsion branch and no engine-count multiplied by a global force.
Rotational torque drives assisted angular acceleration as described below.

For selected harmony `h` in `1..HarmonyCount`, `x = (h - 1) / (HarmonyCount - 1)`
and `curve = x^2`. Both thrust multiplier and speed ceiling interpolate their authored
minimum/maximum endpoints with that same curve. Harmony count changes granularity only.
Rotational torque uses the positive thrust multiplier; rotation does not consume the
shared translational envelope.

## Translation calculation

`PlayerInput` remains the immutable command path. `EngineTranslationCommand` clamps
longitudinal, lateral, and vertical axes independently to `[-1, 1]`. The shared envelope
uses `usage = sqrt(f^2 + l^2 + v^2)` and divides all three axes by usage when it exceeds
one. Every installed engine evaluates that same allocated command against its own harmony
and fractions; simultaneous directions therefore compete for one finite normalized budget.

```text
selected engine harmony
  -> quadratic thrust multiplier and speed ceiling
  -> per-engine directional maxima
  -> hull layout efficiency
  -> shared translation allocation
  -> harmony ceiling taper for forward/reverse
  -> applied force
  -> acceleration = applied force / current mass
  -> velocity integration
```

The existing Newtonian scroll/gear command now selects engine harmony; there is no global
Newtonian speed table. Each engine owns its selected step and authored endpoints. The ship
speed ceiling is the lowest operational installed-engine ceiling. Reverse uses its authored
fraction and retains the separate reverse ceiling ratio. Existing approach-to-ceiling
tapering scales longitudinal force rather than replacing force/mass physics.

`R` is positive ship-local vertical using lateral-strength output; `F` is negative and
also uses lateral strength. `Space` requests the same positive axis using the stronger
lift fraction. It replaces that direction's channel maximum rather than adding an axis,
so `R + Space` remains one full lift command. Negative vertical never uses lift strength.

Atmospheric translation uses the same engine aggregation. Atmospheric Flight Assist may
add positive vertical force up to the available lateral authority. Gravity,
aerodynamic lift, and drag remain separate external forces.

System Newtonian Flight Assist is default-on and toggled with `V`. Its first
implementation preserves the ship-local forward component of reference-relative
velocity and damps only ship-local lateral and vertical velocity toward zero. The
assist force is clamped per axis by current installed-engine maneuvering authority:
sideways and downward correction use lateral thrust, while upward correction uses the
stronger lift thrust. The current tuning factor is 1.0. X-Stop remains separate and
takes precedence when active. Flight Assist publishes `Flight.Assist`,
`Flight.AssistForce`, and `Flight.AssistAcceleration`; applied force/acceleration
telemetry is throttled to roughly 250 ms for pilot-facing instruments.

## Provisional configured results at maximum harmony

All masses include the current 1,200 kg default reactor contribution.

| Ship | Engines | Mass | Forward accel | Lateral accel | Lift accel | Empty hover estimate |
|---|---:|---:|---:|---:|---:|---:|
| Aries | 2 Mule | 78,000 kg | 20.00 m/s2 | 10.00 m/s2 | 15.00 m/s2 | 1.529 g |
| Cosmo | 1 Needle | 14,850 kg | 47.42 m/s2 | 23.71 m/s2 | 35.57 m/s2 | 3.627 g |
| Asterisk | 1 Mule | 15,600 kg | 37.50 m/s2 | 18.75 m/s2 | 28.125 m/s2 | 2.868 g |
| Beren | 4 Needle | 187,800 kg | 20.00 m/s2 | 10.00 m/s2 | 15.00 m/s2 | 1.529 g |
| Antega | 4 Atlas | 3,585,200 kg | 20.00 m/s2 | 5.00 m/s2 | 10.00 m/s2 | 1.020 g |

Asterisk and Cosmo efficiencies are provisionally 0.75 forward, 0.75 maneuvering, and
0.60 rotation. All current other hulls use 1.0.

## Snapshot and diagnostics

`ShipSnapshot.Propulsion` publishes current, hull, component, and installed-engine mass;
installed and operational engine counts; available force and torque; and the latest
applied ship-local force and acceleration. It also publishes selected harmony detail,
curve/multiplier, engine-derived speed ceiling, directional maxima, command usage and
allocation, and diagnostic lift/hover estimates. `SafeLandingGravityG` is only the maximum
hover estimate divided by a temporary 1.25 reserve; it is not authored or legal capability.
The existing `F2` toggle shows these values for in-engine acceptance.

## Assisted rotation

Coordinate convention:

```text
local X = width axis  = pitch angular velocity
local Y = height axis = yaw angular velocity
local Z = length axis = roll angular velocity
forward = local -Z
```

`ShipPresentationBoundsCalculator.GetConfiguredBounds` supplies stable gameplay-owned
configured bounds from hull semantic geometry plus transformed installed engine and
cockpit geometry. The result is cached per ship and invalidated by engine-mount
configuration revision or cockpit installation identity. Simulation does not query
rendering or GPU state.

For current mass `M`, width `W`, height `H`, and length `L`:

```text
Pitch inertia Ixx = M * (H^2 + L^2) / 12
Yaw inertia   Iyy = M * (W^2 + L^2) / 12
Roll inertia  Izz = M * (W^2 + H^2) / 12

axis angular acceleration = effective installed rotational torque / axis inertia
```

These are three independent scalar cuboid approximations, not a full inertia tensor.
Component mass positions, centre of mass, gyroscopic coupling, and off-diagonal terms are
not represented.

`PlayerInput` pitch, yaw, and roll fields are normalized assisted-rate commands. Mouse
deltas are normalized at `ShipInputMapper`; mouse vertical drives pitch, mouse horizontal
drives roll, and `A/D` drives digital yaw. Mouse-right produces positive roll command,
`A` produces positive (left) yaw, and `D` produces negative (right) yaw. Because
ship-forward is local `-Z`, positive roll input maps to negative local-Z angular velocity.
Assisted target limits remain:

| Axis | Maximum target rate |
|---|---:|
| Pitch up | 1.4 rad/s |
| Pitch down | 1.0 rad/s |
| Yaw | 1.0 rad/s |
| Roll | 1.5 rad/s |

Each simulation tick moves ship-local angular velocity toward the target independently on
each axis by at most `available angular acceleration * dt`. Returning input to zero uses
the same torque to brake without overshoot; there is no passive angular damping.
Orientation integrates an axis-angle quaternion from the local angular-velocity vector
using `Orientation * localDelta`, then normalizes.

Configured provisional results at maximum harmony after activating and retuning engine torque:

| Ship | W x H x L | Effective torque | Ixx / Iyy / Izz (kg m2) | Angular accel pitch / yaw / roll | Time to max pitch / yaw / roll |
|---|---:|---:|---:|---:|---:|
| Aries | 12.04 x 5.02 x 16.07 m | 1.20 MN m | 1.843e6 / 2.621e6 / 1.106e6 | 0.651 / 0.458 / 1.085 rad/s2 | 2.15 / 2.18 / 1.38 s |
| Asterisk | 5.31 x 3.34 x 8.71 m | 0.36 MN m | 1.131e5 / 1.353e5 / 5.116e4 | 3.182 / 2.661 / 7.037 rad/s2 | 0.44 / 0.38 / 0.21 s |
| Beren | 19.10 x 5.73 x 27.00 m | 4.20 MN m | 1.192e7 / 1.712e7 / 6.223e6 | 0.352 / 0.245 / 0.675 rad/s2 | 3.97 / 4.08 / 2.22 s |
| Antega | 34.10 x 17.08 x 99.22 m | 360.00 MN m | 3.028e9 / 3.289e9 / 4.346e8 | 0.119 / 0.109 / 0.828 rad/s2 | 11.78 / 9.14 / 1.81 s |

Ship-local angular velocity is simulation-owned and published in
`ShipSnapshot.Rotation`. Explicit teleports, station relocation, and surface collision
velocity resets clear angular velocity. Ship cycling preserves instantaneous angular
velocity. Camera changes do not modify it. Rotational assisted semantics remain active;
flight-assist-OFF torque control is deferred.

## Deferred

- cargo and container mass;
- derived centre of mass and rotation pivot changes;
- full inertia tensors and per-component inertia;
- asymmetric failure torque;
- flight-assist-OFF direct torque controls;
- engine power, fuel, and thermal limits;
- fuel compatibility and consumption;
- new planetary-gravity, automatic-hover, and gravity-compensation behavior;
- rotation sharing the translational envelope;
- detailed engine operational/damage states;
- inertial-dampener acceleration limits.
