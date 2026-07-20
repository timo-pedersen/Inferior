# Brief: Implement Ship Cockpit Mounts and Installed Cockpit

## Context

Read first:

1. `Docs-ai/!invariants.md`
2. `Docs-ai/!current-state.md`
3. `Docs-ai/architecture-map-ai.md`
4. `Docs-ai/ship-ai.md`
5. `Docs-ai/ship-cockpits.md`

`Docs-ai/ship-cockpits.md` is now the active authority for cockpit and cockpit-mount design. It intentionally supersedes the older `ship-ai.md` cockpit-offset model.

## Goal

Implement the first pass of physical cockpit mounts and installed cockpit data.

The first pass should support:

- one cockpit mount on Aries;
- one default compatible cockpit module;
- one installed cockpit instance on the ship;
- resolved cockpit camera position and orientation;
- persistence shape for the installed cockpit;
- minimal simulation-owned light-toggle state;
- existing flight/camera behavior preserved except that cockpit pose now resolves through the mount/module chain.

No cockpit interior rendering is required. No fitting UI is required. No multi-seat support.

## Design Rules

A cockpit is a physical installed module, not a hull camera offset.

The hull owns:

- cockpit mount socket;
- mount class;
- ship-local mount transform;
- allowed installation rotation;
- default cockpit definition id.

The installed cockpit owns:

- selected cockpit module definition id;
- installation rotation;
- runtime state such as canopy lights and cockpit lights.

The cockpit module definition owns:

- camera local transform;
- pilot local transform;
- canopy local transform if present;
- compatibility class;
- preferred facing;
- supported light endpoints.

Do not create a second world authority. The simulation-owned ship resolves the cockpit pose. Rendering consumes `ShipSnapshot`.

## Suggested Types

Add cockpit model types under `Inferior.Gameplay`, probably near hull/ship code unless the existing structure suggests a better local fit.

Expected concepts:

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

    public required DVec3 ShipLocalPosition { get; init; }
    public required Quaternion ShipLocalOrientation { get; init; }

    public required DVec3 SocketSizeMeters { get; init; }
    public required MountFacing Facing { get; init; }

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
        CockpitModuleDefinition definition);

    public Quaternion ResolveShipLocalCameraOrientation(
        CockpitMountDefinition mount,
        CockpitModuleDefinition definition);
}
```

Use repository transform conventions and verify multiplication order before implementing. Add focused tests for the transform chain.

## Hull Integration

Update `HullDefinition` to include cockpit mounts:

```csharp
public required IReadOnlyList<CockpitMountDefinition> CockpitMounts { get; init; }
```

Keep `CockpitOffset` / `CockpitPose` only as transitional compatibility if needed to avoid a large rewrite, but do not let them remain authoritative once `InstalledCockpit` exists.

Add Aries initial mount:

- `MountId`: `type-1.cockpit.top.01`
- class: `C2`
- position: current Aries cockpit position, currently `new DVec3(-1.25, 1.55, -5.9)`
- orientation: current Aries cockpit orientation
- socket size: `new DVec3(1.5, 1.5, 1.0)`
- facing: `Up`
- rotations: `Deg0`, `Deg90`, `Deg180`, `Deg270`
- default cockpit: `aries-civilian-canopy-cockpit`

## Cockpit Definition Registry

Add a small cockpit definition registry similar in spirit to existing definition libraries.

Initial module:

- `DefinitionId`: `aries-civilian-canopy-cockpit`
- display name: `Aries Civilian Canopy Cockpit`
- required class: `C2`
- camera local pose: choose values that preserve the current camera behavior when combined with the Aries mount at `Deg0`
- pilot local pose: reasonable stub near the camera
- canopy local pose: optional
- canopy lights: true
- cockpit lights: true

The first implementation should preserve the current in-game camera position and orientation for Aries as closely as possible.

## Ship Runtime

Update `Ship` so it has an installed cockpit and exposes resolved world pose:

```csharp
public InstalledCockpit? Cockpit { get; init; }

public DVec3 CockpitWorldPosition { get; }
public Quaternion CockpitWorldOrientation { get; }
```

The resolved pose should use:

```text
Ship world transform
  * cockpit mount ship-local transform
  * installation rotation around mount outward axis
  * cockpit module camera local transform
```

`CockpitWorldPosition` and `CockpitWorldOrientation` should be derived, not separately mutable.

Update `SpaceSimulation.ShipSnapshot` to publish the resolved cockpit world position and orientation. Rendering should continue reading snapshot data.

## Persistence

Add:

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

Add to `ShipRecord`:

```csharp
public InstalledCockpitRecord? Cockpit { get; init; }
```

Update `ShipBuilder`, `ShipExtensions`, and any migration/default-loading path so old or missing cockpit data produces the Aries default cockpit for the Aries hull.

Do not leak persistence records into long-term runtime domain objects.

## Commands

Add command topics/constants for:

```text
Cockpit.CanopyLights.Toggle
Cockpit.CanopyLights.Set
Cockpit.InternalLights.Toggle
Cockpit.InternalLights.Set
```

Commands should flow through the existing command-bus pattern into simulation-owned cockpit state. No UI control is required for this pass unless there is an obvious existing debug command pattern to use.

## Rendering Policy

For this pass:

- cockpit camera uses resolved cockpit physical pose;
- first-person cockpit interior remains deferred;
- own ship exterior visibility in first-person may be enabled only if it does not destabilize depth/pass behavior;
- if first-person own-ship exterior is risky, leave it deferred and document the reason;
- chase camera should still render the ship as before.

Do not add a separate overlay cockpit pose. Future cockpit rendering must use the same installed cockpit transform chain.

## Tests

Add focused tests for:

- Aries has exactly one valid cockpit mount;
- Aries default cockpit definition exists and is compatible;
- default `ShipBuilder.NewShip("type-1")` installs the default cockpit;
- resolved cockpit world position/orientation matches the old Aries camera pose at `Deg0`;
- invalid mount/definition combinations fail clearly;
- persistence round-trip includes installed cockpit data;
- missing cockpit data defaults coherently for old records.

Run:

```powershell
dotnet build Inferior.slnx
dotnet test Inferior.Game.Test
```

If `dotnet test Inferior.Game.Test` is not the exact project invocation in this repo, use the existing test project path verified from the solution.

## Non-goals

Do not implement:

- fitting UI;
- cockpit replacement UI;
- cockpit damage;
- life support integration;
- rendered cockpit interior;
- multiple active pilot seats;
- passenger windows;
- broad ship rendering refactors.
