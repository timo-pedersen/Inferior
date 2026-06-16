using Inferior.Core.Math;
using Inferior.Galaxy;

namespace Inferior.Gameplay.Sensors;

/// <summary>
/// Immutable snapshot of a targeted landing pad, passed from the main thread to the sim thread.
/// </summary>
public sealed record LandingPadData(
    DVec3   WorldPosition,
    DVec3   WorldNormal,
    DVec3   ForwardAxis,   // pad-forward in world space — derived via tangent-frame from WorldNormal
    PadSize PadSize,
    string  BayId,
    string  StationName);
