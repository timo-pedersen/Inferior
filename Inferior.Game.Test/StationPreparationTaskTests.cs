using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Xunit;

namespace Inferior.Game.Test;

public sealed class StationPreparationTaskTests
{
    [Fact]
    public void CancellationInsideMegastationGenerationDoesNotEscapeWorkerBoundary()
    {
        using var cancellation = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        StationPreparationTask<MegastationPrototypeCpuResult> task =
            StationPreparationTask<MegastationPrototypeCpuResult>.Start(
                token =>
                {
                    entered.Set();
                    return MegastationPrototypeGenerator.GenerateCpu(
                        "cancelled-megastation-fixture",
                        cancellationToken: token);
                },
                cancellation.Token);

        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        cancellation.Cancel();
        WaitForCompletion(task);

        StationPreparationOutcome<MegastationPrototypeCpuResult> outcome =
            task.ObserveCompleted();
        Assert.Equal(StationPreparationOutcomeKind.Cancelled, outcome.Kind);
        Assert.Null(outcome.Exception);
    }

    [Fact]
    public void RapidSupersessionObservesCancelledWorkerTask()
    {
        var operation = BlockingPreparation();
        operation.Cancellation.Cancel();
        operation.Release.Set();
        WaitForCompletion(operation.Task);

        StationPreparationOutcome<int> outcome = operation.Task.ObserveCompleted();

        Assert.Equal(StationPreparationOutcomeKind.Cancelled, outcome.Kind);
        Assert.True(operation.Task.ObservationClaimed);
        operation.Dispose();
    }

    [Fact]
    public async Task StateResetObservesAndResolvesInflightCancellation()
    {
        var operation = BlockingPreparation();
        operation.Cancellation.Cancel();
        StationPreparationOutcome<int>? observed = null;
        Task observation = operation.Task.ObserveOnCompletion(
            observed: outcome => observed = outcome);
        operation.Release.Set();

        await observation.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(StationPreparationOutcomeKind.Cancelled, observed?.Kind);
        Assert.True(operation.Task.ObservationClaimed);
        operation.Dispose();
    }

    [Fact]
    public async Task SystemChangeCannotReceiveLateExceptionFromPreviousPreparation()
    {
        var operation = BlockingPreparation();
        operation.Cancellation.Cancel();
        Task detachedObservation = operation.Task.ObserveOnCompletion();
        operation.Release.Set();

        await detachedObservation.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(detachedObservation.IsCompletedSuccessfully);
        Assert.True(operation.Task.ObservationClaimed);
        operation.Dispose();
    }

    [Fact]
    public void ExpectedCancellationDoesNotEnterFailedResidencyState()
    {
        var residency = new StationVisualResidencyState(StationVisualResidencyPolicy.Default);
        StationVisualResidencyAction request = Assert.Single(residency.Evaluate([Candidate("a")]));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        StationPreparationOutcome<int> outcome =
            StationPreparationTask<int>.ExecuteWorker(
                token => throw new OperationCanceledException(token),
                cancellation.Token);

        Assert.Equal(StationPreparationOutcomeKind.Cancelled, outcome.Kind);
        Assert.Null(residency.FailedIdentity);
        Assert.True(residency.CanUpload("a", request.RequestSequence));
    }

    [Fact]
    public void ExpectedCancellationDoesNotTriggerRetrySuppression()
    {
        var residency = new StationVisualResidencyState(StationVisualResidencyPolicy.Default);
        StationVisualResidencyAction first = Assert.Single(residency.Evaluate([Candidate("a")]));
        residency.Reset("cancelled request");

        StationVisualResidencyAction retry = Assert.Single(residency.Evaluate([Candidate("a")]));

        Assert.NotEqual(first.RequestSequence, retry.RequestSequence);
        Assert.Null(residency.FailedIdentity);
    }

    [Fact]
    public void NonCancellationExceptionRemainsGenuineFailure()
    {
        var residency = new StationVisualResidencyState(StationVisualResidencyPolicy.Default);
        StationVisualResidencyAction request = Assert.Single(residency.Evaluate([Candidate("a")]));
        StationPreparationTask<int> task = StationPreparationTask<int>.Start(
            _ => throw new InvalidOperationException("genuine failure"),
            CancellationToken.None);
        WaitForCompletion(task);

        StationPreparationOutcome<int> outcome = task.ObserveCompleted();
        Assert.Equal(StationPreparationOutcomeKind.Faulted, outcome.Kind);
        Assert.IsType<InvalidOperationException>(outcome.Exception);
        Assert.True(residency.ReportGenerationFailure("a", request.RequestSequence));
        Assert.Equal("a", residency.FailedIdentity);
    }

    [Fact]
    public void OperationCanceledExceptionWithoutCancelledRequestTokenRemainsFault()
    {
        StationPreparationTask<int> task = StationPreparationTask<int>.Start(
            _ => throw new OperationCanceledException("not request cancellation"),
            CancellationToken.None);
        WaitForCompletion(task);

        StationPreparationOutcome<int> outcome = task.ObserveCompleted();

        Assert.Equal(StationPreparationOutcomeKind.Faulted, outcome.Kind);
        Assert.IsType<OperationCanceledException>(outcome.Exception);
    }

    [Fact]
    public void CancellationFromDifferentTokenIsNotSwallowed()
    {
        using var requestCancellation = new CancellationTokenSource();
        using var otherCancellation = new CancellationTokenSource();
        otherCancellation.Cancel();

        OperationCanceledException exception = Assert.Throws<OperationCanceledException>(() =>
            StationPreparationTask<int>.ExecuteWorker(
                _ =>
                {
                    requestCancellation.Cancel();
                    throw new OperationCanceledException(otherCancellation.Token);
                },
                requestCancellation.Token));

        Assert.Equal(otherCancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task DeferredRequestStartsOnlyAfterCancelledTaskOwnershipResolves()
    {
        var operation = BlockingPreparation();
        bool deferredStarted = false;
        operation.Cancellation.Cancel();
        Task observation = operation.Task.ObserveOnCompletion(
            observed: _ => deferredStarted = true);

        Assert.False(deferredStarted);
        operation.Release.Set();
        await observation.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(deferredStarted);
        operation.Dispose();
    }

    [Fact]
    public void EveryTerminalPreparationOutcomeCanBeObservedExactlyOnce()
    {
        StationPreparationTask<int> succeeded = StationPreparationTask<int>.Start(
            _ => 1,
            CancellationToken.None);
        StationPreparationTask<int> faulted = StationPreparationTask<int>.Start(
            _ => throw new InvalidOperationException("fault"),
            CancellationToken.None);
        using var cancelledSource = new CancellationTokenSource();
        cancelledSource.Cancel();
        StationPreparationTask<int> cancelled = StationPreparationTask<int>.Start(
            _ => 1,
            cancelledSource.Token);
        WaitForCompletion(succeeded);
        WaitForCompletion(faulted);
        WaitForCompletion(cancelled);

        Assert.Equal(StationPreparationOutcomeKind.Succeeded, succeeded.ObserveCompleted().Kind);
        Assert.Equal(StationPreparationOutcomeKind.Faulted, faulted.ObserveCompleted().Kind);
        Assert.Equal(StationPreparationOutcomeKind.Cancelled, cancelled.ObserveCompleted().Kind);
        Assert.True(succeeded.ObservationClaimed);
        Assert.True(faulted.ObservationClaimed);
        Assert.True(cancelled.ObservationClaimed);
        Assert.Throws<InvalidOperationException>(() => succeeded.ObserveCompleted());
        Assert.Throws<InvalidOperationException>(() => faulted.ObserveCompleted());
        Assert.Throws<InvalidOperationException>(() => cancelled.ObserveCompleted());
    }

    private static StationVisualResidencyCandidate Candidate(string identity) => new(
        identity,
        StationVisualClassification.Standard,
        10_000,
        9_000);

    private static void WaitForCompletion<T>(StationPreparationTask<T> task)
    {
        Assert.True(SpinWait.SpinUntil(
            () => task.IsCompleted,
            TimeSpan.FromSeconds(10)));
    }

    private static BlockingOperation BlockingPreparation()
    {
        var cancellation = new CancellationTokenSource();
        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        StationPreparationTask<int> task = StationPreparationTask<int>.Start(
            token =>
            {
                entered.Set();
                release.Wait();
                token.ThrowIfCancellationRequested();
                return 1;
            },
            cancellation.Token);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        return new(task, cancellation, release, entered);
    }

    private sealed record BlockingOperation(
        StationPreparationTask<int> Task,
        CancellationTokenSource Cancellation,
        ManualResetEventSlim Release,
        ManualResetEventSlim Entered) : IDisposable
    {
        public void Dispose()
        {
            Cancellation.Dispose();
            Release.Dispose();
            Entered.Dispose();
        }
    }
}
