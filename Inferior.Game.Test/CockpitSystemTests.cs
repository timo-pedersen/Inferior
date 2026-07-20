using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Game;
using Inferior.Game.Ships;
using Inferior.Gameplay;
using Inferior.Gameplay.Cockpit;
using Inferior.Gameplay.Hull;
using Inferior.Persistence.Data;
using Microsoft.Xna.Framework;
using System.Text.Json;
using Xunit;

namespace Inferior.Game.Test;

public sealed class CockpitSystemTests
{
    [Fact]
    public void Aries_HasOneCompatibleDefaultCockpitMount()
    {
        HullDefinition hull = HullDefinitionLibrary.Get("type-1");
        CockpitMountDefinition mount = Assert.Single(hull.CockpitMounts);
        CockpitModuleDefinition definition =
            CockpitDefinitionLibrary.Get(mount.DefaultCockpitDefinitionId!);

        Assert.Equal("type-1.cockpit.top.01", mount.MountId);
        Assert.Equal(CockpitMountClass.C2, mount.MountClass);
        Assert.Equal(MountFacing.Up, mount.Facing);
        Assert.Equal(new DVec3(1.5, 1.5, 1.0), mount.SocketSizeMeters);
        Assert.Equal(mount.MountClass, definition.RequiredMountClass);
        Assert.Empty(hull.Validate());
    }

    [Fact]
    public void ShipBuilder_InstallsAriesDefaultCockpit()
    {
        var ship = ShipBuilder.NewShip("type-1").Build();

        Assert.NotNull(ship.Cockpit);
        Assert.Equal("type-1.cockpit.top.01", ship.Cockpit.MountId);
        Assert.Equal(CockpitDefinitionLibrary.AriesCivilianCanopyId, ship.Cockpit.DefinitionId);
        Assert.Equal(CockpitRotationStep.Deg0, ship.Cockpit.InstallationRotation);
    }

    [Fact]
    public void AriesDefaultCockpit_ResolvesAuthoredPoseThroughShipTransform()
    {
        var worldPosition = new DVec3(100.0, -20.0, 50.0);
        Quaternion shipOrientation = Quaternion.CreateFromYawPitchRoll(0.4f, -0.2f, 0.1f);
        var ship = ShipBuilder.NewShip("type-1")
            .WithPosition(worldPosition)
            .WithOrientation(shipOrientation)
            .Build();
        var oldPose = HullDefinitionLibrary.Get("type-1").CockpitPose;

        Vector3 expectedOffset = Vector3.Transform(oldPose.Position.ToVector3(), shipOrientation);
        var expectedPosition = worldPosition
            + new DVec3(expectedOffset.X, expectedOffset.Y, expectedOffset.Z);
        Quaternion expectedOrientation =
            Quaternion.Normalize(shipOrientation * oldPose.Orientation);

        AssertDVec3(expectedPosition, ship.CockpitWorldPosition);
        AssertQuaternion(expectedOrientation, ship.CockpitWorldOrientation);
    }

    [Fact]
    public void InstalledCockpit_AppliesInstallationRotationBeforeMountOrientation()
    {
        var cockpit = new InstalledCockpit
        {
            MountId = "test",
            DefinitionId = "test-cockpit",
            InstallationRotation = CockpitRotationStep.Deg90,
        };
        Quaternion mountOrientation =
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathHelper.PiOver2);
        var mount = new CockpitMountDefinition
        {
            MountId = "test",
            MountClass = CockpitMountClass.C2,
            ShipLocalPosition = new DVec3(2.0, 3.0, 4.0),
            ShipLocalOrientation = mountOrientation,
            SocketSizeMeters = new DVec3(1.5, 1.5, 1.0),
            Facing = MountFacing.Up,
            AllowedRotations = new HashSet<CockpitRotationStep> { CockpitRotationStep.Deg90 },
        };
        var definition = new CockpitModuleDefinition
        {
            DefinitionId = "test-cockpit",
            DisplayName = "Test Cockpit",
            RequiredMountClass = CockpitMountClass.C2,
            PilotLocalPosition = DVec3.Zero,
            PilotLocalOrientation = Quaternion.Identity,
            CameraLocalPosition = DVec3.UnitX,
            CameraLocalOrientation = Quaternion.Identity,
        };

        Vector3 afterInstallation = Vector3.Transform(
            Vector3.UnitX,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathHelper.PiOver2));
        Vector3 afterMount = Vector3.Transform(afterInstallation, mountOrientation);
        DVec3 expected = mount.ShipLocalPosition
            + new DVec3(afterMount.X, afterMount.Y, afterMount.Z);

        AssertDVec3(expected, cockpit.ResolveShipLocalCameraPosition(mount, definition));
    }

    [Fact]
    public void InstalledCockpit_RootPoseDoesNotUseCameraChildTransform()
    {
        var cockpit = new InstalledCockpit
        {
            MountId = "test",
            DefinitionId = "test-cockpit",
            InstallationRotation = CockpitRotationStep.Deg90,
        };
        Quaternion mountOrientation =
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathHelper.PiOver2);
        var mount = new CockpitMountDefinition
        {
            MountId = "test",
            MountClass = CockpitMountClass.C2,
            ShipLocalPosition = new DVec3(4.0, 5.0, 6.0),
            ShipLocalOrientation = mountOrientation,
            SocketSizeMeters = new DVec3(1.5, 1.5, 1.0),
            Facing = MountFacing.Up,
            AllowedRotations = new HashSet<CockpitRotationStep> { CockpitRotationStep.Deg90 },
        };
        var definition = new CockpitModuleDefinition
        {
            DefinitionId = "test-cockpit",
            DisplayName = "Test Cockpit",
            RequiredMountClass = CockpitMountClass.C2,
            PilotLocalPosition = DVec3.Zero,
            PilotLocalOrientation = Quaternion.Identity,
            CameraLocalPosition = new DVec3(2.0, 3.0, 4.0),
            CameraLocalOrientation =
                Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.4f),
        };

        DVec3 rootPosition = cockpit.ResolveShipLocalRootPosition(mount, definition);
        Quaternion rootOrientation =
            cockpit.ResolveShipLocalRootOrientation(mount, definition);
        DVec3 cameraPosition =
            cockpit.ResolveShipLocalCameraPosition(mount, definition);

        Assert.Equal(mount.ShipLocalPosition, rootPosition);
        AssertQuaternion(
            Quaternion.Normalize(
                mountOrientation
                * Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathHelper.PiOver2)),
            rootOrientation);
        Assert.NotEqual(cameraPosition, rootPosition);
    }

    [Fact]
    public void InstalledCockpit_RejectsIncompatibleMountClass()
    {
        var cockpit = new InstalledCockpit
        {
            MountId = "test",
            DefinitionId = CockpitDefinitionLibrary.AriesCivilianCanopyId,
            InstallationRotation = CockpitRotationStep.Deg0,
        };
        var mount = new CockpitMountDefinition
        {
            MountId = "test",
            MountClass = CockpitMountClass.C1,
            ShipLocalPosition = DVec3.Zero,
            ShipLocalOrientation = Quaternion.Identity,
            SocketSizeMeters = new DVec3(1.0, 1.0, 1.0),
            Facing = MountFacing.Up,
            AllowedRotations = new HashSet<CockpitRotationStep> { CockpitRotationStep.Deg0 },
        };
        CockpitModuleDefinition definition =
            CockpitDefinitionLibrary.Get(CockpitDefinitionLibrary.AriesCivilianCanopyId);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => cockpit.ResolveShipLocalCameraPosition(mount, definition));

        Assert.Contains("requires mount class C2", error.Message);
    }

    [Fact]
    public void InstalledCockpit_RejectsRotationNotAllowedByMount()
    {
        var cockpit = new InstalledCockpit
        {
            MountId = "test",
            DefinitionId = CockpitDefinitionLibrary.AriesCivilianCanopyId,
            InstallationRotation = CockpitRotationStep.Deg90,
        };
        var mount = new CockpitMountDefinition
        {
            MountId = "test",
            MountClass = CockpitMountClass.C2,
            ShipLocalPosition = DVec3.Zero,
            ShipLocalOrientation = Quaternion.Identity,
            SocketSizeMeters = new DVec3(1.5, 1.5, 1.0),
            Facing = MountFacing.Up,
            AllowedRotations = new HashSet<CockpitRotationStep> { CockpitRotationStep.Deg0 },
        };
        CockpitModuleDefinition definition =
            CockpitDefinitionLibrary.Get(CockpitDefinitionLibrary.AriesCivilianCanopyId);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => cockpit.ResolveShipLocalCameraPosition(mount, definition));

        Assert.Contains("rotation Deg90 is not allowed", error.Message);
    }

    [Fact]
    public void PersistenceRoundTrip_PreservesCockpitInstallationAndState()
    {
        var original = ShipBuilder.NewShip("type-1").Build();
        original.Cockpit!.CanopyLightsOn = true;
        original.Cockpit.CockpitLightsOn = true;

        ShipRecord record = original.ToRecord();
        string json = JsonSerializer.Serialize(record);
        ShipRecord persisted = JsonSerializer.Deserialize<ShipRecord>(json)!;
        var restored = ShipBuilder.From(persisted).Build();

        Assert.NotNull(persisted.Cockpit);
        Assert.NotNull(restored.Cockpit);
        Assert.Equal(original.Cockpit.MountId, restored.Cockpit.MountId);
        Assert.Equal(original.Cockpit.DefinitionId, restored.Cockpit.DefinitionId);
        Assert.Equal(original.Cockpit.InstallationRotation, restored.Cockpit.InstallationRotation);
        Assert.True(restored.Cockpit.CanopyLightsOn);
        Assert.True(restored.Cockpit.CockpitLightsOn);
    }

    [Fact]
    public void MissingPersistedCockpit_UsesHullDefault()
    {
        var oldRecord = new ShipRecord
        {
            Id = "old-aries",
            HullTypeId = "type-1",
            CreatedDate = DateTime.UtcNow,
            Cockpit = null,
        };

        var restored = ShipBuilder.From(oldRecord).Build();

        Assert.NotNull(restored.Cockpit);
        Assert.Equal(
            CockpitDefinitionLibrary.AriesCivilianCanopyId,
            restored.Cockpit.DefinitionId);
    }

    [Fact]
    public void CockpitLightCommands_ChangeInstalledSimulationState()
    {
        var ship = ShipBuilder.NewShip("type-1").Build();

        Assert.True(ship.ApplyCockpitCommand(
            new ComponentCommand(CockpitCommandTopics.CanopyLightsToggle, 0.0)));
        Assert.True(ship.ApplyCockpitCommand(
            new ComponentCommand(CockpitCommandTopics.InternalLightsSet, 1.0)));

        Assert.True(ship.Cockpit!.CanopyLightsOn);
        Assert.True(ship.Cockpit.CockpitLightsOn);
    }

    [Fact]
    public void CockpitLightCommands_FlowThroughSimulationCommandBus()
    {
        var simulation = new SpaceSimulation();
        var ship = ShipBuilder.NewShip("type-1").Build();
        simulation.SetShip(ship);

        CommandBus.Send(CockpitCommandTopics.CanopyLightsSet, 1.0);
        CommandBus.Send(CockpitCommandTopics.InternalLightsToggle);
        simulation.TickForTests(PlayerInput.Zero, 1.0 / 60.0);

        Assert.True(ship.Cockpit!.CanopyLightsOn);
        Assert.True(ship.Cockpit.CockpitLightsOn);
        Assert.NotNull(simulation.ShipState!.Cockpit);
        Assert.True(simulation.ShipState.Cockpit.CanopyLightsOn);
        Assert.True(simulation.ShipState.Cockpit.CockpitLightsOn);
        AssertDVec3(ship.CockpitRootWorldPosition, simulation.ShipState.Cockpit.WorldPosition);
        AssertQuaternion(
            ship.CockpitRootWorldOrientation,
            simulation.ShipState.Cockpit.WorldOrientation);
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
}
