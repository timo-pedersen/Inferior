using Inferior.Core.Math;
using Microsoft.Xna.Framework;

namespace Inferior.Core.World;

/// <summary>
/// Mutable registry intended to be owned by one simulation authority. It deliberately
/// contains no renderer, persistence-format, collision, attachment, or physics handles.
/// </summary>
public sealed class WorldObjectRegistry
{
    private readonly Dictionary<WorldObjectId, WorldObject> _objects = [];

    public int Count => _objects.Count;
    public IEnumerable<WorldObject> Objects => _objects.Values;

    public bool Add(WorldObject worldObject)
    {
        ArgumentNullException.ThrowIfNull(worldObject);
        return _objects.TryAdd(worldObject.Id, worldObject);
    }

    public bool Remove(WorldObjectId id)
        => _objects.Remove(id);

    public bool TryGet(WorldObjectId id, out WorldObject? worldObject)
        => _objects.TryGetValue(id, out worldObject);

    public void Clear() => _objects.Clear();

    /// <summary>
    /// Temporary free-motion integrator used until a physics representation is introduced.
    /// This is kinematic continuity only; it performs no collision detection or response.
    /// </summary>
    public void IntegrateFreeMotion(double dt)
    {
        if (!double.IsFinite(dt) || dt < 0.0)
            throw new ArgumentOutOfRangeException(nameof(dt), "Time step must be finite and non-negative.");
        if (dt == 0.0) return;

        foreach (WorldObject worldObject in _objects.Values)
        {
            worldObject.Position += worldObject.LinearVelocity * dt;

            double angularSpeed = worldObject.AngularVelocity.Length;
            if (angularSpeed <= 0.0) continue;

            DVec3 axis = worldObject.AngularVelocity / angularSpeed;
            var delta = Quaternion.CreateFromAxisAngle(axis.ToVector3(), (float)(angularSpeed * dt));
            worldObject.Orientation = Quaternion.Normalize(delta * worldObject.Orientation);
        }
    }

    public IReadOnlyList<WorldObjectSnapshot> CreateSnapshot()
    {
        var snapshots = new WorldObjectSnapshot[_objects.Count];
        int index = 0;
        foreach (WorldObject worldObject in _objects.Values)
            snapshots[index++] = worldObject.CreateSnapshot();
        return Array.AsReadOnly(snapshots);
    }
}
