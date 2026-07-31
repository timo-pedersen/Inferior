using System.Diagnostics;
using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Galaxy;
using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.States;

public sealed partial class SystemSpaceState
{
    private sealed record StationVisualDescriptor(
        Galaxy.Station Station,
        string Identity,
        StationVisualClassification Classification,
        double ConservativeEnvelopeRadiusMeters,
        bool UseMegastationPrototype);

    private sealed class StationVisualPackage : IDisposable
    {
        private bool _disposed;

        public StationVisualPackage(
            StationVisualDescriptor descriptor,
            List<PlacedModule> modules,
            IReadOnlyList<Texture2D> textures,
            MegastationPrototypeDiagnostics? megastationDiagnostics,
            double generationMilliseconds,
            Vector3 boundsMin,
            Vector3 boundsMax,
            double envelopeRadiusMeters,
            double renderBoundsRadiusMeters)
        {
            Descriptor = descriptor;
            Modules = modules;
            Textures = textures.ToList();
            MegastationDiagnostics = megastationDiagnostics;
            GenerationMilliseconds = generationMilliseconds;
            BoundsMin = boundsMin;
            BoundsMax = boundsMax;
            EnvelopeRadiusMeters = envelopeRadiusMeters;
            RenderBoundsRadiusMeters = renderBoundsRadiusMeters;
        }

        public StationVisualDescriptor Descriptor { get; }
        public List<PlacedModule> Modules { get; }
        public List<Texture2D> Textures { get; }
        public MegastationPrototypeDiagnostics? MegastationDiagnostics { get; }
        public double GenerationMilliseconds { get; }
        public double UploadMilliseconds { get; set; }
        public Vector3 BoundsMin { get; }
        public Vector3 BoundsMax { get; }
        public double EnvelopeRadiusMeters { get; }
        public double RenderBoundsRadiusMeters { get; }

        public Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> DecoMeshes { get; } = [];
        public Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> FlatDecoMeshes { get; } = [];
        public Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> GlassMeshes { get; } = [];
        public Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> HullMeshes { get; } = [];
        public Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> ShadowCasterMeshes { get; } = [];
        public Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> DecoCasterMeshes { get; } = [];
        public Dictionary<PlacedModule, (Vector3 min, Vector3 max)> ShadowCasterHullBounds { get; } = [];
        public Dictionary<PlacedModule, (Vector3 min, Vector3 max)> ShadowCasterDecoBounds { get; } = [];
        public RenderTarget2D? ShadowMap { get; set; }
        public int ShadowMapResolution { get; set; }
        public StationShadowContext? ShadowContext { get; set; }

        public int VertexCount =>
            HullMeshes.Values.Sum(mesh => mesh.vb.VertexCount)
            + DecoMeshes.Values.Sum(mesh => mesh.vb.VertexCount)
            + GlassMeshes.Values.Sum(mesh => mesh.vb.VertexCount);

        public int TriangleCount =>
            HullMeshes.Values.Sum(mesh => mesh.triCount)
            + DecoMeshes.Values.Sum(mesh => mesh.triCount)
            + GlassMeshes.Values.Sum(mesh => mesh.triCount);

        public int OwnedGpuBufferCount =>
            2 * (HullMeshes.Count
                + DecoMeshes.Count
                + FlatDecoMeshes.Count
                + GlassMeshes.Count
                + ShadowCasterMeshes.Count
                + DecoCasterMeshes.Count);

        public int OwnedTextureCount => Textures.Count + (ShadowMap == null ? 0 : 1);

        public long EstimatedCpuMeshBytes
        {
            get
            {
                long bytes = 0;
                foreach (PlacedModule module in Modules)
                {
                    bytes += EstimateMesh(module.Mesh);
                    bytes += EstimateMesh(module.HullMesh);
                    bytes += EstimateMesh(module.GlassMesh);
                }
                return bytes;
            }
        }

        public long EstimatedGpuBytes
        {
            get
            {
                long bytes = 0;
                Add(HullMeshes);
                Add(DecoMeshes);
                Add(FlatDecoMeshes);
                Add(GlassMeshes);
                Add(ShadowCasterMeshes);
                Add(DecoCasterMeshes);
                foreach (Texture2D texture in Textures)
                    bytes += (long)texture.Width * texture.Height * 4;
                if (ShadowMap != null)
                    bytes += (long)ShadowMap.Width * ShadowMap.Height * 8;
                return bytes;

                void Add(Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> meshes)
                {
                    foreach (var mesh in meshes.Values)
                    {
                        bytes += (long)mesh.vb.VertexCount * mesh.vb.VertexDeclaration.VertexStride;
                        bytes += (long)mesh.ib.IndexCount * 4;
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            DisposeMeshes(HullMeshes);
            DisposeMeshes(DecoMeshes);
            DisposeMeshes(FlatDecoMeshes);
            DisposeMeshes(GlassMeshes);
            DisposeMeshes(ShadowCasterMeshes);
            DisposeMeshes(DecoCasterMeshes);
            foreach (Texture2D texture in Textures)
                texture.Dispose();
            Textures.Clear();
            ShadowMap?.Dispose();
            ShadowMap = null;
            ShadowMapResolution = 0;
            ShadowContext = null;
            ShadowCasterHullBounds.Clear();
            ShadowCasterDecoBounds.Clear();

            foreach (PlacedModule module in Modules)
            {
                module.Mesh = null;
                module.HullMesh = null;
                module.GlassMesh = null;
                module.TextureInstance = null;
                module.MaterialInstance = null;
                module.OpenPorts.Clear();
                module.ChildPorts.Clear();
                module.GlowLights.Clear();
            }
            Modules.Clear();
        }

        private static long EstimateMesh(StationModuleMesh? mesh)
            => mesh == null ? 0 : (long)mesh.VertexCount * 36 + (long)mesh.IndexCount * 4;

        private static void DisposeMeshes(
            Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> meshes)
        {
            foreach (var mesh in meshes.Values)
            {
                mesh.vb.Dispose();
                mesh.ib.Dispose();
            }
            meshes.Clear();
        }
    }

    private readonly StationVisualResidencyPolicy _stationVisualPolicy =
        StationVisualResidencyPolicy.Default;
    private readonly StationVisualResidencyState _stationVisualResidency =
        new(StationVisualResidencyPolicy.Default);
    private readonly StationVisualPackageSlot<StationVisualPackage> _stationVisualSlot = new();
    private readonly Dictionary<string, StationVisualDescriptor> _stationVisualCatalog =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, DVec3> _stationPositionByIdentity =
        new(StringComparer.Ordinal);
    private Task<StationGenerationCpuResult>? _stationPreparationTask;
    private CancellationTokenSource? _stationPreparationCancellation;
    private string? _stationPreparationIdentity;
    private long _stationPreparationSequence;
    private StationVisualResidencyAction? _deferredStationPreparationAction;

    private StationVisualPackage? ResidentStationVisual => _stationVisualSlot.Current;
    private Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> _decoMeshes
        => ResidentStationVisual?.DecoMeshes
            ?? throw new InvalidOperationException("No resident station visual.");
    private Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> _decoMeshesFlat
        => ResidentStationVisual?.FlatDecoMeshes
            ?? throw new InvalidOperationException("No resident station visual.");
    private Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> _glassMeshes
        => ResidentStationVisual?.GlassMeshes
            ?? throw new InvalidOperationException("No resident station visual.");
    private Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> _hullMeshes
        => ResidentStationVisual?.HullMeshes
            ?? throw new InvalidOperationException("No resident station visual.");

    private bool TryGetResidentStation(
        out StationVisualPackage visual,
        out Galaxy.Station station,
        out DVec3 position)
    {
        visual = ResidentStationVisual!;
        if (visual == null
            || !_stationPositionByIdentity.TryGetValue(
                visual.Descriptor.Identity,
                out position))
        {
            station = null!;
            position = DVec3.Zero;
            return false;
        }
        station = visual.Descriptor.Station;
        return true;
    }

    private void BuildStationVisualCatalog()
    {
        _stationVisualCatalog.Clear();
        MegastationDevelopmentSelection selection =
            MegastationPrototypeSettings.DevelopmentSelection;
        Galaxy.Station? starter = selection.ForceStarterStation
            || selection.Mode == MegastationPrototypeSelectionMode.ForceStarterStation
            ? StarterSystemSelector.SelectStarterStation(_system.Stations)
            : null;

        foreach (Galaxy.Station station in _system.Stations)
        {
            string identity = station.PersistenceId ?? station.Name;
            bool useMega = ShouldUseMegastationPrototype(station, starter, selection);
            StationVisualClassification classification = useMega
                ? StationVisualClassification.Megastation
                : StationVisualClassification.Standard;
            double radius = useMega
                ? MegastationPrototypeGenerator.EstimateConservativeEnvelopeRadius(identity)
                : SpaceSimulation.StationPhysicalRadius(station);
            _stationVisualCatalog[identity] = new(
                station,
                identity,
                classification,
                radius,
                useMega);
        }
    }

    private void UpdateStationVisualResidency(DVec3 observerPosition)
    {
        CompleteStationPreparationIfReady();
        ApplyStationResidencyActions(
            _stationVisualResidency.Evaluate(BuildResidencyCandidates(observerPosition)));
    }

    private void RequestExplicitStationVisual(string identity, string reason)
    {
        if (!_stationVisualCatalog.TryGetValue(identity, out StationVisualDescriptor? descriptor))
            return;
        DVec3 observer = _frameShipSnap?.Position ?? _camera.UniversePosition;
        ApplyStationResidencyActions(
            _stationVisualResidency.RequestExplicit(
                BuildResidencyCandidate(descriptor, observer),
                reason));
    }

    private void ResetStationVisualResidency(string reason)
    {
        ApplyStationResidencyActions(_stationVisualResidency.Reset(reason));
        CancelStationPreparation();
        _stationVisualSlot.Clear();
        _stationVisualCatalog.Clear();
        _stationPositionByIdentity.Clear();
    }

    private List<StationVisualResidencyCandidate> BuildResidencyCandidates(DVec3 observer)
    {
        var candidates = new List<StationVisualResidencyCandidate>(_stationVisualCatalog.Count);
        foreach (StationVisualDescriptor descriptor in _stationVisualCatalog.Values)
            candidates.Add(BuildResidencyCandidate(descriptor, observer));
        return candidates;
    }

    private StationVisualResidencyCandidate BuildResidencyCandidate(
        StationVisualDescriptor descriptor,
        DVec3 observer)
    {
        DVec3 position = EclipticToGalaxy(
            _system.GetStationPosition(descriptor.Station, _gameTimeSeconds));
        double centre = (position - observer).Length;
        double envelope = ResidentStationVisual?.Descriptor.Identity == descriptor.Identity
            ? ResidentStationVisual.EnvelopeRadiusMeters
            : descriptor.ConservativeEnvelopeRadiusMeters;
        return new(
            descriptor.Identity,
            descriptor.Classification,
            centre,
            Math.Max(centre - envelope, 0.0));
    }

    private void ApplyStationResidencyActions(
        IReadOnlyList<StationVisualResidencyAction> actions)
    {
        foreach (StationVisualResidencyAction action in actions)
        {
            switch (action.Kind)
            {
                case StationVisualResidencyActionKind.Unload:
                    LogStationResidencyChange(action, ResidentStationVisual, stale: false);
                    _stationVisualSlot.Clear();
                    _stationShadowLogged = false;
                    break;
                case StationVisualResidencyActionKind.CancelPreparation:
                    _stationPreparationCancellation?.Cancel();
                    LogStationResidencyChange(action, null, stale: true);
                    break;
                case StationVisualResidencyActionKind.RequestLoad:
                    StartStationPreparation(action);
                    break;
            }
        }
    }

    private void StartStationPreparation(StationVisualResidencyAction action)
    {
        if (!_stationVisualCatalog.TryGetValue(action.Identity, out StationVisualDescriptor? descriptor))
            return;

        if (_stationPreparationTask != null)
        {
            _stationPreparationCancellation?.Cancel();
            _deferredStationPreparationAction = action;
            return;
        }

        _stationPreparationCancellation = new CancellationTokenSource();
        CancellationToken token = _stationPreparationCancellation.Token;
        _stationPreparationIdentity = descriptor.Identity;
        _stationPreparationSequence = action.RequestSequence;
        _stationPreparationTask = Task.Run(
            () => StationGenerator.PrepareCpu(
                descriptor.Station,
                descriptor.UseMegastationPrototype,
                token),
            token);
        LogStationResidencyChange(action, null, stale: false);
    }

    private void CompleteStationPreparationIfReady()
    {
        Task<StationGenerationCpuResult>? task = _stationPreparationTask;
        if (task == null || !task.IsCompleted)
            return;

        string identity = _stationPreparationIdentity ?? "";
        long sequence = _stationPreparationSequence;
        StationVisualResidencyAction? deferred = _deferredStationPreparationAction;
        _deferredStationPreparationAction = null;
        _stationPreparationTask = null;
        _stationPreparationIdentity = null;
        _stationPreparationSequence = 0;
        _stationPreparationCancellation?.Dispose();
        _stationPreparationCancellation = null;

        if (task.IsCanceled)
        {
            PublishStalePreparation(identity, sequence, "CPU preparation cancelled");
            StartDeferredPreparation(deferred);
            return;
        }
        if (task.IsFaulted)
        {
            Exception exception = task.Exception?.GetBaseException()
                ?? new InvalidOperationException("Unknown station preparation failure.");
            if (_stationVisualResidency.ReportGenerationFailure(identity, sequence))
                PublishStationResidencyMessage(
                    $"[StationResidency] generation failed id={identity}; token={sequence}; " +
                    $"error={exception.Message}; livePackages={_stationVisualSlot.LiveCount}; staleDiscarded=false",
                    SystemMessagePriority.Warning);
            else
                PublishStalePreparation(identity, sequence, exception.Message);
            StartDeferredPreparation(deferred);
            return;
        }

        StationGenerationCpuResult prepared = task.Result;
        if (!_stationVisualResidency.CanUpload(identity, sequence)
            || !_stationVisualCatalog.TryGetValue(identity, out StationVisualDescriptor? descriptor))
        {
            PublishStalePreparation(identity, sequence, "request no longer current");
            StartDeferredPreparation(deferred);
            return;
        }

        var uploadStopwatch = Stopwatch.StartNew();
        StationVisualPackage? package = null;
        try
        {
            package = CreateStationVisualPackage(descriptor, prepared);
            uploadStopwatch.Stop();
            package.UploadMilliseconds = uploadStopwatch.Elapsed.TotalMilliseconds;
            StationGenerator.ApplyPreparedLandingPads(
                descriptor.Station,
                package.Modules);
            if (!_stationVisualResidency.TryInstall(identity, sequence))
            {
                package.Dispose();
                PublishStalePreparation(identity, sequence, "request invalidated before install");
                StartDeferredPreparation(deferred);
                return;
            }

            _stationVisualSlot.Install(package);
            if (package.MegastationDiagnostics is { } diagnostics)
                PublishMegastationPrototypeDiagnostics(
                    diagnostics,
                    MegastationPrototypeSettings.DevelopmentSelection.Mode);
            PublishInstalledStationVisual(package, sequence);
        }
        catch (Exception exception)
        {
            package?.Dispose();
            _stationVisualResidency.ReportGenerationFailure(identity, sequence);
            PublishStationResidencyMessage(
                $"[StationResidency] GPU upload failed id={identity}; token={sequence}; " +
                $"error={exception.Message}; livePackages={_stationVisualSlot.LiveCount}; staleDiscarded=false",
                SystemMessagePriority.Warning);
        }
        StartDeferredPreparation(deferred);
    }

    private void StartDeferredPreparation(StationVisualResidencyAction? deferred)
    {
        if (deferred is not { } action
            || !_stationVisualResidency.CanUpload(action.Identity, action.RequestSequence))
            return;
        StartStationPreparation(action);
    }

    private StationVisualPackage CreateStationVisualPackage(
        StationVisualDescriptor descriptor,
        StationGenerationCpuResult prepared)
    {
        StationGenerationResult uploaded = StationGenerator.UploadPrepared(
            descriptor.Station,
            prepared,
            _gd);
        ComputeStationBounds(
            uploaded.Modules,
            out Vector3 boundsMin,
            out Vector3 boundsMax,
            out double actualBoundsRadius);
        double envelopeRadius = Math.Max(
            actualBoundsRadius,
            descriptor.ConservativeEnvelopeRadiusMeters);
        var package = new StationVisualPackage(
            descriptor,
            uploaded.Modules,
            uploaded.PanelTextures,
            uploaded.MegastationDiagnostics,
            prepared.GenerationMilliseconds,
            boundsMin,
            boundsMax,
            envelopeRadius,
            actualBoundsRadius);
        try
        {
            foreach (PlacedModule module in package.Modules)
            {
                if (prepared.FlatDecorationMeshes.TryGetValue(module, out StationMeshCpuData? flat))
                    package.FlatDecoMeshes[module] = BuildGpuMesh(flat);

                var deco = module.Mesh?.Build(_gd);
                if (deco.HasValue)
                    package.DecoMeshes[module] = deco.Value;
                var glass = module.GlassMesh?.Build(_gd);
                if (glass.HasValue)
                    package.GlassMeshes[module] = glass.Value;
                if (module.Definition.MeshFactory == null)
                    package.HullMeshes[module] = BuildHullMesh(_gd, module);
                else
                {
                    var hull = module.HullMesh?.Build(_gd);
                    if (hull.HasValue)
                        package.HullMeshes[module] = hull.Value;
                }
            }
            BuildStationShadowCasterMeshes(package);
            return package;
        }
        catch
        {
            package.Dispose();
            throw;
        }
    }

    private (VertexBuffer vb, IndexBuffer ib, int triCount) BuildGpuMesh(
        StationMeshCpuData mesh)
    {
        var vb = new VertexBuffer(
            _gd,
            VertexPositionNormalColorTexture.VertexDeclaration,
            mesh.Vertices.Length,
            BufferUsage.WriteOnly);
        var ib = new IndexBuffer(
            _gd,
            IndexElementSize.ThirtyTwoBits,
            mesh.Indices.Length,
            BufferUsage.WriteOnly);
        try
        {
            vb.SetData(mesh.Vertices);
            ib.SetData(mesh.Indices);
            return (vb, ib, mesh.Indices.Length / 3);
        }
        catch
        {
            vb.Dispose();
            ib.Dispose();
            throw;
        }
    }

    private static void ComputeStationBounds(
        IReadOnlyList<PlacedModule> modules,
        out Vector3 min,
        out Vector3 max,
        out double radius)
    {
        min = new Vector3(float.MaxValue);
        max = new Vector3(float.MinValue);
        foreach (PlacedModule module in modules)
        {
            min = Vector3.Min(min, module.AabbMin);
            max = Vector3.Max(max, module.AabbMax);
            ExpandMeshBounds(module.Mesh, module.Transform, ref min, ref max);
            ExpandMeshBounds(module.HullMesh, module.Transform, ref min, ref max);
            ExpandMeshBounds(module.GlassMesh, module.Transform, ref min, ref max);
        }
        if (modules.Count == 0)
            min = max = Vector3.Zero;

        radius = 0.0;
        for (int x = 0; x <= 1; x++)
        for (int y = 0; y <= 1; y++)
        for (int z = 0; z <= 1; z++)
        {
            var corner = new Vector3(
                x == 0 ? min.X : max.X,
                y == 0 ? min.Y : max.Y,
                z == 0 ? min.Z : max.Z);
            radius = Math.Max(radius, corner.Length());
        }

    }

    private static void ExpandMeshBounds(
        StationModuleMesh? mesh,
        Matrix transform,
        ref Vector3 min,
        ref Vector3 max)
    {
        if (mesh == null)
            return;
        var bounds = mesh.ComputeFaceRangeBounds(0, mesh.FaceCount);
        if (bounds == null)
            return;
        for (int x = 0; x <= 1; x++)
        for (int y = 0; y <= 1; y++)
        for (int z = 0; z <= 1; z++)
        {
            var corner = new Vector3(
                x == 0 ? bounds.Value.min.X : bounds.Value.max.X,
                y == 0 ? bounds.Value.min.Y : bounds.Value.max.Y,
                z == 0 ? bounds.Value.min.Z : bounds.Value.max.Z);
            Vector3 stationLocal = Vector3.Transform(corner, transform);
            min = Vector3.Min(min, stationLocal);
            max = Vector3.Max(max, stationLocal);
        }
    }

    private void CancelStationPreparation()
    {
        _stationPreparationCancellation?.Cancel();
        Task<StationGenerationCpuResult>? abandoned = _stationPreparationTask;
        if (abandoned != null)
        {
            _ = abandoned.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        _stationPreparationTask = null;
        _stationPreparationIdentity = null;
        _stationPreparationSequence = 0;
        _stationPreparationCancellation?.Dispose();
        _stationPreparationCancellation = null;
        _deferredStationPreparationAction = null;
    }

    private void PublishInstalledStationVisual(
        StationVisualPackage package,
        long sequence)
    {
        StationVisualResidencyCandidate candidate = BuildResidencyCandidate(
            package.Descriptor,
            _frameShipSnap?.Position ?? _camera.UniversePosition);
        StationVisualDistanceRange range = _stationVisualPolicy.For(
            package.Descriptor.Classification);
        PublishStationResidencyMessage(
            $"[StationResidency] installed id={package.Descriptor.Identity}; " +
            $"class={package.Descriptor.Classification}; reason=preparation complete; " +
            $"centre={candidate.CentreDistanceMeters:F1}m; surface={candidate.SurfaceDistanceMeters:F1}m; " +
            $"load={range.LoadDistanceMeters:F0}m; unload={range.UnloadDistanceMeters:F0}m; token={sequence}; " +
            $"generationMs={package.GenerationMilliseconds:F1}; uploadMs={package.UploadMilliseconds:F1}; " +
            $"vertices={package.VertexCount}; triangles={package.TriangleCount}; " +
            $"livePackages={_stationVisualSlot.LiveCount}; gpuBuffers={package.OwnedGpuBufferCount}; " +
            $"textures={package.OwnedTextureCount}; cpuMeshBytes={package.EstimatedCpuMeshBytes}; " +
            $"gpuBytes={package.EstimatedGpuBytes}; staleDiscarded=false",
            SystemMessagePriority.NB);
    }

    private void LogStationResidencyChange(
        StationVisualResidencyAction action,
        StationVisualPackage? package,
        bool stale)
    {
        StationVisualDescriptor? descriptor = package?.Descriptor;
        if (descriptor == null)
            _stationVisualCatalog.TryGetValue(action.Identity, out descriptor);
        StationVisualClassification classification =
            descriptor?.Classification ?? action.Candidate.Classification;
        StationVisualDistanceRange range = _stationVisualPolicy.For(classification);
        string verb = action.Kind switch
        {
            StationVisualResidencyActionKind.RequestLoad => "requested",
            StationVisualResidencyActionKind.Unload => "unloaded",
            _ => "discarded",
        };
        int livePackagesAfterChange =
            action.Kind == StationVisualResidencyActionKind.Unload
                ? 0
                : _stationVisualSlot.LiveCount;
        PublishStationResidencyMessage(
            $"[StationResidency] {verb} id={action.Identity}; class={classification}; " +
            $"reason={action.Reason}; centre={action.Candidate.CentreDistanceMeters:F1}m; " +
            $"surface={action.Candidate.SurfaceDistanceMeters:F1}m; load={range.LoadDistanceMeters:F0}m; " +
            $"unload={range.UnloadDistanceMeters:F0}m; token={action.RequestSequence}; " +
            $"generationMs={(package?.GenerationMilliseconds ?? 0):F1}; " +
            $"uploadMs={(package?.UploadMilliseconds ?? 0):F1}; vertices={package?.VertexCount ?? 0}; " +
            $"triangles={package?.TriangleCount ?? 0}; livePackages={livePackagesAfterChange}; " +
            $"gpuBuffers={package?.OwnedGpuBufferCount ?? 0}; textures={package?.OwnedTextureCount ?? 0}; " +
            $"cpuMeshBytes={package?.EstimatedCpuMeshBytes ?? 0}; gpuBytes={package?.EstimatedGpuBytes ?? 0}; " +
            $"staleDiscarded={stale.ToString().ToLowerInvariant()}",
            SystemMessagePriority.NB);
    }

    private void PublishStalePreparation(string identity, long sequence, string reason)
        => PublishStationResidencyMessage(
            $"[StationResidency] discarded id={identity}; reason={reason}; token={sequence}; " +
            $"livePackages={_stationVisualSlot.LiveCount}; staleDiscarded=true",
            SystemMessagePriority.NB);

    private static void PublishStationResidencyMessage(
        string message,
        SystemMessagePriority priority)
    {
        Console.WriteLine(message);
        DataBus.System.Publish(
            Topics.System.All,
            new SystemMessage(message, priority));
    }
}
