# Question for Claude Code on whether gc load can be optimized

Let's check both `Update` (per-frame main-thread) and `Draw` for allocation patterns.

I did a full investigation — no changes made, as requested. Here's what I found, roughly in order of expected impact.

## 1. `Bus<T>.Drain()` — the dominant, systemic source

`Inferior.Core/DataBus/Bus.cs:81-99`. Every non-empty channel's `Drain()` allocates a fresh `List<(string Topic, T Value)>` **and** a fresh `Dictionary<string,int>` on every call — not amortized, not pooled, every time. `Dispatch()` (line 196) also does `handlers.ToArray()` **per message**, not per drain, to guard against re-entrant unsubscribe.

`DataBus.Drain()` runs all 11 channels from `InferiorGame.Update()` ([InferiorGame.cs:100](Inferior.Game/InferiorGame.cs:100)) — once per render frame, unconditionally. `ScalarTelemetry` alone gets 25+ publishes per sim tick (heartbeat, gravity, atmo pressure, solar spectrum, thermal signature, ~14 flight-mode topics, ~8 landing-support topics — see [SpaceSimulation.cs:2398](Inferior.Game/SpaceSimulation.cs:2398)), so it's never empty during normal play. This is core messaging infrastructure, touched by everything, running every frame — the highest-confidence finding here since it's structural, not an isolated call site.

## 2. `ShipPropulsion.Resolve()` — heaviest single call

`Inferior.Gameplay/Ship/ShipPropulsion.cs:106-183`, called unconditionally every sim tick from `TickNewtonianPhysics` ([SpaceSimulation.cs:1079](Inferior.Game/SpaceSimulation.cs:1079)) — Newtonian is the default in-system flight mode, so this runs continuously during ordinary play, not just on rare events. Each call allocates a `List<ResolvedEnginePropulsion>`, one heap-allocated record *per installed engine* (`ResolvedEnginePropulsion` is a `record` — a reference type — while its four siblings in the same file are deliberately `readonly record struct`, which reads like an oversight rather than a choice), a `.ToArray()` copy, and an `Array.AsReadOnly()` wrapper. Roughly `3 + N` allocations at 60 Hz for the whole time a ship is flying.

## 3. Per-tick topic-string interpolation across nearly every sensor

`PassiveSensor.Publish()` ([PassiveSensor.cs:114](Inferior.Gameplay/Sensors/PassiveSensor.cs:114)) rebuilds `$"{TopicPrefix}.{ValueName}"` on **every publish call**, even though both parts are `required string { get; init; }` — fixed for the sensor's entire lifetime. This is the shared base every passive sensor uses (gravity, atmo pressure, solar spectrum, magnetic field, external temp/pressure, radiation…), plus the same pattern independently repeated in `LandingSupportSystem` (8 sites), `ShieldComponent`, `SolarSpectrumSensor`, `MagneticFieldSensor`. A trivially cacheable value, recomputed 60×/sec per sensor instance.

## 4. Per-frame HUD text formatting (lower severity, wide surface)

`DriveInstrumentPanel.cs:135-157` builds ~6 formatted strings every `Draw()` call regardless of whether the underlying values changed since last frame. Small allocations individually, but the same shape is likely repeated across the other instrument panels (`RadarDisplay`, `LandingRadarPanel`, `DockingInstrument`, `SpectrumGraph`) — I didn't exhaustively check every one. Lowest priority of the four; fixing it properly means introducing a "cache last string, only reformat on change" convention that doesn't exist anywhere in the UI layer today, so it's a bigger shape of change than the others.

## Orthogonal, zero-code-risk lever

No project explicitly configures a GC mode — `Inferior.Game.csproj` sets `TieredCompilation=false` but nothing GC-related, so it runs on .NET's default **Workstation + Concurrent GC** for a desktop app. Given the sim thread and render thread both allocate continuously, Server GC (multiple heaps, parallel collection) is worth a controlled A/B profile — it trades memory footprint for throughput and isn't a clear win without measuring, but it's a one-line `<ServerGarbageCollection>` toggle with no code changes, so it's cheap to try.

## Caveat

This is static-reading-based reasoning about allocation *sites* and call *frequency*, not a profiler trace — I don't have one attached to this session. It tells you *where* the likely pressure is, not confirmed frame-time impact. If you want to act on any of this, #1 and #2 are the ones I'd actually expect to move a profiler's needle; #3 is free and easy regardless; #4 I'd hold off on without first confirming #1/#2 matter.
