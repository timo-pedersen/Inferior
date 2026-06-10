using Inferior.Core.Math;
using Inferior.Galaxy;
using Microsoft.Xna.Framework;

namespace Inferior.Game.States;

/// <summary>
/// Passed to GalaxyMapState when navigating there from in-flight (N key) or system map (N key).
/// Carries the player's current system, game time, and ship position/orientation so
/// Esc or N can return to flight exactly where the player was.
/// </summary>
public record GalaxyMapPayload(
    Star       CurrentStar,
    double     GameTime,
    DVec3?     SpawnPos         = null,
    Quaternion? SpawnOrientation = null);
