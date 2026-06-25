using Inferior.Core;
using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Core.Simulation;
using Inferior.Galaxy;
using Inferior.Game.StationGen;
using Inferior.Gameplay;
using Inferior.Gameplay.Components;
using Inferior.Gameplay.Components.Power;
using Inferior.Gameplay.Sensors;
using Inferior.Gameplay.Ship;
using Inferior.Rendering;
using Inferior.UI;
using Inferior.UI.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Reflection.Metadata;

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
    private readonly ContentManager  _content;

    // ── System data ───────────────────────────────────────────────────────────
    private Star       _star   = null!;
    private StarSystem _system = null!;

    // ── 3D infrastructure ─────────────────────────────────────────────────────
    private Camera3D    _camera = null!;
    private BasicEffect _effect = null!;
    private Effect?     _atmosEffect;
    private Matrix      _eclipticRotation = Matrix.Identity;

    // Double-precision 3×3 rotation matrix for EclipticToGalaxy.
    // Avoids float quantisation (~9 km at 1 AU) that caused station jumpiness.
    // Rows: galaxy-space X/Y/Z expressed in ecliptic basis.
    private double _er00 = 1, _er01, _er02;
    private double _er10, _er11 = 1, _er12;
    private double _er20, _er21, _er22 = 1;

    private VertexBuffer _sphereVb = null!;
    private IndexBuffer  _sphereIb = null!;
    private int          _sphereTriCount;

    // Reusable ring vertex array — rebuilt each frame per orbit
    private VertexPositionColor[] _ringVerts = null!;

    // Reused per glow billboard draw — avoids per-frame allocation
    private VertexPositionColorTexture[] _glowVerts      = new VertexPositionColorTexture[6];
    // Reused per atmosphere billboard draw — 6 verts (2 triangles)
    private VertexPositionTexture[]      _atmosQuadVerts = new VertexPositionTexture[6];

    // Skybox star field — built once on enter, static for the session
    private VertexPositionColor[] _skyboxPoints    = [];  // PointList — one vertex per star
    private VertexPositionColor[] _skyboxGlowVerts = [];  // TriangleList — tiny quads for bright/near stars

    // ── 2D overlay (SpriteBatch for HUD) ──────────────────────────────────────
    private Texture2D _pixel       = null!;
    private Texture2D _starGlowTex = null!;  // soft radial gradient — reused for all glow layers
    private Texture2D _navGlowTex  = null!;  // cubic-falloff radial gradient for nav/strobe light glow

    // ── Time ──────────────────────────────────────────────────────────────────
    private double _gameTimeSeconds;

    // ── Cached body positions ─────────────────────────────────────────────────
    private readonly List<(OrbitalBody body, DVec3 pos)> _bodyPositions = [];

    // ── Station rendering ─────────────────────────────────────────────────────
    // Per-station placed module list — generated once per system entry from name seed.
    private readonly Dictionary<Galaxy.Station, List<PlacedModule>>                          _stationGeometry  = [];
    private readonly List<(Galaxy.Station station, DVec3 pos)>                               _stationPositions = [];
    // TODO: remove test containers — 3–6 debris contacts per station for radar testing
    private readonly List<TestContainerEntry> _testContainers = [];
    // GPU-side decoration meshes built from PlacedModule.Mesh after generation.
    private readonly Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> _decoMeshes  = [];
    // GPU-side glass meshes built from PlacedModule.GlassMesh (windows, portholes).
    private readonly Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> _glassMeshes = [];
    // GPU-side hull meshes (VertexPositionNormalTexture) for real-time BasicEffect lighting.
    private readonly Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> _hullMeshes  = [];

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
    private DirectionBall?   _systemDirBall;
    private DirectionBall?   _cockpitDirBall;
    private RadarDisplay?    _radarDisplay;
    private EdgePanelHost?   _rightPanel;
    private EdgePanelHost?   _leftPanel;
    private CockpitRail?     _cockpitRail;
    // Stored so we can unsubscribe on OnExit
    private Action<SystemMessage>? _systemHandler;
    private HudAlertDisplay        _hudAlert = new();
    private Action<double>?       _gravDirXHandler;
    private Action<double>?       _gravDirYHandler;
    private Action<double>?       _gravDirZHandler;
    private double                _gravDirX, _gravDirY, _gravDirZ;
    private Action<RadarContact>? _radarContactHandler;
    private Action<string>?       _radarLostHandler;

    // ── Targeting ─────────────────────────────────────────────────────────────
    private readonly TargetingSystem _targeting       = new();
    private readonly HashSet<string> _radarContactIds = [];   // IDs fed this session; cleared on exit
    private DirectionBall?      _targetingDirBall;
    private Label?              _targetLineShip;
    private Label?              _targetLineNav;
    private Label?              _targetLineHyp;
    private LandingRadarPanel?  _landingRadar;
    private DockingInstrument? _dockingInstrument;

    // Pad target — world position and bearing recomputed each frame
    private DVec3  _padWorldPos;
    private double _padDistance;
    private DVec3  _padDirection;

    // ── Reference frame (zero-speed) tracking ────────────────────────────────
    private DVec3  _refVelocity;               // current zero-speed velocity in galaxy space
    private string _refName = "";              // name of the reference object
    private DVec3  _prevCameraPos;
    private bool   _prevCameraPosValid;
    private DVec3  _cameraActualVelocity;      // camera position delta / dt this frame

    // ── SCAN tab ──────────────────────────────────────────────────────────────
    private SpectrumGraph?   _spectrumGraph;
    private Button?          _spectrumScanButton;
    private InstrumentMeter? _atmPressureMeter;
    private double           _scanCooldown;  // seconds remaining before button re-enables

    // ── Ship fly speed (scroll-adjusted, main thread → sim thread each frame) ──
    private double _shipBaseSpeed = 5e9;  // same default as camera; proximity scaling clamps it

    // ── Camera modes ──────────────────────────────────────────────────────────
    // TAB  — toggles between ship-control and mouse-driven UI interaction.
    // F11  — toggles between ship camera (cockpit) and free debug camera.
    private bool _uiMouseMode;
    private bool _debugCameraMode;
    private bool _prevIsGameActive = true;
    private bool _prevUiMouseMode;

    // Last thrust input from ship mode — preserved so UI mode keeps the same velocity.
    private PlayerInput _lastFlightInput = PlayerInput.Zero;

    // Ship snapshot captured once at the top of Update() — all sub-systems use this
    // single consistent value so no two decisions in the same frame see different positions.
    private SpaceSimulation.ShipSnapshot? _frameShipSnap;

    // Colour-invert blend for the crosshair: result = src - dest.
    // With white source this gives (1-R, 1-G, 1-B) — readable against any background.
    // Static to follow the MonoGame convention for built-in BlendState singletons;
    // the OS reclaims the GPU resource on application exit.
    private static readonly BlendState _invertBlend = new()
    {
        ColorBlendFunction    = BlendFunction.Subtract,
        ColorSourceBlend      = Blend.One,
        ColorDestinationBlend = Blend.One,
        AlphaBlendFunction    = BlendFunction.Add,
        AlphaSourceBlend      = Blend.Zero,
        AlphaDestinationBlend = Blend.One,
    };

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

    public SystemSpaceState(GraphicsDevice gd, SpriteFont font, SpaceSimulation simulation, ContentManager content)
        : base(GameStateId.SystemSpace)
    {
        _gd         = gd;
        _font       = font;
        _simulation = simulation;
        _content    = content;
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

            if (p.TargetBody != null)
            {
                // Approach from double-click — force new spawn near the body.
                // _system is freshly generated, so p.TargetBody is a different object instance.
                // Look up by name to find the matching body and its parent in the new system.
                _ship = null;
                OrbitalBody? resolvedBody = null;
                DVec3        parentPos    = DVec3.Zero;

                foreach (var planet in _system.Planets)
                {
                    if (planet.Name == p.TargetBody.Name)
                    {
                        resolvedBody = planet;
                        break;
                    }
                    foreach (var moon in planet.Children)
                    {
                        if (moon.Name == p.TargetBody.Name)
                        {
                            resolvedBody = moon;
                            parentPos    = planet.GetPosition(p.GameTime, DVec3.Zero);
                            break;
                        }
                    }
                    if (resolvedBody != null) break;
                }

                var    body         = resolvedBody ?? p.TargetBody;
                DVec3  bodyEcliptic = body.GetPosition(p.GameTime, parentPos);
                DVec3  bodyGalaxy   = EclipticToGalaxy(bodyEcliptic);
                double dist         = System.Math.Max(body.RadiusMeters * 5.0, 1e6);
                var    startPos     = EclipticToGalaxy(bodyEcliptic + new DVec3(0, dist * 0.4, dist));
                Quaternion bodyOri  = QuatLookAt(bodyGalaxy - startPos);
                _camera = new Camera3D(startPos, AspectRatio);
                _camera.SetPose(startPos, bodyOri);
                SpawnShip(startPos, bodyOri);
            }
            else if (p.TargetStation != null)
            {
                // Approach a station from system map double-click — spawn 2 km away, facing it
                _ship = null;
                DVec3 parentEcliptic = DVec3.Zero;
                if (p.TargetStation.OrbitParent != null)
                {
                    DVec3 grandparent = DVec3.Zero;
                    foreach (var planet in _system.Planets)
                        if (planet.Children.Any(c => c.Name == p.TargetStation.OrbitParent.Name))
                            grandparent = planet.GetPosition(p.GameTime, DVec3.Zero);
                    parentEcliptic = p.TargetStation.OrbitParent.GetPosition(p.GameTime, grandparent);
                }
                DVec3 stationEcliptic = p.TargetStation.GetPosition(p.GameTime, parentEcliptic);
                DVec3 stationGalaxy   = EclipticToGalaxy(stationEcliptic);

                // Place spawn 2 km above the ecliptic plane relative to the station
                DVec3 eclipticUp = new DVec3(_er01, _er11, _er21); // ecliptic +Y in galaxy space
                DVec3 spawnPos   = stationGalaxy + eclipticUp * 2000.0;

                Quaternion spawnOri = QuatLookAt(stationGalaxy - spawnPos);
                _camera = new Camera3D(spawnPos, AspectRatio);
                _camera.SetPose(spawnPos, spawnOri);
                SpawnShip(spawnPos, spawnOri);
            }
            else if (_ship != null)
            {
                // Returning from a map — ship instance is already alive in the simulation
                // with the correct position. Just re-register it and sync the camera.
                _simulation.SetShip(_ship);
                var snap = _simulation.ShipState;
                var camPos = snap?.CockpitWorldPosition ?? _ship.CockpitWorldPosition;
                var camOri = snap?.Orientation          ?? _ship.Orientation;
                _camera = new Camera3D(camPos, AspectRatio);
                _camera.SetPose(camPos, camOri);
            }
            else
            {
                // First entry — start 2 AU out
                var startPos = new DVec3(0, 0.5e11, 3e11);
                _camera = new Camera3D(startPos, AspectRatio);
                SpawnShip(startPos, Quaternion.CreateFromYawPitchRoll(0f, -0.2f, 0f));
            }
        }
        else if (payload is Star star)
        {
            // Fallback: entered directly with just a star (shouldn't happen in normal flow)
            _star    = star;
            _system  = StarSystem.Generate(star, GalaxyGenerator.SystemSeed(star));
            ComputeEclipticRotation();
            var fallbackPos = new DVec3(0, 0.5e11, 3e11);
            _camera  = new Camera3D(fallbackPos, AspectRatio);
            SpawnShip(fallbackPos, Quaternion.CreateFromYawPitchRoll(0f, -0.2f, 0f));
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

        StationTextureRegistry.Initialize(_gd);

        StationTextureRegistry.SetTexture(SurfaceTexture.CleanPanel,
            _content.Load<Texture2D>("Textures/cleanpanel"));
        StationTextureRegistry.SetTexture(SurfaceTexture.TechPanel,
            _content.Load<Texture2D>("Textures/techpanel"));
        StationTextureRegistry.SetTexture(SurfaceTexture.IndustrialPanel,
            _content.Load<Texture2D>("Textures/industrialpanel"));
        StationTextureRegistry.SetTexture(SurfaceTexture.CargoPanel,
            _content.Load<Texture2D>("Textures/cargopanel"));
        StationTextureRegistry.SetTexture(SurfaceTexture.WornPanel,
            _content.Load<Texture2D>("Textures/wornpanel"));

        // Station module layouts — generated once from name-derived seed.
        // StationGenerator.Generate also runs StationDecorator internally.
        _stationGeometry.Clear();
        foreach (var v in _decoMeshes.Values)  { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var v in _glassMeshes.Values) { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var v in _hullMeshes.Values)  { v.vb.Dispose(); v.ib.Dispose(); }
        _decoMeshes.Clear();
        _glassMeshes.Clear();
        _hullMeshes.Clear();
        foreach (var station in _system.Stations)
        {
            var modules = StationGenerator.Generate(station, _gd);
            _stationGeometry[station] = modules;
            foreach (var mod in modules)
            {
                var gpu = mod.Mesh?.Build(_gd);
                if (gpu.HasValue)
                    _decoMeshes[mod] = gpu.Value;

                var glassGpu = mod.GlassMesh?.Build(_gd);
                if (glassGpu.HasValue)
                    _glassMeshes[mod] = glassGpu.Value;

                // Custom-mesh modules (MeshFactory) include the hull in mod.Mesh,
                // rendered by the deco pass with baked lighting — skip box hull.
                if (mod.Definition.MeshFactory == null)
                    _hullMeshes[mod] = BuildHullMesh(_gd, mod);
            }
        }
        _stationPositions.Clear();
        foreach (var c in _testContainers) { c.Vb.Dispose(); c.Ib.Dispose(); }
        _testContainers.Clear();
        _prevCameraPosValid = false;

        // Skybox — galaxy stars projected onto a far sphere around the current system
        (_skyboxPoints, _skyboxGlowVerts) = BuildSkybox(_star, GalaxyGenerator.Generate());

        _pixel = new Texture2D(_gd, 1, 1);
        _pixel.SetData([Color.White]);

        _starGlowTex = CreateStarGlowTexture(_gd, 128);
        _navGlowTex  = CreateNavGlowTexture(_gd, 64);
        _atmosEffect = _content.Load<Effect>("Effects/Atmosphere");

        _pendingTransition = null;
        UpdateUI();

        // ── DataBus UI setup ──────────────────────────────────────────────────
        var theme = Theme.InferiorDark(_font);
        _ui = new UIManager(_gd, theme);
        _uiMouseMode     = false;
        _debugCameraMode = false;

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

        _systemDirBall = new DirectionBall
        {
            Header = "HEADING",
            Bounds = new Rectangle(0, 0, innerW, innerW),
        };

        var navPanel = new Panel { DrawBackground = false, DrawBorder = false };
        navPanel.Add(_systemDirBall);

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

        // ── Left panel: SCAN tab ──────────────────────────────────────────────
        const int scanBtnH    = 28;
        const int scanBtnGap  = 8;
        int       graphW      = innerW;
        int       graphH      = graphW / 5;  // 1:5 height:width ratio

        _spectrumScanButton = new Button("SCAN SPECTRUM",
            new Rectangle(0, 0, innerW, scanBtnH));
        _spectrumScanButton.Clicked += _ =>
        {
            if (_scanCooldown > 0) return;
            CommandBus.Send("SolarSpectrumSensor.Scan");
            _spectrumScanButton.Text    = "SCANNING...";
            _spectrumScanButton.Enabled = false;
            _scanCooldown = SolarSpectrumSensor.ScanDurationSeconds + 0.5;
        };

        _atmPressureMeter = new InstrumentMeter
        {
            Label       = "ATM PRESSURE",
            MinValue    = 0,
            MaxValue    = 120_000,  // Pa — up to ~1.2 atm
            ScaleFactor = 1.0,
            Format      = "F0",
            Topic       = "AtmosphericSensor.Pressure",
            Bounds      = new Rectangle(0, scanBtnH + scanBtnGap, innerW, meterH),
        };

        var atmScanButton = new Button("ATM SCAN",
            new Rectangle(0, scanBtnH + scanBtnGap + meterH + scanBtnGap, innerW, scanBtnH));
        atmScanButton.Clicked += _ => CommandBus.Send("AtmosphericSensor.Scan");

        int spectrumY = scanBtnH + scanBtnGap + meterH + scanBtnGap + scanBtnH + scanBtnGap;
        _spectrumGraph = new SpectrumGraph
        {
            Header = "SOLAR SPECTRUM",
            Topic  = "SolarSpectrumSensor.Data",
            Bounds = new Rectangle(0, spectrumY, graphW, graphH),
        };

        var scanPanel = new Panel { DrawBackground = false, DrawBorder = false };
        scanPanel.Add(_spectrumScanButton);
        scanPanel.Add(_atmPressureMeter);
        scanPanel.Add(atmScanButton);
        scanPanel.Add(_spectrumGraph);

        _leftPanel = new EdgePanelHost(PanelEdge.Left)
        {
            PanelSize     = panelW,
            HandleSize    = 28,
            HandleLength  = 80,
            CornerMargin  = 8,
            Bounds        = new Rectangle(0, 0, _gd.Viewport.Width, sidePanelH),
        };
        _leftPanel.AddTab("SCAN", scanPanel);
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

        _radarDisplay = new RadarDisplay();

        _shieldToggleButton = new ToggleButton("SHIELD", new Rectangle(4, 4, 120, 28))
        {
            FontScale = 0.72f,
        };
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

        _landingRadar = new LandingRadarPanel
        {
            Bounds = new Rectangle(0, 0, 500, 220),
        };
        _landingRadar.Released += () =>
        {
            _targeting.ClearNavTarget();
        };

        _cockpitRail = new CockpitRail
        {
            Bounds = new Rectangle(0, 0, _gd.Viewport.Width, _gd.Viewport.Height),
        };
        // Left side (3 tabs): DIR BALL, RADAR, LANDING
        _cockpitRail.AddCenterTab("DIR BALL", _cockpitDirBall);
        _cockpitRail.AddCenterTab("RADAR",    _radarDisplay);
        _cockpitRail.AddCenterTab("LANDING",  _landingRadar);
        _dockingInstrument = new DockingInstrument
        {
            Bounds = new Rectangle(0, 0, 500, 220),
        };

        // Right side (3 tabs): DOCK, LOG, CTRL
        _cockpitRail.AddCenterTab("DOCK",     _dockingInstrument);
        _cockpitRail.AddCenterTab("LOG",      _console);
        _cockpitRail.AddCenterTab("CTRL",     new Panel { DrawBackground = false, DrawBorder = false });
        _cockpitRail.RightWing.Add(_shieldToggleButton);

        // ── LeftWing: targeting direction ball + 3-line target readout ────────
        // Ball has no header — use all 76px for the sphere so it matches text height.
        _targetingDirBall = new DirectionBall
        {
            Header = "",
            Bounds = new Rectangle(4, 6, 76, 76),
        };
        // Labels start just to the right of the ball; colours match DirectionBall dots.
        var tc = _ui!.Theme;
        _targetLineShip = new Label("Target: None", new Rectangle(88, 10, 280, 20))
        {
            FontScale = 0.72f,
            TextColor = tc.TargetShip,
        };
        _targetLineNav = new Label("Nav: None", new Rectangle(88, 34, 280, 20))
        {
            FontScale = 0.72f,
            TextColor = tc.TargetNav,
        };
        _targetLineHyp = new Label("Hyp: None", new Rectangle(88, 58, 280, 20))
        {
            FontScale = 0.72f,
            TextColor = tc.TargetHyp,
        };
        _cockpitRail.LeftWing.Add(_targetingDirBall);
        _cockpitRail.LeftWing.Add(_targetLineShip);
        _cockpitRail.LeftWing.Add(_targetLineNav);
        _cockpitRail.LeftWing.Add(_targetLineHyp);

        _ui.Add(_cockpitRail);

        // Restore panel layout if returning from system map
        if (payload is SystemSpacePayload { Layout: { } layout })
            ApplyCockpitLayout(layout);

        // Restore nav target selected in system map; clear if payload carries none
        if (payload is SystemSpacePayload ssp)
        {
            if (ssp.NavBody != null)          _targeting.SetNavTarget(ssp.NavBody);
            else if (ssp.NavStation != null)  _targeting.SetNavTarget(ssp.NavStation);
            else                              _targeting.ClearNavTarget();
        }

        // Start in ship-control mode — panels retracted, handles and buttons hidden
        ApplyUiMode(false);

        // Meters subscribe themselves via Topic — only non-meter handlers need wiring here
        _systemHandler = msg =>
        {
            _console?.AddMessage(msg);
            _hudAlert.AddMessage(msg);
        };

        _gravDirXHandler = v => _gravDirX = v;
        _gravDirYHandler = v => _gravDirY = v;
        _gravDirZHandler = v => _gravDirZ = v;

        DataBus.Instruments.Subscribe($"GravitySensor.{Topics.GravitySensor.DirectionX}", _gravDirXHandler);
        DataBus.Instruments.Subscribe($"GravitySensor.{Topics.GravitySensor.DirectionY}", _gravDirYHandler);
        DataBus.Instruments.Subscribe($"GravitySensor.{Topics.GravitySensor.DirectionZ}", _gravDirZHandler);
        DataBus.System.Subscribe(Topics.System.All, _systemHandler);

        _radarContactHandler = c =>
        {
            _targeting.OnContactUpdated(c);
            UpdateCockpitDirBallContact(c);
        };
        _radarLostHandler = id =>
        {
            _targeting.OnContactLost(id);
            _cockpitDirBall?.RemoveVector($"radar_{id}");
        };
        DataBus.Radar.Subscribe(Topics.Radar.All,     _radarContactHandler);
        DataBus.RadarLost.Subscribe(Topics.Radar.All, _radarLostHandler);

        // First system message — confirms state entry
        DataBus.System.Publish(Topics.System.All, new($"Entered {_star.Name}"));
    }

    public override void OnExit()
    {
        // Stop ship from drifting while browsing maps
        _simulation.SetInput(PlayerInput.Zero);

        // Meters unsubscribe themselves when Topic is cleared
        if (_reactorPowerOutputMeter != null) _reactorPowerOutputMeter.Topic = "";
        if (_reactorDrawnMeter       != null) _reactorDrawnMeter.Topic       = "";
        if (_busConsumptionMeter     != null) _busConsumptionMeter.Topic     = "";
        if (_connectorFlowMeter      != null) _connectorFlowMeter.Topic      = "";
        if (_connectorNeedle         != null) _connectorNeedle.Topic         = "";
        if (_shieldCapacitorMeter    != null) _shieldCapacitorMeter.Topic    = "";
        if (_atmPressureMeter        != null) _atmPressureMeter.Topic        = "";
        if (_spectrumGraph           != null) _spectrumGraph.Topic           = "";

        if (_gravDirXHandler != null)
            DataBus.Instruments.Unsubscribe($"GravitySensor.{Topics.GravitySensor.DirectionX}", _gravDirXHandler);
        if (_gravDirYHandler != null)
            DataBus.Instruments.Unsubscribe($"GravitySensor.{Topics.GravitySensor.DirectionY}", _gravDirYHandler);
        if (_gravDirZHandler != null)
            DataBus.Instruments.Unsubscribe($"GravitySensor.{Topics.GravitySensor.DirectionZ}", _gravDirZHandler);
        if (_systemHandler != null)
            DataBus.System.Unsubscribe(Topics.System.All, _systemHandler);
        if (_radarContactHandler != null)
            DataBus.Radar.Unsubscribe(Topics.Radar.All, _radarContactHandler);
        if (_radarLostHandler != null)
            DataBus.RadarLost.Unsubscribe(Topics.Radar.All, _radarLostHandler);
        if (_shieldCapacitorHandler != null)
            DataBus.Instruments.Unsubscribe($"Shield.{Topics.Shield.Capacitor}", _shieldCapacitorHandler);

        // Remove all radar contacts fed from this session so TargetingSystem is clean on re-entry
        foreach (string id in _radarContactIds)
            _targeting.OnContactLost(id);
        _radarContactIds.Clear();

        _ui?.Dispose();
        _ui = null;

        _effect?.Dispose();
        _sphereVb?.Dispose();
        _sphereIb?.Dispose();
        foreach (var v in _decoMeshes.Values)  { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var v in _glassMeshes.Values) { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var v in _hullMeshes.Values)  { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var c in _testContainers)     { c.Vb.Dispose(); c.Ib.Dispose(); }
        _decoMeshes.Clear();
        _glassMeshes.Clear();
        _hullMeshes.Clear();
        _testContainers.Clear();
        _pixel?.Dispose();
        _starGlowTex?.Dispose();
        _navGlowTex?.Dispose();
        _atmosEffect = null; // owned by ContentManager — do not dispose manually
    }

    public override void OnResize(int width, int height)
    {
        _camera?.SetProjection(MathHelper.ToRadians(60f), AspectRatio, 0.00001f, 50_000f);
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
        _frameShipSnap = _simulation.ShipState;  // read once — consistent for this entire frame

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

        // Re-enable scan button once cooldown expires
        if (_scanCooldown > 0)
        {
            _scanCooldown -= dt;
            if (_scanCooldown <= 0 && _spectrumScanButton != null)
            {
                _spectrumScanButton.Enabled = true;
                _spectrumScanButton.Text    = "SCAN SPECTRUM";
            }
        }

        // When the window has no OS focus, substitute a centred mouse so look-input delta
        // stays at zero. The real mouse state is still stored in _prevMouse and used for UI
        // hit-testing so button press/release tracking remains correct.
        // Also suppress look-input on the first frame after focus is regained — the OS
        // typically delivers a large accumulated delta on that frame (alt-tab / snipping tool).
        bool focusJustRegained = IsGameActive && !_prevIsGameActive;
        _prevIsGameActive = IsGameActive;
        bool justExitedUiMode = _prevUiMouseMode && !_uiMouseMode;
        _prevUiMouseMode = _uiMouseMode;
        var lookMouse = (IsGameActive && !focusJustRegained && !justExitedUiMode) ? mouse : new MouseState(
            _gd.Viewport.Width / 2, _gd.Viewport.Height / 2,
            mouse.ScrollWheelValue,
            ButtonState.Released, ButtonState.Released, ButtonState.Released,
            ButtonState.Released, ButtonState.Released);

        if (_uiMouseMode)
        {
            // UI mode — UI gets mouse/keyboard; ship rotation is frozen.
            _ui?.Update(dt, new InputState(mouse, _prevMouse, keys, _prevKeys));
            if (_debugCameraMode)
                _camera.Update(dt, new MouseState(), new KeyboardState()); // clear any held drag
            else
            {
                // Ship camera must still track the ship — without this the camera freezes
                // while thrust keeps the ship moving, causing a snap on UI-mode exit.
                if (_frameShipSnap != null)
                    _camera.SetPose(_frameShipSnap.CockpitWorldPosition, _frameShipSnap.Orientation);
            }
            // Preserve the last flight-mode thrust so relative speed is unchanged when
            // the player opens the UI. Rotation inputs are zeroed to keep the ship still.
            _simulation.SetInput(_lastFlightInput with { PitchInput = 0, YawInput = 0, RollInput = 0 });

            // Click-to-target — left click selects the nearest radar contact bracket
            if (mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released)
            {
                var vp = Matrix.Multiply(_camera.ViewMatrix, _camera.ProjectionMatrix);
                _targeting.SelectAtCursor(new Vector2(mouse.X, mouse.Y), vp, _gd.Viewport);
            }
        }
        else if (_debugCameraMode)
        {
            // Debug camera — free-look, ship receives no input and stays put.
            // Cursor is locked to window centre (same as ship mode) so look is always-on.
            _camera.BaseVelocity = _refVelocity;
            int dcx = _gd.Viewport.Width  / 2;
            int dcy = _gd.Viewport.Height / 2;
            _camera.Update(dt, lookMouse, keys, new Point(dcx, dcy));
            _simulation.SetInput(PlayerInput.Zero);
            if (IsGameActive) Mouse.SetPosition(dcx, dcy);
        }
        else
        {
            // Ship mode — input goes to the simulation; camera follows cockpit.
            // Cursor is locked to window centre so mouse can't escape the window.
            _lastFlightInput = BuildShipInput(lookMouse, keys);
            _simulation.SetInput(_lastFlightInput);
            if (_frameShipSnap != null)
                _camera.SetPose(_frameShipSnap.CockpitWorldPosition, _frameShipSnap.Orientation);
            if (IsGameActive) Mouse.SetPosition(_gd.Viewport.Width / 2, _gd.Viewport.Height / 2);
        }

        // Derive orbital time from the snapshot's bundled SimTime — the pair (Position, SimTime)
        // was published in a single TickPhysics call, so they always refer to the same tick.
        // Reading GameClock.SimTime separately races with Advance(), which fires before the
        // position is updated, producing a 1-tick (≈350 m) station-vs-ship mismatch.
        if (_frameShipSnap != null)
            _gameTimeSeconds = _frameShipSnap.SimTime;
        _camera.SetProjection(MathHelper.ToRadians(60f), AspectRatio, ComputeNearClip(), 50_000f);

        // Rebuild body positions — collect in ecliptic space then rotate to galaxy space
        _bodyPositions.Clear();
        foreach (var planet in _system.Planets)
            planet.CollectPositions(_gameTimeSeconds, DVec3.Zero, _bodyPositions);
        for (int i = 0; i < _bodyPositions.Count; i++)
        {
            var (body, pos) = _bodyPositions[i];
            _bodyPositions[i] = (body, EclipticToGalaxy(pos));
        }

        // Rebuild station positions — resolve parent body position, apply ecliptic rotation
        _stationPositions.Clear();
        foreach (var station in _system.Stations)
        {
            DVec3 eclipticPos = _system.GetStationPosition(station, _gameTimeSeconds);
            _stationPositions.Add((station, EclipticToGalaxy(eclipticPos)));
        }

        // Rebuild test container contacts — if newly populated this frame, repopulate
        if (_testContainers.Count == 0 && _stationPositions.Count > 0)
            SpawnTestContainers();

        // Update direction balls — after position rebuild so current-frame station positions
        // are used, not the previous frame's. Avoids the ~1-frame (~350 m) visual offset
        // between the dot indicator and the rendered station.
        UpdateDirectionBall(_systemDirBall);
        UpdateDirectionBall(_cockpitDirBall);

        // Feed planets, moons, and stations into TargetingSystem so C-key / click-to-target work
        FeedRadarContacts();

        // Track camera actual velocity for relative-speed display in debug mode
        DVec3 camPos = _camera.UniversePosition;
        _cameraActualVelocity = _prevCameraPosValid ? (camPos - _prevCameraPos) / dt : DVec3.Zero;
        _prevCameraPos      = camPos;
        _prevCameraPosValid = true;

        // Update targeting system with pre-computed galaxy-space positions
        DVec3 shipPosForTargeting = _frameShipSnap?.Position ?? _camera.UniversePosition;
        _targeting.Update(shipPosForTargeting, _star.GalacticPos, _bodyPositions, _stationPositions);
        UpdatePadTargetPosition();
        UpdateTargetingUI();
        UpdateRadarDisplay();
        UpdateLandingRadar();

        // Update reference frame (zero-speed object)
        UpdateReferenceFrame(_camera.UniversePosition);
        _simulation.SetReferenceVelocity(_refVelocity);

        // Proximity speed scale — applied to debug camera each frame
        if (_debugCameraMode)
            _camera.ProximitySpeedScale = ComputeProximityScale();

        // Ship fly speed — base speed scaled by proximity, then hard-capped near stations
        if (!_debugCameraMode)
        {
            DVec3 shipPos = _frameShipSnap?.Position ?? _camera.UniversePosition;
            _simulation.SetShipMoveSpeed(ComputeShipSpeed(shipPos));
        }

        // Feed world state to simulation — use ship snapshot position in ship mode,
        // camera position in debug mode (sensors track whoever is "there")
        DVec3 refPos = _debugCameraMode
            ? _camera.UniversePosition
            : _frameShipSnap?.Position ?? _camera.UniversePosition;
        _simulation.SetWorldState(_star, _system, refPos, _gameTimeSeconds);

        if (!_uiMouseMode)
            HandleKeyboard(keys, mouse);


        var inputState = new InputState(mouse, _prevMouse, keys, _prevKeys);
        _hudAlert.Update(dt);
        _hudAlert.HandleInput(inputState);

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

        // SunDirection = from scene toward star = opposite of "light travels" direction
        SceneLighting.SunDirection = -lightDir;

        // Pass 0 — skybox (drawn before depth buffer has any data, so geometry always wins)
        gd.DepthStencilState = DepthStencilState.None;
        DrawSkybox();

        // Pass 1 — opaque geometry (depth writes on)
        gd.BlendState        = BlendState.AlphaBlend;
        gd.DepthStencilState = DepthStencilState.Default;
        DrawOrbitRings();
        DrawStationOrbitRings();

        // Star glow — 3D billboard with depth-read so planets drawn opaque afterward
        // correctly overwrite it on their disc areas (fixes glow bleeding through planets).
        gd.BlendState        = BlendState.Additive;
        gd.DepthStencilState = DepthStencilState.DepthRead;
        DrawStarGlow3D();

        gd.BlendState        = BlendState.Opaque;
        gd.DepthStencilState = DepthStencilState.Default;
        DrawStarBody();
        foreach (var (body, pos) in _bodyPositions)
            DrawPlanetBody(body, pos);
        DrawStations();
        DrawTestContainers();
        DrawStationGlows(sb);

        // Pass 2 — transparent (no depth write/read — shader ray-sphere handles visibility)
        gd.BlendState        = BlendState.AlphaBlend;
        gd.DepthStencilState = DepthStencilState.None;
        foreach (var (body, pos) in _bodyPositions)
            DrawAtmosphere(body, pos);

        // ── 2D overlay ────────────────────────────────────────────────────────
        gd.DepthStencilState = DepthStencilState.None;

        sb.Begin(blendState: BlendState.AlphaBlend);
        DrawHUD(sb);
        DrawStationDots(sb);
        DrawTargetingHUD(sb);
        sb.End();

        // Crosshair — separate pass with colour-invert blend so it's readable against any background
        if (!_uiMouseMode)
        {
            sb.Begin(blendState: _invertBlend);
            DrawCrosshair(sb);
            sb.End();
        }

        // UI library draws on top — owns its own SpriteBatch
        _ui?.Draw();

        // HUD alert overlay — drawn after UI so it's always on top
        if (_ui != null)
        {
            sb.Begin(blendState: BlendState.AlphaBlend);
            _hudAlert.Draw(sb, _ui.Renderer, _font, gd.Viewport.Width, gd.Viewport.Height);
            sb.End();
        }
    }

    // ── 3D drawing ────────────────────────────────────────────────────────────

    // ── Station drawing ───────────────────────────────────────────────────────

    private static float StationPhysicalRadius(Galaxy.Station s) => s.Size switch
    {
        Galaxy.StationSize.Small  =>  250f,
        Galaxy.StationSize.Medium =>  800f,
        Galaxy.StationSize.Large  => 2500f,
        _                         =>  250f,
    };

    private void DrawStations()
    {
        if (_stationPositions.Count == 0) return;

        float rs = (float)Camera3D.RenderScale;

        // Hull pass — real-time BasicEffect N·L lighting with procedural texture.
        // Uses VertexPositionNormalTexture so normals are in the vertex data.
        // DirectionalLight0.Direction is already set from the star position in Draw().
        _effect.LightingEnabled                = true;
        _effect.TextureEnabled                 = true;
        _effect.VertexColorEnabled             = false;
        _effect.DiffuseColor                   = Vector3.One;
        _effect.DirectionalLight0.DiffuseColor = SceneLighting.SunColour;
        _effect.AmbientLightColor              = new Vector3(SceneLighting.Ambient);

        foreach (var (station, universePos) in _stationPositions)
        {
            Vector3 renderPos = _camera.ToRenderSpace(universePos);
            if (renderPos.Length() > 30_000f) continue;
            if (!_stationGeometry.TryGetValue(station, out var modules)) continue;

            var sysQ   = station.GetOrientation(_gameTimeSeconds);
            var stRotQ = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);
            Matrix stationRot = Matrix.CreateFromQuaternion(stRotQ);

            foreach (var mod in modules)
            {
                if (!_hullMeshes.TryGetValue(mod, out var hull)) continue;
                if (mod.TextureInstance == null) continue;

                mod.Transform.Decompose(out _, out Quaternion modRot, out Vector3 posMetres);
                _effect.World =
                    Matrix.CreateScale(rs) *
                    Matrix.CreateFromQuaternion(modRot) *
                    stationRot *
                    Matrix.CreateTranslation(Vector3.Transform(posMetres, stationRot) * rs) *
                    Matrix.CreateTranslation(renderPos);

                _effect.Texture = mod.TextureInstance;

                _gd.SetVertexBuffer(hull.vb);
                _gd.Indices = hull.ib;

                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _gd.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        baseVertex: 0, startIndex: 0,
                        primitiveCount: hull.triCount);
                }
            }
        }

        // Decoration pass — pre-baked lighting in vertex colours; texture modulates.
        _effect.LightingEnabled    = false;
        _effect.VertexColorEnabled = true;
        _effect.TextureEnabled     = true;

        foreach (var (station, universePos) in _stationPositions)
        {
            Vector3 renderPos = _camera.ToRenderSpace(universePos);
            if (renderPos.Length() > 30_000f) continue;

            if (!_stationGeometry.TryGetValue(station, out var modules)) continue;

            var sysQ   = station.GetOrientation(_gameTimeSeconds);
            var stRotQ = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);
            Matrix stationRot = Matrix.CreateFromQuaternion(stRotQ);

            foreach (var mod in modules)
            {
                if (!_decoMeshes.TryGetValue(mod, out var deco)) continue;

                mod.Transform.Decompose(out _, out Quaternion modRot, out Vector3 posMetres);

                _effect.World =
                    Matrix.CreateScale(rs) *
                    Matrix.CreateFromQuaternion(modRot) *
                    stationRot *
                    Matrix.CreateTranslation(Vector3.Transform(posMetres, stationRot) * rs) *
                    Matrix.CreateTranslation(renderPos);

                _effect.Texture = mod.TextureInstance ?? StationTextureRegistry.Get(mod.Mesh!.Texture);

                _gd.SetVertexBuffer(deco.vb);
                _gd.Indices = deco.ib;

                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _gd.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        baseVertex: 0, startIndex: 0,
                        primitiveCount: deco.triCount);
                }
            }
        }

        // Glass pass — windows, portholes; White texture so vertex colours are unmodified.
        _effect.Texture = StationTextureRegistry.White;

        foreach (var (station, universePos) in _stationPositions)
        {
            Vector3 renderPos = _camera.ToRenderSpace(universePos);
            if (renderPos.Length() > 30_000f) continue;

            if (!_stationGeometry.TryGetValue(station, out var modules)) continue;

            var sysQ   = station.GetOrientation(_gameTimeSeconds);
            var stRotQ = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);
            Matrix stationRot = Matrix.CreateFromQuaternion(stRotQ);

            foreach (var mod in modules)
            {
                if (!_glassMeshes.TryGetValue(mod, out var glass)) continue;

                mod.Transform.Decompose(out _, out Quaternion modRot, out Vector3 posMetres);

                _effect.World =
                    Matrix.CreateScale(rs) *
                    Matrix.CreateFromQuaternion(modRot) *
                    stationRot *
                    Matrix.CreateTranslation(Vector3.Transform(posMetres, stationRot) * rs) *
                    Matrix.CreateTranslation(renderPos);

                _gd.SetVertexBuffer(glass.vb);
                _gd.Indices = glass.ib;

                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _gd.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        baseVertex: 0, startIndex: 0,
                        primitiveCount: glass.triCount);
                }
            }
        }

        _effect.TextureEnabled     = false;
        _effect.VertexColorEnabled = false;
        _effect.LightingEnabled    = true;
    }

    private void DrawTestContainers()
    {
        if (_testContainers.Count == 0) return;

        float rs = (float)Camera3D.RenderScale;

        _effect.LightingEnabled    = false;
        _effect.VertexColorEnabled = true;
        _effect.TextureEnabled     = true;
        _effect.Texture            = StationTextureRegistry.Get(SurfaceTexture.CleanPanel);

        foreach (var tc in _testContainers)
        {
            DVec3 stPos = DVec3.Zero;
            foreach (var (s, sPos) in _stationPositions)
                if (ReferenceEquals(s, tc.Station)) { stPos = sPos; break; }

            DVec3   universePos = stPos + tc.Offset;
            Vector3 renderPos   = _camera.ToRenderSpace(universePos);
            if (renderPos.Length() > 30_000f) continue;

            _effect.World = Matrix.CreateScale(rs) * Matrix.CreateTranslation(renderPos);

            _gd.SetVertexBuffer(tc.Vb);
            _gd.Indices = tc.Ib;

            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList,
                    baseVertex: 0, startIndex: 0, primitiveCount: tc.TriCount);
            }
        }

        _effect.TextureEnabled     = false;
        _effect.VertexColorEnabled = false;
        _effect.LightingEnabled    = true;
    }

    // Builds a VertexPositionNormalTexture hull mesh for one module (6 box faces, 24 verts).
    // Normals are local-space outward per face; BasicEffect transforms them at draw time.
    // UV uses the same tangent-frame projection as StationModuleMesh.AddQuad (5 m/tile).
    private static (VertexBuffer vb, IndexBuffer ib, int triCount) BuildHullMesh(
        GraphicsDevice gd, PlacedModule mod)
    {
        const float UvScale      = 5.0f;
        const float ChamferInset = 0.38f * 0.707f;  // matches StationDecorator chamferW * 0.707f
        var h  = mod.Definition.BoundingBox * 0.5f;
        float si = ChamferInset;

        var verts = new VertexPositionNormalTexture[24];
        var idx   = new int[36];

        // Per-face UV axes chosen so that U and V are always positive (0→4 for a 20 m face).
        // Cross(normal, arb) produces negative U on several faces of a standard box,
        // making texture V=0.5 (the name text) only partially sampled. Hardcoded axes avoid this.
        static void AddFace(VertexPositionNormalTexture[] v, int[] idx, int face,
                            Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 n,
                            Vector3 uAxis, Vector3 vAxis)
        {
            int b = face * 4;
            v[b    ] = new VertexPositionNormalTexture(v0, n, Vector2.Zero);
            v[b + 1] = new VertexPositionNormalTexture(v1, n, new Vector2(
                Vector3.Dot(v1 - v0, uAxis) / UvScale, Vector3.Dot(v1 - v0, vAxis) / UvScale));
            v[b + 2] = new VertexPositionNormalTexture(v2, n, new Vector2(
                Vector3.Dot(v2 - v0, uAxis) / UvScale, Vector3.Dot(v2 - v0, vAxis) / UvScale));
            v[b + 3] = new VertexPositionNormalTexture(v3, n, new Vector2(
                Vector3.Dot(v3 - v0, uAxis) / UvScale, Vector3.Dot(v3 - v0, vAxis) / UvScale));

            int i = face * 6;
            idx[i    ] = b;     idx[i + 1] = b + 2; idx[i + 2] = b + 1;
            idx[i + 3] = b;     idx[i + 4] = b + 3; idx[i + 5] = b + 2;
        }

        // Each face panel is inset by ChamferInset in its two lateral axes so that
        // the chamfer strip running along each edge is not hidden behind the panel.
        // The face-normal axis stays at the full surface depth (±h.N unchanged).
        //                                                                             n               uAxis              vAxis
        AddFace(verts, idx, 0, new(-h.X+si,-h.Y+si,+h.Z), new(+h.X-si,-h.Y+si,+h.Z), new(+h.X-si,+h.Y-si,+h.Z), new(-h.X+si,+h.Y-si,+h.Z),  Vector3.UnitZ,  Vector3.UnitX,  Vector3.UnitY);  // +Z
        AddFace(verts, idx, 1, new(+h.X-si,-h.Y+si,-h.Z), new(-h.X+si,-h.Y+si,-h.Z), new(-h.X+si,+h.Y-si,-h.Z), new(+h.X-si,+h.Y-si,-h.Z), -Vector3.UnitZ, -Vector3.UnitX,  Vector3.UnitY);  // -Z
        AddFace(verts, idx, 2, new(-h.X,-h.Y+si,-h.Z+si), new(-h.X,-h.Y+si,+h.Z-si), new(-h.X,+h.Y-si,+h.Z-si), new(-h.X,+h.Y-si,-h.Z+si), -Vector3.UnitX,  Vector3.UnitZ,  Vector3.UnitY);  // -X
        AddFace(verts, idx, 3, new(+h.X,-h.Y+si,+h.Z-si), new(+h.X,-h.Y+si,-h.Z+si), new(+h.X,+h.Y-si,-h.Z+si), new(+h.X,+h.Y-si,+h.Z-si),  Vector3.UnitX, -Vector3.UnitZ,  Vector3.UnitY);  // +X
        AddFace(verts, idx, 4, new(-h.X+si,+h.Y,+h.Z-si), new(+h.X-si,+h.Y,+h.Z-si), new(+h.X-si,+h.Y,-h.Z+si), new(-h.X+si,+h.Y,-h.Z+si),  Vector3.UnitY,  Vector3.UnitX, -Vector3.UnitZ);  // +Y
        AddFace(verts, idx, 5, new(-h.X+si,-h.Y,-h.Z+si), new(+h.X-si,-h.Y,-h.Z+si), new(+h.X-si,-h.Y,+h.Z-si), new(-h.X+si,-h.Y,+h.Z-si), -Vector3.UnitY,  Vector3.UnitX,  Vector3.UnitZ);  // -Y

        var vb = new VertexBuffer(gd, VertexPositionNormalTexture.VertexDeclaration,
                                  24, BufferUsage.WriteOnly);
        vb.SetData(verts);
        var ib = new IndexBuffer(gd, IndexElementSize.ThirtyTwoBits, 36, BufferUsage.WriteOnly);
        ib.SetData(idx);
        return (vb, ib, 12);
    }

    private void DrawStationOrbitRings()
    {
        _effect.LightingEnabled    = false;
        _effect.VertexColorEnabled = true;
        _effect.World              = Matrix.Identity;

        var ringColor = new Color(20, 30, 50, 120);

        foreach (var (station, _) in _stationPositions)
        {
            // Station orbit ring is centred on its parent body's render pos
            DVec3 parentEcliptic = station.OrbitParent != null
                ? station.OrbitParent.GetPosition(_gameTimeSeconds, DVec3.Zero)
                : DVec3.Zero;
            DVec3   parentUniverse = EclipticToGalaxy(parentEcliptic);
            Vector3 parentRender   = _camera.ToRenderSpace(parentUniverse);

            float ringR = (float)(station.OrbitalRadius * Camera3D.RenderScale);
            if (ringR < 0.0001f || ringR > 5_000f) continue;

            _effect.World = Matrix.CreateScale(ringR)
                          * _eclipticRotation
                          * Matrix.CreateTranslation(parentRender);
            DrawRingRaw(ringColor);
        }

        _effect.VertexColorEnabled = false;
        _effect.LightingEnabled    = true;
    }

    // Station dot icons — 3×3 pixel screen-space marker, visible up to 1 million km.
    // Drawn on top of all 3D geometry so stations are always locatable.
    private void DrawStationDots(SpriteBatch sb)
    {
        const float MaxDistRU = 1.0f;   // 1 million km → 1.0 render unit

        var viewProj = Matrix.Multiply(_camera.ViewMatrix, _camera.ProjectionMatrix);
        int w = _gd.Viewport.Width;
        int h = _gd.Viewport.Height;

        foreach (var (_, universePos) in _stationPositions)
        {
            Vector3 renderPos = _camera.ToRenderSpace(universePos);
            if (renderPos.Length() > MaxDistRU) continue;

            Vector4 clip = Vector4.Transform(new Vector4(renderPos, 1f), viewProj);
            if (clip.W <= 0f) continue;

            float sx = ( clip.X / clip.W * 0.5f + 0.5f) * w;
            float sy = (-clip.Y / clip.W * 0.5f + 0.5f) * h;
            if (sx < 0 || sx >= w || sy < 0 || sy >= h) continue;

            sb.Draw(_pixel, new Rectangle((int)sx - 1, (int)sy - 1, 3, 3), new Color(160, 190, 210, 220));
        }
    }

    // ── Targeting HUD ────────────────────────────────────────────────────────

    private void DrawTargetingHUD(SpriteBatch sb)
    {
        var vp       = Matrix.Multiply(_camera.ViewMatrix, _camera.ProjectionMatrix);
        var viewport = _gd.Viewport;

        // Station / body contacts
        foreach (var contact in _targeting.AllContacts)
        {
            Vector2? screen = TargetingSystem.ProjectToScreen(contact.RelativePosition, vp, viewport);
            if (screen == null) continue;

            bool  isTarget = _targeting.CurrentRadarTarget?.Id == contact.Id;
            float dist     = contact.RelativePosition.Length();
            float size     = MathHelper.Clamp(3e6f / dist, 8f, 44f);
            float arm      = size * 0.40f;

            Color bracketColor = isTarget
                ? new Color(0, 220, 220)
                : new Color(100, 100, 100);

            DrawBracket(sb, screen.Value, size, arm, 2, bracketColor);

            if (isTarget)
            {
                string distStr = dist < 1000f
                    ? $"{dist:F0} m"
                    : $"{dist / 1000f:F1} km";
                Vector2 labelPos = screen.Value + new Vector2(-40f, size + 6f);
                FontHelper.Draw(sb, _font, contact.DisplayName, labelPos,                        new Color(0, 220, 220));
                FontHelper.Draw(sb, _font, distStr,             labelPos + new Vector2(0f, 18f), new Color(0, 180, 180));
            }
        }

        // Pad target bracket (green, slightly smaller than station brackets)
        if (_targeting.HasPadTarget && _padDistance > 0.1)
        {
            var pad = _targeting.TargetedPad!;
            DVec3 relPos  = _padWorldPos - _camera.UniversePosition;
            var   rel3    = new Vector3((float)relPos.X, (float)relPos.Y, (float)relPos.Z);
            Vector2? screen = TargetingSystem.ProjectToScreen(rel3, vp, viewport);
            if (screen != null)
            {
                Color padColor  = new Color(60, 220, 90);
                float size      = MathHelper.Clamp(2e6f / (float)_padDistance, 6f, 36f);
                DrawBracket(sb, screen.Value, size, size * 0.40f, 2, padColor);

                string padId  = $"PAD {pad.PadIndex + 1:D2}";
                string distStr = _padDistance < 100.0
                    ? $"{_padDistance:F1} m"
                    : _padDistance < 1000.0
                        ? $"{_padDistance:F0} m"
                        : $"{_padDistance / 1000.0:F1} km";
                Vector2 labelPos = screen.Value + new Vector2(-30f, size + 6f);
                FontHelper.Draw(sb, _font, padId,    labelPos,                        padColor);
                FontHelper.Draw(sb, _font, distStr,  labelPos + new Vector2(0f, 18f), new Color(40, 180, 70));
            }
        }
    }

    // Four L-shaped corner brackets centred on `centre`.
    private void DrawBracket(SpriteBatch sb, Vector2 centre, float size, float arm, int thickness, Color color)
    {
        int s  = (int)size;
        int al = (int)arm;
        int t  = thickness;
        int cx = (int)centre.X;
        int cy = (int)centre.Y;

        // Top-left
        sb.Draw(_pixel, new Rectangle(cx - s,      cy - s,      al, t),  color);
        sb.Draw(_pixel, new Rectangle(cx - s,      cy - s,      t,  al), color);
        // Top-right
        sb.Draw(_pixel, new Rectangle(cx + s - al, cy - s,      al, t),  color);
        sb.Draw(_pixel, new Rectangle(cx + s - t,  cy - s,      t,  al), color);
        // Bottom-left
        sb.Draw(_pixel, new Rectangle(cx - s,      cy + s - t,  al, t),  color);
        sb.Draw(_pixel, new Rectangle(cx - s,      cy + s - al, t,  al), color);
        // Bottom-right
        sb.Draw(_pixel, new Rectangle(cx + s - al, cy + s - t,  al, t),  color);
        sb.Draw(_pixel, new Rectangle(cx + s - t,  cy + s - al, t,  al), color);
    }

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

    // ── Star glow (3D billboard, additive) ───────────────────────────────────

    private void DrawStarGlow3D()
    {
        Vector3 renderPos = _camera.ToRenderSpace(DVec3.Zero);
        if (Vector4.Transform(new Vector4(renderPos, 1f),
                              _camera.ViewMatrix * _camera.ProjectionMatrix).W <= 0f) return;

        float baseRU = StarApparentRadius(renderPos);
        var   right  = _camera.Right;
        var   up     = _camera.Up;

        _effect.TextureEnabled     = true;
        _effect.VertexColorEnabled = true;
        _effect.LightingEnabled    = false;
        _effect.Texture            = _starGlowTex;
        _effect.World              = Matrix.Identity;

        DrawGlowBillboard(renderPos, baseRU * 14f,  right, up, _star.GlowColor * 0.07f);
        DrawGlowBillboard(renderPos, baseRU * 6f,   right, up, _star.GlowColor * 0.28f);
        DrawGlowBillboard(renderPos, baseRU * 2.5f, right, up, _star.GlowColor * 0.65f);
        DrawGlowBillboard(renderPos, baseRU * 1.1f, right, up, Color.White     * 0.90f);

        _effect.TextureEnabled     = false;
        _effect.VertexColorEnabled = false;
    }

    private void DrawGlowBillboard(Vector3 center, float radius, Vector3 right, Vector3 up, Color color)
    {
        if (radius < 0.0001f) return;
        var tl = center + (-right + up) * radius;
        var tr = center + ( right + up) * radius;
        var bl = center + (-right - up) * radius;
        var br = center + ( right - up) * radius;
        _glowVerts[0] = new(tl, color, new Vector2(0, 0));
        _glowVerts[1] = new(tr, color, new Vector2(1, 0));
        _glowVerts[2] = new(bl, color, new Vector2(0, 1));
        _glowVerts[3] = new(tr, color, new Vector2(1, 0));
        _glowVerts[4] = new(br, color, new Vector2(1, 1));
        _glowVerts[5] = new(bl, color, new Vector2(0, 1));
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawUserPrimitives(PrimitiveType.TriangleList, _glowVerts, 0, 2);
        }
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

    // Cubic-falloff radial gradient for nav light / strobe glow — bright centre, soft edge.
    private static Texture2D CreateNavGlowTexture(GraphicsDevice gd, int size = 64)
    {
        var   tex  = new Texture2D(gd, size, size);
        var   data = new Color[size * size];
        float r    = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dist  = MathF.Sqrt((x - r) * (x - r) + (y - r) * (y - r));
            float t     = MathF.Max(0f, 1f - dist / r);
            float alpha = t * t * t;  // cubic: full brightness at centre, zero at rim
            data[y * size + x] = Color.White * alpha;
        }
        tex.SetData(data);
        return tex;
    }

    // Draws additive screen-space glow sprites over all station nav lights and warning strobes.
    // Must be called after DrawStations() so the additive blend brightens visible geometry.
    private void DrawStationGlows(SpriteBatch sb)
    {
        if (_stationPositions.Count == 0) return;

        Matrix   viewProj  = _camera.ViewMatrix * _camera.ProjectionMatrix;
        Viewport viewport  = _gd.Viewport;
        Vector2  texCentre = new(_navGlowTex.Width * 0.5f, _navGlowTex.Height * 0.5f);

        sb.Begin(SpriteSortMode.Deferred, BlendState.Additive);
        foreach (var (station, universePos) in _stationPositions)
        {
            if (!_stationGeometry.TryGetValue(station, out var modules)) continue;
            Vector3 stationRel = (universePos - _camera.UniversePosition).ToVector3(); // metres

            var sysQ   = station.GetOrientation(_gameTimeSeconds);
            var stRotQ = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);

            foreach (var mod in modules)
            {
                foreach (var light in mod.GlowLights)
                {
                    Vector3 relPos   = stationRel + Vector3.Transform(light.WorldPosition, stRotQ);
                    float   distance = relPos.Length();
                    if (distance < 0.1f) continue;

                    Vector2? screen = TargetingSystem.ProjectToScreen(relPos, viewProj, viewport);
                    if (screen == null) continue;

                    float intensity = ComputeGlowIntensity(light);
                    if (intensity < 0.01f) continue;

                    float baseSize = light.Type switch
                    {
                        StationGen.GlowType.NavigationLight => 1200f,
                        StationGen.GlowType.WarningStrobe   => 700f,
                        StationGen.GlowType.AviationWarning => 800f,
                        StationGen.GlowType.AmbientMarker   => 400f,
                        _                                   => 400f,
                    };
                    float size  = MathHelper.Clamp(baseSize / distance, 6f, 140f);
                    float scale = size / _navGlowTex.Width;

                    sb.Draw(_navGlowTex, screen.Value, null,
                            light.Colour * intensity, 0f, texCentre, scale,
                            SpriteEffects.None, 0f);
                }
            }
        }
        sb.End();
    }

    private static float ComputeGlowIntensity(StationLightInfo light)
    {
        if (light.Rate <= 0f) return light.BaseIntensity;
        float t = (float)((GameClock.SimTime * light.Rate + light.Phase) % 1.0);
        return light.Pattern switch
        {
            LightPattern.Strobe    => t < 0.18f ? light.BaseIntensity : 0f,
            LightPattern.SlowPulse => (MathF.Sin(t * MathF.Tau) * 0.5f + 0.5f) * light.BaseIntensity,
            LightPattern.Heartbeat => t < 0.10f ? light.BaseIntensity
                                    : t < 0.22f ? 0f
                                    : t < 0.32f ? light.BaseIntensity * 0.65f
                                    : 0f,
            _ => light.BaseIntensity,
        };
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
                        / (2f * MathF.Tan(MathHelper.ToRadians(60f))); // half of 60°

        float minRenderRadius = StarMinPixels * dist / projScale;
        return System.Math.Max(StarVisualRadius, minRenderRadius);
    }

    private void DrawAtmosphere(OrbitalBody body, DVec3 universePos)
    {
        if (body.AtmosphereType == AtmosphereType.None || body.AtmosphereHeight <= 0) return;
        if (_atmosEffect == null) return;

        Vector3 renderPos = _camera.ToRenderSpace(universePos);
        float   camDist   = renderPos.Length();
        if (camDist > 30_000f) return;

        // Physical atmosphere radius preserving the planet/atmosphere ratio under the
        // per-pixel visual size boost applied to distant planets.
        float physPlanetR  = VisualRadius(body);
        float physAtmosR   = (float)((body.RadiusMeters + body.AtmosphereHeight) * Camera3D.RenderScale);
        float planetRadius = PlanetApparentRadius(body, renderPos);
        float atmosRadius  = physPlanetR > 0f ? physAtmosR * (planetRadius / physPlanetR) : planetRadius;

        // Minimum visual thickness so the glow gradient is visible even for thin atmospheres.
        // drawRadius is derived from shaderAtmosRadius so the billboard always exceeds the fade zone.
        float shaderAtmosRadius = MathF.Max(atmosRadius, planetRadius * 1.05f);
        float drawRadius        = shaderAtmosRadius * 1.15f;

        // Billboard half-size: cover the draw sphere's projected circle from outside,
        // or cover the full sky from inside.
        float billHalf;
        if (camDist <= drawRadius)
        {
            // Camera inside — billboard must subtend > 180°, so use 3× the distance
            // to the planet (or 2× the draw radius if planet is very close).
            billHalf = MathF.Max(2f * drawRadius, camDist * 3f);
        }
        else
        {
            // Exact projected angular radius of the draw sphere, 15 % extra margin.
            float sinHA = drawRadius / camDist;
            float tanHA = sinHA / MathF.Sqrt(1f - sinHA * sinHA);
            billHalf    = camDist * tanHA * 1.15f;
        }

        // Camera-aligned billboard centred at the planet's render-space position.
        // CW winding (TL→TR→BL, TR→BR→BL) — front faces are CW under CullCounterClockwise.
        var     right = _camera.Right;
        var     up    = _camera.Up;
        Vector3 tl    = renderPos + (-right + up) * billHalf;
        Vector3 tr    = renderPos + ( right + up) * billHalf;
        Vector3 bl    = renderPos + (-right - up) * billHalf;
        Vector3 br    = renderPos + ( right - up) * billHalf;

        _atmosQuadVerts[0] = new(tl, Vector2.Zero);
        _atmosQuadVerts[1] = new(tr, Vector2.Zero);
        _atmosQuadVerts[2] = new(bl, Vector2.Zero);
        _atmosQuadVerts[3] = new(tr, Vector2.Zero);
        _atmosQuadVerts[4] = new(br, Vector2.Zero);
        _atmosQuadVerts[5] = new(bl, Vector2.Zero);

        _atmosEffect.Parameters["ViewProjection"].SetValue(_camera.ViewMatrix * _camera.ProjectionMatrix);
        _atmosEffect.Parameters["PlanetCenter"].SetValue(renderPos);
        _atmosEffect.Parameters["PlanetRadius"].SetValue(planetRadius * 0.98f); // slight inset to close gap at limb
        _atmosEffect.Parameters["AtmosRadius"].SetValue(shaderAtmosRadius);
        _atmosEffect.Parameters["AtmosphereColor"].SetValue(body.AtmosphereColor.ToVector3());
        _atmosEffect.Parameters["Opacity"].SetValue(OpacityFor(body.AtmosphereType));
        _atmosEffect.Parameters["LightDirection"].SetValue(_effect.DirectionalLight0.Direction);

        _gd.RasterizerState = RasterizerState.CullNone;
        foreach (var pass in _atmosEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawUserPrimitives(PrimitiveType.TriangleList, _atmosQuadVerts, 0, 2);
        }
        _gd.RasterizerState = RasterizerState.CullCounterClockwise;
    }

    private static float OpacityFor(AtmosphereType type) => type switch
    {
        AtmosphereType.Thin       => 0.45f,
        AtmosphereType.Breathable => 0.65f,
        AtmosphereType.Thick      => 0.85f,
        AtmosphereType.Toxic      => 0.75f,
        AtmosphereType.Corrosive  => 0.95f,
        _                         => 0.65f,
    };

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

    private void DrawCrosshair(SpriteBatch sb)
    {
        int cx  = _gd.Viewport.Width  / 2;
        int cy  = _gd.Viewport.Height / 2;
        int arm = 10;
        int gap = 4;

        // Four arms only — no centre dot (avoids obscuring distant targets)
        sb.Draw(_pixel, new Rectangle(cx - arm - gap, cy, arm, 1), Color.White);
        sb.Draw(_pixel, new Rectangle(cx + gap + 1,   cy, arm, 1), Color.White);
        sb.Draw(_pixel, new Rectangle(cx, cy - arm - gap, 1, arm), Color.White);
        sb.Draw(_pixel, new Rectangle(cx, cy + gap + 1,   1, arm), Color.White);
    }

    private void DrawHUD(SpriteBatch sb)
    {
        int bottom = _gd.Viewport.Height;

        // Relative speed — velocity relative to the reference frame object.
        // Debug mode: camera position delta / dt vs reference.
        // Ship mode: simulation velocity vs reference.
        DVec3 movingVel = _debugCameraMode
            ? _cameraActualVelocity
            : _frameShipSnap?.Velocity ?? DVec3.Zero;
        double relSpeedMs = (movingVel - _refVelocity).Length;

        // Show set fly-speed (scroll-adjusted) in both modes
        if (_debugCameraMode)
        {
            DrawText(sb, $"Set: {Units.FormatSpeed(_camera.MoveSpeedMs)}",
                new Vector2(16, bottom - 98), ColHUDDim, 0.8f);
        }
        else
        {
            DVec3 shipPos    = _frameShipSnap?.Position ?? _camera.UniversePosition;
            double effSpeed  = ComputeShipSpeed(shipPos);
            string setLine   = System.Math.Abs(effSpeed - _shipBaseSpeed) > 0.5
                ? $"Set: {Units.FormatSpeed(_shipBaseSpeed)}  (cap: {Units.FormatSpeed(effSpeed)})"
                : $"Set: {Units.FormatSpeed(_shipBaseSpeed)}";
            DrawText(sb, setLine, new Vector2(16, bottom - 98), ColHUDDim, 0.8f);
        }

        DrawText(sb, $"Speed: {Units.FormatSpeed(relSpeedMs)}  (vs {_refName})",
            new Vector2(16, bottom - 80), ColHUD);

        // Game time
        DrawText(sb, $"T+{Units.FormatTime(_gameTimeSeconds)}", new Vector2(16, bottom - 58), ColHUDDim, 0.8f);

        // Controls hint — changes with mode
        if (_uiMouseMode)
        {
            DrawText(sb, "UI MODE  —  TAB: return to flight",
                new Vector2(16, _gd.Viewport.Height - 30), new Color(80, 160, 220), 0.72f);
        }
        else if (_debugCameraMode)
        {
            DrawText(sb, "DEBUG CAM  —  Mouse: look   WASD: fwd/strafe   RF: up/down   QE: roll   Shift: fast   Ctrl: slow   F11: ship cam   TAB: UI",
                new Vector2(16, _gd.Viewport.Height - 30), new Color(220, 160, 80), 0.72f);
        }
        else
        {
            DrawText(sb, "Mouse: look   WASD: fwd/strafe   QE: roll   RF: up/down   M: system map   N: galaxy map   F11: debug   TAB: UI",
                new Vector2(16, _gd.Viewport.Height - 30), ColHUDDim, 0.72f);
        }
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    private void HandleKeyboard(KeyboardState keys, MouseState mouse)
    {
        bool mPressed    = keys.IsKeyDown(Keys.M)    && !_prevKeys.IsKeyDown(Keys.M);
        bool nPressed    = keys.IsKeyDown(Keys.N)    && !_prevKeys.IsKeyDown(Keys.N);
        bool cPressed    = keys.IsKeyDown(Keys.C)    && !_prevKeys.IsKeyDown(Keys.C);
        bool lPressed    = keys.IsKeyDown(Keys.L)    && !_prevKeys.IsKeyDown(Keys.L);
        bool homePressed = keys.IsKeyDown(Keys.Home) && !_prevKeys.IsKeyDown(Keys.Home);

        if (cPressed)
        {
            var vp = Matrix.Multiply(_camera.ViewMatrix, _camera.ProjectionMatrix);
            _targeting.SelectClosestToReticle(vp, _gd.Viewport);
        }

        if (lPressed)
        {
            DVec3 shipPos = _frameShipSnap?.Position ?? _camera.UniversePosition;
            _targeting.CyclePad(shipPos, _stationPositions, _gameTimeSeconds);
        }

        if (mPressed)
        {
            var (pos, ori) = CaptureShipState();
            _pendingTransition = StateTransition.To(GameStateId.SystemMap,
                new SystemMapPayload(_star, _gameTimeSeconds, CaptureCockpitLayout(), pos, ori,
                    _targeting.NavBodyTarget,
                    _targeting.NavStationTarget));
        }

        if (nPressed)
        {
            var (pos, ori) = CaptureShipState();
            _pendingTransition = StateTransition.To(GameStateId.GalaxyMap,
                new GalaxyMapPayload(_star, _gameTimeSeconds, pos, ori,
                    _targeting.NavBodyTarget,
                    _targeting.NavStationTarget));
        }

        int scroll = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;

        if (_debugCameraMode)
        {
            // Scroll adjusts debug camera fly speed
            if (scroll != 0)
            {
                double factor = scroll > 0 ? 2.0 : 0.5;
                _camera.MoveSpeedMs = System.Math.Clamp(_camera.MoveSpeedMs * factor, 10.0, 1e12);
            }
            // Home — reset debug camera to near-star
            if (homePressed)
                _camera = new Camera3D(new DVec3(0, 0.5e11, 3e11), AspectRatio);
        }
        else
        {
            // Scroll adjusts ship base fly speed (same steps as debug camera)
            if (scroll != 0)
            {
                double factor = scroll > 0 ? 2.0 : 0.5;
                _shipBaseSpeed = System.Math.Clamp(_shipBaseSpeed * factor, 1.0, 1e12);
            }
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
                new Color(255, 220, 80), "*"); // "★"
        }

        var gravEcliptic = new Vector3((float)_gravDirX, (float)_gravDirY, (float)_gravDirZ);
        if (gravEcliptic.LengthSquared() > 0.001f)
        {
            var gravGalaxy = Vector3.TransformNormal(gravEcliptic, _eclipticRotation);
            ball.SetVector("grav", gravGalaxy, new Color(220, 60, 200), "g", dotRadius: 2.0f);
        }

        // Clear station markers from the previous frame so out-of-range ones don't persist.
        for (int i = 0; i < _stationPositions.Count; i++)
            ball.RemoveVector($"station_{i}");

        // Collect all bodies plus stations within 100 km into a ranked list.
        // Sorting by distance lets us assign the largest dot to the closest object.
        var ranked = new List<(string key, Vector3 dir, Color color, double dist)>(
            _bodyPositions.Count + _stationPositions.Count);

        for (int i = 0; i < _bodyPositions.Count; i++)
        {
            var (body, bodyPos) = _bodyPositions[i];
            var toBody = bodyPos - _camera.UniversePosition;
            double dist = toBody.Length;
            if (dist < 1e7) continue;   // skip if somehow coincident

            var dir = new Vector3(
                (float)(toBody.X / dist),
                (float)(toBody.Y / dist),
                (float)(toBody.Z / dist));
            var color = body.BodyType == BodyType.Moon
                ? new Color(100, 130, 150)
                : new Color(100, 200, 160);
            ranked.Add(($"body_{i}", dir, color, dist));
        }

        const double StationRange = 100_000.0; // 100 km
        for (int i = 0; i < _stationPositions.Count; i++)
        {
            var (_, stPos) = _stationPositions[i];
            var toStation  = stPos - _camera.UniversePosition;
            double dist    = toStation.Length;
            if (dist > StationRange || dist < 1.0) continue;

            var dir = new Vector3(
                (float)(toStation.X / dist),
                (float)(toStation.Y / dist),
                (float)(toStation.Z / dist));
            ranked.Add(($"station_{i}", dir, new Color(200, 180, 80), dist));
        }

        // Sort closest-first; rank 0 = largest dot (8 px), decreasing by 1 per rank, floor 3 px.
        ranked.Sort(static (a, b) => a.dist.CompareTo(b.dist));
        for (int i = 0; i < ranked.Count; i++)
        {
            var (key, dir, color, _) = ranked[i];
            ball.SetVector(key, dir, color, "", MathF.Max(3f, 8f - i));
        }
    }

    private void UpdateReferenceFrame(DVec3 shipPos)
    {
        const double StationDist = 25_000.0; // 25 km

        // Priority 1: close station
        foreach (var (station, stPos) in _stationPositions)
        {
            if ((stPos - shipPos).Length < StationDist)
            {
                _refVelocity = EclipticToGalaxy(_system.GetStationVelocity(station, _gameTimeSeconds));
                _refName     = station.Name;
                return;
            }
        }

        // Priority 2: dominant body via gravity vector + gravitational weighting.
        //
        // Score = (m/r²) × cos(angle_between_dir_and_gravity).
        // Top-3 cull by m/r² first so a tiny distant planet behind the star can
        // never beat the star itself on angle alone.
        var gravEcl = new DVec3(_gravDirX, _gravDirY, _gravDirZ);
        if (gravEcl.Length < 0.01)
        {
            _refVelocity = DVec3.Zero;
            _refName     = _star.Name;
            return;
        }
        DVec3 gravGal = EclipticToGalaxy(gravEcl);

        // Top-3 slots by m/r² (named variables — no heap allocation)
        OrbitalBody? t0b = null, t1b = null, t2b = null;
        DVec3 t0p = DVec3.Zero, t1p = DVec3.Zero, t2p = DVec3.Zero;
        double t0w = 0, t1w = 0, t2w = 0;

        void Keep(OrbitalBody? body, DVec3 pos, double w)
        {
            if      (w > t0w) { t2b=t1b; t2p=t1p; t2w=t1w; t1b=t0b; t1p=t0p; t1w=t0w; t0b=body; t0p=pos; t0w=w; }
            else if (w > t1w) { t2b=t1b; t2p=t1p; t2w=t1w; t1b=body; t1p=pos; t1w=w; }
            else if (w > t2w) { t2b=body; t2p=pos; t2w=w; }
        }

        double starDist = shipPos.Length;
        if (starDist > 100.0) Keep(null, DVec3.Zero, _star.MassKg / (starDist * starDist));
        foreach (var (body, pos) in _bodyPositions)
        {
            double d = (pos - shipPos).Length;
            if (d > 100.0) Keep(body, pos, body.MassKg / (d * d));
        }

        OrbitalBody? bestBody = null; DVec3 bestPos = DVec3.Zero;
        double bestScore = 0.0; double winCos = 1.0;

        void Score(OrbitalBody? body, DVec3 pos, double w)
        {
            if (w == 0.0) return;
            DVec3 to = pos - shipPos; double d = to.Length;
            if (d < 100.0) return;
            var dir = new DVec3(to.X / d, to.Y / d, to.Z / d);
            double cos = dir.X * gravGal.X + dir.Y * gravGal.Y + dir.Z * gravGal.Z;
            if (cos <= 0.0) return;
            double s = w * cos;
            if (s > bestScore) { bestScore = s; winCos = cos; bestBody = body; bestPos = pos; }
        }

        Score(t0b, t0p, t0w); Score(t1b, t1p, t1w); Score(t2b, t2p, t2w);

        DVec3 domVelocity; string domName;
        if (bestBody == null)
        {
            domVelocity = DVec3.Zero;
            domName     = _star.Name;
        }
        else
        {
            domVelocity = GetBodyGalaxyVelocity(bestBody, _gameTimeSeconds);
            domName     = bestBody.Name;
        }

        // Blend: fully locked at ≤ 45°, linear fade to 0 at 90°
        double angle   = System.Math.Acos(System.Math.Clamp(winCos, -1.0, 1.0));
        double lockRad = System.Math.PI / 4.0;
        double zeroRad = System.Math.PI / 2.0;
        double blend   = angle <= lockRad ? 1.0
                       : angle <  zeroRad ? 1.0 - (angle - lockRad) / (zeroRad - lockRad)
                       : 0.0;

        // In atmosphere mode, skip blend so ground-relative drag uses the full orbital velocity
        _refVelocity = _simulation.CurrentFlightMode == FlightMode.Atmosphere
            ? domVelocity
            : domVelocity * blend;
        _refName = domName;
    }

    // Returns the galaxy-space position of a planet or moon at the given game time.
    private DVec3 GetBodyGalaxyPosition(OrbitalBody body, double gameTime)
    {
        foreach (var planet in _system.Planets)
        {
            if (ReferenceEquals(planet, body))
                return EclipticToGalaxy(planet.GetPosition(gameTime, DVec3.Zero));
            foreach (var moon in planet.Children)
            {
                if (ReferenceEquals(moon, body))
                    return EclipticToGalaxy(moon.GetPosition(gameTime, planet.GetPosition(gameTime, DVec3.Zero)));
            }
        }
        return EclipticToGalaxy(body.GetPosition(gameTime, DVec3.Zero));
    }

    // Returns the analytically computed galaxy-space velocity of a planet or moon.
    private DVec3 GetBodyGalaxyVelocity(OrbitalBody body, double gameTime)
    {
        foreach (var planet in _system.Planets)
        {
            if (ReferenceEquals(planet, body))
                return EclipticToGalaxy(PlanetVelocityEcl(planet, gameTime));
            foreach (var moon in planet.Children)
            {
                if (ReferenceEquals(moon, body))
                {
                    var pv = PlanetVelocityEcl(planet, gameTime);
                    var mv = OrbitalVelocityEcl(gameTime, moon.Period, moon.PhaseOffset, moon.OrbitalRadius);
                    return EclipticToGalaxy(pv + mv);
                }
            }
        }
        return EclipticToGalaxy(OrbitalVelocityEcl(gameTime, body.Period, body.PhaseOffset, body.OrbitalRadius));
    }

    // Keplerian velocity for planets, circular fallback for legacy bodies.
    private static DVec3 PlanetVelocityEcl(OrbitalBody planet, double gameTime)
        => planet.SemiMajorAxis > 0.0 && planet.ParentMassKg > 0.0
            ? planet.ComputeVelocity(gameTime, Units.G * planet.ParentMassKg, DVec3.Zero)
            : OrbitalVelocityEcl(gameTime, planet.Period, planet.PhaseOffset, planet.OrbitalRadius);

    private static DVec3 OrbitalVelocityEcl(double gameTime, double period, double phaseOffset, double radius)
    {
        double angle = DMath.OrbitalAngle(gameTime, period, phaseOffset);
        double omega = 2.0 * System.Math.PI / period;
        return new DVec3(-System.Math.Sin(angle) * radius * omega, 0.0, System.Math.Cos(angle) * radius * omega);
    }

    // Pushes planets, moons, and stations into TargetingSystem each frame so the
    // C-key and click-to-target handlers have contacts to work with.
    // Positions are already galaxy-space from the current frame's _bodyPositions /
    // _stationPositions.  RelativePosition is the vector from camera to contact in
    // metres (float precision is fine for screen-space projection).
    private void FeedRadarContacts()
    {
        DVec3 camPos = _camera.UniversePosition;

        // Remove any stale body:* contacts (planets/moons are not shown on radar)
        var staleBodyIds = new List<string>();
        foreach (var id in _radarContactIds)
            if (id.StartsWith("body:", StringComparison.Ordinal))
                staleBodyIds.Add(id);
        foreach (var id in staleBodyIds)
        {
            _targeting.OnContactLost(id);
            _cockpitDirBall?.RemoveVector($"radar_{id}");
            _radarContactIds.Remove(id);
        }

        foreach (var (station, galaxyPos) in _stationPositions)
        {
            string id      = $"station:{station.Name}";
            DVec3  del     = galaxyPos - camPos;
            var    contact = new RadarContact(
                id, station.Name,
                new Vector3((float)del.X, (float)del.Y, (float)del.Z),
                Vector3.Zero, ContactType.Station);
            _targeting.OnContactUpdated(contact);
            UpdateCockpitDirBallContact(contact);
            _radarContactIds.Add(id);
        }

        // TODO: remove test containers — debug contacts for radar testing
        foreach (var tc in _testContainers)
        {
            DVec3 stPos = DVec3.Zero;
            foreach (var (s, sPos) in _stationPositions)
                if (ReferenceEquals(s, tc.Station)) { stPos = sPos; break; }
            DVec3 pos   = stPos + tc.Offset;
            DVec3 del   = pos - camPos;
            var   contact = new RadarContact(
                tc.Id, tc.Name,
                new Vector3((float)del.X, (float)del.Y, (float)del.Z),
                Vector3.Zero, ContactType.Debris);
            _targeting.OnContactUpdated(contact);
            UpdateCockpitDirBallContact(contact);
            _radarContactIds.Add(tc.Id);
        }
    }

    // TODO: remove SpawnTestContainers — debug helper for radar testing
    private void SpawnTestContainers()
    {
        Color[] palette = [
            new Color(180, 60, 50), new Color(50, 100, 180),
            new Color(60, 160, 80), new Color(180, 140, 50),
        ];
        int globalIdx = 0;
        foreach (var (station, _) in _stationPositions)
        {
            int seed  = station.Name.GetHashCode() ^ (int)(_star.GalacticPos.X * 1000.0);
            var rng   = new Inferior.Core.Random.SeededRandom(seed);
            int count = rng.NextInt(3, 7);  // 3–6 containers per station

            for (int i = 0; i < count; i++)
            {
                double angle  = rng.NextDouble() * System.Math.Tau;
                double dist   = 20.0 + rng.NextDouble() * 480.0;  // 20–500 m from station
                double elevM  = (rng.NextDouble() - 0.5) * 60.0;  // ±30 m vertical
                var    offset = new DVec3(System.Math.Cos(angle) * dist, elevM, System.Math.Sin(angle) * dist);

                var sc = Inferior.Game.Containers.ShippingContainerFactory.Generate(
                    rng.Pick(palette), rng.NextFloat(0f, 0.6f), rng.NextInt(int.MinValue, int.MaxValue));

                var vb = new VertexBuffer(_gd, VertexPositionColorTexture.VertexDeclaration,
                                          sc.Vertices.Length, BufferUsage.WriteOnly);
                vb.SetData(sc.Vertices);
                var ib = new IndexBuffer(_gd, IndexElementSize.SixteenBits,
                                         sc.Indices.Length, BufferUsage.WriteOnly);
                ib.SetData(sc.Indices);

                _testContainers.Add(new TestContainerEntry
                {
                    Id       = $"ctn:{globalIdx}",
                    Name     = $"Ctn-{globalIdx:D2}",
                    Station  = station,
                    Offset   = offset,
                    Vb       = vb,
                    Ib       = ib,
                    TriCount = sc.Indices.Length / 3,
                });
                globalIdx++;
            }
        }
    }

    private void UpdateCockpitDirBallContact(RadarContact c)
    {
        if (_cockpitDirBall == null) return;
        float len = c.RelativePosition.Length();
        if (len < 1f) return;
        var dir = c.RelativePosition / len;
        var col = c.Type switch
        {
            ContactType.Station => new Color(80,  200, 140),
            ContactType.Ship    => new Color(220,  80,  80),
            _                   => new Color(120, 120, 120),
        };
        _cockpitDirBall.SetVector($"radar_{c.Id}", dir, col);
    }

    private void UpdateTargetingUI()
    {
        if (_targetingDirBall == null) return;
        _targetingDirBall.SetOrientation(_camera.Forward, _camera.Right, _camera.Up);

        var tc = _ui?.Theme;

        // Radar target (ship/station contact)
        if (_targeting.HasRadarTarget)
        {
            var contact = _targeting.CurrentRadarTarget!.Value;
            float distM = contact.RelativePosition.Length();
            var col = tc?.TargetShip ?? new Color(0, 220, 220);
            var dir = Vector3.Normalize(contact.RelativePosition);
            _targetingDirBall.SetVector("ship", dir, col, "T");
            if (_targetLineShip != null)
                _targetLineShip.Text = $"Target: {contact.DisplayName} ({Units.FormatDistance(distM)})";
        }
        else
        {
            _targetingDirBall.RemoveVector("ship");
            if (_targetLineShip != null)
                _targetLineShip.Text = "Target: None";
        }

        // Nav target (yellow)
        if (_targeting.HasNavTarget)
        {
            var d    = _targeting.NavTargetDirection;
            var col  = tc?.TargetNav ?? new Color(255, 200, 50);
            _targetingDirBall.SetVector("nav", new Vector3((float)d.X, (float)d.Y, (float)d.Z), col, "N");
            if (_targetLineNav != null)
                _targetLineNav.Text = $"Nav: {_targeting.NavTargetName} ({Units.FormatDistance(_targeting.NavTargetDistance)})";
        }
        else
        {
            _targetingDirBall.RemoveVector("nav");
            if (_targetLineNav != null) _targetLineNav.Text = "Nav: None";
        }

        // Hyperspace target (blue)
        if (_targeting.HasHyperspaceTarget)
        {
            var d   = _targeting.HyperspaceTargetDirection;
            var col = tc?.TargetHyp ?? new Color(80, 160, 255);
            _targetingDirBall.SetVector("hyp", new Vector3((float)d.X, (float)d.Y, (float)d.Z), col, "H");
            if (_targetLineHyp != null)
                _targetLineHyp.Text = $"Hyp: {_targeting.HyperspaceTargetName} ({_targeting.HyperspaceTargetDistanceLY:F1} ly)";
        }
        else
        {
            _targetingDirBall.RemoveVector("hyp");
            if (_targetLineHyp != null) _targetLineHyp.Text = "Hyp: None";
        }

        // Pad target (green)
        if (_targeting.HasPadTarget && _padDirection.Length > 0.5)
        {
            var pad    = _targeting.TargetedPad!;
            string distStr = _padDistance < 100.0
                ? $"{_padDistance:F1} m"
                : _padDistance < 1000.0
                    ? $"{_padDistance:F0} m"
                    : $"{_padDistance / 1000.0:F1} km";
            _targetingDirBall.SetVector("pad",
                new Vector3((float)_padDirection.X, (float)_padDirection.Y, (float)_padDirection.Z),
                new Color(60, 220, 90), "P");
        }
        else
        {
            _targetingDirBall.RemoveVector("pad");
        }
    }

    private void UpdateRadarDisplay()
    {
        if (_radarDisplay == null) return;
        _radarDisplay.Contacts        = _targeting.AllContacts;
        _radarDisplay.SelectedContact = _targeting.CurrentRadarTarget;

        var snap = _frameShipSnap;
        _radarDisplay.LocalFrameSpeedMs = snap != null ? (float)snap.Velocity.Length : 0f;

        // Ship-local orientation axes so the radar can project contacts correctly.
        if (snap != null)
        {
            _radarDisplay.ShipForward = new Vector3((float)snap.Forward.X, (float)snap.Forward.Y, (float)snap.Forward.Z);
            _radarDisplay.ShipUp      = new Vector3((float)snap.Up.X,      (float)snap.Up.Y,      (float)snap.Up.Z);
            _radarDisplay.ShipRight   = Vector3.Cross(_radarDisplay.ShipForward, _radarDisplay.ShipUp);
        }

        // Approach speed — closing rate along selected contact direction
        float approachMs = 0f;
        if (_targeting.HasRadarTarget)
        {
            var c    = _targeting.CurrentRadarTarget!.Value;
            float rlen = c.RelativePosition.Length();
            if (rlen > 1f)
                approachMs = -Vector3.Dot(c.RelativeVelocity, c.RelativePosition / rlen);
        }
        _radarDisplay.ApproachSpeedMs = approachMs;

        // PWR LED — radar is always active while in SystemSpace
        _radarDisplay.PwrLed = true;
    }

    private void UpdateLandingRadar()
    {
        if (_landingRadar == null) return;

        var navStation = _targeting.NavStationTarget;
        if (navStation != null)
        {
            DVec3 relGalaxy   = (_frameShipSnap?.Position ?? _camera.UniversePosition)
                              - _targeting.NavTargetPosition;
            DVec3 relEcliptic = GalaxyToEcliptic(relGalaxy);
            _landingRadar.HasStation     = true;
            _landingRadar.StationName    = navStation.Name;
            _landingRadar.DistanceMeters = _targeting.NavTargetDistance;
            _landingRadar.RelX           = (float)relEcliptic.X;
            _landingRadar.RelZ           = (float)relEcliptic.Z;
        }
        else
        {
            _landingRadar.HasStation  = false;
            _landingRadar.StationName = "";
        }
    }

    // Computes pad world position from current station orbit + orientation, then
    // publishes Docking.* topics to the Instruments bus.
    private void UpdatePadTargetPosition()
    {
        if (!_targeting.HasPadTarget)
        {
            DataBus.Instruments.Publish(Topics.Docking.PadTargeted, 0.0);
            _padWorldPos  = DVec3.Zero;
            _padDistance  = 0.0;
            _padDirection = DVec3.Zero;
            _simulation.SetPadTarget(null);
            return;
        }

        var station = _targeting.TargetedPadStation!;
        var pad     = _targeting.TargetedPad!;

        // Find station's current world position
        DVec3 stationPos = DVec3.Zero;
        foreach (var (s, pos) in _stationPositions)
            if (s == station) { stationPos = pos; break; }

        // Transform pad local position and normal by station orientation
        Quaternion ori       = station.GetOrientation(_gameTimeSeconds);
        var localPos         = new Vector3((float)pad.LocalPosition.X, (float)pad.LocalPosition.Y, (float)pad.LocalPosition.Z);
        var localNrm         = new Vector3((float)pad.LocalNormal.X,   (float)pad.LocalNormal.Y,   (float)pad.LocalNormal.Z);
        var offset           = Vector3.Transform(localPos, ori);
        var worldNrmV        = Vector3.Normalize(Vector3.TransformNormal(localNrm, Matrix.CreateFromQuaternion(ori)));
        _padWorldPos         = stationPos + new DVec3(offset.X, offset.Y, offset.Z);
        DVec3 worldNormal    = new DVec3(worldNrmV.X, worldNrmV.Y, worldNrmV.Z);

        DVec3 shipPos        = _frameShipSnap?.Position ?? _camera.UniversePosition;
        DVec3 delta          = _padWorldPos - shipPos;
        _padDistance         = delta.Length;
        _padDirection        = _padDistance > 1.0 ? delta * (1.0 / _padDistance) : DVec3.Zero;

        // Rotate the station-local forward axis (stored by StationGenerator to match the visual arrow)
        // to world space via the station orientation quaternion.
        var localFwdV = new Vector3((float)pad.LocalForward.X, (float)pad.LocalForward.Y, (float)pad.LocalForward.Z);
        var worldFwdV = Vector3.Normalize(Vector3.TransformNormal(localFwdV, Matrix.CreateFromQuaternion(ori)));
        DVec3 padForward = new DVec3(worldFwdV.X, worldFwdV.Y, worldFwdV.Z);

        // Push to sim thread for LandingSupportSystem
        var padData = new LandingPadData(
            WorldPosition: _padWorldPos,
            WorldNormal:   worldNormal,
            ForwardAxis:   padForward,
            PadSize:       pad.PadSize,
            BayId:         $"PAD {pad.PadIndex + 1:D2}",
            StationName:   station.Name);
        _simulation.SetPadTarget(padData);

        // Publish to Instruments bus so Step 3 docking instrument can subscribe
        DataBus.Instruments.Publish(Topics.Docking.PadTargeted,   1.0);
        DataBus.Instruments.Publish(Topics.Docking.PadDistance,   _padDistance);
        DataBus.Instruments.Publish(Topics.Docking.PadDirectionX, _padDirection.X);
        DataBus.Instruments.Publish(Topics.Docking.PadDirectionY, _padDirection.Y);
        DataBus.Instruments.Publish(Topics.Docking.PadDirectionZ, _padDirection.Z);
        DataBus.Instruments.Publish(Topics.Docking.PadSizeClass,  pad.PadSize == Galaxy.PadSize.Large ? 1.0 : 0.0);
    }

    // ── Ecliptic tilt ─────────────────────────────────────────────────────────

    private void ComputeEclipticRotation()
    {
        var tiltAxis = new Vector3(
            MathF.Cos(_system.EclipticTiltAzimuthRadians),
            0f,
            MathF.Sin(_system.EclipticTiltAzimuthRadians));
        _eclipticRotation = Matrix.CreateFromAxisAngle(tiltAxis, _system.EclipticTiltRadians);

        // Build double-precision rotation from the same axis/angle to avoid float
        // quantisation (~9 km at 1 AU) when transforming large universe coordinates.
        // Rodrigues formula for axis (ux, 0, uz) — uy = 0 by construction (tilt axis is horizontal).
        double ux  = tiltAxis.X, uz = tiltAxis.Z;
        double a   = _system.EclipticTiltRadians;
        double cos = System.Math.Cos(a), sin = System.Math.Sin(a), ic = 1.0 - cos;
        _er00 = cos + ux * ux * ic;  _er01 = -uz * sin;       _er02 = ux * uz * ic;
        _er10 = uz * sin;            _er11 = cos;              _er12 = -ux * sin;
        _er20 = ux * uz * ic;        _er21 = ux * sin;         _er22 = cos + uz * uz * ic;
    }

    // Full double-precision ecliptic-to-galaxy rotation.
    private DVec3 EclipticToGalaxy(DVec3 pos) => new(
        _er00 * pos.X + _er01 * pos.Y + _er02 * pos.Z,
        _er10 * pos.X + _er11 * pos.Y + _er12 * pos.Z,
        _er20 * pos.X + _er21 * pos.Y + _er22 * pos.Z);

    // Inverse rotation (transpose of the rotation matrix — orthogonal matrix property).
    // Used to convert galaxy-space relative positions back to ecliptic plane for the landing radar.
    private DVec3 GalaxyToEcliptic(DVec3 pos) => new(
        _er00 * pos.X + _er10 * pos.Y + _er20 * pos.Z,
        _er01 * pos.X + _er11 * pos.Y + _er21 * pos.Z,
        _er02 * pos.X + _er12 * pos.Y + _er22 * pos.Z);

    // ── Ship ──────────────────────────────────────────────────────────────────
    // _ship persists between OnEnter/OnExit calls — only recreated for new spawns.

    private Ship?             _ship;
    private ShieldComponent?  _shield;

    private void SpawnShip(DVec3 startPos, Quaternion orientation)
    {
        var ship = new Ship
        {
            Position    = startPos,
            SizeClass   = ShipSizeClass.Medium,
            MoveSpeedMs = 5e9,
        };
        ship.SetOrientation(orientation);

        var reactor = new PowerReactor("Reactor", maxPower: 120e6, outputCapacitorJ: 50e6);

        var bus = new PowerBus("MainBus", capacityJ: 10e6, maxPower: 120e6);
        bus.ConnectSource(reactor.OutputCapacitor);

        var powerManager = new PowerPriorityManager();
        powerManager.AttachToBus(bus);

        _shield = new ShieldComponent("Shield", maxShieldJ: 5e6, chargeRateW: 500e3);

        // Connector wires shield to bus via the priority manager, capping at 600 kW
        var shieldConnector = new ConnectorComponent("ShieldConnector", "MainBus", "Shield", maxPower: 600e3);
        shieldConnector.Connect(powerManager, _shield.DemandWatts, _shield.ReceivePower);

        var heatsink = new HyperspaceHeatSink("HeatSink",
            capacityJ:      50_000_000,   // 50 MJ thermal mass
            transferRate:   800_000,      // 800 kW max inflow from coolant
            heatDissipation: 500_000);    // 500 kW to hyperspace at full load

        var coolant = new CoolantSystem("Coolant",
            heatFlowPerComponent: 150_000,  // 150 kW max per node
            coolantLeakage:       0.0002);  // ~0.02 %/s
        coolant.AttachHeatSink(heatsink);
        coolant.RegisterThermalNode(reactor.ThermalNode!);
        coolant.RegisterThermalNode(_shield.ThermalNode!);

        ship.Install(reactor);
        ship.Install(bus);
        ship.Install(powerManager);
        ship.Install(_shield);
        ship.Install(shieldConnector);
        ship.Install(heatsink);
        ship.Install(coolant);
        _shield.PowerOn = false;  // starts off — player enables via SYS panel

        _ship = ship;
        _simulation.SetShip(ship);
    }

    // Returns the quaternion that rotates the camera's default forward (-UnitZ) to face `dir`.
    private static Quaternion QuatLookAt(DVec3 dir)
    {
        var v = Vector3.Normalize(new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z));
        Vector3 cross = Vector3.Cross(-Vector3.UnitZ, v);
        float   dot   = Vector3.Dot(-Vector3.UnitZ, v);
        if (cross.LengthSquared() < 1e-10f)
            return dot > 0f ? Quaternion.Identity
                            : Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        return Quaternion.CreateFromAxisAngle(Vector3.Normalize(cross),
                   MathF.Acos(MathHelper.Clamp(dot, -1f, 1f)));
    }

    private const float MouseSensitivity = 0.0012f;

    private PlayerInput BuildShipInput(MouseState mouse, KeyboardState keys)
    {
        // Rotation — cursor is locked to window centre each frame; accumulate delta from centre.
        int    cx         = _gd.Viewport.Width  / 2;
        int    cy         = _gd.Viewport.Height / 2;
        double yawInput   = -(mouse.X - cx) * MouseSensitivity;
        double pitchInput = -(mouse.Y - cy) * MouseSensitivity;

        // Thrust — keyboard axes, -1..1
        // W/S = fwd/back  A/D = strafe  R/F = up/down  Q/E = roll
        double fwd  = (keys.IsKeyDown(Keys.W) ? 1.0 : 0.0) - (keys.IsKeyDown(Keys.S) ? 1.0 : 0.0);
        double lat  = (keys.IsKeyDown(Keys.D) ? 1.0 : 0.0) - (keys.IsKeyDown(Keys.A) ? 1.0 : 0.0);
        double vert = (keys.IsKeyDown(Keys.R) ? 1.0 : 0.0) - (keys.IsKeyDown(Keys.F) ? 1.0 : 0.0);
        double roll = (keys.IsKeyDown(Keys.E) ? 1.0 : 0.0) - (keys.IsKeyDown(Keys.Q) ? 1.0 : 0.0);

        // V = Flight Assist toggle, G = Glide Mode toggle (rising-edge sent to sim)
        bool faToggle    = keys.IsKeyDown(Keys.V) && !_prevKeys.IsKeyDown(Keys.V);
        bool glideToggle = keys.IsKeyDown(Keys.G) && !_prevKeys.IsKeyDown(Keys.G);

        return new PlayerInput(fwd, lat, vert, roll, pitchInput, yawInput, false,
            FlightAssistToggle: faToggle, GlideModeToggle: glideToggle);
    }

    // ── UI mode ───────────────────────────────────────────────────────────────

    private void ApplyUiMode(bool active)
    {
        if (_rightPanel != null) _rightPanel.UiModeActive = active;
        if (_leftPanel  != null) _leftPanel.UiModeActive  = active;
        // CockpitRail is always interactable — peek strip tabs and toggle work in all modes.
    }

    // ── Cockpit layout ────────────────────────────────────────────────────────

    private (DVec3? pos, Quaternion? ori) CaptureShipState()
    {
        var snap = _simulation.ShipState;
        if (snap == null) return (null, null);
        return (snap.Position, snap.Orientation);
    }

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

    // ── Near clip ─────────────────────────────────────────────────────────────

    // Near clip is proportional to the distance from the camera to the nearest station surface.
    // This keeps it large (10 km) in open space — good z-precision at system scale —
    // and shrinks it as you approach a station, reaching ~1 mm at hull contact.
    // Far-distance z-precision degrades when up-close, but that's acceptable: nothing
    // at AU-scale distances competes for depth buffer precision when you're docking.
    private float ComputeNearClip()
    {
        DVec3  camPos        = _camera.UniversePosition;
        double minSurf       = double.MaxValue;
        double nearestRadius = 250.0; // fallback: smallest station size

        foreach (var (station, stPos) in _stationPositions)
        {
            double r    = StationPhysicalRadius(station);
            double dist = (stPos - camPos).Length;
            double surf = System.Math.Max(dist - r, 0.0);
            if (surf < minSurf) { minSurf = surf; nearestRadius = r; }
        }

        if (minSurf < 500_000.0) // within 500 km of any station surface
        {
            // Floor the reference distance at the station's own radius so Z precision
            // is preserved even when flying through the interior (where surfDist = 0
            // would otherwise collapse the depth buffer to a single value).
            double refDist    = System.Math.Max(minSurf, nearestRadius);
            double nearMeters = refDist * 0.001;
            return (float)(nearMeters * Camera3D.RenderScale);
        }

        return 0.00001f; // default: 10 km near clip
    }

    // ── Proximity speed scale ─────────────────────────────────────────────────
    //
    // Two independent zones — star and bodies — each with three knobs:
    //   FarDist  : surface distance where scaling begins (full free speed beyond this)
    //   NearDist : surface distance where scaling is fully applied
    //   MinScale : multiplier at NearDist — expressed as (target m/s) / (max scroll step)
    //              so "top step → X m/s at NearDist" is readable at a glance.
    //
    // The most restrictive zone wins each frame.

    // Star — large zone, still fast enough near the star to orbit / escape
    private const double StarProxFarDist  = 2.25e11;        // 1.5 AU
    private const double StarProxNearDist = 1e10;            // 10,000,000 km
    private const double StarProxMinScale = 1e5 / 1e12;     // top step → 100 km/s

    // Planets & moons — zone around each body
    // Floor is 100 km/s at 1 km from surface. Atmosphere state takes over well above that,
    // so this only matters for airless bodies; atmospheric bodies transition at ~80 km altitude.
    private const double BodyProxFarDist  = 5e8;               // 500,000 km
    private const double BodyProxNearDist = 1e3;               // 1 km
    private const double BodyProxMinScale = 1e5 / 1e12;        // top step → 100 km/s

    // Stations — flat cap within 10 km of any station surface
    private const double StationProxCapDist  = 1e4;            // 10 km
    private const double StationProxMinScale = 2000.0 / 1e12;  // top step → 2000 m/s

    private double ComputeProximityScale()
    {
        // Star.RadiusMeters is double-converted in generation, so use the visual render
        // radius instead — StarVisualRadius=8 render units = 8/RenderScale = 8e9 m.
        const double StarVisualRadiusMeters = 8.0 / Camera3D.RenderScale;
        double starSurf = System.Math.Max(_camera.UniversePosition.Length - StarVisualRadiusMeters, 0.0);

        double starScale = ScaleForDist(starSurf, StarProxNearDist, StarProxFarDist, StarProxMinScale);

        double bodyScale = 1.0;
        foreach (var (body, pos) in _bodyPositions)
        {
            double surf = System.Math.Max((_camera.UniversePosition - pos).Length - body.RadiusMeters, 0.0);
            double s    = ScaleForDist(surf, BodyProxNearDist, BodyProxFarDist, BodyProxMinScale);
            if (s < bodyScale) bodyScale = s;
        }

        double stationScale = 1.0;
        foreach (var (station, pos) in _stationPositions)
        {
            double r    = StationPhysicalRadius(station);
            double surf = System.Math.Max((_camera.UniversePosition - pos).Length - r, 0.0);
            if (surf <= StationProxCapDist) stationScale = StationProxMinScale;
        }

        return System.Math.Min(System.Math.Min(starScale, bodyScale), stationScale);
    }

    // Ship speed uses the same star/body proximity zones as the camera, but the station
    // zone is a hard cap (2000 m/s max) rather than a proportional scale — so the ship
    // always has a usable docking speed regardless of the current scroll step.
    private double ComputeShipSpeed(DVec3 shipPos)
    {
        const double StarVisualRadiusMeters = 8.0 / Camera3D.RenderScale;
        double starSurf  = System.Math.Max(shipPos.Length - StarVisualRadiusMeters, 0.0);
        double starScale = ScaleForDist(starSurf, StarProxNearDist, StarProxFarDist, StarProxMinScale);

        double bodyScale = 1.0;
        foreach (var (body, pos) in _bodyPositions)
        {
            double surf = System.Math.Max((shipPos - pos).Length - body.RadiusMeters, 0.0);
            double s    = ScaleForDist(surf, BodyProxNearDist, BodyProxFarDist, BodyProxMinScale);
            if (s < bodyScale) bodyScale = s;
        }

        double speed = _shipBaseSpeed * System.Math.Min(starScale, bodyScale);

        // Hard cap near stations — independent of scroll step
        foreach (var (station, pos) in _stationPositions)
        {
            double r    = StationPhysicalRadius(station);
            double surf = System.Math.Max((shipPos - pos).Length - r, 0.0);
            if (surf <= StationProxCapDist)
                speed = System.Math.Min(speed, 2000.0);
        }

        return speed;
    }

    private static double ScaleForDist(double surfDist, double nearDist, double farDist, double minScale)
    {
        if (surfDist >= farDist)  return 1.0;
        if (surfDist <= nearDist) return minScale;

        double t = System.Math.Log(surfDist / nearDist)
                 / System.Math.Log(farDist  / nearDist);
        t = t * t * (3.0 - 2.0 * t); // smoothstep

        return System.Math.Exp(t * System.Math.Log(1.0 / minScale)) * minScale;
    }

    private const float PlanetVisualScale = 1f;

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
        => FontHelper.Draw(sb, _font, text, pos, color, scale);

    private void DrawRect(SpriteBatch sb, Rectangle rect, Color color)
        => sb.Draw(_pixel, rect, color);

    private void DrawRectBorder(SpriteBatch sb, Rectangle rect, Color color, int thickness = 1)
    {
        sb.Draw(_pixel, new Rectangle(rect.Left,  rect.Top,              rect.Width, thickness), color);
        sb.Draw(_pixel, new Rectangle(rect.Left,  rect.Bottom-thickness, rect.Width, thickness), color);
        sb.Draw(_pixel, new Rectangle(rect.Left,  rect.Top,  thickness,  rect.Height),           color);
        sb.Draw(_pixel, new Rectangle(rect.Right-thickness, rect.Top, thickness, rect.Height),   color);
    }

    // TODO: remove — debug container entry for radar testing
    private sealed class TestContainerEntry
    {
        public required string         Id       { get; init; }
        public required string         Name     { get; init; }
        public required Galaxy.Station Station  { get; init; }
        public required DVec3          Offset   { get; init; }
        public required VertexBuffer   Vb       { get; init; }
        public required IndexBuffer    Ib       { get; init; }
        public required int            TriCount { get; init; }
    }
}
