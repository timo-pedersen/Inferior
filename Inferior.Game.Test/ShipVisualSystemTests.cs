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
        var hull = HullDefinitionLibrary.Get("type1");

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
    public void Type1Foundation_HasTwoNamedEngineSlotsAndNoInventedVisualGeometry()
    {
        var hull = HullDefinitionLibrary.Get("type1");

        Assert.Null(hull.VisualGeometry);
        Assert.Contains(hull.Slots, s => s.SlotId == "engine.port.01" && s.Category == SlotCategory.Engine);
        Assert.Contains(hull.Slots, s => s.SlotId == "engine.starboard.01" && s.Category == SlotCategory.Engine);
        Assert.DoesNotContain(hull.Slots, s => s.SlotId == "engine_main");
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
}
