namespace Inferior.Game.StationGen;

internal enum StationPreparationOutcomeKind
{
    Succeeded,
    Cancelled,
    Faulted,
}

internal sealed record StationPreparationOutcome<T>(
    StationPreparationOutcomeKind Kind,
    T? Result = default,
    Exception? Exception = null)
{
    public static StationPreparationOutcome<T> Succeeded(T result) =>
        new(StationPreparationOutcomeKind.Succeeded, result);

    public static StationPreparationOutcome<T> Cancelled() =>
        new(StationPreparationOutcomeKind.Cancelled);

    public static StationPreparationOutcome<T> Faulted(Exception exception) =>
        new(StationPreparationOutcomeKind.Faulted, Exception: exception);
}

/// <summary>
/// Owns one station CPU-preparation task. Expected request-token cancellation is converted
/// inside the worker delegate, while unrelated exceptions remain task faults. Completion
/// ownership must be claimed exactly once, either by the normal polling path or by reset/exit.
/// </summary>
internal sealed class StationPreparationTask<T>
{
    private readonly Task<StationPreparationOutcome<T>> _task;
    private int _observationClaimed;

    private StationPreparationTask(Task<StationPreparationOutcome<T>> task)
    {
        _task = task;
    }

    public bool IsCompleted => _task.IsCompleted;
    public bool ObservationClaimed => Volatile.Read(ref _observationClaimed) != 0;

    public static StationPreparationTask<T> Start(
        Func<CancellationToken, T> prepare,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        return new(Task.Run(() => ExecuteWorker(prepare, cancellationToken)));
    }

    public StationPreparationOutcome<T> ObserveCompleted()
    {
        if (!_task.IsCompleted)
            throw new InvalidOperationException("Station preparation has not completed.");
        ClaimObservation();
        return ReadOutcome(_task);
    }

    public Task ObserveOnCompletion(
        Action<T>? releaseSucceededResult = null,
        Action<StationPreparationOutcome<T>>? observed = null)
    {
        ClaimObservation();
        return _task.ContinueWith(
            completed =>
            {
                StationPreparationOutcome<T> outcome = ReadOutcome(completed);
                if (outcome.Kind == StationPreparationOutcomeKind.Succeeded)
                    releaseSucceededResult?.Invoke(outcome.Result!);
                observed?.Invoke(outcome);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    internal static StationPreparationOutcome<T> ExecuteWorker(
        Func<CancellationToken, T> prepare,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return StationPreparationOutcome<T>.Succeeded(prepare(cancellationToken));
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested
                && exception.CancellationToken == cancellationToken)
        {
            return StationPreparationOutcome<T>.Cancelled();
        }
    }

    private void ClaimObservation()
    {
        if (Interlocked.Exchange(ref _observationClaimed, 1) != 0)
            throw new InvalidOperationException("Station preparation task was already observed.");
    }

    private static StationPreparationOutcome<T> ReadOutcome(
        Task<StationPreparationOutcome<T>> task)
    {
        if (task.IsFaulted)
        {
            Exception exception = task.Exception?.GetBaseException()
                ?? new InvalidOperationException("Unknown station preparation failure.");
            return StationPreparationOutcome<T>.Faulted(exception);
        }
        if (task.IsCanceled)
        {
            return StationPreparationOutcome<T>.Faulted(
                new TaskCanceledException(
                    "Station preparation task was cancelled outside its request-token boundary."));
        }
        return task.Result;
    }
}
