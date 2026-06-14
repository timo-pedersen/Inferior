using Inferior.Core.Math;

namespace Inferior.Galaxy;

// ── Pad size ──────────────────────────────────────────────────────────────────

public enum PadSize
{
    Small,
    Large,
}

// ── Landing pad ───────────────────────────────────────────────────────────────

/// <summary>
/// A single landing pad on a station exterior.
/// Position is relative to the station centre in station-local space.
/// Populated by station geometry generation (Phase 2).
/// </summary>
public sealed class LandingPad
{
    public int     PadIndex    { get; init; }
    public PadSize PadSize     { get; init; }
    public bool    IsOccupied  { get; set; }

    /// <summary>
    /// Position of this pad relative to the station centre, in station-local space (metres).
    /// Set by the station geometry generator when the station mesh is built.
    /// Zero until geometry is generated.
    /// </summary>
    public DVec3 LocalPosition { get; set; }

    /// <summary>
    /// Normal vector of the pad surface in station-local space (points away from station).
    /// Zero until geometry is generated.
    /// </summary>
    public DVec3 LocalNormal { get; set; }
}
