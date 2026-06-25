namespace Inferior.Galaxy;

public enum PlanetType
{
    Barren,      // Airless rocky body; no atmosphere
    Lava,        // Close to star; molten or near-molten surface
    Rocky,       // Solid surface; thin-to-moderate atmosphere
    Ocean,       // Liquid surface covers most of planet
    IcyRocky,    // Rocky with ice; thin or no atmosphere
    GasGiant,    // Hydrogen-dominant; no solid surface; extreme pressure
    IceGiant,    // Methane/ammonia; dense atmosphere; no solid surface
}

public enum AtmosphereCompositionType
{
    None,            // Airless — no atmosphere
    Trace,           // < 0.01 bar — effectively nothing
    CarbonDioxide,   // CO₂ dominant (Mars thin, Venus thick)
    NitrogenOxygen,  // Breathable composition (Earth-like)
    Methane,         // Methane-dominant (Titan-like)
    Ammonia,         // Cold outer worlds
    Hydrogen,        // Gas giant primary layer
}
