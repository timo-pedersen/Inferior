using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Core.Simulation;   // GameClock
using Inferior.Galaxy;
using Inferior.Gameplay;          // Simulation base
using Inferior.Gameplay.Physics;
using Inferior.Gameplay.Sensors;
using Inferior.Gameplay.Ship;
using Microsoft.Xna.Framework;
using SensorEnvironment = Inferior.Gameplay.SensorData.Environment;

namespace Inferior.Game;

/// <summary>
/// Concrete simulation for in-system flight.
/// Owns the player's Ship and drives its physics each tick.
/// Publishes live instrument values and sensor data to DataBus.
/// Runs on the sim thread — only calls DataBus.Publish (enqueue only, thread-safe).
/// </summary>
public sealed class SpaceSimulation : Simulation
{
    private double _nextMessageAt = 8.0;
    private bool   _startupPublished;
    private double _lastHeartbeat;

    // ── Ship ──────────────────────────────────────────────────────────────────
    // Written by main thread via SetShip(); read by sim thread each tick.
    // Volatile reference — assignment is atomic on 64-bit .NET.
    private volatile Ship? _ship;

    /// <summary>
    /// Sets the active ship. Call from the main thread when entering a system.
    /// The sim thread picks it up on the next tick.
    /// </summary>
    public void SetShip(Ship ship) => _ship = ship;

    // ── Ship state snapshot (written by sim thread, read by main thread) ──────
    public sealed record ShipSnapshot(
        DVec3      Position,
        DVec3      Velocity,
        Quaternion Orientation,
        DVec3      CockpitWorldPosition);

    private volatile ShipSnapshot? _shipSnapshot;

    /// <summary>Latest ship state. Null until the first physics tick completes.</summary>
    public ShipSnapshot? ShipState => _shipSnapshot;

    // ── Snap-to-origin request (Home key, main thread → sim thread) ───────────
    private volatile bool _snapToOriginRequested;
    public void RequestSnapToOrigin() => _snapToOriginRequested = true;

    // ── World state snapshot (written by main thread, read by sim thread) ─────
    private sealed record WorldSnapshot(Star Star, StarSystem System, DVec3 ShipPos, double GameTime);
    private volatile WorldSnapshot? _worldSnapshot;

    /// <summary>Called from main thread each frame when in a star system.</summary>
    public void SetWorldState(Star star, StarSystem system, DVec3 refPos, double gameTime)
        => _worldSnapshot = new WorldSnapshot(star, system, refPos, gameTime);

    // ── Sensors ───────────────────────────────────────────────────────────────
    private readonly GravitySensor _gravity = new();

    // ── Physics ───────────────────────────────────────────────────────────────

    protected override void TickPhysics(PlayerInput input, double dt)
    {
        var ship = _ship;  // read once — volatile
        if (ship == null) return;

        // ── Snap-to-origin (Home key) ─────────────────────────────────────
        if (_snapToOriginRequested)
        {
            ship.Position = new DVec3(0, 0.5e11, 3e11);
            ship.Velocity = DVec3.Zero;
            _snapToOriginRequested = false;
        }

        // ── Rotation ─────────────────────────────────────────────────────
        // PitchInput/YawInput carry raw angle deltas in radians (mouse delta × sensitivity).
        // RollInput is a -1..1 rate; ApplyRotation scales it by TurnRateRoll × dt.
        ship.ApplyRotation(input.PitchInput, input.YawInput, input.RollInput, dt);

        // ── Translation (velocity-target stub) ───────────────────────────
        // Replace with force-based Newtonian (F=ma) once engine/power system exists.
        ship.ApplyVelocityTarget(input.ThrustForward, input.ThrustLateral, input.ThrustVertical);
        ship.Position += ship.Velocity * dt;

        // ── Publish snapshot for main thread ──────────────────────────────
        _shipSnapshot = new ShipSnapshot(
            ship.Position, ship.Velocity, ship.Orientation, ship.CockpitWorldPosition);
    }

    // ── Environment ───────────────────────────────────────────────────────────

    protected override void UpdateEnvironment()
    {
        var snap = _worldSnapshot;
        if (snap == null) return;

        var world = SensorEnvironment.World;
        world.MassiveBodies.Clear();

        world.MassiveBodies.Add(new CelestialBody
        {
            Position       = DVec3.Zero,
            Mass           = snap.Star.MassKg,
            Radius         = snap.Star.RadiusMeters,
            Class          = snap.Star.SpectralClass,
            RotationPeriod = 2.192e6,
        });

        foreach (var planet in snap.System.Planets)
            CollectBody(world, planet, DVec3.Zero, snap.GameTime);

        // Prefer ship's authoritative position; fall back to main-thread reference pos
        // (used in debug camera mode when no ship physics are running for sensors)
        var ship  = _ship;
        DVec3 pos = ship?.Position ?? snap.ShipPos;
        DVec3 vel = ship?.Velocity ?? DVec3.Zero;
        SensorEnvironment.UpdateFromSimThread(world, pos, vel);
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

    // ── Publish ───────────────────────────────────────────────────────────────

    protected override void Publish()
    {
        double t = GameClock.SimTime;

        if (!_startupPublished)
        {
            DataBus.System.Publish(Topics.System.All, "Power systems online");
            DataBus.System.Publish(Topics.System.All, "Navigation ready");
            DataBus.System.Publish(Topics.System.All, "Sensors nominal");
            _startupPublished = true;
        }

        double heartbeat = System.Math.Sin(t * 0.614) * 50.0 + 50.0;
        DataBus.Instruments.Publish($"Debug.{Topics.Debug.Heartbeat}", heartbeat);
        DataBus.Instruments.Publish($"Debug.{Topics.Debug.SimTime}", t);

        if (_lastHeartbeat < 90.0 && heartbeat >= 90.0)
            DataBus.System.Publish(Topics.System.All, "Heartbeat threshold exceeded");
        if (_lastHeartbeat > 10.0 && heartbeat <= 10.0)
            DataBus.System.Publish(Topics.System.All, "Heartbeat below minimum");
        _lastHeartbeat = heartbeat;

        _gravity.Tick();

        if (t >= _nextMessageAt)
        {
            DataBus.System.Publish(Topics.System.All, $"T+{t:F0}s - all systems nominal");
            _nextMessageAt += 8.0;
        }
    }
}
