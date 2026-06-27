namespace Inferior.Gameplay;

public enum FlightMode
{
    Docked,
    SystemNewtonian,          // Newtonian flight: gear ceiling, thrust taper, X-stop
    SystemSlipstream,         // Harmonic warp-speed flight
    AtmosphericNewtonian,     // Force-based atmospheric flight (gravity, drag, lift)
    AtmosphericSlipstream,    // High-speed atmospheric mode (was: FlightMode.Atmosphere + slipstream flag)
}
