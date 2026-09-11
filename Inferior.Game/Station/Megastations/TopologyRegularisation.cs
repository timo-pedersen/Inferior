namespace Inferior.Game.StationGen.Megastations;

public enum TopologyContactKind
{
    EdgeDiagonal,
    VertexOnly,
}

public readonly record struct MegacellCoord(int X, int Y, int Z)
{
    public override string ToString() => $"({X},{Y},{Z})";
}

public sealed record TopologyContactAudit(
    TopologyContactKind Kind,
    GridAxis Axis,
    int A,
    int B,
    int C,
    MegacellOwner OwnerA,
    MegacellOwner OwnerB,
    string RegionA,
    string RegionB,
    MegacellCoord CellA,
    MegacellCoord CellB,
    string SurroundingOccupancy);

public sealed record TopologyRegularisationReport(
    int AlgorithmVersion,
    int Iterations,
    int RawOccupiedCells,
    int RegularisedOccupiedCells,
    int RepairAddedCells,
    int RepairRemovedCells,
    int EdgeCriticalBefore,
    int EdgeCriticalAfter,
    int VertexCriticalBefore,
    int VertexCriticalAfter,
    int ConnectedComponentsBefore,
    int ConnectedComponentsAfter,
    bool SealedCavityBefore,
    bool SealedCavityAfter,
    IReadOnlyList<string> DefectOwnerSummary,
    IReadOnlyList<TopologyContactAudit> SampleContacts);

public static class TopologyRegulariser
{
    private const int MaxIterations = 512;
    private static readonly (int dx, int dy, int dz)[] FaceNeighbours =
    [
        (-1, 0, 0), (1, 0, 0), (0, -1, 0), (0, 1, 0), (0, 0, -1), (0, 0, 1),
    ];

    private static readonly (int dx, int dy, int dz)[] EdgeOrFaceNeighbours =
    [
        (-1, -1, 0), (-1, 0, -1), (-1, 0, 0), (-1, 0, 1), (-1, 1, 0),
        (0, -1, -1), (0, -1, 0), (0, -1, 1), (0, 0, -1), (0, 0, 1),
        (0, 1, -1), (0, 1, 0), (0, 1, 1),
        (1, -1, 0), (1, 0, -1), (1, 0, 0), (1, 0, 1), (1, 1, 0),
    ];

    public static (StructuralOccupancy Occupancy, TopologyRegularisationReport Report) Regularise(
        StructuralOccupancy raw,
        MegastationPrototypeSettings settings)
    {
        var beforeConnectivity = MegastationConnectivity.Validate(raw);
        var beforeContacts = FindCriticalContacts(raw);
        var occupancy = raw.Clone();
        int added = 0;
        int iterations = 0;

        while (iterations < MaxIterations)
        {
            var contacts = FindCriticalContacts(occupancy);
            if (contacts.Count == 0) break;

            int addedThisPass = 0;
            foreach (var contact in contacts)
            {
                if (!IsContactStillCritical(occupancy, contact)) continue;
                var repair = ChooseRepairForContact(occupancy, contact);
                if (repair == null) continue;
                occupancy.MarkTopologyRegularisation(
                    repair.Value.X,
                    repair.Value.Y,
                    repair.Value.Z,
                    ChooseRepairRegionId(occupancy, repair.Value));
                added++;
                addedThisPass++;
            }

            if (addedThisPass == 0) break;
            iterations++;
        }

        var afterContacts = FindCriticalContacts(occupancy);
        var afterConnectivity = MegastationConnectivity.Validate(occupancy);

        var report = new TopologyRegularisationReport(
            settings.TopologyRegularisationAlgorithmVersion,
            iterations,
            raw.TotalOccupiedCount,
            occupancy.TotalOccupiedCount,
            added,
            0,
            beforeContacts.Count(c => c.Kind == TopologyContactKind.EdgeDiagonal),
            afterContacts.Count(c => c.Kind == TopologyContactKind.EdgeDiagonal),
            beforeContacts.Count(c => c.Kind == TopologyContactKind.VertexOnly),
            afterContacts.Count(c => c.Kind == TopologyContactKind.VertexOnly),
            beforeConnectivity.ConnectedComponentsBeforeValidation,
            afterConnectivity.ConnectedComponentsBeforeValidation,
            beforeConnectivity.HasSealedCavity,
            afterConnectivity.HasSealedCavity,
            OwnerSummary(beforeContacts),
            beforeContacts.Take(16).ToArray());

        return (occupancy, report);
    }

    public static IReadOnlyList<TopologyContactAudit> FindCriticalContacts(StructuralOccupancy occupancy)
    {
        var contacts = new List<TopologyContactAudit>();
        AddEdgeDiagonalContacts(occupancy, contacts);
        AddVertexOnlyContacts(occupancy, contacts);
        return contacts;
    }

    private static MegacellCoord? ChooseRepairForContact(StructuralOccupancy occupancy, TopologyContactAudit contact)
    {
        RepairCandidate? best = null;
        foreach (var coord in RepairCandidatesFor(occupancy, contact))
        {
            var candidate = new RepairCandidate(
                coord,
                CandidateSealedCavityPenalty(occupancy, coord),
                CandidateConnectedComponents(occupancy, coord),
                CellVolume(occupancy.Grid, coord),
                ContinuityScore(occupancy, coord),
                OwnerContinuityScore(occupancy, coord),
                ExteriorPreference(occupancy.Grid, coord));

            if (best == null || Compare(candidate, best.Value) < 0)
                best = candidate;
        }

        return best?.Coord;
    }

    private static bool IsContactStillCritical(StructuralOccupancy occupancy, TopologyContactAudit contact)
    {
        if (contact.Kind == TopologyContactKind.EdgeDiagonal)
        {
            bool[] edgeOccupied = EdgeQuadrants(contact).Select(c => occupancy.IsOccupied(c.X, c.Y, c.Z)).ToArray();
            return edgeOccupied.Count(o => o) == 2 && ((edgeOccupied[0] && edgeOccupied[3]) || (edgeOccupied[1] && edgeOccupied[2]));
        }

        var vertexOccupied = VertexOctants(contact).Where(c => occupancy.IsOccupied(c.X, c.Y, c.Z)).Select(c => (c.X, c.Y, c.Z)).ToArray();
        return vertexOccupied.Length >= 2 && CountLocalComponents(vertexOccupied, EdgeOrFaceNeighbours) > 1;
    }

    private static int Compare(RepairCandidate a, RepairCandidate b)
    {
        int c = a.SealedCavityPenalty.CompareTo(b.SealedCavityPenalty);
        if (c != 0) return c;
        c = a.Components.CompareTo(b.Components);
        if (c != 0) return c;
        c = a.Volume.CompareTo(b.Volume);
        if (c != 0) return c;
        c = -a.Continuity.CompareTo(b.Continuity);
        if (c != 0) return c;
        c = -a.OwnerContinuity.CompareTo(b.OwnerContinuity);
        if (c != 0) return c;
        c = -a.ExteriorPreference.CompareTo(b.ExteriorPreference);
        if (c != 0) return c;
        c = a.Coord.X.CompareTo(b.Coord.X);
        if (c != 0) return c;
        c = a.Coord.Y.CompareTo(b.Coord.Y);
        if (c != 0) return c;
        return a.Coord.Z.CompareTo(b.Coord.Z);
    }

    private static IEnumerable<MegacellCoord> RepairCandidatesFor(StructuralOccupancy occupancy, TopologyContactAudit contact)
    {
        foreach (var coord in contact.Kind == TopologyContactKind.EdgeDiagonal
                     ? EdgeQuadrants(contact)
                     : VertexOctants(contact))
        {
            if (!occupancy.Grid.Contains(coord.X, coord.Y, coord.Z) ||
                occupancy.IsOccupied(coord.X, coord.Y, coord.Z) ||
                occupancy.IsProtectedVoid(coord.X, coord.Y, coord.Z) ||
                ContinuityScore(occupancy, coord) == 0)
                continue;
            yield return coord;
        }
    }

    private static IEnumerable<MegacellCoord> EdgeQuadrants(TopologyContactAudit contact)
    {
        return contact.Axis switch
        {
            GridAxis.X =>
            [
                new MegacellCoord(contact.A, contact.B - 1, contact.C - 1),
                new MegacellCoord(contact.A, contact.B,     contact.C - 1),
                new MegacellCoord(contact.A, contact.B - 1, contact.C),
                new MegacellCoord(contact.A, contact.B,     contact.C),
            ],
            GridAxis.Y =>
            [
                new MegacellCoord(contact.B - 1, contact.A, contact.C - 1),
                new MegacellCoord(contact.B,     contact.A, contact.C - 1),
                new MegacellCoord(contact.B - 1, contact.A, contact.C),
                new MegacellCoord(contact.B,     contact.A, contact.C),
            ],
            _ =>
            [
                new MegacellCoord(contact.B - 1, contact.C - 1, contact.A),
                new MegacellCoord(contact.B,     contact.C - 1, contact.A),
                new MegacellCoord(contact.B - 1, contact.C,     contact.A),
                new MegacellCoord(contact.B,     contact.C,     contact.A),
            ],
        };
    }

    private static IEnumerable<MegacellCoord> VertexOctants(TopologyContactAudit contact) =>
    [
        new MegacellCoord(contact.A - 1, contact.B - 1, contact.C - 1),
        new MegacellCoord(contact.A,     contact.B - 1, contact.C - 1),
        new MegacellCoord(contact.A - 1, contact.B,     contact.C - 1),
        new MegacellCoord(contact.A,     contact.B,     contact.C - 1),
        new MegacellCoord(contact.A - 1, contact.B - 1, contact.C),
        new MegacellCoord(contact.A,     contact.B - 1, contact.C),
        new MegacellCoord(contact.A - 1, contact.B,     contact.C),
        new MegacellCoord(contact.A,     contact.B,     contact.C),
    ];

    private static void AddEdgeDiagonalContacts(StructuralOccupancy occupancy, List<TopologyContactAudit> contacts)
    {
        var grid = occupancy.Grid;
        for (int x = 0; x < grid.XCount; x++)
        for (int y = 0; y <= grid.YCount; y++)
        for (int z = 0; z <= grid.ZCount; z++)
            AddEdgeContact(occupancy, contacts, GridAxis.X, x, y, z, [(x, y - 1, z - 1), (x, y, z - 1), (x, y - 1, z), (x, y, z)]);

        for (int y = 0; y < grid.YCount; y++)
        for (int x = 0; x <= grid.XCount; x++)
        for (int z = 0; z <= grid.ZCount; z++)
            AddEdgeContact(occupancy, contacts, GridAxis.Y, y, x, z, [(x - 1, y, z - 1), (x, y, z - 1), (x - 1, y, z), (x, y, z)]);

        for (int z = 0; z < grid.ZCount; z++)
        for (int x = 0; x <= grid.XCount; x++)
        for (int y = 0; y <= grid.YCount; y++)
            AddEdgeContact(occupancy, contacts, GridAxis.Z, z, x, y, [(x - 1, y - 1, z), (x, y - 1, z), (x - 1, y, z), (x, y, z)]);
    }

    private static void AddEdgeContact(
        StructuralOccupancy occupancy,
        List<TopologyContactAudit> contacts,
        GridAxis axis,
        int a,
        int b,
        int c,
        (int x, int y, int z)[] cells)
    {
        bool[] occupied = cells.Select(cell => occupancy.IsOccupied(cell.x, cell.y, cell.z)).ToArray();
        if (occupied.Count(o => o) != 2) return;

        bool opposite = (occupied[0] && occupied[3]) || (occupied[1] && occupied[2]);
        if (!opposite) return;

        int first = Array.FindIndex(occupied, o => o);
        int second = Array.FindLastIndex(occupied, o => o);
        contacts.Add(MakeAudit(occupancy, TopologyContactKind.EdgeDiagonal, axis, a, b, c, cells[first], cells[second], cells));
    }

    private static void AddVertexOnlyContacts(StructuralOccupancy occupancy, List<TopologyContactAudit> contacts)
    {
        var grid = occupancy.Grid;
        for (int x = 0; x <= grid.XCount; x++)
        for (int y = 0; y <= grid.YCount; y++)
        for (int z = 0; z <= grid.ZCount; z++)
        {
            (int x, int y, int z)[] cells =
            [
                (x - 1, y - 1, z - 1), (x, y - 1, z - 1), (x - 1, y, z - 1), (x, y, z - 1),
                (x - 1, y - 1, z), (x, y - 1, z), (x - 1, y, z), (x, y, z),
            ];
            var occupied = cells.Where(cell => occupancy.IsOccupied(cell.x, cell.y, cell.z)).ToArray();
            if (occupied.Length < 2 || CountLocalComponents(occupied, EdgeOrFaceNeighbours) <= 1) continue;
            contacts.Add(MakeAudit(occupancy, TopologyContactKind.VertexOnly, GridAxis.X, x, y, z, occupied[0], occupied[^1], cells));
        }
    }

    private static int CountLocalComponents((int x, int y, int z)[] occupied, (int dx, int dy, int dz)[] neighbours)
    {
        var remaining = occupied.ToHashSet();
        int components = 0;
        while (remaining.Count > 0)
        {
            components++;
            var start = remaining.First();
            var q = new Queue<(int x, int y, int z)>();
            q.Enqueue(start);
            remaining.Remove(start);
            while (q.Count > 0)
            {
                var current = q.Dequeue();
                foreach (var n in neighbours)
                {
                    var next = (current.x + n.dx, current.y + n.dy, current.z + n.dz);
                    if (!remaining.Remove(next)) continue;
                    q.Enqueue(next);
                }
            }
        }

        return components;
    }

    private static TopologyContactAudit MakeAudit(
        StructuralOccupancy occupancy,
        TopologyContactKind kind,
        GridAxis axis,
        int a,
        int b,
        int c,
        (int x, int y, int z) first,
        (int x, int y, int z) second,
        (int x, int y, int z)[] surrounding)
    {
        string regionA = occupancy.RegionId(first.x, first.y, first.z) ?? string.Empty;
        string regionB = occupancy.RegionId(second.x, second.y, second.z) ?? string.Empty;
        return new TopologyContactAudit(
            kind,
            axis,
            a,
            b,
            c,
            occupancy.Owner(first.x, first.y, first.z),
            occupancy.Owner(second.x, second.y, second.z),
            regionA,
            regionB,
            new MegacellCoord(first.x, first.y, first.z),
            new MegacellCoord(second.x, second.y, second.z),
            string.Concat(surrounding.Select(cell => occupancy.IsOccupied(cell.x, cell.y, cell.z) ? '1' : '0')));
    }

    private static string ChooseRepairRegionId(StructuralOccupancy occupancy, MegacellCoord coord)
    {
        var best = AdjacentOccupied(occupancy, coord)
            .Select(c => occupancy.RegionId(c.X, c.Y, c.Z))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .GroupBy(id => id!, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .FirstOrDefault();
        return best?.Key ?? "topology-regularisation";
    }

    private static int ContinuityScore(StructuralOccupancy occupancy, MegacellCoord coord)
        => AdjacentOccupied(occupancy, coord).Count();

    private static int OwnerContinuityScore(StructuralOccupancy occupancy, MegacellCoord coord)
        => AdjacentOccupied(occupancy, coord)
            .GroupBy(c => occupancy.RegionId(c.X, c.Y, c.Z) ?? string.Empty, StringComparer.Ordinal)
            .Select(g => g.Count())
            .DefaultIfEmpty(0)
            .Max();

    private static int ExteriorPreference(SliceGrid grid, MegacellCoord coord)
        => grid.ExteriorAxisCount(coord.X, coord.Y, coord.Z);

    private static int CandidateSealedCavityPenalty(StructuralOccupancy occupancy, MegacellCoord coord)
    {
        var test = occupancy.Clone();
        test.MarkTopologyRegularisation(coord.X, coord.Y, coord.Z, ChooseRepairRegionId(occupancy, coord));
        return MegastationConnectivity.Validate(test).HasSealedCavity ? 1 : 0;
    }

    private static int CandidateConnectedComponents(StructuralOccupancy occupancy, MegacellCoord coord)
    {
        var test = occupancy.Clone();
        test.MarkTopologyRegularisation(coord.X, coord.Y, coord.Z, ChooseRepairRegionId(occupancy, coord));
        return MegastationConnectivity.Validate(test).ConnectedComponentsBeforeValidation;
    }

    private static IEnumerable<MegacellCoord> AdjacentOccupied(StructuralOccupancy occupancy, MegacellCoord coord)
    {
        foreach (var n in FaceNeighbours)
        {
            int x = coord.X + n.dx, y = coord.Y + n.dy, z = coord.Z + n.dz;
            if (occupancy.Grid.Contains(x, y, z) && occupancy.IsOccupied(x, y, z))
                yield return new MegacellCoord(x, y, z);
        }
    }

    private static float CellVolume(SliceGrid grid, MegacellCoord coord)
        => grid.GetCellSize(GridAxis.X, coord.X) * grid.GetCellSize(GridAxis.Y, coord.Y) * grid.GetCellSize(GridAxis.Z, coord.Z);

    private static IReadOnlyList<string> OwnerSummary(IReadOnlyList<TopologyContactAudit> contacts)
        => contacts
            .GroupBy(c => OwnerPair(c), StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key}={g.Count()}")
            .ToArray();

    private static string OwnerPair(TopologyContactAudit contact)
    {
        string a = contact.OwnerA.ToString();
        string b = contact.OwnerB.ToString();
        return string.CompareOrdinal(a, b) <= 0 ? $"{a}<->{b}" : $"{b}<->{a}";
    }

    private readonly record struct RepairCandidate(
        MegacellCoord Coord,
        int SealedCavityPenalty,
        int Components,
        float Volume,
        int Continuity,
        int OwnerContinuity,
        int ExteriorPreference);
}
