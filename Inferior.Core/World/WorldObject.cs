using Inferior.Core.Math;
using Microsoft.Xna.Framework;

namespace Inferior.Core.World;

/// <summary>
/// Small common state for an independently existing movable simulation object.
/// Angular velocity is a universe-space axis vector in radians per second.
/// </summary>
public sealed class WorldObject
{
    public WorldObjectId Id { get; }
    public DVec3 Position { get; internal set; }
    public Quaternion Orientation { get; internal set; }
    public DVec3 LinearVelocity { get; internal set; }
    public DVec3 AngularVelocity { get; internal set; }

    public WorldObject(
        WorldObjectId id,
        DVec3 position,
        Quaternion orientation,
        DVec3 linearVelocity,
        DVec3 angularVelocity)
    {
        if (id.Value == Guid.Empty) throw new ArgumentException("World-object identity cannot be empty.", nameof(id));
        if (!IsFinite(position)) throw new ArgumentException("Position must be finite.", nameof(position));
        if (!IsFinite(linearVelocity)) throw new ArgumentException("Linear velocity must be finite.", nameof(linearVelocity));
        if (!IsFinite(angularVelocity)) throw new ArgumentException("Angular velocity must be finite.", nameof(angularVelocity));
        if (!IsFinite(orientation) || orientation.LengthSquared() < 1e-12f)
            throw new ArgumentException("Orientation must be finite and non-zero.", nameof(orientation));

        Id = id;
        Position = position;
        Orientation = Quaternion.Normalize(orientation);
        LinearVelocity = linearVelocity;
        AngularVelocity = angularVelocity;
    }

    internal WorldObjectSnapshot CreateSnapshot()
        => new(Id, Position, Orientation, LinearVelocity, AngularVelocity);

    private static bool IsFinite(DVec3 value)
        => double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);
}

/// <summary>Immutable value copied across the simulation-to-presentation boundary.</summary>
public readonly record struct WorldObjectSnapshot(
    WorldObjectId Id,
    DVec3 Position,
    Quaternion Orientation,
    DVec3 LinearVelocity,
    DVec3 AngularVelocity);
