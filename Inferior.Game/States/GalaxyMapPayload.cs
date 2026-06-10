using Inferior.Galaxy;

namespace Inferior.Game.States;

/// <summary>
/// Passed to GalaxyMapState when navigating there from in-flight (N key).
/// Carries the player's current system and game time so Esc can return to flight.
/// </summary>
public record GalaxyMapPayload(Star CurrentStar, double GameTime);
