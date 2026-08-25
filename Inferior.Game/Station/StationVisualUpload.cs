using System.Diagnostics;

namespace Inferior.Game.StationGen;

internal static class StationGpuByteAccounting
{
    public static long TextureBytes(int width, int height, int bytesPerPixel)
    {
        if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (bytesPerPixel < 0) throw new ArgumentOutOfRangeException(nameof(bytesPerPixel));
        return checked((long)width * height * bytesPerPixel);
    }

    public static long VertexBufferBytes(int vertexCount, int vertexStride)
    {
        if (vertexCount < 0) throw new ArgumentOutOfRangeException(nameof(vertexCount));
        if (vertexStride < 0) throw new ArgumentOutOfRangeException(nameof(vertexStride));
        return checked((long)vertexCount * vertexStride);
    }

    public static long IndexBufferBytes(
        int indexCount,
        Microsoft.Xna.Framework.Graphics.IndexElementSize elementSize)
    {
        int bytesPerIndex = elementSize switch
        {
            Microsoft.Xna.Framework.Graphics.IndexElementSize.SixteenBits => 2,
            Microsoft.Xna.Framework.Graphics.IndexElementSize.ThirtyTwoBits => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(elementSize)),
        };
        return TextureBytes(indexCount, 1, bytesPerIndex);
    }

    public static long ShadowMapBytes(
        int width,
        int height,
        Microsoft.Xna.Framework.Graphics.SurfaceFormat colorFormat,
        Microsoft.Xna.Framework.Graphics.DepthFormat depthFormat)
        => TextureBytes(
            width,
            height,
            ColorBytesPerPixel(colorFormat) + DepthBytesPerPixel(depthFormat));

    public static long ResidentOwnedBytes(long uploadedResourceBytes, long shadowMapBytes)
    {
        if (uploadedResourceBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(uploadedResourceBytes));
        if (shadowMapBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(shadowMapBytes));
        return checked(uploadedResourceBytes + shadowMapBytes);
    }

    private static int ColorBytesPerPixel(
        Microsoft.Xna.Framework.Graphics.SurfaceFormat format)
        => format switch
        {
            Microsoft.Xna.Framework.Graphics.SurfaceFormat.Single => 4,
            Microsoft.Xna.Framework.Graphics.SurfaceFormat.Color => 4,
            _ => throw new NotSupportedException(
                $"Station GPU byte accounting does not define color format {format}."),
        };

    private static int DepthBytesPerPixel(
        Microsoft.Xna.Framework.Graphics.DepthFormat format)
        => format switch
        {
            Microsoft.Xna.Framework.Graphics.DepthFormat.None => 0,
            Microsoft.Xna.Framework.Graphics.DepthFormat.Depth16 => 2,
            Microsoft.Xna.Framework.Graphics.DepthFormat.Depth24 => 4,
            Microsoft.Xna.Framework.Graphics.DepthFormat.Depth24Stencil8 => 4,
            _ => throw new NotSupportedException(
                $"Station GPU byte accounting does not define depth format {format}."),
        };
}

public enum StationVisualUploadResourceKind
{
    PanelAlbedoTexture,
    MaterialTexture,
    HullMesh,
    DecorationMesh,
    FlatDecorationMesh,
    GlassMesh,
    ShadowHullMesh,
    ShadowDecorationMesh,
}

public enum StationVisualUploadDiagnosticPurpose
{
    None,
    MegastationInfrastructureVisible,
    MegastationInfrastructureShadow,
    MegastationMegaGreebleVisible,
    MegastationMegaGreebleShadow,
}

public sealed record StationVisualUploadPlanItem(
    StationVisualUploadResourceKind Kind,
    string ResourceIdentity,
    long EstimatedBytes,
    PlacedModule? Module = null,
    PreparedStationTexture? Texture = null,
    StationMeshCpuData? Mesh = null,
    (Microsoft.Xna.Framework.Vector3 Min, Microsoft.Xna.Framework.Vector3 Max)? Bounds = null,
    StationVisualUploadDiagnosticPurpose DiagnosticPurpose = StationVisualUploadDiagnosticPurpose.None)
{
    public int VertexCount => Mesh?.Vertices.Length ?? 0;
    public int IndexCount => Mesh?.Indices.Length ?? 0;
}

internal interface IStationVisualUploadClock
{
    double ElapsedMilliseconds { get; }
}

internal sealed class StationVisualUploadStopwatchClock : IStationVisualUploadClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    public double ElapsedMilliseconds => _stopwatch.Elapsed.TotalMilliseconds;
}

internal sealed record StationVisualUploadWorkItem(
    StationVisualUploadResourceKind Kind,
    string ResourceIdentity,
    long EstimatedBytes,
    Func<IDisposable?> Execute,
    int VertexCount = 0,
    int IndexCount = 0);

internal sealed record StationVisualOversizedOperation(
    StationVisualUploadResourceKind Kind,
    string ResourceIdentity,
    long EstimatedBytes,
    int VertexCount,
    int IndexCount,
    double ElapsedMilliseconds,
    double BudgetOverrunMilliseconds);

internal enum StationVisualUploadSchedulerState
{
    Uploading,
    CleaningCancelled,
    CleaningFailed,
    Completed,
    Cancelled,
    Failed,
}

/// <summary>
/// Station-specific cooperative scheduler. Each work item is one existing GPU resource;
/// calls themselves are indivisible, but the scheduler never starts another item after the
/// current frame slice has expired. Completed resources remain scheduler-owned until the
/// caller atomically transfers them to a completed station package.
/// </summary>
internal sealed class StationVisualUploadScheduler
{
    public const double DefaultFrameBudgetMilliseconds = 2.0;
    private const int MaximumRetainedOversizedOperations = 32;

    private readonly IReadOnlyList<StationVisualUploadWorkItem> _items;
    private readonly IStationVisualUploadClock _clock;
    private readonly List<IDisposable> _ownedResources = [];
    private readonly List<StationVisualOversizedOperation> _oversizedOperations = [];
    private int _nextItem;
    private bool _resourcesReleased;

    public StationVisualUploadScheduler(
        IReadOnlyList<StationVisualUploadWorkItem> items,
        double frameBudgetMilliseconds = DefaultFrameBudgetMilliseconds,
        IStationVisualUploadClock? clock = null)
    {
        if (!double.IsFinite(frameBudgetMilliseconds) || frameBudgetMilliseconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(frameBudgetMilliseconds));
        _items = items;
        FrameBudgetMilliseconds = frameBudgetMilliseconds;
        _clock = clock ?? new StationVisualUploadStopwatchClock();
        TotalEstimatedBytes = items.Sum(item => item.EstimatedBytes);
        if (items.Count == 0)
            State = StationVisualUploadSchedulerState.Completed;
    }

    public double FrameBudgetMilliseconds { get; }
    public StationVisualUploadSchedulerState State { get; private set; } =
        StationVisualUploadSchedulerState.Uploading;
    public int TotalResourceCount => _items.Count;
    public int CompletedResourceCount => _nextItem;
    public long TotalEstimatedBytes { get; }
    public long CompletedEstimatedBytes { get; private set; }
    public int UploadFrameCount { get; private set; }
    public int FrameBudgetOverrunCount { get; private set; }
    public double TotalUploadMilliseconds { get; private set; }
    public double MaximumUploadFrameMilliseconds { get; private set; }
    public double MaximumOperationMilliseconds { get; private set; }
    public double CleanupMilliseconds { get; private set; }
    public int CreatedResourceCount => _ownedResources.Count;
    public Exception? Failure { get; private set; }
    public StationVisualOversizedOperation? LargestOversizedOperation { get; private set; }
    public IReadOnlyList<StationVisualOversizedOperation> OversizedOperations =>
        _oversizedOperations;
    public int OversizedOperationCount { get; private set; }
    public StationVisualOversizedOperation? FailedOperation { get; private set; }
    public StationVisualUploadResourceKind? CurrentPhase =>
        State == StationVisualUploadSchedulerState.Uploading && _nextItem < _items.Count
            ? _items[_nextItem].Kind
            : null;

    public bool IsResolved => State is StationVisualUploadSchedulerState.Completed
        or StationVisualUploadSchedulerState.Cancelled
        or StationVisualUploadSchedulerState.Failed;

    public void Cancel()
    {
        if (State != StationVisualUploadSchedulerState.Uploading)
            return;
        State = StationVisualUploadSchedulerState.CleaningCancelled;
        ResolveEmptyCleanup();
    }

    public void Pump()
    {
        if (IsResolved)
            return;

        double frameStart = _clock.ElapsedMilliseconds;
        bool didWork = false;
        bool uploadFrame = State == StationVisualUploadSchedulerState.Uploading;

        do
        {
            if (State == StationVisualUploadSchedulerState.Uploading)
            {
                if (_nextItem >= _items.Count)
                {
                    State = StationVisualUploadSchedulerState.Completed;
                    break;
                }

                StationVisualUploadWorkItem item = _items[_nextItem];
                double operationStart = _clock.ElapsedMilliseconds;
                try
                {
                    IDisposable? resource = item.Execute();
                    if (resource != null)
                        _ownedResources.Add(resource);
                }
                catch (Exception exception)
                {
                    double failedElapsed = _clock.ElapsedMilliseconds - operationStart;
                    TotalUploadMilliseconds += failedElapsed;
                    MaximumOperationMilliseconds = Math.Max(
                        MaximumOperationMilliseconds,
                        failedElapsed);
                    FailedOperation = new(
                        item.Kind,
                        item.ResourceIdentity,
                        item.EstimatedBytes,
                        item.VertexCount,
                        item.IndexCount,
                        failedElapsed,
                        Math.Max(failedElapsed - FrameBudgetMilliseconds, 0.0));
                    RecordOversizedOperation(FailedOperation);
                    Failure = exception;
                    State = StationVisualUploadSchedulerState.CleaningFailed;
                    ResolveEmptyCleanup();
                    didWork = true;
                    break;
                }

                double operationElapsed = _clock.ElapsedMilliseconds - operationStart;
                TotalUploadMilliseconds += operationElapsed;
                MaximumOperationMilliseconds = Math.Max(
                    MaximumOperationMilliseconds,
                    operationElapsed);
                if (operationElapsed > FrameBudgetMilliseconds)
                {
                    RecordOversizedOperation(new(
                        item.Kind,
                        item.ResourceIdentity,
                        item.EstimatedBytes,
                        item.VertexCount,
                        item.IndexCount,
                        operationElapsed,
                        operationElapsed - FrameBudgetMilliseconds));
                }
                CompletedEstimatedBytes += item.EstimatedBytes;
                _nextItem++;
                didWork = true;
                if (_nextItem >= _items.Count)
                    State = StationVisualUploadSchedulerState.Completed;
            }
            else
            {
                if (_ownedResources.Count == 0)
                {
                    ResolveEmptyCleanup();
                    break;
                }

                int last = _ownedResources.Count - 1;
                IDisposable resource = _ownedResources[last];
                _ownedResources.RemoveAt(last);
                double cleanupStart = _clock.ElapsedMilliseconds;
                try
                {
                    resource.Dispose();
                }
                catch (Exception exception)
                {
                    Failure ??= exception;
                    State = StationVisualUploadSchedulerState.CleaningFailed;
                }
                CleanupMilliseconds += _clock.ElapsedMilliseconds - cleanupStart;
                didWork = true;
                ResolveEmptyCleanup();
            }
        }
        while (!IsResolved
            && (!didWork || _clock.ElapsedMilliseconds - frameStart < FrameBudgetMilliseconds));

        double frameElapsed = _clock.ElapsedMilliseconds - frameStart;
        if (uploadFrame && didWork)
        {
            UploadFrameCount++;
            MaximumUploadFrameMilliseconds = Math.Max(
                MaximumUploadFrameMilliseconds,
                frameElapsed);
            if (frameElapsed > FrameBudgetMilliseconds)
                FrameBudgetOverrunCount++;
        }
    }

    public void ReleaseCompletedResources()
    {
        if (State != StationVisualUploadSchedulerState.Completed)
            throw new InvalidOperationException("Upload resources can only be released after completion.");
        if (_resourcesReleased)
            throw new InvalidOperationException("Upload resources have already been released.");
        _ownedResources.Clear();
        _resourcesReleased = true;
    }

    public void DisposeImmediately()
    {
        if (_resourcesReleased)
            return;
        double cleanupStart = _clock.ElapsedMilliseconds;
        for (int i = _ownedResources.Count - 1; i >= 0; i--)
        {
            try
            {
                _ownedResources[i].Dispose();
            }
            catch (Exception exception)
            {
                Failure ??= exception;
            }
        }
        CleanupMilliseconds += _clock.ElapsedMilliseconds - cleanupStart;
        _ownedResources.Clear();
        if (!IsResolved)
        {
            State = Failure == null
                ? StationVisualUploadSchedulerState.Cancelled
                : StationVisualUploadSchedulerState.Failed;
        }
    }

    private void ResolveEmptyCleanup()
    {
        if (_ownedResources.Count != 0)
            return;
        if (State == StationVisualUploadSchedulerState.CleaningCancelled)
            State = StationVisualUploadSchedulerState.Cancelled;
        else if (State == StationVisualUploadSchedulerState.CleaningFailed)
            State = StationVisualUploadSchedulerState.Failed;
    }

    private void RecordOversizedOperation(StationVisualOversizedOperation operation)
    {
        if (operation.ElapsedMilliseconds <= FrameBudgetMilliseconds)
            return;
        OversizedOperationCount++;
        if (_oversizedOperations.Count < MaximumRetainedOversizedOperations)
            _oversizedOperations.Add(operation);
        if (LargestOversizedOperation == null
            || operation.ElapsedMilliseconds > LargestOversizedOperation.ElapsedMilliseconds)
            LargestOversizedOperation = operation;
    }
}
