# Ship Mass and Translational Propulsion

This is the active implementation reference for configured ship mass and Stage-1
installed-engine propulsion. Broader rationale and future centre-of-mass design remain in
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
3. Transform engine-local forward (`-Z`) through `EngineGeometryTransform`.
4. Add transformed `ForwardThrustN`, scalar `ManeuveringThrustN`, and scalar
   `RotationalTorqueNm`, multiplied by the operational factor.
5. Apply explicit hull-owned designed-single-engine efficiencies where authored.

There is no hull-ID propulsion branch and no engine-count multiplied by a global force.
Rotational torque drives assisted angular acceleration as described below.

## Translation calculation

`PlayerInput` remains the immutable command path. Translation axes are clamped to
`[-1, 1]`, then the combined command magnitude is clamped to 1.

```text
available force
  -> hull layout efficiency
  -> normalized player command
  -> gear ceiling taper for forward/reverse
  -> applied force
  -> acceleration = applied force / current mass
  -> velocity integration
```

All System Newtonian gears use this calculation. Gear 1 retains its speed ceiling but has
no fixed-acceleration bypass. Reverse retains the existing separate speed ceiling
(`ReverseSpeedRatio`) and full low-speed forward-engine authority. Lateral and vertical
commands use the aggregated maneuvering authority.

The existing `R` binding is positive ship-local vertical (up); `F` is negative. `Space`
now commands the same full positive direction as `R`, and `R + Space` remains clamped to
one full command.

Atmospheric translation uses the same engine aggregation. Atmospheric Flight Assist may
add positive vertical force up to the available maneuvering authority. Gravity,
aerodynamic lift, and drag remain separate external forces.

## Provisional configured results

All masses include the current 1,200 kg default reactor contribution.

| Ship | Engines | Current mass | Engine mass | Forward force | Maneuver force | Forward accel | Maneuver accel |
|---|---:|---:|---:|---:|---:|---:|---:|
| Aries | 2 Mule | 78,000 kg | 4,800 kg | 312,000 N | 156,000 N | 4.00 m/s2 | 2.00 m/s2 |
| Asterisk | 1 Mule | 15,600 kg | 2,400 kg | 117,000 N | 58,500 N | 7.50 m/s2 | 3.75 m/s2 |
| Beren | 4 Needle | 187,800 kg | 6,600 kg | 751,200 N | 375,600 N | 4.00 m/s2 | 2.00 m/s2 |
| Antega | 4 Atlas | 3,585,200 kg | 384,000 kg | 14,340,800 N | 3,585,200 N | 4.00 m/s2 | 1.00 m/s2 |

Asterisk efficiencies are provisionally 0.75 forward, 0.75 maneuvering, and 0.60
rotation. All current other hulls use 1.0.

## Snapshot and diagnostics

`ShipSnapshot.Propulsion` publishes current, hull, component, and installed-engine mass;
installed and operational engine counts; available force and torque; and the latest
applied ship-local force and acceleration. The existing `F2` ship-module debug toggle
shows these values in a temporary HUD panel for in-engine acceptance.

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
drives roll, and `Q/E` drives digital yaw. Mouse-right produces positive roll command,
`Q` produces positive (left) yaw, and `E` produces negative (right) yaw. Because
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

Configured provisional results after activating and retuning engine torque:

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
- detailed engine operational/damage states;
- inertial-dampener acceleration limits.
