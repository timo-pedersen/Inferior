using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

/// <summary>
/// Generation-local projections of windows and fixtures into each planar region's UV
/// frame. Planners make many candidate queries against the same immutable features;
/// projecting once avoids repeatedly scanning every station-wide feature list.
/// </summary>
internal sealed class MegastationSurfaceFeatureIndex
{
    private static readonly WindowFeature[] NoWindows = [];
    private static readonly LightFeature[] NoLights = [];
    private readonly Dictionary<SurfaceKey, WindowFeature[]> _windowsBySurface;
    private readonly Dictionary<SurfaceKey, LightFeature[]> _lightsBySurface;

    public MegastationSurfaceFeatureIndex(
        IReadOnlyList<MegastationPlanarRegion> regions,
        IReadOnlyList<MegastationWindowInstance> windows,
        IReadOnlyList<MegastationLightInstance> lights)
    {
        _windowsBySurface = [];
        _lightsBySurface = [];
        foreach (MegastationPlanarRegion region in regions
                     .GroupBy(SurfaceKey.From)
                     .Select(group => group.First()))
        {
            SurfaceKey key = SurfaceKey.From(region);
            _windowsBySurface[key] = windows
                .Select(window => Project(region, window))
                .Where(feature => feature.NormalAlignment >= .999f)
                .ToArray();
            _lightsBySurface[key] = lights
                .Select(light => new LightFeature(
                    Vector3.Dot(light.Normal, region.OutwardNormal),
                    Vector3.Dot(light.SurfacePosition, region.OutwardNormal),
                    Vector3.Dot(light.SurfacePosition, region.TangentU),
                    Vector3.Dot(light.SurfacePosition, region.TangentV)))
                .Where(feature => feature.NormalAlignment >= .999f)
                .ToArray();
        }
    }

    public IReadOnlyList<WindowFeature> Windows(MegastationPlanarRegion region)
        => _windowsBySurface.GetValueOrDefault(SurfaceKey.From(region), NoWindows);

    public IReadOnlyList<LightFeature> Lights(MegastationPlanarRegion region)
        => _lightsBySurface.GetValueOrDefault(SurfaceKey.From(region), NoLights);

    private static WindowFeature Project(
        MegastationPlanarRegion region,
        MegastationWindowInstance window)
    {
        Vector3 right = Vector3.Normalize(Vector3.Cross(window.Up, window.Normal));
        Vector3[] corners =
        [
            window.Centre - right * window.Width * .5f - window.Up * window.Height * .5f,
            window.Centre + right * window.Width * .5f - window.Up * window.Height * .5f,
            window.Centre + right * window.Width * .5f + window.Up * window.Height * .5f,
            window.Centre - right * window.Width * .5f + window.Up * window.Height * .5f,
        ];
        return new(
            Vector3.Dot(window.Normal, region.OutwardNormal),
            Vector3.Dot(window.Centre, region.OutwardNormal),
            Vector3.Dot(window.Centre, region.TangentU),
            Vector3.Dot(window.Centre, region.TangentV),
            MathF.Max(window.Width, window.Height) * .5f,
            corners.Min(point => Vector3.Dot(point, region.TangentU)),
            corners.Max(point => Vector3.Dot(point, region.TangentU)),
            corners.Min(point => Vector3.Dot(point, region.TangentV)),
            corners.Max(point => Vector3.Dot(point, region.TangentV)));
    }

    internal readonly record struct WindowFeature(
        float NormalAlignment,
        float PlaneCoordinate,
        float U,
        float V,
        float Radius,
        float MinU,
        float MaxU,
        float MinV,
        float MaxV);

    internal readonly record struct LightFeature(
        float NormalAlignment,
        float PlaneCoordinate,
        float U,
        float V);

    private readonly record struct SurfaceKey(
        GridDirection Direction,
        Vector3 OutwardNormal,
        Vector3 TangentU,
        Vector3 TangentV)
    {
        public static SurfaceKey From(MegastationPlanarRegion region)
            => new(region.Direction,
                region.OutwardNormal, region.TangentU, region.TangentV);
    }
}
