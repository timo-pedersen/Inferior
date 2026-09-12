using Xunit;

// DataBusTests and SolarHeatSensorTests both drive the shared static DataBus (Inferior.Core.DataBus.DataBus),
// whose Drain() is documented as belonging to one consumer thread. xUnit parallelizes different test
// classes by default, so without this, concurrent Drain() calls from separate test classes race on
// DataBus's plain (non-thread-safe) Dictionary fields. Same fix already applied in Inferior.Game.Test
// (see XStopAfterburnerTests.cs) for the same reason.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
