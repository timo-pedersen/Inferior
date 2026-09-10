using Inferior.Game.StationGen.Megastations;
using Inferior.Game.StationGen;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class MegastationMegaShelfFastTests
{
    [Fact]
    public void VerticalBandsPartitionUsableIntervalAndOpportunitiesCycleAllBands()
    {
        const float minimum = -180f;
        const float maximum = 240f;
        const int seed = 918273;
        var intervals = Enum.GetValues<MegastationMegaShelfVerticalBand>()
            .Select(band => MegastationMegaShelfPlanner.VerticalBandInterval(
                minimum, maximum, band, seed)).ToArray();

        Assert.Equal(minimum, intervals[0].Minimum);
        Assert.Equal(intervals[0].Maximum, intervals[1].Minimum);
        Assert.Equal(intervals[1].Maximum, intervals[2].Minimum);
        Assert.Equal(maximum, intervals[2].Maximum);
        Assert.All(intervals, interval => Assert.True(interval.Maximum > interval.Minimum));
        Assert.Equal(Enum.GetValues<MegastationMegaShelfVerticalBand>().Order(),
            Enumerable.Range(0, 3)
                .Select(index => MegastationMegaShelfPlanner.VerticalBandForOpportunity(
                    index, seed)).Order());
    }

    [Fact]
    public void OperatingVolumesStartAtShelfTopAndLandingGetsGreaterClearance()
    {
        var wall = new MegastationBayWallSurface(
            "wall", MegastationBayWallKind.Rear, Vector3.Zero,
            Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, 900f, 700f, true);
        Vector3 top = new(0f, 30f, 0f);
        MegastationMegaShelfClearanceVolume landing =
            MegastationMegaShelfPlanner.CreateOperatingVolume(
                top, wall, 220f, 180f, landing: true);
        MegastationMegaShelfClearanceVolume structural =
            MegastationMegaShelfPlanner.CreateOperatingVolume(
                top, wall, 220f, 180f, landing: false);

        Assert.Equal(Vector3.Dot(top, wall.Up),
            Vector3.Dot(landing.Centre, wall.Up) - landing.Size.Y * .5f, 3);
        Assert.Equal(Vector3.Dot(top, wall.Normal) - 180f * .5f,
            Vector3.Dot(landing.Centre, wall.Normal) - landing.Size.Z * .5f, 3);
        Assert.Equal(80f, landing.Size.Y);
        Assert.Equal(44f, structural.Size.Y);
        Assert.True(landing.Size.X > structural.Size.X);
        Assert.True(landing.Size.Z > structural.Size.Z);
    }

    [Fact]
    public void ShelfFacePaletteRemainsReadableAndPreservesFaceHierarchy()
    {
        MegastationMegaShelfFaceColours colours =
            MegastationMegaShelfPlanner.FaceColours(
                new Color(24, 28, 32), new Color(18, 22, 26));

        Assert.True(Luma(colours.Top) > Luma(colours.Side));
        Assert.True(Luma(colours.Side) > Luma(colours.Underside));
        Assert.True(colours.Top.R >= 54 && colours.Top.G >= 54 && colours.Top.B >= 54);
        Assert.True(colours.Side.R >= 42 && colours.Side.G >= 42 && colours.Side.B >= 42);
        Assert.True(colours.Underside.R >= 28 && colours.Underside.G >= 28
            && colours.Underside.B >= 28);

        static int Luma(Color colour) => colour.R + colour.G + colour.B;
    }

    [Fact]
    public void FullSpanRequiresAUsableLargeShipPassageAboveOrBelow()
    {
        Assert.False(MegastationMegaShelfPlanner.FullSpanLeavesNavigablePassage(
            0f, 220f, shelfTop: 112f, shelfThickness: 12f));
        Assert.True(MegastationMegaShelfPlanner.FullSpanLeavesNavigablePassage(
            0f, 420f, shelfTop: 170f, shelfThickness: 12f));
        Assert.True(MegastationMegaShelfPlanner.FullSpanLeavesNavigablePassage(
            0f, 420f, shelfTop: 350f, shelfThickness: 12f));
    }

    [Fact]
    public void NewFamiliesReuseLandingMaterialMarkerAndTrussSemantics()
    {
        Assert.Equal([
                MegastationMegaShelfFamily.Cantilever,
                MegastationMegaShelfFamily.Corner,
                MegastationMegaShelfFamily.FullSpan,
                MegastationMegaShelfFamily.Bookcase,
            ], Enum.GetValues<MegastationMegaShelfFamily>());
        var body = new MegastationInteriorStructuralSolid(
            "shelf/body", MegastationInteriorStructuralRole.MegaShelf,
            Vector3.Zero, new Vector3(300f, 14f, 180f),
            Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, true, true);
        MegastationBayWallSurface rear = Wall(
            "rear", MegastationBayWallKind.Rear, Vector3.UnitZ);
        MegastationBayWallSurface left = Wall(
            "left", MegastationBayWallKind.Left, Vector3.UnitX);
        MegastationBayWallSurface right = Wall(
            "right", MegastationBayWallKind.Right, -Vector3.UnitX);

        IReadOnlyList<MegastationMegaShelfMarker> corner =
            MegastationMegaShelfPlanner.BuildMarkers(
                "corner", body, 9123, MegastationMegaShelfFamily.Corner, [rear, left]);
        Assert.Contains(corner, marker => marker.SurfaceRole
            == MegastationMegaShelfSurfaceRole.FrontEdge);
        Assert.DoesNotContain(corner, marker => marker.Identity.Contains("side:-1",
            StringComparison.Ordinal));

        IReadOnlyList<MegastationMegaShelfMarker> span =
            MegastationMegaShelfPlanner.BuildMarkers(
                "span", body, 9123, MegastationMegaShelfFamily.FullSpan, [left, right]);
        Assert.All(span, marker =>
        {
            Assert.Equal(MegastationMegaShelfSurfaceRole.FrontEdge, marker.SurfaceRole);
            Assert.False(marker.IsCorner);
        });
        Assert.Contains(span, marker => Vector3.Dot(marker.Forward, body.Forward) > .99f);
        Assert.Contains(span, marker => Vector3.Dot(marker.Forward, body.Forward) < -.99f);

        IReadOnlyList<MegastationMegaShelfTruss> spanTrusses =
            MegastationMegaShelfPlanner.BuildTrusses(
                "span", body, 3311, force: true,
                family: MegastationMegaShelfFamily.FullSpan);
        Assert.Equal(2, spanTrusses.Count);
        Assert.Single(spanTrusses.Select(truss => truss.Spec.CrossSection).Distinct());
        Assert.All(spanTrusses, truss => Assert.True(Vector3.Dot(
            new Vector3(truss.Spec.Transform.M31, truss.Spec.Transform.M32,
                truss.Spec.Transform.M33), body.Right) > .999f));

        static MegastationBayWallSurface Wall(
            string identity, MegastationBayWallKind kind, Vector3 normal)
            => new(identity, kind, Vector3.Zero, normal,
                Vector3.Normalize(Vector3.Cross(Vector3.UnitY, normal)),
                Vector3.UnitY, 800f, 600f, true);
    }

    [Fact]
    public void ExactWallSupportAcceptsConnectedFacesAndRejectsHole()
    {
        var grid = new SliceGrid(
            [10f, 10f, 10f], [10f, 10f, 10f], [10f, 10f, 10f],
            1..2, 1..2, 1..2);
        var occupancy = new StructuralOccupancy(grid);
        occupancy.MarkStructural(1, 0, 1);
        occupancy.MarkStructural(1, 1, 1);
        occupancy.ProtectEmpty(2, 0, 1, MegacellVoidKind.InteriorFlightVolume);
        occupancy.ProtectEmpty(2, 1, 1, MegacellVoidKind.InteriorFlightVolume);
        ExteriorSpace.ClassifyExternallyAccessibleEmpty(occupancy);
        BoundaryTopology topology = BoundaryTopologyBuilder.Build(
            occupancy, MegastationPrototypeSettings.Default);
        var wall = new MegastationBayWallSurface(
            "wall", MegastationBayWallKind.Left,
            new Vector3(5f, -5f, 0f), Vector3.UnitX, -Vector3.UnitZ,
            Vector3.UnitY, 10f, 20f, true);

        Assert.True(MegastationMegaShelfPlanner.TryFindExactSupport(
            wall, Vector2.Zero, new Vector2(8f, 18f), grid, topology,
            out BoundaryFaceKey[] faces));
        Assert.Equal(2, faces.Length);

        var withHole = new StructuralOccupancy(grid);
        withHole.MarkStructural(1, 0, 1);
        withHole.ProtectEmpty(2, 0, 1, MegacellVoidKind.InteriorFlightVolume);
        ExteriorSpace.ClassifyExternallyAccessibleEmpty(withHole);
        BoundaryTopology holeTopology = BoundaryTopologyBuilder.Build(
            withHole, MegastationPrototypeSettings.Default);
        Assert.False(MegastationMegaShelfPlanner.TryFindExactSupport(
            wall, Vector2.Zero, new Vector2(8f, 18f), grid, holeTopology,
            out _));

        var rootedBody = new MegastationInteriorStructuralSolid(
            "rooted", MegastationInteriorStructuralRole.MegaShelf,
            new Vector3(55f, -5f, 0f), new Vector3(8f, 18f, 100f),
            -Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX, true, true);
        Assert.True(MegastationMegaShelfPlanner.TryFindRootSupport(
            rootedBody, wall, grid, topology, out BoundaryFaceKey[] rootedFaces));
        Assert.Equal(faces, rootedFaces);
        Assert.False(MegastationMegaShelfPlanner.TryFindRootSupport(
            rootedBody, wall, grid, holeTopology, out _));
    }

    [Fact]
    public void MajorShelfPolicyIsExplicitAndLandingSurfaceRetainsSemanticOwner()
    {
        Assert.Equal([
                MegastationInteriorStructuralRole.MegaShelf,
                MegastationInteriorStructuralRole.MegaShelfTruss,
            ],
            Enum.GetValues<MegastationInteriorStructuralRole>());
        Assert.Equal(5, Enum.GetValues<MegastationMegaShelfSurfaceRole>().Length);

        var solid = new MegastationInteriorStructuralSolid(
            "shelf/body", MegastationInteriorStructuralRole.MegaShelf,
            Vector3.Zero, new Vector3(200f, 12f, 180f),
            Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ,
            CastsStellarShadow: true,
            CastsStaticArtificialShadow: true);
        var surface = new MegastationLandingSurface(
            "shelf/top", Vector3.UnitY * 100f,
            Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ,
            new Vector2(176f, 156f), 12f, solid.Identity, false);

        Assert.True(solid.CastsStellarShadow);
        Assert.True(solid.CastsStaticArtificialShadow);
        Assert.Equal(solid.Identity, surface.StructuralOwnerIdentity);
        Assert.False(surface.IsMainFloor);
    }

    [Fact]
    public void ArrivalExclusionUsesGenericLargeShipEnvelopeAndEntranceLocalFrame()
    {
        Vector3 right = Vector3.UnitZ;
        Vector3 up = Vector3.UnitY;
        Vector3 forward = -Vector3.UnitX;
        var exclusion = new MegastationInteriorMacroExclusionVolume(
            "arrival", MegastationInteriorMacroExclusionRole.EntranceArrivalManeuver,
            new Vector3(40f, 20f, -30f), new Vector3(160f, 130f, 420f),
            right, up, forward,
            new Vector3(
                SupportedShipEnvelopeStandards.LargeWidth,
                SupportedShipEnvelopeStandards.LargeHeight,
                SupportedShipEnvelopeStandards.LargeLength));

        Assert.Equal(new Vector3(36f, 20f, 72f), exclusion.SupportedShipEnvelope);
        Assert.True(exclusion.IntersectsOrientedBox(
            exclusion.Centre + forward * 100f,
            right, up, forward, new Vector3(50f, 30f, 60f)));
        Assert.False(exclusion.IntersectsOrientedBox(
            exclusion.Centre + right * 111f,
            right, up, forward, new Vector3(50f, 30f, 60f)));
    }

    [Fact]
    public void ShelfMarkersAndTrussesAreDeterministicBoundedAndExplicitlyClassified()
    {
        var body = new MegastationInteriorStructuralSolid(
            "shelf/body", MegastationInteriorStructuralRole.MegaShelf,
            new Vector3(17f, 80f, -31f), new Vector3(240f, 14f, 270f),
            Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, true, true);

        IReadOnlyList<MegastationMegaShelfMarker> markers =
            MegastationMegaShelfPlanner.BuildMarkers("shelf", body, 12345);
        IReadOnlyList<MegastationMegaShelfTruss> trusses =
            MegastationMegaShelfPlanner.BuildTrusses("shelf", body, 54321, force: true);

        Assert.Equal(markers,
            MegastationMegaShelfPlanner.BuildMarkers("shelf", body, 12345));
        Assert.Equal(trusses,
            MegastationMegaShelfPlanner.BuildTrusses("shelf", body, 54321, force: true));
        Assert.InRange(markers.Count, 14, 30);
        Assert.Contains(markers, marker => marker.IsCorner);
        Assert.Contains(markers, marker => marker.IsLowerEdge);
        Assert.All(markers, marker =>
        {
            Assert.True(marker.SurfaceRole is MegastationMegaShelfSurfaceRole.FrontEdge
                or MegastationMegaShelfSurfaceRole.SideEdge);
            Assert.True(marker.Colour.R >= marker.Colour.B);
        });

        Assert.InRange(trusses.Count, 2, 4);
        Assert.Single(trusses.Select(truss => truss.Spec.CrossSection).Distinct());
        Assert.All(trusses, truss =>
        {
            Assert.True(truss.CastsStellarShadow);
            Assert.False(truss.CastsStaticArtificialShadow);
            Vector3 localCentre = Vector3.Transform(truss.Spec.Transform.Translation - body.Centre,
                Matrix.Transpose(new Matrix(
                    body.Right.X, body.Right.Y, body.Right.Z, 0f,
                    body.Up.X, body.Up.Y, body.Up.Z, 0f,
                    body.Forward.X, body.Forward.Y, body.Forward.Z, 0f,
                    0f, 0f, 0f, 1f)));
            Assert.True(localCentre.Y + truss.Spec.Height * .5f > -body.Size.Y * .5f);
            Assert.True(localCentre.Y < -body.Size.Y * .5f);
        });
    }

    [Fact]
    public void OperatingAirspaceRejectsVerticalSandwichWithoutBodyOverlap()
    {
        var lower = new MegastationMegaShelfClearanceVolume(
            new Vector3(0f, 70f, 0f), new Vector3(210f, 170f, 220f),
            Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, true);
        var tooClose = new MegastationMegaShelfClearanceVolume(
            new Vector3(0f, 190f, 0f), new Vector3(180f, 150f, 180f),
            Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, true);
        var usefulGap = tooClose with { Centre = new Vector3(0f, 245f, 0f) };

        Assert.True(MegastationMegaShelfPlanner.ClearanceVolumesIntersect(
            lower, tooClose));
        Assert.False(MegastationMegaShelfPlanner.ClearanceVolumesIntersect(
            lower, usefulGap));
    }

    [Fact]
    public void StructuralVolumeSpacingRejectsOverlapButAllowsLayeredVoid()
    {
        MegastationInteriorStructuralSolid first = Solid(Vector3.Zero);
        MegastationInteriorStructuralSolid overlap = Solid(new Vector3(80f, 0f, 0f));
        MegastationInteriorStructuralSolid separate = Solid(new Vector3(0f, 90f, 0f));

        Assert.True(MegastationMegaShelfPlanner.StructuralVolumesIntersect(
            first, overlap, 18f));
        Assert.False(MegastationMegaShelfPlanner.StructuralVolumesIntersect(
            first, separate, 18f));

        static MegastationInteriorStructuralSolid Solid(Vector3 centre) => new(
            $"solid:{centre}", MegastationInteriorStructuralRole.MegaShelf,
            centre, new Vector3(100f, 12f, 100f),
            Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, true, true);
    }

    [Theory]
    [InlineData(MegastationMegaShelfFamily.Cantilever)]
    [InlineData(MegastationMegaShelfFamily.Corner)]
    [InlineData(MegastationMegaShelfFamily.FullSpan)]
    public void PolishedShelfThicknessIsStructuralButWithinThinDeckRange(
        MegastationMegaShelfFamily family)
    {
        float small = MegastationMegaShelfPlanner.ShelfThickness(family, 150f, 100f, 17);
        float large = MegastationMegaShelfPlanner.ShelfThickness(family, 700f, 300f, 17);

        Assert.InRange(small, 1.25f, 5f);
        Assert.InRange(large, 1.25f, 5f);
        Assert.True(large >= small);
        if (family != MegastationMegaShelfFamily.Cantilever)
            Assert.True(large < MegastationMegaShelfPlanner.ShelfThickness(
                MegastationMegaShelfFamily.Cantilever, 700f, 300f, 17));
    }

    [Fact]
    public void CeilingClearanceRetainsHighShelvesButRejectsFormerCrampedGap()
    {
        Assert.True(MegastationMegaShelfPlanner.MinimumCeilingSeparation > 62f);
        const float ceiling = 300f;
        Assert.True(ceiling - 200f >= MegastationMegaShelfPlanner.MinimumCeilingSeparation);
        Assert.False(ceiling - 230f >= MegastationMegaShelfPlanner.MinimumCeilingSeparation);
    }

    [Fact]
    public void FullSpanUsesSparseTrussesAndCornerReceivesAttachedUndersideSupport()
    {
        MegastationInteriorStructuralSolid span = Shelf(new(620f, 3f, 150f));
        IReadOnlyList<MegastationMegaShelfTruss> spanTrusses =
            MegastationMegaShelfPlanner.BuildTrusses(
                "span", span, 3311, family: MegastationMegaShelfFamily.FullSpan);
        Assert.InRange(spanTrusses.Count, 2, 3);

        MegastationInteriorStructuralSolid corner = Shelf(
            new(260f, 3f, 190f), clip: 8f, clipSide: 1);
        IReadOnlyList<MegastationMegaShelfTruss> cornerTrusses =
            MegastationMegaShelfPlanner.BuildTrusses(
                "corner", corner, 9123, family: MegastationMegaShelfFamily.Corner);
        Assert.Equal(2, cornerTrusses.Count);
        Assert.All(cornerTrusses, truss =>
        {
            Vector3 centre = truss.Spec.Transform.Translation;
            Assert.True(centre.Y < -corner.Size.Y * .5f);
            Assert.True(centre.Y + truss.Spec.Height * .5f
                > -corner.Size.Y * .5f - .001f);
            Assert.True(truss.Spec.Length <= MathF.Max(corner.Size.X, corner.Size.Z));
        });
        Assert.Single(cornerTrusses, truss =>
            Vector3.Dot(Axis(truss), corner.Right) > .999f);
        Assert.Single(cornerTrusses, truss =>
            Vector3.Dot(Axis(truss), corner.Forward) > .999f);
    }

    [Fact]
    public void ClippedCornerBodyIsClosedWoundAndLandingSupportIsConservative()
    {
        MegastationInteriorStructuralSolid body = Shelf(
            new(220f, 3.5f, 170f), clip: 8f, clipSide: 1);
        var mesh = new StationModuleMesh();
        MegastationInteriorMeshBuilder.AddMegaShelfBody(mesh, body,
            new MegastationMegaShelfFaceColours(Color.LightGray, Color.Gray, Color.DarkGray));
        var (vertices, indices) = mesh.ToIntArrays();

        Assert.Equal(16, indices.Length / 3); // 3+3 face triangles, five side quads.
        Assert.DoesNotContain(vertices, vertex =>
            MathF.Abs(vertex.Position.X - 110f) < .001f
            && MathF.Abs(vertex.Position.Z - 85f) < .001f);
        Assert.All(Enumerable.Range(0, indices.Length / 3), triangle =>
        {
            var a = vertices[indices[triangle * 3]];
            var b = vertices[indices[triangle * 3 + 1]];
            var c = vertices[indices[triangle * 3 + 2]];
            Vector3 geometric = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            Assert.True(geometric.LengthSquared() > 1e-8f);
            Assert.True(Vector3.Dot(geometric, a.Normal) < 0f);
        });

        Vector2 usable = MegastationMegaShelfPlanner.LandingUsableSize(
            body, Vector3.UnitX, Vector3.UnitZ);
        Assert.Equal(body.Size.X - MegastationMegaShelfPlanner.LandingEdgeMargin * 2f - 8f,
            usable.X, 3);
        Assert.Equal(body.Size.Z - MegastationMegaShelfPlanner.LandingEdgeMargin * 2f - 8f,
            usable.Y, 3);
        Assert.NotNull(MegastationMegaShelfPlanner.ExposedCornerPoint(body));
    }

    [Fact]
    public void EdgeMarkersUseNonMirroredFramesOnAllCardinalAndChamferEdges()
    {
        MegastationInteriorStructuralSolid body = Shelf(
            new(260f, 3f, 190f), clip: 8f, clipSide: 1);
        MegastationBayWallSurface rear = Wall(
            "rear", MegastationBayWallKind.Rear, Vector3.UnitZ);
        MegastationBayWallSurface left = Wall(
            "left", MegastationBayWallKind.Left, Vector3.UnitX);
        MegastationBayWallSurface right = Wall(
            "right", MegastationBayWallKind.Right, -Vector3.UnitX);

        MegastationMegaShelfMarker[] markers =
            MegastationMegaShelfPlanner.BuildMarkers(
                "span", body with { ExposedCornerClip = 0f, ExposedCornerRightSign = 0 },
                731, MegastationMegaShelfFamily.FullSpan, [left, right]).Concat(
            MegastationMegaShelfPlanner.BuildMarkers(
                "corner-left", body, 732, MegastationMegaShelfFamily.Corner, [rear, left])).Concat(
            MegastationMegaShelfPlanner.BuildMarkers(
                "corner-right", body with
                {
                    ExposedCornerRightSign = -1,
                }, 733, MegastationMegaShelfFamily.Corner, [rear, right]))
            .ToArray();

        Assert.Contains(markers, marker => Vector3.Dot(marker.Forward, Vector3.UnitZ) > .99f);
        Assert.Contains(markers, marker => Vector3.Dot(marker.Forward, -Vector3.UnitZ) > .99f);
        Assert.Contains(markers, marker => Vector3.Dot(marker.Forward, Vector3.UnitX) > .99f);
        Assert.Contains(markers, marker => Vector3.Dot(marker.Forward, -Vector3.UnitX) > .99f);
        Assert.Contains(markers, marker => marker.Identity.Contains("corner-chamfer"));
        Assert.All(markers, marker => Assert.True(
            Vector3.Dot(Vector3.Cross(marker.Right, marker.Up), marker.Forward) > .999f));

        static MegastationBayWallSurface Wall(
            string identity, MegastationBayWallKind kind, Vector3 normal)
            => new(identity, kind, Vector3.Zero, normal,
                Vector3.Normalize(Vector3.Cross(Vector3.UnitY, normal)),
                Vector3.UnitY, 800f, 600f, true);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void BookcaseLevelsHaveEqualClearIntervals(int levelCount)
    {
        const float minimum = -300f;
        const float maximum = 360f;
        const float thickness = 3f;
        float[] tops = MegastationMegaShelfPlanner.BookcaseElevations(
            minimum, maximum, levelCount, thickness);

        Assert.Equal(levelCount, tops.Length);
        float[] clear = new float[levelCount - 1];
        for (int index = 1; index < levelCount; index++)
            clear[index - 1] = tops[index] - thickness - tops[index - 1];
        Assert.All(clear, value => Assert.Equal(clear[0], value, 3));
        Assert.True(tops[0] - thickness - minimum >= 25f);
        Assert.True(maximum - tops[^1] >= 80f);
    }

    [Fact]
    public void BookcaseRejectsOneTwoOrUnsafeLevelsAndUsesPiTrussGrammar()
    {
        Assert.Empty(MegastationMegaShelfPlanner.BookcaseElevations(
            0f, 500f, 2, 3f));
        Assert.Empty(MegastationMegaShelfPlanner.BookcaseElevations(
            0f, 250f, 3, 3f));
        MegastationInteriorStructuralSolid body = Shelf(new(500f, 3f, 110f));
        IReadOnlyList<MegastationMegaShelfTruss> trusses =
            MegastationMegaShelfPlanner.BuildTrusses(
                "bookcase", body, 8181, force: true,
                family: MegastationMegaShelfFamily.Bookcase,
                forcedCrossSection: StructuralTrussCrossSection.Triangular);

        Assert.Equal(3, trusses.Count);
        Assert.All(trusses, truss => Assert.Equal(
            StructuralTrussCrossSection.Triangular, truss.Spec.CrossSection));
        Assert.Single(trusses, truss => Vector3.Dot(
            new(truss.Spec.Transform.M31, truss.Spec.Transform.M32,
                truss.Spec.Transform.M33), body.Right) > .999f);
        Assert.Equal(2, trusses.Count(truss => Vector3.Dot(
            new(truss.Spec.Transform.M31, truss.Spec.Transform.M32,
                truss.Spec.Transform.M33), body.Forward) > .999f));
    }

    [Fact]
    public void CantileverAlwaysUsesOneFamilyAcrossUnsupportedEdgeU()
    {
        MegastationInteriorStructuralSolid body = Shelf(new(300f, 4f, 180f));
        IReadOnlyList<MegastationMegaShelfTruss> trusses =
            MegastationMegaShelfPlanner.BuildTrusses(
                "cantilever", body, 55119, force: false,
                family: MegastationMegaShelfFamily.Cantilever);

        Assert.Equal(3, trusses.Count);
        Assert.Single(trusses.Select(truss => truss.Spec.CrossSection).Distinct());
        MegastationMegaShelfTruss[] sideRuns = trusses.Where(truss =>
            Vector3.Dot(Axis(truss), body.Forward) > .999f).ToArray();
        Assert.Equal(2, sideRuns.Length);
        Assert.Contains(sideRuns, truss => LocalRight(truss, body) < -140f);
        Assert.Contains(sideRuns, truss => LocalRight(truss, body) > 140f);
        MegastationMegaShelfTruss front = Assert.Single(trusses, truss =>
            Vector3.Dot(Axis(truss), body.Right) > .999f);
        Assert.True(LocalForward(front, body) > 80f);
    }

    [Fact]
    public void CornerSupportsOnlyPrincipalExposedFrontAndOuterSide()
    {
        MegastationInteriorStructuralSolid body = Shelf(
            new Vector3(300f, 4f, 180f), clip: 30f, clipSide: 1);
        IReadOnlyList<MegastationMegaShelfTruss> trusses =
            MegastationMegaShelfPlanner.BuildTrusses(
                "corner", body, 66881, force: false,
                family: MegastationMegaShelfFamily.Corner);

        Assert.Equal(2, trusses.Count);
        Assert.Single(trusses.Select(truss => truss.Spec.CrossSection).Distinct());
        MegastationMegaShelfTruss front = Assert.Single(trusses, truss =>
            Vector3.Dot(Axis(truss), body.Right) > .999f);
        MegastationMegaShelfTruss side = Assert.Single(trusses, truss =>
            Vector3.Dot(Axis(truss), body.Forward) > .999f);
        Assert.True(LocalForward(front, body) > 80f);
        Assert.True(LocalRight(side, body) > 140f);
        Assert.True(front.Spec.Length < body.Size.X);
        Assert.True(side.Spec.Length < body.Size.Z);
    }

    private static Vector3 Axis(MegastationMegaShelfTruss truss) => new(
        truss.Spec.Transform.M31,
        truss.Spec.Transform.M32,
        truss.Spec.Transform.M33);

    private static float LocalRight(
        MegastationMegaShelfTruss truss,
        MegastationInteriorStructuralSolid body)
        => Vector3.Dot(truss.Spec.Transform.Translation - body.Centre, body.Right);

    private static float LocalForward(
        MegastationMegaShelfTruss truss,
        MegastationInteriorStructuralSolid body)
        => Vector3.Dot(truss.Spec.Transform.Translation - body.Centre, body.Forward);

    private static MegastationInteriorStructuralSolid Shelf(
        Vector3 size,
        float clip = 0f,
        int clipSide = 0) => new(
            "shelf/body", MegastationInteriorStructuralRole.MegaShelf,
            Vector3.Zero, size, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ,
            true, true, clip, clipSide);
}
