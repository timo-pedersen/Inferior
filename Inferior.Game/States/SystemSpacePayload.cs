using Inferior.Galaxy;

namespace Inferior.Game.States;

/// <summary>
/// Passed from SystemMapState to SystemSpaceState.
/// Carries enough context to regenerate the system and spawn the camera near the target.
/// </summary>
public record SystemSpacePayload(
    Star           Star,
    OrbitalBody?   TargetBody,    // null = spawn near star
    double         GameTime,
    CockpitLayout? Layout = null);
