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
    private readonly Dictionary<(Vector3 Source, Vector3 Receiver), bool> _visibilityCache = [];
    private long _receiverSamples;
    private long _visibilityTests;
    private long _blockedTests;
    private long _bakeTicks;

    private MegastationArtificialOcclusion(
        StructuralOccupancy? occupancy,
        IEnumerable<MegastationArtificialOccluder> occluders)
    {
        _occupancy = occupancy;
        _occluders = occluders.Where(item => CastsStaticArtificialShadow(item.Role)).ToArray();
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
        MegastationInteriorPresentationPlan presentation)
    {
        Vector3 right = district.DistrictRight;
        Vector3 up = district.FloorNormal;
        Vector3 forward = district.PreferredHeading;
        var occluders = new List<MegastationArtificialOccluder>();

        Add(MegastationArtificialOccluderRole.MajorPlatform, district.ApronCentre,
            new(district.ApronSize.X, MegastationLandingPadAssemblyStandards.ApronThickness,
                district.ApronSize.Y), right, up, forward);
        foreach (MegastationLandingPadPlan pad in district.Pads)
        {
            Vector3 centre = pad.PadSurface.Centre
                - up * (MegastationLandingPadAssemblyStandards.PadSlabThickness * .5f);
            Add(MegastationArtificialOccluderRole.LandingPadSlab, centre,
                new(pad.NominalSize.X, MegastationLandingPadAssemblyStandards.PadSlabThickness,
                    pad.NominalSize.Y), right, up, forward);

            // Only the broad cargo ramp is substantial enough for this first visibility pass.
            (_, Vector3 rampTop, Vector3 serviceDirection) =
                MegastationLandingPadAssemblyStandards.AccessAnchors(pad);
            Vector3 rampLow = rampTop
                + serviceDirection * MegastationLandingPadAssemblyStandards.CargoRampRun
                - up * MegastationLandingPadAssemblyStandards.PadTopHeightAboveApron;
            Vector3 rampAxis = Vector3.Normalize(rampTop - rampLow);
            Vector3 rampNormal = Vector3.Normalize(Vector3.Cross(rampAxis, right));
            Add(MegastationArtificialOccluderRole.SubstantialAccess,
                (rampTop + rampLow) * .5f,
                new(MegastationLandingPadAssemblyStandards.CargoRampWidth, .20f,
                    Vector3.Distance(rampTop, rampLow)), right, rampNormal, rampAxis);
        }
        foreach (MegastationLandingServiceBuilding building in district.ServiceBuildings)
            Add(MegastationArtificialOccluderRole.ServiceBuilding,
                building.Centre, building.Size, right, up, forward);
        foreach (MegastationLandingContainerPlan container in
                 district.LoadingAreas.SelectMany(area => area.Containers))
            Add(MegastationArtificialOccluderRole.Container,
                container.Centre, container.Size, right, up, forward);
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
        return new(occupancy, occluders);

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
            || _occluders.Any(occluder => SegmentIntersects(occluder, start, end));
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
            if (_occupancy.IsOccupied(x, y, z))
                return true;
            float tx = DistanceToBoundary(grid, GridAxis.X, x, start.X, direction.X);
            float ty = DistanceToBoundary(grid, GridAxis.Y, y, start.Y, direction.Y);
            float tz = DistanceToBoundary(grid, GridAxis.Z, z, start.Z, direction.Z);
            float next = MathF.Min(tx, MathF.Min(ty, tz));
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

    private static Vector3 Axis(Matrix matrix, int axis) => axis switch
    {
        0 => new(matrix.M11, matrix.M12, matrix.M13),
        1 => new(matrix.M21, matrix.M22, matrix.M23),
        _ => new(matrix.M31, matrix.M32, matrix.M33),
    };
}
