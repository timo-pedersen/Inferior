using Inferior.Game.StationGen;
using Inferior.Galaxy;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class StationVisualUploadSchedulerTests
{
    [Fact]
    public void PreparedWorkStartsPendingRatherThanInstallingImmediately()
    {
        var clock = new FakeClock();
        using var slot = new StationVisualPackageSlot<FakePackage>();
        var scheduler = Scheduler(clock, Work(clock, "texture", 0.5));

        Assert.Null(slot.Current);
        Assert.Equal(StationVisualUploadSchedulerState.Uploading, scheduler.State);

        scheduler.Pump();

        Assert.Null(slot.Current);
        Assert.Equal(StationVisualUploadSchedulerState.Completed, scheduler.State);
        scheduler.ReleaseCompletedResources();
        slot.Install(new FakePackage());
        Assert.NotNull(slot.Current);
    }

    [Fact]
    public void SessionResumesAtNextUnfinishedOperation()
    {
        var clock = new FakeClock();
        var executed = new List<string>();
        var scheduler = Scheduler(
            clock,
            Work(clock, "a", 2.5, executed),
            Work(clock, "b", 2.5, executed),
            Work(clock, "c", 2.5, executed));

        scheduler.Pump();
        Assert.Equal(["a"], executed);
        Assert.Equal(1, scheduler.CompletedResourceCount);

        scheduler.Pump();
        Assert.Equal(["a", "b"], executed);
        Assert.Equal(2, scheduler.CompletedResourceCount);

        scheduler.Pump();
        Assert.Equal(["a", "b", "c"], executed);
        Assert.Equal(StationVisualUploadSchedulerState.Completed, scheduler.State);
    }

    [Fact]
    public void CooperativeBudgetIsCheckedBetweenOperations()
    {
        var clock = new FakeClock();
        var executed = new List<string>();
        var scheduler = Scheduler(
            clock,
            Work(clock, "a", 1.25, executed),
            Work(clock, "b", 1.25, executed),
            Work(clock, "c", 1.25, executed));

        scheduler.Pump();

        Assert.Equal(["a", "b"], executed);
        Assert.Equal(2, scheduler.CompletedResourceCount);
        Assert.Equal(1, scheduler.UploadFrameCount);
        Assert.Equal(1, scheduler.FrameBudgetOverrunCount);
    }

    [Fact]
    public void OperationLargerThanBudgetStillMakesProgressAndIsReported()
    {
        var clock = new FakeClock();
        var scheduler = Scheduler(clock, Work(clock, "mega-hull", 7.5));

        scheduler.Pump();

        Assert.Equal(StationVisualUploadSchedulerState.Completed, scheduler.State);
        StationVisualOversizedOperation oversized =
            Assert.IsType<StationVisualOversizedOperation>(scheduler.LargestOversizedOperation);
        Assert.Equal("mega-hull", oversized.ResourceIdentity);
        Assert.Equal(0, oversized.VertexCount);
        Assert.Equal(0, oversized.IndexCount);
        Assert.Equal(7.5, oversized.ElapsedMilliseconds);
        Assert.Equal(5.5, oversized.BudgetOverrunMilliseconds);
        Assert.Equal(1, scheduler.OversizedOperationCount);
        Assert.Same(oversized, Assert.Single(scheduler.OversizedOperations));
    }

    [Fact]
    public void EveryOversizedOperationRetainsMeshMeasurement()
    {
        var clock = new FakeClock();
        var scheduler = new StationVisualUploadScheduler(
            [
                new(
                    StationVisualUploadResourceKind.HullMesh,
                    "module[0]/mega",
                    8_000_000,
                    () =>
                    {
                        clock.Advance(4.5);
                        return new FakeResource(clock, 0.0);
                    },
                    VertexCount: 180_000,
                    IndexCount: 380_000),
                new(
                    StationVisualUploadResourceKind.ShadowHullMesh,
                    "module[0]/mega",
                    8_000_000,
                    () =>
                    {
                        clock.Advance(3.25);
                        return new FakeResource(clock, 0.0);
                    },
                    VertexCount: 180_000,
                    IndexCount: 380_000),
            ],
            2.0,
            clock);

        scheduler.Pump();
        scheduler.Pump();

        Assert.Equal(2, scheduler.OversizedOperationCount);
        Assert.Collection(
            scheduler.OversizedOperations,
            hull =>
            {
                Assert.Equal(StationVisualUploadResourceKind.HullMesh, hull.Kind);
                Assert.Equal(180_000, hull.VertexCount);
                Assert.Equal(380_000, hull.IndexCount);
                Assert.Equal(2.5, hull.BudgetOverrunMilliseconds);
            },
            shadow =>
            {
                Assert.Equal(StationVisualUploadResourceKind.ShadowHullMesh, shadow.Kind);
                Assert.Equal(1.25, shadow.BudgetOverrunMilliseconds);
            });
    }

    [Fact]
    public void MultipleSmallResourcesCanUploadInOneFrame()
    {
        var clock = new FakeClock();
        var scheduler = Scheduler(
            clock,
            Work(clock, "a", 0.25),
            Work(clock, "b", 0.25),
            Work(clock, "c", 0.25));

        scheduler.Pump();

        Assert.Equal(StationVisualUploadSchedulerState.Completed, scheduler.State);
        Assert.Equal(3, scheduler.CompletedResourceCount);
        Assert.Equal(1, scheduler.UploadFrameCount);
    }

    [Fact]
    public void CancellationStopsUploadsAndReleasesTrackedResources()
    {
        var clock = new FakeClock();
        var executed = new List<string>();
        var first = new FakeResource(clock, 2.5);
        var second = new FakeResource(clock, 2.5);
        var scheduler = Scheduler(
            clock,
            Work(clock, "a", 2.5, executed, first),
            Work(clock, "b", 2.5, executed, second));

        scheduler.Pump();
        scheduler.Cancel();
        scheduler.Pump();

        Assert.Equal(["a"], executed);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(0, second.DisposeCount);
        Assert.Equal(StationVisualUploadSchedulerState.Cancelled, scheduler.State);
        Assert.Equal(0, scheduler.CreatedResourceCount);
    }

    [Fact]
    public void FailureCannotLeaveTrackedPartialResources()
    {
        var clock = new FakeClock();
        var first = new FakeResource(clock, 0.25);
        var scheduler = Scheduler(
            clock,
            Work(clock, "a", 2.5, resource: first),
            new StationVisualUploadWorkItem(
                StationVisualUploadResourceKind.HullMesh,
                "failure",
                100,
                () => throw new InvalidOperationException("boom")));

        scheduler.Pump();
        scheduler.Pump();
        Assert.Equal(StationVisualUploadSchedulerState.CleaningFailed, scheduler.State);

        scheduler.Pump();

        Assert.Equal(StationVisualUploadSchedulerState.Failed, scheduler.State);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(0, scheduler.CreatedResourceCount);
        Assert.Equal("boom", scheduler.Failure?.Message);
    }

    [Fact]
    public void ImmediateResetClearsEveryPartialResource()
    {
        var clock = new FakeClock();
        var first = new FakeResource(clock, 0.0);
        var scheduler = Scheduler(
            clock,
            Work(clock, "a", 2.5, resource: first),
            Work(clock, "b", 2.5));

        scheduler.Pump();
        scheduler.DisposeImmediately();

        Assert.Equal(StationVisualUploadSchedulerState.Cancelled, scheduler.State);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(0, scheduler.CreatedResourceCount);
    }

    [Fact]
    public void CompletedResourcesTransferExactlyOnce()
    {
        var clock = new FakeClock();
        var resource = new FakeResource(clock, 0.0);
        var scheduler = Scheduler(clock, Work(clock, "a", 0.5, resource: resource));

        scheduler.Pump();
        scheduler.ReleaseCompletedResources();
        scheduler.DisposeImmediately();

        Assert.Equal(0, resource.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => scheduler.ReleaseCompletedResources());
        Assert.Throws<InvalidOperationException>(() =>
            new StationVisualUploadScheduler(
                [Work(clock, "b", 0.5)],
                2.0,
                clock).ReleaseCompletedResources());
    }

    [Fact]
    public void DeferredRequestStartsOnlyAfterCancelledSessionResolves()
    {
        var clock = new FakeClock();
        var firstResource = new FakeResource(clock, 2.5);
        var first = Scheduler(
            clock,
            Work(clock, "old-a", 2.5, resource: firstResource),
            Work(clock, "old-b", 2.5));
        bool deferredStarted = false;

        first.Pump();
        first.Cancel();
        if (first.IsResolved)
            deferredStarted = true;

        Assert.False(deferredStarted);
        first.Pump();
        if (first.IsResolved)
            deferredStarted = true;

        Assert.True(deferredStarted);
        Assert.Equal(1, firstResource.DisposeCount);
    }

    [Fact]
    public void CompletedStaleUploadCannotInstall()
    {
        var residency = new StationVisualResidencyState(
            StationVisualResidencyPolicy.Default);
        StationVisualResidencyAction oldRequest = Assert.Single(residency.Evaluate([
            new StationVisualResidencyCandidate(
                "old",
                StationVisualClassification.Standard,
                10_000,
                9_000),
        ]));
        var clock = new FakeClock();
        var upload = Scheduler(clock, Work(clock, "old", 0.5));

        residency.RequestExplicit(
            new StationVisualResidencyCandidate(
                "new",
                StationVisualClassification.Standard,
                10_000,
                9_000),
            "superseded");
        upload.Pump();

        Assert.Equal(StationVisualUploadSchedulerState.Completed, upload.State);
        Assert.False(residency.TryInstall("old", oldRequest.RequestSequence));
    }

    [Fact]
    public void EmptyPlanCompletesWithoutCreatingAResource()
    {
        var scheduler = new StationVisualUploadScheduler(
            [],
            StationVisualUploadScheduler.DefaultFrameBudgetMilliseconds,
            new FakeClock());

        Assert.Equal(StationVisualUploadSchedulerState.Completed, scheduler.State);
        Assert.Equal(0, scheduler.TotalResourceCount);
    }

    [Fact]
    public void CpuPreparationBuildsDeterministicOrderedUploadPlan()
    {
        var station = new Station
        {
            Name = "Upload Fixture",
            PersistenceId = "frame-budget-upload-fixture",
            Size = StationSize.Small,
        };

        StationGenerationCpuResult first = StationGenerator.PrepareCpu(station);
        StationGenerationCpuResult second = StationGenerator.PrepareCpu(station);

        Assert.Equal(first.Textures.Count, first.UploadPlan.Count(item => item.Texture != null));
        Assert.Equal(
            first.UploadPlan.Select(item => (item.Kind, item.ResourceIdentity, item.EstimatedBytes)),
            second.UploadPlan.Select(item => (item.Kind, item.ResourceIdentity, item.EstimatedBytes)));
        for (int i = 0; i < first.UploadPlan.Count; i++)
        {
            StationVisualUploadPlanItem a = first.UploadPlan[i];
            StationVisualUploadPlanItem b = second.UploadPlan[i];
            if (a.Texture != null)
                Assert.Equal(a.Texture.Pixels, b.Texture!.Pixels);
            if (a.Mesh != null)
            {
                Assert.Equal(a.Mesh.Vertices, b.Mesh!.Vertices);
                Assert.Equal(a.Mesh.Indices, b.Mesh.Indices);
            }
        }
        Assert.All(first.Modules, module =>
        {
            Assert.Null(module.TextureInstance);
            Assert.Null(module.MaterialInstance);
        });
    }

    [Fact]
    public void BoxHullCpuPreparationRetainsExactResourceShape()
    {
        var module = new PlacedModule
        {
            Definition = new StationModuleDefinition
            {
                Id = "box-fixture",
                Category = "test",
                BoundingBox = new Vector3(20f, 14f, 18f),
                MinScale = StationScale.Outpost,
                Ports = [],
            },
            Transform = Matrix.Identity,
            Seed = 17,
            ChamferDepth = 0.25f,
        };

        StationMeshCpuData first = StationGenerator.PrepareBoxHullMesh(module);
        StationMeshCpuData second = StationGenerator.PrepareBoxHullMesh(module);

        Assert.Equal(24, first.Vertices.Length);
        Assert.Equal(36, first.Indices.Length);
        Assert.Equal(first.Vertices, second.Vertices);
        Assert.Equal(first.Indices, second.Indices);
    }

    private static StationVisualUploadScheduler Scheduler(
        FakeClock clock,
        params StationVisualUploadWorkItem[] items)
        => new(items, 2.0, clock);

    private static StationVisualUploadWorkItem Work(
        FakeClock clock,
        string identity,
        double milliseconds,
        List<string>? executed = null,
        FakeResource? resource = null)
        => new(
            StationVisualUploadResourceKind.HullMesh,
            identity,
            100,
            () =>
            {
                executed?.Add(identity);
                clock.Advance(milliseconds);
                return resource ?? new FakeResource(clock, 0.0);
            });

    private sealed class FakeClock : IStationVisualUploadClock
    {
        public double ElapsedMilliseconds { get; private set; }
        public void Advance(double milliseconds) => ElapsedMilliseconds += milliseconds;
    }

    private sealed class FakeResource(FakeClock clock, double disposeMilliseconds) : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            clock.Advance(disposeMilliseconds);
        }
    }

    private sealed class FakePackage : IDisposable
    {
        public void Dispose() { }
    }
}
