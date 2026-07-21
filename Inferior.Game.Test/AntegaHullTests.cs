using Inferior.Core.Math;
using Inferior.Game;
using Inferior.Game.Ships;
using Inferior.Game.States;
using Inferior.Game.UI;
using Inferior.Gameplay;
using Inferior.Gameplay.Cockpit;
using Inferior.Gameplay.Engines;
using Inferior.Gameplay.Hull;
using Inferior.Gameplay.Ship;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace Inferior.Game.Test;

public sealed class AntegaHullTests
{
    [Fact]
    public void Definition_IsValidLargeHullWithTwelveByFiveByTwoCargoArrangement()
    {
        HullDefinition hull = HullDefinitionLibrary.Get(AntegaHullDefinitionFactory.HullId);
        CargoArrangementDefinition cargo = hull.CargoArrangement!;

        Assert.Equal("Antega", hull.DisplayName);
        Assert.Equal(ShipSizeClass.Large, hull.SizeClass);
        Assert.Empty(hull.Validate());
        Assert.Equal(ShipHullRenderPath.SemanticHull, ShipMeshRenderer.SelectRenderPath(hull));
        Assert.Equal(120, cargo.ContainerCapacity);
        Assert.Contains("twelve fore/aft", cargo.Arrangement);
        Assert.Contains("five across", cargo.Arrangement);
        Assert.Contains("two high", cargo.Arrangement);
        Assert.Equal(new DVec3(12.5, 5.0, 72.0), cargo.StackBoundsMeters);
        Assert.Equal(120, cargo.ContainerPlacements.Count);
        Assert.Equal(12, DistinctCoordinates(cargo, placement => placement.CenterMeters.Z));
        Assert.Equal(5, DistinctCoordinates(cargo, placement => placement.CenterMeters.X));
        Assert.Equal(2, DistinctCoordinates(cargo, placement => placement.CenterMeters.Y));
        Assert.InRange(hull.Dimensions!.LengthMeters, 90.0, 105.0);
        Assert.InRange(hull.Dimensions.WidthMeters, 16.0, 20.0);
        Assert.InRange(hull.Dimensions.HeightMeters, 10.0, 13.0);
    }

    [Fact]
    public void CargoHatch_IsSegmentedAtForwardEndAndLoadsAft()
    {
        HullDefinition hull = HullDefinitionLibrary.Get(AntegaHullDefinitionFactory.HullId);
        SemanticHullGeometry geometry = hull.VisualGeometry!;
        SemanticAssemblyDefinition hatch = Assert.Single(geometry.Assemblies);
        SemanticHullFace hatchFace = geometry.Faces.Single(face => face.Id == hatch.FaceId);
        double hatchZ = geometry.Vertices
            .Where(vertex => hatchFace.VertexIds.Contains(vertex.Id))
            .Average(vertex => vertex.Position.Z);

        Assert.Equal("CargoDoor", hatch.Kind);
        AssertDirection(-DVec3.UnitZ, hatchFace.OutwardNormal);
        Assert.True(hatchZ < -49.5);
        Assert.True(geometry.Faces.Count(face =>
            face.Id.StartsWith($"{hatch.AssemblyId}.segment.", StringComparison.Ordinal)) >= 10);
        AssertDirection(DVec3.UnitZ, hull.CargoArrangement!.TransferAxis);
        Assert.True(hull.CargoArrangement.LoadingClearanceBoundsMeters.Min.Z < hatchZ + 0.1);
    }

    [Fact]
    public void CockpitMount_IsKeyedUpwardC5AndBuilderInstallsBridge()
    {
        HullDefinition hull = HullDefinitionLibrary.Get(AntegaHullDefinitionFactory.HullId);
        CockpitMountDefinition mount = Assert.Single(hull.CockpitMounts);
        CockpitModuleDefinition bridge =
            CockpitDefinitionLibrary.Get(mount.DefaultCockpitDefinitionId!);
        Ship ship = ShipBuilder.NewShip(AntegaHullDefinitionFactory.HullId).Build();

        Assert.Equal(CockpitMountClass.C5, mount.MountClass);
        Assert.Equal(MountFacing.Up, mount.Facing);
        Assert.Equal(new DVec3(4.0, 2.0, 6.0), mount.SocketSizeMeters);
        Assert.Equal([CockpitRotationStep.Deg0], mount.AllowedRotations);
        Assert.Equal(CockpitDefinitionLibrary.AntegaCivilianBridgeId, bridge.DefinitionId);
        Assert.Equal(CockpitMountClass.C5, bridge.RequiredMountClass);
        Assert.Equal(MountFacing.Up, bridge.PreferredFacing);
        Assert.Equal(bridge.DefinitionId, ship.Cockpit!.DefinitionId);
    }

    [Fact]
    public void BridgeCamera_IsInsideGlazingAndLooksFiveDegreesDown()
    {
        HullDefinition hull = HullDefinitionLibrary.Get(AntegaHullDefinitionFactory.HullId);
        CockpitMountDefinition mount = Assert.Single(hull.CockpitMounts);
        CockpitModuleDefinition bridge =
            CockpitDefinitionLibrary.Get(CockpitDefinitionLibrary.AntegaCivilianBridgeId);
        CockpitVisualTriangle[] canopy = bridge.VisualGeometry!.MeshParts
            .Single(part => part.Material == CockpitVisualMaterial.Canopy)
            .Triangles
            .ToArray();
        DVec3[] canopyPoints = canopy
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .ToArray();
        var installed = new InstalledCockpit
        {
            MountId = mount.MountId,
            DefinitionId = bridge.DefinitionId,
            InstallationRotation = CockpitRotationStep.Deg0,
        };
        Quaternion orientation =
            installed.ResolveShipLocalCameraOrientation(mount, bridge);
        Vector3 forward = Vector3.Normalize(
            Vector3.Transform(-Vector3.UnitZ, orientation));
        double downDegrees = Math.Atan2(-forward.Y, -forward.Z) * 180.0 / Math.PI;

        Assert.InRange(bridge.CameraLocalPosition.X, canopyPoints.Min(p => p.X), canopyPoints.Max(p => p.X));
        Assert.InRange(bridge.CameraLocalPosition.Y, canopyPoints.Min(p => p.Y), canopyPoints.Max(p => p.Y));
        Assert.InRange(bridge.CameraLocalPosition.Z, canopyPoints.Min(p => p.Z), canopyPoints.Max(p => p.Z));
        Assert.InRange(downDegrees, 4.99, 5.01);
        Assert.True(forward.Y < 0.0f);
        Assert.True(forward.Z < 0.0f);
    }

    [Fact]
    public void Atlas_IsH10EngineWithLongIndustrialEnvelopeAndAftExhaust()
    {
        EngineVariantDefinition variant =
            EngineDefinitionLibrary.GetVariant(AtlasEngineDefinitionFactory.H10VariantId);
        EngineDefinition atlas = variant.Engine;
        Bounds geometryBounds = BoundsOf(atlas.VisualGeometry!);
        EngineExhaustDefinition exhaust = Assert.Single(atlas.VisualGeometry!.Exhausts);

        Assert.Equal("H10", EngineMountStandardIds.H10);
        Assert.Equal(EngineMountStandardIds.H10, variant.MountStandardId);
        Assert.Equal("Atlas Civilian Drive", atlas.DisplayName);
        Assert.InRange(atlas.NominalEnvelopeMeters.X, 4.0, 6.0);
        Assert.InRange(atlas.NominalEnvelopeMeters.Y, 4.0, 6.0);
        Assert.InRange(atlas.NominalEnvelopeMeters.Z, 50.0, 60.0);
        Assert.InRange(geometryBounds.Size.Z, 50.0, 60.0);
        Assert.True(atlas.VisualGeometry.MeshParts.Count >= 8);
        Assert.True(exhaust.Position.Z > geometryBounds.Max.Z);
        AssertDirection(DVec3.UnitZ, exhaust.Direction);
    }

    [Fact]
    public void Builder_InstallsFourIndependentAtlasEnginesInSeparatedSidePairs()
    {
        Ship ship = ShipBuilder.NewShip(AntegaHullDefinitionFactory.HullId).Build();
        HullDefinition hull = HullDefinitionLibrary.Get(ship.HullTypeId);
        double hullMinX = -hull.Dimensions!.WidthMeters / 2.0;
        double hullMaxX = hull.Dimensions.WidthMeters / 2.0;

        Assert.Equal(4, ship.EngineMounts.Count);
        Assert.Equal(4, ship.EngineMounts.Select(mount => mount.MountId).Distinct().Count());
        Assert.Equal(4, ship.EngineMounts.Select(mount => mount.ComponentSlotId).Distinct().Count());
        Assert.Equal(4, ship.EngineMounts
            .Select(mount => mount.InstalledEngine!.InstanceId)
            .Distinct()
            .Count());
        Assert.All(ship.EngineMounts, mount =>
        {
            Assert.Equal(EngineMountStandardIds.H10, mount.MountStandardId);
            Assert.Equal(
                AtlasEngineDefinitionFactory.H10VariantId,
                mount.InstalledEngine!.Variant.VariantId);
            AssertDirection(
                DVec3.UnitZ,
                TransformDirection(
                    mount.InstalledEngine.GeometryTransform!,
                    Assert.Single(mount.InstalledEngine.Variant.Engine.VisualGeometry!.Exhausts)
                        .Direction));
        });

        EngineMount[] port = ship.EngineMounts
            .Where(mount => mount.Side == EngineMountSide.Port)
            .OrderByDescending(mount => mount.Pose.Position.Y)
            .ToArray();
        EngineMount[] starboard = ship.EngineMounts
            .Where(mount => mount.Side == EngineMountSide.Starboard)
            .OrderByDescending(mount => mount.Pose.Position.Y)
            .ToArray();
        Assert.Equal(2, port.Length);
        Assert.Equal(2, starboard.Length);
        Assert.True(port[0].Pose.Position.Y > port[1].Pose.Position.Y);
        Assert.True(starboard[0].Pose.Position.Y > starboard[1].Pose.Position.Y);
        Assert.All(port, mount =>
            Assert.True(MaxEngineX(mount) < hullMinX - 2.5));
        Assert.All(starboard, mount =>
            Assert.True(MinEngineX(mount) > hullMaxX + 2.5));
    }

    [Fact]
    public void CompositeBoundsAndSnapshotIncludeBridgeAndAllFourEngines()
    {
        Ship ship = ShipBuilder.NewShip(AntegaHullDefinitionFactory.HullId).Build();
        HullDefinition hull = HullDefinitionLibrary.Get(ship.HullTypeId);
        ShipPresentationBounds bounds = ShipPresentationBoundsCalculator.Calculate(ship);
        double hullMinX = hull.VisualGeometry!.Vertices.Min(vertex => vertex.Position.X);
        double hullMaxX = hull.VisualGeometry.Vertices.Max(vertex => vertex.Position.X);
        double hullMaxY = hull.VisualGeometry.Vertices.Max(vertex => vertex.Position.Y);
        var simulation = new SpaceSimulation();
        simulation.SetShip(ship);

        simulation.TickForTests(
            PlayerInput.Zero with { ThrustForward = 0.6 },
            1.0 / 60.0);

        SpaceSimulation.ShipSnapshot snapshot = simulation.ShipState!;
        Assert.True(bounds.Min.X < hullMinX);
        Assert.True(bounds.Max.X > hullMaxX);
        Assert.True(bounds.Max.Y > hullMaxY);
        Assert.InRange(bounds.Size.X, 33.0, 34.1);
        Assert.True(bounds.Size.Z >= 99.0);
        Assert.Equal(bounds, snapshot.PresentationBounds);
        Assert.Equal(4, snapshot.EngineMounts!.Count);
        Assert.Equal(4, snapshot.EngineMounts
            .Select(mount => mount.InstalledEngine!.InstanceId)
            .Distinct()
            .Count());
        Assert.Equal(CockpitDefinitionLibrary.AntegaCivilianBridgeId, snapshot.Cockpit!.DefinitionId);

        var chase = new ChaseCameraState();
        chase.ApplyPresentationBounds(snapshot.PresentationBounds);
        Assert.True(chase.MinimumFramingRadius > 100.0);
        Assert.Equal(chase.MinimumFramingRadius, chase.Radius, 6);
    }

    [Fact]
    public void ActualBridgePose_ProjectsShipForwardReticleAboveCentre()
    {
        Ship ship = ShipBuilder.NewShip(AntegaHullDefinitionFactory.HullId).Build();
        const int width = 1920;
        const int height = 1080;
        var viewport = new Viewport(0, 0, width, height);
        Matrix view = Matrix.CreateLookAt(
            Vector3.Zero,
            Vector3.Transform(-Vector3.UnitZ, ship.CockpitWorldOrientation),
            Vector3.Transform(Vector3.UnitY, ship.CockpitWorldOrientation));
        Matrix projection = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.ToRadians(60.0f),
            (float)width / height,
            0.001f,
            50_000.0f);

        ShipForwardReticleProjection result = ShipForwardReticleProjector.Project(
            ship.CockpitWorldPosition,
            ship.Orientation,
            view,
            projection,
            viewport)!.Value;

        Assert.True(result.ScreenPosition.Y < height / 2.0f);
        Assert.InRange(result.ScreenPosition.X, width / 2.0f - 0.01f, width / 2.0f + 0.01f);
        Assert.False(result.IsClampedToViewport);
    }

    private static int DistinctCoordinates(
        CargoArrangementDefinition cargo,
        Func<CargoContainerPlacementDefinition, double> selector)
        => cargo.ContainerPlacements.Select(selector).Distinct().Count();

    private static Bounds BoundsOf(EngineVisualGeometry geometry)
    {
        DVec3[] points = geometry.MeshParts
            .SelectMany(part => part.Triangles)
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .ToArray();
        return new Bounds(
            new DVec3(
                points.Min(point => point.X),
                points.Min(point => point.Y),
                points.Min(point => point.Z)),
            new DVec3(
                points.Max(point => point.X),
                points.Max(point => point.Y),
                points.Max(point => point.Z)));
    }

    private static double MaxEngineX(EngineMount mount)
        => EnginePoints(mount).Max(point => point.X);

    private static double MinEngineX(EngineMount mount)
        => EnginePoints(mount).Min(point => point.X);

    private static IEnumerable<DVec3> EnginePoints(EngineMount mount)
    {
        EngineInstance engine = mount.InstalledEngine!;
        EngineGeometryTransform transform = engine.GeometryTransform!;
        return engine.Variant.Engine.VisualGeometry!.MeshParts
            .SelectMany(part => part.Triangles)
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .Select(transform.TransformVisualPoint);
    }

    private static DVec3 TransformDirection(
        EngineGeometryTransform transform,
        DVec3 direction)
    {
        DVec3 corrected = transform.MirroredAcrossHullX
            ? new DVec3(-direction.X, direction.Y, direction.Z)
            : direction;
        Vector3 transformed = Vector3.TransformNormal(
            corrected.ToVector3(),
            transform.LocalToHull);
        return new DVec3(transformed.X, transformed.Y, transformed.Z).Normalized();
    }

    private static void AssertDirection(DVec3 expected, DVec3 actual)
    {
        Assert.InRange(Math.Abs(actual.X - expected.X), 0.0, 1e-5);
        Assert.InRange(Math.Abs(actual.Y - expected.Y), 0.0, 1e-5);
        Assert.InRange(Math.Abs(actual.Z - expected.Z), 0.0, 1e-5);
    }

    private readonly record struct Bounds(DVec3 Min, DVec3 Max)
    {
        public DVec3 Size => Max - Min;
    }
}
