using Inferior.Core.Math;
using Inferior.Game.Ships;
using Inferior.Gameplay;
using Inferior.Gameplay.Engines;
using Inferior.Gameplay.Hull;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace Inferior.Game.Test;

public sealed class NeedleEngineTests
{
    [Fact]
    public void Definition_DeclaresNeedleH2EnvelopeRegionsAndDesignIntent()
    {
        EngineVariantDefinition variant =
            EngineDefinitionLibrary.GetVariant(NeedleEngineDefinitionFactory.H2VariantId);
        EngineDefinition definition = variant.Engine;
        string[] partIds = definition.VisualGeometry!.MeshParts
            .Select(part => part.PartId)
            .ToArray();

        Assert.Equal("needle", definition.FamilyId);
        Assert.Equal("Needle", definition.DisplayName);
        Assert.Equal(EngineMountStandardIds.H2, variant.MountStandardId);
        Assert.Equal(new DVec3(1.35, 1.55, 6.20), definition.NominalEnvelopeMeters);
        Assert.Contains("needle.body.forward-fairing", partIds);
        Assert.Contains("needle.mount.adapter-collar", partIds);
        Assert.Contains("needle.body.main-shell", partIds);
        Assert.Contains("needle.body.lower-keel", partIds);
        Assert.Contains("needle.service.spine", partIds);
        Assert.Contains("needle.service.access-strip", partIds);
        Assert.Contains("needle.body.rear-collar", partIds);
        Assert.Contains("needle.exhaust.slot", partIds);
        Assert.Contains("needle.light.forward", partIds);
        Assert.Contains("needle.light.rear", partIds);
        Assert.Equal(EngineIntentRating.High, definition.DesignIntent!.ForwardThrust);
        Assert.Equal(EngineIntentRating.High, definition.DesignIntent.FuelEfficiency);
        Assert.Equal(EngineIntentRating.Low, definition.DesignIntent.ThermalMass);
        Assert.Equal(EngineIntentRating.High, definition.DesignIntent.MaintenanceDifficulty);
        Assert.False(definition.DesignIntent.AlphaRedProduction);
    }

    [Fact]
    public void Geometry_FitsNeedleEnvelopeAndIsLongerNarrowerAndLowerThanRenderedMule()
    {
        EngineVisualGeometry needle = NeedleEngineDefinitionFactory.CreateDefinition().VisualGeometry!;
        EngineVisualGeometry mule = MuleEngineDefinitionFactory.CreateDefinition().VisualGeometry!;
        Bounds3 needleBounds = Bounds(needle);
        Bounds3 muleBounds = Bounds(mule);

        Assert.True(needleBounds.Size.X <= 1.35 + 1e-9);
        Assert.True(needleBounds.Size.Y <= 1.55 + 1e-9);
        Assert.Equal(6.20, needleBounds.Size.Z, 6);
        Assert.True(needleBounds.Size.Z > muleBounds.Size.Z);
        Assert.True(needleBounds.Size.X < muleBounds.Size.X);
        Assert.True(needleBounds.Size.Y < muleBounds.Size.Y);
        Assert.True(TriangleCount(needle) > 80);
    }

    [Fact]
    public void AriesConstruction_SelectsIndependentNeedlePairOnExistingH2Mounts()
    {
        var ship = ShipBuilder.NewShip("type-1")
            .WithEngineVariant(NeedleEngineDefinitionFactory.H2VariantId)
            .Build();
        EngineInstance[] engines = ship.EngineMounts
            .Select(mount => mount.InstalledEngine)
            .OfType<EngineInstance>()
            .ToArray();

        Assert.Equal(2, engines.Length);
        Assert.NotSame(engines[0], engines[1]);
        Assert.NotEqual(engines[0].InstanceId, engines[1].InstanceId);
        Assert.All(engines, engine =>
            Assert.Equal(NeedleEngineDefinitionFactory.H2VariantId, engine.Variant.VariantId));
        Assert.All(ship.EngineMounts, mount =>
            Assert.Equal(EngineMountStandardIds.H2, mount.MountStandardId));

        engines[0].SetDamageFraction(0.65);
        Assert.Equal(0.65, engines[0].DamageFraction);
        Assert.Equal(0.0, engines[1].DamageFraction);
    }

    [Fact]
    public void InstalledNeedleInterfaces_MeetUnchangedAriesMountInterfaces()
    {
        var ship = ShipBuilder.NewShip("type-1")
            .WithEngineVariant(NeedleEngineDefinitionFactory.H2VariantId)
            .Build();

        foreach (EngineMount mount in ship.EngineMounts)
        {
            EngineInstance engine = Assert.IsType<EngineInstance>(mount.InstalledEngine);
            DVec3 interfacePosition = engine.GeometryTransform!.TransformVisualPoint(
                engine.Variant.Engine.VisualGeometry!.AttachmentInterfacePosition);
            Assert.True((interfacePosition - mount.AttachmentInterfacePosition!.Value).Length < 1e-5);
            Assert.Equal(mount.Pose.Position, engine.GeometryTransform.Position);
        }
    }

    [Fact]
    public void PairMirroring_PlacesServiceSpinesOutboardWithCorrectedWinding()
    {
        var ship = ShipBuilder.NewShip("type-1")
            .WithEngineVariant(NeedleEngineDefinitionFactory.H2VariantId)
            .Build();

        foreach (EngineMount mount in ship.EngineMounts)
        {
            EngineInstance engine = Assert.IsType<EngineInstance>(mount.InstalledEngine);
            bool mirrored = engine.GeometryTransform!.MirroredAcrossHullX;
            EngineCpuMesh mesh = EngineMeshBuilder.Build(
                engine.Variant.Engine.VisualGeometry!,
                mirrored);
            EngineCpuMeshPart spine = Assert.Single(
                mesh.Parts,
                part => part.PartId == "needle.service.spine");
            Vector3[] worldSpine = spine.Vertices
                .Select(vertex => Vector3.Transform(
                    vertex.Position,
                    engine.GeometryTransform.LocalToHull))
                .ToArray();

            if (mount.Side == EngineMountSide.Starboard)
                Assert.All(worldSpine, point => Assert.True(point.X > mount.Pose.Position.X));
            else
                Assert.All(worldSpine, point => Assert.True(point.X < mount.Pose.Position.X));

            Assert.Equal(1.0f, engine.GeometryTransform.LocalToHull.M11);
            for (int i = 0; i < spine.Vertices.Count; i += 3)
            {
                Vector3 a = spine.Vertices[i].Position;
                Vector3 b = spine.Vertices[i + 1].Position;
                Vector3 c = spine.Vertices[i + 2].Position;
                Assert.True(Vector3.Dot(
                    Vector3.Cross(b - a, c - a),
                    spine.Vertices[i].Normal) > 0f);
            }
        }
    }

    [Fact]
    public void SlotExhaust_IsRecessedHorizontalAndPointsAft()
    {
        EngineVisualGeometry geometry = NeedleEngineDefinitionFactory.CreateDefinition().VisualGeometry!;
        EngineVisualMeshPart slot = Assert.Single(
            geometry.MeshParts,
            part => part.PartId == "needle.exhaust.slot");
        Bounds3 slotBounds = Bounds(slot.Triangles);
        EngineExhaustDefinition exhaust = Assert.Single(geometry.Exhausts);

        Assert.InRange(slotBounds.Size.X, 0.95, 1.10);
        Assert.InRange(slotBounds.Size.Y, 0.20, 0.28);
        Assert.True(slotBounds.Size.X > slotBounds.Size.Y * 3.0);
        Assert.True(slotBounds.Max.Z < exhaust.Position.Z);
        Assert.Equal(DVec3.UnitZ, exhaust.Direction);
        Assert.Equal("needle.exhaust.slot.01", exhaust.ExhaustId);
    }

    [Fact]
    public void MandatoryLights_DefineDirectedForwardWhiteAndRearRedMarkers()
    {
        EngineVisualGeometry geometry = NeedleEngineDefinitionFactory.CreateDefinition().VisualGeometry!;
        EngineLightDefinition forward = Assert.Single(
            geometry.Lights,
            light => light.LightId == "needle.light.forward.01");
        EngineLightDefinition rear = Assert.Single(
            geometry.Lights,
            light => light.LightId == "needle.light.rear.01");

        Assert.Equal(-DVec3.UnitZ, forward.Direction);
        Assert.True(forward.Position.Z < -3.0);
        Assert.True(forward.Colour.Z > forward.Colour.X);
        Assert.Equal(DVec3.UnitZ, rear.Direction);
        Assert.True(rear.Position.Z > 3.0);
        Assert.True(rear.Colour.X > rear.Colour.Y * 5.0);
    }

    [Fact]
    public void DebugRemoval_LeavesNeedlePeerAndHullMountWithF2Landmarks()
    {
        var simulation = new SpaceSimulation();
        var ship = ShipBuilder.NewShip("type-1")
            .WithEngineVariant(NeedleEngineDefinitionFactory.H2VariantId)
            .Build();
        simulation.SetShip(ship);
        simulation.DebugTickPhysics(PlayerInput.Zero, 1.0 / 60.0);
        simulation.RequestDebugRemoveEngine(EngineMountSide.Port);
        simulation.DebugTickPhysics(PlayerInput.Zero, 1.0 / 60.0);

        IReadOnlyList<EngineMountPresentationSnapshot> mounts =
            simulation.ShipState!.EngineMounts!;
        Assert.Null(mounts.Single(mount => mount.Side == EngineMountSide.Port).InstalledEngine);
        Assert.NotNull(mounts.Single(mount => mount.Side == EngineMountSide.Starboard).InstalledEngine);
        Assert.Equal(
            26,
            HullDefinitionLibrary.Get("type-1").VisualGeometry!.Faces.Count(face =>
                face.Role == HullSurfaceRole.EngineMount && !face.ContributesToClosedHull));

        VertexPositionColor[] lines = ShipMeshRenderer.BuildEngineModuleDebugLines(mounts);
        EngineMountPresentationSnapshot starboard = mounts.Single(mount =>
            mount.Side == EngineMountSide.Starboard);
        AssertLineStartsAt(lines, starboard.HullRootPosition!.Value.ToVector3(), Color.Red);
        AssertLineStartsAt(lines, starboard.AttachmentInterfacePosition!.Value.ToVector3(), Color.Red);
        AssertLineStartsAt(lines, starboard.Pose.Position.ToVector3(), Color.Red);
        Assert.Contains(lines, vertex => vertex.Color == Color.OrangeRed && vertex.Position.Z > 5.8f);
        Assert.Contains(lines, vertex => vertex.Color.R > 200 && vertex.Color.G < 80);
        Assert.Contains(lines, vertex => vertex.Color.R > 180 && vertex.Color.G > 180 && vertex.Color.B > 180);
    }

    private static Bounds3 Bounds(EngineVisualGeometry geometry)
        => Bounds(geometry.MeshParts.SelectMany(part => part.Triangles));

    private static Bounds3 Bounds(IEnumerable<EngineVisualTriangle> triangles)
    {
        DVec3[] points = triangles
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .ToArray();
        return new Bounds3(
            new DVec3(
                points.Min(point => point.X),
                points.Min(point => point.Y),
                points.Min(point => point.Z)),
            new DVec3(
                points.Max(point => point.X),
                points.Max(point => point.Y),
                points.Max(point => point.Z)));
    }

    private static int TriangleCount(EngineVisualGeometry geometry)
        => geometry.MeshParts.Sum(part => part.Triangles.Count);

    private static void AssertLineStartsAt(
        IReadOnlyList<VertexPositionColor> vertices,
        Vector3 start,
        Color colour)
    {
        for (int i = 0; i < vertices.Count; i += 2)
        {
            if (vertices[i].Position == start && vertices[i].Color == colour)
                return;
        }

        Assert.Fail($"Expected debug line starting at {start} with colour {colour}.");
    }

    private readonly record struct Bounds3(DVec3 Min, DVec3 Max)
    {
        public DVec3 Size => Max - Min;
    }
}
