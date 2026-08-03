# Inferior message buses

This directory contains Inferior's topic-based communication infrastructure. This guide
explains both why it works the way it does and how to add a publisher or subscriber without
creating a second, hard-wired data path.

The short version is:

```text
simulation state / SensorData
    -> sensor or component
    -> telemetry or message bus
    -> Inferior-specific UI binding
    -> value-driven UI control

UI action
    -> CommandBus
    -> simulation-owned sensor or component
    -> telemetry, device state, or system-message response
```

The bus is named for what it carries, not for the system that happens to consume it. A value
does not become an "instrument value" merely because a cockpit instrument displays it.

## 1. The available channels

`DataBus` is currently a static compatibility hub. Its channels are intended eventually to
be owned by a ship or simulation session, but code should use the existing hub until that
ownership refactor is made deliberately.

| Channel | Payload | Intended meaning | Default policy |
|---|---|---|---|
| `ScalarTelemetry` | `double` in a `TelemetrySample<double>` | Scalar measurements and operational values | latest per drain, retain latest |
| `VectorTelemetry` | `DVec3` in a `TelemetrySample<DVec3>` | Atomic vector measurements | latest per drain, retain latest |
| `SpectrumTelemetry` | `double[]` in a `TelemetrySample<double[]>` | Spectrum or array-valued measurements | latest per drain, retain latest |
| `TelemetryInfo` | `TelemetryInfo` | Description of a telemetry topic | latest per drain, retain latest |
| `DeviceInfo` | `DeviceInfo` | Description of a sensor or component | latest per drain, retain latest |
| `DeviceState` | `DeviceState` | Current observable operational state | latest per drain, retain latest |
| `SystemMessages` | `SystemMessage` | Human-readable console and HUD messages | deliver all, retain the latest 256 |
| `Radar` | `RadarContact` | Radar contact updates pending radar migration | deliver all, no retention |
| `RadarLost` | `string` | Radar contact-loss events pending radar migration | deliver all, no retention |
| `Target` | `RadarContact` | Current selected target | latest per drain, retain latest |

`CommandBus` is separate because it travels in the opposite direction. The UI or another
caller sends a request; the simulation thread drains and handles it. A command is not proof
that anything happened. Confirmation comes back through telemetry, device state, or a system
message after the simulation has accepted and applied the request.

The channel and topic together identify a contract. The same topic string on two different
channels is not the same contract.

## 2. Publish, drain, and dispatch

Publishing does not call subscribers immediately. `Publish` enqueues a topic/value pair and
returns without blocking the publisher. The owning consumer thread later calls `Drain`, which
applies the topic's dispatch and retention policy and invokes handlers.

For `DataBus` the intended direction is:

- simulation-side producers call `Publish`;
- the main thread calls `DataBus.Drain()` once per frame;
- UI subscribers run while the main thread drains.

For `CommandBus` the direction is reversed:

- the main/UI thread calls `CommandBus.Send`;
- the simulation thread calls `CommandBus.Drain()` once per simulation tick;
- component and sensor command handlers run on the simulation thread.

`Bus<T>.Publish` and telemetry publication are thread-safe. Topic configuration,
subscription, unsubscription, replay, and draining belong to the consumer thread. Do not
mutate live simulation state from a telemetry handler merely because the handler received a
simulation-produced value; telemetry handlers are presentation-side consumers.

Retention is updated during `Drain`, not during `Publish`. Consequently, a value waiting in
the queue is not replayable yet. If a subscriber is added after publication but before the
next drain, it receives the queued value once during normal dispatch. It does not receive an
early replay followed by a duplicate dispatch.

Each bus has a bounded pending queue, defaulting to 65,536 publications. If publishers outrun
the consumer far enough to exceed that bound, the oldest pending publications are discarded.
`DroppedMessageCount` reports how many have been discarded. This is an overload safety net,
not a normal flow-control mechanism; a rising count means publication rate, drain cadence, or
topic policy should be investigated.

## 3. Dispatch mode

Dispatch decides which queued publications are delivered during a drain. It does not decide
what is retained for a future subscriber.

### `DispatchMode.All`

Every queued value is delivered in publication order.

Use it when each occurrence matters. For example:

- A system console should see "reactor starting," "reactor online," and "shield charging"
  even if all three were posted before the next frame.
- A damage-event listener may need every impact rather than only the last impact in a frame.
- A high-fidelity graph may need every sample produced between UI frames.
- An audit or diagnostic stream may need to preserve the complete order of observations.

The `TopicPolicy.OrderedTransient` preset selects `All` with no retention.

### `DispatchMode.LatestPerDrain`

Only the last queued publication for a topic is delivered in one drain. Coalescing is
performed independently per topic.

Use it when intermediate values between two presentation frames have no observable value.
For example:

- A cockpit needle only needs the most recent reactor output when the frame is drawn.
- A progress bar displaying current capacitor charge does not benefit from processing five
  intermediate charge values before the same render.
- A status lamp needs the final on/off state, not every state assignment made while a
  component completed its tick.

Coalescing is only per drain. It does not compare a value with the preceding drain and does
not suppress an unchanged value published on a later tick. If a producer contract says
`PublicationMode.OnChange`, the producer remains responsible for publishing only when its
meaningful state changes.

The `TopicPolicy.LatestState` preset selects `LatestPerDrain` with latest-value retention.

## 4. Retention mode

Retention decides what successfully drained data remains available to subscribers created
later. It is transient bus memory, not saved-game persistence.

### `RetentionMode.None`

Nothing is kept after dispatch.

Use it for occurrences that become meaningless once delivered. For example:

- A button-click-like event should not fire again when a panel is reopened.
- A current-frame diagnostic pulse has no useful state to reconstruct.
- A radar-contact-loss event should not be treated as a current value.

No replay mode can recover data that was never retained.

### `RetentionMode.Latest`

The most recently drained value for each topic is kept.

Use it for current state and for slow or command-triggered readings. For example:

- A needle instrument created after the reactor began running should immediately show the
  current output instead of waiting for the next report.
- A component-status panel should know that the shield is already initializing when the
  panel opens.
- A solar-spectrum scan may have taken time and power. Reopening the spectrum panel should
  show the last completed result rather than pretending the measurement never happened.
- Telemetry and device descriptions must be available to UI created after the device was
  initialized.

Latest retention is usually the right default for scalar, vector, spectrum, metadata, and
device-state topics. It is bounded to one value per topic.

### `RetentionMode.History`

The most recent fixed number of drained values is kept per topic. The oldest retained value
is removed when the configured capacity is exceeded. A positive capacity is mandatory.

Use it when a late subscriber needs a recent time window. For example:

- A graph opened after a minute of flight can draw the most recent 60 seconds rather than
  starting empty.
- A console panel can show recent messages posted while it was closed.
- A diagnostic trend view can reconstruct the last few hundred samples around a fault.

History must always be deliberately bounded. Estimate a capacity from the producer's real
publication frequency and the useful display window. A 4 Hz sensor with a desired one-minute
graph needs roughly 240 retained samples, not an arbitrary unbounded list.

`TopicPolicy.OrderedHistory(capacity)` delivers and retains every value.
`TopicPolicy.CoalescedHistory(capacity)` delivers and retains one value per topic per drain.
The latter is useful when the history should correspond to presentation frames rather than
every simulation publication.

History retention also stores the latest value, so a subscriber may request only the latest
entry from a history topic.

## 5. Replay mode

Replay is chosen by the subscriber. It controls what retained data is delivered immediately
to that new handler when it subscribes.

### `ReplayMode.None`

Start with future dispatches only.

Examples:

- A transient HUD alert should not re-alert the player about all warnings already shown in
  the console.
- A sound or animation triggered by an event must not run merely because its control was
  reconstructed.
- A counter intended to count occurrences from now onward should not include earlier ones.

### `ReplayMode.Latest`

Immediately deliver the latest retained value, if one exists, then receive future values.

Examples:

- A needle, lamp, text readout, or progress meter normally wants the current value.
- An instrument reopened after being hidden should resume at the last known reading.
- A UI binding for `TelemetryInfo`, `DeviceInfo`, or `DeviceState` should normally request
  the current retained description or state.

Value and sample subscriptions on `TelemetryChannel<T>` default to `ReplayMode.Latest`.
Plain `Bus<T>` subscriptions default to `ReplayMode.None`, so event consumers do not
accidentally replay old events.

### `ReplayMode.History`

Immediately deliver retained history in chronological order, then receive future values.

Examples:

- A graph requests history so it can populate the visible window as soon as it opens.
- The system console requests history so recent messages remain readable after UI rebuild.

If a topic retains only a latest value, history replay falls back to that one value. If it
retains nothing, replay produces no callback.

Replay is a direct call to the new handler. It is not republished, re-enqueued, or sent to
existing subscribers. This is why adding a graph does not make existing instruments see a
duplicate data point.

Replay occurs synchronously inside `Subscribe`. Initialize any collection or other state the
handler needs before subscribing. A handler exception propagates to the caller; the failed
subscription is removed automatically. Handlers invoked during normal drain should likewise
avoid throwing, because an exception interrupts that drain.

## 6. Choosing a policy

Think about dispatch and retention separately:

| Use case | Dispatch | Retention | Typical replay |
|---|---|---|---|
| Current needle/bar/lamp value | Latest per drain | Latest | Latest |
| Component operational state | Latest per drain | Latest | Latest |
| Sensor/device metadata | Latest per drain | Latest | Latest |
| Command-triggered scan result | Latest per drain | Latest | Latest |
| Transient event or animation trigger | All | None | None |
| System console | All | Bounded history | History |
| New-only HUD alert from system messages | All | Bounded history | None |
| Graph requiring every sample | All | Bounded history | History |
| Graph requiring at most one point per UI drain | Latest per drain | Bounded history | History |

All six dispatch/retention combinations are valid when their semantics are intentional:

| Dispatch | Retention | Meaning |
|---|---|---|
| All | None | Deliver every occurrence now; offer no late-subscriber reconstruction |
| All | Latest | Deliver every occurrence now; offer only the final current value later |
| All | History | Deliver every occurrence now; offer a bounded recent stream later |
| Latest per drain | None | Deliver only current presentation state; offer no reconstruction later |
| Latest per drain | Latest | Deliver current state and reconstruct the most recent state later |
| Latest per drain | History | Deliver and retain one presentation sample per topic per drain |

The named presets cover the most common combinations. A less common combination can be
declared directly:

```csharp
var policy = new TopicPolicy(
    DispatchMode.All,
    RetentionMode.Latest);
```

`HistoryCapacity` must be positive for history retention and must remain zero for the other
retention modes. Invalid policies throw during bus construction, topic configuration, or
`PublishTelemetryInfo` validation rather than silently doing something unbounded.

A publisher should not select a policy based on one particular control. It should describe
the semantic contract of the topic. A graph and a needle may subscribe to the same telemetry
topic using different replay modes, but both receive the same dispatch stream. If the graph
needs samples the topic deliberately discarded through coalescing, it is a different data
requirement and may need an ordered topic or a separate deliberately sampled topic.

Configure a topic once as part of publishing its contract. Do not repeatedly change its
policy at runtime. `ConfigureTopic` removes retained data that is incompatible with the new
policy, and queued values are interpreted using the policy active when they are drained.

## 7. Telemetry samples, time, and sequence

All typed telemetry is transported as:

```csharp
public readonly record struct TelemetrySample<T>(
    T Value,
    double SimulationTime,
    ulong Sequence);
```

`TelemetryChannel<T>.Publish(topic, value)` captures `GameClock.SimTime`. An overload accepts
an explicit simulation time when the measurement belongs to a known time other than the
instant of publication.

`SimulationTime` is currently session-local simulation seconds. It is suitable for plotting
and calculating intervals within the current session. It is not yet the future system-wide
Universe Time/Date and must not be treated as persistent identity.

`Sequence` is an increasing counter for the telemetry channel instance. It separates and
orders samples that have the same simulation time and can detect ordering or gaps within the
live channel. It is not a database key, is not persisted, and callers must not depend on it
starting at zero. Clearing retained data does not promise to reset it.

Subscribers that only need a value use `Subscribe`. Graphs, recorders, or diagnostics that
need time and sequence use `SubscribeSamples`.

## 8. Telemetry and device descriptions

Telemetry data is accompanied by two distinct descriptions and one live state record.

### `TelemetryInfo`: what does one topic mean?

`TelemetryInfo` is keyed by telemetry topic and describes one published signal:

- `Topic`: the complete topic string.
- `DeviceId`: the sensor or component that owns the topic.
- `ValueKind`: scalar, vector, or spectrum; this also selects the telemetry channel.
- `Quantity`: the physical meaning, such as power, pressure, temperature, or direction.
- `ReferenceFrame`: essential for vectors; not applicable for most scalars.
- `OperatingRange`: the physical range the producer can report.
- `SuggestedDisplayRange`: a useful default meter or graph range. A UI may choose another.
- `Bands`: warning and critical ranges. Multiple disjoint bands are supported.
- `Publication`: whether reports occur every tick, periodically, on change, or on command,
  plus an optional nominal frequency.
- `TopicPolicy`: dispatch and retention semantics for the topic.

Publish telemetry descriptions through `DataBus.PublishTelemetryInfo(info)`, not directly
through `DataBus.TelemetryInfo.Publish`. The helper validates the description, configures the
correct typed telemetry channel, records device ownership for cleanup, and then publishes the
retained description.

Physical telemetry is published in raw SI units. Unit conversion belongs in the
Inferior-specific presentation binding: kelvin to Celsius, watts to megawatts, radians to
degrees, and so on. `PhysicalQuantity` says what the number means; it does not authorize a
producer to publish a display-friendly non-SI value.

### `DeviceInfo`: what can one device do?

`DeviceInfo` is keyed by device ID and describes the device-level bus surface:

- all telemetry topics it publishes;
- all command topics it accepts;
- its `PowerProfile`, including idle watts, active watts, activation energy, and activation
  duration.

This is separate from `TelemetryInfo` because one device may publish several signals with
different units, ranges, frames, and report rates.

### `DeviceState`: what condition is the device in now?

`DeviceState` is keyed by device ID and describes observable live condition:

- operational status;
- damage and efficiency;
- the simulation time at which the state was reported.

Publish state on meaningful change unless a concrete consumer requires another cadence. A
sent command is acknowledged through this state or another returned publication, not through
the fact that `CommandBus.Send` succeeded.

All metadata collections are immutable. For other reference payloads, the bus does not clone
objects or arrays. The publisher must publish a stable snapshot that it will not mutate, and
subscribers must treat received data as read-only. The solar-spectrum sensor, for example,
clones its reusable working array before publication.

## 9. Topic naming

Use shared constants from `Topics` and existing device command-topic definitions. Do not
scatter duplicated string literals through publishers and subscribers.

Most component telemetry follows a device-qualified form such as:

```text
PowerCore.PowerOutput
GravitySensor.Direction
SolarSpectrumSensor.Data
```

Multiple installed instances need unique device IDs, and their full topics must remain
unique. Some established constants already contain their complete qualified topic. Verify the
actual constant before composing it; do not blindly add a second prefix.

Command subscriptions match by ordinal prefix, not exact equality. A subscription to
`Reactor` also matches `Reactor.Throttle.Set` and `Reactor.Output.Query`. Use a unique device
prefix and avoid ambiguous names where one device ID is the beginning of another device ID.
Handlers should still inspect `command.Topic` when one prefix covers several commands.

## 10. Publishing scalar telemetry

The publisher first declares the contract, then publishes readings. Contract publication
normally happens once during sensor/component initialization, before or alongside the first
reading.

```csharp
using Inferior.Core.DataBus;

public sealed class CoolantPressureSensor
{
    public const string DeviceId = "CoolantPressureSensor";
    public const string PressureTopic = "CoolantPressureSensor.Pressure";

    public CoolantPressureSensor()
    {
        DataBus.PublishTelemetryInfo(new TelemetryInfo
        {
            Topic = PressureTopic,
            DeviceId = DeviceId,
            ValueKind = TelemetryValueKind.Scalar,
            Quantity = PhysicalQuantity.Pressure,
            OperatingRange = new RangeValue(0.0, 2_000_000.0),
            SuggestedDisplayRange = new RangeValue(0.0, 1_500_000.0),
            Bands =
            [
                new(new RangeValue(0.0, 300_000.0), TelemetryBandSeverity.Critical),
                new(new RangeValue(300_000.0, 500_000.0), TelemetryBandSeverity.Warning),
                new(new RangeValue(1_300_000.0, 1_500_000.0), TelemetryBandSeverity.Warning),
                new(new RangeValue(1_500_000.0, 2_000_000.0), TelemetryBandSeverity.Critical),
            ],
            Publication = new PublicationInfo(
                PublicationMode.Periodic,
                NominalFrequencyHz: 4.0),
            TopicPolicy = TopicPolicy.LatestState,
        });

        DataBus.DeviceInfo.Publish(DeviceId, new DeviceInfo
        {
            DeviceId = DeviceId,
            PublishedTopics = [PressureTopic],
            Power = new PowerProfile(IdleWatts: 2.0, ActiveWatts: 2.0),
        });
    }

    public void PublishReading(double pressurePascals)
        => DataBus.ScalarTelemetry.Publish(PressureTopic, pressurePascals);
}
```

`PublishReading` may run on the simulation thread. The value is pascals; a UI displaying bar
or atmospheres converts it after subscription.

For a graph retaining one minute at 4 Hz, change the contract to:

```csharp
TopicPolicy = TopicPolicy.OrderedHistory(capacity: 240),
```

That policy is a statement that all 240 samples have semantic value, not merely a UI tweak.

## 11. Publishing vectors

Publish a vector atomically. Do not split it into independent X/Y/Z topics: components could
otherwise be taken from different measurements or drains, and the reference frame would be
easy to lose.

```csharp
using Inferior.Core.DataBus;
using Inferior.Core.Math;

const string deviceId = "GravitySensor";
const string directionTopic = "GravitySensor.Direction";

DataBus.PublishTelemetryInfo(new TelemetryInfo
{
    Topic = directionTopic,
    DeviceId = deviceId,
    ValueKind = TelemetryValueKind.Vector,
    Quantity = PhysicalQuantity.Direction,
    ReferenceFrame = TelemetryReferenceFrame.SystemEcliptic,
    Publication = new PublicationInfo(PublicationMode.EveryTick),
    TopicPolicy = TopicPolicy.LatestState,
});

DVec3 direction = gravitationalVector.Normalized();
DataBus.VectorTelemetry.Publish(directionTopic, direction);
```

Only normalize when normalization is part of the topic contract. A velocity, acceleration,
or magnetic-field vector normally preserves magnitude and uses the matching physical
quantity. Always declare `ReferenceFrame` for vector data.

## 12. Publishing spectra or array-valued results

The spectrum channel currently carries `double[]`. Publish a stable result snapshot:

```csharp
DataBus.PublishTelemetryInfo(new TelemetryInfo
{
    Topic = "SolarSpectrumSensor.Data",
    DeviceId = "SolarSpectrumSensor",
    ValueKind = TelemetryValueKind.Spectrum,
    Quantity = PhysicalQuantity.NormalizedRatio,
    OperatingRange = new RangeValue(0.0, 1.0),
    SuggestedDisplayRange = new RangeValue(0.0, 1.0),
    Publication = new PublicationInfo(PublicationMode.OnCommand),
    TopicPolicy = TopicPolicy.LatestState,
});

double[] publishedResult = (double[])reusableWorkBuffer.Clone();
DataBus.SpectrumTelemetry.Publish("SolarSpectrumSensor.Data", publishedResult);
```

Latest retention is especially important here: the measurement may be expensive and may not
be repeated until commanded. The retained result survives panel teardown and reconstruction,
but it does not survive a process restart. Persisting meaningful sensor state is the owning
sensor/ship persistence layer's future responsibility; a bus cache is not saved-game truth.

## 13. Publishing device state and messages

Publish device state under the device ID:

```csharp
DataBus.DeviceState.Publish(deviceId, new DeviceState(
    DeviceId: deviceId,
    Status: DeviceOperationalStatus.Initializing,
    Damage: damage,
    Efficiency: efficiency,
    SimulationTime: GameClock.SimTime));
```

Publish a human-readable message separately:

```csharp
DataBus.SystemMessages.Publish(
    Topics.System.All,
    new SystemMessage(
        $"{deviceId}: scan complete",
        SystemMessagePriority.Info));
```

State is machine-readable current truth. A system message is prose for a person. Do not make
a control parse message text to discover component state.

## 14. Subscribing to a current value

Retain and dispose the returned subscription. A latest replay is the telemetry default, but
specifying it can make intent clearer at an architectural boundary.

```csharp
public sealed class ReactorOutputBinding : IDisposable
{
    private readonly IDisposable _subscription;

    public ReactorOutputBinding(Action<double> setMeterValue)
    {
        _subscription = DataBus.ScalarTelemetry.Subscribe(
            "PowerCore.PowerOutput",
            setMeterValue,
            ReplayMode.Latest);
    }

    public void Dispose() => _subscription.Dispose();
}
```

If the bus value is watts and the control displays megawatts, convert in this
Inferior-specific binding:

```csharp
_subscription = DataBus.ScalarTelemetry.Subscribe(
    "PowerCore.PowerOutput",
    watts => meter.SetValue(watts * 1e-6),
    ReplayMode.Latest);
```

The generic meter should ideally know only that it received a number. It should not know
about `DataBus`, `Topics`, ship components, or SI conversion. Some existing controls still
auto-subscribe as transition-era convenience; new extractable generic controls should use an
Inferior-specific binding like the one above.

`BusSubscription<T>` is a small ownership wrapper when a field of that form is convenient:

```csharp
private readonly BusSubscription<double> _subscription =
    new(DataBus.ScalarTelemetry, topic, OnValue, ReplayMode.Latest);
```

It still must be disposed by its owner.

## 15. Subscribing to timestamped history

A graph or recorder subscribes to samples rather than bare values:

```csharp
public sealed class PressureGraphBinding : IDisposable
{
    private readonly List<TelemetrySample<double>> _samples = [];
    private readonly IDisposable _subscription;

    public PressureGraphBinding()
    {
        // _samples exists before Subscribe because history replay is synchronous.
        _subscription = DataBus.ScalarTelemetry.SubscribeSamples(
            "CoolantPressureSensor.Pressure",
            OnSample,
            ReplayMode.History);
    }

    private void OnSample(TelemetrySample<double> sample)
    {
        _samples.Add(sample);
        TrimInvisibleDisplayData();
    }

    private void TrimInvisibleDisplayData()
    {
        // The bus bounds retained history. The UI should also bound its own collection
        // according to its visible time window.
    }

    public void Dispose() => _subscription.Dispose();
}
```

Bus history bounds the replay cache, not the subscriber's private collection. A long-lived
graph must still discard points outside its display window.

## 16. Subscribing to metadata and device state

The consumer must know the topic or device ID; the current buses do not provide wildcard
subscriptions or enumeration.

```csharp
private readonly IDisposable _telemetryInfoSubscription =
    DataBus.TelemetryInfo.Subscribe(
        "CoolantPressureSensor.Pressure",
        ApplyTelemetryInfo,
        ReplayMode.Latest);

private readonly IDisposable _deviceInfoSubscription =
    DataBus.DeviceInfo.Subscribe(
        "CoolantPressureSensor",
        BuildDeviceCommands,
        ReplayMode.Latest);

private readonly IDisposable _deviceStateSubscription =
    DataBus.DeviceState.Subscribe(
        "CoolantPressureSensor",
        ApplyDeviceState,
        ReplayMode.Latest);
```

A generic instrument can use `SuggestedDisplayRange` and `Bands` to configure its scale and
coloured regions. It should still handle missing metadata sensibly: metadata publication is
queued and may arrive on the next drain, and legacy or debug topics may not yet publish a
complete description.

## 17. Subscribing to events and system messages

Choose replay based on the consumer, even when several consumers share one retained stream:

```csharp
// A console reconstructs recent context.
IDisposable consoleSubscription = DataBus.SystemMessages.Subscribe(
    Topics.System.All,
    console.Append,
    ReplayMode.History);

// A transient HUD alert reacts only to new messages.
IDisposable alertSubscription = DataBus.SystemMessages.Subscribe(
    Topics.System.All,
    alerts.ShowIfImportant,
    ReplayMode.None);
```

Both receive future messages. Only the console gets the retained backlog, and its replay does
not cause the HUD handler to run again.

## 18. Sending and handling commands

The UI sends a command request:

```csharp
CommandBus.Send("SolarSpectrumSensor.Scan");
CommandBus.Send("Reactor.Throttle.Set", value: 0.75);
```

The simulation-owned device subscribes and owns that subscription:

```csharp
public sealed class ExampleScanner : IDisposable
{
    private readonly IDisposable _commandSubscription;

    public ExampleScanner(string deviceId)
    {
        DeviceId = deviceId;
        _commandSubscription = CommandBus.Subscribe(
            $"{deviceId}.",
            HandleCommand);
    }

    public string DeviceId { get; }

    private void HandleCommand(ComponentCommand command)
    {
        if (command.Topic == $"{DeviceId}.Scan")
            StartPowerAndScanLifecycle();
    }

    private void StartPowerAndScanLifecycle()
    {
        // Simulation-owned implementation charges, works, and later publishes its result.
    }

    public void Dispose() => _commandSubscription.Dispose();
}
```

`CommandBus.Subscribe` and `CommandBus.Drain` belong to the simulation thread. `Send` is
thread-safe. A handler may reject a command because the device is unavailable, already busy,
or lacks power. The UI therefore displays pending/confirmed state from returned bus data; it
must not announce success solely because it called `Send`.

`ShipComponent` subclasses should normally use `RegisterCommand` instead of subscribing
directly. `ActivateBus` and `DeactivateBus` then attach and detach handlers with the active
ship lifecycle, preventing an abandoned ship's components from handling commands.

## 19. Creating and using a standalone `Bus<T>`

Use an existing `DataBus` channel unless the payload has genuinely different semantics and a
concrete use case. If a new channel is warranted, give it an explicit default policy and make
its owner drain it:

```csharp
public readonly record struct HullImpact(double EnergyJoules, string SurfaceId);

var impacts = new Bus<HullImpact>(
    defaultPolicy: TopicPolicy.OrderedTransient,
    pendingCapacity: 4_096);

IDisposable subscription = impacts.Subscribe(
    "Hull.Impact",
    HandleImpact,
    ReplayMode.None);

impacts.Publish("Hull.Impact", impact);

// Called by the channel's owning consumer thread.
impacts.Drain();
```

Per-topic policy can override the bus default:

```csharp
impacts.ConfigureTopic(
    "Hull.LastMajorImpact",
    TopicPolicy.LatestState);
```

Do not create duplicate buses for scalar data merely because a new instrument needs it. Add
a well-defined topic to the appropriate typed channel.

## 20. Cleanup and lifecycle boundaries

Every subscription needs an explicit owner and deterministic lifetime.

- A UI panel or binding disposes subscriptions when removed or replaced.
- A component detaches command subscriptions when its ship stops being active.
- A sensor detaches its commands when the sensor is removed.
- Disposing a subscription more than once is safe.
- Prefer retaining the returned `IDisposable` over the compatibility `Unsubscribe` method.

Retained data also has a lifetime. After a device is removed:

1. stop all its publishers;
2. drain their already queued publications;
3. call `DataBus.RemoveDevice(deviceId)`.

`RemoveDevice` forgets the device's retained scalar, vector, and spectrum values, its
`TelemetryInfo`, its `DeviceInfo`, and its `DeviceState`. It deliberately does not search and
rewrite pending queues, which is why publisher shutdown and drain must happen first.

At a simulation/session boundary, `DataBus.ClearRetained()` clears retained presentation
state while leaving subscriptions and topic policies intact. It does not make bus caches into
persistent state and should only be used as part of an orderly boundary where old publishers
cannot immediately repopulate the caches with stale queued data.

## 21. Persistence

Retention and persistence are intentionally different:

- Retention helps a late subscriber within the live process/session.
- Persistence makes meaningful ship or instrument state survive save/load or restart.

The buses currently start as transient infrastructure. Future save support may persist a
sensor's last meaningful result, graph data, or cockpit layout as ship/instrument state and
republish it after restoration. It may instead restore selected bus-facing snapshots. That
decision remains open.

Code written now must not use `TelemetrySample.Sequence` as persistent identity or assume
session-local `SimulationTime` is Universe Time. Persist the underlying meaningful state with
the owning ship, component, sensor, or instrument, then allow live buses to resume with fresh
transport counters.

The solar spectrum is the representative case: the completed scan is retained for live UI
reconstruction today, while the sensor/ship should eventually persist the result so restarting
the game cannot erase a measurement the ship had already made.

## 22. Common mistakes

### Publishing directly to a HUD control

Do not give a sensor a control reference or let a HUD read a live component. Publish the
observable result and bind the control on the presentation side.

### Treating a command as confirmation

`CommandBus.Send` only queues a request. Publish confirmed state or a result after the
simulation handles it.

### Selecting retention at each publish call

Retention is a stable topic contract, not a property of an individual publication. Configure
it through `TelemetryInfo.TopicPolicy` or `ConfigureTopic`.

### Using latest dispatch for an event stream

Coalescing can silently discard meaningful occurrences. Use `DispatchMode.All` when every
event or sample matters.

### Using ordered dispatch for high-rate current state without need

A needle processing many obsolete intermediate values wastes work. Use latest-per-drain when
only the final current state matters.

### Assuming coalescing means on-change

Latest-per-drain removes intermediate queued values only. The publisher must suppress equal
values across ticks when the contract is on-change.

### Requesting history from a non-retained topic

Replay cannot recreate data that was not retained. Configure bounded history on the topic
first.

### Forgetting that replay is synchronous

Prepare handler state before subscribing. A latest or history callback may occur inside the
`Subscribe` call.

### Forgetting to dispose

Static buses can otherwise retain an abandoned control, component, sensor, ship, or game
state through its handler delegate.

### Publishing mutable buffers

The bus does not clone arrays or objects. Publish a snapshot and do not mutate it afterward.

### Hiding units or reference frames

Publish raw SI, declare the physical quantity, declare vector reference frames, and convert
only in the Inferior-specific presentation layer.

### Using an unbounded local graph collection

Bounded bus history does not bound a subscriber's own list. Trim the UI or recorder's data as
it receives samples.

## 23. Checklist for a new instrument path

Before considering a sensor-to-HUD path implemented, verify all of the following:

- The sensor or component owns the measurement and runs on the correct side of the
  simulation boundary.
- The topic is authoritative, unique, and shared rather than duplicated as string literals.
- The correct scalar, vector, or spectrum channel is used.
- The payload is raw SI where applicable.
- Vector data is atomic and declares its reference frame.
- `TelemetryInfo` describes quantity, ranges, bands, cadence, and topic policy.
- `DeviceInfo` lists publications, commands, and its power profile.
- `DeviceState` reports observable state changes.
- Dispatch, retention, and replay were each chosen for their own reason.
- Any history has a capacity derived from frequency and useful duration.
- Active work is triggered by `CommandBus`, including the power/charge/work lifecycle.
- The UI waits for returned state or data instead of treating the command as success.
- The subscriber owns and disposes its subscription.
- Mutable payloads are published as stable snapshots.
- Device removal stops publishers, drains pending work, and clears retained ownership.
- Tests exercise the producer, bus contract, replay/retention behavior, lifecycle, and at
  least one consumer binding.
