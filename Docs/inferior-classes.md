# Inferior — Classes & Interfaces Reference

> Sketches and agreed designs from design sessions.
> These are design-level sketches, not final implementation.
> See `inferior-design.md` for reasoning behind each decision.

---

## Power system

### `PowerNode`
Base unit for anything in the power graph.

```csharp
public class PowerNode
{
    public double MaxOutput    { get; }   // MW capacity
    public double CurrentLoad  { get; }   // MW currently drawn
    public double Efficiency   { get; }   // 0.0–1.0

    // The only physics that matters:
    public double HeatGenerated  => CurrentLoad * (1.0 - Efficiency);
    public double PowerDelivered => CurrentLoad * Efficiency;
    public bool   IsOverloaded   => CurrentLoad > MaxOutput;
}
```

### `PowerComponent`
Extends `PowerNode` with damage and thermal feedback.

```csharp
public class PowerComponent : PowerNode
{
    public double BaseEfficiency { get; init; }   // design spec, e.g. 0.92
    public double DamageLevel    { get; private set; }  // 0.0 = pristine, 1.0 = destroyed

    // Damage degrades efficiency
    public double CurrentEfficiency
        => BaseEfficiency * (1.0 - DamageLevel * 0.6);
    // At 50% damage: 0.92 * 0.7 = 0.64 → much more heat, less output

    public double HeatOutput(double powerFlow)
        => powerFlow * (1.0 - CurrentEfficiency);

    public void AccumulateDamage(double excessHeat, double dt)
        => DamageLevel = Math.Min(1.0, DamageLevel + excessHeat * dt * 0.001);
}
```

Superconducting components: `BaseEfficiency = 1.0` — zero heat regardless of load.

### `enum PowerPriority`

```csharp
public enum PowerPriority
{
    Critical,  // life support — never starved
    High,      // navigation — starved last
    Normal,    // weapons
    Low        // luxury — starved first
}
```

### Power simulation tick

```csharp
void SimulatePower(double dt)
{
    // 1. Generator produces power
    double available = generator.CurrentOutput; // may be throttled

    // 2. Distribute by priority — fill capacitors and direct consumers
    double remaining = DistributePower(available, dt);
    // remaining < 0 means starvation occurred

    // 3. Each component generates heat proportional to power received
    foreach (var component in components)
        component.GenerateHeat(component.PowerReceived, dt);

    // 4. Heat flows to coolant loop
    double totalHeat = components.Sum(c => c.HeatOutput);
    double coolingCapacity = coolingSystem.CurrentCapacity; // reduced if damaged
    double excessHeat = Math.Max(0, totalHeat - coolingCapacity);

    // 5. Excess heat stays local — damages components over time
    DistributeExcessHeat(excessHeat, dt);

    // 6. Publish to bus
    DataBus.Instruments.Publish("power.available", available);
    DataBus.Instruments.Publish("power.demand", totalDemand);
    DataBus.Instruments.Publish("thermal.load", totalHeat / coolingCapacity);
}
```

### `ShipSignature`

```csharp
public class ShipSignature
{
    // Thermal — heat radiating from hull/radiators
    // Hard to hide — physics
    public double ThermalSignature { get; }

    // EM — radiation from power systems running current
    // Controllable — throttle generator, switch to capacitors
    public double EMSignature { get; }
}
```

---

## Heat system

### `ThermalNode`

```csharp
public class ThermalNode
{
    public double HeatCapacity    { get; }  // MJ — how much it can absorb
    public double DissipationRate { get; }  // passive cooling, MW
    public double CurrentHeat     { get; private set; }

    // Normalised 0–1 — maps directly to green/yellow/red gauge ranges
    public double Temperature => CurrentHeat / HeatCapacity;

    public void Update(double heatInput, double dt)
    {
        double dissipated = DissipationRate * dt;
        CurrentHeat = Math.Max(0, CurrentHeat + (heatInput - dissipated) * dt);
    }

    // Thresholds drive DataBus messages
    public bool IsWarning  => Temperature > 0.7;
    public bool IsCritical => Temperature > 0.9;
    public bool IsFailure  => Temperature >= 1.0;  // component takes damage or shuts down
}
```

### `HyperspaceHeatSink`

```csharp
public class HyperspaceHeatSink
{
    public double Capacity     { get; }   // MJ before saturated
    public double CurrentLoad  { get; private set; }
    public double TransferRate { get; }   // MW it can absorb
    public double PowerDraw    { get; }   // costs energy to run
    public bool   IsSaturated  => CurrentLoad >= Capacity;

    // When saturated — dumps heat back into realspace instantly
    // Massive thermal spike + EM burst — every passive sensor in range lights up
    public event Action? OnSaturation;
}
```

Generator thermal curve constants (tunable per generator type):

```csharp
// Heat multiplier by output %:
// 0–60%  → 1.0×  (efficient range)
// 60–80% → 1.5×
// 80–90% → 2.5×
// 90–100%→ 4.0×  (redline, damage accumulates)
```

---

## Hull & damage

### `HullElement` (one per mesh face)

```csharp
public class HullElement
{
    public int       FaceIndex          { get; init; }
    public string    PanelId            { get; init; }   // references panel registry
    public double    Integrity          { get; private set; } = 1.0;
    public Rarity    Rarity             { get; init; }
    public HullType[] CompatibleHullTypes { get; init; }

    // Visual state driven by integrity (modulate diffuse colour per face):
    // > 0.8  → Intact    (normal hull colour)
    // > 0.5  → Damaged   (slightly darker)
    // > 0.2  → Critical  (visible scoring, heat discolouration)
    // = 0.0  → Failed    (black / transparent — open space)
}
```

Panel registry: panels exist once, ships reference them. Adding a new ship doesn't multiply the panel database.

### `InternalComponent`

```csharp
public class InternalComponent
{
    public double Integrity    { get; private set; } = 1.0;
    public double Fragility    { get; init; }
    // Shield generator: high  — designed to be protected
    // Fuel tank:        extreme — catastrophic if hit
    // Power bus:        moderate
    // Structural frame: low — hard to destroy

    // Which hull faces protect this component
    public int[] ProtectedByFaces { get; init; }

    // Exposure increases as covering elements degrade
    public double ExposureLevel(HullElement[] elements)
    {
        if (ProtectedByFaces.Length == 0) return 0;
        return 1.0 - ProtectedByFaces
            .Select(f => elements[f].Integrity)
            .Average();
    }

    // Probability this component is hit when a covering face is struck
    public double HitProbability(int faceIndex, HullElement[] elements)
    {
        if (!ProtectedByFaces.Contains(faceIndex)) return 0;
        double elementIntegrity = elements[faceIndex].Integrity;
        return Math.Pow(1.0 - elementIntegrity, 2.0);
        // Pristine panel → near zero. Destroyed panel → near certain.
    }

    public ComponentState State => Integrity switch
    {
        > 0.8 => ComponentState.Nominal,
        > 0.5 => ComponentState.Degraded,   // efficiency drops
        > 0.2 => ComponentState.Critical,   // may fail spontaneously
        > 0.0 => ComponentState.Failing,    // actively getting worse
        _     => ComponentState.Destroyed
    };
}
```

### `Shield`

```csharp
public class Shield
{
    public double Capacitor    { get; private set; }  // 0–1, never a binary on/off
    public double ChargeRate   { get; }               // from power bus
    public double MaxReduction { get; init; } = 0.85; // even full shields let 15% through

    // Damage reduction linear with charge
    public double DamageReduction => Capacitor * MaxReduction;

    // Power draw increases when depleted — shield working harder
    public double PowerDraw => BasePower * (1.0 + (1.0 - Capacitor) * 0.5);

    // Heat per hit increases as shield depletes
    // Low shield → more heat → threatens generator → slows recharge → spiral
    public double HeatPerHit(double incomingEnergy)
    {
        double absorbed   = incomingEnergy * DamageReduction;
        double efficiency = 0.95 - (1.0 - Capacitor) * 0.35;
        // Full charge: 95% efficient, minimal heat
        // Empty:       60% efficient, significant heat
        return absorbed * (1.0 - efficiency);
    }

    // Only way shield "dies" — hardware damage, not capacitor depletion
    public bool GeneratorIntact { get; private set; }
}
```

---

## Commander

### `CommanderState`

```csharp
public class CommanderState
{
    public double Sanity { get; private set; } = 1.0;

    // Emergency jump costs sanity
    public void EmergencyJump()
        => Sanity = Math.Max(0.3, Sanity - 0.15);

    // Recovers slowly at stations, faster at home station
    public void Rest(double hours)
        => Sanity = Math.Min(1.0, Sanity + hours * 0.05);

    // Effects are subtle — never gameplay-breaking
    // Published to DataBus.Instruments; HUD widgets subscribe and glitch at low sanity
    public double HUDFlickerChance  => Sanity < 0.7 ? (0.7 - Sanity) * 0.1 : 0;
    public double CommsDistortion   => Sanity < 0.5 ? (0.5 - Sanity) * 0.2 : 0;

    public string StatusDescription => Sanity switch
    {
        > 0.9 => "Nominal",
        > 0.7 => "Shaken",
        > 0.5 => "Disturbed",
        > 0.3 => "Traumatised",
        _     => "Unhinged"
    };
}
```

### `Commander`

```csharp
public class Commander
{
    public string   Name              { get; init; }
    public int      Seed              { get; init; }
    public DateTime CreatedDate       { get; init; }
    public TimeSpan PlayTime          { get; set; }

    public Ship     Ship              { get; set; }
    public Inventory Inventory        { get; set; }
    public long     Credits           { get; set; }
    public Reputation Reputation      { get; set; }

    public HashSet<int> VisitedSystems { get; }
    public List<PersistentException> PersistentExceptions { get; }

    public List<LogEntry> CaptainsLog { get; }
    public bool PermadeathEnabled     { get; set; }
}
```

---

## DataBus

### `DataBus` (static hub)

```csharp
public static class DataBus
{
    // System messages — device status, cold start sequence, state changes
    public static readonly Bus<string>       System           = new();

    // Live numeric instrument values — published every sim tick or on change
    // Topic convention: ComponentName.ValueName  (e.g. "PowerCore.PowerLoad")
    // Multiple components of same type: ComponentName_N.ValueName (e.g. "PowerCore_2.PowerLoad")
    public static readonly Bus<double>       Instruments      = new();

    // Dynamic instrument state — damage percent, efficiency — published on change
    public static readonly Bus<double>       InstrumentState  = new();

    // Instrument ranges — published at startup and when ranges change (e.g. on damage)
    public static readonly Bus<RangeValue>   InstrumentRanges = new();

    // Radar contact updates — published when a contact appears or changes
    public static readonly Bus<RadarContact> Radar            = new();

    // Radar contact lost — published when a contact disappears; subscribers handle cleanup
    public static readonly Bus<string>       RadarLost        = new();

    // Deferred — design pending:
    // public static readonly Bus<CommMessage>  Comms      = new();
    // public static readonly Bus<ScanResult>   Sensors    = new();
    // public static readonly Bus<NavData>      Navigation = new();

    // Called once per frame from Game.Update() on main thread
    // Drains all queued messages and dispatches handlers on main thread
    public static void Drain()
    {
        System.Drain();
        Instruments.Drain();
        InstrumentState.Drain();
        InstrumentRanges.Drain();
        Radar.Drain();
        RadarLost.Drain();
    }
}
```

### `Bus<T>`

Thread-safe: `Publish` may be called from any thread (simulation thread). `Drain` and
`Subscribe`/`Unsubscribe` must be called from the main thread only. Handlers always
execute on the main thread during `Drain`.

```csharp
public sealed class Bus<T>
{
    private readonly ConcurrentQueue<(string topic, T value)> _queue    = new();
    private readonly Dictionary<string, List<Action<T>>>      _handlers = new();

    // Called from any thread — enqueues only, never blocks
    public void Publish(string topic, T value)
        => _queue.Enqueue((topic, value));

    // Called from main thread once per frame — dispatches all pending messages
    public void Drain()
    {
        while (_queue.TryDequeue(out var msg))
            if (_handlers.TryGetValue(msg.topic, out var handlers))
                foreach (var h in handlers)
                    h(msg.value);
    }

    // Main thread only
    public void Subscribe(string topic, Action<T> handler)
    {
        if (!_handlers.TryGetValue(topic, out var list))
            _handlers[topic] = list = new();
        list.Add(handler);
    }

    public void Unsubscribe(string topic, Action<T> handler)
    {
        if (_handlers.TryGetValue(topic, out var list))
            list.Remove(handler);
    }
}
```

### `RangeValue` (readonly record struct)

```csharp
// Used on InstrumentRanges bus — describes the operating envelope of a value
public readonly record struct RangeValue(double Low, double High);

// Example startup publications from PowerCore:
// InstrumentRanges.Publish("PowerCore.PowerLoad", new RangeValue(0, 500));    // min/max MW
// InstrumentRanges.Publish("PowerCore.SafeRange",  new RangeValue(0, 300));   // safe operating range
// Re-published when ranges change, e.g. when component is damaged
```

### `RadarContact` (readonly record struct)

Radar publishes individual contact updates rather than full frame snapshots — low
allocation, no GC pressure. The radar display maintains its own current picture as a
`Dictionary<string, RadarContact>` keyed by `Id`, updating as messages arrive.

```csharp
public readonly record struct RadarContact(
    string      Id,               // unique stable object ID
    string      DisplayName,      // "Commander Olle", "Asteroid", "Unknown"
    Vector3     RelativePosition, // relative to player ship
    Vector3     RelativeVelocity, // relative to player ship
    ContactType Type);

public enum ContactType { Ship, Station, Asteroid, Missile, Debris, Unknown }
```

When a contact disappears, `DataBus.RadarLost.Publish("radar", contact.Id)` is called.
Subscribers (radar display, threat assessment, captain's log) handle cleanup independently.

### Simulation thread

The simulation runs as a background thread at a fixed timestep, independent of frame rate.
It publishes freely to `DataBus` buses. The main thread drains all pending messages once
per `Game.Update()`.

```csharp
// Startup — e.g. in Game.Initialize()
_simThread = new Thread(SimulationLoop) { IsBackground = true, Name = "SimThread" };
_simThread.Start();

void SimulationLoop()
{
    const double TickRate    = 1.0 / 60.0; // 60 Hz
    var          timer       = Stopwatch.StartNew();
    double       accumulated = 0;

    while (_running)
    {
        accumulated += timer.Elapsed.TotalSeconds;
        timer.Restart();

        while (accumulated >= TickRate)
        {
            SimulateTick(TickRate); // publishes to DataBus from sim thread
            accumulated -= TickRate;
        }

        Thread.Sleep(1); // yield — don't spin
    }
}

// In Game.Update() — main thread
protected override void Update(GameTime gameTime)
{
    DataBus.Drain(); // dispatch all queued messages on main thread
    // ... rest of update
}
```

**Threading guarantees:**
- `Publish` — safe from any thread, non-blocking
- `Drain` — main thread only, dispatches handlers synchronously
- `Subscribe` / `Unsubscribe` — main thread only
- If main thread is slow, messages queue up and drain next frame — no data lost
- Simulation thread never waits on main thread

### Deferred message types (design pending)

```csharp
// CommMessage — inter-ship and station communication, hyperspace delay simulation
// public record CommMessage(string SenderId, string Channel, string Text,
//                           double GameTime, CommType Type);
// public enum CommType { Hail, Response, Broadcast, Emergency, Automated }

// ScanResult — realistic instrument data from physics simulation
// spectral analysis, radiation measurements, planetary mineral scans, hyperspace scans
// Design note: values should come from actual in-game physics, not scripted data

// NavData — hyperspace travel data, destination, coordinates, ETA
```

---

## Physics

### Hyperspace interference check

> *Design note: the original mass lock mechanic has been superseded. Mass-based
> lock breaks lore near small bodies (asteroids, small moons). The replacement is
> **hyperspace interference** — a radius based on the ship's own power output and
> size rather than on nearby object mass. Implementation pending.*

```csharp
// Placeholder — final formula TBD; likely based on power core output
bool HyperspaceInterferenceLock(Ship ship)
    => ship.PowerSystem.CurrentOutput > hyperspaceInterferenceThreshold;
```

### Galaxy seed (deterministic)

```csharp
int SystemSeed(int galaxySeed, double x, double y, double z)
    => HashCode.Combine(
        galaxySeed,
        BitConverter.DoubleToInt64Bits(x),
        BitConverter.DoubleToInt64Bits(y),
        BitConverter.DoubleToInt64Bits(z));
// Same star always generates the same system regardless of visit order
```

---

## UI

### `IUIRenderer`

```csharp
public interface IUIRenderer
{
    void DrawPanel(Rectangle bounds, PanelStyle style);
    void DrawButton(Rectangle bounds, string text, ButtonState state);
    void DrawTextBox(Rectangle bounds, string text, bool focused);
    void DrawLabel(Rectangle bounds, string text);
    void DrawWindow(Rectangle bounds, string title, bool focused);
    // Add as needed
}
```

### `Control` (base)

```csharp
public abstract class Control
{
    public Rectangle Bounds     { get; set; }
    public Color     ForeColor  { get; set; }
    public Color     BackColor  { get; set; }
    public Color     TextColor  { get; set; }
    public string    Font       { get; set; }
    public float     FontSize   { get; set; }
    public bool      Visible    { get; set; }
    public bool      Focused    { get; set; }

    public event EventHandler? OnClick;
    public event EventHandler? OnFocus;
    public event EventHandler<int>? OnMouseWheel;  // delta for bars / numeric controls
}
```

Controls: `Button`, `Label`, `TextBox`, `Panel`, `Window` (draggable container),
`InstrumentMeter` (bar + value readout, subscribes to a DataBus topic),
`SystemConsole` (scrolling message log, supports Clip/Wrap/Bleed line-break modes),
`DirectionBall` (projects 3D direction vectors onto a 2D sphere — filled dot = front
hemisphere, hollow rim dot = rear hemisphere, central crosshair for alignment).

---

## Simulation loop

The simulation is the authoritative source of all game state. It runs on its own thread
at 60 Hz and owns the tick order. The main thread only reads — it never writes game state
directly. Player input is the one exception: it crosses from main thread to sim thread via
a shared immutable snapshot (see below).

### Tick order

Each simulation tick runs subsystems in a fixed order. Later subsystems depend on results
from earlier ones within the same tick.

```
1. Input       — consume latest PlayerInput snapshot
2. GameClock   — advance SimTime for this tick
3. Environment — sync world state so sensors have current positions
4. Physics     — apply thrust, update positions and velocities
5. Power       — distribute power, generate heat
6. Damage      — apply heat/impact damage, update component states
7. Radar       — scan nearby objects, diff against last frame
8. Publish     — push all values to DataBus buses (sensors tick here)
```

### `Simulation` class

Lives in `Inferior.Gameplay`. Concrete subclasses (e.g. `SpaceSimulation` in
`Inferior.Game`) override the protected virtual tick methods.

World state crosses from the main thread to the sim thread via a `volatile` immutable
snapshot record — same pattern as `PlayerInput`. The sim thread never blocks on the
main thread.

```csharp
// Inferior.Gameplay namespace
public class Simulation
{
    private volatile PlayerInput _input   = PlayerInput.Zero;
    private volatile bool        _running = false;
    private Thread?              _thread;

    public void Start() { ... }
    public void Stop()  { ... }

    // Called from main thread each frame — atomic reference swap
    public void SetInput(PlayerInput input) => _input = input;

    private void Tick(double dt)
    {
        var input = _input;         // read snapshot once

        GameClock.Advance(dt);      // advance central clock first
        UpdateEnvironment();        // sync world state for sensors

        TickPhysics(input, dt);
        TickPower(dt);
        TickDamage(dt);
        TickRadar();
        Publish();                  // sensors tick inside Publish()
    }

    // Override in subclass — all are no-ops in base
    protected virtual void UpdateEnvironment() { }
    protected virtual void TickPhysics(PlayerInput input, double dt) { }
    protected virtual void TickPower(double dt) { }
    protected virtual void TickDamage(double dt) { }
    protected virtual void TickRadar() { }
    protected virtual void Publish() { }
}
```

#### World state handoff — `WorldSnapshot` pattern

The concrete `SpaceSimulation` class maintains a `volatile WorldSnapshot?` record
written by the main thread via `SetWorldState(...)` and read atomically by
`UpdateEnvironment()` on the sim thread. The record is immutable, so no partial
reads are possible.

```csharp
// In SpaceSimulation (Inferior.Game):
private sealed record WorldSnapshot(Star Star, StarSystem System,
                                    DVec3 ShipPos, double GameTime);
private volatile WorldSnapshot? _worldSnapshot;

public void SetWorldState(Star star, StarSystem system,
                          DVec3 shipPos, double gameTime)
    => _worldSnapshot = new WorldSnapshot(star, system, shipPos, gameTime);

protected override void UpdateEnvironment()
{
    var snap = _worldSnapshot;
    if (snap == null) return;       // retain previous state until main thread provides one

    var world = SensorEnvironment.World;
    world.MassiveBodies.Clear();
    // ... populate from snap.System ...
    SensorEnvironment.UpdateFromSimThread(world, snap.ShipPos, DVec3.Zero);
}
```

### `PlayerInput` (immutable snapshot)

Written by the main thread from keyboard/gamepad state. The sim thread reads it once at
the start of each tick. Using an immutable record means no partial reads — the reference
swap is atomic on 64-bit .NET.

```csharp
public record PlayerInput(
    double ThrustForward,   // −1.0 to 1.0
    double ThrustLateral,   // −1.0 to 1.0 (strafing, if supported)
    double ThrustVertical,  // −1.0 to 1.0
    double RollInput,       // −1.0 to 1.0
    double PitchInput,      // −1.0 to 1.0
    double YawInput,        // −1.0 to 1.0
    bool   JumpRequested,
    bool   FlightAssist);   // flight assist on/off toggle state

public static readonly PlayerInput Zero = new(0,0,0,0,0,0,false,true);
```

### `TickPhysics`

Applies player input to drive offset, updates velocity and position. Gravitational field
alignment is applied here — the ship's reference frame is updated to match the nearest
large body.

```csharp
void TickPhysics(PlayerInput input, double dt)
{
    // 1. Map thrust input to drive wave offset
    _ship.Drive.SetOffset(input.ThrustForward, input.ThrustLateral, input.ThrustVertical);

    // 2. Apply rotational input
    _ship.Drive.SetRotation(input.PitchInput, input.YawInput, input.RollInput);

    // 3. Flight assist — damp lateral and rotational velocity toward zero
    if (input.FlightAssist)
        _ship.Velocity = ApplyFlightAssist(_ship.Velocity, dt);

    // 4. Gravitational field alignment — inherit reference frame of nearest body
    var nearestBody = _world.NearestMassiveBody(_ship.Position);
    _ship.ReferenceFrame = nearestBody?.ReferenceFrame ?? GalacticFrame.Zero;

    // 5. Integrate position
    _ship.Position += _ship.Velocity * dt;

    // 6. Mass lock — flag but don't enforce here; jump system checks it
    _ship.MassLocked = MassLocked(_ship, _world.NearbyObjects(_ship.Position));
}
```

### `TickPower`

Already documented in the Power system section. Called here in sequence.

```csharp
void TickPower(double dt)
    => _ship.PowerSystem.Simulate(dt); // publishes to DataBus internally
```

### `TickDamage`

Checks thermal thresholds on all components. Propagates damage where heat has exceeded
local capacity. Updates component states.

```csharp
void TickDamage(double dt)
{
    foreach (var component in _ship.Components)
    {
        if (component.ThermalNode.IsFailure)
            component.AccumulateDamage(component.ThermalNode.ExcessHeat, dt);

        // State change — publish to InstrumentState
        if (component.StateChanged)
            DataBus.InstrumentState.Publish(
                $"{component.TopicPrefix}.DamagePercent",
                component.DamageLevel);
    }
}
```

### `TickRadar`

Scans nearby objects, diffs against the previous frame's contact list. Publishes new or
updated contacts to `DataBus.Radar`. Publishes lost contacts to `DataBus.RadarLost`.

```csharp
void TickRadar()
{
    var currentIds = new HashSet<string>();

    foreach (var obj in _world.NearbyObjects(_ship.Position, radarRange))
    {
        var contact = new RadarContact(
            Id:               obj.Id,
            DisplayName:      obj.DisplayName,
            RelativePosition: obj.Position - _ship.Position,
            RelativeVelocity: obj.Velocity - _ship.Velocity,
            Type:             obj.ContactType);

        DataBus.Radar.Publish("radar", contact);
        currentIds.Add(obj.Id);
    }

    // Contacts in last frame but not this one — lost
    foreach (var id in _lastRadarIds)
        if (!currentIds.Contains(id))
            DataBus.RadarLost.Publish("radar", id);

    _lastRadarIds = currentIds;
}
```

### `Publish`

Pushes live instrument values to `DataBus.Instruments` every tick. Components that have
already published state changes in their own tick methods don't need to repeat here —
this pass covers continuous live values.

```csharp
void Publish()
{
    // Power
    DataBus.Instruments.Publish("PowerCore.PowerLoad",    _ship.PowerSystem.CurrentLoad);
    DataBus.Instruments.Publish("PowerCore.PowerOutput",  _ship.PowerSystem.CurrentOutput);
    DataBus.Instruments.Publish("Thermal.Load",           _ship.ThermalSystem.NormalisedLoad);
    DataBus.Instruments.Publish("Shield.Capacitor",       _ship.Shield.Capacitor);
    DataBus.Instruments.Publish("Drive.Offset",           _ship.Drive.CurrentOffset);
    DataBus.Instruments.Publish("Drive.FuelRemaining",    _ship.Drive.FuelRemaining);
    // Add as systems are implemented
}
```

### Startup publications

On cold start, each component publishes its ranges to `DataBus.InstrumentRanges` and its
initial state to `DataBus.System`. These fire once when the component initialises, before
the simulation loop begins.

```csharp
// Example — PowerCore.Initialise():
DataBus.System.Publish("system", "PowerCore online");
DataBus.InstrumentRanges.Publish("PowerCore.PowerLoad", new RangeValue(0, MaxOutput));
DataBus.InstrumentRanges.Publish("PowerCore.SafeRange",  new RangeValue(0, MaxOutput * 0.6));
```

---

## GameClock

Central time authority for the simulation. Two distinct time concepts:
- **SimTime** — accumulated simulation seconds since session start. Drives physics, noise
  functions, and anything that needs a continuous monotonic clock. Never paused.
- **PlayTime** — total real-world seconds played across all sessions. Persists to save file.
  Lives on the `Commander` record.
- **InGameDate** — fictional universe date shown to the player. Derived from SimTime plus
  a fixed lore epoch and a time scale factor. Open question: scale factor and epoch date.

```csharp
public static class GameClock
{
    // Accumulated sim seconds this session — advances every sim tick
    // Primary input for noise functions and physics
    // Note: written by sim thread, read by main thread for display.
    // On 64-bit .NET a double read is not guaranteed atomic — mark volatile
    // or use Interlocked.Read on a backing long if this ever causes issues.
    // Low priority: worst case is a stale timestamp on a HUD display for one frame.
    public static double SimTime { get; private set; }

    // Fictional in-universe date — derived, never stored
    // Epoch and scale are placeholders — to be decided
    private static readonly DateTime LoreEpoch     = new DateTime(3200, 1, 1);
    private static readonly double   TimeScale     = 1.0; // 1.0 = real time, 24.0 = 1 day per hour
    public  static DateTime           InGameDate
        => LoreEpoch.AddSeconds(SimTime * TimeScale);

    // Called by simulation thread every tick
    // public so Inferior.Gameplay (a different assembly) can call it
    public static void Advance(double dt)
        => SimTime += dt;
}
```

> *Design note: Time scale is an open question. ED uses roughly real time + offset, which
> players appreciate. Multiplayer compatibility favours real time. No time compression planned.*

---

## Environment

Static query class for world state relevant to sensors and noise sources. Acts as the
ship's local computer view of the surrounding environment — distances, field vectors,
stellar properties. All values derived from the simulation's `World` object.

The sim thread updates `World` each tick. `Environment` reads from it. Sensors and noise
functions call into `Environment` rather than taking direct references to world objects —
keeping the noise/sensor layer decoupled from the world representation.

```csharp
// Inferior.Gameplay.SensorData namespace
public static class Environment
{
    // Private setters — updated exclusively via UpdateFromSimThread()
    public static SimWorld World        { get; private set; } = new();
    public static DVec3    ShipPosition { get; private set; }
    public static DVec3    ShipVelocity { get; private set; }

    // Single entry point for sim-thread state updates — explicit, no accidental writes
    public static void UpdateFromSimThread(SimWorld world, DVec3 shipPos, DVec3 shipVel)
    {
        World        = world;
        ShipPosition = shipPos;
        ShipVelocity = shipVel;
    }

    // ── Nearest star ──────────────────────────────────────────────────────────

    public static CelestialBody NearestStar          => World.NearestStar(ShipPosition);
    public static double        DistanceToNearestStar => (NearestStar.Position - ShipPosition).Length;
    public static DVec3         DirectionToNearestStar
        => DVec3.Normalize(NearestStar.Position - ShipPosition);

    // ── Nearest body ─────────────────────────────────────────────────────────

    public static CelestialBody NearestBody           => World.NearestMassiveBody(ShipPosition);
    public static double        DistanceToNearestBody => (NearestBody.Position - ShipPosition).Length;
    public static double        DistanceToSurface     => DistanceToNearestBody - NearestBody.Radius;

    // ── Field vectors ─────────────────────────────────────────────────────────

    // Gravity bypasses SimWorld — calls GravityCalculations directly to avoid
    // a circular dependency (Physics ↔ SensorData)
    public static DVec3  GravitationalVector
        => GravityCalculations.GravityAt(ShipPosition, World.MassiveBodies);
    public static double GravitationalStrength => GravitationalVector.Length;

    public static DVec3  MagneticFieldVector   => World.MagneticFieldAt(ShipPosition); // stub
    public static double RadiationFlux         => World.RadiationAt(ShipPosition);     // stub

    // ── Stellar properties ────────────────────────────────────────────────────

    // StarPhysics lives in Inferior.Galaxy; Gameplay → Galaxy dep is intentional
    public static double NearestStarCorePressure
        => Galaxy.StarPhysics.CorePressure(NearestStar.Class, NearestStar.Mass);
}
```

> *Note: `SimWorld.MagneticFieldAt()` and `RadiationAt()` are stubs pending physics
> implementation. `GravityAt` is handled by `GravityCalculations` (see below).*

---

## Noise

Pure static functions for sensor noise generation. All functions are stateless —
they take `GameClock.SimTime` as their time input and return a value in roughly −1..1
(or 0..1 for non-bipolar sources). New noise sources are added here as the sensor
system is tuned.

The `Seed` parameter decorrelates multiple sensors using the same noise type —
each sensor instance gets a unique seed (e.g. derived from its topic name hash)
so they don't drift in lockstep.

```csharp
public static class Noise
{
    // ── 1D Simplex noise — the foundation ────────────────────────────────────
    //
    // Simplex noise is a smooth, aperiodic, deterministic function.
    // Given a continuous input t, it returns a smooth value in −1..1.
    // Unlike Perlin noise, it has no visible grid artefacts and is faster
    // to compute. For 1D use (time-varying noise), it's the natural choice.
    //
    // Two implementations provided:
    //   Simplex1()  — standard, good general purpose
    //   Simplex1Fast() — lower quality, cheaper, suitable for low-priority noise

    public static double Simplex1(double t)
    {
        // Standard 1D simplex noise
        int   i  = (int)Math.Floor(t);
        double f = t - i;
        double u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0); // smoothstep

        return Lerp(Grad(Hash(i), f), Grad(Hash(i + 1), f - 1.0), u);
    }

    public static double Simplex1Fast(double t)
    {
        // Cheaper version — cubic smoothstep only, one octave
        int    i = (int)Math.Floor(t);
        double f = t - i;
        double u = f * f * (3.0 - 2.0 * f);
        return Lerp(Grad(Hash(i), f), Grad(Hash(i + 1), f - 1.0), u);
    }

    // ── Noise types ───────────────────────────────────────────────────────────

    // White noise — fast, uncorrelated jitter
    // Frequency controls how quickly it changes
    public static double White(double seed, double frequency = 500.0)
        => Simplex1(seed + GameClock.SimTime * frequency);

    // Pink (1/f) noise — slow drift with texture
    // Sum of octaves at decreasing amplitude — natural-feeling wander
    public static double Pink(double seed)
        => Simplex1(seed + GameClock.SimTime * 0.05) * 0.50   // very slow drift
         + Simplex1(seed + GameClock.SimTime * 0.20) * 0.25   // medium
         + Simplex1(seed + GameClock.SimTime * 0.80) * 0.125  // faster
         + Simplex1(seed + GameClock.SimTime * 3.00) * 0.063; // texture

    // Periodic — deterministic sine wave tied to a physical period
    // Use for neutron star precession, binary orbital period, etc.
    // period in seconds, phase in radians
    public static double Periodic(double period, double phase = 0.0)
        => Math.Sin((GameClock.SimTime / period) * Math.Tau + phase);

    // Spike — occasional sharp transient glitch
    // Returns near 0.0 most of the time, rare sharp positive burst
    // frequency: roughly how many spikes per second (fractional OK, e.g. 0.02)
    // sharpness: higher = narrower spike (try 8.0–20.0)
    public static double Spike(double seed, double frequency = 0.05, double sharpness = 12.0)
    {
        double t = (Simplex1(seed + GameClock.SimTime * frequency) + 1.0) * 0.5; // 0..1
        return t > 0.92 ? Math.Pow((t - 0.92) / 0.08, sharpness) : 0.0;
    }

    // ── Scaling helpers ───────────────────────────────────────────────────────

    // Scale noise to sensor range — apply after combining noise sources
    // noiseFraction: how much of sensor max range the noise can span (e.g. 0.05 = 5%)
    public static double Scale(double noise, double sensorMax, double noiseFraction)
        => noise * sensorMax * noiseFraction;

    // Distance falloff — linear then sharp drop
    // Use to scale external noise sources by proximity
    // Returns 1.0 at distance=0, 0.0 at distance >= maxRange
    public static double DistanceFalloff(double distance, double maxRange)
        => Math.Max(0.0, 1.0 - (distance / maxRange));

    // ── Internals ─────────────────────────────────────────────────────────────

    private static double Lerp(double a, double b, double t)
        => a + t * (b - a);

    private static double Grad(int hash, double x)
        => (hash & 1) == 0 ? x : -x;

    private static int Hash(int i)
    {
        // Fast integer hash — decorrelates octaves and seeds
        i = ((i >> 16) ^ i) * 0x45d9f3b;
        i = ((i >> 16) ^ i) * 0x45d9f3b;
        i = (i >> 16) ^ i;
        return i & 0xFF;
    }
}
```

---

## Sensors

Lives in `Inferior.Gameplay/Sensors/`. Called from the sim thread in `Publish()`.

### `PassiveSensor`

Reusable base class. Reads a raw physical value, layers noise, publishes to
`DataBus.Instruments`. The caller supplies the raw value; the sensor handles noise
and publishing.

```csharp
// Inferior.Gameplay.Sensors namespace
public sealed class PassiveSensor
{
    public required string TopicPrefix { get; init; }  // e.g. "GravitySensor"
    public required string ValueName   { get; init; }  // e.g. "Strength"
    public double MaxValue   { get; init; } = 1.0;
    public double Seed       { get; init; } = 0.0;     // decorrelates noise from other sensors
    public double NoiseWhite { get; init; } = 0.0;     // fraction of MaxValue, e.g. 0.005
    public double NoisePink  { get; init; } = 0.0;

    // Optional environment-driven noise (EM interference, stellar activity, etc.)
    public List<Func<double>> ExternalNoiseSources { get; } = [];

    public void Publish(double rawValue)
    {
        double noise = Noise.Scale(Noise.White(Seed),      MaxValue, NoiseWhite)
                     + Noise.Scale(Noise.Pink(Seed + 1e4), MaxValue, NoisePink);
        foreach (var source in ExternalNoiseSources)
            noise += source();
        DataBus.Instruments.Publish($"{TopicPrefix}.{ValueName}", rawValue + noise);
    }
}
```

### `GravitySensor`

Concrete sensor for gravitational field strength. Reads `Environment.GravitationalStrength`
(m/s²) and publishes to `"GravitySensor.Strength"`. Call `Tick()` once per sim tick
from `Simulation.Publish()`.

```csharp
public sealed class GravitySensor
{
    private readonly PassiveSensor _sensor = new()
    {
        TopicPrefix = "GravitySensor",
        ValueName   = Topics.GravitySensor.Strength,
        MaxValue    = 100.0,  // m/s² — covers asteroids to neutron stars
        Seed        = (double)HashCode.Combine("GravitySensor"),
        NoiseWhite  = 0.005,
        NoisePink   = 0.010,
    };

    public PassiveSensor Sensor => _sensor;  // expose to attach ExternalNoiseSources

    public void Tick()
        => _sensor.Publish(SensorData.Environment.GravitationalStrength);
}
```

### Usage example — attaching neutron star interference

```csharp
double precessionPeriod = Environment.NearestStar.RotationPeriod * 1000.0;
gravitySensor.Sensor.ExternalNoiseSources.Add(() =>
    Noise.Periodic(precessionPeriod, phase: 0.3)
    * Noise.Scale(1.0, 100.0, 0.15)   // up to 15% of max
    * Noise.DistanceFalloff(Environment.DistanceToNearestStar, dangerRadius)
);
```

```
Inferior/
    Inferior.Core/          — DVec3, Units, DataBus, GameClock, Noise, PlayerInput
    Inferior.Galaxy/        — star/system generation, OrbitalBody, StarPhysics
    Inferior.Gameplay/      — Simulation, Physics/, SensorData/, Sensors/
    Inferior.Rendering/     — Camera3D, MeshFactory
    Inferior.UI/            — UIManager, UIRenderer, Theme, all controls
    Inferior.Game/          — entry point, game states, SpaceSimulation
```

Dependency graph:
```
Core ← Galaxy ← Gameplay ← Rendering
Core ←─────────────────── UI
Core ← Galaxy ← Gameplay ← Game  (references everything)
```

---

## Changelog

| Date | Change |
|------|--------|
| 2026-05-27 | Initial design session — flight, power, heat, hull, commander, DataBus, UI |
| 2026-05-31 | Compiled into this document from chat history |
| 2026-06-01 | DataBus major update: threading model, Bus<T> with ConcurrentQueue, RangeValue, RadarContact, RadarLost, simulation thread pattern. Deferred: Comms, Sensors, NavData. |
| 2026-06-01 | Added Simulation loop section: Simulation class, tick order, PlayerInput snapshot, TickPhysics/Power/Damage/Radar/Publish stubs, startup publications pattern. |
| 2026-06-02 | Added GameClock, Environment query class, Noise static class with 1D simplex implementations, usage example. |
| 2026-06-02 | Fixed DVec3 consistency in Environment (was Vector3), added SimTime atomicity note per Code review. |
| 2026-06-04 | Major sync: Simulation moved to Inferior.Gameplay; Tick order updated (GameClock + UpdateEnvironment); WorldSnapshot pattern documented; Environment updated (SimWorld, GravityCalculations, UpdateFromSimThread, private setters); mass lock superseded by hyperspace interference; GameClock.Advance now public; Sensors section added (PassiveSensor, GravitySensor); UI controls list updated (InstrumentMeter, SystemConsole, DirectionBall); project structure updated. |
