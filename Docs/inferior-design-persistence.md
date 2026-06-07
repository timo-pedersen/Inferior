# Inferior — Persistence Design

> Design decisions from session 2026-06-07.
> Covers serialization, ship records, repositories, captain's log, and related architecture.

---

## Guiding principles

- JSON now, binary later. The abstraction makes the format an implementation detail.
- Whole objects saved and loaded — no generic key-value stores.
- No ambient defaults anywhere. A record is always complete.
- `ShipRecord` is a DTO. It appears at the boundary, does its job, disappears.
- The live `Ship` is the truth at runtime. `ShipRecord` is a snapshot on demand.

---

## File structure

```
saves/
  careers/
    {careerId}/
      career.json
      commanders/
        {commanderId}.json
      ships/
        {shipId}/
          ship.json
          log-0001.ndjson     ← sealed page
          log-0002.ndjson     ← sealed page
          log-0003.ndjson     ← current, open for appending
```

Files live under the solution root for now. Not AppData. The repository abstraction
makes the location trivial to change later.

One career folder = one complete playthrough. Even if only one career is ever
supported, the folder layer is worth having from the start.

---

## Assembly: `Inferior.Persistence`

A dedicated assembly. Sits just below `Inferior.Game` in the dependency graph:

```
Core ← Galaxy ← Gameplay ← Persistence   (owns ShipRecord, all repositories)
                          ← Game          (ShipBuilder, factories, all mapping)
                 Game → Persistence       (for repos and record types)
```

Three clean layers:

- `Gameplay` — Ship, simulation. No persistence knowledge at all.
- `Persistence` — ShipRecord, repositories, log. No live object knowledge at all.
- `Game` — knows both. All mapping between live objects and records lives here.

`Persistence` is a pure data and IO layer. Nothing in it ever imports a `Ship`.
`System.Text.Json` is contained within `Persistence`. Nothing below it touches
serialization concerns.

---

## `ShipRecord` — the DTO

Dumb data. No logic. Produced on demand, saved, then discarded.

```csharp
public record ShipRecord
{
    public int      SchemaVersion { get; init; } = ShipRecord.CurrentVersion;
    public string   Id            { get; init; }
    public string   HullTypeId    { get; init; }
    public string?  Name          { get; init; }
    public DateTime CreatedDate   { get; init; }

    // --- Configuration (save on change) ---
    public InstalledComponentRecord[] Components  { get; init; }
    public CockpitLayoutRecord        PanelLayout { get; init; }

    // --- State (save on dock) ---
    public HullElementStateRecord[]   HullElements { get; init; }
    public ConsumablesRecord          Consumables  { get; init; }

    // Captain's log is NOT here — owned by IShipLogRepository
}
```

`InstalledComponentRecord` carries: `SlotId`, `TypeId`, `DamageLevel`, `PowerBusId`,
`PowerPriority`, and `Dictionary<string, JsonElement> Settings` for type-specific tuning.
The persistence layer is oblivious to what any specific component type puts in `Settings`.

Same pattern for instruments: `InstrumentRecord` has `TypeId`, `Topic`, `Bounds`, and
`Dictionary<string, JsonElement> Config`. `InstrumentMeter` knows its own keys;
`SystemConsole` knows its own. The record carries them without understanding them.

No world position in the ship record. Position is live universe state. A docked ship's
position is "docked at station X" — a reference, not a coordinate.

---

## Schema versioning

Added from day one. Never skipped.

```json
{ "$schema": 1, "id": "...", ... }
```

```csharp
public static class ShipRecordMigrator
{
    public static ShipRecord EnsureCurrent(ShipRecord record)
        => record.SchemaVersion switch
        {
            ShipRecord.CurrentVersion => record,
            1 => MigrateV1(record),
            _ => throw new NotSupportedException(
                     $"Unknown schema version {record.SchemaVersion}")
        };
}
```

---

## `ToRecord()` — extension method in Game

`Ship` has no knowledge that it can be serialized. `Persistence` has no knowledge
of live game objects. All mapping lives in `Game`, in `ShipExtensions.cs` alongside
the builder.

```csharp
// Inferior.Game/ShipBuilder/ShipExtensions.cs
public static class ShipExtensions
{
    public static ShipRecord ToRecord(this Ship ship) => new()
    {
        SchemaVersion = ShipRecord.CurrentVersion,
        Id            = ship.Id,
        HullTypeId    = ship.HullTypeId,
        Components    = ship.Components.Select(c => c.ToRecord()).ToArray(),
        HullElements  = ship.HullElements.Select(e => e.ToRecord()).ToArray(),
        PanelLayout   = ship.Cockpit.ToRecord(),
        Consumables   = ship.Consumables.ToRecord(),
    };
}
```

Each subsystem owns its own `ToRecord()`. `ShipExtensions` assembles them.
The seam is sharp: `Persistence` is pure IO, `Game` owns all translation.

---

## Repository abstraction

Typed per domain object. Not a generic key-value store.

```csharp
public interface IShipRepository
{
    Task<ShipRecord?> GetAsync(string shipId);
    Task SaveAsync(ShipRecord record);
    Task DeleteAsync(string shipId);
    Task<IReadOnlyList<ShipSummary>> ListAsync();  // lightweight, for UI lists
}
```

`LocalFileShipRepository` is the current implementation — saves to
`{careerId}/ships/{shipId}/ship.json`. The file format (JSON, binary, database)
is entirely an implementation detail. Callers never see it.

`Commander` gets its own `ICommanderRepository` when the time comes. The commander
record holds a `ShipId` string reference — not an embedded ship, since ships exist
independently in the universe.

---

## `ShipPersistenceService` — the thin coordinator

The only other place `ShipRecord` is touched outside `Persistence`. Lives in `Game`.
Keeps `ShipRecord` from leaking into the rest of the game.

```csharp
// Inferior.Game
public class ShipPersistenceService
{
    private readonly IShipRepository _repo;

    public async Task SaveAsync(Ship ship)
    {
        var record = ship.ToRecord();    // born here
        await _repo.SaveAsync(record);  // dies here
    }

    public async Task<Ship> LoadAsync(string shipId)
    {
        var record = await _repo.GetAsync(shipId);        // born here
        record = ShipRecordMigrator.EnsureCurrent(record);
        return ShipBuilder.From(record).Build();          // dies here
    }
}
```

Callers work with `Ship` in, `Ship` out. `ShipRecord` never crosses a method boundary
into the rest of the game.

---

## `ShipRecord` containment rule

`ShipRecord` must not appear in any type outside `ShipBuilder` and `ShipPersistenceService`.
`Persistence` itself may reference `ShipRecord` freely — repositories, migrators, etc.
What must not happen is `ShipRecord` appearing as a parameter or return type anywhere
else in `Inferior.Game`.

Architecture test — plain reflection, no dependencies:

```csharp
[Fact]
public void ShipRecord_ShouldNotLeakOutsidePermittedTypes()
{
    var permitted = new[] { "ShipBuilder", "ShipPersistenceService" };

    var violations = typeof(Game).Assembly
        .GetTypes()
        .Where(t => !permitted.Any(p => t.Name.Contains(p)))
        .Where(t => t.GetMembers()
            .Any(m => m.ToString()!.Contains("ShipRecord")))
        .Select(t => t.FullName)
        .ToList();

    Assert.Empty(violations);
}
```

---

## `ShipBuilder` — lives in `Inferior.Game`

Maps `ShipRecord` → `Ship`. Knows about both. The only constructor path for `Ship`.

```csharp
// Inferior.Game/ShipBuilder/ShipBuilder.cs

public class ShipBuilder
{
    // Seed from an existing record (load or insurance replacement)
    public static ShipBuilder From(ShipRecord record) { ... }

    // Fluent configuration
    public ShipBuilder WithComponent(string slotId, string typeId, PowerPriority priority) { ... }
    public ShipBuilder WithPanelLayout(CockpitLayoutRecord layout) { ... }
    public ShipBuilder WithConsumables(int fuelRods, double coolant) { ... }
    public ShipBuilder WithNewId(string shipId) { ... }
    public ShipBuilder WithResetHullIntegrity() { ... }
    public ShipBuilder WithDegradedComponents(double maxDamage) { ... }
    public ShipBuilder WithDefaultConsumables() { ... }
    public ShipBuilder WithEmptyLog() { ... }

    // Always succeeds — a damaged or incomplete ship is still a valid Ship
    public Ship Build() { ... }
}
```

`Build()` always succeeds. A derelict with missing components is a valid ship object —
it just cannot fly. Flyability is queried separately:

```csharp
public bool CanFly { get; }
public IReadOnlyList<string> FlyabilityIssues { get; }
// "No engine installed"
// "Generator missing"
```

The game queries `CanFly` before allowing undock. The fitting screen shows
`FlyabilityIssues`. The builder just builds.

---

## Factories — lives in `Inferior.Game`

The single authoritative definition of what a new ship looks like. No ambient defaults
anywhere. If you have a `ShipRecord`, it is complete.

```csharp
public interface IShipFactory
{
    ShipRecord Create(string shipId);
}

public class SidewinderFactory : IShipFactory
{
    public ShipRecord Create(string shipId)
        => SidewinderBuilder(shipId).Build().ToRecord();

    // Exposed so shipyard variants can inherit and override
    public static ShipBuilder SidewinderBuilder(string shipId)
        => new ShipBuilder(HullTypes.Sidewinder, shipId)
            .WithComponent("engine",    "drive-class1-standard",  PowerPriority.High)
            .WithComponent("generator", "generator-xp-class1",    PowerPriority.Critical)
            .WithPanelLayout(SidewinderDefaultLayout.Build())
            .WithConsumables(fuelRods: 20, coolant: 1.0);
}
```

Shipyard variants are free:

```csharp
public class SidewinderMilitaryFactory : IShipFactory
{
    public ShipRecord Create(string shipId)
        => SidewinderFactory.SidewinderBuilder(shipId)
            .WithComponent("engine", "drive-class1-military", PowerPriority.High)
            .Build()
            .ToRecord();
}
```

Same hull, different starting loadout. No copy-paste between variants.

`shipId` is passed in rather than generated inside the factory — ID generation stays at
the call site where uniqueness can be validated against the repository.

---

## Insurance replacement

A factory concern. Not scattered in the game state machine.

```csharp
public static ShipRecord CreateInsuranceReplacement(ShipRecord lost)
    => ShipBuilder.From(lost)
        .WithNewId(Guid.NewGuid().ToString())
        .WithResetHullIntegrity()          // same grade, condition reset to 1.0
        .WithDegradedComponents(0.20)      // equipment preserved, max 20% damage
        .WithDefaultConsumables()          // fresh fuel, coolant
        .WithEmptyLog()                    // log is gone — recover from wreck
        .Build()
        .ToRecord();

// PanelLayout is preserved exactly — follows the ship
```

---

## Captain's log — `IShipLogRepository`

The log is fundamentally different from ship configuration — append-only, grows
unbounded, almost never needs to be read in full. It does not live in `ShipRecord`.

Stored as NDJSON pages — one entry per line. Append = write one line. No parsing,
no rewriting, no loading existing content.

```
ships/{shipId}/
  log-0001.ndjson   ← sealed
  log-0002.ndjson   ← current
```

When the current page reaches 32 KB, seal it and open the next.

```csharp
public interface IShipLogRepository
{
    Task AppendAsync(string shipId, LogEntryRecord entry);
    Task<IReadOnlyList<LogEntryRecord>> GetRecentAsync(string shipId, int count = 50);
    Task<IReadOnlyList<LogEntryRecord>> GetAllAsync(string shipId);
    Task DeleteAllAsync(string shipId);
    Task ValidateLog();
}
```

Recent entries = read last page only, backwards. Full history = all pages in order.
The common case is fast.

Each log entry contains a hash. It is calculated from hash of log text plus hash from previous entry. 
In no such entry exists, a hash or id of the commander / captain is used. 
This ties log to commander, and offers some tamper protection.

ValidateLog() validates a log by checking that all hashes add up.
Used when importing a log from ship, perhaps at game startup.

The wreck recovery mechanic falls out naturally: log files are keyed by `shipId` and
outlive the ship record. The 61-hour salvage window is a timestamp in the career record.
`GetAllAsync(lostShipId)` recovers the log if the player returns in time.

---

## Save frequency

Three distinct change patterns:

| Data | Changes when | Save when |
|------|-------------|-----------|
| Configuration — components, wiring, cockpit layout | Player takes deliberate action | Immediately, on the action |
| State — damage, hull integrity | During gameplay | On dock |
| Consumables — fuel, coolant, ammo, rods | Constantly in flight | On dock |
| Captain's log | On notable events | Immediately, append only |

Consumables are accepted as best-effort. A crash between docks loses what was burned
since last docking — at most one leg of travel. Players refuel at stations anyway.

---

## Performance

`ToRecord()` — pure allocation, no logic. Completes in microseconds. Not a concern.

JSON serialization — low single-digit milliseconds for a full ship. Not a concern.

File I/O — the only real cost, and it is already handled. `SaveAsync()` uses async
file writes. The game loop never blocks.

Threading: `ToRecord()` reads live sim state. If called from the main thread while
the sim thread is mid-tick, a snapshot may be slightly inconsistent (one tick stale).
Not a crash, not noticeable in practice. If it ever matters: call `ToRecord()` inside
the sim thread and hand the resulting record to the main thread for serialization.

---

## Folder layout in `Inferior.Game`

```
Inferior.Game/
  ShipBuilder/
    ShipBuilder.cs              ← ShipRecord → Ship
    ShipExtensions.cs           ← Ship → ShipRecord  (ToRecord)
    ShipPersistenceService.cs   ← orchestrates both
    Factories/
      IShipFactory.cs
      SidewinderFactory.cs
      ...:
```

---

## What is stubbed

- `CommanderRecord` and `ICommanderRepository`
- `CareerRecord` and `ICareerRepository`
- Galaxy state persistence (`visited systems`, discoveries)
- Binary serialization path (repository swap, no other changes required)
- Session autosave for consumables mid-flight
- Concrete `LocalFileShipRepository` implementation
- Concrete `LocalFileShipLogRepository` implementation
- `ShipRecordMigrator` (structure only, no migrations yet)

---

## Changelog

| Date | Change |
|------|--------|
| 2026-06-07 | Initial design session — serialization, records, repositories, builder, factories, log |
| 2026-06-07 | `ToRecord()` moved from `Persistence` extension method to `Game/Ships/ShipExtensions.cs`. `Persistence` is now a pure data/IO layer with no live object knowledge. |
| 2026-06-07 | Updated log size to 32kb. Added log hash |

