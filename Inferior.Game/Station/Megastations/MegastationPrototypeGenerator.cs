using System.Diagnostics;
using Inferior.Galaxy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.StationGen.Megastations;

public sealed record MegastationPrototypeDiagnostics(
    string StationPersistenceId,
    int GeneratorVersion,
    int RootSeed,
    int XSliceCount,
    int YSliceCount,
    int ZSliceCount,
    int GridCellCount,
    int StructuralOccupiedCellCount,
    int UrbanOccupiedCellCount,
    int UrbanizedFaceCount,
    int FaceRegionOccupiedCellCount,
    int EdgeRegionOccupiedCellCount,
    int CornerRegionOccupiedCellCount,
    int DistrictCount,
    int MaximumUrbanDepth,
    IReadOnlyList<string> PerFaceSummary,
    IReadOnlyList<string> PerEdgeSummary,
    IReadOnlyList<string> PerCornerSummary,
    int ConnectedComponentsBeforeValidation,
    int RemovedDisconnectedCells,
    bool HasSealedCavity,
    int ExposedQuadCount,
    int TriangleCount,
    int VertexCount,
    int MeshPageCount,
    long GenerationMilliseconds);

public sealed record MegastationPrototypeResult(
    List<PlacedModule> Modules,
    IReadOnlyList<Texture2D> PanelTextures,
    MegastationPrototypeDiagnostics Diagnostics);

public sealed record MegastationPrototypeCpuResult(
    SliceGrid Grid,
    StructuralOccupancy Occupancy,
    IReadOnlyList<SurfacePatch> Patches,
    IReadOnlyList<UrbanGrowthResult> Faces,
    IReadOnlyList<EdgeRegionPlan> Edges,
    IReadOnlyList<CornerRegionPlan> Corners,
    StationModuleMesh Mesh,
    MegastationMeshStats MeshStats,
    MegastationPrototypeDiagnostics Diagnostics);

public static class MegastationPrototypeGenerator
{
    public static MegastationPrototypeResult Generate(Galaxy.Station station, GraphicsDevice gd, MegastationPrototypeSettings? settings = null)
    {
        settings ??= MegastationPrototypeSettings.Default;
        var sw = Stopwatch.StartNew();
        string id = station.PersistenceId ?? station.Name;
        MegastationPrototypeCpuResult cpu = GenerateCpu(id, settings, sw);

        Vector3 bounds = new(cpu.Grid.Dimension(GridAxis.X), cpu.Grid.Dimension(GridAxis.Y), cpu.Grid.Dimension(GridAxis.Z));
        var def = new StationModuleDefinition
        {
            Id = "megastation-prototype-b",
            Category = "megastation-prototype",
            BoundingBox = bounds,
            MinScale = StationScale.Outpost,
            Ports = [],
            MeshFactory = _ => (new StationModuleMesh(), new StationModuleMesh()),
        };
        var module = new PlacedModule
        {
            Definition = def,
            Transform = Matrix.Identity,
            Seed = cpu.Diagnostics.RootSeed,
            ChamferDepth = 0f,
            AabbMin = bounds * -0.5f,
            AabbMax = bounds * 0.5f,
            HullMesh = cpu.Mesh,
        };

        var albedo = MakeFlat(gd, Color.White);
        var material = MakeFlat(gd, new Color(128, 255, 0, 0));
        module.TextureInstance = albedo;
        module.MaterialInstance = material;
        return new MegastationPrototypeResult([module], [albedo, material], cpu.Diagnostics);
    }

    public static MegastationPrototypeCpuResult GenerateCpu(
        string persistenceId,
        MegastationPrototypeSettings? settings = null,
        Stopwatch? stopwatch = null)
    {
        settings ??= MegastationPrototypeSettings.Default;
        stopwatch ??= Stopwatch.StartNew();
        int rootSeed = MegastationSeed.Root(persistenceId, settings.GeneratorVersion);

        var grid = SliceGrid.Create(settings, MegastationSeed.Derive(rootSeed, "slice-grid layout"));
        var occupancy = new CuboidStructuralVolumeGenerator().Generate(grid);
        ExteriorSpace.ClassifyExternallyAccessibleEmpty(occupancy);
        var patches = SurfacePatchFinder.FindPatches(occupancy);
        var style = MegastationUrbanStyle.Generate(rootSeed);
        var corners = CornerRegionGenerator.PlanCorners(grid, settings, style, rootSeed);
        CornerRegionGenerator.Apply(occupancy, corners);
        var edges = EdgeRegionGenerator.PlanEdges(grid, settings, style, corners, rootSeed);
        EdgeRegionGenerator.Apply(occupancy, edges);

        var faceResults = new List<UrbanGrowthResult>(6);
        foreach (var patch in patches.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            var faceSettings = MegastationFaceSettings.ForPatch(settings, style, grid, patch, rootSeed);
            int faceSeed = patch.Direction == settings.UrbanPatchNormal
                ? MegastationSeed.Derive(rootSeed, "district layout")
                : MegastationSeed.Derive(rootSeed, $"district layout:{patch.Id}");
            faceResults.Add(UrbanGrowth.Generate(occupancy, patch, faceSettings, faceSeed));
        }

        var validation = MegastationConnectivity.Validate(occupancy);
        var mesh = new StationModuleMesh();
        var meshStats = MegastationPrototypeMeshBuilder.Build(occupancy, mesh);
        stopwatch.Stop();

        int districtCount = faceResults.Sum(f => f.Districts.Count);
        int maxDepth = faceResults.Count == 0 ? 0 : faceResults.Max(f => f.MaximumDepth);

        var diag = new MegastationPrototypeDiagnostics(
            persistenceId,
            settings.GeneratorVersion,
            rootSeed,
            grid.XCount,
            grid.YCount,
            grid.ZCount,
            grid.CellCount,
            occupancy.StructuralOccupiedCount,
            occupancy.UrbanOccupiedCount,
            faceResults.Count(f => f.Districts.Count > 0),
            occupancy.FaceRegionOccupiedCount,
            occupancy.EdgeRegionOccupiedCount,
            occupancy.CornerRegionOccupiedCount,
            districtCount,
            maxDepth,
            faceResults.Select(f => $"{RegionIdentity.Face(f.Patch.Direction)} districts={f.Districts.Count} maxDepth={f.MaximumDepth}").ToArray(),
            edges.Select(e => $"{e.Id} {e.ProfileSummary} start=({e.StartCornerDepthA},{e.StartCornerDepthB}) end=({e.EndCornerDepthA},{e.EndCornerDepthB})").ToArray(),
            corners.Select(c => $"{c.Id} {c.Summary} extents=({c.DepthA},{c.DepthB},{c.DepthC})").ToArray(),
            validation.ConnectedComponentsBeforeValidation,
            validation.RemovedDisconnectedCells,
            validation.HasSealedCavity,
            meshStats.ExposedQuadCount,
            meshStats.TriangleCount,
            meshStats.VertexCount,
            meshStats.MeshPageCount,
            stopwatch.ElapsedMilliseconds);

        return new MegastationPrototypeCpuResult(grid, occupancy, patches, faceResults, edges, corners, mesh, meshStats, diag);
    }

    private static Texture2D MakeFlat(GraphicsDevice gd, Color color)
    {
        var tex = new Texture2D(gd, 1, 1);
        tex.SetData([color]);
        return tex;
    }
}
