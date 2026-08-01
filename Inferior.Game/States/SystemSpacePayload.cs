using Inferior.Core.Math;
using Inferior.Galaxy;
using Microsoft.Xna.Framework;

namespace Inferior.Game.States;

/// <summary>
/// Passed to SystemSpaceState on entry.
/// TargetBody     != null -> spawn near that body (approach from system map double-click).
/// StationArrival != null -> relocate near that station through the simulation.
/// SpawnPos       != null -> restore ship to saved position/orientation (back from maps).
/// All null                -> default spawn near star (first entry or galaxy-map Esc).
/// NavBody/NavStation      -> nav target selected on system map; applied to TargetingSystem on entry.
/// </summary>
public readonly record struct StationArrivalTarget(
    string PersistenceId,
    double SurfaceStandOffMeters,
    string? DisplayName = null);

public record SystemSpacePayload(
    Star           Star,
    OrbitalBody?   TargetBody,
    double         GameTime,
    CockpitLayout? Layout           = null,
    DVec3?         SpawnPos         = null,
    Quaternion?    SpawnOrientation = null,
    OrbitalBody?   NavBody          = null,
    Station?       NavStation       = null,
    StationArrivalTarget? StationArrival = null,
    bool           InitialNewGameStarterEntry = false);

public record SystemSpaceResumePayload(
    Star? HyperspaceTarget = null);
