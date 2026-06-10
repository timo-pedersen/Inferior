using Inferior.Core.Math;
using Inferior.Galaxy;
using Microsoft.Xna.Framework;

namespace Inferior.Game.States;

/// <summary>
/// Passed to SystemSpaceState on entry.
/// TargetBody  != null  → spawn near that body (approach from system map double-click).
/// SpawnPos    != null  → restore ship to saved position/orientation (back from maps).
/// Both null            → default spawn near star (first entry or galaxy-map Esc).
/// </summary>
public record SystemSpacePayload(
    Star           Star,
    OrbitalBody?   TargetBody,
    double         GameTime,
    CockpitLayout? Layout           = null,
    DVec3?         SpawnPos         = null,
    Quaternion?    SpawnOrientation = null);
