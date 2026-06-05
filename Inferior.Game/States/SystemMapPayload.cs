using Inferior.Galaxy;

namespace Inferior.Game.States;

/// <summary>
/// Passed from SystemSpaceState back to SystemMapState.
/// Carries star, restored game time, and cockpit panel state.
/// </summary>
public record SystemMapPayload(
    Star          Star,
    double        GameTime,
    CockpitLayout Layout);
