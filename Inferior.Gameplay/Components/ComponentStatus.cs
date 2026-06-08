namespace Inferior.Gameplay.Components;

public enum ComponentStatus
{
    Stopped,       // no power, dormant
    PowerOn,       // power received; startup timer not yet started
    Initializing,  // startup timer running — warming up
    Started,       // fully operational
}
