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

    // ── Teleport request (main thread → sim thread) ───────────────────────────
    // Used by Home key (snap to origin) and F11 (sync debug cam ↔ ship position).
    // Immutable record + volatile ref — assignment is atomic, no partial reads.
    private sealed record TeleportRequest(DVec3 Position, Quaternion Orientation);
    private volatile TeleportRequest? _teleportRequest;

    public void RequestSnapToOrigin()
        => _teleportRequest = new TeleportRequest(new DVec3(0, 0.5e11, 3e11), Quaternion.CreateFromYawPitchRoll(0f, -0.2f, 0f));

    /// <summary>Teleport the ship to the given position and orientation next tick.</summary>
    public void TeleportShip(DVec3 position, Quaternion orientation)
        => _teleportRequest = new TeleportRequest(position, orientation);

    // ── World state snapshot (written by main thread, read by sim thread) ─────
    private sealed record WorldSnapshot(Star Star, StarSystem System, DVec3 ShipPos, double GameTime);
    private volatile WorldSnapshot? _worldSnapshot;

    /// <summary>Called from main thread each frame when in a star system.</summary>
    public void SetWorldState(Star star, StarSystem system, DVec3 refPos, double gameTime)
        => _worldSnapshot = new WorldSnapshot(star, system, refPos, gameTime);

    // ── Reference frame velocity (written by main thread, read by sim thread) ─
    private sealed record RefVelSnapshot(double X, double Y, double Z);
    private volatile RefVelSnapshot? _refVelSnapshot;

    /// <summary>Sets the flight-assist zero point — ship holds this velocity when not thrusting.</summary>
    public void SetReferenceVelocity(DVec3 vel)
        => _refVelSnapshot = new RefVelSnapshot(vel.X, vel.Y, vel.Z);

    // ── Sensors ───────────────────────────────────────────────────────────────
    private readonly GravitySensor              _gravity       = new();
    private readonly AtmosphericPressureSensor  _atmPressure   = new("AtmosphericSensor");
    private readonly SolarSpectrumSensor        _solarSpectrum = new("SolarSpectrumSensor");

    // dt stored in TickPhysics for use in Publish (Publish has no dt parameter)
    private double _lastDt;

    // ── Physics ───────────────────────────────────────────────────────────────

    protected override void TickPhysics(PlayerInput input, double dt)
    {
        _lastDt = dt;
        var ship = _ship;  // read once — volatile
        if (ship == null) return;

        // ── Teleport (Home key or debug-cam sync) ────────────────────────
        var teleport = _teleportRequest;
        if (teleport != null)
        {
            ship.Position = teleport.Position;
            ship.Velocity = DVec3.Zero;
            ship.SetOrientation(teleport.Orientation);
            _teleportRequest = null;
        }

        // ── Rotation ─────────────────────────────────────────────────────
        // PitchInput/YawInput carry raw angle deltas in radians (mouse delta × sensitivity).
        // RollInput is a -1..1 rate; ApplyRotation scales it by TurnRateRoll × dt.
        ship.ApplyRotation(input.PitchInput, input.YawInput, input.RollInput, dt);

        // ── Translation (velocity-target stub) ───────────────────────────
        // Replace with force-based Newtonian (F=ma) once engine/power system exists.
        var rv = _refVelSnapshot;
        var baseVel = rv != null ? new DVec3(rv.X, rv.Y, rv.Z) : DVec3.Zero;
        ship.ApplyVelocityTarget(input.ThrustForward, input.ThrustLateral, input.ThrustVertical, baseVel);
        ship.Position += ship.Velocity * dt;

        // ── Publish snapshot for main thread ──────────────────────────────
        _shipSnapshot = new ShipSnapshot(
            ship.Position, ship.Velocity, ship.Orientation, ship.CockpitWorldPosition);
    }

    // ── Power ─────────────────────────────────────────────────────────────────

    protected override void TickPower(double dt)
    {
        _ship?.TickComponents(dt);
    }

    // ── Environment ───────────────────────────────────────────────────────────

    protected override void UpdateEnvironment()
    {
        var snap = _worldSnapshot;
        if (snap == null) return;

        var world = SensorEnvironment.World;
        world.MassiveBodies.Clear();
        world.OrbitalBodies.Clear();

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

        // Use the reference position the main thread chose — it's already mode-aware:
        // ship position in flight, camera position in debug cam (see SystemSpaceState.SetWorldState).
        // The ship object stays alive in debug mode, so ship?.Position would give the frozen
        // spawn point rather than where the camera actually is.
        var ship  = _ship;
        DVec3 pos = snap.ShipPos;
        DVec3 vel = ship?.Velocity ?? DVec3.Zero;

        // Body positions are in ecliptic space (from GetPosition).
        // Ship position is in galaxy space. Rotate it back to ecliptic so the sensor
        // computes delta in a consistent coordinate frame.
        // Rodrigues' formula in double precision — avoids catastrophic cancellation that
        // occurs when casting 1e11 m coordinates to float before the rotation.
        // Inverse rotation = negate the angle (same axis, opposite direction).
        // Note: gravity direction is published in ecliptic space; SystemSpaceState
        // rotates it to galaxy space before display.
        double az   = snap.System.EclipticTiltAzimuthRadians;
        double tilt = snap.System.EclipticTiltRadians;
        double kx   = System.Math.Cos(az), kz = System.Math.Sin(az);  // ky = 0
        double cosA = System.Math.Cos(-tilt), sinA = System.Math.Sin(-tilt);
        double dot  = kx * pos.X + kz * pos.Z;                        // k·p (ky=0)
        DVec3 shipEcliptic = new DVec3(
            pos.X * cosA + (          - kz * pos.Y) * sinA + kx * dot * (1.0 - cosA),
            pos.Y * cosA + (kz * pos.X - kx * pos.Z) * sinA,
            pos.Z * cosA + (kx * pos.Y            ) * sinA + kz * dot * (1.0 - cosA));

        SensorEnvironment.UpdateFromSimThread(world, shipEcliptic, vel);
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
        world.OrbitalBodies.Add((body, pos));
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
        _atmPressure.Tick(_lastDt);
        _solarSpectrum.Tick(_lastDt);

        if (t >= _nextMessageAt)
        {
            DataBus.System.Publish(Topics.System.All, $"T+{t:F0}s - all systems nominal");
            _nextMessageAt += 8.0;
        }
    }
}
