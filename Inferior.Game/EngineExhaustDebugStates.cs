using Inferior.Gameplay.Engines;

namespace Inferior.Game;

internal enum EngineExhaustDebugMode
{
    Idle,
    Thrust,
    Brake,
    Boost,
}

internal static class EngineExhaustDebugStates
{
    public static EngineExhaustDebugMode Next(EngineExhaustDebugMode current)
        => current switch
        {
            EngineExhaustDebugMode.Idle => EngineExhaustDebugMode.Thrust,
            EngineExhaustDebugMode.Thrust => EngineExhaustDebugMode.Brake,
            EngineExhaustDebugMode.Brake => EngineExhaustDebugMode.Boost,
            _ => EngineExhaustDebugMode.Idle,
        };

    public static EngineVisualState GetState(EngineExhaustDebugMode mode)
        => mode switch
        {
            EngineExhaustDebugMode.Idle => EngineVisualState.Idle,
            EngineExhaustDebugMode.Thrust => EngineVisualState.Thrust,
            EngineExhaustDebugMode.Brake => EngineVisualState.Braking,
            EngineExhaustDebugMode.Boost => EngineVisualState.Boosting,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

    public static string Notification(EngineExhaustDebugMode mode)
        => $"ENGINE EXHAUST\n{mode}";
}
