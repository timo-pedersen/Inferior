using Inferior.Core.Math;
using Inferior.Game;
using Inferior.Game.Ships;
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

public sealed class BerenHullTests
{
    [Fact]
    public void Definition_IsValidMediumNineContainerHull()
    {
        HullDefinition hull = HullDefinitionLibrary.Get(BerenHullDefinitionFactory.HullId);

        Assert.Equal("Beren", hull.DisplayName);
        Assert.Equal(ShipSizeClass.Medium, hull.SizeClass);
        Assert.Empty(hull.Validate());
        Assert.Equal(ShipHullRenderPath.SemanticHull, ShipMeshRenderer.SelectRenderPath(hull));
        Assert.Equal(9, hull.CargoArrangement!.ContainerCapacity);
        Assert.Equal(new DVec3(7.5, 2.5, 18.0), hull.CargoArrangement.StackBoundsMeters);
        Assert.Equal(9, hull.CargoArrangement.ContainerPlacements.Count);
        Assert.Equal(3, hull.CargoArrangement.ContainerPlacements
            .Select(placement => placement.CenterMeters.X)
            .Distinct()
            .Count());
        Assert.Equal(3, hull.CargoArrangement.ContainerPlacements
            .Select(placement => placement.CenterMeters.Z)
            .Distinct()
            .Count());
        Assert.InRange(hull.Dimensions!.LengthMeters, 25.0, 28.0);
        Assert.InRange(hull.Dimensions.WidthMeters, 17.0, 21.0);
        Assert.InRange(hull.Dimensions.HeightMeters, 6.0, 7.0);

        SemanticAssemblyDefinition door = Assert.Single(hull.VisualGeometry!.Assemblies);
        Assert.Equal("CargoDoor", door.Kind);
        AssertDirection(
            DVec3.UnitZ,
            hull.VisualGeometry.Faces.Single(face => face.Id == door.FaceId)
                .OutwardNormal.ToVector3());
    }

    [Fact]
    public void CockpitMount_IsDownwardC2WithCompatibleFullPod()
    {
        HullDefinition hull = HullDefinitionLibrary.Get(BerenHullDefinitionFactory.HullId);
        CockpitMountDefinition mount = Assert.Single(hull.CockpitMounts);
        CockpitModuleDefinition definition =
            CockpitDefinitionLibrary.Get(mount.DefaultCockpitDefinitionId!);

        Assert.Equal(CockpitMountClass.C2, mount.MountClass);
        Assert.Equal(MountFacing.Down, mount.Facing);
        Assert.True(mount.ShipLocalPosition.Y < 0.0);
        AssertDirection(
            -DVec3.UnitY,
            Vector3.Transform(Vector3.UnitY, mount.ShipLocalOrientation));
        AssertDirection(
            -DVec3.UnitZ,
            Vector3.Transform(-Vector3.UnitZ, mount.ShipLocalOrientation));
        Assert.Equal(CockpitDefinitionLibrary.BerenUnderslungCockpitId, definition.DefinitionId);
        Assert.Equal(MountFacing.Down, definition.PreferredFacing);
        Assert.Equal(mount.MountClass, definition.RequiredMountClass);
        Assert.NotNull(definition.VisualGeometry);

        DVec3[] geometryPoints = definition.VisualGeometry!.MeshParts
            .SelectMany(part => part.Triangles)
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .ToArray();
        Assert.True(geometryPoints.Max(point => point.Y) - geometryPoints.Min(point => point.Y) >= 2.0);
        Assert.InRange(
            definition.CameraLocalPosition.Y,
            geometryPoints.Min(point => point.Y),
            geometryPoints.Max(point => point.Y));
    }

    [Fact]
    public void Camera_LooksForwardAndTenDegreesDown()
    {
        HullDefinition hull = HullDefinitionLibrary.Get(BerenHullDefinitionFactory.HullId);
        CockpitMountDefinition mount = Assert.Single(hull.CockpitMounts);
        CockpitModuleDefinition definition =
            CockpitDefinitionLibrary.Get(mount.DefaultCockpitDefinitionId!);
        var installed = new InstalledCockpit
        {
            MountId = mount.MountId,
            DefinitionId = definition.DefinitionId,
            InstallationRotation = CockpitRotationStep.Deg0,
        };

        Quaternion orientation =
            installed.ResolveShipLocalCameraOrientation(mount, definition);
        Vector3 forward = Vector3.Normalize(
            Vector3.Transform(-Vector3.UnitZ, orientation));
        Vector3 up = Vector3.Normalize(
            Vector3.Transform(Vector3.UnitY, orientation));
        double downDegrees = Math.Atan2(-forward.Y, -forward.Z) * 180.0 / Math.PI;

        Assert.InRange(downDegrees, 9.99, 10.01);
        Assert.True(forward.Y < 0.0f);
        Assert.True(forward.Z < 0.0f);
        Assert.InRange(Math.Abs(forward.X), 0.0f, 1e-5f);
        Assert.True(up.Y > 0.98f);
    }

    [Fact]
    public void Builder_InstallsFourUniqueDefaultNeedlesInVerticalSidePairs()
    {
        var ship = ShipBuilder.NewShip(BerenHullDefinitionFactory.HullId).Build();

        Assert.Equal(CockpitDefinitionLibrary.BerenUnderslungCockpitId, ship.Cockpit!.DefinitionId);
        Assert.Equal(4, ship.EngineMounts.Count);
        Assert.Equal(4, ship.EngineMounts.Select(mount => mount.MountId).Distinct().Count());
        Assert.Equal(4, ship.EngineMounts.Select(mount => mount.ComponentSlotId).Distinct().Count());
        Assert.Equal(4, ship.EngineMounts
            .Select(mount => mount.InstalledEngine!.InstanceId)
            .Distinct()
            .Count());
        Assert.All(ship.EngineMounts, mount =>
            Assert.Equal(
                NeedleEngineDefinitionFactory.H2VariantId,
                mount.InstalledEngine!.Variant.VariantId));

        EngineMount[] port = ship.EngineMounts.Where(mount =>
            mount.Side == EngineMountSide.Port).OrderByDescending(mount => mount.Pose.Position.Y).ToArray();
        EngineMount[] starboard = ship.EngineMounts.Where(mount =>
            mount.Side == EngineMountSide.Starboard).OrderByDescending(mount => mount.Pose.Position.Y).ToArray();
        Assert.Equal(2, port.Length);
        Assert.Equal(2, starboard.Length);
        Assert.True(port.All(mount => mount.Pose.Position.X < 0.0));
        Assert.True(starboard.All(mount => mount.Pose.Position.X > 0.0));
        Assert.True(port[0].Pose.Position.Y > port[1].Pose.Position.Y);
        Assert.True(starboard[0].Pose.Position.Y > starboard[1].Pose.Position.Y);

        foreach (EngineMount mount in ship.EngineMounts)
        {
            DVec3 actualInterface = mount.InstalledEngine!.GeometryTransform!.TransformVisualPoint(
                mount.InstalledEngine.Variant.Engine.VisualGeometry!.AttachmentInterfacePosition);
            AssertDVec3(mount.AttachmentInterfacePosition!.Value, actualInterface);
        }
    }

    [Fact]
    public void ResolvedTransforms_RemainRigidThroughYawPitchAndRoll()
    {
        DVec3 position = new(9050.0, -625.0, 42100.0);
        Quaternion orientation =
            Quaternion.CreateFromYawPitchRoll(0.82f, -0.31f, 0.47f);
        var ship = ShipBuilder.NewShip(BerenHullDefinitionFactory.HullId)
            .WithPosition(position)
            .WithOrientation(orientation)
            .Build();
        HullDefinition hull = HullDefinitionLibrary.Get(ship.HullTypeId);
        CockpitMountDefinition mount = Assert.Single(hull.CockpitMounts);
        CockpitModuleDefinition definition =
            CockpitDefinitionLibrary.Get(ship.Cockpit!.DefinitionId);
        DVec3 localCamera =
            ship.Cockpit.ResolveShipLocalCameraPosition(mount, definition);
        Quaternion localCameraOrientation =
            ship.Cockpit.ResolveShipLocalCameraOrientation(mount, definition);

        Vector3 expectedOffset = Vector3.Transform(localCamera.ToVector3(), orientation);
        AssertDVec3(position + ToDVec3(expectedOffset), ship.CockpitWorldPosition);
        AssertQuaternion(
            Quaternion.Normalize(orientation * localCameraOrientation),
            ship.CockpitWorldOrientation);

        EngineGeometryTransform[] engineTransforms = ship.EngineMounts
            .Select(engineMount => engineMount.InstalledEngine!.GeometryTransform!)
            .ToArray();
        ship.SetOrientation(Quaternion.CreateFromYawPitchRoll(-0.55f, 0.22f, -0.63f));
        Assert.Equal(
            engineTransforms,
            ship.EngineMounts.Select(engineMount =>
                engineMount.InstalledEngine!.GeometryTransform));
    }

    [Fact]
    public void Snapshot_PublishesCockpitAndFourIndependentEngines()
    {
        var simulation = new SpaceSimulation();
        var ship = ShipBuilder.NewShip(BerenHullDefinitionFactory.HullId).Build();
        simulation.SetShip(ship);

        simulation.TickForTests(
            PlayerInput.Zero with { ThrustLateral = 0.75 },
            1.0 / 60.0);

        SpaceSimulation.ShipSnapshot snapshot = simulation.ShipState!;
        Assert.Equal(BerenHullDefinitionFactory.HullId, snapshot.HullTypeId);
        Assert.Equal(4, snapshot.EngineMounts!.Count);
        Assert.Equal(4, snapshot.EngineMounts
            .Select(mount => mount.InstalledEngine!.InstanceId)
            .Distinct()
            .Count());
        Assert.All(snapshot.EngineMounts, mount =>
            Assert.Equal(EngineVisualMode.Thrust, mount.InstalledEngine!.VisualState.Mode));
        Assert.Equal(CockpitDefinitionLibrary.BerenUnderslungCockpitId, snapshot.Cockpit!.DefinitionId);
    }

    [Fact]
    public void ActualCockpitPose_ProjectsShipForwardReticleAboveCentre()
    {
        var ship = ShipBuilder.NewShip(BerenHullDefinitionFactory.HullId).Build();
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

    private static void AssertDirection(DVec3 expected, Vector3 actual)
    {
        Assert.InRange(Math.Abs(actual.X - expected.X), 0.0, 1e-5);
        Assert.InRange(Math.Abs(actual.Y - expected.Y), 0.0, 1e-5);
        Assert.InRange(Math.Abs(actual.Z - expected.Z), 0.0, 1e-5);
    }

    private static void AssertDVec3(DVec3 expected, DVec3 actual)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0.0, 1e-5);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0.0, 1e-5);
        Assert.InRange(Math.Abs(expected.Z - actual.Z), 0.0, 1e-5);
    }

    private static void AssertQuaternion(Quaternion expected, Quaternion actual)
    {
        float dot = Math.Abs(Quaternion.Dot(expected, actual));
        Assert.InRange(dot, 0.99999f, 1.00001f);
    }

    private static DVec3 ToDVec3(Vector3 value)
        => new(value.X, value.Y, value.Z);
}
