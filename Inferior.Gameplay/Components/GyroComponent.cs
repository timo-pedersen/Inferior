using Inferior.Core.DataBus;

namespace Inferior.Gameplay.Components;

/// <summary>Crystal carbon gyroscope. Enhances ship turn rates using AlphaRed power from the engine.</summary>
public enum CarbonCrystalType { Graphite, CarbonFiber, Diamond }

/// <summary>Manufacturing quality grade. A is highest.</summary>
public enum CarbonCrystalQuality { C, B, A }

/// <summary>
/// Optional gyro component that boosts rotational authority beyond what the engine alone provides.
/// Requires Red Alpha power, which only certain engines can generate when paired.
///
/// AddedTorqueFactor is a multiplier on the base turn rate derived from crystal grade and quality.
/// The formula is intentionally placeholder — final values depend on engine physics tuning.
///
/// Sensors published:
///   {Name}.TorqueFactor — effective torque multiplier (0 if not Running)
///   {Name}.Damage       — component damage 0..1
/// </summary>
public sealed class GyroComponent : ShipComponent
{
    public CarbonCrystalType    CrystalType    { get; init; }
    public CarbonCrystalQuality CrystalQuality { get; init; }

    /// <summary>
    /// Torque multiplier applied on top of baseline engine gyro effect.
    /// Calculated from crystal type × quality at construction.
    /// Formula is a stub — replace when engine turn-rate model is finalized.
    /// </summary>
    public double AddedTorqueFactor { get; }

    public GyroComponent(string name,
                         CarbonCrystalType    crystalType,
                         CarbonCrystalQuality crystalQuality)
    {
        Name           = name;
        CrystalType    = crystalType;
        CrystalQuality = crystalQuality;
        AddedTorqueFactor = CalcTorqueFactor(crystalType, crystalQuality);
        StartupTimer   = 1.5;

        RegisterSensors();
    }

    protected override void OnTick(double dt) => TickSensors();

    protected override void OnInitializationComplete()
    {
        PublishTelemetryInfo();
        DataBus.SystemMessages.Publish(Topics.System.All,
            new($"{Name}: online — {CrystalType} {CrystalQuality} crystal, {AddedTorqueFactor:F2}× torque factor"));
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static double CalcTorqueFactor(CarbonCrystalType t, CarbonCrystalQuality q)
    {
        double typeBase = t switch
        {
            CarbonCrystalType.Diamond    => 1.5,
            CarbonCrystalType.CarbonFiber => 1.2,
            _                             => 1.0,  // Graphite
        };
        double qualityMult = q switch
        {
            CarbonCrystalQuality.A => 1.3,
            CarbonCrystalQuality.B => 1.1,
            _                      => 1.0,
        };
        return typeBase * qualityMult;
    }

    private void RegisterSensors()
    {
        _sensors.Add(new ComponentSensor(
            $"{Name}.{Topics.Gyro.TorqueFactor}",
            () => Status == ComponentStatus.Running ? AddedTorqueFactor : 0.0,
            safeRange:  new RangeValue(1.0, 2.0),
            totalRange: new RangeValue(0.0, 2.0),
            quantity: PhysicalQuantity.Dimensionless));

        _sensors.Add(new ComponentSensor(
            $"{Name}.{Topics.Gyro.Damage}",
            () => Damage,
            safeRange:  new RangeValue(0, 0.2),
            totalRange: new RangeValue(0, 1.0),
            quantity: PhysicalQuantity.NormalizedRatio));
    }
}
