using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Inferior.Game.States;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace Inferior.Game.Test;

public sealed class MegastationInfrastructureTests
{
    private const string NovaAnchorageId = "Oranae:Oranae I:Nova Anchorage";

    [Fact]
    public void SharedPlanarRegionsPreserveG1ProjectionAndExposeUnfilteredSubstrate()
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu(NovaAnchorageId);
        MegastationPlanarRegion[] reversed = MegastationPlanarRegionExtractor.Extract(
            result.Grid,
            result.BoundaryTopology,
            Reverse(result.SemanticZoning));
        MegastationAttachmentPlan rebuilt = MegastationAttachmentPlanner.Plan(
            result.Grid,
            result.RegularisedOccupancy,
            reversed);

        Assert.Equal(result.PlanarRegions.Select(RegionSignature), reversed.Select(RegionSignature));
        Assert.True(result.PlanarRegions.Count > result.AttachmentPlan.CandidateSurfaces.Count);
        Assert.Equal(
            result.AttachmentPlan.CandidateSurfaces.Select(AttachmentSurfaceSignature),
            rebuilt.CandidateSurfaces.Select(AttachmentSurfaceSignature));
        Assert.Equal(result.AttachmentPlan.Placements, rebuilt.Placements);
        Assert.Equal(result.AttachmentPlan.Reservations, rebuilt.Reservations);
        Assert.All(result.PlanarRegions, region =>
        {
            Assert.NotEmpty(region.ExactMask);
            Assert.NotEmpty(region.Faces);
            Assert.True(region.PhysicalArea > 0f);
            Assert.True(region.PhysicalExtents.X > 0f);
            Assert.True(region.PhysicalExtents.Y > 0f);
        });
    }

    [Fact]
    public void NovaInfrastructureIsDeterministicBatchedTextureFreeAndInSanityRange()
    {
        MegastationPrototypeCpuResult a = MegastationPrototypeGenerator.GenerateCpu(NovaAnchorageId);
        MegastationInfrastructurePlan b = MegastationInfrastructurePlanner.Plan(
            a.PlanarRegions.Reverse().ToArray(),
            a.AttachmentPlan,
            a.WindowPlan,
            a.LightPlan,
            a.ServiceChannelPlan,
            a.MegaGreeblePlan);
        MegastationInfrastructureMeshBuildResult rebuilt = MegastationInfrastructureMeshBuilder.Build(b);

        Assert.Equal(a.InfrastructurePlan.Clusters.Select(ClusterSignature), b.Clusters.Select(ClusterSignature));
        Assert.Equal(a.InfrastructurePlan.Instances, b.Instances);
        Assert.Equal(a.InfrastructureMesh.ToIntArrays().verts, rebuilt.Mesh.ToIntArrays().verts);
        Assert.Equal(a.InfrastructureMesh.ToIntArrays().indices, rebuilt.Mesh.ToIntArrays().indices);
        Assert.True(a.InfrastructurePlan.Diagnostics.ClusterCount > 120);
        Assert.True(a.InfrastructurePlan.Diagnostics.ClusterCount
            <= MegastationInfrastructureTuning.Default.StationClusterCap);
        Assert.True(a.InfrastructurePlan.Diagnostics.PrimitiveCount
            >= a.InfrastructurePlan.Diagnostics.ClusterCount * 5);
        Assert.True(a.InfrastructurePlan.Diagnostics.VisibleVertexCount > 0);
        Assert.True(a.InfrastructurePlan.Diagnostics.ShadowVertexCount > 0);
        Assert.True(a.InfrastructurePlan.Diagnostics.ShadowVertexCount
            < a.InfrastructurePlan.Diagnostics.VisibleVertexCount);

        PlacedModule module = MegastationPrototypeGenerator.CreatePlacedModule(a);
        Assert.Same(a.InfrastructureMesh, module.Mesh);
        Assert.True(module.HasNativeMegastationInfrastructure);
        Assert.NotNull(module.HullMesh);
        Assert.NotNull(module.GlassMesh);
        VertexPositionColor[] debugLines = Assert.IsType<VertexPositionColor[]>(
            module.NativeInfrastructureDebugLines);
        Assert.NotEmpty(debugLines);
        Assert.Contains(debugLines, vertex => vertex.Color == Color.Magenta);
        Assert.Contains(debugLines, vertex => vertex.Color == Color.Cyan);
        Assert.Contains(debugLines, vertex => vertex.Color == Color.Lime);
        Assert.Contains(debugLines, vertex => vertex.Color == Color.Orange);

        Console.WriteLine(Summary(a.InfrastructurePlan.Diagnostics));
        Console.WriteLine(PhysicalSummary(a.InfrastructurePlan));
    }

    [Fact]
    public void StructuralRoleProducesNoInfrastructureAndRoleDensityIsSemantic()
    {
        MegastationInfrastructureTuning tuning = DenseTuning();
        MegastationInfrastructurePlan structural = Plan(
            [Region("structural", MegastationZoneRole.Structural, 240f, 240f)], tuning);
        MegastationInfrastructurePlan industrial = Plan(
            [Region("industrial", MegastationZoneRole.Industrial, 480f, 480f)],
            MegastationInfrastructureTuning.Default);
        MegastationInfrastructurePlan habitation = Plan(
            [Region("habitation", MegastationZoneRole.Habitation, 480f, 480f)],
            MegastationInfrastructureTuning.Default);

        Assert.Empty(structural.Clusters);
        Assert.True(industrial.Clusters.Count > habitation.Clusters.Count);
        Assert.True(industrial.Diagnostics.ByRole[MegastationZoneRole.Industrial].ClusterCount > 0);
        Assert.Equal(0, habitation.Diagnostics.ByRole[MegastationZoneRole.Industrial].ClusterCount);
    }

    [Fact]
    public void TopologySuitabilityRewardsRecessAndSuppressesExposedProminence()
    {
        MegastationPlanarRegion recessed = Region(
            "recessed", MegastationZoneRole.Utilities, 100f, 100f,
            prominence: 0.1f, exposure: 0.1f, depth: 0.9f, concavity: 0.9f);
        MegastationPlanarRegion exposed = Region(
            "exposed", MegastationZoneRole.Utilities, 100f, 100f,
            prominence: 0.95f, exposure: 0.95f, depth: 0.05f, concavity: 0.05f);

        Assert.True(MegastationInfrastructurePlanner.TopologySuitability(recessed)
            > MegastationInfrastructurePlanner.TopologySuitability(exposed));
    }

    [Fact]
    public void PhysicalAreaNotFaceCountDeterminesEquivalentCellOpportunities()
    {
        MegastationPlanarRegion single = Region("same", MegastationZoneRole.Industrial, 240f, 160f);
        MegastationPlanarRegion split = single with
        {
            Faces =
            [
                new(0, 0, 0, GridDirection.PositiveZ),
                new(1, 0, 0, GridDirection.PositiveZ),
            ],
            ExactMask =
            [
                new(new(0, 0, 0, GridDirection.PositiveZ), 0f, 120f, 0f, 160f),
                new(new(1, 0, 0, GridDirection.PositiveZ), 120f, 240f, 0f, 160f),
            ],
        };

        MegastationInfrastructurePlan a = Plan([single], DenseTuning());
        MegastationInfrastructurePlan b = Plan([split], DenseTuning());

        Assert.Equal(a.Diagnostics.CandidateCellCount, b.Diagnostics.CandidateCellCount);
        Assert.Equal(a.Clusters.Select(ClusterSignature), b.Clusters.Select(ClusterSignature));
        Assert.Equal(a.Instances, b.Instances);
    }

    [Fact]
    public void ExactMaskHolesAndSafetyCapsAreEnforced()
    {
        MegastationPlanarRegion region = Region("mask", MegastationZoneRole.Industrial, 240f, 240f) with
        {
            ExactMask =
            [
                new(new(0, 0, 0, GridDirection.PositiveZ), 0f, 100f, 0f, 240f),
                new(new(1, 0, 0, GridDirection.PositiveZ), 140f, 240f, 0f, 240f),
            ],
            PhysicalArea = 48_000f,
        };
        MegastationInfrastructureTuning tuning = DenseTuning() with
        {
            StationClusterCap = 2,
            ZoneClusterCap = 20,
        };

        MegastationInfrastructurePlan plan = Plan([region], tuning);

        Assert.Equal(2, plan.Clusters.Count);
        Assert.True(plan.Diagnostics.ExactMaskRejectCount > 0);
        Assert.True(plan.Diagnostics.StationCapRejectCount > 0);
        Assert.All(plan.Clusters, cluster =>
            Assert.True(MegastationPlanarRegionExtractor.ContainsFootprint(
                region, cluster.MinU, cluster.MaxU, cluster.MinV, cluster.MaxV, 1f)));
    }

    [Fact]
    public void G1WindowLightAndEarlierG2ReservationsRejectWithoutRerollingOthers()
    {
        MegastationPlanarRegion region = Region("reservations", MegastationZoneRole.Industrial, 480f, 480f);
        MegastationInfrastructureTuning tuning = DenseTuning() with
        {
            StationClusterCap = 40,
            ZoneClusterCap = 40,
            MinimumClusterSeparationMetres = 110f,
        };
        MegastationInfrastructurePlan baseline = Plan([region], tuning);
        MegastationInfrastructureCluster target = baseline.Clusters.First();
        MegastationInfrastructureCluster survivor = baseline.Clusters.Last();

        MegastationAttachmentPlan g1 = EmptyAttachment() with
        {
            Placements =
            [
                new("g1", region.StableId, region.ZoneId, region.ZoneRole,
                    "fixture", "fixture", 0, 1, Matrix.Identity,
                    target.AabbMin - Vector3.One, target.AabbMax + Vector3.One,
                    new("g1", region.Direction, region.PlaneCoordinateMetres,
                        region.OutwardNormal, region.TangentU, region.TangentV,
                        target.MinU - 1f, target.MaxU + 1f, target.MinV - 1f, target.MaxV + 1f)),
            ],
            Reservations =
            [
                new("g1", region.Direction, region.PlaneCoordinateMetres,
                    region.OutwardNormal, region.TangentU, region.TangentV,
                    target.MinU - 1f, target.MaxU + 1f, target.MinV - 1f, target.MaxV + 1f),
            ],
        };
        MegastationWindowPlan windows = EmptyWindows() with
        {
            Windows =
            [
                new("window", "region", "block", target.SurfacePosition,
                    region.OutwardNormal, region.TangentV,
                    target.MaxU - target.MinU, target.MaxV - target.MinV,
                    MegastationWindowState.Lit, Color.White),
            ],
        };
        MegastationLightPlan lights = EmptyLights() with
        {
            Lights =
            [
                new("light", "cluster", "region", region.ZoneRole,
                    region.Faces[0], target.SurfacePosition, region.OutwardNormal,
                    target.SurfacePosition + region.OutwardNormal * 0.06f,
                    Color.White, GlowType.AmbientMarker, 1f, 0f, 0f,
                    LightPattern.Continuous),
            ],
        };

        MegastationInfrastructurePlan g1Blocked = MegastationInfrastructurePlanner.Plan(
            [region], g1, EmptyWindows(), EmptyLights(), tuning);
        MegastationInfrastructurePlan windowBlocked = MegastationInfrastructurePlanner.Plan(
            [region], EmptyAttachment(), windows, EmptyLights(), tuning);
        MegastationInfrastructurePlan lightBlocked = MegastationInfrastructurePlanner.Plan(
            [region], EmptyAttachment(), EmptyWindows(), lights, tuning);

        Assert.DoesNotContain(g1Blocked.Clusters, cluster => cluster.Identity == target.Identity);
        Assert.DoesNotContain(windowBlocked.Clusters, cluster => cluster.Identity == target.Identity);
        Assert.DoesNotContain(lightBlocked.Clusters, cluster => cluster.Identity == target.Identity);
        Assert.Contains(g1Blocked.Clusters, cluster => cluster.Identity == survivor.Identity);
        Assert.Contains(windowBlocked.Clusters, cluster => cluster.Identity == survivor.Identity);
        Assert.Contains(lightBlocked.Clusters, cluster => cluster.Identity == survivor.Identity);
        Assert.True(g1Blocked.Diagnostics.G1RejectCount > 0);
        Assert.True(windowBlocked.Diagnostics.WindowRejectCount > 0);
        Assert.True(lightBlocked.Diagnostics.LightRejectCount > 0);
        Assert.True(baseline.Diagnostics.SpacingRejectCount > 0);
    }

    [Fact]
    public void EmittersOrientOnAllSixDirectionsAndSelectiveShadowExcludesVents()
    {
        Vector3[] normals =
        [
            Vector3.UnitX, -Vector3.UnitX,
            Vector3.UnitY, -Vector3.UnitY,
            Vector3.UnitZ, -Vector3.UnitZ,
        ];
        foreach (Vector3 normal in normals)
        {
            (Vector3 u, Vector3 v) = Frame(normal);
            MegastationInfrastructureInstance[] instances =
            [
                Instance("housing", MegastationInfrastructureFamily.MachineryHousing,
                    normal, u, v, 7f, 5f, 4f, casts: true),
                Instance("vent", MegastationInfrastructureFamily.Ventilation,
                    normal, u, v, 4f, 2f, 0.2f, casts: false),
                Instance("tank", MegastationInfrastructureFamily.Tank,
                    normal, u, v, 3f, 8f, 3f, casts: true),
            ];
            MegastationInfrastructurePlan plan = ManualPlan(instances);
            MegastationInfrastructureMeshBuildResult result = MegastationInfrastructureMeshBuilder.Build(plan);
            var (vertices, indices) = result.Mesh.ToIntArrays();

            Assert.NotEmpty(vertices);
            Assert.NotEmpty(indices);
            Assert.All(vertices, vertex => Assert.True(float.IsFinite(vertex.Position.X)
                && float.IsFinite(vertex.Position.Y) && float.IsFinite(vertex.Position.Z)));
            float minimumOutward = vertices.Min(vertex => Vector3.Dot(vertex.Position, normal));
            Assert.True(minimumOutward >= -0.001f,
                $"Infrastructure crossed its support plane for normal {normal}: {minimumOutward}");
            Assert.True(vertices.Max(vertex => Vector3.Dot(vertex.Position, normal)) > 1f);
            Assert.True(result.Diagnostics.ShadowVertexCount > 0);
            Assert.True(result.Diagnostics.ShadowVertexCount < result.Diagnostics.VisibleVertexCount);
            Assert.DoesNotContain(result.Mesh.DecorClassRanges,
                range => range.decorClass == DecorClass.VentGrilles);
            Assert.Contains(result.Mesh.DecorClassRanges,
                range => range.decorClass == DecorClass.MegastationInfrastructureMinor);
            Assert.Contains(result.Mesh.DecorClassRanges,
                range => range.decorClass == DecorClass.MegastationInfrastructureMajor);

            Assert.Equal(Enum.GetValues<MegastationInfrastructureFamily>().Length,
                MegastationInfrastructurePrimitives.ShadowPolicies.Count);
            MegastationShadowFamilyDiagnostics housing = Assert.Single(
                result.Diagnostics.ShadowByFamily, d => d.Family == "MachineryHousing");
            MegastationShadowFamilyDiagnostics vent = Assert.Single(
                result.Diagnostics.ShadowByFamily, d => d.Family == "Ventilation");
            MegastationShadowFamilyDiagnostics tank = Assert.Single(
                result.Diagnostics.ShadowByFamily, d => d.Family == "Tank");
            Assert.Equal(MegastationShadowPolicy.ConditionalSubstantial, housing.Policy);
            Assert.True(housing.VisibleTriangleCount > 0 && housing.CasterTriangleCount > 0);
            Assert.Equal(MegastationShadowPolicy.None, vent.Policy);
            Assert.True(vent.VisibleTriangleCount > 0);
            Assert.Equal(0, vent.CasterTriangleCount);
            Assert.Equal(MegastationShadowPolicy.Simplified, tank.Policy);
            Assert.True(tank.VisibleTriangleCount > 0 && tank.CasterTriangleCount > 0);
            Assert.True(tank.CasterTriangleCount < tank.VisibleTriangleCount);
        }

        Assert.Equal(MegastationShadowPolicy.None,
            MegastationInfrastructurePrimitives.ComponentShadowPolicies["JunctionBox"]);
        Assert.Equal(MegastationShadowPolicy.None,
            MegastationInfrastructurePrimitives.ComponentShadowPolicies["ConduitEntry"]);
        Assert.Equal(MegastationShadowPolicy.None,
            MegastationInfrastructurePrimitives.ComponentShadowPolicies["VentLouverGrille"]);
        Assert.Equal(MegastationShadowPolicy.Simplified,
            MegastationInfrastructurePrimitives.ComponentShadowPolicies["EquipmentHousing"]);
        Assert.Equal(MegastationShadowPolicy.Simplified,
            MegastationInfrastructurePrimitives.ComponentShadowPolicies["TankBodyAndMajorSupports"]);
    }

    [Fact]
    public void NativeInfrastructureReusesItsSingleMeshInMidDepthTierOnly()
    {
        PlacedModule native = Module(hasNativeInfrastructure: true);
        PlacedModule ordinary = Module(hasNativeInfrastructure: false);

        Assert.True(SystemSpaceState.UsesFullDecorationMeshInPass(native, DetailLevel.Full));
        Assert.True(SystemSpaceState.UsesFullDecorationMeshInPass(native, DetailLevel.Medium));
        Assert.False(SystemSpaceState.UsesFullDecorationMeshInPass(native, DetailLevel.Minimal));
        Assert.True(SystemSpaceState.UsesFullDecorationMeshInPass(ordinary, DetailLevel.Full));
        Assert.False(SystemSpaceState.UsesFullDecorationMeshInPass(ordinary, DetailLevel.Medium));
        Assert.False(SystemSpaceState.UsesFullDecorationMeshInPass(ordinary, DetailLevel.Minimal));
    }

    private static MegastationInfrastructurePlan Plan(
        IReadOnlyList<MegastationPlanarRegion> regions,
        MegastationInfrastructureTuning tuning)
        => MegastationInfrastructurePlanner.Plan(
            regions, EmptyAttachment(), EmptyWindows(), EmptyLights(), tuning);

    private static PlacedModule Module(bool hasNativeInfrastructure)
        => new()
        {
            Definition = new StationModuleDefinition
            {
                Id = "fixture",
                Category = "fixture",
                BoundingBox = Vector3.One,
                MinScale = StationScale.Outpost,
                Ports = [],
            },
            Transform = Matrix.Identity,
            Seed = 1,
            ChamferDepth = 0f,
            HasNativeMegastationInfrastructure = hasNativeInfrastructure,
        };

    private static MegastationInfrastructureTuning DenseTuning()
        => MegastationInfrastructureTuning.Default with
        {
            CellSizeMetres = 72f,
            CellJitterMetres = 0f,
            MinimumClusterSeparationMetres = 20f,
            StationClusterCap = 100,
            ZoneClusterCap = 100,
            RoleCaps = Enum.GetValues<MegastationZoneRole>().ToDictionary(role => role, _ => 100),
            RoleDensity = Enum.GetValues<MegastationZoneRole>().ToDictionary(role => role, _ => 1f),
        };

    private static MegastationPlanarRegion Region(
        string identity,
        MegastationZoneRole role,
        float width,
        float height,
        float prominence = 0.25f,
        float exposure = 0.25f,
        float depth = 0.75f,
        float concavity = 0.75f)
    {
        var face = new BoundaryFaceKey(0, 0, 0, GridDirection.PositiveZ);
        return new(
            identity, $"zone:{identity}", 12345, role,
            GridDirection.PositiveZ, 1, 0f, Vector3.Zero,
            Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY,
            new Vector3(width * 0.5f, height * 0.5f, 0f),
            [face], [new(face, 0f, width, 0f, height)], [], [],
            0f, width, 0f, height, width * height, new(width, height),
            prominence, exposure, depth, 0f, concavity, 0.2f);
    }

    private static MegastationAttachmentPlan EmptyAttachment()
        => new([], [], [], new(
            CandidateSurfaceCount: 0,
            SelectedCandidateCount: 0,
            PlacedModuleCount: 0,
            RejectedSupportCount: 0,
            RejectedClearanceCount: 0,
            RejectedSemanticCount: 0,
            HabitationCount: 0,
            IndustrialCount: 0,
            LogisticsCount: 0,
            UtilitiesCount: 0,
            StrategicCount: 0,
            SuppressedWindowCount: 0,
            SuppressedLightCount: 0,
            PlanningMilliseconds: 0,
            ClearanceMilliseconds: 0,
            ModuleFamilyCounts: new Dictionary<string, int>()));

    private static MegastationWindowPlan EmptyWindows()
        => new([], [], [], new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0f, 0f, 0));

    private static MegastationLightPlan EmptyLights()
        => new([], [], [], new(
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0f, 0f, 0f, 0f, 0, 0, 0, 0));

    private static (Vector3 U, Vector3 V) Frame(Vector3 normal)
    {
        Vector3 hint = MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 u = Vector3.Normalize(Vector3.Cross(hint, normal));
        Vector3 v = Vector3.Normalize(Vector3.Cross(normal, u));
        return (u, v);
    }

    private static MegastationInfrastructureInstance Instance(
        string identity,
        MegastationInfrastructureFamily family,
        Vector3 normal,
        Vector3 u,
        Vector3 v,
        float width,
        float height,
        float depth,
        bool casts)
        => new(identity, "cluster", family, 0, Vector3.Zero, normal, u, v,
            width, height, depth, new Color(70, 75, 78), new Color(160, 130, 70), casts);

    private static MegastationInfrastructurePlan ManualPlan(
        IReadOnlyList<MegastationInfrastructureInstance> instances)
    {
        IReadOnlyDictionary<MegastationZoneRole, MegastationInfrastructureRoleDiagnostics> roles =
            Enum.GetValues<MegastationZoneRole>().ToDictionary(
                role => role, _ => new MegastationInfrastructureRoleDiagnostics(0, 0, 0, 0));
        var diagnostics = new MegastationInfrastructureDiagnostics(
            0f, 0f, 0, 0, 1, instances.Count,
            instances.Count(i => i.Family == MegastationInfrastructureFamily.MachineryHousing),
            instances.Count(i => i.Family == MegastationInfrastructureFamily.Ventilation),
            instances.Count(i => i.Family == MegastationInfrastructureFamily.Tank),
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, [], roles);
        return new([], [], instances, diagnostics);
    }

    private static MegastationSemanticZoningResult Reverse(MegastationSemanticZoningResult source)
        => new()
        {
            Anchors = source.Anchors.Reverse().ToArray(),
            Surfaces = source.Surfaces.Reverse().ToArray(),
            SurfaceByFace = source.SurfaceByFace,
            Zones = source.Zones.Reverse().Select(zone => zone with
            {
                Faces = zone.Faces.Reverse().ToArray(),
            }).ToArray(),
            ZoneByFace = source.ZoneByFace,
            DebugIndexGroups = source.DebugIndexGroups,
            Diagnostics = source.Diagnostics,
        };

    private static string Summary(MegastationInfrastructureDiagnostics d)
    {
        string roles = string.Join(',', Enum.GetValues<MegastationZoneRole>()
            .Where(role => role != MegastationZoneRole.Structural)
            .Select(role =>
            {
                MegastationInfrastructureRoleDiagnostics roleDiagnostics = d.ByRole[role];
                return $"{role}:C{roleDiagnostics.ClusterCount}/H{roleDiagnostics.HousingCount}/" +
                    $"V{roleDiagnostics.VentCount}/T{roleDiagnostics.TankCount}";
            }));
        return $"G2 Nova candidateArea={d.CandidateArea:F0}; activeArea={d.ActiveArea:F0}; " +
            $"clusters={d.ClusterCount}; primitives={d.PrimitiveCount}; " +
            $"housings={d.HousingCount}; vents={d.VentCount}; tanks={d.TankCount}; " +
            $"roles={roles}; " +
            $"rejects=mask:{d.ExactMaskRejectCount},g1:{d.G1RejectCount}," +
            $"windows:{d.WindowRejectCount},lights:{d.LightRejectCount}," +
            $"spacing:{d.SpacingRejectCount},density:{d.RoleDensityRejectCount}," +
            $"stationCap:{d.StationCapRejectCount},zoneCap:{d.ZoneCapRejectCount}; " +
            $"visible={d.VisibleVertexCount}v/{d.VisibleTriangleCount}t/{d.VisibleMeshBytes}B; " +
            $"shadow={d.ShadowVertexCount}v/{d.ShadowTriangleCount}t/{d.ShadowMeshBytes}B; " +
            $"shadowFamilies={string.Join(',', d.ShadowByFamily.Select(f => $"{f.Family}:{f.Policy}:{f.ShadowCastingInstanceCount}/{f.VisibleInstanceCount}:{f.CasterVertexCount}v/{f.CasterTriangleCount}t"))}; " +
            $"planningMs={d.PlanningMilliseconds}; meshMs={d.MeshBuildMilliseconds}";
    }

    private static string PhysicalSummary(MegastationInfrastructurePlan plan)
    {
        static string Distribution(IEnumerable<float> source)
        {
            float[] values = source.Order().ToArray();
            float median = values.Length % 2 == 0
                ? (values[values.Length / 2 - 1] + values[values.Length / 2]) * 0.5f
                : values[values.Length / 2];
            return $"{values[0]:F2}/{median:F2}/{values[^1]:F2}";
        }

        static (float width, float outward, float length, float area, float clearance) Dimensions(
            MegastationInfrastructureInstance instance)
        {
            if (instance.Family == MegastationInfrastructureFamily.MachineryHousing)
            {
                float baseDepth = MathF.Min(instance.Depth, MathF.Max(1.0f, instance.Width * 0.45f));
                return (instance.Width, baseDepth * 1.42f, instance.Height,
                    instance.Width * instance.Height, 0f);
            }
            if (instance.Family == MegastationInfrastructureFamily.Ventilation)
                return (instance.Width, MathF.Max(0.22f, instance.Depth)
                    + (instance.Variant == 1 ? 0.067f : 0.031f),
                    instance.Height, instance.Width * instance.Height, 0f);

            float radius = Math.Clamp(instance.Width * 0.5f, 0.6f, 3.4f);
            float length = Math.Clamp(instance.Height, 2f, 15.5f);
            return (radius * 2f, radius * 2f + 0.35f, length + radius,
                radius * 2f * (length + radius), 0f);
        }

        string families = string.Join("; ", Enum.GetValues<MegastationInfrastructureFamily>()
            .Select(family =>
            {
                var dimensions = plan.Instances.Where(instance => instance.Family == family)
                    .Select(Dimensions).ToArray();
                return $"{family}[n={dimensions.Length},width={Distribution(dimensions.Select(x => x.width))}," +
                    $"outward={Distribution(dimensions.Select(x => x.outward))}," +
                    $"length={Distribution(dimensions.Select(x => x.length))}," +
                    $"area={Distribution(dimensions.Select(x => x.area))}," +
                    $"clearance={Distribution(dimensions.Select(x => x.clearance))}]";
            }));
        string compositions = string.Join(',', Enum.GetValues<MegastationInfrastructureArchetype>()
            .Select(archetype =>
            {
                MegastationInfrastructureCluster[] clusters = plan.Clusters
                    .Where(cluster => cluster.Archetype == archetype).ToArray();
                return $"{archetype}:{clusters.Length}/" +
                    (clusters.Length == 0 ? "0" : $"{clusters.Average(c => c.Instances.Count):F1}");
            }));
        return $"G2 physical min/median/max: {families}; " +
            $"clusterWidth={Distribution(plan.Clusters.Select(c => c.MaxU - c.MinU))}; " +
            $"clusterLength={Distribution(plan.Clusters.Select(c => c.MaxV - c.MinV))}; " +
            $"clusterArea={Distribution(plan.Clusters.Select(c => (c.MaxU - c.MinU) * (c.MaxV - c.MinV)))}; " +
            $"piecesPerCluster={Distribution(plan.Clusters.Select(c => (float)c.Instances.Count))}; " +
            $"archetype=count/avgPieces:{compositions}";
    }

    private static object RegionSignature(MegastationPlanarRegion region) => new
    {
        region.StableId,
        region.ZoneId,
        region.ZoneSeed,
        region.ZoneRole,
        region.Direction,
        region.PlaneGridCoordinate,
        region.PlaneCoordinateMetres,
        region.SurfaceOrigin,
        region.OutwardNormal,
        region.TangentU,
        region.TangentV,
        region.PhysicalCentre,
        Faces = string.Join('|', region.Faces),
        Mask = string.Join('|', region.ExactMask),
        Boundary = string.Join('|', region.BoundaryEdges),
        Adjacent = string.Join('|', region.AdjacentFaces),
        region.MinU,
        region.MaxU,
        region.MinV,
        region.MaxV,
        region.PhysicalArea,
        region.PhysicalExtents,
        region.Prominence,
        region.Exposure,
        region.RelativeDepth,
        region.Height,
        region.Concavity,
        region.Extremity,
    };

    private static object ClusterSignature(MegastationInfrastructureCluster cluster) => new
    {
        cluster.Identity,
        cluster.SurfaceStableId,
        cluster.ZoneId,
        cluster.ZoneRole,
        cluster.CellU,
        cluster.CellV,
        cluster.Archetype,
        cluster.SurfacePosition,
        cluster.Normal,
        cluster.TangentU,
        cluster.TangentV,
        cluster.MinU,
        cluster.MaxU,
        cluster.MinV,
        cluster.MaxV,
        cluster.AabbMin,
        cluster.AabbMax,
        Instances = string.Join('|', cluster.Instances.Select(instance => instance.Identity)),
    };

    private static object AttachmentSurfaceSignature(MegastationAttachmentSurface surface) => new
    {
        surface.StableId,
        surface.ZoneId,
        surface.ZoneSeed,
        surface.ZoneRole,
        surface.Direction,
        surface.PlaneGridCoordinate,
        surface.PlaneCoordinateMetres,
        surface.OutwardNormal,
        surface.TangentU,
        surface.TangentV,
        surface.PhysicalCentre,
        Faces = string.Join('|', surface.Faces),
        Mask = string.Join('|', surface.SupportMask),
        surface.PhysicalArea,
        surface.PhysicalExtents,
        surface.Prominence,
        surface.Exposure,
        surface.Concavity,
        surface.Extremity,
        surface.MaximumSupportedFootprint,
        surface.ExteriorClearanceDepth,
    };
}
