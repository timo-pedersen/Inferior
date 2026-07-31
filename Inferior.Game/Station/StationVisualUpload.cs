using System.Diagnostics;

namespace Inferior.Game.StationGen;

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

public sealed record StationVisualUploadPlanItem(
    StationVisualUploadResourceKind Kind,
    string ResourceIdentity,
    long EstimatedBytes,
    PlacedModule? Module = null,
    PreparedStationTexture? Texture = null,
    StationMeshCpuData? Mesh = null,
    (Microsoft.Xna.Framework.Vector3 Min, Microsoft.Xna.Framework.Vector3 Max)? Bounds = null);

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
    Func<IDisposable?> Execute);

internal sealed record StationVisualOversizedOperation(
    StationVisualUploadResourceKind Kind,
    string ResourceIdentity,
    long EstimatedBytes,
    double ElapsedMilliseconds);

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

    private readonly IReadOnlyList<StationVisualUploadWorkItem> _items;
    private readonly IStationVisualUploadClock _clock;
    private readonly List<IDisposable> _ownedResources = [];
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
                        failedElapsed);
                    if (failedElapsed > FrameBudgetMilliseconds
                        && (LargestOversizedOperation == null
                            || failedElapsed > LargestOversizedOperation.ElapsedMilliseconds))
                        LargestOversizedOperation = FailedOperation;
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
                if (operationElapsed > FrameBudgetMilliseconds
                    && (LargestOversizedOperation == null
                        || operationElapsed > LargestOversizedOperation.ElapsedMilliseconds))
                {
                    LargestOversizedOperation = new(
                        item.Kind,
                        item.ResourceIdentity,
                        item.EstimatedBytes,
                        operationElapsed);
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
}
