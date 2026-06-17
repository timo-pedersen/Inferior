using Inferior.Core.DataBus;
using SensorEnv = Inferior.Gameplay.SensorData.Environment;

namespace Inferior.Gameplay.Sensors;

/// <summary>
/// Active solar-spectrum sensor. Begins a scan when commanded; after
/// <see cref="ScanDurationSeconds"/> the spectrum is computed and published.
///
/// Command topic prefix: the name passed to the constructor (e.g. "SolarSpectrumSensor").
/// A second command while scanning is ignored.
///
/// Published topic: "{name}.Data" on DataBus.Spectra — double[] of
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
        DataBus.Spectra.Publish($"{_name}.{Topics.SolarSpectrum.Data}", (double[])_workBuffer.Clone());
        DataBus.System.Publish(Topics.System.All, new("Solar spectrum scan complete"));
    }
}
