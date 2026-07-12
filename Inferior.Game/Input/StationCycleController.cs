using Inferior.Galaxy;
using Microsoft.Xna.Framework.Input;

namespace Inferior.Game.Input;

internal sealed class StationCycleController(double surfaceStandOffMeters)
{
    private object? _systemKey;
    private int _nextIndex;

    public double SurfaceStandOffMeters { get; } = surfaceStandOffMeters;

    public void Reset()
    {
        _systemKey = null;
        _nextIndex = 0;
    }

    public StationCycleResult Handle(
        KeyboardState keys,
        KeyboardState prevKeys,
        StarSystem system,
        Func<StationCycleRequest, bool> requestRelocation)
    {
        return Handle(keys, prevKeys, system, system.Stations, requestRelocation);
    }

    internal StationCycleResult Handle(
        KeyboardState keys,
        KeyboardState prevKeys,
        object systemKey,
        IEnumerable<Station> stations,
        Func<StationCycleRequest, bool> requestRelocation)
    {
        if (!ReferenceEquals(_systemKey, systemKey))
        {
            _systemKey = systemKey;
            _nextIndex = 0;
        }

        if (!IsCyclePressed(keys, prevKeys))
            return StationCycleResult.NoInput;

        Station[] orderedStations = OrderedStations(stations).ToArray();
        if (orderedStations.Length == 0)
            return StationCycleResult.NoStations;

        int selectedIndex = _nextIndex % orderedStations.Length;
        Station station = orderedStations[selectedIndex];
        if (string.IsNullOrWhiteSpace(station.PersistenceId))
            return StationCycleResult.InvalidStation(station.Name, selectedIndex + 1, orderedStations.Length);

        var request = new StationCycleRequest(
            station.PersistenceId,
            SurfaceStandOffMeters,
            station.Name,
            selectedIndex + 1,
            orderedStations.Length);

        if (!requestRelocation(request))
            return StationCycleResult.Rejected(request);

        _nextIndex = (selectedIndex + 1) % orderedStations.Length;
        return StationCycleResult.Requested(request);
    }

    internal static IEnumerable<Station> OrderedStations(IEnumerable<Station> stations)
        => stations
            .OrderBy(station => station.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(station => station.PersistenceId ?? string.Empty, StringComparer.Ordinal);

    internal static bool IsCyclePressed(KeyboardState keys, KeyboardState prevKeys)
    {
        bool ctrlDown = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);
        bool prevCtrlDown = prevKeys.IsKeyDown(Keys.LeftControl) || prevKeys.IsKeyDown(Keys.RightControl);
        bool chordDown = ctrlDown && keys.IsKeyDown(Keys.F12);
        bool prevChordDown = prevCtrlDown && prevKeys.IsKeyDown(Keys.F12);
        return chordDown && !prevChordDown;
    }
}

internal readonly record struct StationCycleRequest(
    string StationPersistenceId,
    double SurfaceStandOffMeters,
    string StationName,
    int OneBasedIndex,
    int TotalCount);

internal readonly record struct StationCycleResult(
    StationCycleResultKind Kind,
    StationCycleRequest? Request = null,
    string? StationName = null,
    int OneBasedIndex = 0,
    int TotalCount = 0)
{
    public static StationCycleResult NoInput { get; } = new(StationCycleResultKind.NoInput);
    public static StationCycleResult NoStations { get; } = new(StationCycleResultKind.NoStations);

    public static StationCycleResult Requested(StationCycleRequest request)
        => new(StationCycleResultKind.Requested, request);

    public static StationCycleResult Rejected(StationCycleRequest request)
        => new(StationCycleResultKind.Rejected, request);

    public static StationCycleResult InvalidStation(string stationName, int oneBasedIndex, int totalCount)
        => new(StationCycleResultKind.InvalidStation, null, stationName, oneBasedIndex, totalCount);
}

internal enum StationCycleResultKind
{
    NoInput,
    NoStations,
    InvalidStation,
    Rejected,
    Requested,
}
