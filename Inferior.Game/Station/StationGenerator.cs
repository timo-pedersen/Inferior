using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Inferior.Core.Math;
using Inferior.Core.Random;
using Inferior.Galaxy;
using Inferior.Game.StationGen.Megastations;
using Inferior.Rendering;

namespace Inferior.Game.StationGen;

// Uploaded panel textures are package-owned rather than globally cached. PanelTextures is
// the disposal manifest installed into the resident StationVisualPackage; modules point
// directly at the corresponding entries.
public sealed record StationGenerationResult(
    List<PlacedModule> Modules,
    IReadOnlyList<Texture2D> PanelTextures,
    MegastationPrototypeDiagnostics? MegastationDiagnostics = null);

public sealed record StationMeshCpuData(
    VertexPositionNormalColorTexture[] Vertices,
    int[] Indices);

public sealed record PreparedStationTexture(int Width, int Height, Color[] Pixels);

public sealed record StationTextureAssignment(
    PlacedModule Module,
    int AlbedoTextureIndex,
    int MaterialTextureIndex);

public sealed record StationTexturePreparationDiagnostics(
    int GeneratedTextureCount,
    int GeneratedVariantPairCount,
    int SelectedUniqueTextureCount,
    int SelectedUniqueTexturePairCount,
    int DiscardedTextureCount,
    int UploadedAlbedoTextureCount,
    int UploadedMaterialTextureCount,
    int ModuleTextureBindingCount,
    int SharedFallbackReferenceCount);

internal sealed record StationTextureCompactionResult(
    IReadOnlyList<PreparedStationTexture> Textures,
    IReadOnlyList<StationTextureAssignment> Assignments,
    StationTexturePreparationDiagnostics Diagnostics);

public sealed record StationGenerationCpuResult(
    List<PlacedModule> Modules,
    IReadOnlyList<PreparedStationTexture> Textures,
    IReadOnlyList<StationTextureAssignment> TextureAssignments,
    IReadOnlyDictionary<PlacedModule, StationMeshCpuData> FlatDecorationMeshes,
    IReadOnlyList<StationVisualUploadPlanItem> UploadPlan,
    MegastationPrototypeDiagnostics? MegastationDiagnostics,
    double GenerationMilliseconds,
    StationTexturePreparationDiagnostics TextureDiagnostics,
    bool UsesSharedMegastationFallbackTextures = false);

/// <summary>
/// Procedural station builder. Grows a station by attaching modules port-to-port,
/// computing exact 3D alignment for each connection and rejecting overlaps.
/// </summary>
public sealed class StationGenerator
{
    private readonly SeededRandom     _rng;
    private readonly int              _seed;
    private readonly List<PlacedModule> _placed = [];

    // Reserved approach corridor in front of a docking bay's door — kept separate from _placed
    // so every pass that iterates _placed expecting real modules (PrepareTextures, BakeLighting,
    // ValidatePlacement, PopulateLandingPads) needs no changes. Only IntersectsAny checks it.
    private readonly List<(Vector3 min, Vector3 max)> _reservedVolumes = [];

    // _seed is kept separately from _rng (which mutates as it's drawn from) so the docking bay's
    // pad-mix/envelope can be derived deterministically without depending on how many other draws
    // happened first — needed before the bay's own module seed exists (see Run()).
    private StationGenerator(int seed) { _rng = new SeededRandom(seed); _seed = seed; }

    // Chamfer bevel depth, seeded per module (5-50cm) — single source of truth read by
    // BuildHullMesh, GenerateEdgeTrimStrips, GeneratePanelSeams, and PlaceContainer.
    // XOR salt keeps this independent of any other value already derived from the same
    // module seed elsewhere.
    internal static float ChamferDepthForSeed(int seed)
        => 0.05f + (float)new System.Random(seed ^ 0x43484D46).NextDouble() * 0.45f;

    // gameTime is no longer read internally — it fed the world-space rotation BakeLighting
    // used for its N.L bake, which is gone now that the sun term is computed per frame in
    // LitSurface.fx (Docs/station-lighting-pipeline-spec.md Phase A). Kept on the signature
    // rather than touching this public entry point's call site for a lighting-only brief.
    public static StationGenerationResult Generate(
                                               Galaxy.Station station, GraphicsDevice gd,
                                               double gameTime = 0.0,
                                               bool useMegastationPrototype = false)
    {
        StationGenerationCpuResult prepared = PrepareCpu(station, useMegastationPrototype);
        StationGenerationResult result = UploadPrepared(station, prepared, gd);
        PopulateLandingPads(station, result.Modules);
        return result;
    }

    public static StationGenerationCpuResult PrepareCpu(
        Galaxy.Station station,
        bool useMegastationPrototype = false,
        CancellationToken cancellationToken = default,
        IReadOnlySet<DecorClass>? enabledShadowCasterClasses = null)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        enabledShadowCasterClasses ??= StationDecorator.DecorCastingPolicy
            .Where(pair => pair.Value)
            .Select(pair => pair.Key)
            .ToHashSet();
        if (useMegastationPrototype)
        {
            string identity = station.PersistenceId ?? station.Name;
            MegastationPrototypeCpuResult cpu = MegastationPrototypeGenerator.GenerateCpu(
                identity,
                stopwatch: stopwatch,
                cancellationToken: cancellationToken);
            PlacedModule module = MegastationPrototypeGenerator.CreatePlacedModule(cpu);
            List<PlacedModule> megaModules = [module];
            PreparedStationTexture[] megaTextures = [];
            StationTextureAssignment[] megaAssignments = [];
            var megaFlatMeshes = new Dictionary<PlacedModule, StationMeshCpuData>();
            IReadOnlyList<StationVisualUploadPlanItem> megaUploadPlan = BuildUploadPlan(
                megaModules,
                megaTextures,
                megaAssignments,
                megaFlatMeshes,
                enabledShadowCasterClasses,
                cancellationToken);
            return new StationGenerationCpuResult(
                megaModules,
                megaTextures,
                megaAssignments,
                megaFlatMeshes,
                megaUploadPlan,
                cpu.Diagnostics,
                stopwatch.Elapsed.TotalMilliseconds,
                new(
                    GeneratedTextureCount: 0,
                    GeneratedVariantPairCount: 0,
                    SelectedUniqueTextureCount: 0,
                    SelectedUniqueTexturePairCount: 1,
                    DiscardedTextureCount: 0,
                    UploadedAlbedoTextureCount: 0,
                    UploadedMaterialTextureCount: 0,
                    ModuleTextureBindingCount: 2,
                    SharedFallbackReferenceCount: 2),
                UsesSharedMegastationFallbackTextures: true);
        }

        int seed = NameHash(station.Name);
        var generator = new StationGenerator(seed);
        StationScale scale = station.Size switch
        {
            StationSize.Small => StationScale.Outpost,
            StationSize.Medium => StationScale.Station,
            StationSize.Large => StationScale.Port,
            _ => StationScale.Outpost,
        };

        var modules = generator.Run(station);
        ValidatePlacement(modules);
        cancellationToken.ThrowIfCancellationRequested();

        var profile = StationProfile.Generate(seed, scale);
        var palette = TexturePalette.From(profile);
        StationDecorator.Decorate(modules);
        BakeLighting(modules);
        cancellationToken.ThrowIfCancellationRequested();

        var flatMeshes = new Dictionary<PlacedModule, StationMeshCpuData>();
        foreach (PlacedModule module in modules)
        {
            if (module.Mesh is not { IsEmpty: false } mesh)
                continue;
            var (vertices, indices) = mesh.ToIntArrays();
            flatMeshes[module] = new StationMeshCpuData(vertices, indices);
        }

        StationDecorator.ApplyAmbientOcclusion(modules);
        var (textures, assignments) = PrepareTextures(
            modules,
            palette,
            profile,
            station,
            cancellationToken);
        StationTextureCompactionResult compacted = CompactSelectedTextures(
            textures,
            assignments,
            generatedVariantPairCount: modules.Count > 0
                ? (textures.Count - 1) / 2
                : 0);
        IReadOnlyList<StationVisualUploadPlanItem> uploadPlan = BuildUploadPlan(
            modules,
            compacted.Textures,
            compacted.Assignments,
            flatMeshes,
            enabledShadowCasterClasses,
            cancellationToken);
        stopwatch.Stop();
        return new StationGenerationCpuResult(
            modules,
            compacted.Textures,
            compacted.Assignments,
            flatMeshes,
            uploadPlan,
            null,
            stopwatch.Elapsed.TotalMilliseconds,
            compacted.Diagnostics);
    }

    internal static StationTextureCompactionResult CompactSelectedTextures(
        IReadOnlyList<PreparedStationTexture> generatedTextures,
        IReadOnlyList<StationTextureAssignment> assignments,
        int generatedVariantPairCount = -1)
    {
        if (generatedVariantPairCount < -1)
            throw new ArgumentOutOfRangeException(nameof(generatedVariantPairCount));
        var remap = new Dictionary<int, int>();
        var compactTextures = new List<PreparedStationTexture>();
        var compactAssignments = new List<StationTextureAssignment>(assignments.Count);
        var albedoIndices = new HashSet<int>();
        var materialIndices = new HashSet<int>();
        int selectedPairCount = assignments
            .Select(assignment => (
                assignment.AlbedoTextureIndex,
                assignment.MaterialTextureIndex))
            .Distinct()
            .Count();

        int Remap(int originalIndex)
        {
            if ((uint)originalIndex >= (uint)generatedTextures.Count)
                throw new InvalidOperationException(
                    $"Station texture assignment index {originalIndex} is outside " +
                    $"the generated texture range 0..{generatedTextures.Count - 1}.");
            if (remap.TryGetValue(originalIndex, out int existing))
                return existing;
            int compactIndex = compactTextures.Count;
            remap.Add(originalIndex, compactIndex);
            compactTextures.Add(generatedTextures[originalIndex]);
            return compactIndex;
        }

        foreach (StationTextureAssignment assignment in assignments)
        {
            int albedo = Remap(assignment.AlbedoTextureIndex);
            int material = Remap(assignment.MaterialTextureIndex);
            albedoIndices.Add(albedo);
            materialIndices.Add(material);
            compactAssignments.Add(assignment with
            {
                AlbedoTextureIndex = albedo,
                MaterialTextureIndex = material,
            });
        }

        return new(
            compactTextures,
            compactAssignments,
            new(
                GeneratedTextureCount: generatedTextures.Count,
                GeneratedVariantPairCount: generatedVariantPairCount >= 0
                    ? generatedVariantPairCount
                    : generatedTextures.Count / 2,
                SelectedUniqueTextureCount: compactTextures.Count,
                SelectedUniqueTexturePairCount: selectedPairCount,
                DiscardedTextureCount: generatedTextures.Count - compactTextures.Count,
                UploadedAlbedoTextureCount: albedoIndices.Count,
                UploadedMaterialTextureCount: materialIndices.Count,
                ModuleTextureBindingCount: assignments.Count * 2,
                SharedFallbackReferenceCount: 0));
    }

    private static IReadOnlyList<StationVisualUploadPlanItem> BuildUploadPlan(
        IReadOnlyList<PlacedModule> modules,
        IReadOnlyList<PreparedStationTexture> textures,
        IReadOnlyList<StationTextureAssignment> assignments,
        IReadOnlyDictionary<PlacedModule, StationMeshCpuData> flatDecorationMeshes,
        IReadOnlySet<DecorClass> enabledShadowCasterClasses,
        CancellationToken cancellationToken)
    {
        var plan = new List<StationVisualUploadPlanItem>();
        var albedoIndices = assignments.Select(a => a.AlbedoTextureIndex).ToHashSet();
        var materialIndices = assignments.Select(a => a.MaterialTextureIndex).ToHashSet();

        for (int i = 0; i < textures.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PreparedStationTexture texture = textures[i];
            StationVisualUploadResourceKind kind = materialIndices.Contains(i)
                && !albedoIndices.Contains(i)
                    ? StationVisualUploadResourceKind.MaterialTexture
                    : StationVisualUploadResourceKind.PanelAlbedoTexture;
            plan.Add(new(
                kind,
                $"texture[{i}]",
                StationGpuByteAccounting.TextureBytes(
                    texture.Width,
                    texture.Height,
                    bytesPerPixel: 4),
                Texture: texture));
        }

        var hullMeshes = new Dictionary<PlacedModule, StationMeshCpuData>();
        foreach ((PlacedModule module, int index) in modules.Select((module, index) => (module, index)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            StationMeshCpuData? mesh = module.Definition.MeshFactory == null
                ? PrepareBoxHullMesh(module)
                : PrepareMesh(module.HullMesh);
            if (mesh == null)
                continue;
            hullMeshes[module] = mesh;
            plan.Add(MeshItem(
                StationVisualUploadResourceKind.HullMesh,
                module,
                index,
                mesh));
        }

        foreach ((PlacedModule module, int index) in modules.Select((module, index) => (module, index)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            StationMeshCpuData? mesh = PrepareMesh(module.Mesh);
            if (mesh != null)
                plan.Add(MeshItem(
                    StationVisualUploadResourceKind.DecorationMesh,
                    module,
                    index,
                    mesh));
        }

        foreach ((PlacedModule module, int index) in modules.Select((module, index) => (module, index)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (flatDecorationMeshes.TryGetValue(module, out StationMeshCpuData? mesh))
                plan.Add(MeshItem(
                    StationVisualUploadResourceKind.FlatDecorationMesh,
                    module,
                    index,
                    mesh));
        }

        foreach ((PlacedModule module, int index) in modules.Select((module, index) => (module, index)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            StationMeshCpuData? mesh = PrepareMesh(module.GlassMesh);
            if (mesh != null)
                plan.Add(MeshItem(
                    StationVisualUploadResourceKind.GlassMesh,
                    module,
                    index,
                    mesh));
        }

        foreach ((PlacedModule module, int index) in modules.Select((module, index) => (module, index)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!hullMeshes.TryGetValue(module, out StationMeshCpuData? mesh))
                continue;
            (Vector3 Min, Vector3 Max)? bounds = module.Definition.MeshFactory == null
                ? (-module.Definition.BoundingBox * 0.5f, module.Definition.BoundingBox * 0.5f)
                : module.HullMesh?.ComputeFaceRangeBounds(0, module.HullMesh.FaceCount);
            plan.Add(MeshItem(
                StationVisualUploadResourceKind.ShadowHullMesh,
                module,
                index,
                mesh,
                bounds));
        }

        foreach ((PlacedModule module, int index) in modules.Select((module, index) => (module, index)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (module.Mesh == null || enabledShadowCasterClasses.Count == 0)
                continue;
            var ranges = module.Mesh.DecorClassRanges
                .Where(range => enabledShadowCasterClasses.Contains(range.decorClass))
                .Select(range => (range.indexStart, range.indexCount))
                .ToList();
            StationMeshCpuData? mesh = module.Mesh.PrepareIndexRanges(ranges);
            if (mesh == null)
                continue;
            plan.Add(MeshItem(
                StationVisualUploadResourceKind.ShadowDecorationMesh,
                module,
                index,
                mesh,
                module.Mesh.ComputeIndexRangeBounds(ranges)));
        }

        return plan;

        static StationVisualUploadPlanItem MeshItem(
            StationVisualUploadResourceKind kind,
            PlacedModule module,
            int index,
            StationMeshCpuData mesh,
            (Vector3 Min, Vector3 Max)? bounds = null)
            => new(
                kind,
                $"module[{index}]/{module.Definition.Id}",
                StationGpuByteAccounting.VertexBufferBytes(
                    mesh.Vertices.Length,
                    VertexPositionNormalColorTexture.VertexDeclaration.VertexStride)
                + StationGpuByteAccounting.IndexBufferBytes(
                    mesh.Indices.Length,
                    IndexElementSize.ThirtyTwoBits),
                module,
                Mesh: mesh,
                Bounds: bounds);

        static StationMeshCpuData? PrepareMesh(StationModuleMesh? mesh)
        {
            if (mesh is not { IsEmpty: false })
                return null;
            var (vertices, indices) = mesh.ToIntArrays();
            return new StationMeshCpuData(vertices, indices);
        }
    }

    internal static StationMeshCpuData PrepareBoxHullMesh(PlacedModule module)
    {
        const float UvScale = 5.0f;
        float chamferInset = module.ChamferDepth * 0.707f;
        Vector3 h = module.Definition.BoundingBox * 0.5f;
        float si = chamferInset;
        var vertices = new VertexPositionNormalColorTexture[24];
        var indices = new int[36];

        static void AddFace(
            VertexPositionNormalColorTexture[] vertices,
            int[] indices,
            int face,
            Vector3 v0,
            Vector3 v1,
            Vector3 v2,
            Vector3 v3,
            Vector3 normal,
            Vector3 uAxis,
            Vector3 vAxis)
        {
            int vertexBase = face * 4;
            vertices[vertexBase] = new(v0, normal, Color.White, Vector2.Zero);
            vertices[vertexBase + 1] = new(v1, normal, Color.White, new Vector2(
                Vector3.Dot(v1 - v0, uAxis) / UvScale,
                Vector3.Dot(v1 - v0, vAxis) / UvScale));
            vertices[vertexBase + 2] = new(v2, normal, Color.White, new Vector2(
                Vector3.Dot(v2 - v0, uAxis) / UvScale,
                Vector3.Dot(v2 - v0, vAxis) / UvScale));
            vertices[vertexBase + 3] = new(v3, normal, Color.White, new Vector2(
                Vector3.Dot(v3 - v0, uAxis) / UvScale,
                Vector3.Dot(v3 - v0, vAxis) / UvScale));

            int indexBase = face * 6;
            indices[indexBase] = vertexBase;
            indices[indexBase + 1] = vertexBase + 2;
            indices[indexBase + 2] = vertexBase + 1;
            indices[indexBase + 3] = vertexBase;
            indices[indexBase + 4] = vertexBase + 3;
            indices[indexBase + 5] = vertexBase + 2;
        }

        AddFace(vertices, indices, 0, new(-h.X+si,-h.Y+si,+h.Z), new(+h.X-si,-h.Y+si,+h.Z), new(+h.X-si,+h.Y-si,+h.Z), new(-h.X+si,+h.Y-si,+h.Z),  Vector3.UnitZ,  Vector3.UnitX,  Vector3.UnitY);
        AddFace(vertices, indices, 1, new(+h.X-si,-h.Y+si,-h.Z), new(-h.X+si,-h.Y+si,-h.Z), new(-h.X+si,+h.Y-si,-h.Z), new(+h.X-si,+h.Y-si,-h.Z), -Vector3.UnitZ, -Vector3.UnitX,  Vector3.UnitY);
        AddFace(vertices, indices, 2, new(-h.X,-h.Y+si,-h.Z+si), new(-h.X,-h.Y+si,+h.Z-si), new(-h.X,+h.Y-si,+h.Z-si), new(-h.X,+h.Y-si,-h.Z+si), -Vector3.UnitX,  Vector3.UnitZ,  Vector3.UnitY);
        AddFace(vertices, indices, 3, new(+h.X,-h.Y+si,+h.Z-si), new(+h.X,-h.Y+si,-h.Z+si), new(+h.X,+h.Y-si,-h.Z+si), new(+h.X,+h.Y-si,+h.Z-si),  Vector3.UnitX, -Vector3.UnitZ,  Vector3.UnitY);
        AddFace(vertices, indices, 4, new(-h.X+si,+h.Y,+h.Z-si), new(+h.X-si,+h.Y,+h.Z-si), new(+h.X-si,+h.Y,-h.Z+si), new(-h.X+si,+h.Y,-h.Z+si),  Vector3.UnitY,  Vector3.UnitX, -Vector3.UnitZ);
        AddFace(vertices, indices, 5, new(-h.X+si,-h.Y,-h.Z+si), new(+h.X-si,-h.Y,-h.Z+si), new(+h.X-si,-h.Y,+h.Z-si), new(-h.X+si,-h.Y,+h.Z-si), -Vector3.UnitY,  Vector3.UnitX,  Vector3.UnitZ);
        return new StationMeshCpuData(vertices, indices);
    }

    public static StationGenerationResult UploadPrepared(
        Galaxy.Station station,
        StationGenerationCpuResult prepared,
        GraphicsDevice gd)
    {
        var uploaded = new List<Texture2D>(prepared.Textures.Count);
        try
        {
            if (prepared.UsesSharedMegastationFallbackTextures)
            {
                Texture2D albedo = UploadFlat(Color.White);
                Texture2D material = UploadFlat(new Color(128, 255, 0, 0));
                foreach (PlacedModule module in prepared.Modules)
                {
                    module.TextureInstance = albedo;
                    module.MaterialInstance = material;
                }
            }
            foreach (PreparedStationTexture texture in prepared.Textures)
            {
                var gpu = new Texture2D(gd, texture.Width, texture.Height);
                gpu.SetData(texture.Pixels);
                uploaded.Add(gpu);
            }

            foreach (StationTextureAssignment assignment in prepared.TextureAssignments)
            {
                assignment.Module.TextureInstance = uploaded[assignment.AlbedoTextureIndex];
                assignment.Module.MaterialInstance = uploaded[assignment.MaterialTextureIndex];
            }

            return new StationGenerationResult(
                prepared.Modules,
                uploaded,
                prepared.MegastationDiagnostics);

            Texture2D UploadFlat(Color color)
            {
                var texture = new Texture2D(gd, 1, 1);
                try
                {
                    texture.SetData([color]);
                    uploaded.Add(texture);
                    return texture;
                }
                catch
                {
                    texture.Dispose();
                    throw;
                }
            }
        }
        catch
        {
            foreach (Texture2D texture in uploaded)
                texture.Dispose();
            foreach (PlacedModule module in prepared.Modules)
            {
                module.TextureInstance = null;
                module.MaterialInstance = null;
            }
            throw;
        }
    }

    public static void ApplyPreparedLandingPads(
        Galaxy.Station station,
        List<PlacedModule> modules)
        => PopulateLandingPads(station, modules);

    // Brief S2b-2 item 5: certain module categories get a fixed, economy-independent
    // look instead of the hosting station's own economy roll — riding on the SAME
    // GenerateVariantSet/OffsetPaletteForVariant pipeline (still per-station-owned,
    // seeded from the same PersistenceId, disposed the same way — NOT a second global
    // cache, that would revive exactly the shared-across-stations problem S2b-1 fixed),
    // just sourced from a different economy's profile/variance so "sciency" reads
    // consistently regardless of which economy actually generated the station. Only
    // "science" is wired — Brief S2b-2 also named a "designated metallic module," but no
    // such category, module Id, or SurfaceTexture exists anywhere in
    // StationModuleRegistry/SurfaceTexture today; confirmed with Timo not to invent one,
    // left unbuilt. Add further entries here if/when a matching category exists.
    // internal, not private: tests confirm the override resolves regardless of the
    // hosting station's own economy, without needing a GraphicsDevice.
    internal static readonly Dictionary<string, StationEconomy> CategorySpecialEconomy = new()
    {
        ["science"] = StationEconomy.Scientific,
    };

    // internal, not private: directly testable without a GraphicsDevice — confirms the
    // override resolves regardless of the hosting station's own economy.
    internal static StationEconomy EconomyForModule(string category, StationEconomy stationEconomy) =>
        CategorySpecialEconomy.TryGetValue(category, out var special) ? special : stationEconomy;

    // Brief S2b-1 established per-station ownership; S2b-2 makes the variant generation
    // and per-module assignment profile-driven instead of uniform. Each (surface,
    // economy) pair used by this station gets its own variant set — the compound key
    // (not just surface) is needed because a category-special module (science) can pull
    // in a second economy's variant set alongside the station's own, for the same
    // surface (TechPanel serves science/military/core alike).
    //
    // Brief S2c-1: each variant is now an (albedo, material) pair — mod.MaterialInstance
    // is assigned alongside mod.TextureInstance, from the SAME variant index (so a
    // module's gloss always matches its own albedo variant, never a different one's).
    // Both textures of both pair elements go into `owned` for disposal — no separate
    // material dictionary, since SystemSpaceState's disposal loop doesn't care about the
    // albedo/material distinction, only that every Texture2D this pass created gets freed.
    private static (
        IReadOnlyList<PreparedStationTexture> Textures,
        IReadOnlyList<StationTextureAssignment> Assignments) PrepareTextures(
        List<PlacedModule> modules,
        TexturePalette palette,
        StationProfile profile,
        Galaxy.Station station,
        CancellationToken cancellationToken)
    {
        var variantSets =
            new Dictionary<(SurfaceTexture surface, StationEconomy economy), (int Albedo, int Material)[]>();
        var prepared = new List<PreparedStationTexture>();
        var assignments = new List<StationTextureAssignment>(modules.Count);

        (int Albedo, int Material)[] VariantsFor(
            SurfaceTexture surface,
            StationEconomy economy,
            TexturePalette economyPalette,
            float colourSpread)
        {
            var key = (surface, economy);
            if (variantSets.TryGetValue(key, out var existing))
                return existing;

            StationTextureRegistry.TexturePixels[] pixels =
                StationTextureRegistry.GenerateVariantPixels(
                    surface,
                    economyPalette,
                    station.PersistenceId ?? station.Name,
                    colourSpread,
                    cancellationToken: cancellationToken);
            var set = new (int Albedo, int Material)[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                int albedo = prepared.Count;
                prepared.Add(new PreparedStationTexture(512, 512, pixels[i].Albedo));
                int material = prepared.Count;
                prepared.Add(new PreparedStationTexture(512, 512, pixels[i].Material));
                set[i] = (albedo, material);
            }
            variantSets[key] = set;
            return set;
        }

        foreach (PlacedModule module in modules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SurfaceTexture surface = SurfaceFor(module.Definition.Category);
            StationEconomy economy = EconomyForModule(module.Definition.Category, profile.Economy);
            TexturePalette economyPalette = economy == profile.Economy
                ? palette
                : TexturePalette.From(new StationProfile
                {
                    Economy = economy,
                    Age = profile.Age,
                    Wealth = profile.Wealth,
                    Population = profile.Population,
                });

            StationVarianceProfile variance = StationEconomyVariance.Profiles[economy];
            var variants = VariantsFor(surface, economy, economyPalette, variance.ColourSpread);
            var selected = variants[
                StationTextureRegistry.SelectVariantIndex(
                    module.Seed,
                    variants.Length,
                    variance.BaseShareRatio)];
            assignments.Add(new StationTextureAssignment(
                module,
                selected.Albedo,
                selected.Material));
        }

        if (modules.Count > 0)
        {
            StationTextureAssignment core = assignments[0];
            Color[] namePixels = GenerateNameFacePixels(
                prepared[core.AlbedoTextureIndex].Pixels,
                station.Name,
                palette);
            int nameIndex = prepared.Count;
            prepared.Add(new PreparedStationTexture(512, 512, namePixels));
            assignments[0] = core with { AlbedoTextureIndex = nameIndex };
        }

        return (prepared, assignments);
    }

    private static Color[] GenerateNameFacePixels(
        Color[] basePixels,
        string name,
        TexturePalette palette)
    {
        const int Size = 512;
        var pixels = basePixels.ToArray();
        int scale = 4;
        int textW = TextPainter.MeasureText(name, scale);
        int textH = TextPainter.MeasureHeight(scale);
        int startX = Math.Clamp((Size - textW) / 2, 4, Size - textW - 4);
        int startY = (Size - textH) / 2;
        Color barColor = TexturePalette.LerpColor(palette.BaseColour, Color.Black, 0.45f);
        const int pad = 8;
        for (int y = startY - pad; y < startY + textH + pad; y++)
        for (int x = 0; x < Size; x++)
            if ((uint)y < Size)
                pixels[y * Size + x] =
                    TexturePalette.LerpColor(pixels[y * Size + x], barColor, 0.70f);
        TextPainter.DrawText(
            pixels,
            Size,
            Size,
            name,
            startX,
            startY,
            palette.TextColour,
            scale,
            alpha: 0.90f);
        return pixels;
    }

    // internal, not private: GenerateModulesForDiagnostics-based tests need this to group
    // modules by surface the same way PrepareTextures does.
    internal static SurfaceTexture SurfaceFor(string category) => category switch
    {
        "hab" or "luxury"                    => SurfaceTexture.CleanPanel,
        "science" or "military" or "core"    => SurfaceTexture.TechPanel,
        "industrial" or "fuel"               => SurfaceTexture.IndustrialPanel,
        "cargo"                              => SurfaceTexture.CargoPanel,
        _                                    => SurfaceTexture.CleanPanel,
    };

    // Writes the self-illumination floor S into each module's decoration vertex alpha
    // (Docs/station-lighting-pipeline-spec.md Phase A) — no directional bake, no world
    // rotation needed any more: the sun term is computed per frame in LitSurface.fx from
    // the real (rotating) world normal. Must run after Decorate() (so meshes exist) and
    // before Build() (so GPU buffers pick up the written alpha).
    private static void BakeLighting(List<PlacedModule> modules)
    {
        foreach (var mod in modules)
        {
            if (mod.Mesh == null) continue;
            mod.Mesh.ApplyIlluminationFlags();

            if (mod.Mesh.AmbientOverrideFaceCount > 0)
                BoostAmbientForFaceRange(mod.Mesh, SceneLighting.Ambient, mod.Seed);
        }
    }

    // Computes a higher, per-face self-illumination floor S for a sub-range of faces the mesh
    // flagged via AmbientOverrideFace* (e.g. a hollow module's interior walls) and writes it
    // directly into vertex alpha via SetFaceIllumination — no colour rescaling: S is a shader
    // input (LitSurface.fx's BakedColorLit technique takes max(N.L, Ambient, S) every frame),
    // not a baked multiply, so it needs no world rotation or sun direction at bake time. Three
    // additive terms, each a per-face constant (flat-shaded, matching the rest of this mesh —
    // not a per-vertex gradient):
    //   - doorProximity: linear falloff from the door plane to the far wall, derived from the
    //     flagged faces' own Z spread (no hardcoded door location) — brighter near the door.
    //   - overheadCue: faces whose normal points down (the ceiling, in this convention) read
    //     brighter than the floor — an up/down orientation cue, not a real light source.
    //   - cornerNoise: seeded per-face jitter, same per-face-constant technique as
    //     ShippingContainerFactory.ApplyWear, weighted heaviest on the back wall (insertion
    //     order index 0 in DockingBayHull.Build) per the reported "can't see corners" complaint.
    // Still fully disposable once real interior-light-fixture baking exists.
    private const float InteriorBaseFloor   = 0.45f;
    private const float DoorProximityWeight = 0.35f;
    private const float OverheadCueWeight   = 0.25f;
    private const float CornerNoiseWeight   = 0.15f;

    private static void BoostAmbientForFaceRange(StationModuleMesh mesh, float baseAmbient, int seed)
    {
        int start = mesh.AmbientOverrideFaceStart;
        int count = mesh.AmbientOverrideFaceCount;
        if (count <= 0) return;

        count = System.Math.Min(count, mesh.FaceCount - start);
        if (count <= 0) return;

        float minZ = float.MaxValue, maxZ = float.MinValue;
        var centers = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            var (center, _, _) = mesh.GetFaceBounds(start + i);
            centers[i] = center;
            minZ = MathF.Min(minZ, center.Z);
            maxZ = MathF.Max(maxZ, center.Z);
        }
        float depth = MathF.Max(maxZ - minZ, 0.01f);

        // Independent salt from ChamferDepthForSeed/WallThicknessForSeed's own draws — a fresh,
        // disposable stream, not shared with anything that affects geometry.
        var noiseRng = new System.Random(seed ^ 0x4C4F5754);

        for (int i = 0; i < count; i++)
        {
            int f = start + i;
            Vector3 localN = mesh.LocalFaceNormal(f);
            if (localN == Vector3.Zero) continue;

            float doorProximity = 1f - MathHelper.Clamp((centers[i].Z - minZ) / depth, 0f, 1f);
            float overheadCue   = MathF.Max(0f, Vector3.Dot(localN, -Vector3.UnitY));
            float noiseWeight   = i == 0 ? 1f : 0.3f;
            float cornerNoise   = ((float)noiseRng.NextDouble() * 2f - 1f) * noiseWeight;

            float brightness = InteriorBaseFloor
                + doorProximity * DoorProximityWeight
                + overheadCue   * OverheadCueWeight
                + cornerNoise   * CornerNoiseWeight;
            brightness = MathHelper.Clamp(brightness, baseAmbient, 1f);

            mesh.SetFaceIllumination(f, brightness);
        }
    }

    // Runs just the growth loop (module placement — port math and AABB checks only, no
    // GraphicsDevice, no mesh building) and returns the docking bay's definition if the
    // pre-growth attachment succeeded, or null. Deterministic per station name, same as
    // Generate — cheap enough to call once per station when a system loads (e.g.
    // SystemMapState.OnEnter) rather than during full 3D generation. Returns the actual
    // matched Definition (not a hardcoded display constant) so BoundingBox/DoorOpening stay
    // correct automatically if more bay variants exist later.
    public static StationModuleDefinition? FindDockingBay(Galaxy.Station station)
    {
        int seed = NameHash(station.Name);
        var gen  = new StationGenerator(seed);
        var modules = gen.Run(station);
        ValidatePlacement(modules);
        return modules.FirstOrDefault(m => m.Definition.Category == "docking-bay")?.Definition;
    }

    // Diagnostic-only, no GraphicsDevice: exposes the growth loop's real PlacedModule
    // list (with real per-module Seed values) so tests can inspect variant-index
    // distribution (Brief S2b-1 gate diagnosis) without needing PrepareTextures' actual
    // texture creation. Same GD-free split as FindDockingBay, one step further.
    internal static List<PlacedModule> GenerateModulesForDiagnostics(Galaxy.Station station)
    {
        int seed = NameHash(station.Name);
        var gen  = new StationGenerator(seed);
        var modules = gen.Run(station);
        ValidatePlacement(modules);
        PopulateLandingPads(station, modules);
        return modules;
    }

    // Asserts that every module except the core (index 0) has a non-null AttachmentPort.
    // A null AttachmentPort means the module was placed without a parent connection — an orphan.
    [System.Diagnostics.Conditional("DEBUG")]
    private static void ValidatePlacement(List<PlacedModule> placed)
    {
        for (int i = 1; i < placed.Count; i++)
        {
            var mod = placed[i];
            if (mod.AttachmentPort != null) continue;

            throw new InvalidOperationException(
                $"Orphan module at placed[{i}]: '{mod.Definition.Id}' " +
                $"depth={mod.Depth} " +
                $"aabb=[({mod.AabbMin.X:F1},{mod.AabbMin.Y:F1},{mod.AabbMin.Z:F1})" +
                $"..({mod.AabbMax.X:F1},{mod.AabbMax.Y:F1},{mod.AabbMax.Z:F1})]");
        }
    }

    // ── Growth loop ───────────────────────────────────────────────────────────

    private List<PlacedModule> Run(Galaxy.Station station)
    {
        StationScale stationScale = station.Size switch
        {
            StationSize.Small  => StationScale.Outpost,
            StationSize.Medium => StationScale.Station,
            StationSize.Large  => StationScale.Port,
            _                  => StationScale.Outpost,
        };

        // "core" and "docking-bay" are never organically picked: core is the station root
        // (placed below), docking-bay is placed exactly once by the pre-growth step further down.
        var availableModules = StationModuleRegistry.All
            .Where(m => m.MinScale <= stationScale && m.Category != "core" && m.Category != "docking-bay")
            .ToList();

        if (availableModules.Count == 0) return _placed;

        int moduleLimit = stationScale switch
        {
            StationScale.Outpost     => 8  + _rng.NextInt(12),
            StationScale.Station     => 15 + _rng.NextInt(20),
            StationScale.Port        => 25 + _rng.NextInt(35),
            StationScale.Megastation => 50 + _rng.NextInt(80),
            _                        => 8,
        };

        var archetype = StationArchetypeRegistry.Pick(_rng);

        // Core hub — always placed at the station origin. CoreHubLarge (Large-tier ports) becomes
        // the root at Port+ scale — this is what makes the docking bay's Large-tier ports able to
        // attach at all; the small CoreHub's ports max out at Medium.
        var coreDefn = stationScale >= StationScale.Port
            ? StationModuleRegistry.CoreHubLarge
            : StationModuleRegistry.CoreHub;
        var (coreMin, coreMax) = ComputeWorldAabb(Matrix.Identity, coreDefn.BoundingBox);
        int coreSeed = _rng.NextInt(0, 999999);
        var core = new PlacedModule
        {
            Definition   = coreDefn,
            Transform    = Matrix.Identity,
            Seed         = coreSeed,
            ChamferDepth = ChamferDepthForSeed(coreSeed),
            Depth        = 0,
            AabbMin      = coreMin,
            AabbMax      = coreMax,
        };
        foreach (var p in coreDefn.Ports)
        {
            if (!p.IsTerminal)
                core.OpenPorts.Add(ToWorldPort(core, p, 0));
        }
        _placed.Add(core);

        // Pre-growth step: attach the docking bay directly to the core, before the general
        // frontier loop starts. Deliberate placement, not organic growth — generalizes to
        // multiple bays later by repeating this same step in sequence.
        OpenPort?     dockingBayPort = null;
        PlacedModule? dockingBay     = null;
        if (stationScale >= StationScale.Port)
        {
            var dockingBayDefn = StationModuleRegistry.CreateDockingBay(_seed, stationScale);
            foreach (var corePort in core.OpenPorts)
            {
                dockingBay = TryAttach(dockingBayDefn, corePort);
                if (dockingBay != null) { dockingBayPort = corePort; break; }
            }
        }
        if (dockingBay != null)
        {
            _placed.Add(dockingBay);
            _reservedVolumes.Add(ComputeReservedCorridor(dockingBay));
        }

        // Priority frontier: higher archetype score → expanded sooner.
        // PriorityQueue is a min-heap, so negate the score.
        var frontier = new PriorityQueue<OpenPort, float>();
        foreach (var op in core.OpenPorts)
        {
            // Consumed by the pre-growth attachment above — don't re-offer it.
            if (op == dockingBayPort) continue;
            frontier.Enqueue(op, -archetype.ScorePort(op, _placed.Count));
        }
        if (dockingBay != null)
            foreach (var op in dockingBay.OpenPorts)
                frontier.Enqueue(op, -archetype.ScorePort(op, _placed.Count));

        const int MaxAttemptsPerPort = 6;

        while (frontier.Count > 0 && _placed.Count < moduleLimit)
        {
            var port = frontier.Dequeue();

            for (int attempt = 0; attempt < MaxAttemptsPerPort; attempt++)
            {
                var candidate = WeightedPickModule(availableModules, archetype, port.Depth, stationScale);
                var placed    = TryAttach(candidate, port);
                if (placed != null)
                {
                    _placed.Add(placed);
                    foreach (var op in placed.OpenPorts)
                        frontier.Enqueue(op, -archetype.ScorePort(op, _placed.Count));
                    break;
                }
            }
        }

        return _placed;
    }

    // Weighted random module pick factoring in SelectWeight, archetype bias, and station scale.
    private StationModuleDefinition WeightedPickModule(
        List<StationModuleDefinition> candidates,
        IStationArchetype             archetype,
        int                           depth,
        StationScale                  stationScale)
    {
        double total = 0;
        foreach (var m in candidates)
        {
            float sizeBonus = stationScale >= StationScale.Port && m.Id.EndsWith("-large") ? 2.5f : 1.0f;
            total += m.SelectWeight * sizeBonus * archetype.CategoryBias(m.Category, depth);
        }

        double roll  = _rng.NextDouble() * total;
        double accum = 0;
        foreach (var m in candidates)
        {
            float sizeBonus = stationScale >= StationScale.Port && m.Id.EndsWith("-large") ? 2.5f : 1.0f;
            accum += m.SelectWeight * sizeBonus * archetype.CategoryBias(m.Category, depth);
            if (roll < accum) return m;
        }
        return candidates[^1];
    }

    // ── Attachment attempt ────────────────────────────────────────────────────

    private PlacedModule? TryAttach(StationModuleDefinition candidate, OpenPort parentPort)
    {
        // Parent port's AcceptsCategories restricts which module categories can attach
        if (parentPort.Definition.AcceptsCategories.Length > 0
            && !parentPort.Definition.AcceptsCategories.Contains(candidate.Category))
            return null;

        var attachPort = SelectAttachmentPort(candidate, parentPort);
        if (attachPort == null) return null;

        float twist     = _rng.NextInt(3) * (MathF.PI / 2f);   // 0°, 90°, 180°, or 270°
        var   transform = ComputeAttachmentTransform(parentPort, attachPort, twist);

        var (min, max) = ComputeWorldAabb(transform, candidate.BoundingBox);
        if (IntersectsAny(min, max)) return null;

        int moduleSeed = _rng.NextInt(0, 999999);
        var placed = new PlacedModule
        {
            Definition     = candidate,
            Transform      = transform,
            Seed           = moduleSeed,
            ChamferDepth   = ChamferDepthForSeed(moduleSeed),
            Depth          = parentPort.Depth + 1,
            AabbMin        = min,
            AabbMax        = max,
            AttachmentPort = attachPort,
        };

        // Mark the parent's port as consumed
        parentPort.ParentModule.ChildPorts.Add(parentPort.Definition);

        foreach (var port in candidate.Ports)
        {
            if (port == attachPort) continue;
            if (port.IsTerminal) continue;
            placed.OpenPorts.Add(ToWorldPort(placed, port, placed.Depth));
        }

        return placed;
    }

    // Selects which port on the candidate module will serve as the attachment point.
    private StationPort? SelectAttachmentPort(StationModuleDefinition candidate, OpenPort parentPort)
    {
        var eligible = candidate.Ports
            .Where(p => !p.IsTerminal)
            .Where(p => (int)p.Size <= (int)parentPort.Definition.Size)
            .Where(p => p.AcceptsCategories.Length == 0
                     || p.AcceptsCategories.Contains(parentPort.ParentModule.Definition.Category))
            .ToList();

        if (eligible.Count == 0) return null;
        return eligible[_rng.NextInt(eligible.Count - 1)];
    }

    // ── Port-alignment math ───────────────────────────────────────────────────

    // Computes the world transform for a child module so its chosen attachment
    // port meets the parent port flush, normals opposing.
    private static Matrix ComputeAttachmentTransform(
        OpenPort    parentPort,
        StationPort childAttachPort,
        float       twistAngle)
    {
        Vector3 childLocalNormal = childAttachPort.OutwardNormal;
        Vector3 targetNormal     = -parentPort.WorldNormal;   // child normal must oppose parent normal

        Quaternion r1 = RotationBetween(childLocalNormal, targetNormal);
        Quaternion r2 = Quaternion.CreateFromAxisAngle(-parentPort.WorldNormal, twistAngle);
        Quaternion combinedRot = Quaternion.Normalize(r2 * r1);

        Vector3 rotatedAttachPos = Vector3.Transform(childAttachPort.LocalPosition, combinedRot);
        Vector3 childWorldOrigin = parentPort.WorldPosition - rotatedAttachPos;

        return Matrix.CreateFromQuaternion(combinedRot)
             * Matrix.CreateTranslation(childWorldOrigin);
    }

    // Computes the shortest rotation quaternion from unit vector `from` to unit vector `to`.
    private static Quaternion RotationBetween(Vector3 from, Vector3 to)
    {
        float dot = Vector3.Dot(from, to);

        if (dot >= 0.9999f)
            return Quaternion.Identity;

        // Exactly opposite — rotate 180° around any perpendicular axis
        if (dot <= -0.9999f)
        {
            Vector3 perp = MathF.Abs(from.X) < 0.9f
                ? Vector3.Normalize(Vector3.Cross(from, Vector3.UnitX))
                : Vector3.Normalize(Vector3.Cross(from, Vector3.UnitY));
            return Quaternion.CreateFromAxisAngle(perp, MathF.PI);
        }

        Vector3 axis  = Vector3.Normalize(Vector3.Cross(from, to));
        float   angle = MathF.Acos(Math.Clamp(dot, -1f, 1f));
        return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(axis, angle));
    }

    // Computes world-space OpenPort from a placed module's transform.
    private static OpenPort ToWorldPort(PlacedModule parent, StationPort port, int depth)
    {
        Vector3 worldPos    = Vector3.Transform(port.LocalPosition, parent.Transform);
        Vector3 worldNormal = Vector3.Normalize(Vector3.TransformNormal(port.OutwardNormal, parent.Transform));
        return new OpenPort
        {
            ParentModule  = parent,
            Definition    = port,
            WorldPosition = worldPos,
            WorldNormal   = worldNormal,
            Depth         = depth,
        };
    }

    // ── AABB utilities ────────────────────────────────────────────────────────

    // Computes the world-space AABB for a module at the given transform.
    // BoundingBox is the full extents in metres (e.g. (20, 20, 20) for a 20m cube).
    private static (Vector3 min, Vector3 max) ComputeWorldAabb(Matrix transform, Vector3 boundingBox)
    {
        Vector3 half = boundingBox * 0.5f;

        Span<Vector3> corners = stackalloc Vector3[8];
        corners[0] = new Vector3(-half.X, -half.Y, -half.Z);
        corners[1] = new Vector3(+half.X, -half.Y, -half.Z);
        corners[2] = new Vector3(-half.X, +half.Y, -half.Z);
        corners[3] = new Vector3(+half.X, +half.Y, -half.Z);
        corners[4] = new Vector3(-half.X, -half.Y, +half.Z);
        corners[5] = new Vector3(+half.X, -half.Y, +half.Z);
        corners[6] = new Vector3(-half.X, +half.Y, +half.Z);
        corners[7] = new Vector3(+half.X, +half.Y, +half.Z);

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var corner in corners)
        {
            Vector3 world = Vector3.Transform(corner, transform);
            min = Vector3.Min(min, world);
            max = Vector3.Max(max, world);
        }

        return (min, max);
    }

    // Returns true if a candidate AABB overlaps any placed module's AABB, or any reserved
    // volume (e.g. a docking bay's approach corridor). A small margin prevents flush-touching
    // faces from registering as an overlap.
    private bool IntersectsAny(Vector3 candidateMin, Vector3 candidateMax, float margin = 0.5f)
    {
        var shrunkMin = candidateMin + new Vector3(margin);
        var shrunkMax = candidateMax - new Vector3(margin);

        foreach (var m in _placed)
        {
            if (shrunkMax.X > m.AabbMin.X && shrunkMin.X < m.AabbMax.X &&
                shrunkMax.Y > m.AabbMin.Y && shrunkMin.Y < m.AabbMax.Y &&
                shrunkMax.Z > m.AabbMin.Z && shrunkMin.Z < m.AabbMax.Z)
                return true;
        }
        foreach (var (rMin, rMax) in _reservedVolumes)
        {
            if (shrunkMax.X > rMin.X && shrunkMin.X < rMax.X &&
                shrunkMax.Y > rMin.Y && shrunkMin.Y < rMax.Y &&
                shrunkMax.Z > rMin.Z && shrunkMin.Z < rMax.Z)
                return true;
        }
        return false;
    }

    // Reserved approach corridor extending 150m outward from the docking bay's door (the -Z
    // face), cross-section 50x35 for lateral/vertical maneuvering room beyond the door's own
    // clear opening — door width is always 40m (Medium/Large ships share the same max width)
    // and height is at most 24m, so 50x35 comfortably exceeds every door variant without
    // needing to scale with it. Computed once, in the module's local space, then transformed by
    // its actual placed Transform — so it rotates correctly regardless of the random attach twist.
    private static (Vector3 min, Vector3 max) ComputeReservedCorridor(PlacedModule dockingBay)
    {
        // Door face is at local z = -halfDepth (the bay's own computed depth, no longer fixed);
        // corridor extends a further 150m outward, so it's centred at z = -halfDepth - 75.
        float   halfDepth   = dockingBay.Definition.BoundingBox.Z * 0.5f;
        Vector3 localCenter = new(0, 0, -halfDepth - 75f);
        Vector3 half        = new(25f, 17.5f, 75f);

        Span<Vector3> corners = stackalloc Vector3[8];
        corners[0] = localCenter + new Vector3(-half.X, -half.Y, -half.Z);
        corners[1] = localCenter + new Vector3(+half.X, -half.Y, -half.Z);
        corners[2] = localCenter + new Vector3(-half.X, +half.Y, -half.Z);
        corners[3] = localCenter + new Vector3(+half.X, +half.Y, -half.Z);
        corners[4] = localCenter + new Vector3(-half.X, -half.Y, +half.Z);
        corners[5] = localCenter + new Vector3(+half.X, -half.Y, +half.Z);
        corners[6] = localCenter + new Vector3(-half.X, +half.Y, +half.Z);
        corners[7] = localCenter + new Vector3(+half.X, +half.Y, +half.Z);

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var corner in corners)
        {
            Vector3 world = Vector3.Transform(corner, dockingBay.Transform);
            min = Vector3.Min(min, world);
            max = Vector3.Max(max, world);
        }
        return (min, max);
    }

    // Fills LocalPosition/LocalNormal on each LandingPad from the matching docking port
    // in world space. Simple positional mapping — pads assigned in module iteration order.
    // Any IsDocking port counts, regardless of category — an interior port (docking-bay) works
    // exactly like an exterior one (docking-arm); only the port flag matters.
    private static void PopulateLandingPads(Galaxy.Station station, List<PlacedModule> modules)
    {
        int padIdx = 0;
        foreach (var mod in modules)
        {
            foreach (var port in mod.Definition.Ports)
            {
                if (!port.IsDocking) continue;
                if (padIdx >= station.LandingPads.Count) return;
                var pad = station.LandingPads[padIdx++];
                Vector3 wp = Vector3.Transform(port.LocalPosition, mod.Transform);
                Vector3 wn = Vector3.Normalize(Vector3.TransformNormal(port.OutwardNormal, mod.Transform));
                pad.LocalPosition = new DVec3(wp.X, wp.Y, wp.Z);
                pad.LocalNormal   = new DVec3(wn.X, wn.Y, wn.Z);

                // Compute pad-forward = visual arrow direction, using module-local normal
                // (port.OutwardNormal) for the tangent frame — same as StationDecorator.TangentFrame.
                // Transforming module-local "up" to station-local space gives the correct forward axis.
                Vector3 localN  = port.OutwardNormal;
                Vector3 hint    = MathF.Abs(localN.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
                Vector3 localR  = Vector3.Normalize(Vector3.Cross(hint, localN));
                Vector3 localUp = Vector3.Normalize(Vector3.Cross(localN, localR));
                Vector3 wf      = Vector3.Normalize(Vector3.TransformNormal(localUp, mod.Transform));
                pad.LocalForward = new DVec3(wf.X, wf.Y, wf.Z);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // internal, not private: Brief P1's Nova Anchorage regression test needs to reconstruct
    // the exact same StationProfile (and therefore economy/palette) that Generate() derived
    // internally, without a second, drifting seed derivation living in test code.
    internal static int NameHash(string name)
    {
        int h = 17;
        foreach (char c in name) h = h * 31 + c;
        return h;
    }
}
