namespace Inferior.Gameplay.Components;

public enum ComponentStatus
{
    PowerOff,      // switched off or never started — draws no power, does nothing
    PowerOn,       // power received; startup timer not yet started
    Initializing,  // startup timer running — warming up
    Running,       // fully operational
}
