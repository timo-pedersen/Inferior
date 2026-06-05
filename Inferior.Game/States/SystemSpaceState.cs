using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Inferior.Core;
using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Galaxy;
using Inferior.Rendering;
using Inferior.UI;
using Inferior.UI.Controls;

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
    private readonly GraphicsDevice  _gd;
    private readonly SpriteFont      _font;
    private readonly SpaceSimulation _simulation;

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

    // Skybox star field — built once on enter, static for the session
    private VertexPositionColor[] _skyboxPoints    = [];  // PointList — one vertex per star
    private VertexPositionColor[] _skyboxGlowVerts = [];  // TriangleList — tiny quads for bright/near stars

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
    private Button?          _backButton;
    private Button?          _timeButton;

    // ── DataBus UI ────────────────────────────────────────────────────────────
    private UIManager?       _ui;
    private InstrumentMeter? _heartbeatMeter;
    private InstrumentMeter? _simTimeMeter;
    private InstrumentMeter? _gravityMeter;
    private SystemConsole?   _console;
    private DirectionBall?   _dirBall;
    private EdgePanelHost?   _rightPanel;
    private EdgePanelHost?   _leftPanel;
    // Stored so we can unsubscribe on OnExit
    private Action<double>?  _heartbeatHandler;
    private Action<double>?  _simTimeHandler;
    private Action<double>?  _gravityHandler;
    private Action<string>?  _systemHandler;
    private Action<double>?  _gravDirXHandler;
    private Action<double>?  _gravDirYHandler;
    private Action<double>?  _gravDirZHandler;
    private double           _gravDirX, _gravDirY, _gravDirZ;

    // ── UI mouse mode ─────────────────────────────────────────────────────────
    // TAB toggles between free-look ship control and mouse-driven UI interaction.
    private bool _uiMouseMode;

    public override bool WantsCursor => _uiMouseMode;

    // ── Visual constants ──────────────────────────────────────────────────────
    // Visual radii in render units (NOT true physical radius — inflated for visibility)
    private const float StarVisualRadius = 8f;
    // Minimum apparent star size in screen pixels — keeps the star visible at any distance
    private const float StarMinPixels         = 1f;
    // Planets: minimum pixel size within boost range, then allowed to shrink and vanish
    private const float PlanetMinPixels       = 1f;
    private const float PlanetMaxBoostDist    = 4500f; // ~30 AU — no boost beyond this

    // Colours
    private static readonly Color ColBackground = new(4, 4, 12);
    private static readonly Color ColOrbitRing  = new(25, 35, 55, 180);
    private static readonly Color ColHUD        = new(180, 200, 220);
    private static readonly Color ColHUDDim     = new(80, 90, 110);
    private static readonly Color ColPanel      = new(8, 12, 25, 200);
    private static readonly Color ColBorder     = new(40, 60, 90);

    // ── Constructor ───────────────────────────────────────────────────────────

    public SystemSpaceState(GraphicsDevice gd, SpriteFont font, SpaceSimulation simulation)
        : base(GameStateId.SystemSpace)
    {
        _gd         = gd;
        _font       = font;
        _simulation = simulation;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnEnter(object? payload)
    {
        if (payload is SystemSpacePayload p)
        {
            _star            = p.Star;
            _system          = StarSystem.Generate(p.Star, GalaxyGenerator.SystemSeed(p.Star));
            _gameTimeSeconds = p.GameTime;

            DVec3 startPos;
            if (p.TargetBody != null)
            {
                // Spawn above and behind the target body — 5× its physical radius away
                DVec3  bodyPos = p.TargetBody.GetPosition(p.GameTime, DVec3.Zero);
                double dist    = System.Math.Max(p.TargetBody.RadiusMeters * 5.0, 1e6);
                startPos = bodyPos + new DVec3(0, dist * 0.4, dist);
            }
            else
            {
                // Spawned from star double-click — start 2 AU from origin
                startPos = new DVec3(0, 0.5e11, 3e11);
            }
            _camera = new Camera3D(startPos, AspectRatio);
        }
        else if (payload is Star star)
        {
            // Fallback: entered directly with just a star (shouldn't happen in normal flow)
            _star    = star;
            _system  = StarSystem.Generate(star, GalaxyGenerator.SystemSeed(star));
            _camera  = new Camera3D(new DVec3(0, 0.5e11, 3e11), AspectRatio);
        }

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

        // Skybox — galaxy stars projected onto a far sphere around the current system
        (_skyboxPoints, _skyboxGlowVerts) = BuildSkybox(_star, GalaxyGenerator.Generate());

        _pixel = new Texture2D(_gd, 1, 1);
        _pixel.SetData([Color.White]);

        _pendingTransition = null;
        UpdateUI();

        // ── DataBus UI setup ──────────────────────────────────────────────────
        var theme = Theme.InferiorDark(_font);
        _ui = new UIManager(_gd, theme);
        _uiMouseMode = false;

        // ── Right panel: INSTR tab (meters) + NAV tab (direction ball) ────────
        const int panelW   = 260;
        const int innerW   = panelW - 16; // 8px padding each side
        const int meterH   = 46;
        const int meterGap = 8;

        _heartbeatMeter = new InstrumentMeter { Label = "HEARTBEAT", MinValue = 0, MaxValue = 100,
            Bounds = new Rectangle(0, 0, innerW, meterH) };
        _simTimeMeter = new InstrumentMeter { Label = "SIM TIME", MinValue = 0, MaxValue = 300,
            Format = "F0", Bounds = new Rectangle(0, meterH + meterGap, innerW, meterH) };
        _gravityMeter = new InstrumentMeter { Label = "GRAVITY", MinValue = 0, MaxValue = 30,
            Format = "F4", Bounds = new Rectangle(0, (meterH + meterGap) * 2, innerW, meterH) };

        var instrPanel = new Panel { DrawBackground = false, DrawBorder = false };
        instrPanel.Add(_heartbeatMeter);
        instrPanel.Add(_simTimeMeter);
        instrPanel.Add(_gravityMeter);

        _dirBall = new DirectionBall
        {
            Header = "HEADING",
            Bounds = new Rectangle(0, 0, innerW, innerW),
        };
        var navPanel = new Panel { DrawBackground = false, DrawBorder = false };
        navPanel.Add(_dirBall);

        _rightPanel = new EdgePanelHost(PanelEdge.Right)
        {
            PanelSize     = panelW,
            HandleSize    = 28,
            HandleLength  = 80,
            CornerMargin  = 8,
            Bounds        = new Rectangle(0, 0, _gd.Viewport.Width, _gd.Viewport.Height),
        };
        _rightPanel.AddTab("INSTR", instrPanel);
        _rightPanel.AddTab("NAV",   navPanel);

        // ── Left panel: LOG tab (system console) ──────────────────────────────
        _console = new SystemConsole
        {
            Header    = "SYSTEM LOG",
            MaxLines  = 9,
            LineBreak = LineBreakMode.Wrap,
            Bounds    = new Rectangle(0, 0, innerW, 220),
        };
        var logPanel = new Panel { DrawBackground = false, DrawBorder = false };
        logPanel.Add(_console);

        _leftPanel = new EdgePanelHost(PanelEdge.Left)
        {
            PanelSize     = panelW,
            HandleSize    = 28,
            HandleLength  = 80,
            CornerMargin  = 8,
            Bounds        = new Rectangle(0, 0, _gd.Viewport.Width, _gd.Viewport.Height),
        };
        _leftPanel.AddTab("LOG", logPanel);

        _ui.Add(_rightPanel);
        _ui.Add(_leftPanel);

        _backButton = new Button("< SYSTEM MAP", new Rectangle(16, 16, 160, 36));
        _timeButton = new Button($"TIME: {TimeLabels[_timeCompIndex]}", new Rectangle(16, 60, 190, 36));

        _backButton.Clicked += _ =>
            _pendingTransition = StateTransition.To(GameStateId.SystemMap,
                new SystemMapPayload(_star, _gameTimeSeconds, CaptureCockpitLayout()));

        _timeButton.Clicked += _ =>
        {
            _timeCompIndex = (_timeCompIndex + 1) % TimeCompressions.Length;
            _timeButton.Text = $"TIME: {TimeLabels[_timeCompIndex]}";
        };

        _ui.Add(_backButton);
        _ui.Add(_timeButton);

        // Restore panel layout if returning from system map
        if (payload is SystemSpacePayload { Layout: { } layout })
            ApplyCockpitLayout(layout);

        // Subscribe — handlers run on main thread during DataBus.Drain()
        _heartbeatHandler = v => _heartbeatMeter.SetValue(v);
        _simTimeHandler   = v => _simTimeMeter.SetValue(v);
        _gravityHandler   = v => _gravityMeter.SetValue(v);
        _systemHandler    = msg => _console.AddMessage(msg);

        _gravDirXHandler = v => _gravDirX = v;
        _gravDirYHandler = v => _gravDirY = v;
        _gravDirZHandler = v => _gravDirZ = v;

        DataBus.Instruments.Subscribe($"Debug.{Topics.Debug.Heartbeat}", _heartbeatHandler);
        DataBus.Instruments.Subscribe($"Debug.{Topics.Debug.SimTime}",   _simTimeHandler);
        DataBus.Instruments.Subscribe($"GravitySensor.{Topics.GravitySensor.Strength}",   _gravityHandler);
        DataBus.Instruments.Subscribe($"GravitySensor.{Topics.GravitySensor.DirectionX}", _gravDirXHandler);
        DataBus.Instruments.Subscribe($"GravitySensor.{Topics.GravitySensor.DirectionY}", _gravDirYHandler);
        DataBus.Instruments.Subscribe($"GravitySensor.{Topics.GravitySensor.DirectionZ}", _gravDirZHandler);
        DataBus.System.Subscribe(Topics.System.All, _systemHandler);

        // First system message — confirms state entry
        DataBus.System.Publish(Topics.System.All, $"Entered {_star.Name}");
    }

    public override void OnExit()
    {
        // Unsubscribe before disposing controls
        if (_heartbeatHandler != null)
            DataBus.Instruments.Unsubscribe($"Debug.{Topics.Debug.Heartbeat}", _heartbeatHandler);
        if (_simTimeHandler != null)
            DataBus.Instruments.Unsubscribe($"Debug.{Topics.Debug.SimTime}", _simTimeHandler);
        if (_gravityHandler != null)
            DataBus.Instruments.Unsubscribe($"GravitySensor.{Topics.GravitySensor.Strength}",   _gravityHandler);
        if (_gravDirXHandler != null)
            DataBus.Instruments.Unsubscribe($"GravitySensor.{Topics.GravitySensor.DirectionX}", _gravDirXHandler);
        if (_gravDirYHandler != null)
            DataBus.Instruments.Unsubscribe($"GravitySensor.{Topics.GravitySensor.DirectionY}", _gravDirYHandler);
        if (_gravDirZHandler != null)
            DataBus.Instruments.Unsubscribe($"GravitySensor.{Topics.GravitySensor.DirectionZ}", _gravDirZHandler);
        if (_systemHandler != null)
            DataBus.System.Unsubscribe(Topics.System.All, _systemHandler);

        _ui?.Dispose();
        _ui = null;

        _effect?.Dispose();
        _sphereVb?.Dispose();
        _sphereIb?.Dispose();
        _pixel?.Dispose();
    }

    public override void OnResize(int width, int height)
    {
        _camera?.SetProjection(MathHelper.ToRadians(60f), AspectRatio, 0.1f, 50_000f);
        UpdateUI();
        var screenBounds = new Rectangle(0, 0, width, height);
        if (_rightPanel != null) _rightPanel.Bounds = screenBounds;
        if (_leftPanel  != null) _leftPanel.Bounds  = screenBounds;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public override StateTransition? Update(GameTime gameTime)
    {
        var mouse = Mouse.GetState();
        var keys  = Keyboard.GetState();
        double dt = gameTime.ElapsedGameTime.TotalSeconds;

        // TAB toggles between UI mouse mode and ship control mode
        bool tabJustPressed = keys.IsKeyDown(Keys.Tab) && !_prevKeys.IsKeyDown(Keys.Tab);
        if (tabJustPressed)
            _uiMouseMode = !_uiMouseMode;

        // Animations always run, regardless of input mode
        _ui?.Animate(dt);

        // In UI mode: UI gets input and camera is locked.
        // In ship mode: camera gets input and UI is non-interactive.
        if (_uiMouseMode)
        {
            _ui?.Update(dt, new InputState(mouse, _prevMouse, keys, _prevKeys));
            // Feed neutral input to camera so it clears any held right-drag state
            _camera.Update(dt, new MouseState(), new KeyboardState());
        }
        else
        {
            _camera.Update(dt, mouse, keys);
        }

        _gameTimeSeconds += dt * TimeCompression;
        _camera.SetProjection(MathHelper.ToRadians(60f), AspectRatio, 0.1f, 50_000f);

        // Update direction ball — orientation + direction to star + gravity
        if (_dirBall != null)
        {
            _dirBall.SetOrientation(_camera.Forward, _camera.Right, _camera.Up);

            // Star — always at universe origin
            var toStar = DVec3.Zero - _camera.UniversePosition;
            if (toStar.Length > 0.001)
            {
                toStar = toStar / toStar.Length;
                _dirBall.SetVector("star",
                    new Vector3((float)toStar.X, (float)toStar.Y, (float)toStar.Z),
                    new Color(255, 220, 80), "★");
            }

            // Gravity — from DataBus (zeros until sim world is populated)
            var gravDir = new Vector3((float)_gravDirX, (float)_gravDirY, (float)_gravDirZ);
            if (gravDir.LengthSquared() > 0.001f)
                _dirBall.SetVector("grav", gravDir, new Color(120, 200, 255), "g");
        }

        // Rebuild body positions
        _bodyPositions.Clear();
        foreach (var planet in _system.Planets)
            planet.CollectPositions(_gameTimeSeconds, DVec3.Zero, _bodyPositions);

        // Feed current world state to simulation so sensors have live gravity data
        _simulation.SetWorldState(_star, _system, _camera.UniversePosition, _gameTimeSeconds);

        if (!_uiMouseMode)
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

        // Pass 0 — skybox (drawn before depth buffer has any data, so geometry always wins)
        gd.DepthStencilState = DepthStencilState.None;
        DrawSkybox();

        // Pass 1 — opaque geometry (depth writes on)
        gd.BlendState        = BlendState.AlphaBlend;
        gd.DepthStencilState = DepthStencilState.Default;
        DrawOrbitRings();

        gd.BlendState = BlendState.Opaque;
        DrawStarBody();
        foreach (var (body, pos) in _bodyPositions)
            DrawPlanetBody(body, pos);

        // Pass 2 — transparent glows (depth test but no depth writes, so they don't occlude)
        gd.BlendState        = BlendState.AlphaBlend;
        gd.DepthStencilState = DepthStencilState.DepthRead;
        DrawStarGlows();
        foreach (var (body, pos) in _bodyPositions)
            DrawAtmosphere(body, pos);

        // ── 2D overlay ────────────────────────────────────────────────────────
        gd.DepthStencilState = DepthStencilState.None;

        sb.Begin(blendState: BlendState.AlphaBlend);
        DrawHUD(sb);
        sb.End();

        // UI library draws on top — owns its own SpriteBatch
        _ui?.Draw();
    }

    // ── 3D drawing ────────────────────────────────────────────────────────────

    // ── Opaque pass ───────────────────────────────────────────────────────────

    private void DrawStarBody()
    {
        Vector3 renderPos = _camera.ToRenderSpace(DVec3.Zero);
        float   radius    = StarApparentRadius(renderPos);
        _effect.LightingEnabled    = false;
        _effect.VertexColorEnabled = false;
        DrawSphere(renderPos, radius,         _star.GlowColor, false);
        DrawSphere(renderPos, radius * 0.35f, Color.White,     false);
        _effect.LightingEnabled = true;
    }

    private void DrawPlanetBody(OrbitalBody body, DVec3 universePos)
    {
        Vector3 renderPos = _camera.ToRenderSpace(universePos);
        if (renderPos.Length() > 30_000f) return;

        DrawSphere(renderPos, PlanetApparentRadius(body, renderPos), BodyColor(body), lit: true);
    }

    // ── Transparent pass (AlphaBlend + DepthRead) ─────────────────────────────

    private void DrawStarGlows()
    {
        Vector3 renderPos = _camera.ToRenderSpace(DVec3.Zero);
        float   radius    = StarApparentRadius(renderPos);
        _effect.LightingEnabled    = false;
        _effect.VertexColorEnabled = false;
        DrawSphere(renderPos, radius * 1.6f, _star.GlowColor * 0.25f, false);
        DrawSphere(renderPos, radius * 1.2f, _star.GlowColor * 0.50f, false);
        _effect.LightingEnabled = true;
    }

    /// <summary>
    /// Minimum render-space radius for a planet within boost range.
    /// Beyond PlanetMaxBoostDist the planet is left to shrink and vanish naturally.
    /// </summary>
    private float PlanetApparentRadius(OrbitalBody body, Vector3 renderPos)
    {
        float dist       = renderPos.Length();
        float baseRadius = VisualRadius(body);
        if (dist > PlanetMaxBoostDist) return baseRadius;

        float projScale      = _gd.Viewport.Height
                             / (2f * MathF.Tan(MathHelper.ToRadians(30f)));
        float minRenderRadius = PlanetMinPixels * dist / projScale;
        return System.Math.Max(baseRadius, minRenderRadius);
    }

    /// <summary>
    /// Minimum render-space radius that keeps the star at least <see cref="StarMinPixels"/>
    /// pixels across at any distance. Grows with distance so the star is always visible;
    /// never shrinks below StarVisualRadius when close.
    /// </summary>
    private float StarApparentRadius(Vector3 renderPos)
    {
        float dist = renderPos.Length();
        if (dist < 0.001f) return StarVisualRadius;

        // projScale converts render-space size at unit distance to screen pixels.
        // For a symmetric frustum: projScale = screenHeight / (2 * tan(halfFov))
        float projScale = _gd.Viewport.Height
                        / (2f * MathF.Tan(MathHelper.ToRadians(30f))); // half of 60°

        float minRenderRadius = StarMinPixels * dist / projScale;
        return System.Math.Max(StarVisualRadius, minRenderRadius);
    }

    private void DrawAtmosphere(OrbitalBody body, DVec3 universePos)
    {
        if (body.AtmosphereType == AtmosphereType.None) return;

        Vector3 renderPos = _camera.ToRenderSpace(universePos);
        if (renderPos.Length() > 30_000f) return;

        _effect.LightingEnabled = false;
        DrawSphere(renderPos, PlanetApparentRadius(body, renderPos) * 1.18f, body.AtmosphereColor * 0.35f, lit: false);
        _effect.LightingEnabled = true;
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

    // ── Skybox ────────────────────────────────────────────────────────────────

    private const float SkyboxRadius    = 20_000f;
    private const float SkyboxGlowSize = 12f;    // RU per unit MapDotSize
    private const float SkyboxGlowCutoff = 0.15f; // brightness threshold for glow quads

    // Desaturated sky tint — barely-coloured near-white so stars don't look like blobs.
    // These intentionally diverge from GlowColor (which is for up-close system views).
    private static Color SkyboxStarColor(SpectralClass sc, float brightness)
    {
        Vector3 tint = sc switch
        {
            SpectralClass.O           => new Vector3(0.88f, 0.92f, 1.00f),
            SpectralClass.B           => new Vector3(0.92f, 0.95f, 1.00f),
            SpectralClass.A           => new Vector3(0.97f, 0.98f, 1.00f),
            SpectralClass.F           => new Vector3(1.00f, 0.99f, 0.95f),
            SpectralClass.G           => new Vector3(1.00f, 0.97f, 0.91f),
            SpectralClass.K           => new Vector3(1.00f, 0.93f, 0.85f),
            SpectralClass.M           => new Vector3(1.00f, 0.89f, 0.80f),
            SpectralClass.WhiteDwarf  => new Vector3(0.94f, 0.96f, 1.00f),
            SpectralClass.NeutronStar => new Vector3(0.88f, 0.94f, 1.00f),
            SpectralClass.BlackHole   => new Vector3(0.08f, 0.08f, 0.10f),
            _                         => Vector3.One,
        };
        return new Color(tint * brightness);
    }

    private static (VertexPositionColor[] points, VertexPositionColor[] glowVerts)
        BuildSkybox(Star currentStar, Star[] galaxy)
    {
        var points = new List<VertexPositionColor>(galaxy.Length);
        var glows  = new List<VertexPositionColor>();

        // Half the galaxy radius in ly — used for distance falloff so stars dim gradually
        // across galaxy-scale distances rather than popping.
        const double falloffScale = GalaxyGenerator.GalaxyRadiusLY * 0.4;

        foreach (var star in galaxy)
        {
            if (star.GalaxyIndex == currentStar.GalaxyIndex) continue;

            DVec3  offset = star.GalacticPos - currentStar.GalacticPos;
            double dist   = offset.Length;
            if (dist < 0.001) continue;

            var dir    = Vector3.Normalize(new Vector3(
                (float)(offset.X / dist), (float)(offset.Y / dist), (float)(offset.Z / dist)));
            var center = dir * SkyboxRadius;

            float brightness = (star.MapDotSize / 3.5f)
                             * (float)System.Math.Exp(-dist / falloffScale);
            brightness = System.Math.Clamp(brightness, 0.03f, 1.0f);

            points.Add(new VertexPositionColor(center, SkyboxStarColor(star.SpectralClass, brightness)));

            if (brightness >= SkyboxGlowCutoff)
            {
                float   size    = star.MapDotSize * SkyboxGlowSize;
                float   alpha   = brightness * 0.30f;
                Color   glowCol = SkyboxStarColor(star.SpectralClass, alpha);

                Vector3 worldUp = MathF.Abs(dir.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
                Vector3 tan     = Vector3.Normalize(Vector3.Cross(dir, worldUp));
                Vector3 bitan   = Vector3.Cross(dir, tan);

                glows.Add(new VertexPositionColor(center + (-tan + bitan) * size, glowCol));
                glows.Add(new VertexPositionColor(center + ( tan + bitan) * size, glowCol));
                glows.Add(new VertexPositionColor(center + (-tan - bitan) * size, glowCol));
                glows.Add(new VertexPositionColor(center + ( tan + bitan) * size, glowCol));
                glows.Add(new VertexPositionColor(center + ( tan - bitan) * size, glowCol));
                glows.Add(new VertexPositionColor(center + (-tan - bitan) * size, glowCol));
            }
        }

        return ([.. points], [.. glows]);
    }

    private void DrawSkybox()
    {
        if (_skyboxPoints.Length == 0 && _skyboxGlowVerts.Length == 0) return;

        _gd.RasterizerState = RasterizerState.CullNone;
        _gd.BlendState      = BlendState.AlphaBlend;

        _effect.World              = Matrix.Identity;
        _effect.LightingEnabled    = false;
        _effect.VertexColorEnabled = true;
        _effect.TextureEnabled     = false;
        _effect.DiffuseColor       = Vector3.One;
        _effect.Alpha              = 1f;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();

            if (_skyboxGlowVerts.Length >= 3)
                _gd.DrawUserPrimitives(PrimitiveType.TriangleList, _skyboxGlowVerts, 0, _skyboxGlowVerts.Length / 3);

            if (_skyboxPoints.Length > 0)
                _gd.DrawUserPrimitives(PrimitiveType.PointList, _skyboxPoints, 0, _skyboxPoints.Length);
        }

        _effect.VertexColorEnabled = false;
        _effect.LightingEnabled    = true;
        _gd.RasterizerState        = RasterizerState.CullCounterClockwise;
        _gd.BlendState             = BlendState.Opaque;
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

        // Controls hint — changes with UI mode
        if (_uiMouseMode)
        {
            DrawText(sb, "UI MODE  —  TAB: return to flight",
                new Vector2(16, _gd.Viewport.Height - 30), new Color(80, 160, 220), 0.72f);
        }
        else
        {
            DrawText(sb, "Right drag: look   WASD/QE: move   Shift: fast   Ctrl: slow   TAB: UI",
                new Vector2(16, _gd.Viewport.Height - 30), ColHUDDim, 0.72f);
        }
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    private void HandleKeyboard(KeyboardState keys, MouseState mouse)
    {
        bool escPressed = keys.IsKeyDown(Keys.Escape) && !_prevKeys.IsKeyDown(Keys.Escape);

        if (escPressed)
            _pendingTransition = StateTransition.To(GameStateId.SystemMap,
                new SystemMapPayload(_star, _gameTimeSeconds, CaptureCockpitLayout()));

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

    private void UpdateUI() { }

    // ── Cockpit layout ────────────────────────────────────────────────────────

    private CockpitLayout CaptureCockpitLayout()
    {
        var (rightTab, rightOpen) = _rightPanel?.CaptureState() ?? (-1, false);
        var (leftTab,  leftOpen)  = _leftPanel?.CaptureState()  ?? (-1, false);
        return new CockpitLayout(rightTab, rightOpen, leftTab, leftOpen);
    }

    private void ApplyCockpitLayout(CockpitLayout layout)
    {
        _rightPanel?.ApplyState(layout.RightActiveTab, layout.RightOpen);
        _leftPanel?.ApplyState(layout.LeftActiveTab,  layout.LeftOpen);
    }

    // Visual inflation factor: 100 = planets appear 100× their true physical radius.
    // Reduce toward 1 for true-scale navigation.
    private const float PlanetVisualScale = 10f;

    private static float VisualRadius(OrbitalBody body) =>
        (float)(body.RadiusMeters * Camera3D.RenderScale * PlanetVisualScale);

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
