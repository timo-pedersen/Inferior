using Inferior.Core.DataBus;

namespace Inferior.Gameplay.Components;

/// <summary>
/// Runs a configurable set of flyability checks and posts warnings to the system bus.
/// Operates without external power — internal low-power circuit, not simulated.
///
/// Checks are registered via AddCheck(). Each check returns null when OK or a
/// human-readable issue string when a problem is detected. The monitor re-runs
/// all checks every CheckInterval seconds and publishes any failures.
///
/// Intended usage: wire during ship construction so the player knows about
/// configuration problems before undocking (missing engine, disconnected bus, etc.).
/// Does not enforce anything — it only warns.
/// </summary>
public sealed class FlyabilityMonitor : ShipComponent
{
    public double CheckInterval { get; set; } = 5.0;  // seconds between sweeps

    private readonly List<(string Name, Func<string?> Check)> _checks = [];
    private double _cooldown;

    public FlyabilityMonitor(string name = "FlyabilityMonitor")
    {
        Name = name;
    }

    /// <summary>
    /// Add a named check. The check returns null when OK,
    /// or a short human-readable issue description when a problem is found.
    /// </summary>
    public void AddCheck(string name, Func<string?> check)
        => _checks.Add((name, check));

    public override void Tick(double dt)
    {
        _cooldown -= dt;
        if (_cooldown > 0.0) { TickSensors(); return; }
        _cooldown = CheckInterval;

        bool allClear = true;
        foreach (var (name, check) in _checks)
        {
            var issue = check();
            if (issue == null) continue;
            DataBus.System.Publish(Topics.System.All, $"[FLY] {name}: {issue}");
            allClear = false;
        }

        if (allClear && _checks.Count > 0)
            DataBus.System.Publish(Topics.System.All, "[FLY] All checks nominal");

        TickSensors();
    }
}
