using Inferior.Core.DataBus;
using Inferior.Core.Simulation;

namespace Inferior.Game;

/// <summary>
/// Concrete simulation for in-system flight.
/// Overrides Publish() to emit live instrument values and periodic system messages.
/// Runs on the sim thread — only calls DataBus.Publish (enqueue only, thread-safe).
/// </summary>
public sealed class SpaceSimulation : Simulation
{
    private double _nextMessageAt = 8.0;  // first system message after 8 sim-seconds
    private bool   _startupPublished;
    private double _lastHeartbeat;        // for threshold-crossing detection

    protected override void Publish()
    {
        double t = GameClock.SimTime;

        // ── Startup message ───────────────────────────────────────────────────
        if (!_startupPublished)
        {
            DataBus.System.Publish(Topics.System.All, "Power systems online");
            DataBus.System.Publish(Topics.System.All, "Navigation ready");
            DataBus.System.Publish(Topics.System.All, "Sensors nominal");
            _startupPublished = true;
        }

        // ── Live instrument values ────────────────────────────────────────────

        // Heartbeat — oscillates 0–100, period ~20 s
        double heartbeat = System.Math.Sin(t * 0.614) * 50.0 + 50.0;
        DataBus.Instruments.Publish($"Debug.{Topics.Debug.Heartbeat}", heartbeat);

        // Sim clock
        DataBus.Instruments.Publish($"Debug.{Topics.Debug.SimTime}", t);

        // ── Threshold event ───────────────────────────────────────────────────
        if (_lastHeartbeat < 90.0 && heartbeat >= 90.0)
            DataBus.System.Publish(Topics.System.All, "Heartbeat threshold exceeded");
        if (_lastHeartbeat > 10.0 && heartbeat <= 10.0)
            DataBus.System.Publish(Topics.System.All, "Heartbeat below minimum");
        _lastHeartbeat = heartbeat;

        // ── Periodic status ───────────────────────────────────────────────────
        if (t >= _nextMessageAt)
        {
            DataBus.System.Publish(Topics.System.All,
                $"T+{t:F0}s - all systems nominal");
            _nextMessageAt += 8.0;
        }
    }
}
