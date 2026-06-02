using System.Diagnostics;

namespace Inferior.Core.Simulation;

/// <summary>
/// Runs the physics/power/damage/radar simulation at 60 Hz on a background thread.
/// Main thread calls SetInput() each frame; DataBus.Drain() dispatches results.
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

    // Called from main thread each frame — atomically replaces the input snapshot
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
        var input = _input; // read snapshot once — consistent across tick

        // Advance the central clock before any subsystem reads it
        GameClock.Advance(dt);

        // Sync Environment so sensors and noise have current ship state
        UpdateEnvironment();

        TickPhysics(input, dt);
        TickPower(dt);
        TickDamage(dt);
        TickRadar();
        Publish();
    }

    /// <summary>Push current ship state into Environment before sensor/noise reads.</summary>
    protected virtual void UpdateEnvironment() { }

    // ── Subsystems ────────────────────────────────────────────────────────────

    protected virtual void TickPhysics(PlayerInput input, double dt) { }

    protected virtual void TickPower(double dt) { }

    protected virtual void TickDamage(double dt) { }

    protected virtual void TickRadar() { }

    protected virtual void Publish() { }
}
