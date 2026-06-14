using Inferior.Core.Math;
using Inferior.Galaxy;
using Microsoft.Xna.Framework;

namespace Inferior.Game.States;

/// <summary>
/// Passed from SystemSpaceState to SystemMapState.
/// Carries star, game time, cockpit layout, and the ship's last position/orientation
/// so that returning to flight restores exactly where the player was.
/// NavBody/NavStation: currently selected nav target in flight; shown highlighted on map.
/// </summary>
public record SystemMapPayload(
    Star          Star,
    double        GameTime,
    CockpitLayout Layout,
    DVec3?        SpawnPos         = null,
    Quaternion?   SpawnOrientation = null,
    OrbitalBody?  NavBody          = null,
    Station?      NavStation       = null);
