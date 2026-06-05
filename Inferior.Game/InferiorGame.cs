using Inferior.Core;
using Inferior.Game.States;
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
    private WindowMode   _windowMode = WindowMode.Windowed;
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

        // Centre the window on screen after the display mode is known
        var dm = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        Window.Position = new Point(
            (dm.Width  - DefaultWindowWidth)  / 2,
            (dm.Height - DefaultWindowHeight) / 2);

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _font = Content.Load<SpriteFont>("Fonts/DefaultFont");

        _stateMachine.Register(new GalaxyMapState(GraphicsDevice, _font));
        _stateMachine.Register(new SystemMapState(GraphicsDevice, _font));
        _stateMachine.Register(new SystemSpaceState(GraphicsDevice, _font, _simulation));
        _stateMachine.Start(GameStateId.GalaxyMap);
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
        GraphicsDevice.Clear(Color.CornflowerBlue);
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
            _                     => WindowMode.Windowed,
        };

        var dm = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;

        switch (_windowMode)
        {
            case WindowMode.Windowed:
                _graphics.IsFullScreen           = false;
                _graphics.HardwareModeSwitch     = true;
                _graphics.PreferredBackBufferWidth  = DefaultWindowWidth;
                _graphics.PreferredBackBufferHeight = DefaultWindowHeight;
                _graphics.ApplyChanges();
                Window.Position = new Point(
                    (dm.Width  - DefaultWindowWidth)  / 2,
                    (dm.Height - DefaultWindowHeight) / 2);
                break;

            case WindowMode.Borderless:
                _graphics.HardwareModeSwitch     = false;
                _graphics.IsFullScreen           = true;
                _graphics.ApplyChanges();
                break;

            case WindowMode.Fullscreen:
                _graphics.HardwareModeSwitch     = true;
                _graphics.PreferredBackBufferWidth  = dm.Width;
                _graphics.PreferredBackBufferHeight = dm.Height;
                _graphics.IsFullScreen           = true;
                _graphics.ApplyChanges();
                break;
        }
    }
}
