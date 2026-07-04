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
    private volatile Ship? _ship;

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
        FlightMode FlightMode        = FlightMode.SystemNewtonian,
        bool       FlightAssistOn    = true,
        // Newtonian / LKM
        int        NewtonianGear     = 0,   // 0-based gear index
        int        NewtonianGearCount= 10,  // total gears available
        int        LkmMaxGear        = int.MaxValue,  // 0-based; int.MaxValue = no limit
        int        LkmZone           = 0,   // 0=none, 1/2/3
        double     LkmComplianceTimer= 0,
        bool       XStopActive       = false,
        // Slipstream
        int        SlipstreamHarmonicIndex = 0,
        double     ClunkPhase        = -1.0,   // -1 = inactive, 0→1 = animating
        // Speeds relative to reference frame
        double     RelativeSpeedMs   = 0.0,    // |vel - refVel| in m/s
        double     ForwardSpeedMs    = 0.0,    // dot(vel - refVel, forward) — signed
        double     AccelerationMs2   = 0.0);   // d(ForwardSpeedMs)/dt — signed, Newtonian only

    private volatile ShipSnapshot? _shipSnapshot;

    public ShipSnapshot? ShipState => _shipSnapshot;

    public FlightMode CurrentFlightMode => _shipSnapshot?.FlightMode ?? FlightMode.SystemNewtonian;

    // ── Teleport request (main thread → sim thread) ───────────────────────────
    private sealed record TeleportRequest(DVec3 Position, Quaternion Orientation);
    private volatile TeleportRequest? _teleportRequest;

    public void RequestSnapToOrigin()
        => _teleportRequest = new TeleportRequest(new DVec3(0, 0.5e11, 3e11), Quaternion.CreateFromYawPitchRoll(0f, -0.2f, 0f));

    public void TeleportShip(DVec3 position, Quaternion orientation)
        => _teleportRequest = new TeleportRequest(position, orientation);

    // ── Flight mode override (main thread → sim thread, used by hyperspace) ──
    // -1 = no override pending; otherwise cast to FlightMode.
    private volatile int _flightModeOverride = -1;

    /// <summary>
    /// Request the sim to set its flight mode on the next tick. Used when hyperspace exits
    /// need the sim to wake up in Newtonian or Slipstream without going through normal transitions.
    /// </summary>
    public void SetFlightMode(FlightMode mode) => _flightModeOverride = (int)mode;

    // ── World state snapshot (written by main thread, read by sim thread) ─────
    private sealed record WorldSnapshot(Star Star, StarSystem System, DVec3 ShipPos, double GameTime);
    private volatile WorldSnapshot? _worldSnapshot;

    public void SetWorldState(Star star, StarSystem system, DVec3 refPos, double gameTime)
        => _worldSnapshot = new WorldSnapshot(star, system, refPos, gameTime);

    // ── Reference frame velocity (main thread → sim thread) ──────────────────
    // In space: dominant body's blended orbital velocity (Newtonian zero-point).
    // In atmosphere: dominant body's full orbital velocity (ground reference).
    private sealed record RefVelSnapshot(double X, double Y, double Z);
    private volatile RefVelSnapshot? _refVelSnapshot;

    public void SetReferenceVelocity(DVec3 vel)
        => _refVelSnapshot = new RefVelSnapshot(vel.X, vel.Y, vel.Z);

    // ── Ship move speed (legacy — used by old velocity-target path; still read by debug cam proximity) ──
    private long _shipSpeedBits = BitConverter.DoubleToInt64Bits(5e9);

    public void SetShipMoveSpeed(double speedMs)
        => System.Threading.Interlocked.Exchange(ref _shipSpeedBits, BitConverter.DoubleToInt64Bits(speedMs));

    // ── Nearest station distance (main thread → sim thread) ──────────────────
    private long _nearestStationDistBits = BitConverter.DoubleToInt64Bits(double.MaxValue);

    public void SetNearestStationDistance(double meters)
        => System.Threading.Interlocked.Exchange(ref _nearestStationDistBits, BitConverter.DoubleToInt64Bits(meters));

    private double GetNearestStationDistance()
        => BitConverter.Int64BitsToDouble(System.Threading.Interlocked.Read(ref _nearestStationDistBits));

    // ── Flight mode (sim-internal) ────────────────────────────────────────────
    private FlightMode _currentFlightMode = FlightMode.SystemNewtonian;

    // ── Flight Assist (atmospheric only) ─────────────────────────────────────
    private bool _flightAssistEnabled    = true;
    private bool _prevFlightAssistToggle = false;

    // ── Atmospheric Slipstream state (used when _currentFlightMode == AtmosphericNewtonian) ─
    private bool   _slipstreamModeActive  = false;
    private bool   _prevSlipstreamToggle  = false;
    private double _slipstreamChargeTimer;
    private const double AtmoSlipstreamMinDensity  = 0.05;   // relative density threshold
    private const double AtmoSlipstreamStartupTime = 2.0;    // seconds charge delay
    private const double AtmoSlipstreamAccelRate   = 200.0;  // m/s² to reach min speed
    private const double AtmoSlipstreamMinSpeedMs  = 1_000.0;
    private const double AtmoSlipstreamMaxSpeedMs  = 10_000.0;

    // ── SystemNewtonian state ─────────────────────────────────────────────────
    private int  _newtonianGear  = 0;          // 0-based
    private int  _lkmMaxGear     = int.MaxValue;
    private bool _xStopActive    = false;
    private bool _prevXStopToggle = false;

    // ── SystemSlipstream state ────────────────────────────────────────────────
    private int    _slipstreamHarmonicIndex   = 0;
    private double _slipstreamCurrentSpeed    = 0;
    private double _slipstreamTargetSpeed     = 0;
    private double _slipstreamStartSpeed      = 0;   // speed when last gear shift began
    private bool   _slipstreamTransitioning   = false;
    private double _slipstreamTransitionTimer = 0;

    // ── Clunk animation (tick-counted, published in snapshot for render thread) ─
    private double _clunkTimer    = 0;
    private double _clunkDuration = 0;

    // ── LKM zone state ────────────────────────────────────────────────────────
    private int    _currentLkmZone    = 0;
    private double _lkmComplianceTimer = 0;
    private bool   _lkmPenaltyPending  = false;

    // ── Surface contact state ─────────────────────────────────────────────────
    private bool _surfaceContact;

    // ── Nearest atmospheric body (written by UpdateEnvironment, read by TickPhysics) ──
    private sealed record NearAtmBodyInfo(OrbitalBody Body, DVec3 EclipticPos, double AltitudeM);
    private NearAtmBodyInfo? _nearAtmBody;

    // Previous-tick forward speed, for computing signed acceleration.
    private double _prevFwdSpeedMs = 0.0;

    // Nearest body surface altitude (all bodies, regardless of atmosphere).
    private double _nearBodyAltitude = double.MaxValue;
    // Position, radius, and body ref of the body that owns _nearBodyAltitude (for position snap on dropout).
    private DVec3      _nearBodyEclipticPos;
    private double     _nearBodyRadius;
    private OrbitalBody? _nearBodyRef;

    // Ecliptic tilt (written by UpdateEnvironment)
    private double _eclipticAz;
    private double _eclipticTilt;

    // Galaxy-space velocity of the atmospheric body at atmosphere entry.
    private DVec3 _atmosphericPlanetVelocity;

    // ── Sensors ───────────────────────────────────────────────────────────────
    private readonly GravitySensor              _gravity              = new();
    private readonly AtmosphericPressureSensor  _atmPressure          = new("AtmosphericSensor");
    private readonly SolarSpectrumSensor        _solarSpectrum        = new("SolarSpectrumSensor");
    private readonly LandingSupportSystem       _landingSupport       = new();
    private readonly PlanetaryCoordinateSensor  _planetaryCoordSensor = new();

    // ── Pad target (main thread → sim thread) ─────────────────────────────────
    private volatile LandingPadData? _activePadTarget;

    public void SetPadTarget(LandingPadData? data) => _activePadTarget = data;

    private double _lastDt;

    // ── Physics constants ─────────────────────────────────────────────────────
    private const double PhysG               = 6.674e-11;
    private const double ShipCollisionRadius = 5.0;

    // ── TickPhysics ───────────────────────────────────────────────────────────

    protected override void TickPhysics(PlayerInput input, double dt)
    {
        _lastDt = dt;
        var ship = _ship;
        if (ship == null) return;

        // ── Teleport ─────────────────────────────────────────────────────
        var teleport = _teleportRequest;
        if (teleport != null)
        {
            ship.Position = teleport.Position;
            ship.Velocity = DVec3.Zero;
            ship.SetOrientation(teleport.Orientation);
            _teleportRequest = null;
        }

        // ── Flight mode override (from hyperspace exit) ───────────────────
        int modeOverride = _flightModeOverride;
        if (modeOverride >= 0)
        {
            _currentFlightMode  = (FlightMode)modeOverride;
            _flightModeOverride = -1;
        }

        // ── Rotation ─────────────────────────────────────────────────────
        ship.ApplyRotation(input.PitchInput, input.YawInput, input.RollInput, dt);

        // ── Flight Assist toggle (atmospheric only, rising edge) ──────────
        if (input.FlightAssistToggle && !_prevFlightAssistToggle)
        {
            _flightAssistEnabled = !_flightAssistEnabled;
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage(_flightAssistEnabled ? "Flight Assist ON" : "Flight Assist OFF"));
        }
        _prevFlightAssistToggle = input.FlightAssistToggle;

        // ── X-Stop toggle (rising edge) ───────────────────────────────────
        if (input.XStopToggle && !_prevXStopToggle)
        {
            if (_currentFlightMode == FlightMode.SystemNewtonian)
            {
                _xStopActive = !_xStopActive;
                DataBus.System.Publish(Topics.System.All,
                    new SystemMessage(_xStopActive ? "X-Stop active" : "X-Stop cancelled"));
            }
        }
        _prevXStopToggle = input.XStopToggle;

        // ── Slipstream / flight mode toggle (rising edge) ─────────────────
        if (input.SlipstreamToggle && !_prevSlipstreamToggle)
        {
            switch (_currentFlightMode)
            {
                case FlightMode.SystemNewtonian:
                    TryEnterSystemSlipstream(ship);
                    break;
                case FlightMode.SystemSlipstream:
                    ExitSystemSlipstream(ship);
                    break;
                case FlightMode.AtmosphericNewtonian:
                    if (!_slipstreamModeActive && _slipstreamChargeTimer <= 0)
                        TryEnterAtmosphericSlipstream(ship);
                    else if (_slipstreamModeActive)
                        ExitAtmosphericSlipstream(ship);
                    break;
            }
        }
        _prevSlipstreamToggle = input.SlipstreamToggle;

        // ── Newtonian gear shifts ─────────────────────────────────────────
        if (_currentFlightMode == FlightMode.SystemNewtonian)
        {
            double[] gears  = ship.NewtonianGears;
            int      topIdx = gears.Length - 1;
            int      prev   = _newtonianGear;
            if (input.GearUp   && _newtonianGear < topIdx)  _newtonianGear++;
            if (input.GearDown && _newtonianGear > 0)        _newtonianGear--;
            if (_newtonianGear != prev) TriggerClunk(ship.ClunkDurationMs);
        }
        else if (_currentFlightMode == FlightMode.SystemSlipstream)
        {
            if (input.GearUp || input.GearDown)
                ShiftSlipstreamHarmonic(ship, input.GearUp ? 1 : -1);
        }

        // ── FlightMode transition (Space ↔ Atmosphere) ───────────────────
        var   nearBody = _nearAtmBody;
        bool  inSpace  = _currentFlightMode is FlightMode.SystemNewtonian or FlightMode.SystemSlipstream;
        bool  inAtmo   = _currentFlightMode == FlightMode.AtmosphericNewtonian;
        FlightMode newMode = _currentFlightMode;

        if (nearBody == null)
        {
            if (!inSpace) newMode = FlightMode.SystemNewtonian;
        }
        else
        {
            double ceiling = nearBody.Body.AtmosphereCeilingAltitude;
            if (inSpace && nearBody.AltitudeM < ceiling)
                newMode = FlightMode.AtmosphericNewtonian;
            else if (inAtmo && nearBody.AltitudeM > ceiling * 1.1)
                newMode = FlightMode.SystemNewtonian;
        }

        if (newMode != _currentFlightMode)
        {
            // Entering atmosphere — clear any space Slipstream state
            if (newMode == FlightMode.AtmosphericNewtonian)
            {
                bool fromSlipstream = _currentFlightMode == FlightMode.SystemSlipstream;
                if (fromSlipstream)
                {
                    _slipstreamTransitioning = false;
                    _slipstreamCurrentSpeed  = 0;
                    _clunkTimer = 0;
                }

                var nb = nearBody!;
                DVec3 velEcl = nb.Body.SemiMajorAxis > 0.0 && nb.Body.ParentMassKg > 0.0
                    ? nb.Body.ComputeVelocity(GameClock.SimTime, Units.G * nb.Body.ParentMassKg, DVec3.Zero)
                    : SimpleOrbitalVelocityEcl(nb.Body, GameClock.SimTime);
                _atmosphericPlanetVelocity = EclipticToGalaxy(velEcl);
                // Slipstream speed is virtual — entering atmosphere directly from slipstream
                // would carry the full forward harmonic as real velocity. Zero it first so
                // planet-relative entry speed is 0 (gravity then accelerates normally from rest).
                if (fromSlipstream)
                    ship.Velocity = _atmosphericPlanetVelocity;
                ship.Velocity -= _atmosphericPlanetVelocity;
            }
            else if (newMode == FlightMode.SystemNewtonian)
            {
                ship.Velocity += _atmosphericPlanetVelocity;
                _atmosphericPlanetVelocity = DVec3.Zero;
                _slipstreamModeActive  = false;
                _slipstreamChargeTimer = 0;
            }

            _currentFlightMode = newMode;
            DataBus.System.Publish(Topics.System.All, new SystemMessage(
                newMode == FlightMode.AtmosphericNewtonian ? "Entering atmosphere" : "Leaving atmosphere"));
        }

        // ── LKM zone update (space modes only) ───────────────────────────
        if (_currentFlightMode is FlightMode.SystemNewtonian or FlightMode.SystemSlipstream)
            UpdateLkmZones(ship, dt);

        // ── Clunk timer (runs in all flight modes) ────────────────────────
        if (_clunkTimer > 0)
            _clunkTimer = System.Math.Max(0, _clunkTimer - dt);

        // ── Physics dispatch ──────────────────────────────────────────────
        switch (_currentFlightMode)
        {
            case FlightMode.SystemNewtonian:
                TickNewtonianPhysics(ship, input, dt);
                break;
            case FlightMode.SystemSlipstream:
                TickSystemSlipstreamPhysics(ship, dt);
                break;
            case FlightMode.AtmosphericNewtonian:
                if (nearBody != null)
                    TickAtmospherePhysics(ship, input, nearBody, dt);
                break;
        }

        // ── Snapshot ──────────────────────────────────────────────────────
        FlightMode snapMode = _currentFlightMode == FlightMode.AtmosphericNewtonian && _slipstreamModeActive
            ? FlightMode.AtmosphericSlipstream
            : _currentFlightMode;

        double clunkPhase = _clunkTimer > 0 && _clunkDuration > 0
            ? 1.0 - _clunkTimer / _clunkDuration
            : -1.0;

        DVec3  snapRefVel = GetRefVelocity();
        DVec3  snapRelVel = ship.Velocity - snapRefVel;
        double snapRelSpd = snapRelVel.Length;
        double snapFwdSpd = DVec3.Dot(snapRelVel, ship.Forward);
        double snapAccel  = dt > 0 ? (snapFwdSpd - _prevFwdSpeedMs) / dt : 0.0;
        _prevFwdSpeedMs   = snapFwdSpd;

        _shipSnapshot = new ShipSnapshot(
            ship.Position, ship.Velocity, ship.Orientation, ship.CockpitWorldPosition,
            ship.Forward, ship.Up,
            GameClock.SimTime,
            snapMode,
            _flightAssistEnabled,
            _newtonianGear,
            ship.NewtonianGears.Length,
            _lkmMaxGear,
            _currentLkmZone,
            _lkmComplianceTimer,
            _xStopActive,
            _slipstreamHarmonicIndex,
            clunkPhase,
            snapRelSpd,
            snapFwdSpd,
            snapAccel);
    }

    // ── SystemNewtonian physics ───────────────────────────────────────────────

    private void TickNewtonianPhysics(Ship ship, PlayerInput input, double dt)
    {
        double[] gears  = ship.NewtonianGears;
        int      maxIdx = System.Math.Min(
            _lkmMaxGear == int.MaxValue ? gears.Length - 1 : _lkmMaxGear,
            gears.Length - 1);
        if (_newtonianGear > maxIdx) _newtonianGear = maxIdx;

        double gearCeiling    = gears[_newtonianGear];
        double reverseCeiling = gearCeiling * FlightConstants.ReverseSpeedRatio;
        double accel          = _newtonianGear == 0
            ? FlightConstants.Gear1AccelerationMs2
            : ship.FlightAcceleration;

        DVec3 refVel  = GetRefVelocity();
        DVec3 relVel  = ship.Velocity - refVel;
        DVec3 fwdDir  = ship.Forward;

        // Any thrust input cancels X-Stop — lets the pilot override braking immediately
        // instead of having to wait for it to finish or re-press X.
        if (_xStopActive &&
            (input.ThrustForward != 0 || input.ThrustLateral != 0 || input.ThrustVertical != 0))
        {
            _xStopActive = false;
            DataBus.System.Publish(Topics.System.All, new SystemMessage("X-Stop cancelled"));
        }

        // X-Stop: maximum braking toward reference velocity
        if (_xStopActive)
        {
            double relSpeed = relVel.Length;
            if (relSpeed < FlightConstants.XStopSnapThreshold)
            {
                ship.Velocity = refVel;
                _xStopActive  = false;
                DataBus.System.Publish(Topics.System.All, new SystemMessage("X-Stop complete"));
            }
            else
            {
                ship.Velocity += relVel.Normalized() * (-accel * FlightConstants.XStopBrakeFactor * dt);
            }
            ship.Position += ship.Velocity * dt;
            return;
        }

        // Forward thrust — tapered as speed approaches gear ceiling
        if (input.ThrustForward > 0)
        {
            double speedAlongFwd = DVec3.Dot(relVel, fwdDir);
            double fraction      = System.Math.Clamp(speedAlongFwd / gearCeiling, 0, 1);
            double thrustFactor  = System.Math.Max(0,
                1.0 - System.Math.Pow(fraction, FlightConstants.ThrustTaperExponent));
            ship.Velocity += fwdDir * (accel * thrustFactor * input.ThrustForward * dt);
        }

        // Reverse thrust — separate ceiling
        if (input.ThrustForward < 0)
        {
            double speedAgainstFwd = DVec3.Dot(relVel, -fwdDir);
            double fraction        = System.Math.Clamp(speedAgainstFwd / reverseCeiling, 0, 1);
            double thrustFactor    = System.Math.Max(0,
                1.0 - System.Math.Pow(fraction, FlightConstants.ThrustTaperExponent));
            ship.Velocity += (-fwdDir) * (accel * thrustFactor * System.Math.Abs(input.ThrustForward) * dt);
        }

        // Strafe and vertical (uncapped — small inputs, no separate ceiling defined)
        double strafeAccel = accel * 0.5;
        if (input.ThrustLateral  != 0) ship.Velocity += ship.Right * (strafeAccel * input.ThrustLateral  * dt);
        if (input.ThrustVertical != 0) ship.Velocity += ship.Up    * (strafeAccel * input.ThrustVertical * dt);

        ship.Position += ship.Velocity * dt;
    }

    // ── SystemSlipstream physics ──────────────────────────────────────────────

    private void TickSystemSlipstreamPhysics(Ship ship, double dt)
    {
        // Forced dropout — planets
        if (_nearBodyAltitude < FlightConstants.SlipstreamPlanetDropoutAltitude)
        {
            // Snap position to dropout altitude along the surface normal so high-speed
            // approaches don't tunnel through the planet in a single frame.
            DVec3  bodyGalaxy = EclipticToGalaxy(_nearBodyEclipticPos);
            DVec3  toShip     = ship.Position - bodyGalaxy;
            double dist       = toShip.Length;
            if (dist > 1.0)
                ship.Position = bodyGalaxy + (toShip / dist)
                                * (_nearBodyRadius + FlightConstants.SlipstreamPlanetDropoutAltitude);

            // Zero relative speed — use the body's actual orbital velocity directly rather
            // than the blended reference velocity, which can lag by one tick near the dropout edge.
            if (_nearBodyRef is { SemiMajorAxis: > 0.0, ParentMassKg: > 0.0 } dropBody)
            {
                DVec3 velEcl = dropBody.ComputeVelocity(GameClock.SimTime, Units.G * dropBody.ParentMassKg, DVec3.Zero);
                ship.Velocity = EclipticToGalaxy(velEcl);
            }
            else
                ship.Velocity = GetRefVelocity();

            DataBus.System.Publish(Topics.System.All,
                new SystemMessage("Slipstream disengaged — proximity limit", SystemMessagePriority.ImportantWarning));
            ExitSystemSlipstreamToNewtonian(ship);
            TickNewtonianPhysics(ship, PlayerInput.Zero, dt);
            return;
        }

        // Forced dropout — stations
        if (GetNearestStationDistance() < FlightConstants.SlipstreamStationDropoutRange)
        {
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage("Slipstream disengaged — proximity limit", SystemMessagePriority.ImportantWarning));
            ExitSystemSlipstreamToNewtonian(ship, capVelocity: true);
            TickNewtonianPhysics(ship, PlayerInput.Zero, dt);
            return;
        }

        // Smooth ramp between harmonics
        if (_slipstreamTransitioning)
        {
            _slipstreamTransitionTimer -= dt;
            double rawT = 1.0 - System.Math.Clamp(
                _slipstreamTransitionTimer / FlightConstants.SlipstreamAccelSeconds, 0, 1);
            double t = rawT * rawT * (3.0 - 2.0 * rawT);  // smooth-step
            _slipstreamCurrentSpeed = _slipstreamStartSpeed
                + (_slipstreamTargetSpeed - _slipstreamStartSpeed) * t;

            if (_slipstreamTransitionTimer <= 0)
            {
                _slipstreamCurrentSpeed  = _slipstreamTargetSpeed;
                _slipstreamTransitioning = false;
            }
        }

        // Apply velocity — damped by proximity to stations/bodies so speed
        // visibly decreases on approach, leaving the ship near-stopped at dropout.
        double effectiveSpeed = _slipstreamCurrentSpeed * ComputeProximityScale();
        DVec3  refVel         = GetRefVelocity();
        ship.Velocity = refVel + ship.Forward * effectiveSpeed;
        ship.Position += ship.Velocity * dt;
    }

    // ── Atmosphere physics (force-based) ──────────────────────────────────────

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

        double altitude = dist - body.RadiusMeters;
        double density  = body.DensityAtAltitude(System.Math.Max(altitude, 0));
        DVec3  groundVel = DVec3.Zero;  // ship.Velocity is planet-relative

        // Atmospheric Slipstream charge timer
        if (_slipstreamChargeTimer > 0 && !_slipstreamModeActive)
        {
            _slipstreamChargeTimer -= dt;
            if (_slipstreamChargeTimer <= 0)
            {
                _slipstreamChargeTimer = 0;
                _slipstreamModeActive  = true;
                DataBus.System.Publish(Topics.System.All, new SystemMessage("Slipstream engaged"));
            }
        }

        // Auto-deactivate atmospheric Slipstream if density drops
        if (_slipstreamModeActive && density < AtmoSlipstreamMinDensity)
        {
            _slipstreamModeActive = false;
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage("Slipstream disengaged — insufficient atmosphere",
                    SystemMessagePriority.ImportantWarning));
        }

        DVec3 totalForce = gravForce;

        if (!_slipstreamModeActive)
        {
            // Drag and lift against ground-relative velocity
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

            totalForce += ship.Forward * (input.ThrustForward  * ship.MaxForwardThrustN);
            totalForce += ship.Right   * (input.ThrustLateral   * ship.MaxDownThrustN * 0.5);
            totalForce += ship.Up      * (input.ThrustVertical   * ship.MaxDownThrustN);

            if (_flightAssistEnabled && density >= AtmoSlipstreamMinDensity)
            {
                double faN = System.Math.Min(ship.MaxDownThrustN, gMag * ship.Mass);
                totalForce += ship.Up * faN;
            }
        }

        ship.Velocity += totalForce / ship.Mass * dt;

        // Atmospheric Slipstream speed management
        if (_slipstreamModeActive)
        {
            DVec3  relVel   = ship.Velocity - groundVel;
            double vForward = DVec3.Dot(relVel, ship.Forward);

            if (vForward < AtmoSlipstreamMinSpeedMs)
            {
                double delta = System.Math.Min(AtmoSlipstreamAccelRate * dt, AtmoSlipstreamMinSpeedMs - vForward);
                ship.Velocity += ship.Forward * delta;
            }
            else if (vForward > AtmoSlipstreamMaxSpeedMs)
            {
                double delta = System.Math.Min(AtmoSlipstreamAccelRate * dt, vForward - AtmoSlipstreamMaxSpeedMs);
                ship.Velocity -= ship.Forward * delta;
            }
        }

        ship.Position += (ship.Velocity + _atmosphericPlanetVelocity) * dt;

        if (near.Body.Planet != null)
            _planetaryCoordSensor.Tick(ship.Position, ship.Velocity, near.Body, bodyPos, dt);

        // Shield atmospheric depletion
        if (density > 0.0)
        {
            ShieldComponent? shield = null;
            foreach (var c in ship.Components)
                if (c is ShieldComponent sc && sc.Status == ComponentStatus.Running)
                    { shield = sc; break; }

            if (shield != null)
            {
                const double AtmDrainRate       = 5.0;
                const double ShieldHeatFactor   = 2_000.0;
                double drain = density * shield.CapacitorFill * AtmDrainRate * dt;
                drain = System.Math.Min(drain, shield.CapacitorFill);
                shield.DrainCapacitor(drain);
                shield.AddHeat(drain * ShieldHeatFactor);
            }
        }

        // Sphere collision
        double minDist   = body.RadiusMeters + ShipCollisionRadius;
        double distAfter = (ship.Position - bodyPos).Length;

        if (distAfter < minDist && distAfter > 0)
        {
            DVec3 surfaceNormal = (ship.Position - bodyPos) / distAfter;
            ship.Position = bodyPos + surfaceNormal * minDist;
            ship.Velocity = DVec3.Zero;

            if (!_surfaceContact)
            {
                _surfaceContact = true;
                DataBus.System.Publish(Topics.System.All,
                    new SystemMessage("Surface contact.", SystemMessagePriority.Info));
            }
        }
        else
        {
            _surfaceContact = false;
        }
    }

    // ── LKM zone detection ────────────────────────────────────────────────────

    private void UpdateLkmZones(Ship ship, double dt)
    {
        var    zones      = FlightConstants.StationLkmZones;
        double dist       = GetNearestStationDistance();
        int    newMax     = int.MaxValue;
        int    activeZone = 0;

        for (int z = zones.Length - 1; z >= 0; z--)
        {
            if (dist < zones[z].radius)
            {
                newMax     = zones[z].maxGearIndex;
                activeZone = z + 1;
                break;
            }
        }

        // Zone entry: message + start compliance window
        if (activeZone > _currentLkmZone)
        {
            double maxSpeed = FlightConstants.NewtonianGearSpeeds[System.Math.Min(newMax, FlightConstants.NewtonianGearSpeeds.Length - 1)];
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage($"LKM: Zone {activeZone} — max speed {maxSpeed:N0} m/s. " +
                    $"Comply within {FlightConstants.LkmComplianceWindow:N0}s."));
            _lkmComplianceTimer = FlightConstants.LkmComplianceWindow;
            _lkmPenaltyPending  = true;

            // Force exit from SystemSlipstream when entering any LKM zone
            if (_currentFlightMode == FlightMode.SystemSlipstream)
            {
                DataBus.System.Publish(Topics.System.All,
                    new SystemMessage("Slipstream disengaged — LKM zone", SystemMessagePriority.ImportantWarning));
                ExitSystemSlipstreamToNewtonian(ship);
            }
        }

        // Zone exit: clear penalty
        if (activeZone < _currentLkmZone)
            _lkmPenaltyPending = false;

        _currentLkmZone = activeZone;
        _lkmMaxGear     = newMax;

        // Compliance check
        if (_lkmPenaltyPending && activeZone > 0)
        {
            _lkmComplianceTimer -= dt;
            DVec3  refVel  = GetRefVelocity();
            double curSpd  = (ship.Velocity - refVel).Length;
            double limSpd  = FlightConstants.NewtonianGearSpeeds[System.Math.Min(newMax, FlightConstants.NewtonianGearSpeeds.Length - 1)];

            if (curSpd <= limSpd * 1.05)
            {
                _lkmPenaltyPending = false;  // complied in time
            }
            else if (_lkmComplianceTimer <= 0)
            {
                _lkmPenaltyPending = false;
                FlagLkmViolation();
            }
        }
    }

    private static void FlagLkmViolation()
        => DataBus.System.Publish(Topics.System.All,
            new SystemMessage("LKM violation recorded. Commander flagged.", SystemMessagePriority.ImportantWarning));

    // ── Slipstream helpers ────────────────────────────────────────────────────

    private void TryEnterSystemSlipstream(Ship ship)
    {
        if (_nearBodyAltitude < FlightConstants.SlipstreamPlanetDropoutAltitude)
        {
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage("Cannot engage Slipstream — clear space required"));
            return;
        }

        if (_currentLkmZone > 0)
        {
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage("Cannot engage Slipstream — LKM zone active"));
            return;
        }

        double[]  harmonics    = ship.SlipstreamHarmonics;
        DVec3     refVel       = GetRefVelocity();
        double    currentSpeed = System.Math.Max(0, DVec3.Dot(ship.Velocity - refVel, ship.Forward));

        // Start at lowest harmonic; ramp up from current speed
        _slipstreamHarmonicIndex   = 0;
        _slipstreamTargetSpeed     = harmonics[0];
        _slipstreamStartSpeed      = System.Math.Min(currentSpeed, harmonics[0]);
        _slipstreamCurrentSpeed    = _slipstreamStartSpeed;
        _slipstreamTransitioning   = true;
        _slipstreamTransitionTimer = FlightConstants.SlipstreamAccelSeconds;

        _currentFlightMode = FlightMode.SystemSlipstream;
        _xStopActive = false;
        DataBus.System.Publish(Topics.System.All, new SystemMessage("Slipstream engaged"));
    }

    private void ExitSystemSlipstream(Ship ship)
    {
        // Apply exit tumble at high speed
        double[]  harmonics  = ship.SlipstreamHarmonics;
        double    harmMax    = harmonics[harmonics.Length - 1];
        double    speedFrac  = _slipstreamCurrentSpeed / harmMax;

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
                new SystemMessage("Warning — high-speed Slipstream exit", SystemMessagePriority.ImportantWarning));
        }

        // Zero relative speed — set velocity to the gravity-dominant body's reference velocity.
        ship.Velocity = GetRefVelocity();

        DataBus.System.Publish(Topics.System.All, new SystemMessage("Slipstream disengaged"));
        ExitSystemSlipstreamToNewtonian(ship);
    }

    private void ExitSystemSlipstreamToNewtonian(Ship ship, bool capVelocity = false)
    {
        _slipstreamTransitioning = false;
        _slipstreamCurrentSpeed  = 0;
        _clunkTimer              = 0;
        _currentFlightMode       = FlightMode.SystemNewtonian;

        DVec3 refVel = GetRefVelocity();

        // Clamp exit speed near stations so the ship can brake without overshooting.
        // capVelocity is set when the dropout was triggered by a speed-advance check
        // (station may be further than 10 km but still too close for current speed).
        if (capVelocity || GetNearestStationDistance() < 10_000.0)
        {
            DVec3  relVel = ship.Velocity - refVel;
            double relSpd = relVel.Length;
            double maxSpd = FlightConstants.NewtonianGearSpeeds[3];  // 400 m/s — inner LKM zone limit
            if (relSpd > maxSpd && relSpd > 0)
                ship.Velocity = refVel + relVel * (maxSpd / relSpd);
        }

        // Auto-select gear matching exit speed
        double speed  = System.Math.Max(0, DVec3.Dot(ship.Velocity - refVel, ship.Forward));
        double[] gears = ship.NewtonianGears;
        _newtonianGear = gears.Length - 1;
        for (int i = 0; i < gears.Length; i++)
        {
            if (gears[i] >= speed) { _newtonianGear = i; break; }
        }
    }

    private void ShiftSlipstreamHarmonic(Ship ship, int direction)
    {
        if (_slipstreamTransitioning) return;  // debounce during transition

        double[] harmonics = ship.SlipstreamHarmonics;
        int newIdx = System.Math.Clamp(_slipstreamHarmonicIndex + direction, 0, harmonics.Length - 1);
        if (newIdx == _slipstreamHarmonicIndex) return;

        _slipstreamHarmonicIndex   = newIdx;
        _slipstreamStartSpeed      = _slipstreamCurrentSpeed;
        _slipstreamTargetSpeed     = harmonics[newIdx];
        _slipstreamTransitioning   = true;
        _slipstreamTransitionTimer = FlightConstants.SlipstreamAccelSeconds;
    }

    private void TriggerClunk(double durationMs)
    {
        _clunkDuration = durationMs / 1000.0;
        _clunkTimer    = _clunkDuration;
    }

    private void TryEnterAtmosphericSlipstream(Ship ship)
    {
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
                new SystemMessage("Slipstream unavailable — shields active"));
        else if (nearDensity < AtmoSlipstreamMinDensity)
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage("Slipstream unavailable — insufficient atmospheric pressure"));
        else
        {
            _slipstreamChargeTimer = AtmoSlipstreamStartupTime;
            DataBus.System.Publish(Topics.System.All, new SystemMessage("Slipstream charging..."));
        }
    }

    private void ExitAtmosphericSlipstream(Ship ship)
    {
        DVec3  refVel    = DVec3.Zero;  // planet-relative zero
        DVec3  relVel    = ship.Velocity - refVel;
        double exitSpeed = DVec3.Dot(relVel, ship.Forward);
        double speedFrac = exitSpeed / AtmoSlipstreamMaxSpeedMs;

        _slipstreamModeActive  = false;
        _slipstreamChargeTimer = 0;
        DataBus.System.Publish(Topics.System.All, new SystemMessage("Slipstream disengaged"));

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
                new SystemMessage("Warning — high-speed slipstream exit", SystemMessagePriority.ImportantWarning));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private DVec3 GetRefVelocity()
    {
        var rv = _refVelSnapshot;
        return rv != null ? new DVec3(rv.X, rv.Y, rv.Z) : DVec3.Zero;
    }

    /// <summary>
    /// Returns [0, 1] proximity damping for Slipstream speed.
    /// 1.0 far from any station or body; approaches 0 within ~20 km.
    /// Cubic dropoff over <see cref="FlightConstants.SlipstreamProximityDropoffM"/>.
    /// </summary>
    private double ComputeProximityScale()
    {
        double dropoff = FlightConstants.SlipstreamProximityDropoffM;
        double minDist = GetNearestStationDistance();
        if (_nearBodyAltitude < minDist) minDist = _nearBodyAltitude;
        if (minDist >= dropoff) return 1.0;
        double t = minDist / dropoff;  // 0 near object, 1 at dropoff edge
        return t * t * t;              // cubic: visible drop from ~80 km, near-zero at 20 km
    }

    private static DVec3 SimpleOrbitalVelocityEcl(OrbitalBody body, double simTime)
    {
        double angle = DMath.OrbitalAngle(simTime, body.Period, body.PhaseOffset);
        double omega = 2.0 * System.Math.PI / body.Period;
        return new DVec3(
            -System.Math.Sin(angle) * body.OrbitalRadius * omega,
            0.0,
            System.Math.Cos(angle) * body.OrbitalRadius * omega);
    }

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

        var ship  = _ship;
        DVec3 pos = snap.ShipPos;
        DVec3 vel = ship?.Velocity ?? DVec3.Zero;

        double az   = snap.System.EclipticTiltAzimuthRadians;
        double tilt = snap.System.EclipticTiltRadians;
        double kx   = System.Math.Cos(az), kz = System.Math.Sin(az);
        double cosA = System.Math.Cos(-tilt), sinA = System.Math.Sin(-tilt);
        double dot  = kx * pos.X + kz * pos.Z;
        DVec3 shipEcliptic = new DVec3(
            pos.X * cosA + (           - kz * pos.Y) * sinA + kx * dot * (1.0 - cosA),
            pos.Y * cosA + (kz * pos.X - kx * pos.Z) * sinA,
            pos.Z * cosA + (kx * pos.Y             ) * sinA + kz * dot * (1.0 - cosA));

        _eclipticAz   = az;
        _eclipticTilt = tilt;

        // Find nearest body surface altitude (for Slipstream dropout check)
        _nearBodyAltitude = double.MaxValue;
        _nearAtmBody      = null;
        double nearestDist = double.MaxValue;

        foreach (var (body, bodyEclipticPos) in world.OrbitalBodies)
        {
            double d   = (shipEcliptic - bodyEclipticPos).Length;
            double alt = d - body.RadiusMeters;

            // Track nearest surface altitude (all bodies); clamp to 0 so underground
            // readings don't poison ComputeProximityScale (which would give negative speed).
            if (alt < _nearBodyAltitude)
            {
                _nearBodyAltitude    = System.Math.Max(0.0, alt);
                _nearBodyEclipticPos = bodyEclipticPos;
                _nearBodyRadius      = body.RadiusMeters;
                _nearBodyRef         = body;
            }

            // Track nearest atmospheric body for FlightMode transitions.
            // Exclude alt < 0: ship underground in ecliptic space means a position mismatch;
            // accepting it would trigger atmosphere entry with the ship inside the planet.
            if (alt >= 0 && alt < body.AtmosphereCeilingAltitude * 1.2 && d < nearestDist)
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
        UpdatePlanetOrientation(body, gameTime);
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

    private static void UpdatePlanetOrientation(OrbitalBody body, double simTime)
    {
        if (body.Planet is null) return;
        PlanetData p      = body.Planet;
        double period     = System.Math.Abs(p.RotationPeriod);
        if (period < 1.0) return;
        double direction  = p.RotationPeriod >= 0.0 ? 1.0 : -1.0;
        double angle      = (direction * (2.0 * System.Math.PI * simTime / period) + p.RotationEpoch)
                            % (2.0 * System.Math.PI);
        body.Orientation  = Quaternion.CreateFromAxisAngle(p.PoleDirection, (float)angle);
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

        if (_ship != null)
        {
            double sig = 0.0;
            foreach (var c in _ship.Components)
                if (c.ThermalNode != null) sig += c.ThermalNode.LastHeatInputW;
            DataBus.Instruments.Publish(Topics.Ship.ThermalSignature, sig);
        }

        // Publish flight-mode topics for instrument subscribers
        var snap = _shipSnapshot;
        if (snap != null)
        {
            DataBus.Instruments.Publish(Topics.Flight.Mode,            (double)snap.FlightMode);
            DataBus.Instruments.Publish(Topics.Flight.Gear,            (double)(snap.NewtonianGear + 1));
            DataBus.Instruments.Publish(Topics.Flight.GearCount,       (double)snap.NewtonianGearCount);
            double gearCeil = snap.NewtonianGear < FlightConstants.NewtonianGearSpeeds.Length
                ? FlightConstants.NewtonianGearSpeeds[snap.NewtonianGear] : 0.0;
            DataBus.Instruments.Publish(Topics.Flight.GearCeilingMs,   gearCeil);
            DataBus.Instruments.Publish(Topics.Flight.MaxGear,
                snap.LkmMaxGear == int.MaxValue ? -1.0 : (double)(snap.LkmMaxGear + 1));
            DataBus.Instruments.Publish(Topics.Flight.HarmonicIndex,   (double)(snap.SlipstreamHarmonicIndex + 1));
            DataBus.Instruments.Publish(Topics.Flight.HarmonicCount,   (double)snap.NewtonianGearCount);
            DataBus.Instruments.Publish(Topics.Flight.LkmZone,         (double)snap.LkmZone);
            DataBus.Instruments.Publish(Topics.Flight.LkmCompliance,   snap.LkmComplianceTimer);
            DataBus.Instruments.Publish(Topics.Flight.XStopActive,     snap.XStopActive ? 1.0 : 0.0);
            DataBus.Instruments.Publish(Topics.Flight.RelativeSpeedMs,  snap.RelativeSpeedMs);
            DataBus.Instruments.Publish(Topics.Flight.ForwardSpeedMs,   snap.ForwardSpeedMs);
            DataBus.Instruments.Publish(Topics.Flight.AccelerationMs2,  snap.AccelerationMs2);
            DataBus.Instruments.Publish(Topics.Ship.WarnLevel,         0.0);  // stub — connected to real systems in future brief
        }

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
