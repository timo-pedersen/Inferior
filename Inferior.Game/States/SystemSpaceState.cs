using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Inferior.Core;
using Inferior.Core.Math;
using Inferior.Galaxy;

namespace Inferior.Game.States;

/// <summary>
/// 3D star system flight.
/// The player's camera flies freely through the system.
/// Planets orbit on rails. Lighting comes from the star.
///
/// Coordinate flow:
///   Universe space (DVec3, meters)
///     → subtract camera universe position
///     → multiply by RenderScale (1e-9)
///     → Vector3 render space (float, render units)
///     → BasicEffect World matrix adds per-object scale
///     → GPU applies View + Projection
///
/// Everything visible:
///   - Star at system origin, glowing sphere
///   - Planets as coloured spheres, lit by star
///   - Moons as smaller spheres
///   - Orbit rings as line loops in the ecliptic plane
///   - Asteroid belt as scattered dots (TODO)
///
/// Controls: see Camera3D.cs
/// ESC / back button: return to SystemMapState
/// [ / ]            : time compression
/// Home             : snap camera to star
/// </summary>
public sealed class SystemSpaceState : GameState
{
    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly GraphicsDevice _gd;
    private readonly SpriteFont     _font;

    // ── System data ───────────────────────────────────────────────────────────
    private Star       _star   = null!;
    private StarSystem _system = null!;

    // ── 3D infrastructure ─────────────────────────────────────────────────────
    private Camera3D   _camera = null!;
    private BasicEffect _effect = null!;

    private VertexBuffer _sphereVb = null!;
    private IndexBuffer  _sphereIb = null!;
    private int          _sphereTriCount;

    // Reusable ring vertex array — rebuilt each frame per orbit
    private VertexPositionColor[] _ringVerts = null!;

    // ── 2D overlay (SpriteBatch for HUD) ──────────────────────────────────────
    private Texture2D _pixel = null!;

    // ── Time ──────────────────────────────────────────────────────────────────
    private double _gameTimeSeconds;
    private static readonly double[] TimeCompressions  = [1, 100, 10_000, 1_000_000];
    private static readonly string[] TimeLabels        = ["1x", "100x", "10k x", "1M x"];
    private int _timeCompIndex = 0;
    private double TimeCompression => TimeCompressions[_timeCompIndex];

    // ── Cached body positions ─────────────────────────────────────────────────
    private readonly List<(OrbitalBody body, DVec3 pos)> _bodyPositions = [];

    // ── UI ────────────────────────────────────────────────────────────────────
    private StateTransition? _pendingTransition;
    private MouseState       _prevMouse;
    private KeyboardState    _prevKeys;
    private Rectangle        _backButtonRect;
    private Rectangle        _timeButtonRect;

    // ── Visual constants ──────────────────────────────────────────────────────
    // Visual radii in render units (NOT true physical radius — inflated for visibility)
    private const float StarVisualRadius = 8f;

    // Colours
    private static readonly Color ColBackground = new(4, 4, 12);
    private static readonly Color ColOrbitRing  = new(25, 35, 55, 180);
    private static readonly Color ColHUD        = new(180, 200, 220);
    private static readonly Color ColHUDDim     = new(80, 90, 110);
    private static readonly Color ColPanel      = new(8, 12, 25, 200);
    private static readonly Color ColBorder     = new(40, 60, 90);

    // ── Constructor ───────────────────────────────────────────────────────────

    public SystemSpaceState(GraphicsDevice gd, SpriteFont font)
        : base(GameStateId.SystemSpace)
    {
        _gd   = gd;
        _font = font;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnEnter(object? payload)
    {
        if (payload is Star star)
        {
            _star   = star;
            _system = StarSystem.Generate(star, GalaxyGenerator.SystemSeed(star));
        }

        // Camera starts 300 render units from star, looking at it
        // 300 render units = 300 / 1e-9 = 3e11 m ≈ 2 AU
        var startPos = new DVec3(0, 0.5e11, 3e11); // slightly above ecliptic
        _camera = new Camera3D(startPos, AspectRatio);

        // BasicEffect — our shader
        _effect = new BasicEffect(_gd)
        {
            TextureEnabled   = false,
            VertexColorEnabled = false,
            LightingEnabled  = true,
        };

        // Enable one directional light (the star)
        _effect.DirectionalLight0.Enabled = true;
        _effect.DirectionalLight1.Enabled = false;
        _effect.DirectionalLight2.Enabled = false;

        // Sphere mesh — shared by all planets, scaled per draw
        var (vb, ib) = MeshFactory.CreateSphere(_gd, rings: 24, segments: 24);
        _sphereVb        = vb;
        _sphereIb        = ib;
        _sphereTriCount  = 24 * 24 * 2;

        // Ring vertices reused per orbit ring
        _ringVerts = MeshFactory.CreateRingVertices(128);

        _pixel = new Texture2D(_gd, 1, 1);
        _pixel.SetData([Color.White]);

        _pendingTransition = null;
        UpdateUI();
    }

    public override void OnExit()
    {
        _effect?.Dispose();
        _sphereVb?.Dispose();
        _sphereIb?.Dispose();
        _pixel?.Dispose();
    }

    public override void OnResize(int width, int height)
    {
        _camera?.SetProjection(MathHelper.ToRadians(70f), AspectRatio, 0.1f, 50_000f);
        UpdateUI();
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public override StateTransition? Update(GameTime gameTime)
    {
        var mouse = Mouse.GetState();
        var keys  = Keyboard.GetState();
        double dt = gameTime.ElapsedGameTime.TotalSeconds;

        _gameTimeSeconds += dt * TimeCompression;

        // Camera input (mouse look only when right button held)
        _camera.Update(dt, mouse, keys);
        _camera.SetProjection(MathHelper.ToRadians(70f), AspectRatio, 0.1f, 50_000f);

        // Rebuild body positions
        _bodyPositions.Clear();
        foreach (var planet in _system.Planets)
            planet.CollectPositions(_gameTimeSeconds, DVec3.Zero, _bodyPositions);

        HandleKeyboard(keys, mouse);

        _prevMouse = mouse;
        _prevKeys  = keys;

        var t = _pendingTransition;
        _pendingTransition = null;
        return t;
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw(GameTime gameTime, GraphicsDevice gd, SpriteBatch sb)
    {
        gd.Clear(ColBackground);

        // ── 3D scene ──────────────────────────────────────────────────────────

        // Depth buffer on, backface culling on
        gd.DepthStencilState = DepthStencilState.Default;
        gd.RasterizerState   = RasterizerState.CullCounterClockwise;
        gd.BlendState        = BlendState.Opaque;

        // Set matrices shared by everything
        _effect.View       = _camera.ViewMatrix;
        _effect.Projection = _camera.ProjectionMatrix;

        // Star lighting direction — from star (at origin) toward camera
        // This gives correct shading on planet hemispheres
        Vector3 starRenderPos = _camera.ToRenderSpace(DVec3.Zero);
        Vector3 lightDir      = starRenderPos == Vector3.Zero
            ? -Vector3.UnitZ
            : Vector3.Normalize(-starRenderPos); // light FROM star TOWARD scene

        _effect.DirectionalLight0.Direction    = lightDir;
        _effect.DirectionalLight0.DiffuseColor = _star.LightColor.ToVector3();
        _effect.AmbientLightColor              = _star.GlowColor.ToVector3() * _star.AmbientIntensity;

        // Draw orbit rings (behind planets)
        gd.BlendState = BlendState.AlphaBlend;
        DrawOrbitRings();
        gd.BlendState = BlendState.Opaque;

        // Draw star
        DrawStar();

        // Draw planets and moons
        foreach (var (body, pos) in _bodyPositions)
            DrawBody(body, pos);

        // ── 2D overlay ────────────────────────────────────────────────────────
        gd.DepthStencilState = DepthStencilState.None;

        sb.Begin(blendState: BlendState.AlphaBlend);
        DrawHUD(sb);
        DrawBackButton(sb, Mouse.GetState());
        DrawTimeButton(sb, Mouse.GetState());
        sb.End();
    }

    // ── 3D drawing ────────────────────────────────────────────────────────────

    private void DrawStar()
    {
        Vector3 renderPos = _camera.ToRenderSpace(DVec3.Zero);

        // Star is self-luminous — disable lighting, use emissive colour
        _effect.LightingEnabled    = false;
        _effect.VertexColorEnabled = false;
        _effect.DiffuseColor       = _star.GlowColor.ToVector3();

        // Outer glow — slightly transparent larger sphere
        DrawSphere(renderPos, StarVisualRadius * 1.6f, _star.GlowColor * 0.35f, false);
        DrawSphere(renderPos, StarVisualRadius * 1.2f, _star.GlowColor * 0.6f,  false);

        // Star body
        DrawSphere(renderPos, StarVisualRadius, _star.GlowColor, false);

        // Bright white core
        DrawSphere(renderPos, StarVisualRadius * 0.35f, Color.White, false);

        _effect.LightingEnabled = true;
    }

    private void DrawBody(OrbitalBody body, DVec3 universePos)
    {
        Vector3 renderPos = _camera.ToRenderSpace(universePos);

        // Cull bodies far offscreen (rough distance check in render units)
        float dist = renderPos.Length();
        if (dist > 30_000f) return;

        float  radius = VisualRadius(body);
        Color  color  = BodyColor(body);

        DrawSphere(renderPos, radius, color, lit: true);

        // Atmosphere glow — slightly larger, semi-transparent
        if (body.AtmosphereType != AtmosphereType.None)
        {
            _effect.LightingEnabled = false;
            DrawSphere(renderPos, radius * 1.15f, body.AtmosphereColor * 0.25f, lit: false);
            _effect.LightingEnabled = true;
        }
    }

    private void DrawSphere(Vector3 renderPos, float radius, Color color, bool lit)
    {
        _effect.LightingEnabled = lit;
        _effect.DiffuseColor    = color.ToVector3();
        _effect.Alpha           = color.A / 255f;

        _effect.World = Matrix.CreateScale(radius)
                      * Matrix.CreateTranslation(renderPos);

        _gd.SetVertexBuffer(_sphereVb);
        _gd.Indices = _sphereIb;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawIndexedPrimitives(
                PrimitiveType.TriangleList,
                baseVertex: 0,
                startIndex: 0,
                primitiveCount: _sphereTriCount);
        }
    }

    private void DrawOrbitRings()
    {
        // Disable lighting for line drawing
        _effect.LightingEnabled    = false;
        _effect.VertexColorEnabled = true;
        _effect.World              = Matrix.Identity;

        foreach (var planet in _system.Planets)
        {
            float ringRadius = (float)(planet.OrbitalRadius * Camera3D.RenderScale);

            // Skip rings too small to see (inside star or sub-pixel)
            if (ringRadius < StarVisualRadius * 1.5f) continue;
            if (ringRadius > 25_000f) continue; // too far

            // Colour ring by distance from camera for depth feel
            Color col = ColOrbitRing;

            DrawRing(ringRadius, col);

            // Moon orbit rings — centred on current planet position
            if (planet.Children.Count > 0)
            {
                DVec3 planetUniverse = planet.GetPosition(_gameTimeSeconds, DVec3.Zero);
                Vector3 planetRender = _camera.ToRenderSpace(planetUniverse);

                foreach (var moon in planet.Children)
                {
                    float moonRingR = (float)(moon.OrbitalRadius * Camera3D.RenderScale);
                    if (moonRingR < 0.01f) continue;

                    // Translate ring to planet's render position
                    _effect.World = Matrix.CreateScale(moonRingR)
                                  * Matrix.CreateTranslation(planetRender);

                    DrawRingRaw(new Color(20, 28, 44, 140));
                }
            }
        }

        _effect.VertexColorEnabled = false;
        _effect.LightingEnabled    = true;
    }

    private void DrawRing(float radius, Color color)
    {
        _effect.World = Matrix.CreateScale(radius);
        DrawRingRaw(color);
    }

    private void DrawRingRaw(Color color)
    {
        // Set colour on all vertices
        for (int i = 0; i < _ringVerts.Length; i++)
            _ringVerts[i].Color = color;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawUserPrimitives(
                PrimitiveType.LineStrip,
                _ringVerts, 0,
                _ringVerts.Length - 1); // n-1 lines from n+1 verts (closed loop)
        }
    }

    // ── 2D HUD ────────────────────────────────────────────────────────────────

    private void DrawHUD(SpriteBatch sb)
    {
        // Speed indicator
        double speedMs = _camera.MoveSpeedMs;
        string speedStr = Units.FormatSpeed(speedMs);
        DrawText(sb, $"Speed: {speedStr}", new Vector2(16, _gd.Viewport.Height - 80), ColHUD);

        // Game time
        string timeStr = Units.FormatTime(_gameTimeSeconds);
        DrawText(sb, $"T+{timeStr}", new Vector2(16, _gd.Viewport.Height - 58), ColHUDDim, 0.8f);

        // Controls hint
        DrawText(sb, "Right drag: look   WASD/QE: move   Shift: fast   Ctrl: slow",
            new Vector2(16, _gd.Viewport.Height - 30), ColHUDDim, 0.72f);
    }

    private void DrawBackButton(SpriteBatch sb, MouseState mouse)
    {
        bool hov = _backButtonRect.Contains(mouse.X, mouse.Y);
        DrawRect(sb, _backButtonRect, hov ? new Color(35, 55, 85) : ColPanel);
        DrawRectBorder(sb, _backButtonRect, ColBorder);
        DrawText(sb, "< SYSTEM MAP",
            new Vector2(_backButtonRect.X + 10, _backButtonRect.Y + 8),
            hov ? Color.White : ColHUD, 0.85f);
    }

    private void DrawTimeButton(SpriteBatch sb, MouseState mouse)
    {
        bool hov = _timeButtonRect.Contains(mouse.X, mouse.Y);
        DrawRect(sb, _timeButtonRect, hov ? new Color(35, 55, 85) : ColPanel);
        DrawRectBorder(sb, _timeButtonRect, ColBorder);
        DrawText(sb, $"TIME: {TimeLabels[_timeCompIndex]}",
            new Vector2(_timeButtonRect.X + 10, _timeButtonRect.Y + 8),
            hov ? Color.White : ColHUD, 0.85f);
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    private void HandleKeyboard(KeyboardState keys, MouseState mouse)
    {
        bool escPressed = keys.IsKeyDown(Keys.Escape) && !_prevKeys.IsKeyDown(Keys.Escape);
        bool backClicked = mouse.LeftButton == ButtonState.Released
                        && _prevMouse.LeftButton == ButtonState.Pressed
                        && _backButtonRect.Contains(mouse.X, mouse.Y);

        bool timeClicked = mouse.LeftButton == ButtonState.Released
                        && _prevMouse.LeftButton == ButtonState.Pressed
                        && _timeButtonRect.Contains(mouse.X, mouse.Y);

        if (escPressed || backClicked)
            _pendingTransition = StateTransition.To(GameStateId.SystemSpace, _star);
        // Note: returning to SystemMapState (2D map) — GameStateId.SystemSpace
        // is shared. When you have separate IDs for 2D map vs 3D flight,
        // use the correct one here.

        if (timeClicked)
            _timeCompIndex = (_timeCompIndex + 1) % TimeCompressions.Length;

        if (keys.IsKeyDown(Keys.OemCloseBrackets) && !_prevKeys.IsKeyDown(Keys.OemCloseBrackets))
            _timeCompIndex = System.Math.Min(_timeCompIndex + 1, TimeCompressions.Length - 1);

        if (keys.IsKeyDown(Keys.OemOpenBrackets) && !_prevKeys.IsKeyDown(Keys.OemOpenBrackets))
            _timeCompIndex = System.Math.Max(_timeCompIndex - 1, 0);

        // Mouse wheel adjusts movement speed
        int scroll = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        if (scroll != 0)
        {
            double factor = scroll > 0 ? 2.0 : 0.5;
            _camera.MoveSpeedMs = System.Math.Clamp(
                _camera.MoveSpeedMs * factor, 1e6, 1e12);
        }

        // Home — snap back to near star
        if (keys.IsKeyDown(Keys.Home) && !_prevKeys.IsKeyDown(Keys.Home))
        {
            // Reconstruct camera near origin — same as OnEnter
            _camera = new Camera3D(new DVec3(0, 0.5e11, 3e11), AspectRatio);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private float AspectRatio =>
        (float)_gd.Viewport.Width / _gd.Viewport.Height;

    private void UpdateUI()
    {
        _backButtonRect = new Rectangle(16, 16, 160, 36);
        _timeButtonRect = new Rectangle(16, 60, 190, 36);
    }

    private static float VisualRadius(OrbitalBody body) => body.BodyType switch
    {
        BodyType.GasGiant    => 5.0f,
        BodyType.IceGiant    => 3.5f,
        BodyType.EarthLike   => 2.0f,
        BodyType.OceanPlanet => 2.0f,
        BodyType.Desert      => 1.8f,
        BodyType.Volcanic    => 1.8f,
        BodyType.RockyPlanet => 1.5f,
        BodyType.IcePlanet   => 1.5f,
        BodyType.Moon        => 0.7f,
        BodyType.Asteroid    => 0.2f,
        _                    => 1.0f,
    };

    private static Color BodyColor(OrbitalBody body) => body.BodyType switch
    {
        BodyType.EarthLike   => new Color(80,  140, 200),
        BodyType.OceanPlanet => new Color(40,  100, 200),
        BodyType.Desert      => new Color(200, 160,  80),
        BodyType.Volcanic    => new Color(200,  60,  20),
        BodyType.RockyPlanet => new Color(140, 130, 120),
        BodyType.IcePlanet   => new Color(200, 220, 240),
        BodyType.IceGiant    => new Color(100, 180, 220),
        BodyType.GasGiant    => new Color(200, 160, 100),
        BodyType.Moon        => new Color(160, 155, 150),
        _                    => new Color(150, 150, 150),
    };

    // ── 2D primitives (same as other states) ──────────────────────────────────

    private void DrawText(SpriteBatch sb, string text, Vector2 pos, Color color, float scale = 1.0f)
        => sb.DrawString(_font, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

    private void DrawRect(SpriteBatch sb, Rectangle rect, Color color)
        => sb.Draw(_pixel, rect, color);

    private void DrawRectBorder(SpriteBatch sb, Rectangle rect, Color color, int thickness = 1)
    {
        sb.Draw(_pixel, new Rectangle(rect.Left,  rect.Top,              rect.Width, thickness), color);
        sb.Draw(_pixel, new Rectangle(rect.Left,  rect.Bottom-thickness, rect.Width, thickness), color);
        sb.Draw(_pixel, new Rectangle(rect.Left,  rect.Top,  thickness,  rect.Height),           color);
        sb.Draw(_pixel, new Rectangle(rect.Right-thickness, rect.Top, thickness, rect.Height),   color);
    }
}
