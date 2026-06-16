# Inferior — QoL Bug Fix Reference

> Maintained by Timo. Feed to Code one section at a time.
> Ordered by impact on playability.

---

## Bug 1 — Mouse not captured in flight mode [DONE]

**Symptom:** Looking around requires holding right mouse button. Mouse escapes
window, loses focus, right-click menus appear in other apps.

**Cause:** No mouse capture or cursor lock in flight mode.

**Fix:** In flight mode, lock cursor to window centre every frame and accumulate
delta for camera look. In UI mode, release cursor normally.

```csharp
// In SystemSpaceState — add field:
private const float MouseSensitivity = 0.0018f;   // tune this; lower = slower

// In Update(), replace right-click look with this:
if (_inFlightMode)  // whatever flag distinguishes flight vs UI mode
{
    int cx = _graphics.PreferredBackBufferWidth  / 2;
    int cy = _graphics.PreferredBackBufferHeight / 2;

    var ms = Mouse.GetState();
    float dx = (ms.X - cx) * MouseSensitivity;
    float dy = (ms.Y - cy) * MouseSensitivity;

    if (MathF.Abs(dx) > 0.00001f || MathF.Abs(dy) > 0.00001f)
        _camera.ApplyMouseLook(dx, dy);   // existing camera rotation method

    Mouse.SetPosition(cx, cy);
    Game.IsMouseVisible = false;
}
else
{
    Game.IsMouseVisible = true;
    // normal UI mouse handling here
}
```

`MouseSensitivity` should be a constant that can be tuned. Start at 0.0018f.
If movement "jumps", lower it. The `Mouse.SetPosition` call is what prevents
the cursor from drifting out of the window.

---
## Bug 1b — Mouse now captured even when game does not have focus

Mouse need to be release when game looses focus. And recaptured when game gets focus (when in flight mode / debug cam).

---

## Bug 2 — Spawning inside planet on teleport [DONE]

**Symptom:** Teleporting to a moon or station via system map sometimes spawns
inside a planet. Speed capped at 100 km/s, can't escape.

**Cause:** Spawn position is calculated relative to the target body without
accounting for the planet's radius, or the planet itself is between the spawn
point and the station.

**Fix:** When calculating spawn position, ensure it is outside all planet radii
in the system. The minimum safe approach:

```csharp
// When teleporting to a station or body:
DVec3 targetPos = GetTargetWorldPosition(target);   // station or body position

// Find any body that might be between player and target
// Minimum: ensure spawn is outside the parent planet's radius + buffer
DVec3 parentPos  = GetParentBodyPosition(target);   // parent planet/star
double parentRadius = GetParentBodyRadius(target);

DVec3 awayFromParent = DVec3.Normalize(targetPos - parentPos);
double safeOffset    = Math.Max(parentRadius * 1.5, 5000.0);  // outside planet + 5km

DVec3 spawnPos = targetPos + awayFromParent * safeOffset;
```

For stations specifically, spawn 300–500m from the station in the direction
away from the parent body. For planets/moons, spawn 1.5× radius distance
from the body centre.

---

## Bug 3 — Wrong view direction on teleport arrival [DONE]

**Symptom:** After teleporting to a station, camera faces a random direction —
usually toward the star or a planet, not the station.

**Cause:** The spawn function sets player position but doesn't orient the camera
toward the destination.

**Fix:** After computing spawn position, set camera to look at the target:

```csharp
// After computing spawnPos:
DVec3 toTarget = DVec3.Normalize(targetPos - spawnPos);
_camera.SetLookDirection((Vector3)toTarget);
// Or whatever the camera's orientation-setting API is
```

If the camera uses a quaternion orientation, compute the rotation from world
forward to `toTarget` and set it directly. The player should arrive looking
at the station they just teleported to.

---

## Bug 4 — Nav target ball points at star instead of nav target [??? STILL SEEMS OFF SOMETIMES] 

**Symptom:** The direction ball nav marker points toward the star regardless of
what was selected as nav target in the system map.

**Cause:** The nav target set in `SystemMapState` isn't being carried into
`SystemSpaceState`, or the direction ball is reading the wrong data source
(star position instead of nav target position).

**Diagnosis — check these in order:**
1. When a station or planet is selected as nav target in system map, is that
   target stored somewhere accessible to system space state?
2. In `SystemSpaceState.Update()`, is a nav target position being published to
   the DataBus each frame?
3. Does `DirectionBall` subscribe to a nav target topic, or does it only have
   a star direction topic?

**Fix pattern:**

```csharp
// In SystemSpaceState — store nav target on enter:
public void Enter(object? parameter)
{
    if (parameter is NavTarget navTarget)
        _navTarget = navTarget;
}

// In Update() — publish nav target direction each frame:
if (_navTarget != null)
{
    DVec3 toTarget = _navTarget.WorldPosition - _shipPosition;
    DataBus.Instruments.Publish(Topics.Navigation.TargetRelativePosition,
        (Vector3)toTarget.Normalized());
}

// In DirectionBall — subscribe to this topic and render a distinct marker
// (different colour/shape from the star marker)
```

The star marker and nav target marker should be visually distinct —
different colours at minimum. Suggested: star = white/yellow, nav target =
cyan/green to match the targeting system bracket colour.

---

## Bug 5 — Station drift at ~1 m/s [DONE]

**Symptom:** Stations appear to move slowly (~1 m/s) even when player is
stationary relative to the parent planet. Speed readout shows 0.0 m/s vs planet
but station visibly drifts.

**Cause:** Stations have orbital velocity around their parent body. If the
player is stationary relative to the planet, the station is still moving at
its orbital speed. The relative velocity display compares player to planet,
not player to station.

**Fix:** When displaying relative speed to a specific object (station, moon),
compute velocity relative to that object directly:

```csharp
// Relative speed to station = |playerVelocity - stationVelocity|
// Station orbital velocity magnitude:
double orbitalSpeed = 2.0 * Math.PI * station.OrbitRadius / station.OrbitalPeriod;
// Direction: tangential to orbit (perpendicular to position vector from parent)
DVec3 orbitDir = DVec3.Normalize(DVec3.Cross(
    station.OrbitPosition - parentBody.Position,
    parentBody.RotationAxis));   // or approximate with world Y
DVec3 stationVelocity = orbitDir * orbitalSpeed;

DVec3 relativeVelocity = playerVelocity - stationVelocity;
float relSpeed = (float)relativeVelocity.Length();
```

If orbital period isn't stored on stations yet, approximate:
`orbitalPeriod = 2π × sqrt(r³ / GM)` where r = orbit radius and GM is the
parent body's gravitational parameter.

Note: the visual drift of the station is physically correct — it IS moving.
The display just needs to show speed relative to the station, not relative
to the planet.

---

## Bug 6 — Chamfer (edge trim) winding bug [DONE]

**Symptom:** Some chamfer strips visible from inside modules instead of outside.
Visible as Y-shaped interior geometry when flying inside a module. Octagons show
chamfer on wrong side.

**Cause:** `GenerateEdgeTrimStrips` quad vertex order is reversed for some edge
configurations. Same winding issue as other geometry.

**Fix:** In `GenerateEdgeTrimStrips`, find the `AddQuad` call:

```csharp
// Current (may be wrong for some edges):
module.Mesh.AddQuad(wA0, wA1, wB1, wB0, wTrimNormal, trimColor);

// Try reversing:
module.Mesh.AddQuad(wA0, wB0, wB1, wA1, wTrimNormal, trimColor);
```

The correct order depends on which direction `wTrimNormal` points. Verify
by checking: does `dot(wTrimNormal, outward direction from station centre) > 0`?
If yes, the normal is pointing outward and the winding should produce an
outward-facing face. Use whichever vertex order achieves this.

For octagonal modules: the chamfer strips between octagon side faces use
different normal directions — ensure the winding matches the sign of the
cross product for those normals.

---

## Bug 7 — Direction ball: station marker and relative dot sizes

**Symptom:** No station marker on main direction ball when within range. Distance
dot sizes are absolute rather than relative to distance rank.

### Part A — Station markers

Add station markers to the direction ball when within 100km:

```csharp
// In DirectionBall rendering — add to existing body marker loop:
foreach (var station in nearbyStations)   // stations within 100km
{
    DVec3 toStation = station.WorldPosition - shipPosition;
    if (toStation.Length() > 100_000.0) continue;

    Vector3 dir = (Vector3)DVec3.Normalize(toStation);
    // Project dir onto ball surface — same projection as planet markers
    DrawMarker(dir, color: new Color(200, 180, 80),   // dark yellow
               size: GetRelativeSize(toStation.Length(), allDistances));
}
```

Station marker colour: dark yellow `(200, 180, 80)` — distinct from planet
markers and star marker.

### Part B — Relative dot sizes

Replace absolute size calculation with rank-based:

```csharp
// Build sorted distance list for all visible objects:
var objects = allBodies
    .Select(b => (body: b, dist: (shipPos - b.Position).Length()))
    .OrderBy(x => x.dist)
    .ToList();

// Assign sizes by rank (closest = largest dot):
int[] dotSizes = [8, 7, 6, 5, 4, 3];   // pixel sizes by rank
for (int i = 0; i < objects.Count; i++)
{
    int sizeIdx = Math.Min(i, dotSizes.Length - 1);
    DrawMarker(objects[i].body, dotSizes[sizeIdx]);
}
```

The closest object always gets the largest dot. Objects beyond rank 6 get
the minimum size. This makes it immediately clear which body is closest.

---

## Bug 8 — Targeting ('C' key and mouse click)

**Symptom:** Pressing 'C' does not select the nearest object to reticle.
Mouse click in UI mode doesn't target objects.

**Cause:** `TargetingSystem` may not be wired into the input handling, or
`SelectClosestToReticle` isn't being called on C key press.

**Diagnosis:** Check `SystemSpaceState.Update()` — is there a handler for
`Keys.C` that calls `_targeting.SelectClosestToReticle(...)`?

**Fix:** The targeting brief (`inferior-targeting-brief.md`) has the full
implementation. Key wiring:

```csharp
// In SystemSpaceState.Update():
if (input.WasJustPressed(Keys.C))
{
    _targeting.SelectClosestToReticle(
        _camera.ViewProjectionMatrix,
        GraphicsDevice.Viewport,
        (Vector3)_camera.CockpitOffset);
}
```

Targetable objects: stations, planets, moons. Star targeting deferred.
All radar contacts (from `DataBus.Radar`) are automatically targetable once
the system is wired. The targeting brief has the full `TargetingSystem` class
and HUD bracket rendering.

---

## Bug 9 — Galaxy map too small, missing grid quadrant

**Symptom:** Galaxy map appears as a small coin shape. Top-left grid quadrant
missing lines. Scrolling/zoom doesn't cap at useful limits.

**Fixes:**

```csharp
// Initial zoom: fit galaxy to screen width
// In GalaxyMapState.Enter() or Initialize():
float galaxyExtent = 2048f;   // units — adjust to actual galaxy size
_zoom = Math.Min(
    _viewport.Width  / galaxyExtent,
    _viewport.Height / galaxyExtent) * 0.9f;  // 90% of screen

// Zoom cap:
_zoom = Math.Clamp(_zoom, minZoom, maxZoomThatShowsWholeGalaxy);

// Grid quadrant fix — check grid line generation loop bounds.
// If generating lines from 0→N on X and 0→N on Y, negative coordinate
// stars won't have grid. Change to generate from -N→+N in both axes.
for (int x = -gridCount; x <= gridCount; x++)
    DrawVerticalGridLine(x * gridSpacing);
for (int y = -gridCount; y <= gridCount; y++)
    DrawHorizontalGridLine(y * gridSpacing);
```

---

# Bug 10 — Disconnected stations (found in home system, middle station)

This disconnection pattern is different from the one fixed earlier. 
Previously every individual module floated free — this time you have several internally-connected clusters separated from each other. 
That distinction matters for diagnosis.
What the image shows:

Top-left: small connected cluster with cylindrical octagonal module
Center-left: larger connected cluster with window grids (LinearSpine character visible)
Right: what looks exactly like a connector-long-large (80m) with modules at each end — a perfectly valid H-shape, just not attached to the main structure

This pattern — multiple correct sub-clusters — points to one specific cause: the large connector module's AABB (80×16×16) is 
being flagged as intersecting the main structure when it shouldn't be, causing TryAttach to reject what should be a valid placement. 
The growth engine then moves on, and subsequent ports generate a disconnected branch.

The 80m connector is 4× longer than the standard one. Its AABB extends 40m in each direction from centre. If any part of that 
80m box overlaps with an existing module's AABB — even at the far end, far from any actual geometry — the placement is rejected.
Diagnostic to give Code:
Add this temporary print immediately after TryAttach returns null:
```
csharpif (placed == null)
    Debug.WriteLine($"REJECTED: {moduleDef.Id} from port {parentPort.Definition.Id} " +
                    $"on {parentPort.ParentModule.Definition.Id} depth={parentPort.Depth}");
```

Run with the home system seed. If you see connector-long-large rejections repeatedly, that's the culprit.
Likely fix: Increase the AABB intersection margin for the connection axis only, or reduce the large connector's bounding box to exclude 
the port-face ends (since those faces will be occupied by child modules anyway). The quick fix is simply making connector-long-large 
slightly narrower in its bounding box — say 76×14×14 instead of 80×16×16 — giving just enough clearance for adjacent modules not to trigger a 
false intersection.



## Deferred — do later

| Item | Notes |
|------|-------|
| Star targeting from sky view | Requires projecting star positions to screen; ring marker around selected hyperspace target |
| Mouse sensitivity tuning | Tune `MouseSensitivity` constant once capture is working |
| Flight feel parameters | Max speed near stations, rotation rate, assist curves |
| Star as hyperspace target for fast travel | When instant-travel debug removed |
