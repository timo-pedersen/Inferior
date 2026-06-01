using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Core;

// ── State identifiers ─────────────────────────────────────────────────────────

public enum GameStateId
{
    /// <summary>Top-level galaxy map. Jump planning, commander overview.</summary>
    GalaxyMap,

    /// <summary>Transitioning into hyperspace from system space.</summary>
    HyperspaceEntry,

    /// <summary>Navigating hyperspace (flat, tunnel, or deep variants).</summary>
    Hyperspace,

    /// <summary>Dropping out of hyperspace into a system.</summary>
    HyperspaceExit,

    /// <summary>2D orbital map of a star system — select a body to approach.</summary>
    SystemMap,

    /// <summary>Flying freely within a star system.</summary>
    SystemSpace,

    /// <summary>Close approach to a planet — atmosphere entry possible.</summary>
    PlanetApproach,

    /// <summary>Inside an atmosphere — aerodynamic drag, heat, visual effects.</summary>
    Atmosphere,

    /// <summary>On a planet or moon surface.</summary>
    Surface,

    /// <summary>Docked at a station or outpost.</summary>
    Docked,

    /// <summary>Paused — update frozen, pause menu visible.</summary>
    Paused,

    /// <summary>Main menu / commander select.</summary>
    MainMenu,
}

// ── State transition request ──────────────────────────────────────────────────

/// <summary>
/// Returned by a state's Update to request a transition.
/// Null means stay in current state.
/// </summary>
public sealed class StateTransition
{
    public GameStateId Target  { get; }
    public object?     Payload { get; }   // optional data passed to next state

    public StateTransition(GameStateId target, object? payload = null)
    {
        Target  = target;
        Payload = payload;
    }

    public static StateTransition To(GameStateId target, object? payload = null)
        => new(target, payload);
}

// ── Base class ────────────────────────────────────────────────────────────────

/// <summary>
/// Base class for all game states.
/// Each state owns its update loop, renderer, and input handling.
/// States should be self-contained — no cross-state direct references.
/// Communication between states is via StateTransition payloads.
/// </summary>
public abstract class GameState
{
    public GameStateId Id { get; }

    protected GameState(GameStateId id) => Id = id;

    /// <summary>
    /// Called once when this state becomes active.
    /// Payload is the optional data from the preceding StateTransition.
    /// </summary>
    public virtual void OnEnter(object? payload) { }

    /// <summary>
    /// Called once when leaving this state.
    /// Clean up any state-local resources here.
    /// </summary>
    public virtual void OnExit() { }

    /// <summary>
    /// Update logic. Return a StateTransition to change state, or null to stay.
    /// GameTime is real time. Use GameContext.GameTime for simulation time.
    /// </summary>
    public abstract StateTransition? Update(GameTime gameTime);

    /// <summary>Draw the state. SpriteBatch is not begun — states manage their own batches.</summary>
    public abstract void Draw(GameTime gameTime, GraphicsDevice graphicsDevice, SpriteBatch spriteBatch);

    /// <summary>Optional: handle window resize.</summary>
    public virtual void OnResize(int width, int height) { }
}

// ── State machine ─────────────────────────────────────────────────────────────

/// <summary>
/// Drives the active GameState and handles transitions.
/// Owned by the root Game class.
/// </summary>
public sealed class GameStateMachine
{
    private readonly Dictionary<GameStateId, GameState> _states = new();
    private GameState? _current;

    public GameStateId? CurrentId => _current?.Id;

    public void Register(GameState state)
        => _states[state.Id] = state;

    public void Start(GameStateId initial, object? payload = null)
    {
        if (!_states.TryGetValue(initial, out var state))
            throw new InvalidOperationException($"State {initial} not registered.");

        _current = state;
        _current.OnEnter(payload);
    }

    public void Update(GameTime gameTime)
    {
        if (_current is null) return;

        var transition = _current.Update(gameTime);
        if (transition is null) return;

        if (!_states.TryGetValue(transition.Target, out var next))
            throw new InvalidOperationException($"State {transition.Target} not registered.");

        _current.OnExit();
        _current = next;
        _current.OnEnter(transition.Payload);
    }

    public void Draw(GameTime gameTime, GraphicsDevice graphicsDevice, SpriteBatch spriteBatch)
        => _current?.Draw(gameTime, graphicsDevice, spriteBatch);

    public void OnResize(int width, int height)
        => _current?.OnResize(width, height);
}
