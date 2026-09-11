namespace Inferior.Game.StationGen;

internal enum StationVisualClassification
{
    Standard,
    Megastation,
}

internal readonly record struct StationVisualDistanceRange(
    double LoadDistanceMeters,
    double UnloadDistanceMeters)
{
    public StationVisualDistanceRange Validate()
    {
        if (!double.IsFinite(LoadDistanceMeters) || LoadDistanceMeters < 0.0)
            throw new ArgumentOutOfRangeException(nameof(LoadDistanceMeters));
        if (!double.IsFinite(UnloadDistanceMeters) || UnloadDistanceMeters <= LoadDistanceMeters)
            throw new ArgumentOutOfRangeException(nameof(UnloadDistanceMeters));
        return this;
    }
}

internal sealed class StationVisualResidencyPolicy
{
    public const double DefaultLoadDistanceMeters = 200_000.0;
    public const double DefaultUnloadDistanceMeters = 250_000.0;

    private readonly IReadOnlyDictionary<StationVisualClassification, StationVisualDistanceRange> _overrides;
    private readonly StationVisualDistanceRange _defaultRange;

    public static StationVisualResidencyPolicy Default { get; } = new();

    public StationVisualResidencyPolicy(
        StationVisualDistanceRange? defaultRange = null,
        IReadOnlyDictionary<StationVisualClassification, StationVisualDistanceRange>? overrides = null)
    {
        _defaultRange = (defaultRange ?? new(
            DefaultLoadDistanceMeters,
            DefaultUnloadDistanceMeters)).Validate();
        _overrides = overrides == null
            ? new Dictionary<StationVisualClassification, StationVisualDistanceRange>()
            : overrides.ToDictionary(pair => pair.Key, pair => pair.Value.Validate());
    }

    public StationVisualDistanceRange For(StationVisualClassification classification)
        => _overrides.TryGetValue(classification, out var range) ? range : _defaultRange;
}

internal readonly record struct StationVisualResidencyCandidate(
    string Identity,
    StationVisualClassification Classification,
    double CentreDistanceMeters,
    double SurfaceDistanceMeters);

internal enum StationVisualResidencyActionKind
{
    Unload,
    RequestLoad,
    CancelPreparation,
}

internal readonly record struct StationVisualResidencyAction(
    StationVisualResidencyActionKind Kind,
    string Identity,
    long RequestSequence,
    string Reason,
    StationVisualResidencyCandidate Candidate);

/// <summary>
/// GraphicsDevice-free zero-or-one residency state machine. It owns identity and request
/// sequencing only; the presentation owner synchronously applies returned disposal/upload actions.
/// </summary>
internal sealed class StationVisualResidencyState(StationVisualResidencyPolicy policy)
{
    private long _requestSequence;

    public string? ResidentIdentity { get; private set; }
    public string? PendingIdentity { get; private set; }
    public string? FailedIdentity { get; private set; }
    public long PendingRequestSequence { get; private set; }
    public long CurrentSequence => _requestSequence;

    public IReadOnlyList<StationVisualResidencyAction> Evaluate(
        IReadOnlyList<StationVisualResidencyCandidate> candidates)
    {
        var actions = new List<StationVisualResidencyAction>(2);
        if (FailedIdentity != null)
        {
            StationVisualResidencyCandidate? failed = Find(candidates, FailedIdentity);
            if (failed == null
                || failed.Value.SurfaceDistanceMeters
                    >= policy.For(failed.Value.Classification).UnloadDistanceMeters)
                FailedIdentity = null;
        }

        if (ResidentIdentity != null)
        {
            var resident = Find(candidates, ResidentIdentity);
            if (resident == null)
            {
                actions.Add(Unload(ResidentIdentity, "station no longer belongs to current system", default));
                ResidentIdentity = null;
            }
            else
            {
                var range = policy.For(resident.Value.Classification);
                if (resident.Value.SurfaceDistanceMeters >= range.UnloadDistanceMeters)
                {
                    actions.Add(Unload(ResidentIdentity, "unload boundary crossed", resident.Value));
                    ResidentIdentity = null;
                }
                else
                {
                    return actions;
                }
            }
        }

        if (PendingIdentity != null)
        {
            var pending = Find(candidates, PendingIdentity);
            if (pending == null
                || pending.Value.SurfaceDistanceMeters
                    >= policy.For(pending.Value.Classification).UnloadDistanceMeters)
            {
                actions.Add(CancelPending(
                    pending ?? default,
                    pending == null
                        ? "station no longer belongs to current system"
                        : "unload boundary crossed while preparing"));
            }
            else
            {
                return actions;
            }
        }

        StationVisualResidencyCandidate? nearest = null;
        foreach (StationVisualResidencyCandidate candidate in candidates)
        {
            if (candidate.SurfaceDistanceMeters
                > policy.For(candidate.Classification).LoadDistanceMeters)
                continue;
            if (string.Equals(candidate.Identity, FailedIdentity, StringComparison.Ordinal))
                continue;
            if (nearest == null
                || candidate.SurfaceDistanceMeters < nearest.Value.SurfaceDistanceMeters
                || (candidate.SurfaceDistanceMeters == nearest.Value.SurfaceDistanceMeters
                    && string.CompareOrdinal(candidate.Identity, nearest.Value.Identity) < 0))
                nearest = candidate;
        }

        if (nearest != null)
            actions.Add(BeginRequest(nearest.Value, "load boundary reached"));

        return actions;
    }

    public IReadOnlyList<StationVisualResidencyAction> RequestExplicit(
        StationVisualResidencyCandidate destination,
        string reason)
    {
        var actions = new List<StationVisualResidencyAction>(3);
        if (string.Equals(ResidentIdentity, destination.Identity, StringComparison.Ordinal))
            return actions;
        if (string.Equals(PendingIdentity, destination.Identity, StringComparison.Ordinal))
            return actions;
        if (string.Equals(FailedIdentity, destination.Identity, StringComparison.Ordinal))
            FailedIdentity = null;

        if (ResidentIdentity != null)
        {
            actions.Add(Unload(ResidentIdentity, reason, destination));
            ResidentIdentity = null;
        }
        if (PendingIdentity != null)
            actions.Add(CancelPending(destination, reason));

        actions.Add(BeginRequest(destination, reason));
        return actions;
    }

    public IReadOnlyList<StationVisualResidencyAction> Reset(string reason)
    {
        var actions = new List<StationVisualResidencyAction>(2);
        if (ResidentIdentity != null)
        {
            actions.Add(Unload(ResidentIdentity, reason, default));
            ResidentIdentity = null;
        }
        if (PendingIdentity != null)
            actions.Add(CancelPending(default, reason));
        else
            _requestSequence++;
        FailedIdentity = null;
        return actions;
    }

    public bool CanUpload(string identity, long requestSequence)
        => ResidentIdentity == null
        && string.Equals(PendingIdentity, identity, StringComparison.Ordinal)
        && PendingRequestSequence == requestSequence;

    public bool TryInstall(string identity, long requestSequence)
    {
        if (!CanUpload(identity, requestSequence))
            return false;
        ResidentIdentity = identity;
        PendingIdentity = null;
        PendingRequestSequence = 0;
        FailedIdentity = null;
        return true;
    }

    public bool ReportGenerationFailure(string identity, long requestSequence)
    {
        if (!CanUpload(identity, requestSequence))
            return false;
        PendingIdentity = null;
        PendingRequestSequence = 0;
        FailedIdentity = identity;
        return true;
    }

    private StationVisualResidencyAction BeginRequest(
        StationVisualResidencyCandidate candidate,
        string reason)
    {
        PendingIdentity = candidate.Identity;
        PendingRequestSequence = ++_requestSequence;
        return new(
            StationVisualResidencyActionKind.RequestLoad,
            candidate.Identity,
            PendingRequestSequence,
            reason,
            candidate);
    }

    private StationVisualResidencyAction CancelPending(
        StationVisualResidencyCandidate candidate,
        string reason)
    {
        string identity = PendingIdentity!;
        long sequence = PendingRequestSequence;
        PendingIdentity = null;
        PendingRequestSequence = 0;
        _requestSequence++;
        return new(
            StationVisualResidencyActionKind.CancelPreparation,
            identity,
            sequence,
            reason,
            candidate);
    }

    private StationVisualResidencyAction Unload(
        string identity,
        string reason,
        StationVisualResidencyCandidate candidate)
        => new(
            StationVisualResidencyActionKind.Unload,
            identity,
            _requestSequence,
            reason,
            candidate);

    private static StationVisualResidencyCandidate? Find(
        IReadOnlyList<StationVisualResidencyCandidate> candidates,
        string identity)
    {
        foreach (var candidate in candidates)
            if (string.Equals(candidate.Identity, identity, StringComparison.Ordinal))
                return candidate;
        return null;
    }
}

internal sealed class StationVisualPackageSlot<T> : IDisposable where T : class, IDisposable
{
    public T? Current { get; private set; }
    public int LiveCount => Current == null ? 0 : 1;

    public void Install(T package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (Current != null)
            throw new InvalidOperationException("A station visual package is already installed.");
        Current = package;
    }

    public void Clear()
    {
        T? package = Current;
        Current = null;
        package?.Dispose();
    }

    public void Dispose() => Clear();
}
