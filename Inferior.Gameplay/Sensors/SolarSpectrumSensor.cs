using Inferior.Core.DataBus;
using Inferior.Core.Simulation;
using SensorEnv = Inferior.Gameplay.SensorData.Environment;

namespace Inferior.Gameplay.Sensors;

/// <summary>
/// Active solar-spectrum sensor. Begins a scan when commanded; after
/// <see cref="ScanDurationSeconds"/> the spectrum is computed and published.
///
/// Command topic prefix: the name passed to the constructor (e.g. "SolarSpectrumSensor").
/// A second command while scanning is ignored.
///
/// Published topic: "{name}.Data" on DataBus.SpectrumTelemetry — double[] of
/// <see cref="Environment.SpectrumBins"/> normalised values (0-1 per bin).
///
/// Allocation strategy: one double[] is allocated per scan (result copy).
/// The working buffer is pre-allocated at construction and reused — no per-tick allocation.
/// </summary>
public sealed class SolarSpectrumSensor
{
    public const double ScanDurationSeconds = 2.0;

    private readonly string   _name;
    private          double   _scanCountdown = -1.0;  // < 0 = idle
    private readonly double[] _workBuffer    = new double[SensorEnv.SpectrumBins];

    public SolarSpectrumSensor(string name)
    {
        _name = name;
        CommandBus.Subscribe(name, _ => StartScan());

        string dataTopic = $"{name}.{Topics.SolarSpectrum.Data}";
        DataBus.PublishTelemetryInfo(new TelemetryInfo
        {
            Topic = dataTopic,
            DeviceId = name,
            ValueKind = TelemetryValueKind.Spectrum,
            Quantity = PhysicalQuantity.NormalizedRatio,
            OperatingRange = new RangeValue(0.0, 1.0),
            SuggestedDisplayRange = new RangeValue(0.0, 1.0),
            Publication = new PublicationInfo(PublicationMode.OnCommand),
            TopicPolicy = TopicPolicy.LatestState,
        });
        DataBus.DeviceInfo.Publish(name, new DeviceInfo
        {
            DeviceId = name,
            PublishedTopics = [dataTopic],
            CommandTopics = [$"{name}.Scan"],
            Power = new PowerProfile(
                IdleWatts: 0.0,
                ActiveWatts: 0.0,
                ActivationDurationSeconds: ScanDurationSeconds),
        });
        DataBus.DeviceState.Publish(name, new DeviceState(
            name,
            DeviceOperationalStatus.Running,
            Damage: 0.0,
            Efficiency: 1.0,
            SimulationTime: GameClock.SimTime));
    }

    private void StartScan()
    {
        if (_scanCountdown >= 0) return;  // already scanning
        _scanCountdown = ScanDurationSeconds;
    }

    /// <summary>Call once per sim tick from Simulation.Publish().</summary>
    public void Tick(double dt)
    {
        if (_scanCountdown < 0) return;
        _scanCountdown -= dt;
        if (_scanCountdown > 0) return;

        _scanCountdown = -1.0;
        SensorEnv.GetSolarVisibleSpectrum(_workBuffer.AsSpan());
        DataBus.SpectrumTelemetry.Publish($"{_name}.{Topics.SolarSpectrum.Data}", (double[])_workBuffer.Clone());
        DataBus.SystemMessages.Publish(Topics.System.All, new("Solar spectrum scan complete"));
    }
}
