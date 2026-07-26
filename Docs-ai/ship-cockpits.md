# Ship Cockpits

This doc is aimed to replace current implementation, and is now the
main authority on cockpits and cockpit mounts.

## Purpose

A cockpit is a physical ship module that defines where and how a pilot operates a ship.

A cockpit is not a camera offset, UI mode, or special case attached to a hull.

The cockpit exists as a real physical component of the ship. Its placement, geometry,
and surrounding hull determine the pilot's perspective.

---

# Design goals

Cockpits should:

- make ships feel physically distinct;
- allow different ship layouts;
- support future cockpit replacement;
- support different visual styles without special-case ship code;
- provide a foundation for future command (via topic on command bus), damage, and system integration.

Cockpits should not:

- force all ships into aircraft-like layouts;
- define artificial viewing directions;
- require rendered interior spaces;
- become a multiplayer crew system.

---

# Ownership model

Cockpit ownership follows the same pattern as engines: a hull provides a mount, a mount is
filled by an installed module, and the installed module carries its own runtime state.

```
HullDefinition
    |
    +-- CockpitMount
            |
            +-- InstalledCockpit
                    |
                    +-- Runtime state
```

The hull provides a compatible mounting location(s) (the socket). The installed cockpit provides
the physical cockpit (the plug). These are not the same location in space — see **Transforms**, below.

---

# Cockpit mounts

A cockpit mount defines the physical interface between hull and cockpit module. A mount provides:

- mount class;
- ship-local transform;
- socket dimensions;
- allowed orientation(s);
- installation direction (top, bottom, starboard etc).

The mount is part of the hull definition. The cockpit module must provide a compatible plug.
This is the same philosophy as engine mounts.

## Mount classes

Compatibility uses mount size classes:

```
C1
C2
C3
C4
C5
```

These are compatibility standards, not fixed size categories. A mount class does not imply:

- ship size;
- civilian/military role;
- performance level.

The installed cockpit determines the actual characteristics.

Example:

```
Aries hull:
    Cockpit mount: C2
Installed cockpit:
    Aries civilian canopy cockpit
```

A different C2 cockpit could instead be:

- armoured military command pod;
- industrial cockpit;
- luxury observation cockpit.

## Socket dimensions by class

These figures define the plug interface — the socket a cockpit module must physically fit
into — not the canopy size. The canopy, framing, and external housing can extend well beyond
the socket footprint.

| Class | Approx socket size | Depth |
|-------|--------------------|-------|
| C1    | 1 × 1 m            | 1 m   |
| C2    | 1.5 × 1.5 m        | 1 m   |
| C3    | 2 × 2 m            | 1 m   |
| C4    | 3 × 4 m            | 1.5 m |
| C5    | 4 × 6 m            | 2 m   |

---

# Transforms

The mount and the installed cockpit are separate things with separate transforms.
A single "cockpit transform" is too simple once cockpits can extend above, below,
or to the side of their mount point.

```
Ship
 |
 +-- CockpitMountTransform (ship-local, provides a neutral view position and a view direction normal)
          |
          +-- CockpitInstallationTransform (relative to mount)
                    |
                    +-- CanopyTransform
                    |
                    +-- PilotTransform
                    |
                    +-- CameraTransform
```

The mount is the socket, penetrating the hull at a fixed ship-local location. The cockpit is the
thing installed in that socket — its canopy and pilot volume are positioned relative to the mount,
and may extend well away from it.

**Top-mounted cockpit** — the mount penetrates the hull; the canopy and pilot volume sit above it:

```
          canopy (1 meter tall)
          ______
Top      |      |
hull ====|_    _|=======
          |    |
          |____|  mount


Pilot body: lower part of body inside hull / mount, head and shoulders above hull, inside canopy.
```



**Bottom-mounted (underslung) cockpit** — the entire pilot volume hangs below the hull:

```

          mount
          ____
bottom   |    |
hull====_|    |_======
       |        |
       |  Pilot |
       |________|
         canopy (2 meters tall, all of pilot fits sitting)

```

Without separate mount/installation transforms, layouts like these become impossible without hacks.

## Camera invariant

The cockpit camera is a physical camera mounted inside the cockpit. It is not:

- a camera offset;
- a free external camera;
- a special ship view mode.

The camera transform is derived through the chain above — ultimately from the cockpit
module, by way of the mount. The camera inherits ship movement and orientation.


# Mount facing and installation orientation

Cockpit placement has two distinct orientation concepts.

## Mount facing

The cockpit mount's ship-local transform determines the socket's position and
outward-facing direction.

A mount may face:

- forward;
- aft;
- upward;
- downward;
- port;
- starboard.

This determines whether an installed cockpit is front-mounted, top-mounted, underslung, or side-mounted.

A cockpit definition may declare a preferred mount-facing direction. This guides ship design but does not make
other physically compatible placements invalid. A cockpit designed for a top-facing mount may therefore
be installed beneath a ship, leaving the pilot inverted, if the designer accepts the result.

## Installation rotation

Installation rotation describes rotation of the cockpit plug around the mount's outward axis.

- C1, C2, and C3 use square sockets and permit four orientations at 90-degree intervals.
- C4 and C5 use rectangular sockets and permit only their keyed forward orientation.

Preferred mount facing and allowed installation rotation are independent properties.

---

## Preferred orientation on ship

Cockpit modules have a preferred installation orientation. Examples:

- Under the ship;
- top of ship;
- Starboard;
- forward-facing fighter cockpit;
- upward observation cockpit;
- downward industrial inspection cockpit.

The preferred orientation guides ship designers but does not prevent unusual installations if physically valid.
A "top" cockpit may intentionally be installed upside down or sideways.
This preserves the "if you want to fly upside down, go ahead" philosophy.

---

# Visibility

The ship geometry determines what the pilot can see. The cockpit does not define explicit viewports. Avoid concepts such as:

- front window;
- left window;
- right window;
- top window.

Instead: the pilot looks through whatever physical openings exist around the cockpit.

Examples:

## Fighter-style canopy

Large transparent canopy. Provides wide visibility.

## Armoured military cockpit

Small protected viewing area or no direct external view.

## Top-mounted cockpit

Pilot sees over the hull.

## Underslung cockpit

Pilot sees beneath the ship.

No special rendering code is required in any of these cases.

---

# Cockpit geometry

A cockpit may contain:

- canopy geometry;
- structural framing;
- external housing;
- mounting structure.

Interior rendering is not required. A dark cockpit volume is acceptable. The focus is physical placement and external silhouette.

---

# Cockpit and windows

A cockpit canopy belongs conceptually to the cockpit module — it is not a separate "window" module. A cockpit is a coherent physical machine.

```
Cockpit:
- pilot visibility;
- canopy;
- command location.

Other windows:
- passenger observation;
- engineering observation;
- decorative/functional openings.
```

General ship windows (passenger observation, engineering observation, decorative/functional openings) are a separate, future module type and should not be modeled as belonging to the cockpit.

---

# Command interface (minimal, initial scope)

Cockpit modules may expose command endpoints (via command bus system).
This is not gameplay UI — no screens, switches, or interiors — but the
module should not be purely visual, given the project's existing command-bus philosophy.

Initial example endpoints:

- canopy lights on/off;
- cockpit lighting.

Anything beyond this (pilot controls, full ship command bus integration, instrumentation) is
deferred — see **Future extensions**.

---

# Implemented modules

- Aries: one top-facing C2 mount with the Aries civilian canopy cockpit.
- Asterisk: one starboard-facing C2 mount with a compact side command blister.
  Its camera looks primarily forward and 20 degrees outward toward starboard.
- Beren: one downward-facing forward C2 mount with a fully underslung command pod.
  Its camera looks forward and 10 degrees down.
- Antega: one upward-facing, keyed C5 mount on the dorsal centreline far aft, allowing
  only `Deg0`, with the broad armoured Antega civilian bridge. Its camera sits inside
  the forward glazing and looks 5 degrees down.
- All four modules own their external geometry, camera child pose, dark backing, and
  independent canopy/internal light elements.
- Own-ship geometry remains hidden in first-person. No cockpit interior is rendered.
- The projected ship-forward reticle consumes the resolved camera pose and requires
  no hull-specific offset.

---

# Future extensions

The module system allows future additions without changing ship architecture.

## Command systems

- pilot controls;
- ship command bus connection;
- interface modules.

## Damage

- cockpit destruction;
- loss of control;
- emergency systems.

## Life support

- crew environment;
- survival systems.

## Alternative control arrangements

The architecture does not forbid:

- remote ships;
- drone ships;
- specialised command modules;
- multi-operator ships.

However: the initial game assumes one active pilot cockpit. Multi-seat gameplay is deferred.

---

# Design invariants

1. A cockpit is a physical module, not a camera trick.
2. A cockpit mount defines the hull interface; the cockpit module defines the pilot environment.
3. Mount transforms and cockpit installation transforms are separate.
4. Visibility comes from physical geometry, not hard-coded viewport directions.
5. Cockpits are replaceable through mount compatibility.
6. A canopy belongs to the cockpit module, not a separate window module.
7. Interior rendering is optional and not required.
8. The first implementation remains simple: one mount, one cockpit, one camera.
9. The initial game assumes one active pilot cockpit; multi-seat is deferred.
10. Unusual installations (sideways/upside-down) are allowed when physically compatible with the mount.

---

===== End of main doc =========================================


# Appendix A - Implementation proposals

## Proposed data model

Initial cockpit support should use three distinct concepts:

- `CockpitMountDefinition` - hull-authored socket/interface.
- `CockpitModuleDefinition` - installable cockpit type.
- `InstalledCockpit` - runtime/persistent cockpit instance installed on a ship.

The hull owns only the mount. The installed cockpit owns the pilot/camera/canopy placement.

```csharp
public enum CockpitMountClass
{
    C1,
    C2,
    C3,
    C4,
    C5,
}

public enum MountFacing
{
    Forward,
    Aft,
    Up,
    Down,
    Port,
    Starboard,
}

public enum CockpitRotationStep
{
    Deg0,
    Deg90,
    Deg180,
    Deg270,
}
```

```csharp
public sealed record CockpitMountDefinition
{
    public required string MountId { get; init; }

    public required CockpitMountClass MountClass { get; init; }

    // Ship-local socket pose. Position is the hull penetration/interface point.
    // Orientation defines the mount outward axis and socket-local up/right.
    public required DVec3 ShipLocalPosition { get; init; }
    public required Quaternion ShipLocalOrientation { get; init; }

    public required DVec3 SocketSizeMeters { get; init; }

    public required MountFacing Facing { get; init; }

    // Square sockets usually allow 0/90/180/270. Rectangular/keyed sockets only allow Deg0.
    public required IReadOnlySet<CockpitRotationStep> AllowedRotations { get; init; }

    public string? DefaultCockpitDefinitionId { get; init; }
}
```

```csharp
public sealed record CockpitModuleDefinition
{
    public required string DefinitionId { get; init; }
    public required string DisplayName { get; init; }

    public required CockpitMountClass RequiredMountClass { get; init; }

    // Cockpit-local transforms, relative to the installed cockpit root.
    public required DVec3 PilotLocalPosition { get; init; }
    public required Quaternion PilotLocalOrientation { get; init; }

    public required DVec3 CameraLocalPosition { get; init; }
    public required Quaternion CameraLocalOrientation { get; init; }

    public DVec3? CanopyLocalPosition { get; init; }
    public Quaternion? CanopyLocalOrientation { get; init; }

    public MountFacing? PreferredFacing { get; init; }

    public bool HasCanopyLights { get; init; }
    public bool HasCockpitLights { get; init; }
}
```

```csharp
public sealed class InstalledCockpit
{
    public required string MountId { get; init; }
    public required string DefinitionId { get; init; }

    public required CockpitRotationStep InstallationRotation { get; init; }

    public bool CanopyLightsOn { get; set; }
    public bool CockpitLightsOn { get; set; }

    public DVec3 ResolveShipLocalCameraPosition(
        CockpitMountDefinition mount,
        CockpitModuleDefinition definition)
    {
        // Conceptual contract:
        // shipLocal = mount transform * installation rotation * definition.CameraLocalPosition
        throw new NotImplementedException();
    }

    public Quaternion ResolveShipLocalCameraOrientation(
        CockpitMountDefinition mount,
        CockpitModuleDefinition definition)
    {
        // Conceptual contract:
        // shipLocalOrientation = mount orientation * installation rotation * definition.CameraLocalOrientation
        throw new NotImplementedException();
    }
}
```

## Persistence shape

Cockpit installation should persist as ship configuration, not as hull data.

```csharp
public sealed record InstalledCockpitRecord
{
    public string MountId { get; init; } = "";
    public string DefinitionId { get; init; } = "";
    public CockpitRotationStep InstallationRotation { get; init; } = CockpitRotationStep.Deg0;

    public bool CanopyLightsOn { get; init; }
    public bool CockpitLightsOn { get; init; }
}
```

`ShipRecord` should eventually contain either:

```csharp
public InstalledCockpitRecord? Cockpit { get; init; }
```

or use the generic installed-component record if cockpit becomes a normal component slot:

```csharp
InstalledComponentRecord
{
    SlotId = "cockpit",
    TypeId = "aries-civilian-canopy-cockpit"
}
```

Initial recommendation: use an explicit `InstalledCockpitRecord` until cockpit behaviour is better understood. It avoids overloading generic component records with transform-specific installation data too early.

## Hull integration

`HullDefinition` should eventually replace hull-level `CockpitOffset` / `CockpitPose` with cockpit mounts.

```csharp
public sealed class HullDefinition
{
    public required IReadOnlyList<CockpitMountDefinition> CockpitMounts { get; init; }

    // Transitional only:
    // CockpitOffset and CockpitPose may remain temporarily as compatibility fallback,
    // but should not be treated as authoritative once InstalledCockpit exists.
}
```

Aries initial mount:

```csharp
new CockpitMountDefinition
{
    MountId = "type-1.cockpit.top.01",
    MountClass = CockpitMountClass.C2,
    ShipLocalPosition = new DVec3(-1.25, 1.55, -5.9),
    ShipLocalOrientation = Quaternion.CreateFromYawPitchRoll(
        MathHelper.ToRadians(-3.0f),
        0.0f,
        0.0f),
    SocketSizeMeters = new DVec3(1.5, 1.5, 1.0),
    Facing = MountFacing.Up,
    AllowedRotations =
    [
        CockpitRotationStep.Deg0,
        CockpitRotationStep.Deg90,
        CockpitRotationStep.Deg180,
        CockpitRotationStep.Deg270,
    ],
    DefaultCockpitDefinitionId = "aries-civilian-canopy-cockpit",
}
```

## Runtime camera contract

The live ship should expose a resolved cockpit pose, but should not own the authored cockpit data.

```csharp
public DVec3 CockpitWorldPosition { get; }
public Quaternion CockpitWorldOrientation { get; }
```

Resolution order:

```text
Ship world transform
  * CockpitMountDefinition ship-local transform
  * InstalledCockpit installation rotation
  * CockpitModuleDefinition camera local transform
```

`ShipSnapshot` may publish the resolved world-space camera pose for rendering. That snapshot is presentation data, not a second cockpit authority.

## Command endpoints

Initial cockpit commands should be ordinary simulation-owned state changes.

Suggested command topics:

```text
Cockpit.CanopyLights.Toggle
Cockpit.CanopyLights.Set
Cockpit.InternalLights.Toggle
Cockpit.InternalLights.Set
```

Input/UI sends commands to the simulation. The installed cockpit state changes on the simulation side.
Rendering reads published snapshot/presentation state.

## First-person render policy

Initial implementation may choose not to render cockpit interior geometry.

Minimum explicit policy:

- cockpit camera uses the resolved physical camera pose;
- own ship exterior remains hidden in first-person for the current implementation;
- cockpit/canopy exterior geometry may be visible in chase camera;
- first-person visible cockpit interior is deferred;
- future first-person cockpit rendering must use the same installed cockpit transform chain, not a separate overlay pose.
