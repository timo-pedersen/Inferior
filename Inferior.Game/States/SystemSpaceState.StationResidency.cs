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

    private sealed record PreparedStationVisualCpuResult(
        StationGenerationCpuResult Generation,
        Vector3 BoundsMin,
        Vector3 BoundsMax,
        double RenderBoundsRadiusMeters);

    private sealed class PendingStationVisualUpload(
        StationVisualDescriptor descriptor,
        long requestSequence,
        PreparedStationVisualCpuResult prepared,
        StationVisualPackage package,
        StationVisualUploadScheduler scheduler)
    {
        public StationVisualDescriptor Descriptor { get; } = descriptor;
        public long RequestSequence { get; } = requestSequence;
        public PreparedStationVisualCpuResult Prepared { get; } = prepared;
        public StationVisualPackage Package { get; } = package;
        public StationVisualUploadScheduler Scheduler { get; } = scheduler;
        public Stopwatch WallStopwatch { get; } = Stopwatch.StartNew();
        public string CancellationReason { get; set; } = "request invalidated";
        public StationVisualUploadResourceKind? CancellationPhase { get; set; }
    }

    private sealed class DelegateDisposable(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }

    private sealed class StationVisualPackage : IDisposable
    {
        private sealed record TextureUploadDiagnostic(
            Texture2D Texture,
            StationVisualUploadResourceKind Purpose,
            string ResourceIdentity,
            int OperationOrdinal,
            string PreviousOperation,
            int Width,
            int Height,
            SurfaceFormat Format,
            bool HasMipmaps,
            long ByteCount,
            double ConstructorMilliseconds,
            double SetDataMilliseconds,
            double OwnershipAssignmentMilliseconds);

        private sealed record TextureDisposalDiagnostic(
            string ResourceIdentity,
            double ElapsedMilliseconds);

        private bool _disposed;
        private readonly List<TextureUploadDiagnostic> _textureUploadDiagnostics = [];
        private readonly List<TextureDisposalDiagnostic> _textureDisposalDiagnostics = [];

        public StationVisualPackage(
            StationVisualDescriptor descriptor,
            List<PlacedModule> modules,
            IReadOnlyList<Texture2D> textures,
            MegastationPrototypeDiagnostics? megastationDiagnostics,
            MegastationSemanticZoningResult? megastationSemanticZoning,
            MegastationWindowDiagnostics? megastationWindowDiagnostics,
            StationTexturePreparationDiagnostics textureDiagnostics,
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
            MegastationSemanticZoning = megastationSemanticZoning;
            MegastationWindowDiagnostics = megastationWindowDiagnostics;
            TextureDiagnostics = textureDiagnostics;
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
        public MegastationSemanticZoningResult? MegastationSemanticZoning { get; }
        public MegastationWindowDiagnostics? MegastationWindowDiagnostics { get; }
        public StationTexturePreparationDiagnostics TextureDiagnostics { get; }
        public double GenerationMilliseconds { get; }
        public double UploadMilliseconds { get; set; }
        public double UploadWallMilliseconds { get; set; }
        public double FinalCommitMilliseconds { get; set; }
        public long UploadedResourceGpuBytes { get; set; }
        public int TextureReferenceAssignmentCount { get; set; }
        public double TextureReferenceAssignmentMilliseconds { get; set; }
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
        public Dictionary<MegastationZoneRole, IndexBuffer> SemanticDebugIndexBuffers { get; } = [];
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

        public int OwnedTextureCount => Textures.Count;
        public int OwnedShadowMapCount => ShadowMap == null ? 0 : 1;

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

        public long ShadowMapGpuBytes => ShadowMap == null
            ? 0
            : StationGpuByteAccounting.ShadowMapBytes(
                ShadowMap.Width,
                ShadowMap.Height,
                ShadowMap.Format,
                ShadowMap.DepthStencilFormat);

        public long ResidentOwnedGpuBytes => StationGpuByteAccounting.ResidentOwnedBytes(
            UploadedResourceGpuBytes,
            ShadowMapGpuBytes);

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            var totalStopwatch = Stopwatch.StartNew();

            DisposeMeshes(HullMeshes);
            DisposeMeshes(DecoMeshes);
            DisposeMeshes(FlatDecoMeshes);
            DisposeMeshes(GlassMeshes);
            DisposeMeshes(ShadowCasterMeshes);
            DisposeMeshes(DecoCasterMeshes);
            foreach (IndexBuffer buffer in SemanticDebugIndexBuffers.Values)
                buffer.Dispose();
            SemanticDebugIndexBuffers.Clear();
            foreach (Texture2D texture in Textures)
                DisposeTexture(texture);
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
            totalStopwatch.Stop();
            PublishTextureDisposalDiagnostics(totalStopwatch.Elapsed.TotalMilliseconds);
        }

        public IReadOnlyDictionary<MegastationZoneRole, IndexBuffer> EnsureSemanticDebugIndexBuffers(
            GraphicsDevice graphicsDevice)
        {
            if (SemanticDebugIndexBuffers.Count > 0 || MegastationSemanticZoning == null)
                return SemanticDebugIndexBuffers;

            foreach (MegastationSemanticIndexGroup group in MegastationSemanticZoning.DebugIndexGroups)
            {
                if (group.Indices.Count == 0)
                    continue;
                var buffer = new IndexBuffer(
                    graphicsDevice,
                    IndexElementSize.ThirtyTwoBits,
                    group.Indices.Count,
                    BufferUsage.WriteOnly);
                buffer.SetData(group.Indices.ToArray());
                SemanticDebugIndexBuffers.Add(group.Role, buffer);
            }
            return SemanticDebugIndexBuffers;
        }

        public void RecordTextureUpload(
            Texture2D texture,
            StationVisualUploadPlanItem item,
            int operationOrdinal,
            string previousOperation,
            double constructorMilliseconds,
            double setDataMilliseconds,
            double ownershipAssignmentMilliseconds)
        {
            _textureUploadDiagnostics.Add(new(
                texture,
                item.Kind,
                item.ResourceIdentity,
                operationOrdinal,
                previousOperation,
                texture.Width,
                texture.Height,
                texture.Format,
                texture.LevelCount > 1,
                item.EstimatedBytes,
                constructorMilliseconds,
                setDataMilliseconds,
                ownershipAssignmentMilliseconds));
        }

        public void RemoveAndDisposeTexture(Texture2D texture)
        {
            Textures.Remove(texture);
            DisposeTexture(texture);
        }

        public void PublishTextureUploadDiagnostics()
        {
            int total = _textureUploadDiagnostics.Count;
            double constructorTotal = _textureUploadDiagnostics.Sum(
                diagnostic => diagnostic.ConstructorMilliseconds);
            double setDataTotal = _textureUploadDiagnostics.Sum(
                diagnostic => diagnostic.SetDataMilliseconds);
            double ownershipAssignmentTotal = _textureUploadDiagnostics.Sum(
                diagnostic => diagnostic.OwnershipAssignmentMilliseconds);
            double maximumSetData = total == 0
                ? 0.0
                : _textureUploadDiagnostics.Max(diagnostic => diagnostic.SetDataMilliseconds);
            for (int i = 0; i < total; i++)
            {
                TextureUploadDiagnostic diagnostic = _textureUploadDiagnostics[i];
                Debug.WriteLine(
                    $"[StationTexture] upload station={Descriptor.Identity}; " +
                    $"owner=StationVisualPackage; creation={i + 1}/{total}; " +
                    $"purpose={diagnostic.Purpose}; resource={diagnostic.ResourceIdentity}; " +
                    $"operation={diagnostic.OperationOrdinal}; previous={diagnostic.PreviousOperation}; " +
                    $"dimensions={diagnostic.Width}x{diagnostic.Height}; format={diagnostic.Format}; " +
                    $"mipmaps={diagnostic.HasMipmaps}; bytes={diagnostic.ByteCount}; " +
                    $"constructorMs={diagnostic.ConstructorMilliseconds:F3}; " +
                    $"setDataMs={diagnostic.SetDataMilliseconds:F3}; " +
                    $"ownerAssignmentMs={diagnostic.OwnershipAssignmentMilliseconds:F3}");
            }
            Debug.WriteLine(
                $"[StationTexture] references station={Descriptor.Identity}; " +
                $"owner=PlacedModule; assignments={TextureReferenceAssignmentCount}; " +
                $"assignmentMs={TextureReferenceAssignmentMilliseconds:F3}");
            Debug.WriteLine(
                $"[StationTexture] preparation station={Descriptor.Identity}; " +
                $"generatedTextureObjects={TextureDiagnostics.GeneratedTextureCount}; " +
                $"generatedVariantPairs={TextureDiagnostics.GeneratedVariantPairCount}; " +
                $"selectedUniqueTextureObjects={TextureDiagnostics.SelectedUniqueTextureCount}; " +
                $"selectedUniquePairs={TextureDiagnostics.SelectedUniqueTexturePairCount}; " +
                $"discardedTextureObjects={TextureDiagnostics.DiscardedTextureCount}; " +
                $"uploadedAlbedo={TextureDiagnostics.UploadedAlbedoTextureCount}; " +
                $"uploadedMaterial={TextureDiagnostics.UploadedMaterialTextureCount}; " +
                $"moduleBindings={TextureDiagnostics.ModuleTextureBindingCount}; " +
                $"fallbackReferences={TextureDiagnostics.SharedFallbackReferenceCount}; " +
                $"setDataCalls={_textureUploadDiagnostics.Count}; " +
                $"constructorTotalMs={constructorTotal:F3}; setDataTotalMs={setDataTotal:F3}; " +
                $"maxSetDataMs={maximumSetData:F3}; " +
                $"ownerAssignmentTotalMs={ownershipAssignmentTotal:F3}");
        }

        private void DisposeTexture(Texture2D texture)
        {
            string identity = _textureUploadDiagnostics
                .FirstOrDefault(diagnostic => ReferenceEquals(diagnostic.Texture, texture))
                ?.ResourceIdentity ?? "unknown";
            var stopwatch = Stopwatch.StartNew();
            texture.Dispose();
            stopwatch.Stop();
            _textureDisposalDiagnostics.Add(new(
                identity,
                stopwatch.Elapsed.TotalMilliseconds));
        }

        private void PublishTextureDisposalDiagnostics(double totalMilliseconds)
        {
            foreach (TextureDisposalDiagnostic diagnostic in _textureDisposalDiagnostics)
            {
                Debug.WriteLine(
                    $"[StationTexture] dispose station={Descriptor.Identity}; " +
                    $"owner=StationVisualPackage; resource={diagnostic.ResourceIdentity}; " +
                    $"disposeMs={diagnostic.ElapsedMilliseconds:F3}");
            }
            Debug.WriteLine(
                $"[StationTexture] package-dispose station={Descriptor.Identity}; " +
                $"textures={_textureDisposalDiagnostics.Count}; totalDisposeMs={totalMilliseconds:F3}");
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
    private StationPreparationTask<PreparedStationVisualCpuResult>? _stationPreparationTask;
    private CancellationTokenSource? _stationPreparationCancellation;
    private string? _stationPreparationIdentity;
    private long _stationPreparationSequence;
    private StationVisualResidencyAction? _deferredStationPreparationAction;
    private PendingStationVisualUpload? _stationUploadSession;

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
        PumpStationVisualUpload();
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
        CancelStationPreparation(reason);
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
                    CancelStationUpload(action.Reason);
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

        if (_stationPreparationTask != null || _stationUploadSession != null)
        {
            _stationPreparationCancellation?.Cancel();
            CancelStationUpload("superseded by newer request");
            _deferredStationPreparationAction = action;
            return;
        }

        _stationPreparationCancellation = new CancellationTokenSource();
        CancellationToken token = _stationPreparationCancellation.Token;
        _stationPreparationIdentity = descriptor.Identity;
        _stationPreparationSequence = action.RequestSequence;
        HashSet<DecorClass> enabledShadowCasters = ClassesForStage(_casterStage).ToHashSet();
        _stationPreparationTask = StationPreparationTask<PreparedStationVisualCpuResult>.Start(
            workerToken => PrepareStationVisualCpu(
                descriptor,
                enabledShadowCasters,
                workerToken),
            token);
        LogStationResidencyChange(action, null, stale: false);
    }

    private static PreparedStationVisualCpuResult PrepareStationVisualCpu(
        StationVisualDescriptor descriptor,
        IReadOnlySet<DecorClass> enabledShadowCasters,
        CancellationToken cancellationToken)
    {
        StationGenerationCpuResult generation = StationGenerator.PrepareCpu(
            descriptor.Station,
            descriptor.UseMegastationPrototype,
            cancellationToken,
            enabledShadowCasters);
        cancellationToken.ThrowIfCancellationRequested();
        ComputeStationBounds(
            generation.Modules,
            out Vector3 boundsMin,
            out Vector3 boundsMax,
            out double renderBoundsRadius);
        return new(generation, boundsMin, boundsMax, renderBoundsRadius);
    }

    private void CompleteStationPreparationIfReady()
    {
        StationPreparationTask<PreparedStationVisualCpuResult>? task = _stationPreparationTask;
        if (task == null || !task.IsCompleted)
            return;

        string identity = _stationPreparationIdentity ?? "";
        long sequence = _stationPreparationSequence;
        StationVisualResidencyAction? deferred = _deferredStationPreparationAction;
        StationPreparationOutcome<PreparedStationVisualCpuResult> outcome =
            task.ObserveCompleted();
        _deferredStationPreparationAction = null;
        _stationPreparationTask = null;
        _stationPreparationIdentity = null;
        _stationPreparationSequence = 0;
        _stationPreparationCancellation?.Dispose();
        _stationPreparationCancellation = null;

        if (outcome.Kind == StationPreparationOutcomeKind.Cancelled)
        {
            PublishStalePreparation(identity, sequence, "CPU preparation cancelled");
            StartDeferredPreparation(deferred);
            return;
        }
        if (outcome.Kind == StationPreparationOutcomeKind.Faulted)
        {
            Exception exception = outcome.Exception
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

        PreparedStationVisualCpuResult prepared = outcome.Result
            ?? throw new InvalidOperationException("Successful station preparation returned no result.");
        if (!_stationVisualResidency.CanUpload(identity, sequence)
            || !_stationVisualCatalog.TryGetValue(identity, out StationVisualDescriptor? descriptor))
        {
            ReleasePreparedStationCpu(prepared.Generation);
            PublishStalePreparation(identity, sequence, "request no longer current");
            StartDeferredPreparation(deferred);
            return;
        }

        try
        {
            _stationUploadSession = CreateStationUploadSession(
                descriptor,
                sequence,
                prepared);
            PublishStationUploadStarted(_stationUploadSession);
        }
        catch (Exception exception)
        {
            ReleasePreparedStationCpu(prepared.Generation);
            _stationVisualResidency.ReportGenerationFailure(identity, sequence);
            PublishStationResidencyMessage(
                $"[StationUpload] session creation failed id={identity}; token={sequence}; " +
                $"error={exception.Message}; livePackages={_stationVisualSlot.LiveCount}; staleDiscarded=false",
                SystemMessagePriority.Warning);
            StartDeferredPreparation(deferred);
        }
    }

    private void StartDeferredPreparation(StationVisualResidencyAction? deferred)
    {
        if (deferred is not { } action
            || !_stationVisualResidency.CanUpload(action.Identity, action.RequestSequence))
            return;
        StartStationPreparation(action);
    }

    private PendingStationVisualUpload CreateStationUploadSession(
        StationVisualDescriptor descriptor,
        long sequence,
        PreparedStationVisualCpuResult prepared)
    {
        StationGenerationCpuResult generation = prepared.Generation;
        double envelopeRadius = Math.Max(
            prepared.RenderBoundsRadiusMeters,
            descriptor.ConservativeEnvelopeRadiusMeters);
        var package = new StationVisualPackage(
            descriptor,
            generation.Modules,
            [],
            generation.MegastationDiagnostics,
            generation.MegastationSemanticZoning,
            generation.MegastationWindowDiagnostics,
            generation.TextureDiagnostics,
            generation.GenerationMilliseconds,
            prepared.BoundsMin,
            prepared.BoundsMax,
            envelopeRadius,
            prepared.RenderBoundsRadiusMeters);
        if (generation.UsesSharedMegastationFallbackTextures)
        {
            MeshRenderer renderer = _meshRenderer
                ?? throw new InvalidOperationException(
                    "Megastation fallbacks require an active mesh renderer.");
            var assignmentStopwatch = Stopwatch.StartNew();
            foreach (PlacedModule module in generation.Modules)
            {
                module.TextureInstance = renderer.WhiteFallbackTexture;
                module.MaterialInstance = renderer.StationFallbackMaterialTexture;
            }
            assignmentStopwatch.Stop();
            package.TextureReferenceAssignmentCount = generation.Modules.Count * 2;
            package.TextureReferenceAssignmentMilliseconds =
                assignmentStopwatch.Elapsed.TotalMilliseconds;
        }
        var work = new List<StationVisualUploadWorkItem>(generation.UploadPlan.Count);
        for (int i = 0; i < generation.UploadPlan.Count; i++)
        {
            StationVisualUploadPlanItem item = generation.UploadPlan[i];
            int operationOrdinal = i;
            string previousOperation = i == 0
                ? "none"
                : $"{generation.UploadPlan[i - 1].Kind}:{generation.UploadPlan[i - 1].ResourceIdentity}";
            work.Add(new(
                item.Kind,
                item.ResourceIdentity,
                item.EstimatedBytes,
                () => UploadStationVisualResource(
                    package,
                    item,
                    operationOrdinal,
                    previousOperation),
                item.VertexCount,
                item.IndexCount));
        }
        return new(
            descriptor,
            sequence,
            prepared,
            package,
            new StationVisualUploadScheduler(work));
    }

    private IDisposable UploadStationVisualResource(
        StationVisualPackage package,
        StationVisualUploadPlanItem item,
        int operationOrdinal,
        string previousOperation)
    {
        if (item.Texture is { } preparedTexture)
        {
            var constructorStopwatch = Stopwatch.StartNew();
            var texture = new Texture2D(
                _gd,
                preparedTexture.Width,
                preparedTexture.Height);
            constructorStopwatch.Stop();
            try
            {
                var setDataStopwatch = Stopwatch.StartNew();
                texture.SetData(preparedTexture.Pixels);
                setDataStopwatch.Stop();
                var assignmentStopwatch = Stopwatch.StartNew();
                package.Textures.Add(texture);
                assignmentStopwatch.Stop();
                package.RecordTextureUpload(
                    texture,
                    item,
                    operationOrdinal,
                    previousOperation,
                    constructorStopwatch.Elapsed.TotalMilliseconds,
                    setDataStopwatch.Elapsed.TotalMilliseconds,
                    assignmentStopwatch.Elapsed.TotalMilliseconds);
            }
            catch
            {
                texture.Dispose();
                throw;
            }
            return new DelegateDisposable(() =>
            {
                package.RemoveAndDisposeTexture(texture);
            });
        }

        if (item.Module == null || item.Mesh == null)
            throw new InvalidOperationException($"Upload item '{item.ResourceIdentity}' has no resource data.");

        PlacedModule module = item.Module;
        (VertexBuffer vb, IndexBuffer ib, int triCount) gpu = BuildGpuMesh(item.Mesh);
        Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> target = item.Kind switch
        {
            StationVisualUploadResourceKind.HullMesh => package.HullMeshes,
            StationVisualUploadResourceKind.DecorationMesh => package.DecoMeshes,
            StationVisualUploadResourceKind.FlatDecorationMesh => package.FlatDecoMeshes,
            StationVisualUploadResourceKind.GlassMesh => package.GlassMeshes,
            StationVisualUploadResourceKind.ShadowHullMesh => package.ShadowCasterMeshes,
            StationVisualUploadResourceKind.ShadowDecorationMesh => package.DecoCasterMeshes,
            _ => throw new InvalidOperationException($"Unsupported mesh upload type {item.Kind}.")
        };
        try
        {
            target.Add(module, gpu);
            if (item.Kind == StationVisualUploadResourceKind.ShadowHullMesh
                && item.Bounds is { } hullBounds)
                package.ShadowCasterHullBounds[module] = (hullBounds.Min, hullBounds.Max);
            else if (item.Kind == StationVisualUploadResourceKind.ShadowDecorationMesh
                && item.Bounds is { } decoBounds)
                package.ShadowCasterDecoBounds[module] = (decoBounds.Min, decoBounds.Max);
        }
        catch
        {
            gpu.vb.Dispose();
            gpu.ib.Dispose();
            throw;
        }

        return new DelegateDisposable(() =>
        {
            target.Remove(module);
            if (item.Kind == StationVisualUploadResourceKind.ShadowHullMesh)
                package.ShadowCasterHullBounds.Remove(module);
            else if (item.Kind == StationVisualUploadResourceKind.ShadowDecorationMesh)
                package.ShadowCasterDecoBounds.Remove(module);
            gpu.vb.Dispose();
            gpu.ib.Dispose();
        });
    }

    private void PumpStationVisualUpload()
    {
        PendingStationVisualUpload? session = _stationUploadSession;
        if (session == null)
            return;

        if (session.Scheduler.State == StationVisualUploadSchedulerState.Uploading
            && !_stationVisualResidency.CanUpload(
                session.Descriptor.Identity,
                session.RequestSequence))
        {
            CancelStationUpload("request no longer current");
        }

        StationVisualUploadResourceKind? phaseBeforePump = session.Scheduler.CurrentPhase;
        session.Scheduler.Pump();
        if (session.Scheduler.State == StationVisualUploadSchedulerState.CleaningFailed)
            session.CancellationPhase = phaseBeforePump;
        if (!session.Scheduler.IsResolved)
            return;

        _stationUploadSession = null;
        session.WallStopwatch.Stop();
        if (session.Scheduler.State == StationVisualUploadSchedulerState.Completed)
            CompleteStationVisualUpload(session);
        else
            ResolveAbortedStationUpload(session);
    }

    private void CompleteStationVisualUpload(PendingStationVisualUpload session)
    {
        StationVisualPackage package = session.Package;
        var commitStopwatch = Stopwatch.StartNew();
        bool installed = false;
        try
        {
            if (!_stationVisualResidency.CanUpload(
                    session.Descriptor.Identity,
                    session.RequestSequence))
            {
                session.Scheduler.Cancel();
                session.Scheduler.DisposeImmediately();
                package.Dispose();
                PublishStalePreparation(
                    session.Descriptor.Identity,
                    session.RequestSequence,
                    "request invalidated before install");
                StartDeferredPreparation(TakeDeferredStationPreparation());
                return;
            }

            if (!session.Prepared.Generation.UsesSharedMegastationFallbackTextures)
            {
                var textureAssignmentStopwatch = Stopwatch.StartNew();
                foreach (StationTextureAssignment assignment in session.Prepared.Generation.TextureAssignments)
                {
                    assignment.Module.TextureInstance = package.Textures[assignment.AlbedoTextureIndex];
                    assignment.Module.MaterialInstance = package.Textures[assignment.MaterialTextureIndex];
                }
                textureAssignmentStopwatch.Stop();
                package.TextureReferenceAssignmentCount =
                    session.Prepared.Generation.TextureAssignments.Count * 2;
                package.TextureReferenceAssignmentMilliseconds =
                    textureAssignmentStopwatch.Elapsed.TotalMilliseconds;
            }
            StationGenerator.ApplyPreparedLandingPads(
                session.Descriptor.Station,
                package.Modules);
            if (!_stationVisualResidency.TryInstall(
                    session.Descriptor.Identity,
                    session.RequestSequence))
                throw new InvalidOperationException("Residency transition rejected completed upload.");

            _stationVisualSlot.Install(package);
            session.Scheduler.ReleaseCompletedResources();
            commitStopwatch.Stop();
            package.FinalCommitMilliseconds = commitStopwatch.Elapsed.TotalMilliseconds;
            package.UploadMilliseconds = session.Scheduler.TotalUploadMilliseconds;
            package.UploadWallMilliseconds = session.WallStopwatch.Elapsed.TotalMilliseconds;
            package.UploadedResourceGpuBytes = session.Scheduler.CompletedEstimatedBytes;
            installed = true;
        }
        catch (Exception exception)
        {
            session.Scheduler.DisposeImmediately();
            package.Dispose();
            _stationVisualResidency.ReportGenerationFailure(
                session.Descriptor.Identity,
                session.RequestSequence);
            PublishStationResidencyMessage(
                $"[StationUpload] final commit failed id={session.Descriptor.Identity}; " +
                $"token={session.RequestSequence}; commitMs={commitStopwatch.Elapsed.TotalMilliseconds:F1}; " +
                $"error={exception.Message}; livePackages={_stationVisualSlot.LiveCount}",
                SystemMessagePriority.Warning);
        }
        if (installed)
        {
            if (package.MegastationDiagnostics is { } diagnostics)
                PublishMegastationPrototypeDiagnostics(
                    diagnostics,
                    MegastationPrototypeSettings.DevelopmentSelection.Mode);
            if (package.MegastationSemanticZoning is { } zoning)
                PublishMegastationSemanticZoningDiagnostics(
                    package.Descriptor.Identity,
                    zoning.Diagnostics);
            if (package.MegastationWindowDiagnostics is { } windows)
                PublishMegastationWindowDiagnostics(package.Descriptor.Identity, windows);
            PublishInstalledStationVisual(package, session.RequestSequence, session.Scheduler);
            package.PublishTextureUploadDiagnostics();
            PublishMissingStationHullCasterWarnings(package);
        }
        StartDeferredPreparation(TakeDeferredStationPreparation());
    }

    private static void PublishMissingStationHullCasterWarnings(StationVisualPackage package)
    {
        foreach (PlacedModule module in package.Modules)
        {
            if (package.ShadowCasterMeshes.ContainsKey(module))
                continue;
            DataBus.System.Publish(Topics.System.All, new SystemMessage(
                $"Station shadow: module '{module.Definition.Id}' (category '{module.Definition.Category}') " +
                "has no hull shadow caster — its decoration may cast unattached shadows.",
                SystemMessagePriority.NB));
        }
    }

    private void ResolveAbortedStationUpload(PendingStationVisualUpload session)
    {
        session.Package.Dispose();
        StationVisualUploadScheduler scheduler = session.Scheduler;
        string oversized = scheduler.LargestOversizedOperation is { } operation
            ? $"; oversizedType={operation.Kind}; oversizedId={operation.ResourceIdentity}; " +
              $"oversizedBytes={operation.EstimatedBytes}; oversizedMs={operation.ElapsedMilliseconds:F1}"
            : "; oversizedType=none";
        if (scheduler.State == StationVisualUploadSchedulerState.Failed)
        {
            string failedOperation = scheduler.FailedOperation is { } failed
                ? $"; failedType={failed.Kind}; failedId={failed.ResourceIdentity}; " +
                  $"failedBytes={failed.EstimatedBytes}; failedMs={failed.ElapsedMilliseconds:F1}"
                : "; failedType=cleanup";
            _stationVisualResidency.ReportGenerationFailure(
                session.Descriptor.Identity,
                session.RequestSequence);
            PublishStationResidencyMessage(
                $"[StationUpload] failed id={session.Descriptor.Identity}; token={session.RequestSequence}; " +
                $"phase={session.CancellationPhase}; resources={scheduler.CompletedResourceCount}/{scheduler.TotalResourceCount}; " +
                $"bytes={scheduler.CompletedEstimatedBytes}/{scheduler.TotalEstimatedBytes}; " +
                $"uploadWallMs={session.WallStopwatch.Elapsed.TotalMilliseconds:F1}; " +
                $"gameThreadUploadMs={scheduler.TotalUploadMilliseconds:F1}; " +
                $"maxUploadFrameMs={scheduler.MaximumUploadFrameMilliseconds:F1}; " +
                $"maxUploadOperationMs={scheduler.MaximumOperationMilliseconds:F1}; " +
                $"uploadFrames={scheduler.UploadFrameCount}; budgetOverruns={scheduler.FrameBudgetOverrunCount}; " +
                $"cleanupMs={scheduler.CleanupMilliseconds:F1}; error={scheduler.Failure?.Message}; " +
                $"livePackages={_stationVisualSlot.LiveCount}{failedOperation}{oversized}",
                SystemMessagePriority.Warning);
        }
        else
        {
            PublishStationResidencyMessage(
                $"[StationUpload] cancelled id={session.Descriptor.Identity}; token={session.RequestSequence}; " +
                $"reason={session.CancellationReason}; phase={session.CancellationPhase}; " +
                $"resources={scheduler.CompletedResourceCount}/{scheduler.TotalResourceCount}; " +
                $"bytes={scheduler.CompletedEstimatedBytes}/{scheduler.TotalEstimatedBytes}; " +
                $"uploadWallMs={session.WallStopwatch.Elapsed.TotalMilliseconds:F1}; " +
                $"gameThreadUploadMs={scheduler.TotalUploadMilliseconds:F1}; " +
                $"maxUploadFrameMs={scheduler.MaximumUploadFrameMilliseconds:F1}; " +
                $"maxUploadOperationMs={scheduler.MaximumOperationMilliseconds:F1}; " +
                $"uploadFrames={scheduler.UploadFrameCount}; budgetOverruns={scheduler.FrameBudgetOverrunCount}; " +
                $"cleanupMs={scheduler.CleanupMilliseconds:F1}; livePackages={_stationVisualSlot.LiveCount}{oversized}",
                SystemMessagePriority.NB);
        }
        PublishOversizedStationUploadOperations(
            session.Descriptor.Identity,
            scheduler,
            scheduler.State == StationVisualUploadSchedulerState.Failed
                ? SystemMessagePriority.Warning
                : SystemMessagePriority.NB);
        StartDeferredPreparation(TakeDeferredStationPreparation());
    }

    private void CancelStationUpload(string reason)
    {
        PendingStationVisualUpload? session = _stationUploadSession;
        if (session == null)
            return;
        session.CancellationReason = reason;
        session.CancellationPhase = session.Scheduler.CurrentPhase;
        session.Scheduler.Cancel();
    }

    private StationVisualResidencyAction? TakeDeferredStationPreparation()
    {
        StationVisualResidencyAction? deferred = _deferredStationPreparationAction;
        _deferredStationPreparationAction = null;
        return deferred;
    }

    private (VertexBuffer vb, IndexBuffer ib, int triCount) BuildGpuMesh(
        StationMeshCpuData mesh)
    {
        VertexBuffer? vb = null;
        IndexBuffer? ib = null;
        try
        {
            vb = new VertexBuffer(
                _gd,
                VertexPositionNormalColorTexture.VertexDeclaration,
                mesh.Vertices.Length,
                BufferUsage.WriteOnly);
            ib = new IndexBuffer(
                _gd,
                IndexElementSize.ThirtyTwoBits,
                mesh.Indices.Length,
                BufferUsage.WriteOnly);
            vb.SetData(mesh.Vertices);
            ib.SetData(mesh.Indices);
            return (vb, ib, mesh.Indices.Length / 3);
        }
        catch
        {
            vb?.Dispose();
            ib?.Dispose();
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

    private void CancelStationPreparation(string reason)
    {
        _stationPreparationCancellation?.Cancel();
        StationPreparationTask<PreparedStationVisualCpuResult>? abandoned =
            _stationPreparationTask;
        if (abandoned != null)
        {
            _ = abandoned.ObserveOnCompletion(
                prepared => ReleasePreparedStationCpu(prepared.Generation));
        }
        _stationPreparationTask = null;
        _stationPreparationIdentity = null;
        _stationPreparationSequence = 0;
        _stationPreparationCancellation?.Dispose();
        _stationPreparationCancellation = null;
        if (_stationUploadSession is { } upload)
        {
            var cleanupStopwatch = Stopwatch.StartNew();
            upload.CancellationPhase = upload.Scheduler.CurrentPhase;
            upload.Scheduler.Cancel();
            upload.Scheduler.DisposeImmediately();
            upload.Package.Dispose();
            cleanupStopwatch.Stop();
            PublishStationResidencyMessage(
                $"[StationUpload] cancelled id={upload.Descriptor.Identity}; " +
                $"token={upload.RequestSequence}; reason={reason}; phase={upload.CancellationPhase}; " +
                $"resources={upload.Scheduler.CompletedResourceCount}/{upload.Scheduler.TotalResourceCount}; " +
                $"bytes={upload.Scheduler.CompletedEstimatedBytes}/{upload.Scheduler.TotalEstimatedBytes}; " +
                $"cleanupMs={cleanupStopwatch.Elapsed.TotalMilliseconds:F1}; forcedCleanup=true; " +
                $"livePackages={_stationVisualSlot.LiveCount}",
                SystemMessagePriority.NB);
            _stationUploadSession = null;
        }
        _deferredStationPreparationAction = null;
    }

    private static void ReleasePreparedStationCpu(StationGenerationCpuResult prepared)
    {
        foreach (PlacedModule module in prepared.Modules)
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
        prepared.Modules.Clear();
    }

    private static void PublishStationUploadStarted(PendingStationVisualUpload session)
    {
        StationVisualUploadScheduler scheduler = session.Scheduler;
        PublishStationResidencyMessage(
            $"[StationUpload] started id={session.Descriptor.Identity}; token={session.RequestSequence}; " +
            $"phase={scheduler.CurrentPhase}; resources=0/{scheduler.TotalResourceCount}; " +
            $"bytes=0/{scheduler.TotalEstimatedBytes}; budgetMs={scheduler.FrameBudgetMilliseconds:F1}; " +
            $"pendingVisible=false",
            SystemMessagePriority.NB);
    }

    private static void PublishOversizedStationUploadOperations(
        string stationIdentity,
        StationVisualUploadScheduler scheduler,
        SystemMessagePriority priority)
    {
        foreach (StationVisualOversizedOperation operation in scheduler.OversizedOperations)
        {
            PublishStationResidencyMessage(
                $"[StationUpload] oversized id={stationIdentity}; type={operation.Kind}; " +
                $"mesh={operation.ResourceIdentity}; vertices={operation.VertexCount}; " +
                $"indices={operation.IndexCount}; bytes={operation.EstimatedBytes}; " +
                $"uploadMs={operation.ElapsedMilliseconds:F1}; " +
                $"budgetMs={scheduler.FrameBudgetMilliseconds:F1}; " +
                $"overrunMs={operation.BudgetOverrunMilliseconds:F1}",
                priority);
        }
        int omitted = scheduler.OversizedOperationCount - scheduler.OversizedOperations.Count;
        if (omitted > 0)
        {
            PublishStationResidencyMessage(
                $"[StationUpload] oversized id={stationIdentity}; omitted={omitted}; " +
                $"retained={scheduler.OversizedOperations.Count}; " +
                $"total={scheduler.OversizedOperationCount}",
                priority);
        }
    }

    private void PublishInstalledStationVisual(
        StationVisualPackage package,
        long sequence,
        StationVisualUploadScheduler scheduler)
    {
        StationVisualResidencyCandidate candidate = BuildResidencyCandidate(
            package.Descriptor,
            _frameShipSnap?.Position ?? _camera.UniversePosition);
        StationVisualDistanceRange range = _stationVisualPolicy.For(
            package.Descriptor.Classification);
        string oversized = scheduler.LargestOversizedOperation is { } operation
            ? $"; oversizedType={operation.Kind}; oversizedId={operation.ResourceIdentity}; " +
              $"oversizedBytes={operation.EstimatedBytes}; oversizedMs={operation.ElapsedMilliseconds:F1}"
            : "; oversizedType=none";
        PublishStationResidencyMessage(
            $"[StationResidency] installed id={package.Descriptor.Identity}; " +
            $"class={package.Descriptor.Classification}; reason=preparation complete; " +
            $"centre={candidate.CentreDistanceMeters:F1}m; surface={candidate.SurfaceDistanceMeters:F1}m; " +
            $"load={range.LoadDistanceMeters:F0}m; unload={range.UnloadDistanceMeters:F0}m; token={sequence}; " +
            $"generationMs={package.GenerationMilliseconds:F1}; gameThreadUploadMs={package.UploadMilliseconds:F1}; " +
            $"uploadWallMs={package.UploadWallMilliseconds:F1}; uploadFrames={scheduler.UploadFrameCount}; " +
            $"maxUploadFrameMs={scheduler.MaximumUploadFrameMilliseconds:F1}; " +
            $"maxUploadOperationMs={scheduler.MaximumOperationMilliseconds:F1}; " +
            $"budgetOverruns={scheduler.FrameBudgetOverrunCount}; finalCommitMs={package.FinalCommitMilliseconds:F1}; " +
            $"uploadedResources={scheduler.CompletedResourceCount}/{scheduler.TotalResourceCount}; " +
            $"uploadedBytes={scheduler.CompletedEstimatedBytes}/{scheduler.TotalEstimatedBytes}; " +
            $"vertices={package.VertexCount}; triangles={package.TriangleCount}; " +
            $"livePackages={_stationVisualSlot.LiveCount}; gpuBuffers={package.OwnedGpuBufferCount}; " +
            $"ownedTextures={package.OwnedTextureCount}; shadowMaps={package.OwnedShadowMapCount}; " +
            $"cpuMeshBytes={package.EstimatedCpuMeshBytes}; " +
            $"uploadedResourceGpuBytes={package.UploadedResourceGpuBytes}; " +
            $"shadowMapGpuBytes={package.ShadowMapGpuBytes}; " +
            $"residentOwnedGpuBytes={package.ResidentOwnedGpuBytes}; " +
            $"staleDiscarded=false{oversized}",
            SystemMessagePriority.NB);
        PublishOversizedStationUploadOperations(
            package.Descriptor.Identity,
            scheduler,
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
            $"gameThreadUploadMs={(package?.UploadMilliseconds ?? 0):F1}; vertices={package?.VertexCount ?? 0}; " +
            $"triangles={package?.TriangleCount ?? 0}; livePackages={livePackagesAfterChange}; " +
            $"gpuBuffers={package?.OwnedGpuBufferCount ?? 0}; " +
            $"ownedTextures={package?.OwnedTextureCount ?? 0}; " +
            $"shadowMaps={package?.OwnedShadowMapCount ?? 0}; " +
            $"cpuMeshBytes={package?.EstimatedCpuMeshBytes ?? 0}; " +
            $"uploadedResourceGpuBytes={package?.UploadedResourceGpuBytes ?? 0}; " +
            $"shadowMapGpuBytes={package?.ShadowMapGpuBytes ?? 0}; " +
            $"residentOwnedGpuBytes={package?.ResidentOwnedGpuBytes ?? 0}; " +
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
        Debug.WriteLine(message);
        DataBus.System.Publish(
            Topics.System.All,
            new SystemMessage(message, priority));
    }
}
