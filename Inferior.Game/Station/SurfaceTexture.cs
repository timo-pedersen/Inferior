namespace Inferior.Game.StationGen;

public enum SurfaceTexture
{
    CleanPanel,       // hab, luxury — off-white, minimal wear
    TechPanel,        // science, core, military — grey-blue, precise panels
    IndustrialPanel,  // industrial, fuel — dark grey, heavy wear
    CargoPanel,       // cargo — stained, reinforced-looking
    WornPanel,        // aged stations — patched, faded. Currently unreachable: SurfaceFor
                      // (StationGenerator.cs) never returns it. Read by S2b-2 (Age-driven
                      // wear tier), left as-is per Brief S2b-1 — not this brief's material.
    Glass,            // windows, portholes — neutral white (vertex colour passes through)
}
