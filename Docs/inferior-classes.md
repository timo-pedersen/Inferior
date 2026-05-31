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
    public static readonly Bus<double>      Instruments = new();
    public static readonly Bus<RadarFrame>  Radar       = new();
    public static readonly Bus<CommMessage> Comms       = new();
    public static readonly Bus<ScanResult>  Sensors     = new();
    public static readonly Bus<NavData>     Navigation  = new();
    // Add buses as needed — cost is near zero
}
```

### `Bus<T>` (~20 lines)

```csharp
public sealed class Bus<T>
{
    public void Subscribe(string topic, Action<T> handler)   { ... }
    public void Unsubscribe(string topic, Action<T> handler) { ... }
    public void Publish(string topic, T value)               { ... }
}
```

### `CommMessage` (record)

```csharp
public record CommMessage(
    string   SenderId,   // station ID, ship ID, or "SYSTEM"
    string   Channel,    // "radio.local", "hyperspace.band1"
    string   Text,
    double   GameTime,   // when sent — for hyperspace delay simulation
    CommType Type);

public enum CommType { Hail, Response, Broadcast, Emergency, Automated }
```

### `RadarFrame`

```csharp
public class RadarFrame
{
    public List<RadarContact> Contacts { get; }
    // Each contact: Position, Velocity, ContactType, IFF
}
```

---

## Physics

### Mass lock check

```csharp
bool MassLocked(Ship ship, List<GameObject> nearbyObjects)
{
    foreach (var obj in nearbyObjects)
    {
        double distance      = (obj.Position - ship.Position).Length();
        double massLockRadius = Math.Sqrt(obj.Mass) * massLockConstant;
        if (distance < massLockRadius) return true;
    }
    return false;
}
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

Controls: `Button`, `Label`, `TextBox`, `Panel`, `Window` (draggable container).

---

## Project structure (planned)

```
Inferior/
    Inferior.Core/          — shared types, DVec3, units, DataBus
    Inferior.Galaxy/        — generation, star systems, orbital mechanics
    Inferior.Rendering/     — MonoGame rendering, camera, mesh
    Inferior.Gameplay/      — physics, power sim, damage, flight model
    Inferior.UI/            — IUIRenderer, controls
    Inferior.Game/          — entry point, state machine, game loop
```

---

## Changelog

| Date | Change |
|------|--------|
| 2026-05-27 | Initial design session — flight, power, heat, hull, commander, DataBus, UI |
| 2026-05-31 | Compiled into this document from chat history |
