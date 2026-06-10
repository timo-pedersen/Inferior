using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Inferior.Core;
using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Galaxy;
using Inferior.Gameplay;
using Inferior.Gameplay.Ship;
using Inferior.Gameplay.Components;
using Inferior.Rendering;
using Inferior.UI;
using Inferior.UI.Controls;
using Inferior.Gameplay.Components.Power;

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
    private Matrix      _eclipticRotation = Matrix.Identity;

    private VertexBuffer _sphereVb = null!;
    private IndexBuffer  _sphereIb = null!;
    private int          _sphereTriCount;

    // Reusable ring vertex array — rebuilt each frame per orbit
    private VertexPositionColor[] _ringVerts = null!;

    // Skybox star field — built once on enter, static for the session
    private VertexPositionColor[] _skyboxPoints    = [];  // PointList — one vertex per star
    private VertexPositionColor[] _skyboxGlowVerts = [];  // TriangleList — tiny quads for bright/near stars

    // ── 2D overlay (SpriteBatch for HUD) ──────────────────────────────────────
    private Texture2D _pixel       = null!;
    private Texture2D _starGlowTex = null!;  // soft radial gradient — reused for all glow layers

    // ── Time ──────────────────────────────────────────────────────────────────
    private double _gameTimeSeconds;

    // ── Cached body positions ─────────────────────────────────────────────────
    private readonly List<(OrbitalBody body, DVec3 pos)> _bodyPositions = [];

    // ── UI ────────────────────────────────────────────────────────────────────
    private StateTransition? _pendingTransition;
    private MouseState       _prevMouse;
    private KeyboardState    _prevKeys;

    // ── DataBus UI ────────────────────────────────────────────────────────────
    private UIManager?       _ui;
    private InstrumentMeter? _reactorPowerOutputMeter;
    private InstrumentMeter? _reactorDrawnMeter;
    private InstrumentMeter? _busConsumptionMeter;
    private InstrumentMeter? _connectorFlowMeter;
    private AnalogueNeedle?  _connectorNeedle;
    private InstrumentMeter? _shieldCapacitorMeter;
    private ToggleButton?    _shieldToggleButton;
    private Action<double>?  _shieldCapacitorHandler;
    private SystemConsole?   _console;
    private DirectionBall?   _dirBall;
    private DirectionBall?   _cockpitDirBall;
    private EdgePanelHost?   _rightPanel;
    private EdgePanelHost?   _leftPanel;
    private CockpitRail?     _cockpitRail;
    // Stored so we can unsubscribe on OnExit
    private Action<string>?  _systemHandler;
    private Action<double>?  _gravDirXHandler;
    private Action<double>?  _gravDirYHandler;
    private Action<double>?  _gravDirZHandler;
    private double           _gravDirX, _gravDirY, _gravDirZ;

    // ── Camera modes ──────────────────────────────────────────────────────────
    // TAB  — toggles between ship-control and mouse-driven UI interaction.
    // F11  — toggles between ship camera (cockpit) and free debug camera.
    private bool _uiMouseMode;
    private bool _debugCameraMode;

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
            ComputeEclipticRotation();

            DVec3 startPos;
            if (p.TargetBody != null)
            {
                // Spawn above and behind the target body — spawn offset is in ecliptic space
                // then rotated into galaxy space so the ship appears near the body's tilted position
                DVec3  bodyEcliptic = p.TargetBody.GetPosition(p.GameTime, DVec3.Zero);
                double dist         = System.Math.Max(p.TargetBody.RadiusMeters * 5.0, 1e6);
                startPos = EclipticToGalaxy(bodyEcliptic + new DVec3(0, dist * 0.4, dist));
            }
            else
            {
                // Spawned from star double-click — start 2 AU from origin
                startPos = new DVec3(0, 0.5e11, 3e11);
            }
            _camera = new Camera3D(startPos, AspectRatio);
            SpawnShip(startPos);
        }
        else if (payload is Star star)
        {
            // Fallback: entered directly with just a star (shouldn't happen in normal flow)
            _star    = star;
            _system  = StarSystem.Generate(star, GalaxyGenerator.SystemSeed(star));
            ComputeEclipticRotation();
            var fallbackPos = new DVec3(0, 0.5e11, 3e11);
            _camera  = new Camera3D(fallbackPos, AspectRatio);
            SpawnShip(fallbackPos);
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

        _starGlowTex = CreateStarGlowTexture(_gd, 128);

        _pendingTransition = null;
        UpdateUI();

        // ── DataBus UI setup ──────────────────────────────────────────────────
        var theme = Theme.InferiorDark(_font);
        _ui = new UIManager(_gd, theme);
        _uiMouseMode   = false;
        _debugCameraMode = true;

        // ── Right panel: INSTR tab (meters) + NAV tab (direction ball) ────────
        const int panelW   = 260;
        const int innerW   = panelW - 16; // 8px padding each side
        const int meterH   = 46;
        const int meterGap = 8;

        _reactorPowerOutputMeter = new InstrumentMeter
        { Label = "REACTOR OUT", MinValue = 0, MaxValue = 120,
            Topic = "Reactor.Output",
            ScaleFactor = 1e-6,   // sensor publishes watts; meter displays MW
            Format = "F1",
            Bounds = new Rectangle(0, 0, innerW, meterH)
        };
        _reactorDrawnMeter = new InstrumentMeter
        { Label = "REACTOR DRAW", MinValue = 0, MaxValue = 120,
            Topic = "Reactor.Drawn",
            ScaleFactor = 1e-6,   // sensor publishes watts; meter displays MW
            Format = "F1",
            Bounds = new Rectangle(0, meterH + meterGap, innerW, meterH)
        };
        _busConsumptionMeter = new InstrumentMeter
        { Label = "BUS DRAW", MinValue = 0, MaxValue = 120,
            Topic = "MainBus.Consumption",
            ScaleFactor = 1e-6,   // sensor publishes watts; meter displays MW
            Format = "F1",
            Bounds = new Rectangle(0, (meterH + meterGap) * 2, innerW, meterH)
        };
        _connectorFlowMeter = new InstrumentMeter
        { Label = "SHIELD CONN", MinValue = 0, MaxValue = 1.0,
            Topic = "ShieldConnector.Flow",
            ScaleFactor = 1e-6,   // sensor publishes watts; meter displays MW (max 0.6 MW)
            Format = "F3",
            Bounds = new Rectangle(0, (meterH + meterGap) * 3, innerW, meterH)
        };
        const int needleH = 130;
        _connectorNeedle = new AnalogueNeedle
        { Label = "SHIELD CONN", MinValue = 0, MaxValue = 1.0,
            Topic = "ShieldConnector.Flow",
            ScaleFactor = 1e-6,   // sensor publishes watts; needle displays MW
            Format = "F3",
            AnimationSpeed = 5.0,
            Bounds = new Rectangle(0, (meterH + meterGap) * 4, innerW, needleH)
        };
        _shieldCapacitorMeter = new InstrumentMeter
        { Label = "SHIELD CAP", MinValue = 0, MaxValue = 100,
            Topic = $"Shield.{Topics.Shield.Capacitor}",
            ScaleFactor = 100.0,  // sensor publishes 0–1 fill; meter displays 0–100 %
            Format = "F0",
            Bounds = new Rectangle(0, (meterH + meterGap) * 4 + needleH + meterGap, innerW, meterH)
        };

        var instrPanel = new Panel { DrawBackground = false, DrawBorder = false };
        instrPanel.Add(_reactorPowerOutputMeter);
        instrPanel.Add(_reactorDrawnMeter);
        instrPanel.Add(_busConsumptionMeter);
        instrPanel.Add(_connectorFlowMeter);
        instrPanel.Add(_connectorNeedle);
        instrPanel.Add(_shieldCapacitorMeter);

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

        // Side panels stop at the CockpitRail wing top to avoid overlap
        int wingH       = 160; // matches CockpitRail.WingHeight
        int sidePanelH  = _gd.Viewport.Height - wingH;

        // ── Left panel: empty for now ─────────────────────────────────────────
        _leftPanel = new EdgePanelHost(PanelEdge.Left)
        {
            PanelSize     = panelW,
            HandleSize    = 28,
            HandleLength  = 80,
            CornerMargin  = 8,
            Bounds        = new Rectangle(0, 0, _gd.Viewport.Width, sidePanelH),
        };
        _rightPanel.Bounds = new Rectangle(0, 0, _gd.Viewport.Width, sidePanelH);

        _ui.Add(_rightPanel);
        _ui.Add(_leftPanel);

        // ── CockpitRail: 4 tabs (RADAR, DIR BALL, ???, LOG) ──────────────────
        _console = new SystemConsole
        {
            Header    = "SYSTEM LOG",
            MaxLines  = 6,
            LineBreak = LineBreakMode.Wrap,
            Bounds    = new Rectangle(0, 0, 500, 200),
        };

        _cockpitDirBall = new DirectionBall
        {
            Header = "HEADING",
            Bounds = new Rectangle(0, 0, 300, 300),
        };

        var radarPlaceholder = new Panel { DrawBackground = false, DrawBorder = false };
        var miscPlaceholder  = new Panel { DrawBackground = false, DrawBorder = false };

        _shieldToggleButton = new ToggleButton("SHIELD", new Rectangle(4, 4, 120, 28));
        _shieldToggleButton.SetState(false, false);
        _shieldToggleButton.Toggled += (_, on) =>
        {
            if (_shield == null) return;
            _shield.PowerOn = on;
        };

        _shieldCapacitorHandler = fill =>
        {
            if (_shieldToggleButton == null) return;
            _shieldToggleButton.IsConfirmed = fill >= 1.0 ? true
                                            : fill <= 0.0 ? false
                                            : null;
        };
        DataBus.Instruments.Subscribe($"Shield.{Topics.Shield.Capacitor}", _shieldCapacitorHandler);

        _cockpitRail = new CockpitRail
        {
            Bounds = new Rectangle(0, 0, _gd.Viewport.Width, _gd.Viewport.Height),
        };
        _cockpitRail.AddCenterTab("RADAR",    radarPlaceholder);
        _cockpitRail.AddCenterTab("DIR BALL", _cockpitDirBall);
        _cockpitRail.AddCenterTab("???",      miscPlaceholder);
        _cockpitRail.AddCenterTab("LOG",      _console);
        _cockpitRail.RightWing.Add(_shieldToggleButton);

        _ui.Add(_cockpitRail);

        // Restore panel layout if returning from system map
        if (payload is SystemSpacePayload { Layout: { } layout })
            ApplyCockpitLayout(layout);

        // Start in ship-control mode — panels retracted, handles and buttons hidden
        ApplyUiMode(false);

        // Meters subscribe themselves via Topic — only non-meter handlers need wiring here
        _systemHandler = msg => _console.AddMessage(msg);

        _gravDirXHandler = v => _gravDirX = v;
        _gravDirYHandler = v => _gravDirY = v;
        _gravDirZHandler = v => _gravDirZ = v;

        DataBus.Instruments.Subscribe($"GravitySensor.{Topics.GravitySensor.DirectionX}", _gravDirXHandler);
        DataBus.Instruments.Subscribe($"GravitySensor.{Topics.GravitySensor.DirectionY}", _gravDirYHandler);
        DataBus.Instruments.Subscribe($"GravitySensor.{Topics.GravitySensor.DirectionZ}", _gravDirZHandler);
        DataBus.System.Subscribe(Topics.System.All, _systemHandler);

        // First system message — confirms state entry
        DataBus.System.Publish(Topics.System.All, $"Entered {_star.Name}");
    }

    public override void OnExit()
    {
        // Meters unsubscribe themselves when Topic is cleared
        if (_reactorPowerOutputMeter != null) _reactorPowerOutputMeter.Topic = "";
        if (_reactorDrawnMeter       != null) _reactorDrawnMeter.Topic       = "";
        if (_busConsumptionMeter     != null) _busConsumptionMeter.Topic     = "";
        if (_connectorFlowMeter      != null) _connectorFlowMeter.Topic      = "";
        if (_connectorNeedle         != null) _connectorNeedle.Topic         = "";
        if (_shieldCapacitorMeter    != null) _shieldCapacitorMeter.Topic    = "";

        if (_gravDirXHandler != null)
            DataBus.Instruments.Unsubscribe($"GravitySensor.{Topics.GravitySensor.DirectionX}", _gravDirXHandler);
        if (_gravDirYHandler != null)
            DataBus.Instruments.Unsubscribe($"GravitySensor.{Topics.GravitySensor.DirectionY}", _gravDirYHandler);
        if (_gravDirZHandler != null)
            DataBus.Instruments.Unsubscribe($"GravitySensor.{Topics.GravitySensor.DirectionZ}", _gravDirZHandler);
        if (_systemHandler != null)
            DataBus.System.Unsubscribe(Topics.System.All, _systemHandler);
        if (_shieldCapacitorHandler != null)
            DataBus.Instruments.Unsubscribe($"Shield.{Topics.Shield.Capacitor}", _shieldCapacitorHandler);

        _ui?.Dispose();
        _ui = null;

        _effect?.Dispose();
        _sphereVb?.Dispose();
        _sphereIb?.Dispose();
        _pixel?.Dispose();
        _starGlowTex?.Dispose();
    }

    public override void OnResize(int width, int height)
    {
        _camera?.SetProjection(MathHelper.ToRadians(60f), AspectRatio, 0.001f, 50_000f);
        UpdateUI();
        int wingH      = _cockpitRail?.WingHeight ?? 160;
        var sideBounds = new Rectangle(0, 0, width, height - wingH);
        if (_rightPanel  != null) _rightPanel.Bounds  = sideBounds;
        if (_leftPanel   != null) _leftPanel.Bounds   = sideBounds;
        if (_cockpitRail != null) _cockpitRail.Bounds = new Rectangle(0, 0, width, height);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public override StateTransition? Update(GameTime gameTime)
    {
        var mouse = Mouse.GetState();
        var keys  = Keyboard.GetState();
        double dt = gameTime.ElapsedGameTime.TotalSeconds;

        // TAB — UI mode toggle; F11 — ship/debug camera toggle
        bool tabJustPressed = keys.IsKeyDown(Keys.Tab) && !_prevKeys.IsKeyDown(Keys.Tab);
        bool f11JustPressed = keys.IsKeyDown(Keys.F11) && !_prevKeys.IsKeyDown(Keys.F11);

        if (tabJustPressed)
        {
            _uiMouseMode = !_uiMouseMode;
            ApplyUiMode(_uiMouseMode);
        }
        if (f11JustPressed)
        {
            if (_debugCameraMode)
            {
                // Leaving debug cam — teleport ship to where the camera is now
                _simulation.TeleportShip(_camera.UniversePosition, _camera.Orientation);
            }
            _debugCameraMode = !_debugCameraMode;
        }

        // Animations always run, regardless of input mode
        _ui?.Animate(dt);

        if (_uiMouseMode)
        {
            // UI mode — UI gets mouse/keyboard; camera and ship are frozen
            _ui?.Update(dt, new InputState(mouse, _prevMouse, keys, _prevKeys));
            if (_debugCameraMode)
                _camera.Update(dt, new MouseState(), new KeyboardState()); // clear any held drag
            _simulation.SetInput(PlayerInput.Zero);
        }
        else if (_debugCameraMode)
        {
            // Debug camera — free-look, ship receives no input and stays put
            _camera.Update(dt, mouse, keys);
            _simulation.SetInput(PlayerInput.Zero);
        }
        else
        {
            // Ship mode — input goes to the simulation; camera follows cockpit
            _simulation.SetInput(BuildShipInput(mouse, keys));
            var snap = _simulation.ShipState;
            if (snap != null)
                _camera.SetPose(snap.CockpitWorldPosition, snap.Orientation);
        }

        _gameTimeSeconds += dt;
        _camera.SetProjection(MathHelper.ToRadians(60f), AspectRatio, 0.001f, 50_000f);

        // Update direction balls — both the right-panel ball and the cockpit center ball
        UpdateDirectionBall(_dirBall);
        UpdateDirectionBall(_cockpitDirBall);

        // Rebuild body positions — collect in ecliptic space then rotate to galaxy space
        _bodyPositions.Clear();
        foreach (var planet in _system.Planets)
            planet.CollectPositions(_gameTimeSeconds, DVec3.Zero, _bodyPositions);
        for (int i = 0; i < _bodyPositions.Count; i++)
        {
            var (body, pos) = _bodyPositions[i];
            _bodyPositions[i] = (body, EclipticToGalaxy(pos));
        }

        // Feed world state to simulation — use ship snapshot position in ship mode,
        // camera position in debug mode (sensors track whoever is "there")
        DVec3 refPos = _debugCameraMode
            ? _camera.UniversePosition
            : _simulation.ShipState?.Position ?? _camera.UniversePosition;
        _simulation.SetWorldState(_star, _system, refPos, _gameTimeSeconds);

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
        // Boost diffuse well above 1.0 so the lit side of planets is bright.
        // BasicEffect clamps output to [0,1] per channel, so this drives the lit
        // hemisphere toward white without blowing out the dark side.
        _effect.DirectionalLight0.DiffuseColor = _star.LightColor.ToVector3() * 3.0f;
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

        // Pass 2 — transparent (depth test, no depth writes)
        gd.BlendState        = BlendState.AlphaBlend;
        gd.DepthStencilState = DepthStencilState.DepthRead;
        foreach (var (body, pos) in _bodyPositions)
            DrawAtmosphere(body, pos);

        // ── 2D overlay ────────────────────────────────────────────────────────
        gd.DepthStencilState = DepthStencilState.None;

        // Star glow — additive so layers accumulate as emitted light.
        // Drawn after all 3D geometry so it blooms over planets that are in front.
        sb.Begin(blendState: BlendState.Additive);
        DrawStarGlow2D(sb);
        sb.End();

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
        // Star surface colour — white base tinted toward LightColor by a per-class factor.
        // Hot stars (O/B) stay near-white; cool stars (K/M) show clear yellow/orange/red.
        Color bodyColor = Color.Lerp(Color.White, _star.LightColor, _star.BodyTintStrength);
        DrawSphere(renderPos, radius, bodyColor, false);
        _effect.LightingEnabled = true;
    }

    private void DrawPlanetBody(OrbitalBody body, DVec3 universePos)
    {
        Vector3 renderPos = _camera.ToRenderSpace(universePos);
        if (renderPos.Length() > 30_000f) return;

        DrawSphere(renderPos, PlanetApparentRadius(body, renderPos), BodyColor(body), lit: true);
    }

    // ── 2D star glow (screen-space, additive) ────────────────────────────────

    private void DrawStarGlow2D(SpriteBatch sb)
    {
        Vector3 renderPos = _camera.ToRenderSpace(DVec3.Zero);

        // Project star to clip space — bail if behind the camera
        var clip = Vector4.Transform(new Vector4(renderPos, 1f),
                                     _camera.ViewMatrix * _camera.ProjectionMatrix);
        if (clip.W <= 0f) return;

        var   vp    = _gd.Viewport;
        float ndcX  = clip.X / clip.W;
        float ndcY  = clip.Y / clip.W;
        var   screen = new Vector2(
            (ndcX + 1f) * 0.5f * vp.Width,
            (1f - ndcY) * 0.5f * vp.Height);

        // Convert the 3D body radius to screen pixels
        float dist      = MathF.Max(renderPos.Length(), 0.001f);
        float projScale = vp.Height / (2f * MathF.Tan(MathHelper.ToRadians(30f)));
        float bodyR     = MathF.Min(StarApparentRadius(renderPos) * projScale / dist, 220f);

        // Layers drawn largest→smallest so the bright core paints over the outer halo.
        // Additive blend: layers accumulate as emitted light, center saturates to white.
        DrawGlowLayer(sb, screen, bodyR * 14f,  _star.GlowColor * 0.07f); // faint outer corona
        DrawGlowLayer(sb, screen, bodyR * 6f,   _star.GlowColor * 0.28f); // mid halo
        DrawGlowLayer(sb, screen, bodyR * 2.5f, _star.GlowColor * 0.65f); // inner corona
        DrawGlowLayer(sb, screen, bodyR * 1.1f, Color.White     * 0.90f); // white-hot surface
    }

    private void DrawGlowLayer(SpriteBatch sb, Vector2 center, float radius, Color color)
    {
        if (radius < 1f) return;
        int r = (int)radius;
        sb.Draw(_starGlowTex,
            new Rectangle((int)(center.X - radius), (int)(center.Y - radius), r * 2, r * 2),
            color);
    }

    // Gaussian radial gradient baked into a texture — reused for every glow layer.
    private static Texture2D CreateStarGlowTexture(GraphicsDevice gd, int size)
    {
        var   tex  = new Texture2D(gd, size, size);
        var   data = new Color[size * size];
        float r    = size * 0.5f;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float t     = MathF.Min(MathF.Sqrt((x - r) * (x - r) + (y - r) * (y - r)) / r, 1f);
            float alpha = MathF.Exp(-t * t * 3f); // gaussian: 1.0 at center → ~0.05 at edge
            data[y * size + x] = Color.White * alpha;
        }

        tex.SetData(data);
        return tex;
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

            // Apply ecliptic tilt so rings lie in the system's orbital plane, not the galaxy plane
            _effect.World = Matrix.CreateScale(ringRadius) * _eclipticRotation;
            DrawRingRaw(col);

            // Moon orbit rings — centred on the planet's tilted position
            if (planet.Children.Count > 0)
            {
                DVec3 planetUniverse = EclipticToGalaxy(planet.GetPosition(_gameTimeSeconds, DVec3.Zero));
                Vector3 planetRender = _camera.ToRenderSpace(planetUniverse);

                foreach (var moon in planet.Children)
                {
                    float moonRingR = (float)(moon.OrbitalRadius * Camera3D.RenderScale);
                    if (moonRingR < 0.01f) continue;

                    // Scale → tilt → translate to planet position
                    _effect.World = Matrix.CreateScale(moonRingR)
                                  * _eclipticRotation
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
        // Speed indicator — ship velocity in ship mode, debug cam setting otherwise
        double speedMs = _debugCameraMode
            ? _camera.MoveSpeedMs
            : _simulation.ShipState?.Velocity.Length ?? 0.0;
        DrawText(sb, $"Speed: {Units.FormatSpeed(speedMs)}", new Vector2(16, _gd.Viewport.Height - 80), ColHUD);

        // Game time
        DrawText(sb, $"T+{Units.FormatTime(_gameTimeSeconds)}", new Vector2(16, _gd.Viewport.Height - 58), ColHUDDim, 0.8f);

        // Controls hint — changes with mode
        if (_uiMouseMode)
        {
            DrawText(sb, "UI MODE  —  TAB: return to flight",
                new Vector2(16, _gd.Viewport.Height - 30), new Color(80, 160, 220), 0.72f);
        }
        else if (_debugCameraMode)
        {
            DrawText(sb, "DEBUG CAM  —  Right drag: look   WASD/QE: move   Shift: fast   Ctrl: slow   F11: ship cam   TAB: UI",
                new Vector2(16, _gd.Viewport.Height - 30), new Color(220, 160, 80), 0.72f);
        }
        else
        {
            DrawText(sb, "Right drag: look   WASD: fwd/strafe   QE: roll   RF: up/down   M: system map   N: galaxy map   F11: debug   TAB: UI",
                new Vector2(16, _gd.Viewport.Height - 30), ColHUDDim, 0.72f);
        }
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    private void HandleKeyboard(KeyboardState keys, MouseState mouse)
    {
        bool mPressed    = keys.IsKeyDown(Keys.M)    && !_prevKeys.IsKeyDown(Keys.M);
        bool nPressed    = keys.IsKeyDown(Keys.N)    && !_prevKeys.IsKeyDown(Keys.N);
        bool homePressed = keys.IsKeyDown(Keys.Home) && !_prevKeys.IsKeyDown(Keys.Home);

        if (mPressed)
            _pendingTransition = StateTransition.To(GameStateId.SystemMap,
                new SystemMapPayload(_star, _gameTimeSeconds, CaptureCockpitLayout()));

        if (nPressed)
            _pendingTransition = StateTransition.To(GameStateId.GalaxyMap,
                new GalaxyMapPayload(_star, _gameTimeSeconds));

        int scroll = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;

        if (_debugCameraMode)
        {
            // Scroll adjusts debug camera fly speed
            if (scroll != 0)
            {
                double factor = scroll > 0 ? 2.0 : 0.5;
                _camera.MoveSpeedMs = System.Math.Clamp(_camera.MoveSpeedMs * factor, 1e6, 1e12);
            }
            // Home — reset debug camera to near-star
            if (homePressed)
                _camera = new Camera3D(new DVec3(0, 0.5e11, 3e11), AspectRatio);
        }
        else
        {
            // Home — snap ship back to near-star (useful during dev)
            if (homePressed)
                _simulation.RequestSnapToOrigin();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private float AspectRatio =>
        (float)_gd.Viewport.Width / _gd.Viewport.Height;

    private void UpdateUI() { }

    private void UpdateDirectionBall(DirectionBall? ball)
    {
        if (ball == null) return;
        ball.SetOrientation(_camera.Forward, _camera.Right, _camera.Up);

        var toStar = DVec3.Zero - _camera.UniversePosition;
        if (toStar.Length > 0.001)
        {
            toStar = toStar / toStar.Length;
            ball.SetVector("star",
                new Vector3((float)toStar.X, (float)toStar.Y, (float)toStar.Z),
                new Color(255, 220, 80), "★");
        }

        var gravEcliptic = new Vector3((float)_gravDirX, (float)_gravDirY, (float)_gravDirZ);
        if (gravEcliptic.LengthSquared() > 0.001f)
        {
            var gravGalaxy = Vector3.TransformNormal(gravEcliptic, _eclipticRotation);
            ball.SetVector("grav", gravGalaxy, new Color(120, 200, 255), "g");
        }
    }

    // ── Ecliptic tilt ─────────────────────────────────────────────────────────

    private void ComputeEclipticRotation()
    {
        var tiltAxis = new Vector3(
            MathF.Cos(_system.EclipticTiltAzimuthRadians),
            0f,
            MathF.Sin(_system.EclipticTiltAzimuthRadians));
        _eclipticRotation = Matrix.CreateFromAxisAngle(tiltAxis, _system.EclipticTiltRadians);
    }

    private DVec3 EclipticToGalaxy(DVec3 pos)
    {
        var v = Vector3.Transform(new Vector3((float)pos.X, (float)pos.Y, (float)pos.Z), _eclipticRotation);
        return new DVec3(v.X, v.Y, v.Z);
    }

    // ── Ship ──────────────────────────────────────────────────────────────────

    private ShieldComponent? _shield;

    private void SpawnShip(DVec3 startPos)
    {
        var ship = new Ship
        {
            Position    = startPos,
            SizeClass   = ShipSizeClass.Medium,
            MoveSpeedMs = 5e9,
        };
        ship.SetOrientation(Quaternion.CreateFromYawPitchRoll(0f, -0.2f, 0f));

        var reactor = new PowerReactor("Reactor", maxPower: 120e6, outputCapacitorJ: 50e6);

        var bus = new PowerBus("MainBus", capacityJ: 10e6, maxPower: 120e6);
        bus.ConnectSource(reactor.OutputCapacitor);

        var powerManager = new PowerPriorityManager();
        powerManager.AttachToBus(bus);

        _shield = new ShieldComponent("Shield", maxShieldJ: 5e6, chargeRateW: 500e3);

        // Connector wires shield to bus via the priority manager, capping at 600 kW
        var shieldConnector = new ConnectorComponent("ShieldConnector", "MainBus", "Shield", maxPower: 600e3);
        shieldConnector.Connect(powerManager, _shield.DemandWatts, _shield.ReceivePower);

        ship.Install(reactor);
        ship.Install(bus);
        ship.Install(powerManager);
        ship.Install(_shield);
        ship.Install(shieldConnector);
        _shield.PowerOn = false;  // starts off — player enables via SYS panel

        _simulation.SetShip(ship);
    }

    private const float MouseSens = 0.003f;

    private PlayerInput BuildShipInput(MouseState mouse, KeyboardState keys)
    {
        // Rotation — right-drag maps to pitch/yaw as raw angle deltas (radians)
        double pitchInput = 0.0, yawInput = 0.0;
        if (mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Pressed)
        {
            int dx = mouse.X - _prevMouse.X;
            int dy = mouse.Y - _prevMouse.Y;
            yawInput   = -dx * MouseSens;
            pitchInput = -dy * MouseSens;
        }

        // Thrust — keyboard axes, -1..1
        // W/S = fwd/back  A/D = strafe  R/F = up/down  Q/E = roll
        double fwd  = (keys.IsKeyDown(Keys.W) ? 1.0 : 0.0) - (keys.IsKeyDown(Keys.S) ? 1.0 : 0.0);
        double lat  = (keys.IsKeyDown(Keys.D) ? 1.0 : 0.0) - (keys.IsKeyDown(Keys.A) ? 1.0 : 0.0);
        double vert = (keys.IsKeyDown(Keys.R) ? 1.0 : 0.0) - (keys.IsKeyDown(Keys.F) ? 1.0 : 0.0);
        double roll = (keys.IsKeyDown(Keys.E) ? 1.0 : 0.0) - (keys.IsKeyDown(Keys.Q) ? 1.0 : 0.0);

        return new PlayerInput(fwd, lat, vert, roll, pitchInput, yawInput, false, true);
    }

    // ── UI mode ───────────────────────────────────────────────────────────────

    private void ApplyUiMode(bool active)
    {
        if (_rightPanel != null) _rightPanel.UiModeActive = active;
        if (_leftPanel  != null) _leftPanel.UiModeActive  = active;
        // CockpitRail is always interactable — peek strip tabs and toggle work in all modes.
    }

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
