namespace Inferior.Gameplay.Components.Power;

public enum PowerOutType
{
    Standard,       // Main bus — G/L/O/U band generators, whatever the ship uses
    RedAlpha,       // A-band dual-engine output — feeds gyros
    ShieldAlpha,    // V-Alpha sub-band — standard shields
    ShieldTheta,    // V-Theta sub-band — superior shields
    ArtificialGravity, // H-sw sub-band — inertial dampening and grav fields
                       // JbVortex?   // J-b sub-band — anti-shield weapons when we get there
}
