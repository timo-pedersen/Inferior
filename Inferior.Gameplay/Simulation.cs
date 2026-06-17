using System.Diagnostics;
using Inferior.Core.DataBus;
using Inferior.Core.Simulation;   // GameClock

namespace Inferior.Gameplay;

/// <summary>
/// Runs the physics/power/damage/radar simulation at 60 Hz on a background thread.
/// Main thread calls SetInput() each frame; DataBus.Drain() dispatches results.
///
/// Tick order each frame:
///   1. GameClock.Advance     — central time authority
///   2. UpdateEnvironment     — sync world state for sensors
///   3. TickPhysics           — thrust, positions, velocities
///   4. TickPower             — distribute power, generate heat
///   5. TickDamage            — heat/impact damage, component states
///   6. TickRadar             — scan nearby objects, publish contacts
///   7. Publish               — push live values to DataBus
/// </summary>
public class Simulation
{
    private const double TickRate = 1.0 / 60.0;

    private volatile PlayerInput _input   = PlayerInput.Zero;
    private volatile bool        _running = false;
    private Thread?              _thread;

    public void Start()
    {
        _running = true;
        _thread  = new Thread(Loop) { IsBackground = true, Name = "SimThread" };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(TimeSpan.FromMilliseconds(500));
    }

    /// <summary>Called from main thread each frame — atomically replaces the input snapshot.</summary>
    public void SetInput(PlayerInput input)
        => _input = input;

    private void Loop()
    {
        var    timer       = Stopwatch.StartNew();
        double accumulated = 0;

        while (_running)
        {
            accumulated += timer.Elapsed.TotalSeconds;
            timer.Restart();

            // Cap catch-up to 3 frames. Without this, OS thread suspension (alt-tab,
            // screenshot tools, process scheduling) causes a burst of hundreds of ticks
            // when the thread wakes — the ship jumps while the render camera didn't move.
            if (accumulated > TickRate * 3)
                accumulated = TickRate * 3;

            while (accumulated >= TickRate)
            {
                Tick(TickRate);
                accumulated -= TickRate;
            }

            Thread.Sleep(1); // yield — don't spin
        }
    }

    private void Tick(double dt)
    {
        CommandBus.Drain();          // apply any commands queued by the main thread
        var input = _input;          // read snapshot once — consistent across tick

        GameClock.Advance(dt);
        UpdateEnvironment();

        TickPhysics(input, dt);
        TickPower(dt);
        TickDamage(dt);
        TickRadar();
        TickEMP(dt);
        Publish();
    }

    // ── Subsystems — override in concrete subclasses ──────────────────────────

    /// <summary>Sync ship position/velocity into Environment for this tick's sensors.</summary>
    protected virtual void UpdateEnvironment() { }

    protected virtual void TickPhysics(PlayerInput input, double dt) { }

    protected virtual void TickPower(double dt) { }

    protected virtual void TickDamage(double dt) { }

    protected virtual void TickRadar() { }

    protected virtual void TickEMP(double dt)
    {
        SensorData.EMP.Tick(dt);
    }

    protected virtual void Publish() { }
}
