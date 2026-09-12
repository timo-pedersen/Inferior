using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public enum MegastationArtificialOccluderRole
{
    StructuralOccupancy,
    MajorPlatform,
    ServiceBuilding,
    LandingPadSlab,
    Container,
    SubstantialAccess,
    MajorStructuralMass,
    WallFacility,
    MinorDetail,
}

public readonly record struct MegastationArtificialOccluder(
    MegastationArtificialOccluderRole Role,
    Vector3 Centre,
    Vector3 HalfSize,
    Vector3 Right,
    Vector3 Up,
    Vector3 Forward);

public readonly record struct MegastationArtificialOcclusionDiagnostics(
    int OccluderCount,
    int ArtificialSourceCount,
    long ReceiverSampleCount,
    long VisibilityTestCount,
    long BlockedVisibilityTestCount,
    double BakeMilliseconds);

/// <summary>
/// Generation-only visibility scene for static artificial light. Structural mass uses
/// the authoritative non-uniform occupancy grid; added interior architecture uses the
/// same planned poses and dimensions that drive its visible geometry.
/// </summary>
public sealed class MegastationArtificialOcclusion
{
    private const float EndpointTolerance = .02f;
    private readonly StructuralOccupancy? _occupancy;
    private readonly MegastationArtificialOccluder[] _occluders;
    private readonly OccluderNode? _occluderIndex;
    private readonly MegastationArtificialOccluder[] _occupancyVoids;
    private readonly Dictionary<(Vector3 Source, Vector3 Receiver), bool> _visibilityCache = [];
    private long _receiverSamples;
    private long _visibilityTests;
    private long _blockedTests;
    private long _bakeTicks;

    private MegastationArtificialOcclusion(
        StructuralOccupancy? occupancy,
        IEnumerable<MegastationArtificialOccluder> occluders,
        IEnumerable<MegastationArtificialOccluder>? occupancyVoids = null)
    {
        _occupancy = occupancy;
        _occluders = occluders.Where(item => CastsStaticArtificialShadow(item.Role)).ToArray();
        _occluderIndex = OccluderNode.Build(_occluders);
        _occupancyVoids = occupancyVoids?.ToArray() ?? [];
    }

    public static bool CastsStaticArtificialShadow(MegastationArtificialOccluderRole role)
        => role != MegastationArtificialOccluderRole.MinorDetail;

    public static MegastationArtificialOcclusion CreateForTests(
        params MegastationArtificialOccluder[] occluders)
        => new(null, occluders);

    public static MegastationArtificialOcclusion CreateForTests(
        StructuralOccupancy occupancy,
        params MegastationArtificialOccluder[] occluders)
        => new(occupancy, occluders);

    public static MegastationArtificialOcclusion Build(
        StructuralOccupancy occupancy,
        MegastationLandingDistrictPlan district,
        MegastationInteriorPresentationPlan presentation,
        MegastationBayFacilityPlan? bayFacilities = null,
        IEnumerable<MegastationInteriorStructuralSolid>? structuralSolids = null)
    {
        Vector3 up = district.FloorNormal;
        var occluders = new List<MegastationArtificialOccluder>();

        foreach (MegastationInteriorStructuralSolid solid in structuralSolids ?? [])
            if (solid.CastsStaticArtificialShadow)
                Add(MegastationArtificialOccluderRole.MajorStructuralMass,
                    solid.Centre, solid.Size, solid.Right, solid.Up, solid.Forward);

        foreach (MegastationLandingSitePlan site in district.Sites)
            Add(MegastationArtificialOccluderRole.MajorPlatform, site.ApronCentre,
                new(site.ApronSize.X, MegastationLandingPadAssemblyStandards.ApronThickness,
                    site.ApronSize.Y), site.Right, up, site.PreferredHeading);
        foreach (MegastationLandingPadPlan pad in district.Pads)
        {
            Vector3 centre = pad.PadSurface.Centre
                - up * (MegastationLandingPadAssemblyStandards.PadSlabThickness * .5f);
            Add(MegastationArtificialOccluderRole.LandingPadSlab, centre,
                new(pad.NominalSize.X, MegastationLandingPadAssemblyStandards.PadSlabThickness,
                    pad.NominalSize.Y), pad.PadSurface.Right, up,
                pad.PadSurface.PreferredHeading);

            // The thin deck remains substantial cargo infrastructure. Its visibility
            // proxy shares the exact authoritative pose used by visible geometry.
            MegastationCargoRampGeometry ramp =
                MegastationLandingPadAssemblyStandards.CargoRamp(pad);
            Add(MegastationArtificialOccluderRole.SubstantialAccess,
                (ramp.High + ramp.Low) * .5f
                    - ramp.SurfaceNormal * (ramp.Thickness * .5f),
                new(ramp.Width, ramp.Thickness, Vector3.Distance(ramp.High, ramp.Low)),
                ramp.Right, ramp.SurfaceNormal, ramp.Axis);
        }
        foreach (MegastationLandingServiceBuilding building in district.ServiceBuildings)
            Add(MegastationArtificialOccluderRole.ServiceBuilding,
                building.Centre, building.Size, building.Frontage.Right,
                building.Frontage.Up, building.Frontage.Normal);
        foreach (MegastationLoadingAreaPlan area in district.LoadingAreas)
        foreach (MegastationLandingContainerPlan container in area.Containers)
            Add(MegastationArtificialOccluderRole.Container,
                container.Centre, container.Size, area.Right, up, area.PreferredHeading);
        foreach (MegastationInteriorGuidanceElement element in presentation.Elements)
        {
            // H1c bakes the main cavity only. The hundreds of constructed-throat pieces
            // neither receive this bake nor lie within its short source ranges, so keep
            // them out of the cavity visibility set. Major cavity landmarks do matter.
            if (!element.CastsShadow
                || element.Kind != MegastationInteriorGuidanceKind.InteriorLandmark)
                continue;
            Add(MegastationArtificialOccluderRole.MajorStructuralMass,
                element.Centre, element.Size,
                Axis(element.Frame, 0), Axis(element.Frame, 1), Axis(element.Frame, 2));
        }
        if (bayFacilities is not null)
        foreach (MegastationBayFacility facility in bayFacilities.Facilities)
        foreach (MegastationBayFacilityPart part in facility.Parts.Where(part =>
                     part.CastsArtificialShadow))
            Add(MegastationArtificialOccluderRole.WallFacility,
                part.Centre, part.Size, facility.Right, facility.Up, facility.Normal);
        MegastationArtificialOccluder[] occupancyVoids = bayFacilities?.Facilities
            .Where(facility => facility.Cutout is not null)
            .Select(facility => facility.Cutout!)
            .Select(cutout => new MegastationArtificialOccluder(
                MegastationArtificialOccluderRole.MinorDetail,
                cutout.Centre - cutout.Normal * (cutout.Depth * .5f),
                new(cutout.Size.X * .5f, cutout.Size.Y * .5f,
                    cutout.Depth * .5f + EndpointTolerance),
                cutout.Right, cutout.Up, cutout.Normal))
            .ToArray() ?? [];
        return new(occupancy, occluders, occupancyVoids);

        void Add(MegastationArtificialOccluderRole role, Vector3 centre, Vector3 size,
            Vector3 localRight, Vector3 localUp, Vector3 localForward)
            => occluders.Add(new(role, centre, size * .5f,
                Vector3.Normalize(localRight), Vector3.Normalize(localUp),
                Vector3.Normalize(localForward)));
    }

    public void RecordReceiverSample() => _receiverSamples++;

    public bool IsVisible(Vector3 source, Vector3 receiver)
    {
        _visibilityTests++;
        var key = (source, receiver);
        if (_visibilityCache.TryGetValue(key, out bool cached))
        {
            if (!cached)
                _blockedTests++;
            return cached;
        }
        Vector3 segment = receiver - source;
        float length = segment.Length();
        if (!float.IsFinite(length) || length <= EndpointTolerance * 2f)
            return true;
        Vector3 direction = segment / length;
        Vector3 start = source + direction * EndpointTolerance;
        Vector3 end = receiver - direction * EndpointTolerance;

        bool blocked = OccupancyBlocks(start, end)
            || (_occluderIndex?.IntersectsAny(_occluders, start, end) ?? false);
        _visibilityCache.Add(key, !blocked);
        if (blocked)
            _blockedTests++;
        return !blocked;
    }

    public void MeasureBake(Action action)
    {
        long start = Stopwatch.GetTimestamp();
        action();
        _bakeTicks += Stopwatch.GetTimestamp() - start;
    }

    public MegastationArtificialOcclusionDiagnostics Diagnostics(int sourceCount)
        => new(_occluders.Length + (_occupancy is null ? 0 : 1), sourceCount,
            _receiverSamples, _visibilityTests, _blockedTests,
            _bakeTicks * 1000d / Stopwatch.Frequency);

    private bool OccupancyBlocks(Vector3 start, Vector3 end)
    {
        if (_occupancy is null)
            return false;
        SliceGrid grid = _occupancy.Grid;
        Vector3 delta = end - start;
        float length = delta.Length();
        if (length <= 1e-6f)
            return false;
        Vector3 direction = delta / length;
        int x = CoordinateIndex(grid, GridAxis.X, start.X);
        int y = CoordinateIndex(grid, GridAxis.Y, start.Y);
        int z = CoordinateIndex(grid, GridAxis.Z, start.Z);
        if (!grid.Contains(x, y, z))
            return false;

        float travelled = 0f;
        while (travelled < length && grid.Contains(x, y, z))
        {
            float tx = DistanceToBoundary(grid, GridAxis.X, x, start.X, direction.X);
            float ty = DistanceToBoundary(grid, GridAxis.Y, y, start.Y, direction.Y);
            float tz = DistanceToBoundary(grid, GridAxis.Z, z, start.Z, direction.Z);
            float next = MathF.Min(tx, MathF.Min(ty, tz));
            float intervalEnd = MathF.Min(next, length);
            if (_occupancy.IsOccupied(x, y, z)
                && !OccupancyIntervalIsCarved(
                    start, direction, travelled, intervalEnd))
                return true;
            if (!float.IsFinite(next) || next >= length)
                break;
            const float crossingTolerance = 1e-4f;
            if (MathF.Abs(tx - next) <= crossingTolerance)
                x += Math.Sign(direction.X);
            if (MathF.Abs(ty - next) <= crossingTolerance)
                y += Math.Sign(direction.Y);
            if (MathF.Abs(tz - next) <= crossingTolerance)
                z += Math.Sign(direction.Z);
            travelled = next;
        }
        return false;
    }

    private bool OccupancyIntervalIsCarved(
        Vector3 start,
        Vector3 direction,
        float intervalStart,
        float intervalEnd)
    {
        if (_occupancyVoids.Length == 0)
            return false;
        float inset = MathF.Min(EndpointTolerance,
            MathF.Max(0f, (intervalEnd - intervalStart) * .25f));
        Vector3 a = start + direction * (intervalStart + inset);
        Vector3 b = start + direction * (intervalEnd - inset);
        return _occupancyVoids.Any(volume => Contains(volume, a) && Contains(volume, b));
    }

    private static bool Contains(MegastationArtificialOccluder volume, Vector3 point)
    {
        Vector3 offset = point - volume.Centre;
        const float tolerance = .025f;
        return MathF.Abs(Vector3.Dot(offset, volume.Right)) <= volume.HalfSize.X + tolerance
            && MathF.Abs(Vector3.Dot(offset, volume.Up)) <= volume.HalfSize.Y + tolerance
            && MathF.Abs(Vector3.Dot(offset, volume.Forward)) <= volume.HalfSize.Z + tolerance;
    }

    private static float DistanceToBoundary(
        SliceGrid grid, GridAxis axis, int index, float origin, float direction)
    {
        if (MathF.Abs(direction) <= 1e-8f)
            return float.PositiveInfinity;
        float boundary = direction > 0f
            ? grid.GetCellMaximum(axis, index)
            : grid.GetCellMinimum(axis, index);
        float distance = (boundary - origin) / direction;
        return distance >= 0f ? distance : 0f;
    }

    private static int CoordinateIndex(SliceGrid grid, GridAxis axis, float coordinate)
    {
        int low = 0;
        int high = grid.Count(axis) - 1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (coordinate < grid.GetCellMinimum(axis, mid))
                high = mid - 1;
            else if (coordinate >= grid.GetCellMaximum(axis, mid))
                low = mid + 1;
            else
                return mid;
        }
        return -1;
    }

    private static bool SegmentIntersects(
        MegastationArtificialOccluder occluder, Vector3 start, Vector3 end)
    {
        Vector3 localStart = new(
            Vector3.Dot(start - occluder.Centre, occluder.Right),
            Vector3.Dot(start - occluder.Centre, occluder.Up),
            Vector3.Dot(start - occluder.Centre, occluder.Forward));
        Vector3 localDelta = new(
            Vector3.Dot(end - start, occluder.Right),
            Vector3.Dot(end - start, occluder.Up),
            Vector3.Dot(end - start, occluder.Forward));
        float minimum = 0f;
        float maximum = 1f;
        return Clip(localStart.X, localDelta.X, occluder.HalfSize.X, ref minimum, ref maximum)
            && Clip(localStart.Y, localDelta.Y, occluder.HalfSize.Y, ref minimum, ref maximum)
            && Clip(localStart.Z, localDelta.Z, occluder.HalfSize.Z, ref minimum, ref maximum)
            && maximum >= 0f && minimum <= 1f;
    }

    private static bool Clip(float origin, float delta, float halfSize,
        ref float minimum, ref float maximum)
    {
        if (MathF.Abs(delta) <= 1e-8f)
            return origin >= -halfSize && origin <= halfSize;
        float a = (-halfSize - origin) / delta;
        float b = (halfSize - origin) / delta;
        if (a > b) (a, b) = (b, a);
        minimum = MathF.Max(minimum, a);
        maximum = MathF.Min(maximum, b);
        return minimum <= maximum;
    }

    private sealed class OccluderNode
    {
        private const int LeafSize = 4;
        private const float BoundsInflation = .05f;
        private readonly Vector3 _minimum;
        private readonly Vector3 _maximum;
        private readonly OccluderNode? _left;
        private readonly OccluderNode? _right;
        private readonly int[]? _indices;

        private OccluderNode(
            Vector3 minimum,
            Vector3 maximum,
            OccluderNode? left,
            OccluderNode? right,
            int[]? indices)
        {
            _minimum = minimum;
            _maximum = maximum;
            _left = left;
            _right = right;
            _indices = indices;
        }

        public static OccluderNode? Build(MegastationArtificialOccluder[] occluders)
        {
            if (occluders.Length == 0)
                return null;
            (Vector3 Min, Vector3 Max)[] bounds = occluders.Select(Bounds).ToArray();
            return Build(bounds, Enumerable.Range(0, occluders.Length).ToArray());
        }

        public bool IntersectsAny(
            MegastationArtificialOccluder[] occluders,
            Vector3 start,
            Vector3 end)
        {
            if (!SegmentIntersectsBounds(start, end, _minimum, _maximum))
                return false;
            if (_indices is not null)
            {
                foreach (int index in _indices)
                    if (SegmentIntersects(occluders[index], start, end))
                        return true;
                return false;
            }
            return (_left?.IntersectsAny(occluders, start, end) ?? false)
                || (_right?.IntersectsAny(occluders, start, end) ?? false);
        }

        private static OccluderNode Build(
            (Vector3 Min, Vector3 Max)[] bounds,
            int[] indices)
        {
            Vector3 minimum = new(
                indices.Min(index => bounds[index].Min.X),
                indices.Min(index => bounds[index].Min.Y),
                indices.Min(index => bounds[index].Min.Z));
            Vector3 maximum = new(
                indices.Max(index => bounds[index].Max.X),
                indices.Max(index => bounds[index].Max.Y),
                indices.Max(index => bounds[index].Max.Z));
            if (indices.Length <= LeafSize)
                return new(minimum, maximum, null, null, indices);

            Vector3 centroidMinimum = new(
                indices.Min(index => (bounds[index].Min.X + bounds[index].Max.X) * .5f),
                indices.Min(index => (bounds[index].Min.Y + bounds[index].Max.Y) * .5f),
                indices.Min(index => (bounds[index].Min.Z + bounds[index].Max.Z) * .5f));
            Vector3 centroidMaximum = new(
                indices.Max(index => (bounds[index].Min.X + bounds[index].Max.X) * .5f),
                indices.Max(index => (bounds[index].Min.Y + bounds[index].Max.Y) * .5f),
                indices.Max(index => (bounds[index].Min.Z + bounds[index].Max.Z) * .5f));
            Vector3 span = centroidMaximum - centroidMinimum;
            int axis = span.X >= span.Y && span.X >= span.Z ? 0
                : span.Y >= span.Z ? 1 : 2;
            Array.Sort(indices, (a, b) =>
            {
                float ac = Component(bounds[a].Min + bounds[a].Max, axis);
                float bc = Component(bounds[b].Min + bounds[b].Max, axis);
                int comparison = ac.CompareTo(bc);
                return comparison != 0 ? comparison : a.CompareTo(b);
            });
            int middle = indices.Length / 2;
            OccluderNode left = Build(bounds, indices[..middle]);
            OccluderNode right = Build(bounds, indices[middle..]);
            return new(minimum, maximum, left, right, null);
        }

        private static (Vector3 Min, Vector3 Max) Bounds(
            MegastationArtificialOccluder occluder)
        {
            Vector3 extent = Abs(occluder.Right) * occluder.HalfSize.X
                + Abs(occluder.Up) * occluder.HalfSize.Y
                + Abs(occluder.Forward) * occluder.HalfSize.Z
                + new Vector3(BoundsInflation);
            return (occluder.Centre - extent, occluder.Centre + extent);
        }

        private static bool SegmentIntersectsBounds(
            Vector3 start,
            Vector3 end,
            Vector3 minimum,
            Vector3 maximum)
        {
            Vector3 delta = end - start;
            float entry = 0f;
            float exit = 1f;
            return ClipBounds(start.X, delta.X, minimum.X, maximum.X, ref entry, ref exit)
                && ClipBounds(start.Y, delta.Y, minimum.Y, maximum.Y, ref entry, ref exit)
                && ClipBounds(start.Z, delta.Z, minimum.Z, maximum.Z, ref entry, ref exit);
        }

        private static bool ClipBounds(
            float origin,
            float delta,
            float minimum,
            float maximum,
            ref float entry,
            ref float exit)
        {
            if (MathF.Abs(delta) <= 1e-8f)
                return origin >= minimum && origin <= maximum;
            float a = (minimum - origin) / delta;
            float b = (maximum - origin) / delta;
            if (a > b) (a, b) = (b, a);
            entry = MathF.Max(entry, a);
            exit = MathF.Min(exit, b);
            return entry <= exit;
        }

        private static float Component(Vector3 value, int axis)
            => axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;

        private static Vector3 Abs(Vector3 value)
            => new(MathF.Abs(value.X), MathF.Abs(value.Y), MathF.Abs(value.Z));
    }

    private static Vector3 Axis(Matrix matrix, int axis) => axis switch
    {
        0 => new(matrix.M11, matrix.M12, matrix.M13),
        1 => new(matrix.M21, matrix.M22, matrix.M23),
        _ => new(matrix.M31, matrix.M32, matrix.M33),
    };
}
