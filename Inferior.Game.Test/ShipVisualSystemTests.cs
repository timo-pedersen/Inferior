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
        Assert.Equal(hull.AerodynamicLift, ship.AerodynamicLift);
        Assert.Equal(hull.AerodynamicBrakeFront, ship.AerodynamicBrakeFront);
        Assert.Equal(hull.AerodynamicBrakeLateral, ship.AerodynamicBrakeLateral);
        Assert.Equal(300_000.0, ship.MaxDownThrustN);
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
    public void AriesDefinition_HasTwoEngineSlotsAndSemanticGeometry()
    {
        var hull = HullDefinitionLibrary.Get("type-1");
        var geometry = hull.VisualGeometry;

        Assert.NotNull(geometry);
        Assert.Empty(geometry.Validate());
        Assert.True(geometry.RequireClosedHull);
        Assert.Contains(hull.Slots, s => s.SlotId == "engine.port.01" && s.Category == SlotCategory.Engine);
        Assert.Contains(hull.Slots, s => s.SlotId == "engine.starboard.01" && s.Category == SlotCategory.Engine);
        Assert.DoesNotContain(hull.Slots, s => s.SlotId == "engine_main");
        Assert.Equal(2, geometry.AttachmentPorts.Count(p => p.Capabilities.HasFlag(AttachmentCapability.Engine)));
        Assert.Equal(3, geometry.AttachmentPorts.Count(p => p.Capabilities.HasFlag(AttachmentCapability.LandingGear)));
    }

    [Fact]
    public void AriesSemanticGeometry_DefinesCargoDoorPanelsServicesAndLights()
    {
        var geometry = HullDefinitionLibrary.Get("type-1").VisualGeometry!;

        Assert.Contains(geometry.Assemblies, a => a.AssemblyId == "type-1.rear.cargo-door.01" && a.Kind == "CargoDoor");
        Assert.Contains(geometry.Faces, f => f.Id == "type-1.rear.cargo-door.01" && f.Role == HullSurfaceRole.CargoDoor);
        Assert.True(geometry.Faces.Count(f => f.Role == HullSurfaceRole.PanelSeat) >= 6);
        Assert.True(geometry.Faces.Count(f => f.Role == HullSurfaceRole.EngineMount) >= 4);
        Assert.True(geometry.Faces.Count(f => f.Role == HullSurfaceRole.ServiceSurface) >= 4);
        Assert.Contains(geometry.Faces, f => f.Role == HullSurfaceRole.CockpitGlass);
        Assert.Contains(geometry.Faces, f => f.Role == HullSurfaceRole.CockpitFrame);
        Assert.Equal(4, geometry.MarkerLights.Count);
        Assert.Equal(2, geometry.BeamLights.Count);
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

        public bool OverlapsWithPositiveVolume(Cuboid other)
            => Min.X < other.Max.X && Max.X > other.Min.X
            && Min.Y < other.Max.Y && Max.Y > other.Min.Y
            && Min.Z < other.Max.Z && Max.Z > other.Min.Z;
    }
}
