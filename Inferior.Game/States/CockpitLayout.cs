namespace Inferior.Game.States;

/// <summary>
/// Snapshot of which cockpit panels are open and which tab is active on each edge.
/// Carried through state transitions so the layout survives system map round-trips.
/// When Ship class exists this will become a property of Ship.
/// </summary>
public record CockpitLayout(
    int  RightActiveTab,
    bool RightOpen,
    int  LeftActiveTab,
    bool LeftOpen)
{
    public static readonly CockpitLayout Default = new(-1, false, -1, false);
}
