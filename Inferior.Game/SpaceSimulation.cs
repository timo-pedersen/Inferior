using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Core.Simulation;   // GameClock
using Inferior.Galaxy;
using Inferior.Gameplay;          // Simulation base
using Inferior.Gameplay.Physics;
using Inferior.Gameplay.Sensors;
using SensorEnvironment = Inferior.Gameplay.SensorData.Environment;

namespace Inferior.Game;

/// <summary>
/// Concrete simulation for in-system flight.
/// Overrides Publish() to emit live instrument values and periodic system messages.
/// Runs on the sim thread — only calls DataBus.Publish (enqueue only, thread-safe).
/// </summary>
public sealed class SpaceSimulation : Simulation
{
    private double _nextMessageAt = 8.0;
    private bool   _startupPublished;
    private double _lastHeartbeat;

    // ── World state snapshot (written by main thread, read by sim thread) ─────
    private sealed record WorldSnapshot(Star Star, StarSystem System, DVec3 ShipPos, double GameTime);
    private volatile WorldSnapshot? _worldSnapshot;

    /// <summary>Called from main thread each frame when in a star system.</summary>
    public void SetWorldState(Star star, StarSystem system, DVec3 shipPos, double gameTime)
        => _worldSnapshot = new WorldSnapshot(star, system, shipPos, gameTime);

    // ── Sensors ───────────────────────────────────────────────────────────────
    private readonly GravitySensor _gravity = new();

    protected override void UpdateEnvironment()
    {
        var snap = _worldSnapshot;
        var world = SensorEnvironment.World;
        world.MassiveBodies.Clear();

        if (snap == null) return;

        world.MassiveBodies.Add(new CelestialBody
        {
            Position       = DVec3.Zero,
            Mass           = snap.Star.MassKg,
            Radius         = snap.Star.RadiusMeters,
            Class          = snap.Star.SpectralClass,
            RotationPeriod = 2.192e6, // ~25.4 days default
        });

        foreach (var planet in snap.System.Planets)
            CollectBody(world, planet, DVec3.Zero, snap.GameTime);

        SensorEnvironment.ShipPosition = snap.ShipPos;
    }

    private static void CollectBody(SimWorld world, OrbitalBody body, DVec3 parentPos, double gameTime)
    {
        DVec3 pos = body.GetPosition(gameTime, parentPos);
        world.MassiveBodies.Add(new CelestialBody
        {
            Position = pos,
            Mass     = body.MassKg,
            Radius   = body.RadiusMeters,
        });
        foreach (var child in body.Children)
            CollectBody(world, child, pos, gameTime);
    }

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

        // ── Sensors ───────────────────────────────────────────────────────────
        _gravity.Tick();

        // ── Periodic status ───────────────────────────────────────────────────
        if (t >= _nextMessageAt)
        {
            DataBus.System.Publish(Topics.System.All,
                $"T+{t:F0}s - all systems nominal");
            _nextMessageAt += 8.0;
        }
    }
}
