using Inferior.Core.Math;
using Inferior.Game.Ships;
using Inferior.Gameplay.Hull;
using Inferior.Gameplay.Ship;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class ShipVisualSystemTests
{
    [Fact]
    public void ShipSizeClass_UsesCurrentFourClassSet()
    {
        var names = Enum.GetNames<ShipSizeClass>();

        Assert.Equal(["Shuttle", "Small", "Medium", "Large"], names);
        Assert.DoesNotContain("Capital", names);
    }

    [Fact]
    public void ShipBuilder_DerivesHullOwnedPropertiesFromHullDefinition()
    {
        var hull = HullDefinitionLibrary.Get("type-1");

        var ship = ShipBuilder.NewShip(hull.HullTypeId)
            .WithPosition(new DVec3(1, 2, 3))
            .WithOrientation(Quaternion.Identity)
            .Build();

        Assert.Equal(hull.HullTypeId, ship.HullTypeId);
        Assert.Equal(hull.SizeClass, ship.SizeClass);
        Assert.Equal(hull.HullMass, ship.HullMass);
        Assert.Equal(hull.CockpitOffset, ship.CockpitOffset);
        Assert.Equal(hull.CockpitPose, ship.CockpitPose);
        Assert.Equal(hull.AerodynamicLift, ship.AerodynamicLift);
        Assert.Equal(hull.AerodynamicBrakeFront, ship.AerodynamicBrakeFront);
        Assert.Equal(hull.AerodynamicBrakeLateral, ship.AerodynamicBrakeLateral);
    }

    [Fact]
    public void AriesDefinition_HasConfirmedDesignMetadata()
    {
        var hull = HullDefinitionLibrary.Get("type-1");

        Assert.Equal("type-1", hull.HullTypeId);
        Assert.Equal("Aries", hull.DisplayName);
        Assert.Equal(ShipSizeClass.Small, hull.SizeClass);
        Assert.Equal("Utility", hull.PrimaryDesignBias);
        Assert.Equal("Light freight", hull.SecondaryDesignBias);
        Assert.Equal(new DVec3(-1.25, 1.55, -5.9), hull.CockpitOffset);
        Assert.Equal(hull.CockpitOffset, hull.CockpitPose.Position);
        Assert.Equal(16.0, hull.Dimensions!.LengthMeters);
        Assert.Equal(10.0, hull.Dimensions.WidthMeters);
        Assert.Equal(5.0, hull.Dimensions.HeightMeters);
        Assert.Equal(7.0, hull.Dimensions.StructuralHullWidthMeters);
        Assert.Equal(5.0, hull.Dimensions.StructuralHullHeightMeters);
        Assert.Equal(2, hull.CargoArrangement!.ContainerCapacity);
        Assert.Equal("two standard containers side by side", hull.CargoArrangement.Arrangement);
        Assert.Equal(new DVec3(5.0, 2.5, 6.0), hull.CargoArrangement.StackBoundsMeters);
        Assert.Equal(new DVec3(6.0, 3.2, 7.2), hull.CargoArrangement.DesignVolumeBoundsMeters);
        Assert.Equal("type-1.rear.cargo-door.01", hull.CargoArrangement.CargoDoorAssemblyId);
    }

    [Fact]
    public void AriesCockpitPose_IsPortOffsetRaisedAndYawedInward()
    {
        var pose = HullDefinitionLibrary.Get("type-1").CockpitPose;

        Assert.InRange(pose.Position.X, -1.5, -1.0);
        Assert.InRange(pose.Position.Y, 1.2, 1.7);
        Assert.InRange(pose.Position.Z, -6.5, -5.5);

        var forward = Vector3.Normalize(Vector3.Transform(Vector3.Forward, pose.Orientation));
        var up = Vector3.Normalize(Vector3.Transform(Vector3.Up, pose.Orientation));
        double inwardYawDegrees = Math.Atan2(forward.X, -forward.Z) * 180.0 / Math.PI;

        Assert.InRange(inwardYawDegrees, 2.0, 4.0);
        Assert.InRange(Math.Abs(forward.Y), 0.0, 0.0001);
        Assert.InRange(Vector3.Dot(up, Vector3.Up), 0.9999f, 1.0f);
    }

    [Fact]
    public void AriesDefinition_HasTwoEngineSlotsAndSemanticGeometry()
    {
        var hull = HullDefinitionLibrary.Get("type-1");
        var geometry = hull.VisualGeometry;

        Assert.NotNull(geometry);
        Assert.Empty(geometry.Validate());
        Assert.True(geometry.RequireClosedHull);
        Assert.Contains(hull.Slots, s => s.SlotId == "engine.port.01" && s.Category == SlotCategory.Engine && s.Required);
        Assert.Contains(hull.Slots, s => s.SlotId == "engine.starboard.01" && s.Category == SlotCategory.Engine && s.Required);
        Assert.DoesNotContain(hull.Slots, s => s.SlotId == "engine_main");
        Assert.Equal(2, geometry.AttachmentPorts.Count(p => p.Capabilities.HasFlag(AttachmentCapability.Engine)));
        Assert.Equal(3, geometry.AttachmentPorts.Count(p => p.Capabilities.HasFlag(AttachmentCapability.LandingGear)));
    }

    [Fact]
    public void AriesEngineSlots_AreIndependentRequiredPhysicalEnginesWithoutHullOwnedThrust()
    {
        var hull = HullDefinitionLibrary.Get("type-1");
        var engineSlots = hull.Slots.Where(s => s.Category == SlotCategory.Engine).ToArray();

        Assert.Equal(2, engineSlots.Length);
        Assert.Equal(
            ["engine.port.01", "engine.starboard.01"],
            engineSlots.Select(s => s.SlotId).Order(StringComparer.Ordinal).ToArray());
        Assert.All(engineSlots, slot => Assert.True(slot.Required));
        Assert.DoesNotContain(engineSlots, slot => slot.SlotId == "engine_main");

        Assert.Null(typeof(Ship).GetProperty("MaxDownThrustN"));
        Assert.Null(typeof(Ship).GetProperty("MaxForwardThrustN"));
        Assert.Null(typeof(Ship).GetProperty("FlightAcceleration"));
    }

    [Fact]
    public void AriesSemanticGeometry_DefinesCargoDoorPanelsServicesAndLights()
    {
        var geometry = HullDefinitionLibrary.Get("type-1").VisualGeometry!;

        Assert.Contains(geometry.Assemblies, a => a.AssemblyId == "type-1.rear.cargo-door.01" && a.Kind == "CargoDoor");
        Assert.Contains(geometry.Faces, f => f.Id == "type-1.rear.cargo-door.01" && f.Role == HullSurfaceRole.CargoDoor);
        Assert.InRange(geometry.Faces.Count(f => f.Role == HullSurfaceRole.PanelSeat), 8, 16);
        Assert.Equal(2, geometry.Faces.Count(f => f.Role == HullSurfaceRole.EngineMount));
        Assert.True(geometry.Faces.Count(f => f.Role == HullSurfaceRole.ServiceSurface) >= 6);
        Assert.True(geometry.Faces.Count(f => f.Role == HullSurfaceRole.ExposedStructure) >= 4);
        Assert.Contains(geometry.Faces, f => f.Id == "type-1.top.cockpit-glass.01" && f.Role == HullSurfaceRole.CockpitGlass);
        Assert.Contains(geometry.Faces, f => f.Id == "type-1.port.cockpit-frame.01" && f.Role == HullSurfaceRole.CockpitFrame);
        Assert.Equal(4, geometry.MarkerLights.Count);
        Assert.Equal(2, geometry.BeamLights.Count);
    }

    [Fact]
    public void AriesLights_DefineHullOwnedMarkersAndForwardDownBeamMounts()
    {
        var geometry = HullDefinitionLibrary.Get("type-1").VisualGeometry!;
        var port = Assert.Single(geometry.MarkerLights, l => l.LightId == "type-1.port.navigation-light.01");
        var starboard = Assert.Single(geometry.MarkerLights, l => l.LightId == "type-1.starboard.navigation-light.01");
        var rear = Assert.Single(geometry.MarkerLights, l => l.LightId == "type-1.rear.position-light.01");
        var beams = geometry.BeamLights.OrderBy(l => l.LightId, StringComparer.Ordinal).ToArray();

        Assert.Equal("red", port.Colour);
        Assert.Equal(-DVec3.UnitX, port.Direction);
        Assert.Equal("continuous", port.Pattern);
        Assert.True(port.Position.X < 0.0);
        Assert.True(port.Position.Z < -5.0);

        Assert.Equal("green", starboard.Colour);
        Assert.Equal(DVec3.UnitX, starboard.Direction);
        Assert.Equal("continuous", starboard.Pattern);
        Assert.True(starboard.Position.X > 0.0);
        Assert.Equal(port.Position.Z, starboard.Position.Z, 6);

        Assert.Equal("white", rear.Colour);
        Assert.Equal(DVec3.UnitZ, rear.Direction);
        Assert.Equal("continuous", rear.Pattern);
        Assert.True(rear.Position.Y > 0.5);
        Assert.True(rear.Position.Z > 8.0);

        Assert.Equal(["type-1.underside.beam-light.01", "type-1.underside.beam-light.02"], beams.Select(b => b.LightId).ToArray());
        Assert.All(beams, beam =>
        {
            Assert.True(beam.Position.Y < -1.0);
            Assert.True(beam.Position.Z < -7.0);
            Assert.True(beam.Direction.Y < -0.2);
            Assert.True(beam.Direction.Z < -0.9);
            Assert.Equal(24.0, beam.ConeAngleDegrees);
            Assert.Equal(700.0, beam.RangeMeters);
            Assert.Equal("warm-white", beam.Colour);
        });
        Assert.DoesNotContain(geometry.MarkerLights.Select(l => l.LightId).Concat(geometry.BeamLights.Select(l => l.LightId)),
            id => id.Contains("engine", StringComparison.Ordinal));
    }

    [Fact]
    public void AriesSurfaceRoleMap_ClassifiesEveryProductionFace()
    {
        var geometry = HullDefinitionLibrary.Get("type-1").VisualGeometry!;
        var engineMountFaces = geometry.Faces.Where(f => f.Role == HullSurfaceRole.EngineMount).ToArray();

        Assert.All(geometry.Faces, face => Assert.True(Enum.IsDefined(face.Role)));
        Assert.Equal(
            ["type-1.port.engine-root.01", "type-1.starboard.engine-root.01"],
            engineMountFaces.Select(f => f.Id).Order(StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(engineMountFaces, face => !string.IsNullOrWhiteSpace(face.PanelSlotId));

        Assert.Contains(geometry.Faces, f => f.Id == "type-1.front.armoured-head.01" && f.Role == HullSurfaceRole.PanelSeat);
        Assert.Contains(geometry.Faces, f => f.Id == "type-1.port.forward-side.01" && f.Role == HullSurfaceRole.PanelSeat);
        Assert.Contains(geometry.Faces, f => f.Id == "type-1.starboard.forward-side.01" && f.Role == HullSurfaceRole.PanelSeat);
        Assert.Contains(geometry.Faces, f => f.Id == "type-1.port.cargo-shoulder.01" && f.Role == HullSurfaceRole.PanelSeat);
        Assert.Contains(geometry.Faces, f => f.Id == "type-1.starboard.cargo-shoulder.01" && f.Role == HullSurfaceRole.PanelSeat);
        Assert.Contains(geometry.Faces, f => f.Id == "type-1.top.cargo.01" && f.Role == HullSurfaceRole.PanelSeat);
        Assert.Contains(geometry.Faces, f => f.Id == "type-1.rear.cargo-door.port.01" && f.Role == HullSurfaceRole.PanelSeat && f.AssemblyId == "type-1.rear.cargo-door.01");
        Assert.Contains(geometry.Faces, f => f.Id == "type-1.rear.cargo-door.starboard.01" && f.Role == HullSurfaceRole.PanelSeat && f.AssemblyId == "type-1.rear.cargo-door.01");
        Assert.All(
            geometry.Faces.Where(f => f.Id.Contains("cargo-door-frame", StringComparison.Ordinal)),
            face => Assert.Equal(HullSurfaceRole.ExposedStructure, face.Role));
    }

    [Fact]
    public void AriesSemanticIds_FollowLocationBasedTypedNamespaceConvention()
    {
        var hull = HullDefinitionLibrary.Get("type-1");
        var ids = EnumerateAriesSemanticIds(hull).ToArray();

        Assert.NotEmpty(ids);
        Assert.All(ids, id =>
        {
            string[] segments = id.Split('.');

            Assert.StartsWith("type-1.", id, StringComparison.Ordinal);
            Assert.True(segments.Length >= 4, $"Semantic id '{id}' does not include hull, region, subregion, and number.");
            Assert.True(int.TryParse(segments[^1], out _), $"Semantic id '{id}' does not end with a numeric instance segment.");
            Assert.DoesNotContain("v", segments);
            Assert.DoesNotContain("face", segments);
            Assert.DoesNotContain("panel", segments);
        });

        Assert.Contains("type-1.top.cargo.01", ids);
        Assert.Contains("type-1.top.cargo.01", hull.VisualGeometry!.Faces.Select(f => f.PanelSlotId).OfType<string>());
    }

    [Fact]
    public void AriesCargoVolume_FitsTwoCanonicalContainersAndRearLoadingPath()
    {
        var hull = HullDefinitionLibrary.Get("type-1");
        var cargo = hull.CargoArrangement!;
        var geometry = hull.VisualGeometry!;

        var designVolume = Cuboid.FromCenterAndSize(cargo.DesignVolumeCenterMeters, cargo.DesignVolumeBoundsMeters);
        var containerA = Cuboid.FromCenterAndSize(
            cargo.DesignVolumeCenterMeters + new DVec3(-1.25, 0.0, 0.0),
            new DVec3(2.5, 2.5, 6.0));
        var containerB = Cuboid.FromCenterAndSize(
            cargo.DesignVolumeCenterMeters + new DVec3(1.25, 0.0, 0.0),
            new DVec3(2.5, 2.5, 6.0));

        Assert.True(designVolume.Contains(containerA));
        Assert.True(designVolume.Contains(containerB));
        Assert.False(containerA.OverlapsWithPositiveVolume(containerB));

        Cuboid hullBounds = Cuboid.FromPoints(geometry.Vertices.Select(v => v.Position));
        Assert.True(hullBounds.Contains(designVolume));

        Assert.True(cargo.RearOpeningBoundsMeters.X >= cargo.StackBoundsMeters.X);
        Assert.True(cargo.RearOpeningBoundsMeters.Y >= cargo.StackBoundsMeters.Y);
        Assert.Equal(DVec3.UnitZ, cargo.TransferAxis);
    }

    [Fact]
    public void AriesCargoDoorAssembly_DefinesClosedSlidingDoorAndArmourPanelSeats()
    {
        var hull = HullDefinitionLibrary.Get("type-1");
        var cargo = hull.CargoArrangement!;
        var geometry = hull.VisualGeometry!;
        var door = Assert.Single(geometry.Assemblies, a => a.AssemblyId == cargo.CargoDoorAssemblyId);

        Assert.Equal("CargoDoor", door.Kind);
        Assert.Equal("type-1.rear.cargo-door.01", door.FaceId);
        Assert.Equal("Closed", door.ClosedPose);
        Assert.Equal("Two sliding leaves", door.MovementConcept);
        Assert.Equal([-DVec3.UnitX, DVec3.UnitX], door.MovementAxes);
        Assert.Equal(8, door.OpeningPolygonVertexIds.Count);
        Assert.Equal(2, door.MovementClearanceVolumes.Count);

        var portSeat = Assert.Single(door.ArmourPanelSeats, s => s.SeatId == "type-1.rear.cargo-door.port.01");
        var starboardSeat = Assert.Single(door.ArmourPanelSeats, s => s.SeatId == "type-1.rear.cargo-door.starboard.01");

        Assert.Equal("port container lane", portSeat.ProtectedLane);
        Assert.Equal("starboard container lane", starboardSeat.ProtectedLane);
        Assert.True(portSeat.CenterMeters.X < 0.0);
        Assert.True(starboardSeat.CenterMeters.X > 0.0);
        Assert.Equal(DVec3.UnitZ, portSeat.Normal);
        Assert.Equal(DVec3.UnitZ, starboardSeat.Normal);
    }

    [Fact]
    public void AriesCargoDoorMovementClearance_DoesNotBlockEnginesLandingFeetOrLoadingPath()
    {
        var hull = HullDefinitionLibrary.Get("type-1");
        var cargo = hull.CargoArrangement!;
        var geometry = hull.VisualGeometry!;
        var door = Assert.Single(geometry.Assemblies, a => a.AssemblyId == cargo.CargoDoorAssemblyId);
        var loadingPath = new Cuboid(
            new DVec3(
                cargo.DesignVolumeCenterMeters.X - cargo.DesignVolumeBoundsMeters.X / 2.0,
                cargo.DesignVolumeCenterMeters.Y - cargo.DesignVolumeBoundsMeters.Y / 2.0,
                cargo.DesignVolumeCenterMeters.Z - cargo.DesignVolumeBoundsMeters.Z / 2.0),
            new DVec3(
                cargo.DesignVolumeCenterMeters.X + cargo.DesignVolumeBoundsMeters.X / 2.0,
                cargo.DesignVolumeCenterMeters.Y + cargo.DesignVolumeBoundsMeters.Y / 2.0,
                8.6));
        var engineClearances = geometry.AttachmentPorts
            .Where(p => p.Capabilities.HasFlag(AttachmentCapability.Engine))
            .Select(p => new Cuboid(p.ClearanceMinMeters, p.ClearanceMaxMeters))
            .ToArray();
        var landingFeet = geometry.AttachmentPorts
            .Where(p => p.Capabilities.HasFlag(AttachmentCapability.LandingGear))
            .Select(p => p.Position)
            .ToArray();

        foreach (var bounds in door.MovementClearanceVolumes)
        {
            var doorClearance = new Cuboid(bounds.Min, bounds.Max);

            Assert.False(doorClearance.OverlapsWithPositiveVolume(loadingPath));
            Assert.All(engineClearances, engineClearance => Assert.False(doorClearance.OverlapsWithPositiveVolume(engineClearance)));
            Assert.All(landingFeet, landingFoot => Assert.False(doorClearance.Contains(landingFoot)));
        }
    }

    [Fact]
    public void AriesLandingFeet_MatchForwardPairAndRearFootLayout()
    {
        var hull = HullDefinitionLibrary.Get("type-1");
        var cargo = hull.CargoArrangement!;
        var feet = hull.VisualGeometry!.AttachmentPorts
            .Where(p => p.Capabilities.HasFlag(AttachmentCapability.LandingGear))
            .OrderBy(p => p.PortId, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(3, feet.Length);
        Assert.Equal(
            ["type-1.underside.landing-foot.01", "type-1.underside.landing-foot.02", "type-1.underside.landing-foot.03"],
            feet.Select(p => p.PortId).ToArray());

        var portForward = feet[0];
        var starboardForward = feet[1];
        var rearFoot = feet[2];

        Assert.True(portForward.Position.X < 0.0);
        Assert.True(starboardForward.Position.X > 0.0);
        Assert.True(portForward.Position.Z < 0.0);
        Assert.True(starboardForward.Position.Z < 0.0);
        Assert.Equal(portForward.Position.Z, starboardForward.Position.Z, 6);
        Assert.Equal(portForward.Position.Y, starboardForward.Position.Y, 6);
        Assert.Equal(-portForward.Position.X, starboardForward.Position.X, 6);
        double cargoRearEdge = cargo.DesignVolumeCenterMeters.Z + cargo.DesignVolumeBoundsMeters.Z / 2.0;
        Assert.True(rearFoot.Position.Z > 0.0);
        Assert.True(rearFoot.Position.Z < cargoRearEdge);
        Assert.Equal(0.0, rearFoot.Position.X, 6);

        Assert.All(feet, foot =>
        {
            Assert.Equal(-DVec3.UnitY, foot.Normal);
            Assert.True(foot.FootprintMeters.X > 0.0);
            Assert.True(foot.FootprintMeters.Y > 0.0);
            Assert.True(foot.ClearanceMaxMeters.X > foot.ClearanceMinMeters.X);
            Assert.True(foot.ClearanceMaxMeters.Y > foot.ClearanceMinMeters.Y);
            Assert.True(foot.ClearanceMaxMeters.Z > foot.ClearanceMinMeters.Z);
        });
    }

    [Fact]
    public void AriesLandingFeet_FormSupportTriangleAroundHullMassAndClearCargoDoor()
    {
        var hull = HullDefinitionLibrary.Get("type-1");
        var geometry = hull.VisualGeometry!;
        var cargo = hull.CargoArrangement!;
        var door = Assert.Single(geometry.Assemblies, a => a.AssemblyId == cargo.CargoDoorAssemblyId);
        var feet = geometry.AttachmentPorts
            .Where(p => p.Capabilities.HasFlag(AttachmentCapability.LandingGear))
            .OrderBy(p => p.PortId, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(3, feet.Select(p => p.PortId).Distinct(StringComparer.Ordinal).Count());

        DVec3 a = feet[0].Position;
        DVec3 b = feet[1].Position;
        DVec3 c = feet[2].Position;
        double supportArea2 = Math.Abs(SignedTriangleArea2XZ(a, b, c));
        Assert.True(supportArea2 > 1.0);

        var hullMassProjection = DVec3.Zero;
        Assert.True(PointInTriangleXZ(hullMassProjection, a, b, c));

        foreach (var foot in feet)
        {
            var footClearance = new Cuboid(foot.ClearanceMinMeters, foot.ClearanceMaxMeters);
            Assert.True(footClearance.Contains(foot.Position));

            foreach (var doorBounds in door.MovementClearanceVolumes)
            {
                var doorClearance = new Cuboid(doorBounds.Min, doorBounds.Max);

                Assert.False(doorClearance.Contains(foot.Position));
                Assert.False(doorClearance.OverlapsWithPositiveVolume(footClearance));
            }
        }
    }

    [Fact]
    public void AriesEngineZones_DoNotBlockRearCargoLoadingCorridor()
    {
        var hull = HullDefinitionLibrary.Get("type-1");
        var cargo = hull.CargoArrangement!;
        var loadingCorridor = Cuboid.FromCenterAndSize(cargo.DesignVolumeCenterMeters, cargo.DesignVolumeBoundsMeters);
        var enginePorts = hull.VisualGeometry!.AttachmentPorts
            .Where(p => p.Capabilities.HasFlag(AttachmentCapability.Engine))
            .ToArray();

        Assert.Equal(2, enginePorts.Length);

        foreach (var enginePort in enginePorts)
        {
            var clearance = new Cuboid(enginePort.ClearanceMinMeters, enginePort.ClearanceMaxMeters);

            Assert.False(clearance.OverlapsWithPositiveVolume(loadingCorridor));
        }
    }

    [Fact]
    public void AriesEngineMounts_DefineSlotBoundMirrorAttachmentPoses()
    {
        var hull = HullDefinitionLibrary.Get("type-1");
        var ports = hull.VisualGeometry!.AttachmentPorts
            .Where(p => p.Capabilities.HasFlag(AttachmentCapability.Engine))
            .OrderBy(p => p.PortId, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, ports.Length);
        var port = Assert.Single(ports, p => p.PortId == "type-1.port.engine-root.01");
        var starboard = Assert.Single(ports, p => p.PortId == "type-1.starboard.engine-root.01");

        Assert.Equal("engine.port.01", port.ComponentSlotId);
        Assert.Equal("engine.starboard.01", starboard.ComponentSlotId);
        Assert.Equal(-DVec3.UnitX, port.Normal);
        Assert.Equal(DVec3.UnitX, starboard.Normal);
        Assert.Equal(DVec3.UnitY, port.Up);
        Assert.Equal(DVec3.UnitY, starboard.Up);
        Assert.Equal(new DVec3(2.2, 2.4, 0.0), port.FootprintMeters);
        Assert.Equal(port.FootprintMeters, starboard.FootprintMeters);

        Assert.Equal(-port.Position.X, starboard.Position.X, 6);
        Assert.Equal(port.Position.Y, starboard.Position.Y, 6);
        Assert.Equal(port.Position.Z, starboard.Position.Z, 6);
        Assert.Equal(-port.ClearanceMaxMeters.X, starboard.ClearanceMinMeters.X, 6);
        Assert.Equal(-port.ClearanceMinMeters.X, starboard.ClearanceMaxMeters.X, 6);
        Assert.Equal(port.ClearanceMinMeters.Y, starboard.ClearanceMinMeters.Y, 6);
        Assert.Equal(port.ClearanceMaxMeters.Y, starboard.ClearanceMaxMeters.Y, 6);
        Assert.Equal(port.ClearanceMinMeters.Z, starboard.ClearanceMinMeters.Z, 6);
        Assert.Equal(port.ClearanceMaxMeters.Z, starboard.ClearanceMaxMeters.Z, 6);
    }

    [Fact]
    public void AriesEngineMountClearances_RespectShipEnvelopeAndStayClearOfCockpitDoorAndFeet()
    {
        var hull = HullDefinitionLibrary.Get("type-1");
        var geometry = hull.VisualGeometry!;
        var cargo = hull.CargoArrangement!;
        var door = Assert.Single(geometry.Assemblies, a => a.AssemblyId == cargo.CargoDoorAssemblyId);
        var loadingCorridor = Cuboid.FromCenterAndSize(cargo.DesignVolumeCenterMeters, cargo.DesignVolumeBoundsMeters);
        var cockpitKeepout = Cuboid.FromCenterAndSize(hull.CockpitPose.Position, new DVec3(2.2, 1.6, 2.0));
        var landingFeet = geometry.AttachmentPorts
            .Where(p => p.Capabilities.HasFlag(AttachmentCapability.LandingGear))
            .Select(p => p.Position)
            .ToArray();
        var enginePorts = geometry.AttachmentPorts
            .Where(p => p.Capabilities.HasFlag(AttachmentCapability.Engine))
            .ToArray();

        foreach (var enginePort in enginePorts)
        {
            var clearance = new Cuboid(enginePort.ClearanceMinMeters, enginePort.ClearanceMaxMeters);

            Assert.InRange(clearance.Min.X, -hull.Dimensions!.WidthMeters / 2.0, hull.Dimensions.WidthMeters / 2.0);
            Assert.InRange(clearance.Max.X, -hull.Dimensions.WidthMeters / 2.0, hull.Dimensions.WidthMeters / 2.0);
            Assert.True(clearance.Max.Z > clearance.Min.Z);
            Assert.True(clearance.Max.Z - clearance.Min.Z >= 8.0);
            Assert.False(clearance.OverlapsWithPositiveVolume(loadingCorridor));
            Assert.False(clearance.OverlapsWithPositiveVolume(cockpitKeepout));
            Assert.All(landingFeet, foot => Assert.False(clearance.Contains(foot)));

            foreach (var doorBounds in door.MovementClearanceVolumes)
                Assert.False(clearance.OverlapsWithPositiveVolume(new Cuboid(doorBounds.Min, doorBounds.Max)));
        }
    }

    [Fact]
    public void AriesServicePorts_ReserveSmallCapabilityDrivenFutureAttachmentSet()
    {
        var geometry = HullDefinitionLibrary.Get("type-1").VisualGeometry!;
        var servicePorts = geometry.AttachmentPorts
            .Where(p => p.Capabilities.HasFlag(AttachmentCapability.Sensor)
                && p.Capabilities.HasFlag(AttachmentCapability.Utility))
            .ToArray();

        Assert.InRange(servicePorts.Length, 2, 3);
        Assert.All(servicePorts, port =>
        {
            Assert.True(port.Capabilities.HasFlag(AttachmentCapability.Sensor));
            Assert.True(port.Capabilities.HasFlag(AttachmentCapability.Utility));
            Assert.False(port.Capabilities.HasFlag(AttachmentCapability.Engine));
            Assert.False(port.Capabilities.HasFlag(AttachmentCapability.LandingGear));
            Assert.True(port.FootprintMeters.X > 0.0);
            Assert.True(port.FootprintMeters.Y > 0.0);
            Assert.True(new Cuboid(port.ClearanceMinMeters, port.ClearanceMaxMeters).Contains(port.Position));
        });

        Assert.Single(servicePorts, p => p.Normal == -DVec3.UnitY && p.Position.Y < -2.0);
        Assert.Single(servicePorts, p => Math.Abs(p.Normal.X) > 0.99 && p.Position.X < 0.0);
    }

    [Fact]
    public void AriesStructuralShell_IsCompleteReadableClosedSemanticBoundary()
    {
        var geometry = HullDefinitionLibrary.Get("type-1").VisualGeometry!;

        Assert.True(geometry.RequireClosedHull);
        Assert.Empty(geometry.Validate());
        var structuralFaces = geometry.Faces.Where(face => face.ContributesToClosedHull).ToArray();
        Assert.Equal(18, structuralFaces.Length);
        Assert.All(geometry.Faces, face => Assert.InRange(face.VertexIds.Count, 4, 8));
        Assert.DoesNotContain(geometry.Faces, face => face.VertexIds.Count == 3);

        Assert.Equal(16, structuralFaces.Count(face => face.VertexIds.Count == 4));
        Assert.Equal(2, structuralFaces.Count(face => face.VertexIds.Count == 8));
        Assert.All(geometry.Faces.Where(face => !face.ContributesToClosedHull), face => Assert.Equal(4, face.VertexIds.Count));
        Assert.Contains(geometry.Faces, face => face.Id == "type-1.front.armoured-head.01");
        Assert.Contains(geometry.Faces, face => face.Id == "type-1.rear.cargo-door.01" && face.Role == HullSurfaceRole.CargoDoor);
    }

    [Fact]
    public void SemanticGeometryValidator_AcceptsSharedSemanticLocationAcrossTypedNamespaces()
    {
        var geometry = GeometryWithFace(
            faceId: "sample.top.nose.01",
            role: HullSurfaceRole.PanelSeat,
            panelSlotId: "sample.top.nose.01");

        Assert.Empty(geometry.Validate());
    }

    [Fact]
    public void SemanticGeometryValidator_RejectsDuplicateIdsWithinTypedNamespaces()
    {
        var geometry = new SemanticHullGeometry
        {
            Vertices =
            [
                new("sample.v.01", new DVec3(0, 0, 0)),
                new("sample.v.01", new DVec3(1, 0, 0)),
                new("sample.v.03", new DVec3(0, 1, 0)),
            ],
            Faces =
            [
                new("sample.top.nose.01", ["sample.v.01", "sample.v.03", "sample.v.01"], HullSurfaceRole.PanelSeat, "panel", DVec3.UnitZ, "sample.top.nose.01"),
                new("sample.top.nose.01", ["sample.v.01", "sample.v.03", "sample.v.01"], HullSurfaceRole.PanelSeat, "panel", DVec3.UnitZ, "sample.top.nose.01"),
            ],
            AttachmentPorts =
            [
                new("sample.starboard.engine-root.01", new DVec3(1, 0, 0), DVec3.UnitX, AttachmentCapability.Engine),
                new("sample.starboard.engine-root.01", new DVec3(1, 1, 0), DVec3.UnitX, AttachmentCapability.Engine),
            ],
        };

        var errors = geometry.Validate();

        Assert.Contains(errors, e => e.Contains("Duplicate semantic hull vertex id", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Duplicate semantic hull face id", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Duplicate panel slot id", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Duplicate attachment port id", StringComparison.Ordinal));
    }

    [Fact]
    public void SemanticGeometryValidator_RejectsInvalidFaceNormal()
    {
        var geometry = GeometryWithFace(outwardNormal: -DVec3.UnitZ);

        Assert.Contains(
            geometry.Validate(),
            e => e.Contains("declared normal disagrees", StringComparison.Ordinal));
    }

    [Fact]
    public void SemanticGeometryValidator_RejectsNonPlanarFaces()
    {
        var geometry = new SemanticHullGeometry
        {
            Vertices =
            [
                new("sample.v.01", new DVec3(0, 0, 0)),
                new("sample.v.02", new DVec3(1, 0, 0)),
                new("sample.v.03", new DVec3(1, 1, 0.01)),
                new("sample.v.04", new DVec3(0, 1, 0)),
            ],
            Faces =
            [
                new("sample.top.nose.01", ["sample.v.01", "sample.v.02", "sample.v.03", "sample.v.04"], HullSurfaceRole.ExposedStructure, "structural", DVec3.UnitZ),
            ],
        };

        Assert.Contains(
            geometry.Validate(),
            e => e.Contains("non-planar", StringComparison.Ordinal));
    }

    [Fact]
    public void SemanticGeometryValidator_RejectsNonConvexFacesForAnySurfaceRole()
    {
        var geometry = new SemanticHullGeometry
        {
            Vertices =
            [
                new("sample.v.01", new DVec3(0, 0, 0)),
                new("sample.v.02", new DVec3(2, 0, 0)),
                new("sample.v.03", new DVec3(1, 0.5, 0)),
                new("sample.v.04", new DVec3(2, 1, 0)),
                new("sample.v.05", new DVec3(0, 1, 0)),
            ],
            Faces =
            [
                new("sample.service.01", ["sample.v.01", "sample.v.02", "sample.v.03", "sample.v.04", "sample.v.05"], HullSurfaceRole.ServiceSurface, "structural", DVec3.UnitZ),
            ],
        };

        Assert.Contains(
            geometry.Validate(),
            e => e.Contains("not convex", StringComparison.Ordinal));
    }

    [Fact]
    public void SemanticGeometryValidator_RejectsMissingAndRepeatedFaceVertices()
    {
        var geometry = new SemanticHullGeometry
        {
            Vertices =
            [
                new("sample.v.01", new DVec3(0, 0, 0)),
                new("sample.v.02", new DVec3(1, 0, 0)),
            ],
            Faces =
            [
                new("sample.top.nose.01", ["sample.v.01", "sample.v.02", "sample.v.02", "sample.v.missing"], HullSurfaceRole.ExposedStructure, "structural", DVec3.UnitZ),
            ],
        };

        var errors = geometry.Validate();

        Assert.Contains(errors, e => e.Contains("repeats perimeter vertex", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("references unknown vertex", StringComparison.Ordinal));
    }

    [Fact]
    public void SemanticGeometryValidator_RejectsNearZeroAreaFaces()
    {
        var geometry = new SemanticHullGeometry
        {
            Vertices =
            [
                new("sample.v.01", new DVec3(0, 0, 0)),
                new("sample.v.02", new DVec3(1, 0, 0)),
                new("sample.v.03", new DVec3(2, 0, 0)),
            ],
            Faces =
            [
                new("sample.top.nose.01", ["sample.v.01", "sample.v.02", "sample.v.03"], HullSurfaceRole.ExposedStructure, "structural", DVec3.UnitZ),
            ],
        };

        Assert.Contains(
            geometry.Validate(),
            e => e.Contains("near-zero area", StringComparison.Ordinal));
    }

    [Fact]
    public void SemanticGeometryValidator_RejectsClosedHullWithInconsistentSharedEdgeWinding()
    {
        var geometry = new SemanticHullGeometry
        {
            RequireClosedHull = true,
            Vertices =
            [
                new("sample.v.01", new DVec3(0, 0, 0)),
                new("sample.v.02", new DVec3(1, 0, 0)),
                new("sample.v.03", new DVec3(0, 1, 0)),
                new("sample.v.04", new DVec3(0, 0, 1)),
            ],
            Faces =
            [
                new("sample.face.01", ["sample.v.01", "sample.v.02", "sample.v.03"], HullSurfaceRole.ExposedStructure, "structural", DVec3.UnitZ),
                new("sample.face.02", ["sample.v.01", "sample.v.02", "sample.v.04"], HullSurfaceRole.ExposedStructure, "structural", -DVec3.UnitY),
                new("sample.face.03", ["sample.v.01", "sample.v.03", "sample.v.04"], HullSurfaceRole.ExposedStructure, "structural", DVec3.UnitX),
                new("sample.face.04", ["sample.v.02", "sample.v.03", "sample.v.04"], HullSurfaceRole.ExposedStructure, "structural", new DVec3(1, 1, 1).Normalized()),
            ],
        };

        Assert.Contains(
            geometry.Validate(),
            e => e.Contains("same direction", StringComparison.Ordinal) || e.Contains("opposing face winding", StringComparison.Ordinal));
    }

    [Fact]
    public void SemanticGeometryValidator_RejectsPanelSlotOnNonPanelSeat()
    {
        var geometry = GeometryWithFace(
            role: HullSurfaceRole.EngineMount,
            panelSlotId: "sample.top.nose.01");

        Assert.Contains(
            geometry.Validate(),
            e => e.Contains("Non-PanelSeat face", StringComparison.Ordinal));
    }

    private static SemanticHullGeometry GeometryWithFace(
        string faceId = "sample.top.nose.01",
        HullSurfaceRole role = HullSurfaceRole.ExposedStructure,
        DVec3? outwardNormal = null,
        string? panelSlotId = null)
        => new()
        {
            Vertices =
            [
                new("sample.v.01", new DVec3(0, 0, 0)),
                new("sample.v.02", new DVec3(1, 0, 0)),
                new("sample.v.03", new DVec3(0, 1, 0)),
            ],
            Faces =
            [
                new(faceId, ["sample.v.01", "sample.v.02", "sample.v.03"], role, "structural", outwardNormal ?? DVec3.UnitZ, panelSlotId),
            ],
        };

    private static double SignedTriangleArea2XZ(DVec3 a, DVec3 b, DVec3 c)
        => (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);

    private static bool PointInTriangleXZ(DVec3 point, DVec3 a, DVec3 b, DVec3 c)
    {
        double area = SignedTriangleArea2XZ(a, b, c);
        double invArea = 1.0 / area;
        double u = SignedTriangleArea2XZ(point, b, c) * invArea;
        double v = SignedTriangleArea2XZ(point, c, a) * invArea;
        double w = SignedTriangleArea2XZ(point, a, b) * invArea;
        const double tolerance = 1e-9;

        return u >= -tolerance && v >= -tolerance && w >= -tolerance;
    }

    private static IEnumerable<string> EnumerateAriesSemanticIds(HullDefinition hull)
    {
        yield return hull.CargoArrangement!.CargoDoorAssemblyId;

        var geometry = hull.VisualGeometry!;
        foreach (var vertex in geometry.Vertices)
            yield return vertex.Id;

        foreach (var face in geometry.Faces)
        {
            yield return face.Id;

            if (!string.IsNullOrWhiteSpace(face.PanelSlotId))
                yield return face.PanelSlotId;

            if (!string.IsNullOrWhiteSpace(face.AssemblyId))
                yield return face.AssemblyId;

            foreach (string vertexId in face.VertexIds)
                yield return vertexId;
        }

        foreach (var assembly in geometry.Assemblies)
        {
            yield return assembly.AssemblyId;
            yield return assembly.FaceId;

            foreach (string vertexId in assembly.OpeningPolygonVertexIds)
                yield return vertexId;

            foreach (var seat in assembly.ArmourPanelSeats)
                yield return seat.SeatId;
        }

        foreach (var port in geometry.AttachmentPorts)
            yield return port.PortId;

        foreach (var light in geometry.MarkerLights)
            yield return light.LightId;

        foreach (var light in geometry.BeamLights)
            yield return light.LightId;
    }

    private readonly record struct Cuboid(DVec3 Min, DVec3 Max)
    {
        public static Cuboid FromCenterAndSize(DVec3 center, DVec3 size)
            => new(center - size / 2.0, center + size / 2.0);

        public static Cuboid FromPoints(IEnumerable<DVec3> points)
        {
            bool any = false;
            double minX = 0, minY = 0, minZ = 0;
            double maxX = 0, maxY = 0, maxZ = 0;

            foreach (var point in points)
            {
                if (!any)
                {
                    minX = maxX = point.X;
                    minY = maxY = point.Y;
                    minZ = maxZ = point.Z;
                    any = true;
                    continue;
                }

                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                minZ = Math.Min(minZ, point.Z);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
                maxZ = Math.Max(maxZ, point.Z);
            }

            if (!any)
                throw new ArgumentException("Cannot create a cuboid from an empty point set.", nameof(points));

            return new Cuboid(new DVec3(minX, minY, minZ), new DVec3(maxX, maxY, maxZ));
        }

        public bool Contains(Cuboid other)
            => other.Min.X >= Min.X && other.Max.X <= Max.X
            && other.Min.Y >= Min.Y && other.Max.Y <= Max.Y
            && other.Min.Z >= Min.Z && other.Max.Z <= Max.Z;

        public bool Contains(DVec3 point)
            => point.X >= Min.X && point.X <= Max.X
            && point.Y >= Min.Y && point.Y <= Max.Y
            && point.Z >= Min.Z && point.Z <= Max.Z;

        public bool OverlapsWithPositiveVolume(Cuboid other)
            => Min.X < other.Max.X && Max.X > other.Min.X
            && Min.Y < other.Max.Y && Max.Y > other.Min.Y
            && Min.Z < other.Max.Z && Max.Z > other.Min.Z;
    }
}
