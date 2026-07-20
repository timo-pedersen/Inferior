using Inferior.Core.Math;
using Inferior.Gameplay.Cockpit;
using Inferior.Gameplay.Engines;
using Inferior.Gameplay.Hull;
using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Ship;

public readonly record struct ShipPresentationBounds(DVec3 Min, DVec3 Max)
{
    public DVec3 Center => (Min + Max) / 2.0;
    public DVec3 Size => Max - Min;
    public double RadiusMeters => Size.Length / 2.0;
}

public static class ShipPresentationBoundsCalculator
{
    public static ShipPresentationBounds? TryCalculate(Ship ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return HullDefinitionLibrary.TryGet(ship.HullTypeId, out _)
            ? Calculate(ship)
            : null;
    }

    public static ShipPresentationBounds Calculate(Ship ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        HullDefinition hull = HullDefinitionLibrary.Get(ship.HullTypeId);
        var accumulator = new BoundsAccumulator();

        if (hull.VisualGeometry is { } hullGeometry)
        {
            foreach (SemanticHullVertex vertex in hullGeometry.Vertices)
                accumulator.Include(vertex.Position);
        }
        else if (hull.Dimensions is { } dimensions)
        {
            accumulator.Include(new DVec3(
                -dimensions.WidthMeters / 2.0,
                -dimensions.HeightMeters / 2.0,
                -dimensions.LengthMeters / 2.0));
            accumulator.Include(new DVec3(
                dimensions.WidthMeters / 2.0,
                dimensions.HeightMeters / 2.0,
                dimensions.LengthMeters / 2.0));
        }

        foreach (EngineInstance engine in ship.EngineMounts
            .Select(mount => mount.InstalledEngine)
            .OfType<EngineInstance>())
        {
            if (engine.GeometryTransform is not { } transform
                || engine.Variant.Engine.VisualGeometry is not { } geometry)
            {
                continue;
            }

            foreach (EngineVisualTriangle triangle in geometry.MeshParts
                .SelectMany(part => part.Triangles))
            {
                accumulator.Include(transform.TransformVisualPoint(triangle.A));
                accumulator.Include(transform.TransformVisualPoint(triangle.B));
                accumulator.Include(transform.TransformVisualPoint(triangle.C));
            }
        }

        if (ship.Cockpit is { } cockpit)
        {
            CockpitModuleDefinition definition =
                CockpitDefinitionLibrary.Get(cockpit.DefinitionId);
            if (definition.VisualGeometry is { } geometry)
            {
                DVec3 rootPosition = ship.CockpitRootShipLocalPosition;
                Quaternion rootOrientation = ship.CockpitRootShipLocalOrientation;
                foreach (CockpitVisualTriangle triangle in geometry.MeshParts
                    .SelectMany(part => part.Triangles))
                {
                    accumulator.Include(TransformCockpitPoint(
                        triangle.A,
                        rootPosition,
                        rootOrientation));
                    accumulator.Include(TransformCockpitPoint(
                        triangle.B,
                        rootPosition,
                        rootOrientation));
                    accumulator.Include(TransformCockpitPoint(
                        triangle.C,
                        rootPosition,
                        rootOrientation));
                }
            }
        }

        return accumulator.Build();
    }

    private static DVec3 TransformCockpitPoint(
        DVec3 point,
        DVec3 rootPosition,
        Quaternion rootOrientation)
    {
        Vector3 rotated = Vector3.Transform(point.ToVector3(), rootOrientation);
        return rootPosition + new DVec3(rotated.X, rotated.Y, rotated.Z);
    }

    private sealed class BoundsAccumulator
    {
        private DVec3 _min;
        private DVec3 _max;
        private bool _hasPoint;

        public void Include(DVec3 point)
        {
            if (!_hasPoint)
            {
                _min = point;
                _max = point;
                _hasPoint = true;
                return;
            }

            _min = new DVec3(
                Math.Min(_min.X, point.X),
                Math.Min(_min.Y, point.Y),
                Math.Min(_min.Z, point.Z));
            _max = new DVec3(
                Math.Max(_max.X, point.X),
                Math.Max(_max.Y, point.Y),
                Math.Max(_max.Z, point.Z));
        }

        public ShipPresentationBounds Build()
        {
            if (!_hasPoint)
                throw new InvalidOperationException("Ship presentation bounds contain no geometry.");
            return new ShipPresentationBounds(_min, _max);
        }
    }
}
