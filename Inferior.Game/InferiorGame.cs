using Inferior.Core;
using Inferior.Galaxy;
using Inferior.Game.States;
using Inferior.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Inferior.Game;

public class InferiorGame : Microsoft.Xna.Framework.Game
{
    private GraphicsDeviceManager _graphics;
    private SpriteBatch? _spriteBatch;

    private GameStateMachine _stateMachine = new();
    private SpriteFont _font = null!;
    private readonly SpaceSimulation _simulation = new();

    // ── Window mode ───────────────────────────────────────────────────────────

    private enum WindowMode { Windowed, Borderless, Fullscreen }
    private WindowMode   _windowMode = WindowMode.Borderless;
    private KeyboardState _prevKeys;

    private const int DefaultWindowWidth  = 1600;
    private const int DefaultWindowHeight = 900;

    // ── Constructor ───────────────────────────────────────────────────────────

    public InferiorGame()
    {
        _graphics = new GraphicsDeviceManager(this);
        _graphics.PreferredBackBufferWidth  = DefaultWindowWidth;
        _graphics.PreferredBackBufferHeight = DefaultWindowHeight;
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void Initialize()
    {
        _simulation.Start();

        Window.TextInput += (_, e) => InputState.PushTypedChar(e.Character);

        // Forward resize events to the active game state so UI panels reflow correctly.
        Window.ClientSizeChanged += (_, _) =>
            _stateMachine.OnResize(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

        base.Initialize();

        // Apply startup window mode AFTER base.Initialize() so the graphics device
        // exists and ApplyChanges() takes effect. base.Initialize() also runs
        // LoadContent(), so the state machine is live and OnResize() will reflow the UI.
        ApplyWindowMode();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _font = Content.Load<SpriteFont>("Fonts/DefaultFont");

        _stateMachine.Register(new GalaxyMapState(GraphicsDevice, _font));
        _stateMachine.Register(new SystemMapState(GraphicsDevice, _font));
        _stateMachine.Register(new SystemSpaceState(GraphicsDevice, _font, _simulation, Content));

        var galaxy = GalaxyGenerator.Generate();
        var starterResult = StarterSystemSelector.SelectStar(galaxy);
        if (starterResult.Diagnostic != null)
            System.Console.WriteLine($"[Starter] {starterResult.Diagnostic}");
        _stateMachine.Start(GameStateId.SystemSpace,
            new SystemSpacePayload(starterResult.Star, null, 0.0, null, InitialNewGameStarterEntry: true));
    }

    protected override void Update(GameTime gameTime)
    {
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed)
            Exit();

        var keys = Keyboard.GetState();
        if (keys.IsKeyDown(Keys.F12) && !_prevKeys.IsKeyDown(Keys.F12))
            CycleWindowMode();
        _prevKeys = keys;

        Core.DataBus.DataBus.Drain();
        _stateMachine.IsActive = IsActive;
        _stateMachine.Update(gameTime);
        IsMouseVisible = _stateMachine.CurrentWantsCursor;
        base.Update(gameTime);
    }

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        _simulation.Stop();
        base.OnExiting(sender, args);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);
        _stateMachine.Draw(gameTime, GraphicsDevice, _spriteBatch!);
        base.Draw(gameTime);
    }

    // ── Window mode cycling ───────────────────────────────────────────────────

    private void CycleWindowMode()
    {
        _windowMode = _windowMode switch
        {
            WindowMode.Windowed   => WindowMode.Borderless,
            WindowMode.Borderless => WindowMode.Fullscreen,
            WindowMode.Fullscreen => WindowMode.Windowed,
            _                     => WindowMode.Borderless,
        };
        ApplyWindowMode();
    }

    private void ApplyWindowMode()
    {
        var dm = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;

        switch (_windowMode)
        {
            case WindowMode.Windowed:
                _graphics.IsFullScreen              = false;
                _graphics.HardwareModeSwitch        = true;
                _graphics.PreferredBackBufferWidth  = DefaultWindowWidth;
                _graphics.PreferredBackBufferHeight = DefaultWindowHeight;
                _graphics.ApplyChanges();
                Window.Position = new Point(
                    (dm.Width  - DefaultWindowWidth)  / 2,
                    (dm.Height - DefaultWindowHeight) / 2);
                break;

            case WindowMode.Borderless:
                _graphics.HardwareModeSwitch = false;
                _graphics.IsFullScreen       = true;
                _graphics.ApplyChanges();
                break;

            case WindowMode.Fullscreen:
                _graphics.HardwareModeSwitch        = true;
                _graphics.PreferredBackBufferWidth  = dm.Width;
                _graphics.PreferredBackBufferHeight = dm.Height;
                _graphics.IsFullScreen              = true;
                _graphics.ApplyChanges();
                break;
        }
    }
}
