# Flat Hyperspace — Design Document

## Overview

Flat Hyperspace is a two-dimensional travel layer that cuts through the galaxy. It is entered
from normal space via a preamble sequence, flown through as a distinct visual environment, and
exited either voluntarily or by drifting too close to a star's gravitational disturbance.

---

## Flight Modes

Two new values are added to the `FlightMode` enum:

| Value | Description |
|---|---|
| `EnteringFlatHyperspace` | Preamble sequence: ship aligns, sheets animate in |
| `FlatHyperspace` | Active hyperspace travel — 2D left/right steering only |

---

## The Hyperspace Plane

### Definition

The plane is defined by **the player's up vector as its normal**. It is the horizontal plane
of the ship at the moment H is pressed (after alignment). The plane passes through the ship's 
current galactic position and the target system's position.

In practice: no matter what heading the player had in system space, the hyperspace plane is
always perpendicular to the ship's world-up at entry. If the ship is aligned with the galactic
disc, the plane cuts through many stars. If the ship is banked steeply, the plane tilts and
fewer stars intersect it.

### Coordinate mapping

`GalacticPos` is stored in **light years** (DVec3). Conversion to SI: `× 9.4607 × 10¹⁵ m/ly`.

In flat hyperspace the player's galactic position is tracked as a DVec3 that moves along the
plane. Forward speed is ~1000 ly/min (tuneable). The mapping is 1:1 — the player's galactic
position is real galaxy coordinates compressed into 2D travel.

### Stars in / near the plane

A star's signed distance from the plane is:

```
d = dot(star.GalacticPos - planeOrigin, planeNormal)   // in ly
```

Stars with `|d| ≤ MaxDisturbanceRadius` (default **100 ly**, tuneable constant) are visible as
disturbances. The disturbance strength at the plane surface scales inversely with `|d|`:

```
strength = 1 - (|d| / MaxDisturbanceRadius)   // 0..1, 1 = star on the plane
```

The projected position of the disturbance on the plane is:

```
projectedPos = star.GalacticPos - d * planeNormal
```

This is also the position used for dropout checks and visual placement.

---

## Entry Sequence — `EnteringFlatHyperspace`

H key is pressable at any time from `SystemSpaceState`; no hyperspace target is required.

### Phase 1 — Auto-align (variable duration)

- Roll and yaw/pitch input are **ignored**. Ship auto-rotates toward the hyperspace target
  (or, if no target, skip alignment).
- HUD displays: **`HYPERSPACE PREAMBLE`** (even if no target)
- No animation yet.

### Phase 2 — Dot appears (trigger: within 10° of target)

- A bright white dot appears at the target star's skybox position (or a fixed forward point if
  no target).
- Roll is also **locked** from this point.
- The dot animates from invisible to full brightness over **1–2 s** (eased in).
- This event also starts the remaining timer-driven animation chain.

### Phase 3 — Line grows (≈ 2 s, starts immediately after dot reaches full brightness)

- The dot stretches left and right symmetrically from the centre toward the screen edges.
- Easing: starts slow, accelerates (ease-in or quadratic). The visual metaphor is approaching
  a vast flat structure, its horizon line growing as you get closer.
- The line is rendered in 2D screen-space (drawn over the 3D scene, aligned to the horizon).

### Phase 4 — Pause (1 s)

- Line holds at full width. No new visuals.

### Phase 5 — Sheets appear (≈ 1 s)

- Two surfaces grow from the horizon line toward the top and bottom screen edges simultaneously.
- Same easing as Phase 3 (starts slow, accelerates).
- The surfaces are the **hyperspace sheets** (see Visual Design below).
- These may be rendered as actual 3D geometry (two large textured/grid quads that the ship
  flies between) or as 2D overlays — the implementation should be abstracted behind an
  `IHyperspaceSheetRenderer` interface so the appearance can be swapped without touching
  flight logic.

### Phase 6 — Flat Hyperspace entered

- Sheets reach screen edges. Flight mode transitions to `FlatHyperspace`.
- Galaxy position is now updating continuously.
- The text **`HYPERSPACE`** is no longer displayed in the HUD.

---

## Visual Design of the Sheets

The two sheets represent the upper and lower boundary of the hyperspace corridor. Appearance
options (select one at implementation time; renderer is pluggable):

| Style | Description |
|---|---|
| **2001 grid** | 1980s-style perspective grid with glowing blue/white lines, receding to infinity ahead |
| **Noise pattern** | Animated Perlin/simplex noise texture, monochromatic, giving an organic tunnel feel |
| **Star streaks** | Compressed star trails, giving a "warp" impression |

Default: **2001 grid** (easiest to implement, no texture assets required — generated geometry).

Sheet separation: **100–250 m** in render-space. Stars within or close to the plane (disturbances) cause
the sheets to **pinch toward each other** above and below the disturbance position. A star
exactly on the plane (strength = 1) makes the sheets touch. This pinch is the primary visual
warning to the player.

---

## Flat Hyperspace Flight

### Controls

| Input | Effect |
|---|---|
| Forward thrust | Always on — player moves forward at hyperspace speed |
| Left / Right yaw | Steer left / right in the galactic plane |
| All other inputs | Ignored (no pitch, no roll, no reverse) |

Speed: ~1000 ly/min (constant, no throttle). Tuneable via `FlatHyperspaceConstants`.

### Disturbances

Each star within `MaxDisturbanceRadius` ly of the plane projects to a point on the plane. The
disturbance is rendered as a visual pinch in the sheets and optionally as a glow/warning
indicator.

**Dropout condition:** The player is dropped out of hyperspace if they come within
`DropoutRadius` (tuneable, default **~2 ly** in galactic scale) of a disturbance's projected
point **and** the ship is travelling toward it. "Towards" is defined as:

```
dot(forward_direction, normalize(disturbance_pos - player_pos)) > cos(60°)  // ≈ 0.5
```

i.e. the disturbance is within a **120° forward cone** (60° half-angle from forward vector).
This means a disturbance behind or beside the ship never triggers dropout — critical so the
player can leave a system.

### Dropout outcomes

| Situation | Outcome |
|---|---|
| Dropped by non-target disturbance (star) | Enter `SystemNewtonian` ~100 AU from that star (random 80–120 AU) |
| Dropped by non-target disturbance (no star, edge case) | Enter `SystemNewtonian` at current galactic position; nearby systems checked within 1 ly |
| Arrived at target disturbance | Enter `SystemSlipstream` at 0.5–1 AU from target star, ship roughly pointing at star (±10°) |
| Voluntary exit (H again or dedicated key) | Enter `SystemNewtonian` at current galactic position; generate system if within 1 ly of any star |

### Arriving at target

The target star's disturbance uses the same dropout radius, but triggers the "arrival" path:
ship drops into `SystemSlipstream` at a random position 0.5–1 AU from the star, oriented
within ±10° of the star.

---

## System Generation on Exit

When the player exits hyperspace in a location that doesn't correspond to an existing loaded
system:

1. Check all stars for any within 1 ly of the player's current galactic position.
2. If one is found, generate and enter that star system (existing `StarSystem` generation path),
   spawning the player at the computed distance.
3. If none found, spawn the player in empty space — a future "deep space" state. For now, this
   falls back to `SystemNewtonian` with no local star; sensors simply return empty.

---

## Architecture

### New flight modes

```csharp
// FlightMode.cs
EnteringFlatHyperspace,
FlatHyperspace,
```

### New constants file

```csharp
// FlatHyperspaceConstants.cs (Inferior.Gameplay)
public static class FlatHyperspaceConstants
{
    public const double SpeedLYPerSecond    = 1000.0 / 60.0;  // ~16.7 ly/s
    public const double MaxDisturbanceRadius = 100.0;          // ly
    public const double DropoutRadiusLY      = 2.0;            // ly
    public const double DropoutConeHalfAngle = 60.0;           // degrees
    public const double ArrivalRadiusAU_Min  = 0.5;
    public const double ArrivalRadiusAU_Max  = 1.0;
    public const double DropoutRadiusAU_Min  = 80.0;
    public const double DropoutRadiusAU_Max  = 120.0;
    public const double AlignThresholdDeg    = 10.0;
}
```

### New class: `Hyperspaceplane`

Lives in `Inferior.Gameplay` or `Inferior.Galaxy`. Constructed once at H-key press:

```csharp
public sealed class HyperspacePlane
{
    public DVec3   Origin       { get; }  // galactic position at entry
    public DVec3   Normal       { get; }  // player up at entry (unit vector)
    public DVec3   Forward      { get; }  // player forward projected onto plane (unit vector)
    public DVec3   Right        { get; }  // cross(Forward, Normal)

    // Returns all stars within MaxDisturbanceRadius ly of the plane, with metadata.
    public IReadOnlyList<PlaneDisturbance> ComputeDisturbances(Star[] allStars);
}

public sealed record PlaneDisturbance(
    Star    Star,
    DVec3   ProjectedPos,   // star projected onto plane (galactic ly)
    double  Strength,       // 0..1, 1 = star on the plane
    double  SignedDist);    // signed distance from plane in ly
```

### Preamble state machine

The `EnteringFlatHyperspace` update tick runs a simple enum-driven state machine:

```
Aligning → DotFadeIn → LineGrow → Pause → SheetsGrow → Done
```

Each timed phase stores `float _phaseTimer` and advances when the timer exceeds the phase
duration.

### Renderer interface

```csharp
// Inferior.Rendering (or Inferior.Game/Hyperspace/)
public interface IHyperspaceSheetRenderer
{
    void Update(float dt, float sheetsProgress); // 0..1
    void Draw(GraphicsDevice gd, Camera3D camera, float sheetsProgress);
}
```

Concrete implementation: `GridHyperspaceSheetRenderer` (2001-style perspective grid).

### Integration into SystemSpaceState

Rather than a new GameState, the flat hyperspace modes live inside `SystemSpaceState`:

- `Update()` switches on `_flightMode` and calls `UpdateEnteringHyperspace()` or
  `UpdateFlatHyperspace()`.
- `Draw()` calls `DrawHyperspaceOverlay()` when in either mode; normal 3D scene still renders
  underneath (ship, skybox) until sheets fully cover the screen.
- Galactic position (`_galacticPos : DVec3`) is updated each tick in `FlatHyperspace` mode.
- On dropout, existing `EnterSystem(star)` / `ExitToNewtonianAtPos(galacticPos)` helpers handle
  state resets.

Cockpit UI panels, sensor data, dirBall targeting, and ship state are all preserved — no
re-initialisation required.

---

## Open Questions / Future Work

- **Deep space state**: exiting hyperspace with no nearby star needs a dedicated state. Deferred.
- **Mini-map**: a 2D map showing disturbances and the player's track through the hyperspace
  plane would aid navigation. Deferred.
- **Sound**: hyperspace should have a distinctive ambient drone and dropout sound. Deferred.
- **Sheet visual variants**: `IHyperspaceSheetRenderer` makes it trivial to add new looks later.
- **Multiplayer / persistence**: hyperspace position needs to be part of the save record.
  Deferred to the persistence design doc.
- **H-band lore tie-in**: the CSV in `Docs/` lists H-band as the artificial gravity band. Flat
  hyperspace travel is likely powered by manipulating H-band fields — worth mentioning in
  in-game flavour text when the mechanic is exposed to players.
