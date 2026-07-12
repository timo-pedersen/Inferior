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
    public sealed record StationProximityTickDiagnostic(
        long TickSequence,
        double EnvironmentSimTime,
        DVec3 EnvironmentShipPosition,
        string? NearestStationName,
        string? NearestStationId,
        Station? NearestStation,
        DVec3 StationEclipticPosition,
        DVec3 StationGalaxyPosition,
        double RawCentreDistance,
        double PhysicalRadius,
        double SurfaceDistance,
        int PublishedLkmZone,
        int PublishedMaxGearIndex,
        double SnapshotSimTime,
        DVec3 SnapshotShipPosition,
        DVec3 ShipMovementDuringTick,
        FlightMode PublishedFlightMode);

    public sealed record MainStationProximityDiagnostic(
        DateTime RequestedAtUtc,
        Star MainStar,
        StarSystem MainSystem,
        string? TargetStationName,
        string? TargetStationId,
        Station? TargetStation,
        double MainTime,
        DVec3 TargetStationEclipticPosition,
        DVec3 TargetStationGalaxyPosition,
        DVec3 CameraUniversePosition,
        DVec3? ShipSnapshotPosition,
        double CameraToStationDistance,
        double? ShipSnapshotToStationDistance);

    private readonly record struct StationProximitySample(
        Station? Station,
        DVec3 EclipticPosition,
        DVec3 GalaxyPosition,
        double CentreDistance,
        double PhysicalRadius,
        double SurfaceDistance);

    internal readonly record struct LkmClassification(int Zone, int MaxGear);

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
        long       TickSequence      = 0,
        FlightMode FlightMode        = FlightMode.SystemNewtonian,
        bool       FlightAssistOn    = true,
        // Newtonian / LKM
        int        NewtonianGear     = 0,   // 0-based gear index
        int        NewtonianGearCount= 10,  // total gears available
        int        LkmMaxGear        = int.MaxValue,  // 0-based; int.MaxValue = no limit
        int        LkmZone           = 0,   // 0=none, 1/2/3
        double     LkmComplianceTimer= 0,
        bool       XStopActive       = false,
        bool       AfterburnerActive = false,   // blocks the H hyperspace trigger on the main thread
        // Slipstream
        int        SlipstreamHarmonicIndex = 0,
        double     ClunkPhase        = -1.0,   // -1 = inactive, 0→1 = animating
        DVec3      ReferenceVelocity = default,
        string     ReferenceName     = "",
        string     ReferenceSourceId = "",
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

    private sealed record StationRelocationRequest(string StationPersistenceId, double SurfaceStandOffMeters);
    private volatile StationRelocationRequest? _stationRelocationRequest;
    private bool _stationRelocationAppliedThisTick;

    public void RequestStationRelocation(string stationPersistenceId, double surfaceStandOffMeters)
        => _stationRelocationRequest = new StationRelocationRequest(stationPersistenceId, surfaceStandOffMeters);

    // ── Flight mode override (main thread → sim thread, used by hyperspace) ──
    // -1 = no override pending; otherwise cast to FlightMode.
    private volatile int _flightModeOverride = -1;

    /// <summary>
    /// Request the sim to set its flight mode on the next tick. Used when hyperspace exits
    /// need the sim to wake up in Newtonian or Slipstream without going through normal transitions.
    /// </summary>
    public void SetFlightMode(FlightMode mode) => _flightModeOverride = (int)mode;

    // ── System context install (requested by main thread, applied by sim thread) ─────
    private sealed record SystemContext(Star Star, StarSystem System);
    private volatile SystemContext? _pendingSystemContext;
    private SystemContext? _systemContext;

    public void InstallSystem(Star star, StarSystem system)
        => _pendingSystemContext = new SystemContext(star, system);

    private volatile MainStationProximityDiagnostic? _stationProximityDiagnosticRequest;

    public void RequestStationProximityDiagnostic(MainStationProximityDiagnostic diagnostic)
        => _stationProximityDiagnosticRequest = diagnostic;

    // ── Ship move speed (legacy — used by old velocity-target path; still read by debug cam proximity) ──
    private long _shipSpeedBits = BitConverter.DoubleToInt64Bits(5e9);

    public void SetShipMoveSpeed(double speedMs)
        => System.Threading.Interlocked.Exchange(ref _shipSpeedBits, BitConverter.DoubleToInt64Bits(speedMs));

    // ── Nearest station surface distance (sim-owned; used by LKM and Slipstream) ──
    private double _nearestStationDistance = double.MaxValue;
    private Station? _nearestStation;
    private DVec3 _nearestStationEclipticPosition;
    private DVec3 _nearestStationGalaxyPosition;
    private DVec3 _nearestStationShipPosition;
    private double _nearestStationSimTime;
    private double _nearestStationCentreDistance = double.MaxValue;
    private double _nearestStationPhysicalRadius;
    private long _stationProximityTickSequence;
    private long _currentStationProximityTickSequence;
    private volatile StationProximityTickDiagnostic? _lastStationProximityTickDiagnostic;

    public StationProximityTickDiagnostic? LastStationProximityTickDiagnostic
        => _lastStationProximityTickDiagnostic;

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
    // Tracks whether "X-Stop complete" has already been sent for the current activation —
    // X-Stop now holds indefinitely once threshold is crossed (see TickNewtonianPhysics),
    // so the message must not repeat every tick while holding.
    private bool _xStopCompleteAnnounced = false;

    // ── Afterburner (SystemNewtonian only) ────────────────────────────────────
    private bool               _afterburnerActive        = false;
    private double             _afterburnerTimeRemaining = 0;
    private bool               _prevAfterburnerToggle    = false;
    private readonly System.Random _afterburnerShakeRng  = new();

    // Continuous reference-frame carry (see TickNewtonianPhysics) — tracks the previous
    // tick's reference velocity and which object it came from, so the ship can be carried
    // along with however the reference accelerates every tick (same principle already used
    // for planets in atmosphere) without integrating a phantom jump when the reference
    // SOURCE changes (e.g. crossing the 25km station-proximity boundary) rather than the
    // same source actually changing speed.
    private DVec3  _prevRefVel      = DVec3.Zero;
    private string _prevRefSourceId = "";
    private DVec3  _referenceVelocity;
    private string _referenceSourceId = "";
    private string _referenceName     = "";

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

    internal void TickForTests(PlayerInput input, double dt)
    {
        CommandBus.Drain();
        GameClock.Advance(dt);
        UpdateEnvironment();
        TickPhysics(input, dt);
        TickPower(dt);
        TickDamage(dt);
        TickRadar();
        TickEMP(dt);
        Publish();
    }

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

        UpdateReferenceFrame(ship);
        if (_stationRelocationAppliedThisTick)
        {
            _stationRelocationAppliedThisTick = false;
            goto PublishSnapshot;
        }

        // ── Afterburner toggle (rising edge; SystemNewtonian only; no re-trigger while active) ──
        if (input.AfterburnerToggle && !_prevAfterburnerToggle && !_afterburnerActive
            && _currentFlightMode == FlightMode.SystemNewtonian)
        {
            _afterburnerActive        = true;
            _afterburnerTimeRemaining = FlightConstants.AfterburnerDurationSeconds;
            DataBus.System.Publish(Topics.System.All, new SystemMessage("Afterburner engaged"));
        }
        _prevAfterburnerToggle = input.AfterburnerToggle;

        if (_afterburnerActive)
        {
            _afterburnerTimeRemaining -= dt;
            if (_afterburnerTimeRemaining <= 0)
            {
                _afterburnerActive = false;
                DataBus.System.Publish(Topics.System.All, new SystemMessage("Afterburner burned out"));
            }
        }

        // ── Rotation ─────────────────────────────────────────────────────
        // While the afterburner burns, add a small random pitch/yaw jitter on top of mouse-look —
        // a Brownian-ish shake that also nudges the ship's real forward vector (and so its actual
        // travel direction) slightly, rather than a purely cosmetic camera overlay. Roll is
        // untouched either way.
        double pitchInput = input.PitchInput;
        double yawInput   = input.YawInput;
        if (_afterburnerActive)
        {
            pitchInput += (_afterburnerShakeRng.NextDouble() * 2.0 - 1.0) * FlightConstants.AfterburnerShakeRadians;
            yawInput   += (_afterburnerShakeRng.NextDouble() * 2.0 - 1.0) * FlightConstants.AfterburnerShakeRadians;
        }
        ship.ApplyRotation(pitchInput, yawInput, input.RollInput, dt);

        // ── Flight Assist toggle (atmospheric only, rising edge) ──────────
        if (input.FlightAssistToggle && !_prevFlightAssistToggle)
        {
            _flightAssistEnabled = !_flightAssistEnabled;
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage(_flightAssistEnabled ? "Flight Assist ON" : "Flight Assist OFF"));
        }
        _prevFlightAssistToggle = input.FlightAssistToggle;

        // ── X-Stop toggle (rising edge) ───────────────────────────────────
        if (input.XStopToggle && !_prevXStopToggle && !_afterburnerActive)
        {
            if (_currentFlightMode == FlightMode.SystemNewtonian)
            {
                _xStopActive = !_xStopActive;
                if (_xStopActive) _xStopCompleteAnnounced = false;
                DataBus.System.Publish(Topics.System.All,
                    new SystemMessage(_xStopActive ? "X-Stop active" : "X-Stop cancelled"));
            }
        }
        _prevXStopToggle = input.XStopToggle;

        // ── Slipstream / flight mode toggle (rising edge) ─────────────────
        if (input.SlipstreamToggle && !_prevSlipstreamToggle && !_afterburnerActive)
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
            UpdateReferenceFrame(ship);
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
        PublishSnapshot:
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

        long snapTickSequence = _currentStationProximityTickSequence;
        DVec3 shipMovementDuringTick = ship.Position - _nearestStationShipPosition;
        var postPhysicsProximity = ComputeNearestStationProximity(ship.Position, GameClock.SimTime);
        var postPhysicsLkm = ClassifyLkm(postPhysicsProximity.SurfaceDistance);

        _shipSnapshot = new ShipSnapshot(
            ship.Position, ship.Velocity, ship.Orientation, ship.CockpitWorldPosition,
            ship.Forward, ship.Up,
            GameClock.SimTime,
            snapTickSequence,
            snapMode,
            _flightAssistEnabled,
            _newtonianGear,
            ship.NewtonianGears.Length,
            postPhysicsLkm.MaxGear,
            postPhysicsLkm.Zone,
            _lkmComplianceTimer,
            _xStopActive,
            _afterburnerActive,
            _slipstreamHarmonicIndex,
            clunkPhase,
            _referenceVelocity,
            _referenceName,
            _referenceSourceId,
            snapRelSpd,
            snapFwdSpd,
            snapAccel);

        _lastStationProximityTickDiagnostic = new StationProximityTickDiagnostic(
            snapTickSequence,
            _nearestStationSimTime,
            _nearestStationShipPosition,
            _nearestStation?.Name,
            _nearestStation?.PersistenceId,
            _nearestStation,
            _nearestStationEclipticPosition,
            _nearestStationGalaxyPosition,
            _nearestStationCentreDistance,
            _nearestStationPhysicalRadius,
            _nearestStationDistance,
            postPhysicsLkm.Zone,
            postPhysicsLkm.MaxGear,
            GameClock.SimTime,
            ship.Position,
            shipMovementDuringTick,
            snapMode);
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

        DVec3  refVel = GetRefVelocity();
        string refId  = GetRefSourceId();

        // Carry the ship along with however the reference is currently accelerating —
        // deliberately not simulating the ship's own true orbital mechanics (matching a
        // station's actual curved path is unintuitive to fly against); instead the current
        // reference is treated as perpetually stationary from the player's perspective,
        // same principle already used for planets in atmosphere. Only applied when the
        // reference SOURCE hasn't changed — a hard cutoff elsewhere (e.g. crossing the 25km
        // station-proximity boundary in UpdateReferenceFrame) can make refVel jump between
        // two unrelated objects' velocities on consecutive ticks; integrating that jump as
        // if it were real acceleration is what produced the reported "dropped on the moon"
        // symptom. When the source changes, resynchronize the baseline instead — apply no
        // delta this tick, just start tracking the new source from here.
        if (refId == _prevRefSourceId)
            ship.Velocity += refVel - _prevRefVel;

        _prevRefVel      = refVel;
        _prevRefSourceId = refId;

        // Afterburner — constant forward accel at AfterburnerAccelMultiplier × the current
        // full-throttle accel above, applied automatically for the whole burn. Not
        // player-steerable: WASD and X-Stop are entirely skipped while active (their toggles
        // are also gated in TickPhysics so they can't be engaged/cancelled mid-burn either).
        if (_afterburnerActive)
        {
            ship.Velocity += ship.Forward * (accel * FlightConstants.AfterburnerAccelMultiplier * dt);
            ship.Position += ship.Velocity * dt;
            return;
        }

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

        // X-Stop: maximum braking toward reference velocity, then hold indefinitely.
        // refVel is live (UpdateReferenceFrame recomputes it every tick from the actual
        // station orbit), so re-assigning ship.Velocity = refVel every tick here keeps the
        // ship locked to a moving target rather than freezing at one instant's value and
        // drifting away as the true reference velocity keeps evolving after that.
        if (_xStopActive)
        {
            double relSpeed = relVel.Length;
            if (relSpeed < FlightConstants.XStopSnapThreshold)
            {
                if (!_xStopCompleteAnnounced)
                {
                    _xStopCompleteAnnounced = true;
                    DataBus.System.Publish(Topics.System.All, new SystemMessage("X-Stop complete"));
                }
                MatchShipVelocityToReference(ship, refVel);
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
        if (_nearestStationDistance < FlightConstants.SlipstreamStationDropoutRange)
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
        var classification = ClassifyLkm(_nearestStationDistance);
        int newMax = classification.MaxGear;
        int activeZone = classification.Zone;

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

    internal static LkmClassification ClassifyLkm(double stationSurfaceDistance)
    {
        var zones = FlightConstants.StationLkmZones;
        for (int z = zones.Length - 1; z >= 0; z--)
        {
            if (stationSurfaceDistance < zones[z].radius)
                return new LkmClassification(z + 1, zones[z].maxGearIndex);
        }

        return new LkmClassification(0, int.MaxValue);
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
        if (capVelocity || _nearestStationDistance < 10_000.0)
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
        => _referenceVelocity;

    private string GetRefSourceId()
        => _referenceSourceId;

    private void ApplyPendingSystemContext()
    {
        var context = _pendingSystemContext;
        if (context == null) return;
        _systemContext = context;
        _pendingSystemContext = null;
    }

    private void ApplyPendingStationRelocation(Ship ship, SystemContext context, double simTime)
    {
        var request = _stationRelocationRequest;
        if (request == null) return;
        _stationRelocationRequest = null;

        if (string.IsNullOrWhiteSpace(request.StationPersistenceId))
        {
            RejectStationRelocation("invalid station identity");
            return;
        }

        if (!double.IsFinite(request.SurfaceStandOffMeters) || request.SurfaceStandOffMeters < 0.0)
        {
            RejectStationRelocation("invalid stand-off distance");
            return;
        }

        Station? station = ResolveStationByPersistenceId(context.System, request.StationPersistenceId);
        if (station == null)
        {
            RejectStationRelocation($"station '{request.StationPersistenceId}' not found in installed system");
            return;
        }

        DVec3 stationEclipticPos = context.System.GetStationPosition(station, simTime);
        DVec3 stationGalaxyPos = EclipticToGalaxy(stationEclipticPos);
        if (!IsFinite(stationGalaxyPos))
        {
            RejectStationRelocation($"station '{request.StationPersistenceId}' has non-finite live position");
            return;
        }

        double stationRadius = StationPhysicalRadius(station);
        if (!double.IsFinite(stationRadius) || stationRadius <= 0.0)
        {
            RejectStationRelocation($"station '{request.StationPersistenceId}' has invalid canonical radius");
            return;
        }

        DVec3 offsetDirection = DirectionOrFallback(ship.Position - stationGalaxyPos);
        double centreDistance = stationRadius + request.SurfaceStandOffMeters;
        DVec3 relocatedPosition = stationGalaxyPos + offsetDirection * centreDistance;
        if (!IsFinite(relocatedPosition))
        {
            RejectStationRelocation($"station '{request.StationPersistenceId}' relocation position is non-finite");
            return;
        }

        Quaternion orientation = CreateShipFacingOrientation(ship.Orientation, stationGalaxyPos - relocatedPosition);
        if (!IsFinite(orientation))
        {
            RejectStationRelocation($"station '{request.StationPersistenceId}' relocation orientation is non-finite");
            return;
        }

        ship.Position = relocatedPosition;
        ship.SetOrientation(orientation);

        UpdateReferenceFrame(ship);
        MatchShipVelocityToReference(ship, GetRefVelocity());
        _prevRefVel = _referenceVelocity;
        _prevRefSourceId = _referenceSourceId;
        _stationRelocationAppliedThisTick = true;

        if (_xStopActive)
            _xStopCompleteAnnounced = true;
    }

    private static Station? ResolveStationByPersistenceId(StarSystem system, string stationPersistenceId)
    {
        foreach (var station in system.Stations)
            if (string.Equals(station.PersistenceId, stationPersistenceId, StringComparison.Ordinal))
                return station;
        return null;
    }

    private static void RejectStationRelocation(string reason)
        => DataBus.System.Publish(Topics.System.All,
            new SystemMessage($"Station relocation rejected: {reason}", SystemMessagePriority.ImportantWarning));

    internal static void MatchShipVelocityToReference(Ship ship, DVec3 referenceVelocity)
        => ship.Velocity = referenceVelocity;

    internal static Quaternion CreateShipFacingOrientation(Quaternion currentOrientation, DVec3 desiredWorldForward)
    {
        DVec3 forward = DirectionOrFallback(desiredWorldForward);
        Quaternion current = IsFinite(currentOrientation)
            ? Quaternion.Normalize(currentOrientation)
            : Quaternion.Identity;

        var currentRot = Matrix.CreateFromQuaternion(current);
        var currentForwardV = Vector3.Transform(-Vector3.UnitZ, currentRot);
        DVec3 currentForward = IsFinite(currentForwardV)
            ? new DVec3(currentForwardV.X, currentForwardV.Y, currentForwardV.Z)
            : new DVec3(0, 0, -1);
        if (!TryNormalize(currentForward, out currentForward))
            currentForward = new DVec3(0, 0, -1);

        double dot = System.Math.Clamp(DVec3.Dot(currentForward, forward), -1.0, 1.0);
        if (dot > 0.999999)
            return current;

        var currentUpV = Vector3.Transform(Vector3.UnitY, currentRot);
        DVec3 currentUp = IsFinite(currentUpV)
            ? new DVec3(currentUpV.X, currentUpV.Y, currentUpV.Z)
            : DVec3.UnitY;
        if (!TryNormalize(currentUp, out currentUp))
            currentUp = DVec3.UnitY;

        DVec3 axis = DVec3.Cross(currentForward, forward);
        if (!TryNormalize(axis, out axis))
        {
            axis = dot < 0.0 ? currentUp : DVec3.UnitY;
            axis -= currentForward * DVec3.Dot(axis, currentForward);
            if (!TryNormalize(axis, out axis))
                axis = System.Math.Abs(DVec3.Dot(currentForward, DVec3.UnitY)) < 0.9
                    ? DVec3.UnitY
                    : DVec3.UnitX;
        }

        float angle = dot < -0.999999
            ? MathF.PI
            : (float)System.Math.Acos(dot);
        var delta = Quaternion.CreateFromAxisAngle(
            new Vector3((float)axis.X, (float)axis.Y, (float)axis.Z),
            angle);
        return Quaternion.Normalize(delta * current);
    }

    private void UpdateReferenceFrame(Ship ship)
    {
        const double StationDist = 25_000.0;

        var context = _systemContext;
        if (context == null)
        {
            _referenceVelocity = DVec3.Zero;
            _referenceName     = "";
            _referenceSourceId = "";
            return;
        }

        double simTime = GameClock.SimTime;
        DVec3 shipPos = ship.Position;

        foreach (var station in context.System.Stations)
        {
            DVec3 stPos = EclipticToGalaxy(context.System.GetStationPosition(station, simTime));
            if ((stPos - shipPos).Length < StationDist)
            {
                _referenceVelocity = EclipticToGalaxy(context.System.GetStationVelocity(station, simTime));
                _referenceName     = station.Name;
                _referenceSourceId = "station:" + station.Name;
                return;
            }
        }

        DVec3 shipEcliptic = GalaxyToEcliptic(shipPos);
        DVec3 gravEcl = Inferior.Gameplay.SensorData.GravityCalculations
            .GravityAt(shipEcliptic, SensorEnvironment.World.MassiveBodies);
        if (gravEcl.Length < 0.01)
        {
            _referenceVelocity = DVec3.Zero;
            _referenceName     = context.Star.Name;
            _referenceSourceId = "star:" + context.Star.Name;
            return;
        }

        DVec3 gravGal = EclipticToGalaxy(gravEcl);

        OrbitalBody? t0b = null, t1b = null, t2b = null;
        DVec3 t0p = DVec3.Zero, t1p = DVec3.Zero, t2p = DVec3.Zero;
        double t0w = 0, t1w = 0, t2w = 0;

        void Keep(OrbitalBody? body, DVec3 pos, double w)
        {
            if (w > t0w) { t2b = t1b; t2p = t1p; t2w = t1w; t1b = t0b; t1p = t0p; t1w = t0w; t0b = body; t0p = pos; t0w = w; }
            else if (w > t1w) { t2b = t1b; t2p = t1p; t2w = t1w; t1b = body; t1p = pos; t1w = w; }
            else if (w > t2w) { t2b = body; t2p = pos; t2w = w; }
        }

        double starDist = shipPos.Length;
        if (starDist > 100.0) Keep(null, DVec3.Zero, context.Star.MassKg / (starDist * starDist));
        foreach (var (body, eclipticPos) in SensorEnvironment.World.OrbitalBodies)
        {
            DVec3 pos = EclipticToGalaxy(eclipticPos);
            double d = (pos - shipPos).Length;
            if (d > 100.0) Keep(body, pos, body.MassKg / (d * d));
        }

        OrbitalBody? bestBody = null;
        double bestScore = 0.0;
        double winCos = 1.0;

        void Score(OrbitalBody? body, DVec3 pos, double w)
        {
            if (w == 0.0) return;
            DVec3 to = pos - shipPos;
            double d = to.Length;
            if (d < 100.0) return;
            var dir = new DVec3(to.X / d, to.Y / d, to.Z / d);
            double cos = dir.X * gravGal.X + dir.Y * gravGal.Y + dir.Z * gravGal.Z;
            if (cos <= 0.0) return;
            double score = w * cos;
            if (score > bestScore) { bestScore = score; winCos = cos; bestBody = body; }
        }

        Score(t0b, t0p, t0w);
        Score(t1b, t1p, t1w);
        Score(t2b, t2p, t2w);

        DVec3 domVelocity;
        string domName;
        string domSourceId;
        if (bestBody == null)
        {
            domVelocity = DVec3.Zero;
            domName = context.Star.Name;
            domSourceId = "star:" + context.Star.Name;
        }
        else
        {
            domVelocity = GetBodyGalaxyVelocity(bestBody, simTime);
            domName = bestBody.Name;
            domSourceId = "body:" + bestBody.Name;
        }

        double angle = System.Math.Acos(System.Math.Clamp(winCos, -1.0, 1.0));
        double lockRad = System.Math.PI / 4.0;
        double zeroRad = System.Math.PI / 2.0;
        double blend = angle <= lockRad ? 1.0
            : angle < zeroRad ? 1.0 - (angle - lockRad) / (zeroRad - lockRad)
            : 0.0;

        _referenceVelocity = _currentFlightMode is FlightMode.AtmosphericNewtonian or FlightMode.AtmosphericSlipstream
            ? DVec3.Zero
            : domVelocity * blend;
        _referenceName = domName;
        _referenceSourceId = domSourceId;
    }

    private DVec3 GetBodyGalaxyVelocity(OrbitalBody body, double gameTime)
    {
        var context = _systemContext;
        if (context == null) return DVec3.Zero;

        foreach (var planet in context.System.Planets)
        {
            if (ReferenceEquals(planet, body))
                return EclipticToGalaxy(PlanetVelocityEcl(planet, gameTime));
            foreach (var moon in planet.Children)
            {
                if (ReferenceEquals(moon, body))
                {
                    var pv = PlanetVelocityEcl(planet, gameTime);
                    var mv = SimpleOrbitalVelocityEcl(moon, gameTime);
                    return EclipticToGalaxy(pv + mv);
                }
            }
        }

        return EclipticToGalaxy(SimpleOrbitalVelocityEcl(body, gameTime));
    }

    private static DVec3 PlanetVelocityEcl(OrbitalBody planet, double gameTime)
        => planet.SemiMajorAxis > 0.0 && planet.ParentMassKg > 0.0
            ? planet.ComputeVelocity(gameTime, Units.G * planet.ParentMassKg, DVec3.Zero)
            : SimpleOrbitalVelocityEcl(planet, gameTime);

    /// <summary>
    /// Returns [0, 1] proximity damping for Slipstream speed.
    /// 1.0 far from any station or body; approaches 0 within ~20 km.
    /// Cubic dropoff over <see cref="FlightConstants.SlipstreamProximityDropoffM"/>.
    /// </summary>
    private double ComputeProximityScale()
    {
        double dropoff = FlightConstants.SlipstreamProximityDropoffM;
        double minDist = _nearestStationDistance;
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
        => CoordinateTransforms.EclipticToGalaxy(ecl, _eclipticAz, _eclipticTilt);

    private DVec3 GalaxyToEcliptic(DVec3 gal)
        => CoordinateTransforms.GalaxyToEcliptic(gal, _eclipticAz, _eclipticTilt);

    // ── Power ─────────────────────────────────────────────────────────────────

    protected override void TickPower(double dt)
    {
        _ship?.TickComponents(dt);
    }

    // ── Environment ───────────────────────────────────────────────────────────

    protected override void UpdateEnvironment()
    {
        ApplyPendingSystemContext();
        var context = _systemContext;
        if (context == null)
        {
            RejectPendingStationRelocation("no system context installed");
            return;
        }

        var ship = _ship;
        if (ship == null)
        {
            RejectPendingStationRelocation("no ship installed");
            return;
        }

        double simTime = GameClock.SimTime;
        _eclipticAz = context.System.EclipticTiltAzimuthRadians;
        _eclipticTilt = context.System.EclipticTiltRadians;

        ApplyPendingStationRelocation(ship, context, simTime);

        long tickSequence = ++_stationProximityTickSequence;
        _currentStationProximityTickSequence = tickSequence;

        var world = SensorEnvironment.World;
        world.MassiveBodies.Clear();
        world.OrbitalBodies.Clear();

        world.MassiveBodies.Add(new CelestialBody
        {
            Position       = DVec3.Zero,
            Mass           = context.Star.MassKg,
            Radius         = context.Star.RadiusMeters,
            Class          = context.Star.SpectralClass,
            RotationPeriod = 2.192e6,
        });

        foreach (var planet in context.System.Planets)
            CollectBody(world, planet, DVec3.Zero, simTime);

        DVec3 pos = ship.Position;
        DVec3 vel = ship.Velocity;
        DVec3 shipEcliptic = GalaxyToEcliptic(pos);

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

        var stationProximity = ComputeNearestStationProximity(pos, simTime);
        _nearestStationDistance = stationProximity.SurfaceDistance;
        _nearestStation = stationProximity.Station;
        _nearestStationEclipticPosition = stationProximity.EclipticPosition;
        _nearestStationGalaxyPosition = stationProximity.GalaxyPosition;
        _nearestStationShipPosition = pos;
        _nearestStationSimTime = simTime;
        _nearestStationCentreDistance = stationProximity.CentreDistance;
        _nearestStationPhysicalRadius = stationProximity.PhysicalRadius;

        SensorEnvironment.UpdateFromSimThread(world, shipEcliptic, vel);
    }

    private StationProximitySample ComputeNearestStationProximity(DVec3 shipPosition, double simTime)
    {
        var context = _systemContext;
        if (context == null)
            return new StationProximitySample(null, DVec3.Zero, DVec3.Zero, double.MaxValue, 0.0, double.MaxValue);

        Station? nearestStation = null;
        DVec3 nearestEclipticPosition = DVec3.Zero;
        DVec3 nearestGalaxyPosition = DVec3.Zero;
        double nearestCentreDistance = double.MaxValue;
        double nearestPhysicalRadius = 0.0;
        double nearestSurfaceDistance = double.MaxValue;

        foreach (var station in context.System.Stations)
        {
            DVec3 stationEclipticPos = context.System.GetStationPosition(station, simTime);
            DVec3 stationPos = EclipticToGalaxy(stationEclipticPos);
            double radius = StationPhysicalRadius(station);
            double centreDist = (stationPos - shipPosition).Length;
            double surfDist = System.Math.Max(centreDist - radius, 0.0);
            if (surfDist < nearestSurfaceDistance)
            {
                nearestSurfaceDistance = surfDist;
                nearestStation = station;
                nearestEclipticPosition = stationEclipticPos;
                nearestGalaxyPosition = stationPos;
                nearestCentreDistance = centreDist;
                nearestPhysicalRadius = radius;
            }
        }

        return new StationProximitySample(
            nearestStation,
            nearestEclipticPosition,
            nearestGalaxyPosition,
            nearestCentreDistance,
            nearestPhysicalRadius,
            nearestSurfaceDistance);
    }

    private void RejectPendingStationRelocation(string reason)
    {
        if (_stationRelocationRequest == null)
            return;

        _stationRelocationRequest = null;
        RejectStationRelocation(reason);
    }

    internal static double StationPhysicalRadius(Station station) => station.Size switch
    {
        StationSize.Small  =>  250.0,
        StationSize.Medium =>  800.0,
        StationSize.Large  => 2500.0,
        _                  =>  250.0,
    };

    private static DVec3 DirectionOrFallback(DVec3 direction)
        => TryNormalize(direction, out var normalized) ? normalized : DVec3.UnitY;

    private static bool TryNormalize(DVec3 value, out DVec3 normalized)
    {
        normalized = DVec3.Zero;
        if (!IsFinite(value))
            return false;

        double length = value.Length;
        if (!double.IsFinite(length) || length < 1e-9)
            return false;

        normalized = value / length;
        return IsFinite(normalized);
    }

    private static bool IsFinite(DVec3 value)
        => double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);

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

        WriteStationProximityDiagnosticIfRequested();

        if (t >= _nextMessageAt)
        {
            DataBus.System.Publish(Topics.System.All, new($"T+{t:F0}s - all systems nominal"));
            _nextMessageAt += 8.0;
        }
    }

    private void WriteStationProximityDiagnosticIfRequested()
    {
        var main = _stationProximityDiagnosticRequest;
        if (main == null) return;
        _stationProximityDiagnosticRequest = null;

        var context = _systemContext;
        var ship = _ship;
        Station? simStation = _nearestStation;

        bool sameSystemRef = context != null && ReferenceEquals(main.MainSystem, context.System);
        bool sameStarRef = context != null && ReferenceEquals(main.MainStar, context.Star);
        bool sameStationRef = main.TargetStation != null && simStation != null && ReferenceEquals(main.TargetStation, simStation);
        bool sameStationName = main.TargetStationName != null && simStation != null
            && string.Equals(main.TargetStationName, simStation.Name, StringComparison.Ordinal);
        bool sameStationId = main.TargetStationId != null && simStation?.PersistenceId != null
            && string.Equals(main.TargetStationId, simStation.PersistenceId, StringComparison.Ordinal);

        DVec3 stationDelta = simStation != null
            ? main.TargetStationGalaxyPosition - _nearestStationGalaxyPosition
            : DVec3.Zero;
        DVec3? shipDelta = ship != null && main.ShipSnapshotPosition.HasValue
            ? main.ShipSnapshotPosition.Value - _nearestStationShipPosition
            : null;
        double timeDelta = main.MainTime - _nearestStationSimTime;

        string classification;
        if (context == null)
            classification = "NO_SIM_SYSTEM_CONTEXT";
        else if (!sameSystemRef || !sameStarRef)
            classification = "DIFFERENT_INSTALLED_SYSTEM_OR_STAR";
        else if (!sameStationRef && !sameStationName && !sameStationId)
            classification = "DIFFERENT_STATION_IDENTITY";
        else if (stationDelta.Length > 1.0)
            classification = "SAME_STATION_NAME_OR_ID_DIFFERENT_WORLD_POSITION";
        else if (shipDelta.HasValue && shipDelta.Value.Length > 1.0)
            classification = "SAME_STATION_POSITION_DIFFERENT_SHIP_POSITION";
        else if (_currentLkmZone == 1 && _nearestStationDistance >= FlightConstants.StationLkmZones[1].radius)
            classification = "EXPECTED_LKM1_FROM_SIM_DISTANCE";
        else
            classification = "SAME_CONTEXT_UNEXPECTED_DISTANCE_OR_ZONE";

        string V(DVec3 v) => $"({v.X:R}, {v.Y:R}, {v.Z:R}) |len|={v.Length:R}";
        string MaybeV(DVec3? v) => v.HasValue ? V(v.Value) : "<null>";

        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "station_proximity_diagnostic.log");
        var text =
            "=== Station proximity diagnostic ===\n" +
            $"requestedUtc={main.RequestedAtUtc:O} capturedUtc={DateTime.UtcNow:O}\n" +
            $"classification={classification}\n\n" +
            "[Main selected target]\n" +
            $"star={main.MainStar.Name}#{main.MainStar.GalaxyIndex}\n" +
            $"systemStationCount={main.MainSystem.Stations.Count}\n" +
            $"targetName={main.TargetStationName ?? "<none>"}\n" +
            $"targetId={main.TargetStationId ?? "<null>"}\n" +
            $"mainTime={main.MainTime:R}\n" +
            $"targetEcliptic={V(main.TargetStationEclipticPosition)}\n" +
            $"targetGalaxy={V(main.TargetStationGalaxyPosition)}\n" +
            $"cameraUniverse={V(main.CameraUniversePosition)}\n" +
            $"shipSnapshotPosition={MaybeV(main.ShipSnapshotPosition)}\n" +
            $"cameraToStationDistance={main.CameraToStationDistance:R}\n" +
            $"shipSnapshotToStationDistance={(main.ShipSnapshotToStationDistance.HasValue ? main.ShipSnapshotToStationDistance.Value.ToString("R") : "<null>")}\n\n" +
            "[Simulation nearest station]\n" +
            $"star={(context?.Star.Name ?? "<none>")}#{(context?.Star.GalaxyIndex.ToString() ?? "<none>")}\n" +
            $"stationCount={(context?.System.Stations.Count.ToString() ?? "<none>")}\n" +
            $"nearestName={simStation?.Name ?? "<none>"}\n" +
            $"nearestId={simStation?.PersistenceId ?? "<null>"}\n" +
            $"simTime={_nearestStationSimTime:R}\n" +
            $"stationEcliptic={V(_nearestStationEclipticPosition)}\n" +
            $"stationGalaxy={V(_nearestStationGalaxyPosition)}\n" +
            $"shipPositionUsed={V(_nearestStationShipPosition)}\n" +
            $"currentShipPosition={(ship != null ? V(ship.Position) : "<null>")}\n" +
            $"rawCentreDistance={_nearestStationCentreDistance:R}\n" +
            $"stationPhysicalRadius={_nearestStationPhysicalRadius:R}\n" +
            $"nearestStationDistance={_nearestStationDistance:R}\n" +
            $"lkmZone={_currentLkmZone}\n" +
            $"maxGearIndex={(_lkmMaxGear == int.MaxValue ? "<none>" : _lkmMaxGear.ToString())}\n\n" +
            "[Direct comparison]\n" +
            $"sameStarReference={sameStarRef}\n" +
            $"sameSystemReference={sameSystemRef}\n" +
            $"sameStationReference={sameStationRef}\n" +
            $"sameStationName={sameStationName}\n" +
            $"sameStationId={sameStationId}\n" +
            $"mainTargetGalaxyMinusSimNearestGalaxy={V(stationDelta)}\n" +
            $"mainShipSnapshotMinusSimShipUsed={MaybeV(shipDelta)}\n" +
            $"mainTimeMinusSimTime={timeDelta:R}\n" +
            "====================================\n\n";

        System.IO.File.AppendAllText(path, text);
        DataBus.System.Publish(Topics.System.All,
            new SystemMessage($"Station proximity diagnostic written: {path}", SystemMessagePriority.Info));
    }
}
