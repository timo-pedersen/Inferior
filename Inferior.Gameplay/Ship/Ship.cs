using Microsoft.Xna.Framework;
using Inferior.Core.Math;
using Inferior.Gameplay.Components;

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

    // ── Components ────────────────────────────────────────────────────────────
    private readonly List<ShipComponent> _components = new();
    public IReadOnlyList<ShipComponent> Components => _components;

    public void Install(ShipComponent component)
    {
        _components.Add(component);
        ComponentMass += component is PowerReactor r ? r.MaxOutputW * 0.00001 : 0; // stub
        component.OnStartup();
    }

    public void TickComponents(double dt)
    {
        foreach (var c in _components)
            c.Tick(dt);
    }

    // ── Mass ──────────────────────────────────────────────────────────────────
    public double HullMass      { get; init; } = 50_000.0;  // kg, hull only
    public double ComponentMass { get; set;  } = 0.0;       // kg, updated as components change
    public double Mass          => HullMass + ComponentMass;

    // ── Cockpit ───────────────────────────────────────────────────────────────
    /// <summary>Offset from centre of mass in ship-local space. Camera eye point.</summary>
    public DVec3 CockpitOffset { get; init; } = DVec3.Zero;

    /// <summary>World-space cockpit position. The camera follows this, not Position.</summary>
    public DVec3 CockpitWorldPosition
    {
        get
        {
            if (CockpitOffset == DVec3.Zero) return Position;
            var rotated = Vector3.Transform(CockpitOffset.ToVector3(), Orientation);
            return Position + new DVec3(rotated.X, rotated.Y, rotated.Z);
        }
    }

    // ── Derived orientation axes ───────────────────────────────────────────────
    public DVec3 Forward { get; private set; } = new(0, 0, -1);
    public DVec3 Right   { get; private set; } = new(1, 0, 0);
    public DVec3 Up      { get; private set; } = new(0, 1, 0);

    // ── Turn rates (calculated — stubbed until engine + gyro system exists) ────
    // Asymmetric by design: up-pitch is faster because the engine can vector thrust
    // upward; this also assists planetary landing. Replace getter bodies with
    // real calculations once engine/mass data is available.
    public double TurnRatePitchUp   => TurnRateBase * 1.4;  // rad/s
    public double TurnRatePitchDown => TurnRateBase;
    public double TurnRateYaw       => TurnRateBase;
    public double TurnRateRoll      => TurnRateBase * 1.5;

    private const double TurnRateBase = 1.0;  // rad/s — derive from engine + gyro + mass later

    // ── Drive ─────────────────────────────────────────────────────────────────
    // Velocity-target model: ship snaps to MoveSpeedMs in the thrust direction.
    // Replace with force-based Newtonian (F=ma) once the engine/power system exists.
    public double MoveSpeedMs { get; set; } = 5e9;  // m/s — tuned to match debug camera feel

    // ── Orientation API ───────────────────────────────────────────────────────

    public void SetOrientation(Quaternion q)
    {
        Orientation = Quaternion.Normalize(q);
        RefreshAxes();
    }

    /// <summary>
    /// Apply a rotation this tick. pitchDelta/yawDelta are raw angle deltas in radians
    /// (from mouse input). rollRate is -1..1 and is scaled by TurnRateRoll × dt.
    /// </summary>
    public void ApplyRotation(double pitchDelta, double yawDelta, double rollRate, double dt)
    {
        double rollDelta = rollRate * TurnRateRoll * dt;
        if (pitchDelta == 0.0 && yawDelta == 0.0 && rollDelta == 0.0) return;

        var pitchQ = Quaternion.CreateFromAxisAngle(RightF,   (float)pitchDelta);
        var yawQ   = Quaternion.CreateFromAxisAngle(UpF,      (float)yawDelta);
        var rollQ  = Quaternion.CreateFromAxisAngle(ForwardF, (float)rollDelta);
        SetOrientation(Quaternion.Normalize(pitchQ * yawQ * rollQ * Orientation));
    }

    /// <summary>
    /// Set velocity toward the given thrust direction at MoveSpeedMs.
    /// Components are each -1..1. Zero input zeroes velocity (flight-assist-always-on stub).
    /// </summary>
    public void ApplyVelocityTarget(double fwd, double lat, double vert)
    {
        var dir = Forward * fwd + Right * lat + Up * vert;
        double len = dir.Length;
        Velocity = len > 0.001 ? dir / len * MoveSpeedMs : DVec3.Zero;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    public void RefreshAxes()
    {
        var rot = Matrix.CreateFromQuaternion(Orientation);
        Forward = Vec3(Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, rot)));
        Right   = Vec3(Vector3.Normalize(Vector3.Transform( Vector3.UnitX, rot)));
        Up      = Vec3(Vector3.Normalize(Vector3.Transform( Vector3.UnitY, rot)));
    }

    private Vector3 ForwardF => new((float)Forward.X, (float)Forward.Y, (float)Forward.Z);
    private Vector3 RightF   => new((float)Right.X,   (float)Right.Y,   (float)Right.Z);
    private Vector3 UpF      => new((float)Up.X,      (float)Up.Y,      (float)Up.Z);

    private static DVec3 Vec3(Vector3 v) => new(v.X, v.Y, v.Z);
}
