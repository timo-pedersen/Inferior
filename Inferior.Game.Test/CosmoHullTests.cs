using Inferior.Core.Math;
using Inferior.Game;
using Inferior.Gameplay;
using Inferior.Game.Ships;
using Inferior.Game.UI;
using Inferior.Gameplay.Cockpit;
using Inferior.Gameplay.Engines;
using Inferior.Gameplay.Hull;
using Inferior.Gameplay.Ship;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace Inferior.Game.Test;

public sealed class CosmoHullTests
{
    [Fact]
    public void Definition_IsValidSmallNoCargoSportHull()
    {
        HullDefinition hull = HullDefinitionLibrary.Get(CosmoHullDefinitionFactory.HullId);

        Assert.Equal("Cosmo", hull.DisplayName);
        Assert.Equal(ShipSizeClass.Small, hull.SizeClass);
        Assert.Equal(12_000.0, hull.HullMass);
        Assert.Empty(hull.Validate());
        Assert.NotNull(hull.VisualGeometry);
        Assert.Equal(ShipHullRenderPath.SemanticHull, ShipMeshRenderer.SelectRenderPath(hull));
        Assert.Equal(0, hull.CargoArrangement!.ContainerCapacity);
        Assert.Empty(hull.CargoArrangement.ContainerPlacements);
        Assert.Equal(8.0, hull.Dimensions!.LengthMeters);
        Assert.InRange(hull.Dimensions.WidthMeters, 2.8, 3.2);
        Assert.InRange(hull.Dimensions.HeightMeters, 2.0, 2.2);
    }

    [Fact]
    public void CockpitMount_IsCenteredTopC1WithCompatibleDefinition()
    {
        HullDefinition hull = HullDefinitionLibrary.Get(CosmoHullDefinitionFactory.HullId);
        CockpitMountDefinition mount = Assert.Single(hull.CockpitMounts);
        CockpitModuleDefinition definition =
            CockpitDefinitionLibrary.Get(mount.DefaultCockpitDefinitionId!);

        Assert.Equal(CockpitMountClass.C1, mount.MountClass);
        Assert.Equal(MountFacing.Up, mount.Facing);
        Assert.Equal(new DVec3(1.0, 1.0, 1.0), mount.SocketSizeMeters);
        Assert.Equal(0.0, mount.ShipLocalPosition.X);
        Assert.True(mount.ShipLocalPosition.Y > 0.0);
        Assert.Equal(CockpitDefinitionLibrary.CosmoC1SportCockpitId, definition.DefinitionId);
        Assert.Equal(mount.MountClass, definition.RequiredMountClass);
        Assert.Equal(MountFacing.Up, definition.PreferredFacing);
        Assert.NotNull(definition.VisualGeometry);
        AssertCameraInsideCockpit(definition);
    }

    [Fact]
    public void DorsalNeedleMount_IsAboveBehindCockpitAndPointsShipForward()
    {
        HullDefinition hull = HullDefinitionLibrary.Get(CosmoHullDefinitionFactory.HullId);
        AttachmentPortDefinition enginePort = Assert.Single(
            hull.VisualGeometry!.AttachmentPorts,
            port => port.Capabilities.HasFlag(AttachmentCapability.Engine));
        CockpitMountDefinition cockpitMount = Assert.Single(hull.CockpitMounts);

        Assert.Equal(EngineMountSide.Starboard, enginePort.EngineMountSide);
        Assert.True(enginePort.Position.Y > cockpitMount.ShipLocalPosition.Y + 1.5);
        Assert.True(enginePort.Position.Z > cockpitMount.ShipLocalPosition.Z);
        AssertDirection(DVec3.UnitY, enginePort.Normal.ToVector3());
        AssertDirection(-DVec3.UnitX, enginePort.Up.ToVector3());

        var pose = new EngineMountPose(enginePort.Position, enginePort.Normal, enginePort.Up);
        Vector3 thrustDirection = Vector3.Transform(-Vector3.UnitZ, pose.Orientation);
        Vector3 exhaustDirection = Vector3.Transform(Vector3.UnitZ, pose.Orientation);
        AssertDirection(-DVec3.UnitZ, thrustDirection);
        AssertDirection(DVec3.UnitZ, exhaustDirection);
    }

    [Fact]
    public void Builder_InstallsOneDefaultNeedleAndCosmoCockpit()
    {
        var ship = ShipBuilder.NewShip(CosmoHullDefinitionFactory.HullId).Build();

        Assert.Equal(CosmoHullDefinitionFactory.HullId, ship.HullTypeId);
        Assert.Equal(CockpitDefinitionLibrary.CosmoC1SportCockpitId, ship.Cockpit!.DefinitionId);
        EngineMount mount = Assert.Single(ship.EngineMounts);
        Assert.Equal(EngineMountSide.Starboard, mount.Side);
        Assert.Equal(NeedleEngineDefinitionFactory.H2VariantId, mount.InstalledEngine!.Variant.VariantId);
        Assert.False(mount.InstalledEngine.GeometryTransform!.MirroredAcrossHullX);

        DVec3 actualInterface = mount.InstalledEngine.GeometryTransform.TransformVisualPoint(
            mount.InstalledEngine.Variant.Engine.VisualGeometry!.AttachmentInterfacePosition);
        AssertDVec3(mount.AttachmentInterfacePosition!.Value, actualInterface);
    }

    [Fact]
    public void ActualCockpitPose_ProjectsShipForwardReticleAtCentre()
    {
        var ship = ShipBuilder.NewShip(CosmoHullDefinitionFactory.HullId).Build();
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

        Assert.InRange(result.ScreenPosition.X, width / 2.0f - 0.01f, width / 2.0f + 0.01f);
        Assert.InRange(result.ScreenPosition.Y, height / 2.0f - 0.01f, height / 2.0f + 0.01f);
        Assert.False(result.IsClampedToViewport);
    }

    private static void AssertCameraInsideCockpit(CockpitModuleDefinition definition)
    {
        DVec3 camera = definition.CameraLocalPosition;
        DVec3[] points = definition.VisualGeometry!.MeshParts
            .SelectMany(part => part.Triangles)
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .ToArray();
        Assert.InRange(camera.X, points.Min(point => point.X), points.Max(point => point.X));
        Assert.InRange(camera.Y, points.Min(point => point.Y), points.Max(point => point.Y));
        Assert.InRange(camera.Z, points.Min(point => point.Z), points.Max(point => point.Z));
    }

    private static void AssertDVec3(DVec3 expected, DVec3 actual, double tolerance = 1e-5)
    {
        Assert.InRange(Math.Abs(actual.X - expected.X), 0.0, tolerance);
        Assert.InRange(Math.Abs(actual.Y - expected.Y), 0.0, tolerance);
        Assert.InRange(Math.Abs(actual.Z - expected.Z), 0.0, tolerance);
    }

    private static void AssertDirection(DVec3 expected, Vector3 actual)
    {
        Assert.InRange(Math.Abs(actual.X - expected.X), 0.0, 1e-5);
        Assert.InRange(Math.Abs(actual.Y - expected.Y), 0.0, 1e-5);
        Assert.InRange(Math.Abs(actual.Z - expected.Z), 0.0, 1e-5);
    }
}
