using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

public static partial class StationDecorator
{
    // ── Face zoning (Brief Z1) ────────────────────────────────────────────────
    //
    // A zone is the same FaceInfo struct with a shifted centre and smaller extent, sharing
    // the face's tangent frame — so no decoration pass needs its internals changed; the
    // per-face loop in Decorate() becomes a per-zone loop with a type filter. Z1 builds
    // this mechanism and maps only EXISTING passes onto it; CommsArray/PipeCorridor are
    // reserved for Z2 (zone content) and never assigned here.
    internal enum ZoneType
    {
        Windows,      // GenerateWindows (+ portholes/cupolas) — count-driven, not weighted, see AssignZoneTypes
        Machinery,    // GenerateTanks, GenerateVentGrilles, GenerateGreebles
        TankFarm,     // GenerateTanks only — same pass as Machinery, just not competing with vents/greebles for occupancy
        ServiceCore,  // GenerateHatches, GenerateVentGrilles, GenerateGreebles
        Structural,   // blank — no raised decoration; panel seams/edge trim still apply outside the zoned loop
        Storage,      // GenerateContainers
        Signage,      // reserved — claims area, generates nothing until Z2
        CommsArray,   // Z2 — reserved, unpopulated in Z1
        PipeCorridor, // Z2 — reserved, unpopulated in Z1
    }

    // Target cell size for subdividing a face into zones (brief: "~8-12m, Code tunes").
    // A face is left as a single whole-face zone (bypassing zoning entirely — see
    // ComputeZones' isUnzoned return) whenever BOTH its dimensions are smaller than 1.5
    // cells. This one constant sizes both the mega-face subdivision granularity and the
    // small-face cutoff, per the brief's literal design.
    //
    // Tuned to 18 (above the "~8-12m" suggestion) rather than a literal 10, after
    // measuring the gate: at 10 (threshold 15m), the base non-"_Large" catalogue —
    // hab-block (18x14), core-hub (20x20), industrial-block (22x18), cargo-bay (24x12),
    // all previously verified as "the ordinary module set" in the window-sizing brief —
    // started multi-zoning too, which would visibly change their decoration character
    // exactly like the window-sizing brief warned against ("if a small module visibly
    // changes, the constants are too tight, raise them"). 18 (threshold 27m) sits just
    // above cargo-bay's 24m worst case, so all five stay single-zone/bit-identical, while
    // the "_Large" tier (36-48m) and docking-bay (100-238m) still zone as intended —
    // matching Timo's "tanks and pipes should dominate industrial/bay" note, which reads
    // as being about the industrial/bay CATEGORY at scale (Large variants, mega bays), not
    // the base industrial-block module.
    private const float ZoneTargetCellSize     = 18f;
    private const float ZoneSingleZoneThreshold = 1.5f * ZoneTargetCellSize;

    // Windows are assigned by COUNT, not by the weight table (see AssignZoneTypes) — a
    // per-zone probability breaks at scale (a 40-zone bay wall at category probability 0.6
    // would roll ~24 window zones, the opposite of the intent). 0-3 per face, capped 5 per
    // module (both Timo's spec).
    private const int MaxWindowZonesPerFace   = 3;
    private const int MaxWindowZonesPerModule = 5;

    // "ZONE" salt — independent from Decorate()'s sequential baseRng.Next() chain by
    // construction (XORed into mod.Seed directly, never drawn from baseRng), so inserting
    // zoning does not shift any existing per-pass RNG stream. See the brief's determinism
    // requirement: reusing baseRng.Next() here would re-roll every station in the galaxy.
    private const int ZoneRngSalt = 0x5A4F4E45;

    // Per-module-category zone-type weights — mirrors DecorCastingPolicy's one-named-table
    // pattern. Windows is deliberately absent (assigned by count, not weight — see above).
    // Per Timo: tanks and pipes should dominate industrial/bay categories (Machinery +
    // TankFarm weighted heavily there); Structural should be common enough to visibly break
    // up large faces everywhere.
    //
    // Brief Z2: Signage removed from docking-bay's entry (it was Z1's only weight-table
    // reference to Signage). Part 4 frames Signage as "one reserved blank area per
    // multi-zone module" — singular, deliberate — so like Windows it's now guaranteed-set-
    // only, never weight-selectable; leaving it here let the weight pass roll additional
    // Signage zones on top of the guaranteed one, breaking "exactly 1 per module." The
    // freed 0.05 moved to Storage.
    private static readonly IReadOnlyDictionary<string, (ZoneType Type, float Weight)[]> ZoneTypeWeights =
        new Dictionary<string, (ZoneType, float)[]>
        {
            ["industrial"]  = [(ZoneType.Machinery, 0.30f), (ZoneType.TankFarm, 0.30f), (ZoneType.Structural, 0.20f), (ZoneType.ServiceCore, 0.15f), (ZoneType.Storage, 0.05f)],
            ["docking-bay"] = [(ZoneType.Machinery, 0.25f), (ZoneType.TankFarm, 0.25f), (ZoneType.Structural, 0.20f), (ZoneType.ServiceCore, 0.15f), (ZoneType.Storage, 0.15f)],
            ["cargo"]       = [(ZoneType.Storage, 0.35f), (ZoneType.Structural, 0.20f), (ZoneType.Machinery, 0.20f), (ZoneType.ServiceCore, 0.15f), (ZoneType.TankFarm, 0.10f)],
            ["hab"]         = [(ZoneType.ServiceCore, 0.35f), (ZoneType.Structural, 0.30f), (ZoneType.Machinery, 0.20f), (ZoneType.Storage, 0.15f)],
            ["science"]     = [(ZoneType.ServiceCore, 0.35f), (ZoneType.Structural, 0.30f), (ZoneType.Machinery, 0.20f), (ZoneType.Storage, 0.15f)],
            ["core"]        = [(ZoneType.ServiceCore, 0.35f), (ZoneType.Structural, 0.35f), (ZoneType.Machinery, 0.20f), (ZoneType.Storage, 0.10f)],
            ["connector"]   = [(ZoneType.Structural, 0.50f), (ZoneType.ServiceCore, 0.30f), (ZoneType.Machinery, 0.20f)],
            ["_default"]    = [(ZoneType.Structural, 0.35f), (ZoneType.ServiceCore, 0.30f), (ZoneType.Machinery, 0.20f), (ZoneType.Storage, 0.15f)],
        };

    // Splits a face into zones. isUnzoned=true means the face is below the single-zone
    // threshold (or the grid rounds to a single 1x1 cell) — the caller must NOT run this
    // through the zone-type dispatch, but through the exact pre-Z1 per-face body, so the
    // "small modules bit-identical" gate has a real, checkable meaning: isUnzoned is a
    // structural signal, not just "zones.Length happened to come out to 1" (the grid/merge
    // algorithm below can also legitimately produce a single zone by a random full-face
    // merge on a face that WAS eligible for zoning — that case must still go through
    // zone-type assignment, not the bit-identical bypass).
    // internal, not private: StationWindowGridTests-style pure-helper testing (no
    // GraphicsDevice/mesh needed) — verifies zone counts/threshold behaviour directly.
    internal static (FaceInfo[] zones, bool isUnzoned) ComputeZones(FaceInfo face, System.Random zoneRng)
    {
        if (face.Width <= ZoneSingleZoneThreshold && face.Height <= ZoneSingleZoneThreshold)
            return ([face], true);

        int cellsU = Math.Max(1, (int)MathF.Round(face.Width  / ZoneTargetCellSize));
        int cellsV = Math.Max(1, (int)MathF.Round(face.Height / ZoneTargetCellSize));
        if (cellsU <= 1 && cellsV <= 1)
            return ([face], true);

        // Cells fit the face exactly (same discipline as the window grid — derive cell
        // count from face size, then divide evenly, no leftover strip).
        float cellW = face.Width  / cellsU;
        float cellH = face.Height / cellsV;

        // Greedy rectangular partition, scanned row-major: each unclaimed cell becomes the
        // top-left corner of a new zone whose width/height (in cells) is rolled from the
        // zone rng, bounded by how far the unclaimed area extends right/down. This always
        // fully covers the grid with non-overlapping rectangles (no L-shapes, by
        // construction) and naturally produces bands (wide, short runs) and risers
        // (narrow, tall runs) depending on what the rolls happen to pick.
        var claimed = new bool[cellsU, cellsV];
        var zones   = new List<FaceInfo>();

        for (int v = 0; v < cellsV; v++)
        for (int u = 0; u < cellsU; u++)
        {
            if (claimed[u, v]) continue;

            int maxW = 1;
            while (u + maxW < cellsU && !claimed[u + maxW, v]) maxW++;
            int wCells = 1 + zoneRng.Next(maxW);

            int maxH = 1;
            while (true)
            {
                int nextV = v + maxH;
                if (nextV >= cellsV) break;
                bool rowClear = true;
                for (int du = 0; du < wCells; du++)
                    if (claimed[u + du, nextV]) { rowClear = false; break; }
                if (!rowClear) break;
                maxH++;
            }
            int hCells = 1 + zoneRng.Next(maxH);

            for (int dv = 0; dv < hCells; dv++)
            for (int du = 0; du < wCells; du++)
                claimed[u + du, v + dv] = true;

            float zoneWidth  = wCells * cellW;
            float zoneHeight = hCells * cellH;
            float offsetU = -face.Width  * 0.5f + (u + wCells * 0.5f) * cellW;
            float offsetV = -face.Height * 0.5f + (v + hCells * 0.5f) * cellH;

            zones.Add(new FaceInfo(
                face.LocalNormal,
                face.LocalCenter + face.LocalRight * offsetU + face.LocalUp * offsetV,
                face.LocalRight, face.LocalUp,
                zoneWidth, zoneHeight,
                face.IsExposed));
        }

        return ([.. zones], false);
    }

    // Brief Z2: per-module state threaded across every multi-zone face's AssignZoneTypes
    // call for one module — bundled into one mutable object rather than five separate ref
    // parameters. Only ever touched from Decorate()'s multi-zone (else) branch, so a module
    // whose faces are all single-zone never has this object's fields consumed at all — the
    // "guaranteed set applies only to modules with a multi-zone face" rule (Brief Z2's
    // CRITICAL note) holds by construction, not by a category/module-kind check.
    // internal, not private: constructed directly by StationZoningTests.
    internal sealed class ModuleZoneBudget
    {
        public int  WindowZonesRemaining = MaxWindowZonesPerModule;
        // Lazily rolled (1-4) on first real use inside AssignZoneTypes, not here — a
        // module that never reaches a multi-zone face must never consume zoneRng for this,
        // matching the brief's determinism requirement exactly as strictly as windows'
        // existing budget already does for rng-free modules.
        public int? TankFarmRemaining;
        public bool NeedsPipeCorridor = true;
        public bool NeedsCommsArray   = true;
        public bool NeedsSignage      = true;
    }

    // Brief D-Z2: pure observability record — retains exactly what Decorate() actually
    // produced for one zone, recorded at the moment of production rather than recomputed
    // later, so PlacedModule.DebugZones is provably "what the production assignment did,"
    // not a re-simulation. Never read by any decision-making code; consumed only by the
    // zone-type debug overlay and the in-game per-zone content dump.
    internal sealed record ZoneDebugRecord(
        int FaceIndex, int ZoneIndexOnFace,
        FaceInfo Zone, ZoneType Type,
        int RegionsClaimed, int FacesAdded);

    // Picks a uniformly random still-unclaimed zone index, or -1 if none remain. Marking
    // claimed[] as zones are picked makes repeated calls behave like sampling without
    // replacement — equivalent to a Fisher-Yates shuffle, just rebuilt each call (zone
    // counts here are at most a few dozen, so this is not a hot loop).
    private static int PickUnclaimed(bool[] claimed, System.Random zoneRng)
    {
        int count = 0;
        for (int i = 0; i < claimed.Length; i++) if (!claimed[i]) count++;
        if (count == 0) return -1;

        int pick = zoneRng.Next(count);
        for (int i = 0; i < claimed.Length; i++)
        {
            if (claimed[i]) continue;
            if (pick == 0) return i;
            pick--;
        }
        return -1; // unreachable
    }

    // Brief Z2 Part 3: "long and thin" shape preference for PipeCorridor — zones are
    // already-formed rectangles by the time AssignZoneTypes runs (ComputeZones' greedy
    // merge decides shape before any type is assigned), so this doesn't reshape anything;
    // it just prefers an already-elongated zone (a "band" or "riser," in ComputeZones' own
    // words) over a squarish one when there's a choice.
    private const float PipeCorridorElongationRatio = 1.8f;

    // internal, not private: StationZoningTests' adjacency-rate check reuses this exact
    // test rather than duplicating the rectangle math.
    internal static bool IsElongatedZone(FaceInfo zone)
        => MathF.Max(zone.Width, zone.Height) / MathF.Max(0.01f, MathF.Min(zone.Width, zone.Height))
           >= PipeCorridorElongationRatio;

    // Rectangle-touching test in the shared face-local (u, v) tangent plane — every zone
    // in one AssignZoneTypes call shares the same LocalRight/LocalUp (all derived from one
    // parent face by ComputeZones), so projecting each zone's centre onto either zone's own
    // axes gives directly-comparable coordinates. "Touching" allows a small tolerance for
    // floating-point cell-boundary noise, not just an exact zero gap.
    // internal, not private: StationZoningTests' adjacency-rate check reuses this exact
    // test rather than duplicating the rectangle math.
    private const float ZoneAdjacencyTolerance = 0.5f; // metres
    internal static bool ZonesAreAdjacent(FaceInfo a, FaceInfo b)
    {
        float au = Vector3.Dot(a.LocalCenter, a.LocalRight), av = Vector3.Dot(a.LocalCenter, a.LocalUp);
        float bu = Vector3.Dot(b.LocalCenter, a.LocalRight), bv = Vector3.Dot(b.LocalCenter, a.LocalUp);

        float uGap = MathF.Abs(au - bu) - (a.Width  * 0.5f + b.Width  * 0.5f);
        float vGap = MathF.Abs(av - bv) - (a.Height * 0.5f + b.Height * 0.5f);

        bool uTouches  = uGap <= ZoneAdjacencyTolerance;
        bool vTouches  = vGap <= ZoneAdjacencyTolerance;
        bool uOverlaps = uGap < 0f;
        bool vOverlaps = vGap < 0f;

        // Two axis-aligned rectangles share an edge when one axis's gap is ~0 (touching)
        // while the other axis's ranges actually overlap (not just also touching at a
        // corner) — the classic edge-vs-corner-adjacency distinction.
        return (uTouches && vOverlaps) || (vTouches && uOverlaps);
    }

    // Brief Z2 Part 3's adjacency bias: "prefer placing pipe corridors adjacent to
    // TankFarm zones... best-effort, not a hard constraint." Preference order: adjacent AND
    // elongated (best), then just adjacent, then just elongated, then any unclaimed zone —
    // never fails to pick if any unclaimed zone remains.
    private static int PickForPipeCorridor(
        FaceInfo[] zones, bool[] claimed, List<int> tankFarmIndices, System.Random zoneRng)
    {
        bool AdjacentToAnyTankFarm(int idx)
        {
            foreach (int t in tankFarmIndices)
                if (ZonesAreAdjacent(zones[idx], zones[t])) return true;
            return false;
        }

        var candidates = new List<int>();
        void Collect(bool requireAdjacent, bool requireElongated)
        {
            candidates.Clear();
            for (int i = 0; i < zones.Length; i++)
            {
                if (claimed[i]) continue;
                if (requireAdjacent && !AdjacentToAnyTankFarm(i)) continue;
                if (requireElongated && !IsElongatedZone(zones[i])) continue;
                candidates.Add(i);
            }
        }

        Collect(requireAdjacent: true,  requireElongated: true);
        if (candidates.Count == 0) Collect(requireAdjacent: true,  requireElongated: false);
        if (candidates.Count == 0) Collect(requireAdjacent: false, requireElongated: true);
        if (candidates.Count == 0) Collect(requireAdjacent: false, requireElongated: false);
        if (candidates.Count == 0) return -1;

        return candidates[zoneRng.Next(candidates.Count)];
    }

    // Assigns a ZoneType to every zone on one face, in the Brief Z2 order:
    //   1. GUARANTEED — tank farm (1-4/module), structural (>=1/face), pipe corridor
    //      (>=1/module), comms array (>=1/module), signage (exactly 1/module), claimed in
    //      that priority so a small face degrades gracefully (takes what it can, in order,
    //      no error) instead of losing an arbitrary one. A floor, not a budget: the weight
    //      pass below can and does add more Machinery/TankFarm zones on top.
    //   2. COUNT — windows, into whatever's still unclaimed (0-3/face, capped 5/module).
    //   3. WEIGHT — PickWeightedZoneType fills every zone still unclaimed.
    // budget is threaded across every face of one module (see ModuleZoneBudget's own
    // comment) — internal, not private: StationZoningTests constructs it directly.
    internal static ZoneType[] AssignZoneTypes(
        string category, FaceInfo[] zones, System.Random zoneRng, ModuleZoneBudget budget)
    {
        var types   = new ZoneType[zones.Length];
        var claimed = new bool[zones.Length];

        // ── Step 1: GUARANTEED ──
        budget.TankFarmRemaining ??= 1 + zoneRng.Next(4); // 1-4 inclusive, per module

        var tankFarmIndices = new List<int>();
        while (budget.TankFarmRemaining > 0)
        {
            int idx = PickUnclaimed(claimed, zoneRng);
            if (idx < 0) break;
            types[idx] = ZoneType.TankFarm;
            claimed[idx] = true;
            tankFarmIndices.Add(idx);
            budget.TankFarmRemaining--;
        }

        // Structural: >=1 per face (every face reaching AssignZoneTypes is already "large"
        // by ZoneSingleZoneThreshold's own gating) — not module-threaded, so every
        // multi-zone face gets its own blank band regardless of what other faces claimed.
        {
            int idx = PickUnclaimed(claimed, zoneRng);
            if (idx >= 0) { types[idx] = ZoneType.Structural; claimed[idx] = true; }
        }

        if (budget.NeedsPipeCorridor)
        {
            int idx = PickForPipeCorridor(zones, claimed, tankFarmIndices, zoneRng);
            if (idx >= 0)
            {
                types[idx] = ZoneType.PipeCorridor;
                claimed[idx] = true;
                budget.NeedsPipeCorridor = false;
            }
        }

        if (budget.NeedsCommsArray)
        {
            int idx = PickUnclaimed(claimed, zoneRng);
            if (idx >= 0) { types[idx] = ZoneType.CommsArray; claimed[idx] = true; budget.NeedsCommsArray = false; }
        }

        if (budget.NeedsSignage)
        {
            int idx = PickUnclaimed(claimed, zoneRng);
            if (idx >= 0) { types[idx] = ZoneType.Signage; claimed[idx] = true; budget.NeedsSignage = false; }
        }

        // ── Step 2: COUNT (windows) — only into what's still unclaimed ──
        int unclaimedBeforeWindows = 0;
        for (int i = 0; i < claimed.Length; i++) if (!claimed[i]) unclaimedBeforeWindows++;

        int windowZoneCount = Math.Min(zoneRng.Next(MaxWindowZonesPerFace + 1), budget.WindowZonesRemaining);
        windowZoneCount = Math.Min(windowZoneCount, unclaimedBeforeWindows);
        budget.WindowZonesRemaining -= windowZoneCount;

        for (int n = 0; n < windowZoneCount; n++)
        {
            int idx = PickUnclaimed(claimed, zoneRng);
            types[idx] = ZoneType.Windows;
            claimed[idx] = true;
        }

        // ── Step 3: WEIGHT — fills whatever's left. Per Timo: guaranteed is a floor, not
        // a budget — this legitimately adds MORE TankFarm/Machinery zones on top. ──
        for (int i = 0; i < zones.Length; i++)
        {
            if (claimed[i]) continue;
            types[i] = PickWeightedZoneType(category, zoneRng);
        }

        return types;
    }

    private static ZoneType PickWeightedZoneType(string category, System.Random zoneRng)
    {
        var weights = ZoneTypeWeights.TryGetValue(category, out var w) ? w : ZoneTypeWeights["_default"];
        float total = 0f;
        foreach (var (_, weight) in weights) total += weight;

        float roll = (float)zoneRng.NextDouble() * total;
        float cumulative = 0f;
        foreach (var (type, weight) in weights)
        {
            cumulative += weight;
            if (roll < cumulative) return type;
        }
        return weights[^1].Type; // float-rounding fallback
    }

    // Brief Z2 Part 2: cap on CommsArray's "a small greeble tank or 5" — see GenerateTanks'
    // sizeCap parameter. 0.85 is PickTankRadius's own "common small" tier ceiling, so a
    // CommsArray tank never rolls into the medium/large/rare tiers regardless of which one
    // the underlying roll landed on.
    private const float CommsArrayTankSizeCap = 0.85f;

    // Brief Z4 Fix 2: content density per zone type, one named table (mirrors
    // DecorCastingPolicy's single-table pattern) — starting values, tunable by eye, not
    // correctness. Machinery is deliberately absent: per Timo, "1-3 small tanks is fine —
    // roughly today's tank behaviour, not area-scaled" (unchanged from Z3's GenerateTanks
    // call). Only the zone types whose composition genuinely differs by area are here.
    internal static class ZoneContentDensity
    {
        // TankFarm: cluster count (a cluster is one tank, a row, or a pair — see
        // PlaceTankCluster) per square metre. Reference point from the brief: a ~1200m²
        // zone should read as 8-15 clusters; 0.0083/m² gives ~10 at 1200m², inside that
        // range. Capped so a truly enormous zone doesn't produce an unreasonable count.
        public const float TankFarmClustersPerSqm = 0.0083f;
        public const int   TankFarmMaxClusters     = 20;

        // Fraction of TankFarm clusters that deliberately roll "large" (~5m diameter, via
        // PickLargeTankRadius) rather than the normal small/medium/rare PickTankRadius
        // range — Timo: "several large tanks... with smaller ones around them," the size
        // MIX is what reads as a farm, not cluster count alone.
        public const float TankFarmLargeClusterFraction = 0.35f;

        // CommsArray: antenna/mast count per square metre — "a large comms zone should
        // read as an array, not two masts."
        public const float CommsArrayAntennasPerSqm = 0.05f;
        public const int   CommsArrayMinAntennas    = 2;
        public const int   CommsArrayMaxAntennas    = 14;

        // Storage: container count per square metre — a yard, not a couple of crates.
        public const float StorageContainersPerSqm = 0.05f;
        public const int   StorageMaxContainers     = 16;

        // PipeCorridor: one parallel run per this many metres of the zone's PERPENDICULAR
        // span (length already tracks the zone's long axis via runHalfLen; this is about
        // how many parallel lines fit side-by-side across the short axis).
        public const float PipeCorridorRunSpacingMetres = 4.0f;
        public const int   PipeCorridorMinRuns          = 3;
        public const int   PipeCorridorMaxRuns          = 12;
    }

    // Dispatches one zone's content by type. Antennas/Dishes aren't assigned to any
    // specific ZoneType in Z1's table (the brief lists them as "zoned" scope but doesn't
    // give them a table row) — they run as a background pass on every zone regardless of
    // type, same as they already ran unconditionally on the whole face today, just now on
    // a smaller area per call. Not new content and no change to either pass's own
    // placement/occupancy logic. Brief Z2 Part 2: antennas run "heavy" specifically for
    // CommsArray — this IS their real home (F1 excluded them from Structural/Signage;
    // CommsArray is where they're supposed to dominate).
    private static void RunZonePasses(
        PlacedModule mod, FaceInfo zone, ZoneType type,
        StationModuleMesh mesh, StationModuleMesh glassMesh, FaceOccupancy occupancy,
        List<PlacedGreebleInfo> greeblePlacements,
        System.Random windowRng, System.Random hatchRng, System.Random antennaRng, System.Random dishRng,
        System.Random ventRng, System.Random greebleRng, System.Random tankRng, System.Random containerRng,
        System.Random surfacePipeRng)
    {
        // Brief F1 Fix 3: excluded from Structural and Signage specifically — Structural's
        // whole purpose is a genuinely blank band breaking up the field, and an antenna or
        // dish sprinkled into it partly undoes that; Signage is reserved area a future
        // sign/placard needs to be able to claim cleanly. Every other type keeps them as a
        // background pass — CommsArray gets the "heavy" variant (Brief Z2 Part 2), everyone
        // else (including PipeCorridor) gets the normal occasional roll unchanged.
        // Brief Z4 Fix 2: CommsArray's mast count now also scales with zone area
        // (ZoneContentDensity.CommsArrayAntennasPerSqm) instead of heavy's flat rng.Next(2,5).
        if (type != ZoneType.Structural && type != ZoneType.Signage)
        {
            int? commsArrayCount = type == ZoneType.CommsArray
                ? Math.Clamp(
                    (int)MathF.Round(zone.Width * zone.Height * ZoneContentDensity.CommsArrayAntennasPerSqm),
                    ZoneContentDensity.CommsArrayMinAntennas, ZoneContentDensity.CommsArrayMaxAntennas)
                : null;

            mesh.CurrentDecorClass = DecorClass.Antennas;
            GenerateAntennas(mod, zone, antennaRng, mesh, mod.GlowLights, occupancy, greeblePlacements,
                heavy: type == ZoneType.CommsArray, explicitCount: commsArrayCount);
            mesh.CurrentDecorClass = DecorClass.Dishes;
            GenerateDishes(mod, zone, dishRng, mesh, occupancy, greeblePlacements);
        }

        switch (type)
        {
            case ZoneType.Windows:
                mesh.CurrentDecorClass = DecorClass.Windows;
                // guaranteed: true — this zone was already allocated as Windows by the
                // count mechanism (AssignZoneTypes); skip GenerateWindows' own
                // WindowProbability/blank-face gate so the allocation is honoured (Brief
                // F1 Fix 4). Only reached for multi-zone faces — RunZonePasses is never
                // called from the single-zone/unzoned path.
                GenerateWindows(mod, zone, windowRng, mesh, glassMesh, occupancy, guaranteed: true);
                break;

            case ZoneType.Machinery:
                // Brief Z3 Fix B / Z4 Fix 1: guaranteed: true on all three — Machinery's own
                // named character is "greebles, vents, work/storage clutter" plus tanks, so
                // every pass serving it is a commitment, not just the tank call Z3 covered.
                mesh.CurrentDecorClass = DecorClass.Tanks;
                GenerateTanks(mod, zone, mesh, occupancy, new System.Random(tankRng.Next()), guaranteed: true);
                mesh.CurrentDecorClass = DecorClass.VentGrilles;
                GenerateVentGrilles(mod, zone, ventRng, mesh, occupancy, guaranteed: true);
                mesh.CurrentDecorClass = DecorClass.Greebles;
                GenerateGreebles(mod, zone, greebleRng, mesh, occupancy, greeblePlacements, guaranteed: true);
                break;

            case ZoneType.TankFarm:
                // Brief Z4 Fix 2/3: dedicated area-scaled, size-mixed composition — differs
                // from Machinery's generic GenerateTanks call in kind, not just amount (Timo:
                // TankFarm and Machinery "differ in kind, not only amount"). Supersedes the
                // Z3 Fix B guaranteed-GenerateTanks call this case used to make.
                mesh.CurrentDecorClass = DecorClass.Tanks;
                GenerateTankFarmContent(mod, zone, mesh, occupancy, new System.Random(tankRng.Next()));
                break;

            case ZoneType.ServiceCore:
                // Brief Z4 Fix 1: guaranteed: true on VentGrilles/Greebles — ServiceCore's
                // name itself is its committed content (hatches+vents+greebles). Hatches has
                // no probability gate of its own (only size/orientation gates), so it was
                // already effectively unconditional — no parameter needed there.
                mesh.CurrentDecorClass = DecorClass.Hatches;
                GenerateHatches(mod, zone, hatchRng, mesh, occupancy);
                mesh.CurrentDecorClass = DecorClass.VentGrilles;
                GenerateVentGrilles(mod, zone, ventRng, mesh, occupancy, guaranteed: true);
                mesh.CurrentDecorClass = DecorClass.Greebles;
                GenerateGreebles(mod, zone, greebleRng, mesh, occupancy, greeblePlacements, guaranteed: true);
                break;

            case ZoneType.Storage:
                // Brief Z4 Fix 1: guaranteed: true — Storage's whole purpose is containers.
                mesh.CurrentDecorClass = DecorClass.Containers;
                GenerateContainers(mod, zone, mesh, occupancy, new System.Random(containerRng.Next()), guaranteed: true);
                break;

            // Brief Z2 Part 2: assembled from existing passes, not new geometry. Antennas
            // (heavy, above) are the dominant read; greeble boxes at their natural
            // per-category rate (equipment cabinets at mast bases); a few small tanks
            // (Timo: "a small greeble tank or 5") via GenerateTanks' sizeCap. Cables need
            // no wiring here — greeblePlacements collected from this zone already flow into
            // the SAME per-face list StationCableGenerator.GenerateFaceCables consumes
            // (Decorate()'s multi-zone branch declares it once per face, threads it through
            // every zone's RunZonePasses call). Dishes stay whatever the unmodified
            // background pass produces — already structurally rare/absent for most
            // categories (DishCategories excludes docking-bay/industrial/hab/cargo
            // outright), so "shouldn't dominate" holds without any change here.
            case ZoneType.CommsArray:
                // Brief Z3 Fix B: guaranteed: true on the greeble call only — "equipment
                // cabinets at mast bases" is named as CommsArray's content. The tank call
                // stays unguaranteed (Timo's own framing was "PERHAPS a small greeble tank
                // or 5" — optional flavour, not a commitment like TankFarm/Machinery).
                mesh.CurrentDecorClass = DecorClass.Greebles;
                GenerateGreebles(mod, zone, greebleRng, mesh, occupancy, greeblePlacements, guaranteed: true);
                mesh.CurrentDecorClass = DecorClass.Tanks;
                GenerateTanks(mod, zone, mesh, occupancy, new System.Random(tankRng.Next()), sizeCap: CommsArrayTankSizeCap);
                break;

            // Brief Z2 Part 3: parallel surface-pipe runs along the zone's long axis — a
            // weighting of GenerateSurfacePipes (its parallel parameter), not new geometry.
            // Brief Z4 Fix 1 audit: GenerateParallelSurfacePipes (the parallel:true target)
            // has NO probability/frequency gate at all — its only early-returns are
            // !face.IsExposed and a near-zero-length zone. Measured directly: 0 of 4886
            // sampled exposed PipeCorridor zones (300 stations) produced zero pipes. This
            // means the Z4 brief's own stated diagnosis for "empty purple zones"
            // ("GenerateSurfacePipes has its own probability gate that docking-bay falls
            // through") does not reproduce against this code — flagged to Timo rather than
            // adding a bypass for a gate that doesn't exist here. No change made.
            case ZoneType.PipeCorridor:
                mesh.CurrentDecorClass = DecorClass.SurfacePipes;
                GenerateSurfacePipes(mod, zone, surfacePipeRng, mesh, parallel: true);
                break;

            case ZoneType.Structural:
            case ZoneType.Signage:
                // Blank — Structural is permanently blank by design (the floor-slab/rib
                // bands that break up the field); Signage claims area and generates nothing
                // until its own future brief (Brief Z2 Part 4 — direction-to-nearest-door
                // hook noted there, not built).
                break;
        }
    }
}
