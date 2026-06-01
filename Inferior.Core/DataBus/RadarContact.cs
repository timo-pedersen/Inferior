using Microsoft.Xna.Framework;

namespace Inferior.Core.DataBus;

public readonly record struct RadarContact(
    string      Id,
    string      DisplayName,
    Vector3     RelativePosition,
    Vector3     RelativeVelocity,
    ContactType Type);

public enum ContactType { Ship, Station, Asteroid, Missile, Debris, Unknown }
