using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Core.Simulation;   // GameClock
using Inferior.Galaxy;
using Inferior.Gameplay;          // Simulation base, FlightMode
using Inferior.Gameplay.Components;
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
        DVec3      CockpitWorldPosition,
        DVec3      Forward,
        DVec3      Up,
        double     SimTime,
        FlightMode FlightMode      = FlightMode.Space,
        bool       FlightAssistOn  = true,
        bool       GlideModeActive = false);

    private volatile ShipSnapshot? _shipSnapshot;

    /// <summary>Latest ship state. Null until the first physics tick completes.</summary>
    public ShipSnapshot? ShipState => _shipSnapshot;

    /// <summary>Current flight mode readable from the main thread (via volatile ShipSnapshot).</summary>
    public FlightMode CurrentFlightMode => _shipSnapshot?.FlightMode ?? FlightMode.Space;

    // ── Teleport request (main thread → sim thread) ───────────────────────────
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
    // In space mode: dominant body's blended orbital velocity (flight-assist zero point).
    // In atmosphere mode: dominant body's full orbital velocity (ground reference for drag).
    private sealed record RefVelSnapshot(double X, double Y, double Z);
    private volatile RefVelSnapshot? _refVelSnapshot;

    /// <summary>Sets the flight-assist zero point — ship holds this velocity when not thrusting.</summary>
    public void SetReferenceVelocity(DVec3 vel)
        => _refVelSnapshot = new RefVelSnapshot(vel.X, vel.Y, vel.Z);

    // ── Ship move speed (written by main thread, read by sim thread) ──────────
    private long _shipSpeedBits = BitConverter.DoubleToInt64Bits(5e9);

    /// <summary>Sets the ship's effective move speed. Called from the main thread each frame.</summary>
    public void SetShipMoveSpeed(double speedMs)
        => System.Threading.Interlocked.Exchange(ref _shipSpeedBits, BitConverter.DoubleToInt64Bits(speedMs));

    // ── Flight mode (sim-internal; exposed read-only via ShipSnapshot) ─────────
    private FlightMode _currentFlightMode = FlightMode.Space;

    // ── Flight Assist & Glide (sim-owned state; toggled by rising edge in input) ─
    private bool   _flightAssistEnabled    = true;
    private bool   _glideModeActive        = false;
    private bool   _prevFlightAssistToggle = false;
    private bool   _prevGlideModeToggle    = false;
    // Glide charge delay: counts down from GlideStartupTime → 0, then sets _glideModeActive = true
    private double _glideChargeTimer;

    // ── Nearest atmospheric body (written by UpdateEnvironment, read by TickPhysics) ──
    // Both run on the sim thread — no cross-thread sync needed.
    private sealed record NearAtmBodyInfo(OrbitalBody Body, DVec3 EclipticPos, double AltitudeM);
    private NearAtmBodyInfo? _nearAtmBody;

    // Ecliptic tilt for EclipticToGalaxy conversion (written by UpdateEnvironment)
    private double _eclipticAz;
    private double _eclipticTilt;

    // ── Sensors ───────────────────────────────────────────────────────────────
    private readonly GravitySensor              _gravity        = new();
    private readonly AtmosphericPressureSensor  _atmPressure    = new("AtmosphericSensor");
    private readonly SolarSpectrumSensor        _solarSpectrum  = new("SolarSpectrumSensor");
    private readonly LandingSupportSystem       _landingSupport = new();

    // ── Pad target (main thread → sim thread) ─────────────────────────────────
    private volatile LandingPadData? _activePadTarget;

    public void SetPadTarget(LandingPadData? data) => _activePadTarget = data;

    // dt stored in TickPhysics for use in Publish (Publish has no dt parameter)
    private double _lastDt;

    // ── Physics constants ─────────────────────────────────────────────────────
    private const double PhysG           = 6.674e-11;
    private const double GlideMinSpeed   = 1_000.0;  // m/s — minimum forward speed in glide
    private const double GlideMaxSpeed   = 10_000.0; // m/s — maximum forward speed in glide
    private const double GlideMinDensity = 0.05;     // relative density — below this glide unavailable
    private const double GlideStartupTime = 2.0;     // seconds charge delay before glide engages
    private const double GlideAccelRate  = 200.0;    // m/s² — acceleration to reach min speed

    // ── TickPhysics ───────────────────────────────────────────────────────────

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
        ship.ApplyRotation(input.PitchInput, input.YawInput, input.RollInput, dt);

        // ── Flight Assist toggle (rising edge) ────────────────────────────
        if (input.FlightAssistToggle && !_prevFlightAssistToggle)
        {
            _flightAssistEnabled = !_flightAssistEnabled;
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage(_flightAssistEnabled ? "Flight Assist ON" : "Flight Assist OFF"));
        }
        _prevFlightAssistToggle = input.FlightAssistToggle;

        // ── Glide Mode toggle (rising edge) ──────────────────────────────
        if (input.GlideModeToggle && !_prevGlideModeToggle)
        {
            if (!_glideModeActive && _glideChargeTimer <= 0)
            {
                // Attempting to activate — check prerequisites
                bool shieldsActive = false;
                double nearDensity = 0;
                foreach (var c in ship.Components)
                {
                    if (c is ShieldComponent sc && sc.Status == ComponentStatus.Running && sc.CapacitorFill > 0)
                        shieldsActive = true;
                }
                var nb = _nearAtmBody;
                if (nb != null)
                    nearDensity = nb.Body.DensityAtAltitude(System.Math.Max(nb.AltitudeM, 0));

                if (shieldsActive)
                    DataBus.System.Publish(Topics.System.All,
                        new SystemMessage("Glide unavailable — shields active"));
                else if (nearDensity < GlideMinDensity)
                    DataBus.System.Publish(Topics.System.All,
                        new SystemMessage("Glide unavailable — insufficient atmospheric pressure"));
                else
                {
                    _glideChargeTimer = GlideStartupTime;
                    DataBus.System.Publish(Topics.System.All, new SystemMessage("Glide mode charging..."));
                }
            }
            else if (_glideModeActive)
            {
                // Deactivating — apply exit tumble if at high speed
                var rv = _refVelSnapshot;
                DVec3 groundVel  = rv != null ? new DVec3(rv.X, rv.Y, rv.Z) : DVec3.Zero;
                DVec3  relVel    = ship.Velocity - groundVel;
                double exitSpeed = DVec3.Dot(relVel, ship.Forward);
                double speedFrac = exitSpeed / GlideMaxSpeed;

                _glideModeActive  = false;
                _glideChargeTimer = 0;
                DataBus.System.Publish(Topics.System.All, new SystemMessage("Glide mode disengaged"));

                if (speedFrac > 0.5)
                {
                    double gyroFactor = ship.HasGyro ? 0.4 : 1.0;
                    double impulseMag = speedFrac * 2.0 * gyroFactor;
                    double ax = System.Random.Shared.NextDouble() - 0.5;
                    double ay = System.Random.Shared.NextDouble() - 0.5;
                    double len = System.Math.Sqrt(ax * ax + ay * ay);
                    if (len > 0.001)
                        ship.ApplyAngularImpulse(new DVec3(ax / len, ay / len, 0) * impulseMag);
                    DataBus.System.Publish(Topics.System.All,
                        new SystemMessage("Warning — high-speed glide exit", SystemMessagePriority.ImportantWarning));
                }
            }
        }
        _prevGlideModeToggle = input.GlideModeToggle;

        // ── FlightMode transition ─────────────────────────────────────────
        var nearBody = _nearAtmBody;  // set by UpdateEnvironment this tick
        FlightMode newMode = _currentFlightMode;

        if (nearBody == null)
        {
            newMode = FlightMode.Space;
        }
        else
        {
            double ceiling = nearBody.Body.AtmosphereCeilingAltitude;
            if (_currentFlightMode == FlightMode.Space && nearBody.AltitudeM < ceiling)
                newMode = FlightMode.Atmosphere;
            else if (_currentFlightMode == FlightMode.Atmosphere && nearBody.AltitudeM > ceiling * 1.1)
                newMode = FlightMode.Space;
        }

        if (newMode != _currentFlightMode)
        {
            _currentFlightMode = newMode;
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage(newMode == FlightMode.Atmosphere
                    ? "Entering atmosphere"
                    : "Leaving atmosphere"));
        }

        // ── Physics dispatch ──────────────────────────────────────────────
        if (_currentFlightMode == FlightMode.Atmosphere && nearBody != null)
            TickAtmospherePhysics(ship, input, nearBody, dt);
        else
            TickSpacePhysics(ship, input, dt);

        _shipSnapshot = new ShipSnapshot(
            ship.Position, ship.Velocity, ship.Orientation, ship.CockpitWorldPosition,
            ship.Forward, ship.Up,
            GameClock.SimTime,
            _currentFlightMode, _flightAssistEnabled, _glideModeActive);
    }

    // ── Space (velocity-target stub) ─────────────────────────────────────────

    private void TickSpacePhysics(Ship ship, PlayerInput input, double dt)
    {
        var rv      = _refVelSnapshot;
        var baseVel = rv != null ? new DVec3(rv.X, rv.Y, rv.Z) : DVec3.Zero;
        ship.MoveSpeedMs = BitConverter.Int64BitsToDouble(
            System.Threading.Interlocked.Read(ref _shipSpeedBits));
        ship.ApplyVelocityTarget(input.ThrustForward, input.ThrustLateral, input.ThrustVertical, baseVel);
        ship.Position += ship.Velocity * dt;
    }

    // ── Atmosphere (force-based Newtonian) ───────────────────────────────────

    private void TickAtmospherePhysics(Ship ship, PlayerInput input, NearAtmBodyInfo near, double dt)
    {
        var   body    = near.Body;
        DVec3 bodyPos = EclipticToGalaxy(near.EclipticPos);

        // Gravity
        DVec3  toBody    = bodyPos - ship.Position;
        double dist      = System.Math.Max(toBody.Length, body.RadiusMeters * 1.001);
        DVec3  gravDir   = toBody / dist;
        double gMag      = PhysG * body.MassKg / (dist * dist);
        DVec3  gravForce = gravDir * (gMag * ship.Mass);

        // Altitude & atmospheric density
        double altitude = dist - body.RadiusMeters;
        double density  = body.DensityAtAltitude(System.Math.Max(altitude, 0));

        // Ground velocity (main thread provides full orbital velocity via SetReferenceVelocity
        // when FlightMode == Atmosphere — see SystemSpaceState.UpdateReferenceVelocity)
        var   rv        = _refVelSnapshot;
        DVec3 groundVel = rv != null ? new DVec3(rv.X, rv.Y, rv.Z) : DVec3.Zero;

        // Glide charge timer — counts down then activates
        if (_glideChargeTimer > 0 && !_glideModeActive)
        {
            _glideChargeTimer -= dt;
            if (_glideChargeTimer <= 0)
            {
                _glideChargeTimer = 0;
                _glideModeActive  = true;
                DataBus.System.Publish(Topics.System.All, new SystemMessage("Glide mode active"));
            }
        }

        // Auto-deactivate glide if density drops below threshold (e.g. flying above atmosphere)
        if (_glideModeActive && density < GlideMinDensity)
        {
            _glideModeActive = false;
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage("Glide disengaged — insufficient atmosphere",
                    SystemMessagePriority.ImportantWarning));
        }

        DVec3 totalForce = gravForce;

        if (!_glideModeActive)
        {
            // Aerodynamic drag & lift against ground-relative velocity
            DVec3 velRel = ship.Velocity - groundVel;
            if (density > 0 && body.AtmosphereSurfaceDensity > 0)
            {
                double vFwd  = DVec3.Dot(velRel, ship.Forward);
                DVec3  vLat  = velRel - ship.Forward * vFwd;
                double vLatL = vLat.Length;

                totalForce -= ship.Forward * (ship.AerodynamicBrakeFront   * density * vFwd * System.Math.Abs(vFwd));
                if (vLatL > 0.001)
                    totalForce -= (vLat / vLatL) * (ship.AerodynamicBrakeLateral * density * vLatL * vLatL);
                if (ship.AerodynamicLift > 0)
                    totalForce += ship.Up * (ship.AerodynamicLift * density * vFwd * System.Math.Abs(vFwd));
            }

            // Engine thrust
            totalForce += ship.Forward * (input.ThrustForward  * ship.MaxForwardThrustN);
            totalForce += ship.Right   * (input.ThrustLateral   * ship.MaxDownThrustN * 0.5);
            totalForce += ship.Up      * (input.ThrustVertical   * ship.MaxDownThrustN);

            // Flight Assist: apply upward thrust to oppose gravity (battery-backed — no power draw)
            if (_flightAssistEnabled && density >= GlideMinDensity)
            {
                double faN = System.Math.Min(ship.MaxDownThrustN, gMag * ship.Mass);
                totalForce += ship.Up * faN;
            }
        }
        else
        {
            // Glide: no drag applied — speed is managed via direct velocity adjustment below
        }

        // Integrate forces
        ship.Velocity += totalForce / ship.Mass * dt;

        // Glide speed management — applied after force integration, directly on velocity
        if (_glideModeActive)
        {
            DVec3  relVel   = ship.Velocity - groundVel;
            double vForward = DVec3.Dot(relVel, ship.Forward);

            if (vForward < GlideMinSpeed)
            {
                double delta = System.Math.Min(GlideAccelRate * dt, GlideMinSpeed - vForward);
                ship.Velocity += ship.Forward * delta;
            }
            else if (vForward > GlideMaxSpeed)
            {
                double delta = System.Math.Min(GlideAccelRate * dt, vForward - GlideMaxSpeed);
                ship.Velocity -= ship.Forward * delta;
            }
        }

        ship.Position += ship.Velocity * dt;

        // Sphere collision
        const double CollisionRadius = 10.0;
        double distAfter = (ship.Position - bodyPos).Length;
        if (distAfter < body.RadiusMeters + CollisionRadius)
        {
            DVec3  outward   = (ship.Position - bodyPos) / distAfter;
            ship.Position    = bodyPos + outward * (body.RadiusMeters + CollisionRadius);
            double radialVel = DVec3.Dot(ship.Velocity, outward);
            if (radialVel < 0)
                ship.Velocity -= outward * radialVel;
        }
    }

    // ── Coordinate helper — ecliptic space → galaxy space ────────────────────
    // Inverse of the galaxy→ecliptic Rodrigues rotation in UpdateEnvironment
    // (same axis k, angle = +tilt instead of -tilt).

    private DVec3 EclipticToGalaxy(DVec3 ecl)
    {
        double kx   = System.Math.Cos(_eclipticAz);
        double kz   = System.Math.Sin(_eclipticAz);
        double cosT = System.Math.Cos(_eclipticTilt);
        double sinT = System.Math.Sin(_eclipticTilt);
        double dot  = kx * ecl.X + kz * ecl.Z;
        return new DVec3(
            ecl.X * cosT - kz * ecl.Y * sinT + kx * dot * (1.0 - cosT),
            ecl.Y * cosT + (kz * ecl.X - kx * ecl.Z) * sinT,
            ecl.Z * cosT + kx * ecl.Y * sinT + kz * dot * (1.0 - cosT));
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
        // ship position in flight, camera position in debug cam.
        var ship  = _ship;
        DVec3 pos = snap.ShipPos;
        DVec3 vel = ship?.Velocity ?? DVec3.Zero;

        // Body positions are in ecliptic space (from GetPosition).
        // Rotate ship position to ecliptic for consistent sensor deltas.
        // Rodrigues' formula, double precision — inverse rotation uses angle = -tilt.
        double az   = snap.System.EclipticTiltAzimuthRadians;
        double tilt = snap.System.EclipticTiltRadians;
        double kx   = System.Math.Cos(az), kz = System.Math.Sin(az);
        double cosA = System.Math.Cos(-tilt), sinA = System.Math.Sin(-tilt);
        double dot  = kx * pos.X + kz * pos.Z;
        DVec3 shipEcliptic = new DVec3(
            pos.X * cosA + (           - kz * pos.Y) * sinA + kx * dot * (1.0 - cosA),
            pos.Y * cosA + (kz * pos.X - kx * pos.Z) * sinA,
            pos.Z * cosA + (kx * pos.Y             ) * sinA + kz * dot * (1.0 - cosA));

        // Store for TickPhysics (same thread — written before TickPhysics runs)
        _eclipticAz   = az;
        _eclipticTilt = tilt;

        // Detect nearest body within 120% of atmosphere ceiling for FlightMode detection.
        // 120% provides a buffer zone so _nearAtmBody is non-null both entering and during
        // the 10% hysteresis used for exit.
        _nearAtmBody = null;
        double nearestDist = double.MaxValue;
        foreach (var (body, bodyEclipticPos) in world.OrbitalBodies)
        {
            double d   = (shipEcliptic - bodyEclipticPos).Length;
            double alt = d - body.RadiusMeters;
            if (alt < body.AtmosphereCeilingAltitude * 1.2 && d < nearestDist)
            {
                nearestDist  = d;
                _nearAtmBody = new NearAtmBodyInfo(body, bodyEclipticPos, alt);
            }
        }

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
            DataBus.System.Publish(Topics.System.All, new("Power systems online"));
            DataBus.System.Publish(Topics.System.All, new("Navigation ready"));
            DataBus.System.Publish(Topics.System.All, new("Sensors nominal"));
            _startupPublished = true;
        }

        double heartbeat = System.Math.Sin(t * 0.614) * 50.0 + 50.0;
        DataBus.Instruments.Publish($"Debug.{Topics.Debug.Heartbeat}", heartbeat);
        DataBus.Instruments.Publish($"Debug.{Topics.Debug.SimTime}", t);

        if (_lastHeartbeat < 90.0 && heartbeat >= 90.0)
            DataBus.System.Publish(Topics.System.All, new("Heartbeat threshold exceeded"));
        if (_lastHeartbeat > 10.0 && heartbeat <= 10.0)
            DataBus.System.Publish(Topics.System.All, new("Heartbeat below minimum"));
        _lastHeartbeat = heartbeat;

        _gravity.Tick();
        _atmPressure.Tick(_lastDt);
        _solarSpectrum.Tick(_lastDt);

        // Thermal signature = sum of heat generation rates across all heated components
        if (_ship != null)
        {
            double sig = 0.0;
            foreach (var c in _ship.Components)
                if (c.ThermalNode != null) sig += c.ThermalNode.LastHeatInputW;
            DataBus.Instruments.Publish(Topics.Ship.ThermalSignature, sig);
        }

        var snap = _shipSnapshot;
        if (_ship != null && snap != null)
        {
            _landingSupport.SelectPad(_activePadTarget);
            _landingSupport.Tick(snap.Position, snap.Forward, snap.Up);
        }
        else
        {
            DataBus.Instruments.Publish($"Ship.{Topics.LandingSupport.PadTargeted}", 0.0);
        }

        if (t >= _nextMessageAt)
        {
            DataBus.System.Publish(Topics.System.All, new($"T+{t:F0}s - all systems nominal"));
            _nextMessageAt += 8.0;
        }
    }
}
