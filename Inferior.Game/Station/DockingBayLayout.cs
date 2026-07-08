namespace Inferior.Game.StationGen;

// Pad-mix-driven sizing for the docking-bay module. Computed once per station (from the
// station's own seed, not the per-module seed drawn during attachment — see StationGenerator.Run)
// so the resulting envelope is known before the module is placed, which is when it's needed for
// AABB/collision checks. StationModuleRegistry.CreateDockingBay turns this into the actual
// StationModuleDefinition (BoundingBox, ports, DoorOpening); DockingBayHull.Build reads the same
// instance to build the matching mesh, so the two can never disagree.
//
// Ship-envelope reference: Docs-claude/inferior-ship-sizes-and-mass-claude.md. Medium and Large
// ships share the same max width (36m) — only height/length differ — so the door only needs a
// height variant per class, not a width one. Both door and cavity margins reproduce the original
// MVP's fixed 40x24m / 32m-tall bay exactly when solved backwards, confirming the formulas.
public readonly record struct DockingBayLayout(
    int   MediumCount, int LargeCount,
    int   Columns, int Rows,
    float CavityWidth, float CavityDepth, float CavityHeight,
    float DoorWidth, float DoorHeight,
    float ChamferFraction)
{
    // Landing pad footprints (lore-fixed, see the ship-sizes reference): medium/small ships use a
    // 36x36m pad; large ships need 36x72m (2 slots along the long axis).
    private const float SlotSize = 36f;

    // Gaps for buildings/cables between pad slots, and clearance from the interior cavity walls.
    private const float PadSpacing          = 18f;
    private const float PerimeterClearance  = 20f;

    // Door = largest-served ship's max envelope + this margin. Reproduces the MVP's fixed
    // 40x24m door exactly from Large's 36x20 max (width+height), confirming the convention.
    private const float ShipClearanceMargin = 4f;

    // Cavity height = door height + this — reproduces the MVP's fixed 32m from its 24m door.
    private const float CavityHeightMargin  = 8f;

    private const float LargeMaxHeight  = 20f;  // Docs-claude/inferior-ship-sizes-and-mass-claude.md
    private const float MediumMaxHeight = 12f;
    private const float ShipMaxWidth    = 36f;  // shared by Medium and Large

    public static DockingBayLayout Compute(int stationSeed, StationScale stationScale)
    {
        var rng = new System.Random(stationSeed ^ 0x42415950);   // "BAYP" salt

        var (capMin, capMax) = stationScale switch
        {
            StationScale.Port        => (4, 10),
            StationScale.Megastation => (12, 28),
            _                        => (2, 4),   // not reachable today (docking bay is Port+ only)
        };
        int capacity = rng.Next(capMin, capMax + 1);   // pad-equivalents; a large pad counts as 2

        // Mostly medium, occasional large — 10-30% of capacity as large pads.
        float largeFraction = 0.10f + (float)rng.NextDouble() * 0.20f;
        int   largeCount    = System.Math.Max(0, (int)System.Math.Round(capacity * largeFraction / 2f));
        int   mediumCount   = System.Math.Max(1, capacity - largeCount * 2);   // always at least 1 pad

        int totalSlots = mediumCount + largeCount * 2;
        int columns    = System.Math.Clamp((int)System.Math.Ceiling(System.Math.Sqrt(totalSlots)), 2, 6);

        // Round-robin packing: large pads first (2 rows each), then medium (1 row), across
        // columns. Only the resulting footprint (columns x tallest column) is needed — this
        // MVP doesn't place individual pad markers, so a full bin-packing solve isn't warranted.
        var colRows = new int[columns];
        int col = 0;
        for (int i = 0; i < largeCount; i++)  { colRows[col] += 2; col = (col + 1) % columns; }
        for (int i = 0; i < mediumCount; i++) { colRows[col] += 1; col = (col + 1) % columns; }
        int rows = 1;
        foreach (var r in colRows) rows = System.Math.Max(rows, r);

        float cavityWidth = columns * SlotSize + (columns - 1) * PadSpacing + 2 * PerimeterClearance;
        float cavityDepth = rows    * SlotSize + (rows    - 1) * PadSpacing + 2 * PerimeterClearance;

        bool  hasLarge   = largeCount > 0;
        float doorWidth  = ShipMaxWidth + ShipClearanceMargin;
        float doorHeight = (hasLarge ? LargeMaxHeight : MediumMaxHeight) + ShipClearanceMargin;

        // Defensive floor — the grid footprint should always comfortably exceed the door, but
        // don't assume it for every possible seed/mix.
        cavityWidth = System.Math.Max(cavityWidth, doorWidth + 2 * PerimeterClearance);

        float cavityHeight = doorHeight + CavityHeightMargin;

        // Door corner chamfer as a % of door height, seeded once per station (not per door) so
        // every bay on one station shares the same proportional look. 5-25% lands in the
        // requested ~0.5-6m absolute range across both door-height variants (16m/24m).
        float chamferFraction = 0.05f + (float)rng.NextDouble() * 0.20f;

        return new DockingBayLayout(mediumCount, largeCount, columns, rows,
            cavityWidth, cavityDepth, cavityHeight, doorWidth, doorHeight, chamferFraction);
    }
}
