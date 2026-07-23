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
    private static readonly IReadOnlyDictionary<string, (ZoneType Type, float Weight)[]> ZoneTypeWeights =
        new Dictionary<string, (ZoneType, float)[]>
        {
            ["industrial"]  = [(ZoneType.Machinery, 0.30f), (ZoneType.TankFarm, 0.30f), (ZoneType.Structural, 0.20f), (ZoneType.ServiceCore, 0.15f), (ZoneType.Storage, 0.05f)],
            ["docking-bay"] = [(ZoneType.Machinery, 0.25f), (ZoneType.TankFarm, 0.25f), (ZoneType.Structural, 0.20f), (ZoneType.ServiceCore, 0.15f), (ZoneType.Storage, 0.10f), (ZoneType.Signage, 0.05f)],
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

    // Assigns a ZoneType to every zone on one face. Windows are handled first and
    // separately (count-driven, see the class comment); everything else is a weighted pick
    // from ZoneTypeWeights. moduleWindowZoneBudget is threaded by ref across every face of
    // one module so the 5-per-module cap holds regardless of how many faces contribute.
    internal static ZoneType[] AssignZoneTypes(string category, FaceInfo[] zones, System.Random zoneRng, ref int moduleWindowZoneBudget)
    {
        var types = new ZoneType[zones.Length];

        int windowZoneCount = Math.Min(zoneRng.Next(MaxWindowZonesPerFace + 1), moduleWindowZoneBudget);
        windowZoneCount = Math.Min(windowZoneCount, zones.Length);
        moduleWindowZoneBudget -= windowZoneCount;

        // Partial Fisher-Yates: picks windowZoneCount distinct zone indices uniformly,
        // deterministic from zoneRng, without biasing toward low indices.
        var indices = new int[zones.Length];
        for (int i = 0; i < indices.Length; i++) indices[i] = i;
        for (int i = 0; i < windowZoneCount; i++)
        {
            int j = i + zoneRng.Next(indices.Length - i);
            (indices[i], indices[j]) = (indices[j], indices[i]);
            types[indices[i]] = ZoneType.Windows;
        }

        var isWindowZone = new bool[zones.Length];
        for (int i = 0; i < windowZoneCount; i++) isWindowZone[indices[i]] = true;

        for (int i = 0; i < zones.Length; i++)
        {
            if (isWindowZone[i]) continue;
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

    // Dispatches one zone's content by type. Antennas/Dishes aren't assigned to any
    // specific ZoneType in Z1's table (the brief lists them as "zoned" scope but doesn't
    // give them a table row) — they run as a background pass on every zone regardless of
    // type, same as they already ran unconditionally on the whole face today, just now on
    // a smaller area per call. Not new content and no change to either pass's own
    // placement/occupancy logic.
    private static void RunZonePasses(
        PlacedModule mod, FaceInfo zone, ZoneType type,
        StationModuleMesh mesh, StationModuleMesh glassMesh, FaceOccupancy occupancy,
        List<PlacedGreebleInfo> greeblePlacements,
        System.Random windowRng, System.Random hatchRng, System.Random antennaRng, System.Random dishRng,
        System.Random ventRng, System.Random greebleRng, System.Random tankRng, System.Random containerRng)
    {
        mesh.CurrentDecorClass = DecorClass.Antennas;
        GenerateAntennas(mod, zone, antennaRng, mesh, mod.GlowLights, occupancy, greeblePlacements);
        mesh.CurrentDecorClass = DecorClass.Dishes;
        GenerateDishes(mod, zone, dishRng, mesh, occupancy, greeblePlacements);

        switch (type)
        {
            case ZoneType.Windows:
                mesh.CurrentDecorClass = DecorClass.Windows;
                GenerateWindows(mod, zone, windowRng, mesh, glassMesh, occupancy);
                break;

            case ZoneType.Machinery:
                mesh.CurrentDecorClass = DecorClass.Tanks;
                GenerateTanks(mod, zone, mesh, occupancy, new System.Random(tankRng.Next()));
                mesh.CurrentDecorClass = DecorClass.VentGrilles;
                GenerateVentGrilles(mod, zone, ventRng, mesh, occupancy);
                mesh.CurrentDecorClass = DecorClass.Greebles;
                GenerateGreebles(mod, zone, greebleRng, mesh, occupancy, greeblePlacements);
                break;

            case ZoneType.TankFarm:
                mesh.CurrentDecorClass = DecorClass.Tanks;
                GenerateTanks(mod, zone, mesh, occupancy, new System.Random(tankRng.Next()));
                break;

            case ZoneType.ServiceCore:
                mesh.CurrentDecorClass = DecorClass.Hatches;
                GenerateHatches(mod, zone, hatchRng, mesh, occupancy);
                mesh.CurrentDecorClass = DecorClass.VentGrilles;
                GenerateVentGrilles(mod, zone, ventRng, mesh, occupancy);
                mesh.CurrentDecorClass = DecorClass.Greebles;
                GenerateGreebles(mod, zone, greebleRng, mesh, occupancy, greeblePlacements);
                break;

            case ZoneType.Storage:
                mesh.CurrentDecorClass = DecorClass.Containers;
                GenerateContainers(mod, zone, mesh, occupancy, new System.Random(containerRng.Next()));
                break;

            case ZoneType.Structural:
            case ZoneType.Signage:
            case ZoneType.CommsArray:
            case ZoneType.PipeCorridor:
                // Blank in Z1 — Structural is permanently blank by design (the floor-slab/
                // rib bands that break up the field); Signage/CommsArray/PipeCorridor claim
                // area but generate nothing until Z2.
                break;
        }
    }
}
