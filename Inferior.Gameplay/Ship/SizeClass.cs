namespace Inferior.Gameplay.Ship;

public enum ShipSizeClass
{
    Shuttle = 0,
    Small = 1,
    Medium = 2,
    Large = 3,
}

// Capital ships are intentionally deferred and currently unsized. Do not re-add a
// Capital enum member without a dedicated design and persistence decision.

// TODO - add methods for getting limitations for each size class, e.g. max component mass, max speed, etc.
// These will be used by the ship builder and the UI to enforce limits and provide feedback to the player.
