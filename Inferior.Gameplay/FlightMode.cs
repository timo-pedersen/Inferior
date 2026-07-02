namespace Inferior.Gameplay;

public enum FlightMode
{
    Docked,
    SystemNewtonian,          // Newtonian flight: gear ceiling, thrust taper, X-stop
    SystemSlipstream,         // Harmonic warp-speed flight
    AtmosphericNewtonian,     // Force-based atmospheric flight (gravity, drag, lift)
    AtmosphericSlipstream,    // High-speed atmospheric mode (was: FlightMode.Atmosphere + slipstream flag)
    EnteringFlatHyperspace,   // Preamble: ship auto-aligns, sheets animate in
    FlatHyperspace,           // Active 2-D hyperspace travel — left/right steering only
}
