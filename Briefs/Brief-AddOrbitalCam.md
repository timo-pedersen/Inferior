Chase Camera — Editable Orbital Mode Brief
Goal
Extend the existing snapshot-driven F3 chase camera with an orbital inspection/edit mode.
The orbital mode edits the persistent ship-relative chase-camera pose. When the player leaves orbital mode, the resulting camera position and roll become the normal chase-camera configuration.
This is not a free camera and not a drone.
The camera remains anchored to the authoritative ship snapshot and always looks at the centre of the ship.
1. Confirmed controls
F3
Toggle ordinary chase-camera presentation:
Ship/cockpit view ↔ chase camera
Preserve existing F3 behaviour and easing.
Ctrl+F3
While chase camera is active:
Normal chase mode ↔ orbital edit mode
Ctrl+F3 should not create a separate camera stack.
It changes how the existing chase-camera relative pose is controlled.
Orbital edit controls
Input	Action
W	Move camera upward around orbital sphere
S	Move camera downward around orbital sphere
A	Move camera left around orbital sphere
D	Move camera right around orbital sphere
Q	Roll camera counterclockwise around camera-to-ship view axis
E	Roll camera clockwise around camera-to-ship view axis
R	Reduce orbit radius; move camera closer
F	Increase orbit radius; move camera farther away
X	Reset orbital/chase pose to default

Normal ship controls remain normal ship controls outside orbital edit mode.
While orbital edit mode is active, these listed keys control the camera and must not simultaneously command the ship for the same input axes.
Mouse behaviour should remain unchanged unless current input routing requires suppressing it to prevent simultaneous ship-look changes. Do not introduce mouse-controlled orbit in this brief.
2. Camera model
The chase camera is defined by ship-relative presentation state:
ChaseCameraPose
    Relative direction from ship to camera
    Radius
    Roll
Equivalent representations are acceptable, such as:
Hull-local offset vector
Camera roll
The state must not store an eased absolute universe position as its authority.
Every frame:
authoritative ship snapshot translation
    +
ship-relative chase offset
    =
desired camera world position
The ship snapshot translation is applied exactly.
Only ship-relative camera presentation state may be eased.
This preserves the recently established invariant:
Chase-camera smoothing must never interpolate absolute universe coordinates.

3. Coordinate ownership
The editable chase offset must remain ship-relative.
When the ship pitches, yaws, or rolls, the chase-camera position follows the ship’s orientation.
A saved side view remains a side view of the ship rather than becoming fixed relative to the star system.
Conceptually:
camera world offset =
    transform(saved hull-local offset, ship snapshot orientation)

camera world position =
    snapshot position + camera world offset
Do not store the edited camera position as an absolute world-space location.
Do not derive ship truth from the camera.
4. Orbital sphere movement
In orbital edit mode, WASD moves the camera along the surface of a sphere centred on the ship.
The current radius remains constant while using WASD.
The camera must not move linearly through the sphere.
Screen-relative axes
Derive the current camera basis from:
Forward = normalized(ship centre - camera position)
Right   = camera screen-right
Up      = camera screen-up
Then:
A/D orbit around the ship in the current screen-left/screen-right direction.
W/S orbit around the ship in the current screen-up/screen-down direction.
Movement should update the offset direction while preserving its length.
A practical implementation may rotate the offset vector around the appropriate tangent axis rather than adding a tangent displacement and renormalising.
Interaction with roll
Q/E changes camera roll.
Because WASD is screen-relative, rolling the camera changes what screen-up and screen-right mean. Consequently, after a 90° roll, W moves around the sphere in what was previously a sideways direction.
This is intentional.
5. Camera roll
Q/E rotates the camera around the axis formed by:
camera position → ship centre
This changes camera roll only.
It must not:
change orbit radius;
change camera position on the sphere;
change the ship;
change the camera’s look target.
The camera continues to look at ship centre, with the authored roll applied around the resulting view axis.
Store the roll as part of the persistent chase-camera pose.
Normal chase mode retains the roll chosen in orbital edit mode.
X resets roll to zero.
6. Radius controls
R moves the camera closer to the ship.
F moves the camera farther away.
These controls change only the radius, preserving current orbital direction and roll.
Define conservative constants for:
minimum radius;
maximum radius;
radial adjustment speed.
Suggested initial limits:
Minimum radius: 15–25 m
Default radius: existing chase-camera radius, approximately 85 m
Maximum radius: 500–1000 m
Select exact values that fit the current camera and clipping setup.
The minimum must keep the camera outside or sensibly near the Aries hull rather than allowing it to pass through the ship immediately.
Do not attempt station-wall or scenery collision avoidance in this brief.
7. Reset behaviour
X restores the original default chase configuration:
existing behind-and-above offset;
existing radius;
roll zero.
Use the current intended default, approximately:
80 m behind
30 m above
looking at ship centre
Do not encode the reset as an absolute universe position.
Reset should work in orbital edit mode.
It does not reset or move the ship.
8. Mode transitions
Entering F3 chase mode
Use the currently saved chase pose.
The first time in a session, use the default pose.
Preserve the existing easing into chase view.
Entering orbital edit mode
Ctrl+F3 while chase mode is active:
begin editing the current chase pose;
do not jump to another predefined orbit;
do not reset radius or roll;
keep looking at ship centre.
The transition should be visually continuous.
Leaving orbital edit mode
Ctrl+F3 again:
return to normal chase behaviour;
preserve the edited offset direction;
preserve radius;
preserve roll.
There should be no positional jump. The camera is already at the new chase pose.
Leaving chase mode
F3 returns to ordinary ship/cockpit view.
The edited chase pose remains saved for the next F3 activation.
Ctrl+F3 outside chase mode
Prefer no action, optionally with a small HUD/system message:
Orbital camera requires chase view.
Do not implicitly enable F3 chase mode unless the existing input conventions strongly favour that behaviour.
9. Newtonian-only restriction
Chase and orbital camera modes are available only in Newtonian flight modes.
They must not operate during slipstream.
Required behaviour:
F3 request during slipstream does nothing or produces a concise HUD message.
Ctrl+F3 during slipstream does nothing.
If the ship enters slipstream while chase or orbital mode is active, automatically return to ordinary ship view.
Preserve the saved chase pose so it is available after returning to Newtonian flight.
Do not change slipstream simulation or camera behaviour beyond enforcing this presentation restriction.
Use the existing authoritative flight-mode state rather than inferring mode from speed or effects.
10. Ship controls and visual interpretation
The camera pose must not alter the meaning of ship controls.
Ship-forward remains ship-forward regardless of camera position.
Examples:
With camera behind the ship, forward acceleration appears to move away from camera.
With camera in front of the ship, forward acceleration appears to move toward or past the camera while the camera follows backward.
In a side view, forward movement appears sideways across the screen.
In top-down view, pitch/yaw/roll retain their normal ship-local meaning.
Do not remap flight controls to camera axes.
Orbital edit controls are only an input-mode override while the player is explicitly editing the camera.
11. Ship-centre look target
For this brief, always look at the hull/ship origin:
ShipSnapshot.Position
Do not use:
cockpit position;
centre of visual bounds;
cargo centre;
camera-selected point;
target object.
This may later be refined if the authored hull origin proves visually unsuitable, but one stable target is preferable now.
12. Easing
Preserve existing chase-camera easing.
The architectural rule remains:
Snapshot translation:
    exact every frame

Relative camera offset:
    may be eased

Camera roll/orientation:
    may be eased if existing behaviour benefits
During orbital editing, controls should feel responsive.
It is acceptable to apply mild angular and radial smoothing, but do not introduce a long lag between input and camera movement.
When the ship is moving at high speed, the camera must not lag kilometres behind.
Add a regression test preserving the fixed high-speed translation behaviour.
13. HUD and targeting ownership
Preserve the recently corrected ownership split.
HUD truth
Ship and target telemetry use ship/simulation snapshot truth:
target distance = distance(ship snapshot position, target position)
Camera orbit must not change displayed ship-to-target distance.
Targeting and screen projection
Crosshair projection and view selection use the active render camera.
An object visible under the chase/orbital reticle should be targetable from that view.
Do not convert HUD navigation truth to camera-relative truth.
No new chase-camera reticle or additional statistics are required in this brief.
14. State structure
Keep the new state narrow and presentation-owned.
A possible shape:
ChaseCameraState
    IsActive
    IsOrbitalEditActive
    HullLocalDirection or HullLocalOffset
    Radius
    Roll
    EasedRelativeOffset
The exact names are flexible.
Avoid:
a second Camera3D;
a separate orbital-camera class duplicating chase logic;
a world-space camera position as persistent authority;
introducing the future drone abstraction now.
The future camera drone is explicitly deferred.
15. Input precedence
When orbital edit mode is active:
consume WASD, Q, E, R, F, and X for camera editing;
prevent those consumed inputs from simultaneously reaching ship thrust/roll controls;
leave unrelated controls functional where safe;
F3 and Ctrl+F3 transitions must remain reliable.
Be careful with Ctrl+F3 detection so it does not also trigger the ordinary F3 toggle in the same frame.
Resolve the modified shortcut before the unmodified F3 action.
16. Minimal user feedback
Add concise system messages:
Chase camera enabled.
Chase camera disabled.
Orbital camera edit enabled.
Orbital camera edit disabled.
Chase camera reset.
Chase camera unavailable during slipstream.
Exact wording is flexible.
Do not implement the planned alternate reticle or expanded chase HUD yet.
17. Tests
Add focused tests around presentation math and mode behaviour.
Orbital movement
WASD preserves radius.
Opposite inputs approximately reverse movement.
Movement remains on the orbit sphere.
WASD is screen-relative.
Changing roll changes screen-relative orbit directions.
Roll
Q/E changes roll.
Roll does not alter camera position or radius.
Camera forward continues to point at ship centre.
X restores zero roll.
Radius
R decreases radius.
F increases radius.
Radius is clamped to minimum and maximum.
Radius changes preserve orbital direction.
Persistence
Leaving orbital edit mode preserves offset, radius, and roll.
Leaving F3 and re-entering restores the edited chase pose.
X restores the default behind-and-above pose.
Snapshot anchoring
Large absolute ship translations move the camera by exactly the same translation.
Camera relative offset remains stable.
No interpolation of absolute universe coordinates returns.
High-speed movement cannot create kilometre-scale chase lag.
Flight modes
F3 cannot activate during slipstream.
Ctrl+F3 cannot activate during slipstream.
Entering slipstream exits active chase/orbital presentation.
Saved pose survives the forced exit.
Ownership
Camera movement does not change ship-to-target HUD distance.
Target projection uses active camera view.
Orbital input does not mutate simulation ship position or orientation.
Consumed orbital keys do not simultaneously command the ship.
18. Manual verification
Timo will verify:
Enter F3 near a station.
Ctrl+F3 enters orbital edit without a jump.
WASD moves smoothly around the ship at fixed distance.
Q/E visibly rolls the view without moving the camera.
After roll, WASD remains screen-relative.
R/F changes distance.
X restores the original rear-above view.
Ctrl+F3 leaves edit mode while preserving the chosen view.
Flying in normal chase retains that view.
A front-facing chase pose behaves like a rear-view camera while ship controls remain unchanged.
F3 off/on restores the customised pose.
No flicker or kilometre-scale lag returns.
HUD distances remain ship-relative.
Targeting follows the current chase/orbital view.
Entering slipstream returns to ordinary ship view.
19. Explicit non-goals
Do not implement:
free-flight camera;
camera drone;
repair or spying gameplay;
station or scenery collision avoidance;
camera-wall clipping prevention;
automatic camera repositioning;
alternate chase reticle;
additional chase HUD statistics;
mouse orbit;
debug-camera repair;
docking or landing work;
shadows;
engines;
exhaust;
further ship geometry.
20. Commit structure
Suggested commits:
Extract/clarify persistent ship-relative chase pose.
Add orbital movement and radius editing.
Add roll and reset.
Add Ctrl+F3 mode transitions and input consumption.
Enforce Newtonian-only availability.
Add tests and documentation.
Build and test after each coherent checkpoint.
Acceptance criteria
The brief is complete when:
F3 retains its stable snapshot-driven chase camera.
Ctrl+F3 toggles orbital editing while chase mode is active.
WASD moves the camera screen-relatively around a fixed-radius sphere.
Q/E rolls around the camera-to-ship axis without moving the camera.
R/F changes orbit radius.
X resets to the default chase pose.
Edited pose persists when returning to normal chase and across F3 toggles.
Camera always looks at ship centre.
Ship flight controls retain ship-local meaning.
Chase/orbital modes are unavailable in slipstream.
Snapshot translation remains exact and only relative presentation state is eased.
HUD and targeting retain their distinct correct truth sources.
Build and complete tests pass.
No deferred camera, ship, docking, engine, or shadow scope is included.
