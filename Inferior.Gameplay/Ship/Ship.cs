using Microsoft.Xna.Framework;
using Inferior.Core.Math;
using Inferior.Core.DataBus;
using Inferior.Gameplay.Cockpit;
using Inferior.Gameplay.Components;
using Inferior.Gameplay.Components.Power;
using Inferior.Gameplay.Engines;
using Inferior.Gameplay.Hull;

namespace Inferior.Gameplay.Ship;

/// <summary>
/// A ship instance — unique physical object in the universe.
/// Owns position, velocity, and orientation. The camera follows the cockpit,
/// not the centre of mass. All persistent config (power presets, UI layout, etc.)
/// will live here as the simulation layers are built out.
/// </summary>
public sealed class Ship
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public string        Id          { get; init; } = "";
    public string        HullTypeId  { get; init; } = "";
    public string?       Name        { get; init; }
    public DateTime      CreatedDate { get; init; }
    public ShipSizeClass SizeClass   { get; init; } = ShipSizeClass.Medium;

    // ── Flyability ────────────────────────────────────────────────────────────
    public bool                  CanFly           => true;  // stub — always flyable until systems are wired
    public IReadOnlyList<string> FlyabilityIssues => [];

    // ── Physics state ──────────────────────────────────────────────────────────
    public DVec3      Position    { get; set; }
    public DVec3      Velocity    { get; set; }
    public Quaternion Orientation { get; private set; } = Quaternion.Identity;
    public DVec3 AngularVelocityLocalRadPerSec { get; private set; }

    public void SetAngularVelocityLocal(DVec3 value)
    {
        if (!IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        AngularVelocityLocalRadPerSec = value;
    }

    public void ResetAngularVelocity()
        => AngularVelocityLocalRadPerSec = DVec3.Zero;

    public void ApplyAngularImpulse(DVec3 deltaRadPerSec)
        => SetAngularVelocityLocal(AngularVelocityLocalRadPerSec + deltaRadPerSec);

    // ── Components ────────────────────────────────────────────────────────────
    private readonly List<ShipComponent> _components = new();
    public IReadOnlyList<ShipComponent> Components => _components;

    public void Install(ShipComponent component)
    {
        _components.Add(component);
        component.ActivateBus();
        ComponentMass += component is PowerReactor r ? r.MaxPower * 0.00001 : 0; // stub
        component.PowerOn = true;
        component.NotifyPowerAvailable();
    }

    public void TickComponents(double dt)
    {
        foreach (var c in _components)
            c.Tick(dt);
    }

    private readonly List<EngineMount> _engineMounts = [];
    public IReadOnlyList<EngineMount> EngineMounts => _engineMounts;

    public void AddEngineMount(EngineMount mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        if (_engineMounts.Any(existing =>
            string.Equals(existing.MountId, mount.MountId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Duplicate engine mount id '{mount.MountId}'.");
        }
        if (_engineMounts.Any(existing =>
            string.Equals(existing.ComponentSlotId, mount.ComponentSlotId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Duplicate engine mount component slot '{mount.ComponentSlotId}'.");
        }

        _engineMounts.Add(mount);
    }

    /// <summary>True when at least one GyroComponent is running. Reduces slipstream exit tumble.</summary>
    public bool HasGyro => _components.Any(c => c is GyroComponent { Status: ComponentStatus.Running });

    // ── Mass ──────────────────────────────────────────────────────────────────
    public double HullMass      { get; init; } = 50_000.0;  // kg, hull only
    public double ComponentMass { get; set;  } = 0.0;       // kg, updated as components change
    public double InstalledEngineMass => _engineMounts.Sum(
        mount => mount.InstalledEngine?.Variant.Engine.DryMassKg ?? 0.0);
    public double Mass          => HullMass + ComponentMass + InstalledEngineMass;
    public DesignedSingleEngineEfficiency? SingleEngineEfficiency { get; init; }

    // ── Cockpit ───────────────────────────────────────────────────────────────
    public InstalledCockpit? Cockpit { get; init; }

    /// <summary>Transitional camera offset for hulls without an installed cockpit.</summary>
    public DVec3 CockpitOffset { get; init; } = DVec3.Zero;

    /// <summary>Transitional camera pose for hulls without an installed cockpit.</summary>
    public CockpitPoseDefinition CockpitPose { get; init; } = new(DVec3.Zero, Quaternion.Identity);

    /// <summary>World-space physical cockpit camera position.</summary>
    public DVec3 CockpitWorldPosition
    {
        get
        {
            (DVec3 localPosition, _) = ResolveCockpitShipLocalPose();
            var rotated = Vector3.Transform(localPosition.ToVector3(), Orientation);
            return Position + new DVec3(rotated.X, rotated.Y, rotated.Z);
        }
    }

    /// <summary>World-space physical cockpit camera orientation.</summary>
    public Quaternion CockpitWorldOrientation
    {
        get
        {
            (_, Quaternion localOrientation) = ResolveCockpitShipLocalPose();
            return Quaternion.Normalize(Orientation * localOrientation);
        }
    }

    public DVec3 CockpitRootWorldPosition
    {
        get
        {
            (DVec3 localPosition, _) = ResolveCockpitShipLocalRootPose();
            Vector3 rotated = Vector3.Transform(localPosition.ToVector3(), Orientation);
            return Position + new DVec3(rotated.X, rotated.Y, rotated.Z);
        }
    }

    public Quaternion CockpitRootWorldOrientation
    {
        get
        {
            (_, Quaternion localOrientation) = ResolveCockpitShipLocalRootPose();
            return Quaternion.Normalize(Orientation * localOrientation);
        }
    }

    public DVec3 CockpitRootShipLocalPosition =>
        ResolveCockpitShipLocalRootPose().Position;

    public Quaternion CockpitRootShipLocalOrientation =>
        ResolveCockpitShipLocalRootPose().Orientation;

    public bool ApplyCockpitCommand(ComponentCommand command)
    {
        if (Cockpit is null)
            return false;

        CockpitModuleDefinition definition = CockpitDefinitionLibrary.Get(Cockpit.DefinitionId);
        return Cockpit.ApplyCommand(command, definition);
    }

    // ── Derived orientation axes ───────────────────────────────────────────────
    public DVec3 Forward { get; private set; } = new(0, 0, -1);
    public DVec3 Right   { get; private set; } = new(1, 0, 0);
    public DVec3 Up      { get; private set; } = new(0, 1, 0);

    // Assisted target-rate limits. Torque and box inertia determine time to reach them.
    public double TurnRatePitchUp => FlightConstants.MaximumAssistedPitchUpRateRadPerSec;
    public double TurnRatePitchDown => FlightConstants.MaximumAssistedPitchDownRateRadPerSec;
    public double TurnRateYaw => FlightConstants.MaximumAssistedYawRateRadPerSec;
    public double TurnRateRoll => FlightConstants.MaximumAssistedRollRateRadPerSec;

    // ── Drive ─────────────────────────────────────────────────────────────────
    // Velocity-target model: ship snaps to MoveSpeedMs in the thrust direction.
    // Used by debug camera proximity scaling; Newtonian flight does not read MoveSpeedMs.
    public double MoveSpeedMs { get; set; } = 5e9;  // m/s

    // Transitional slipstream node count. Newtonian harmony comes from installed engines.
    public int NodeCount { get; init; } = FlightConstants.DefaultNodeCount;

    // Slipstream harmonic speeds — NodeCount entries, log-scaled from min to max.
    public double[] SlipstreamHarmonics
    {
        get
        {
            int    n     = NodeCount;
            double min   = FlightConstants.SlipstreamMinSpeed;
            double max   = FlightConstants.SlipstreamMaxSpeed;
            double ratio = System.Math.Pow(max / min, 1.0 / (n - 1));
            return Enumerable.Range(0, n)
                .Select(i => min * System.Math.Pow(ratio, i))
                .ToArray();
        }
    }

    // Gear-shift clunk duration in seconds (shorter for more nodes).
    public double ClunkDurationMs =>
        FlightConstants.ClunkBaseDurationMs
        + FlightConstants.ClunkNodePenaltyMs * (24 - NodeCount);

    // ── Atmosphere / aerodynamics ──────────────────────────────────────────────
    // Aerodynamics are set from HullDefinition at ship construction.

    public double AerodynamicLift         { get; init; } = 0.0;
    public double AerodynamicBrakeFront   { get; init; } = 0.0;
    public double AerodynamicBrakeLateral { get; init; } = 0.0;

    // ── Orientation API ───────────────────────────────────────────────────────

    public void SetOrientation(Quaternion q)
    {
        Orientation = Quaternion.Normalize(q);
        RefreshAxes();
    }

    public void IntegrateAngularVelocity(double dt)
    {
        if (!double.IsFinite(dt) || dt < 0.0)
            throw new ArgumentOutOfRangeException(nameof(dt));

        double speed = AngularVelocityLocalRadPerSec.Length;
        double angle = speed * dt;
        if (angle < 1e-12)
            return;

        DVec3 axis = AngularVelocityLocalRadPerSec / speed;
        Quaternion delta = Quaternion.CreateFromAxisAngle(axis.ToVector3(), (float)angle);
        SetOrientation(Quaternion.Normalize(Orientation * delta));
    }

    /// <summary>
    /// Set velocity toward the given thrust direction at MoveSpeedMs.
    /// Components are each -1..1. Zero input zeroes velocity (flight-assist-always-on stub).
    /// </summary>
    public void ApplyVelocityTarget(double fwd, double lat, double vert, DVec3 baseVelocity = default)
    {
        var dir = Forward * fwd + Right * lat + Up * vert;
        double len = dir.Length;
        Velocity = baseVelocity + (len > 0.001 ? dir / len * MoveSpeedMs : DVec3.Zero);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    public void RefreshAxes()
    {
        var rot = Matrix.CreateFromQuaternion(Orientation);
        Forward = Vec3(Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, rot)));
        Right   = Vec3(Vector3.Normalize(Vector3.Transform( Vector3.UnitX, rot)));
        Up      = Vec3(Vector3.Normalize(Vector3.Transform( Vector3.UnitY, rot)));
    }

    private static DVec3 Vec3(Vector3 v) => new(v.X, v.Y, v.Z);

    private static bool IsFinite(DVec3 value)
        => double.IsFinite(value.X)
        && double.IsFinite(value.Y)
        && double.IsFinite(value.Z);

    private (DVec3 Position, Quaternion Orientation) ResolveCockpitShipLocalPose()
    {
        if (Cockpit is null)
            return (CockpitPose.Position, CockpitPose.Orientation);

        HullDefinition hull = HullDefinitionLibrary.Get(HullTypeId);
        CockpitMountDefinition mount = hull.CockpitMounts.SingleOrDefault(candidate =>
            string.Equals(candidate.MountId, Cockpit.MountId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Ship '{Id}' cockpit references unknown mount '{Cockpit.MountId}'.");
        CockpitModuleDefinition definition = CockpitDefinitionLibrary.Get(Cockpit.DefinitionId);
        return (
            Cockpit.ResolveShipLocalCameraPosition(mount, definition),
            Cockpit.ResolveShipLocalCameraOrientation(mount, definition));
    }

    private (DVec3 Position, Quaternion Orientation) ResolveCockpitShipLocalRootPose()
    {
        if (Cockpit is null)
            return (CockpitPose.Position, CockpitPose.Orientation);

        HullDefinition hull = HullDefinitionLibrary.Get(HullTypeId);
        CockpitMountDefinition mount = hull.CockpitMounts.SingleOrDefault(candidate =>
            string.Equals(candidate.MountId, Cockpit.MountId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Ship '{Id}' cockpit references unknown mount '{Cockpit.MountId}'.");
        CockpitModuleDefinition definition = CockpitDefinitionLibrary.Get(Cockpit.DefinitionId);
        return (
            Cockpit.ResolveShipLocalRootPosition(mount, definition),
            Cockpit.ResolveShipLocalRootOrientation(mount, definition));
    }
}
