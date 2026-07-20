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
Rotational torque is resolved and published but does not alter the existing fixed rotation
rates in this stage.

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

## Deferred

- cargo and container mass;
- derived centre of mass and rotation pivot changes;
- inertia tensors and angular velocity;
- torque-driven rotation and asymmetric failure torque;
- engine power, fuel, and thermal limits;
- detailed engine operational/damage states;
- inertial-dampener acceleration limits.
