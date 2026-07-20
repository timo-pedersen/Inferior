using Inferior.Core.Math;
using Inferior.Game;
using Inferior.Game.Ships;
using Inferior.Game.UI;
using Inferior.Gameplay;
using Inferior.Gameplay.Cockpit;
using Inferior.Gameplay.Engines;
using Inferior.Gameplay.Hull;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace Inferior.Game.Test;

public sealed class AsteriskHullTests
{
    [Fact]
    public void Definition_IsValidCompactOneContainerHull()
    {
        HullDefinition hull = HullDefinitionLibrary.Get(AsteriskHullDefinitionFactory.HullId);

        Assert.Equal("Asterisk", hull.DisplayName);
        Assert.Empty(hull.Validate());
        Assert.NotNull(hull.VisualGeometry);
        Assert.Equal(ShipHullRenderPath.SemanticHull, ShipMeshRenderer.SelectRenderPath(hull));
        Assert.Equal(1, hull.CargoArrangement!.ContainerCapacity);
        Assert.Equal(new DVec3(2.5, 2.5, 6.0), hull.CargoArrangement.StackBoundsMeters);
        Assert.Single(hull.CargoArrangement.ContainerPlacements);
        Assert.InRange(hull.Dimensions!.LengthMeters, 8.0, 9.0);
        Assert.InRange(hull.Dimensions.WidthMeters, 4.0, 6.0);
        Assert.InRange(hull.Dimensions.HeightMeters, 3.0, 3.5);

        SemanticAssemblyDefinition door = Assert.Single(hull.VisualGeometry!.Assemblies);
        Assert.Equal("CargoDoor", door.Kind);
        Assert.StartsWith("asterisk.front.cargo-door", door.AssemblyId);
        AssertDirection(
            -DVec3.UnitZ,
            hull.VisualGeometry.Faces.Single(face => face.Id == door.FaceId)
                .OutwardNormal.ToVector3());
    }

    [Fact]
    public void CockpitMount_IsStarboardC2WithCompatibleDefinition()
    {
        HullDefinition hull = HullDefinitionLibrary.Get(AsteriskHullDefinitionFactory.HullId);
        CockpitMountDefinition mount = Assert.Single(hull.CockpitMounts);
        CockpitModuleDefinition definition =
            CockpitDefinitionLibrary.Get(mount.DefaultCockpitDefinitionId!);

        Assert.Equal(CockpitMountClass.C2, mount.MountClass);
        Assert.Equal(MountFacing.Starboard, mount.Facing);
        Assert.True(mount.ShipLocalPosition.X > 0.0);
        AssertDirection(
            DVec3.UnitX,
            Vector3.Transform(Vector3.UnitY, mount.ShipLocalOrientation));
        AssertDirection(
            -DVec3.UnitZ,
            Vector3.Transform(-Vector3.UnitZ, mount.ShipLocalOrientation));
        Assert.Equal(CockpitDefinitionLibrary.AsteriskStarboardCockpitId, definition.DefinitionId);
        Assert.Equal(mount.MountClass, definition.RequiredMountClass);
        Assert.Equal(MountFacing.Starboard, definition.PreferredFacing);
        Assert.NotNull(definition.VisualGeometry);
    }

    [Fact]
    public void Camera_LooksForwardAndThirtyDegreesTowardStarboard()
    {
        HullDefinition hull = HullDefinitionLibrary.Get(AsteriskHullDefinitionFactory.HullId);
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
        double yawDegrees = Math.Atan2(forward.X, -forward.Z) * 180.0 / Math.PI;

        Assert.InRange(yawDegrees, 29.99, 30.01);
        Assert.True(forward.X > 0.0f);
        Assert.True(forward.Z < 0.0f);
        Assert.InRange(Math.Abs(forward.Y), 0.0f, 1e-5f);
        Assert.InRange(Vector3.Dot(up, Vector3.UnitY), 0.99999f, 1.00001f);
        AssertCameraInsideCockpit(definition);
    }

    [Fact]
    public void EngineAndCockpit_OccupyOppositePhysicalSides()
    {
        HullDefinition hull = HullDefinitionLibrary.Get(AsteriskHullDefinitionFactory.HullId);
        AttachmentPortDefinition enginePort = Assert.Single(
            hull.VisualGeometry!.AttachmentPorts,
            port => port.Capabilities.HasFlag(AttachmentCapability.Engine));
        CockpitMountDefinition cockpitMount = Assert.Single(hull.CockpitMounts);

        Assert.Equal(EngineMountSide.Port, enginePort.EngineMountSide);
        Assert.True(enginePort.Position.X < 0.0);
        Assert.True(cockpitMount.ShipLocalPosition.X > 0.0);

        // From a front camera looking aft, +X appears viewer-left.
        Assert.True(cockpitMount.ShipLocalPosition.X > enginePort.Position.X);
    }

    [Fact]
    public void Builder_InstallsOneDefaultMuleAndAsteriskCockpit()
    {
        var ship = ShipBuilder.NewShip(AsteriskHullDefinitionFactory.HullId).Build();

        Assert.Equal(AsteriskHullDefinitionFactory.HullId, ship.HullTypeId);
        Assert.Equal(CockpitDefinitionLibrary.AsteriskStarboardCockpitId, ship.Cockpit!.DefinitionId);
        EngineMount mount = Assert.Single(ship.EngineMounts);
        Assert.Equal(EngineMountSide.Port, mount.Side);
        Assert.Equal(MuleEngineDefinitionFactory.H2VariantId, mount.InstalledEngine!.Variant.VariantId);
        Assert.True(mount.InstalledEngine.GeometryTransform!.MirroredAcrossHullX);

        DVec3 actualInterface = mount.InstalledEngine.GeometryTransform.TransformVisualPoint(
            mount.InstalledEngine.Variant.Engine.VisualGeometry!.AttachmentInterfacePosition);
        AssertDVec3(mount.AttachmentInterfacePosition!.Value, actualInterface);
    }

    [Fact]
    public void ResolvedModuleTransforms_RemainRigidThroughArbitraryShipRotation()
    {
        DVec3 worldPosition = new(8125.0, -419.0, 22000.0);
        Quaternion shipOrientation =
            Quaternion.CreateFromYawPitchRoll(0.71f, -0.43f, 0.29f);
        var ship = ShipBuilder.NewShip(AsteriskHullDefinitionFactory.HullId)
            .WithPosition(worldPosition)
            .WithOrientation(shipOrientation)
            .Build();
        HullDefinition hull = HullDefinitionLibrary.Get(ship.HullTypeId);
        CockpitMountDefinition cockpitMount = Assert.Single(hull.CockpitMounts);
        CockpitModuleDefinition cockpitDefinition =
            CockpitDefinitionLibrary.Get(ship.Cockpit!.DefinitionId);
        DVec3 localCamera = ship.Cockpit.ResolveShipLocalCameraPosition(
            cockpitMount,
            cockpitDefinition);
        Quaternion localCameraOrientation =
            ship.Cockpit.ResolveShipLocalCameraOrientation(cockpitMount, cockpitDefinition);

        Vector3 expectedCameraOffset =
            Vector3.Transform(localCamera.ToVector3(), shipOrientation);
        AssertDVec3(
            worldPosition + ToDVec3(expectedCameraOffset),
            ship.CockpitWorldPosition);
        AssertQuaternion(
            Quaternion.Normalize(shipOrientation * localCameraOrientation),
            ship.CockpitWorldOrientation);

        EngineMount engineMount = Assert.Single(ship.EngineMounts);
        EngineGeometryTransform before = engineMount.InstalledEngine!.GeometryTransform!;
        ship.SetOrientation(Quaternion.CreateFromYawPitchRoll(-0.28f, 0.36f, -0.52f));
        Assert.Same(before, engineMount.InstalledEngine.GeometryTransform);
        Assert.Equal(engineMount.Pose.Position, before.Position);
    }

    [Fact]
    public void Snapshot_PublishesOneInstalledEngineAndCockpit()
    {
        var simulation = new SpaceSimulation();
        var ship = ShipBuilder.NewShip(AsteriskHullDefinitionFactory.HullId).Build();
        simulation.SetShip(ship);

        simulation.TickForTests(
            PlayerInput.Zero with { ThrustForward = 1.0 },
            1.0 / 60.0);

        SpaceSimulation.ShipSnapshot snapshot = simulation.ShipState!;
        Assert.Equal(AsteriskHullDefinitionFactory.HullId, snapshot.HullTypeId);
        EngineMountPresentationSnapshot engine = Assert.Single(snapshot.EngineMounts!);
        Assert.Equal(MuleEngineDefinitionFactory.H2VariantId, engine.InstalledEngine!.VariantId);
        Assert.Equal(EngineVisualMode.Thrust, engine.InstalledEngine.VisualState.Mode);
        Assert.Equal(CockpitDefinitionLibrary.AsteriskStarboardCockpitId, snapshot.Cockpit!.DefinitionId);
        AssertDVec3(ship.CockpitRootWorldPosition, snapshot.Cockpit.WorldPosition);
    }

    [Fact]
    public void ActualCockpitPose_ProjectsShipForwardReticleLeftOfCentre()
    {
        var ship = ShipBuilder.NewShip(AsteriskHullDefinitionFactory.HullId).Build();
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

        Assert.True(result.ScreenPosition.X < width / 2.0f);
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
