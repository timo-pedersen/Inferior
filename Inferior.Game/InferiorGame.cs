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

    public InferiorGame()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
    }

    protected override void Initialize()
    {
        // TODO: Add your initialization logic here

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _font = Content.Load<SpriteFont>("Fonts/DefaultFont");

        _stateMachine.Register(new GalaxyMapState(GraphicsDevice, _font));
        _stateMachine.Register(new SystemMapState(GraphicsDevice, _font));
        _stateMachine.Register(new SystemSpaceState(GraphicsDevice, _font));
        _stateMachine.Start(GameStateId.GalaxyMap);
    }

    protected override void Update(GameTime gameTime)
    {
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();

        // TODO: Add your update logic here
        _stateMachine.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);

        // TODO: Add your drawing code here
        _stateMachine.Draw(gameTime, GraphicsDevice, _spriteBatch!);
        base.Draw(gameTime);
    }
}
