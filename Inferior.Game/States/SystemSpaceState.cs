using Inferior.Core;
using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Core.Simulation;
using Inferior.Galaxy;
using Inferior.Game.Hyperspace;
using Inferior.Game.StationGen;
using Inferior.Game.UI;
using Inferior.Gameplay;
using Inferior.Gameplay.Components;
using Inferior.Gameplay.Components.Power;
using Inferior.Gameplay.Sensors;
using Inferior.Gameplay.Ship;
using Inferior.Rendering;
using Inferior.UI;
using Inferior.UI.Controls;
using Inferior.UI.Controls.Cockpit;
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
public sealed partial class SystemSpaceState : GameState
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
    private VertexPositionColor[]       _skyboxPoints    = [];  // PointList — one vertex per star
    private VertexPositionColor[]       _skyboxGlowVerts = [];  // TriangleList — tiny quads for bright/near stars
    private (Vector3 pos, Star star)[]  _targetableStars = [];  // stars ≤1000 ly — hittable from cursor

    // Skybox targeting state
    private Star?   _hoveredSkyboxStar;   // star under cursor this frame (UI mode only)
    private Star?   _lockedSkyboxStar;    // currently selected hyperspace-target star
    private Vector2 _uiCursorScreen;      // cached cursor position for overlay drawing

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
    // Per-planet checkerboard sphere meshes (VertexPositionColor, pre-baked lighting)
    private readonly Dictionary<OrbitalBody, (VertexBuffer vb, IndexBuffer ib, int triCount)> _planetSpheres = [];

    // ── Container rendering ───────────────────────────────────────────────────
    // One shared mesh (all containers identical geometry; colour from lock grade per draw call).
    private MeshRenderer?  _meshRenderer;
    private VertexBuffer?  _containerVb;
    private IndexBuffer?   _containerIb;

    // ── Ship mesh (three components, built once per session entry) ────────────
    private VertexBuffer? _shipHullVb,    _shipNacelleVb,    _shipPylonVb;
    private IndexBuffer?  _shipHullIb,    _shipNacelleIb,    _shipPylonIb;

    // ── UI ────────────────────────────────────────────────────────────────────
    private StateTransition? _pendingTransition;
    private MouseState       _prevMouse;
    private KeyboardState    _prevKeys;

    // ── DataBus UI ────────────────────────────────────────────────────────────
    private UIManager?       _ui;
    private DriveInstrumentPanel? _drivePanel;
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
    private LedIndicator?          _stopLed;
    private LedIndicator?          _warnLed;
    private Action<double>?       _gravDirXHandler;
    private Action<double>?       _gravDirYHandler;
    private Action<double>?       _gravDirZHandler;
    private double                _gravDirX, _gravDirY, _gravDirZ;

    // ── Ground radar (atmosphere-only instrument panel) ───────────────────────
    private Action<double>? _pcAltHandler, _pcVsHandler, _pcLatHandler,
                            _pcLonHandler, _pcHdgHandler, _pcGsHandler, _pcTempHandler, _pcPressHandler;
    private double _pcAlt, _pcVs, _pcLat, _pcLon, _pcHdg, _pcGs, _pcTemp, _pcPress;
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
    // F3   — toggles third-person camera (ship mesh visible behind camera).
    private bool _uiMouseMode;
    private bool _debugCameraMode;
    private bool _thirdPersonMode;
    private DVec3 _tpCamPos;       // smoothed third-person camera position
    private bool  _tpCamPosValid;
    private bool _prevIsGameActive = true;
    private bool _prevUiMouseMode;

    // Last thrust input from ship mode — preserved so UI mode keeps the same velocity.
    private PlayerInput _lastFlightInput = PlayerInput.Zero;

    // ── Flat Hyperspace ───────────────────────────────────────────────────────
    private FlatHyperspaceController _hyperspace = null!;

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

        // Shared sphere mesh for moons/asteroids (no PlanetData)
        var (vb, ib) = MeshFactory.CreateSphere(_gd, rings: 24, segments: 24);
        _sphereVb        = vb;
        _sphereIb        = ib;
        _sphereTriCount  = 24 * 24 * 2;

        // Per-planet checkerboard sphere meshes
        foreach (var planet in _system.Planets)
            if (planet.Planet != null)
                _planetSpheres[planet] = BuildPlanetSphere(planet);

        // Container renderer and shared mesh (one geometry; colour from lock grade per draw call)
        _meshRenderer = new MeshRenderer(_gd);
        (_containerVb, _containerIb) = BuildContainerMesh(_gd);

        // Ship mesh — three components; built once per session entry on the main thread
        var (hullMesh, nacelleMesh, pylonMesh) = Type1HullFactory.BuildAll(_gd);
        _shipHullVb    = hullMesh.vb;    _shipHullIb    = hullMesh.ib;
        _shipNacelleVb = nacelleMesh.vb; _shipNacelleIb = nacelleMesh.ib;
        _shipPylonVb   = pylonMesh.vb;   _shipPylonIb   = pylonMesh.ib;
        _thirdPersonMode  = false;
        _tpCamPosValid    = false;

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
        // Pre-set SunDirection now so BakeLighting uses the correct world-space direction.
        // Draw() would set it per-frame, but Generate() runs in OnEnter before any Draw().
        {
            Vector3 srp = _camera.ToRenderSpace(DVec3.Zero);
            Vector3 ld  = srp == Vector3.Zero ? -Vector3.UnitZ : Vector3.Normalize(-srp);
            SceneLighting.SunDirection = -ld;
        }
        _stationGeometry.Clear();
        foreach (var v in _decoMeshes.Values)  { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var v in _glassMeshes.Values) { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var v in _hullMeshes.Values)  { v.vb.Dispose(); v.ib.Dispose(); }
        _decoMeshes.Clear();
        _glassMeshes.Clear();
        _hullMeshes.Clear();
        foreach (var station in _system.Stations)
        {
            var modules = StationGenerator.Generate(station, _gd, _gameTimeSeconds);
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
        _testContainers.Clear();
        _prevCameraPosValid = false;

        // Skybox — galaxy stars projected onto a far sphere around the current system
        (_skyboxPoints, _skyboxGlowVerts, _targetableStars) = BuildSkybox(_star, GalaxyGenerator.Generate());

        _pixel = new Texture2D(_gd, 1, 1);
        _pixel.SetData([Color.White]);

        _hyperspace = new FlatHyperspaceController(_gd, _pixel, _simulation, _targeting, EnterSystem);

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
        _drivePanel = new DriveInstrumentPanel();
        _cockpitRail.RightWing.Add(_drivePanel);     // drawn first, under shield button
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

        _pcAltHandler   = v => _pcAlt   = v;
        _pcVsHandler    = v => _pcVs    = v;
        _pcLatHandler   = v => _pcLat   = v;
        _pcLonHandler  = v => _pcLon  = v;
        _pcHdgHandler  = v => _pcHdg  = v;
        _pcGsHandler   = v => _pcGs   = v;
        _pcTempHandler  = v => _pcTemp  = v;
        _pcPressHandler = v => _pcPress = v;
        DataBus.Instruments.Subscribe(Topics.PlanetCoord.Altitude,      _pcAltHandler);
        DataBus.Instruments.Subscribe(Topics.PlanetCoord.VerticalSpeed, _pcVsHandler);
        DataBus.Instruments.Subscribe(Topics.PlanetCoord.Latitude,      _pcLatHandler);
        DataBus.Instruments.Subscribe(Topics.PlanetCoord.Longitude,     _pcLonHandler);
        DataBus.Instruments.Subscribe(Topics.PlanetCoord.Heading,       _pcHdgHandler);
        DataBus.Instruments.Subscribe(Topics.PlanetCoord.GroundSpeed,   _pcGsHandler);
        DataBus.Instruments.Subscribe(Topics.PlanetCoord.Temperature,   _pcTempHandler);
        DataBus.Instruments.Subscribe(Topics.PlanetCoord.Pressure,      _pcPressHandler);

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

        _stopLed = new LedIndicator(
            Topics.Flight.XStopActive,
            DataBus.Instruments,
            _gd,
            _font)
        {
            LabelText      = "STOP",
            LabelAnchor    = LabelAnchor.Bottom,
            LabelFontScale = 0.8f,
            Shape          = LedShape.Round,
            LampSize       = 28,
            MainColor      = new Color(255, 140, 0),
            OnRangeMin     = 0.5,
            OnRangeMax     = double.PositiveInfinity,
        };

        _warnLed = new LedIndicator(
            Topics.Ship.WarnLevel,
            DataBus.Instruments,
            _gd,
            _font)
        {
            LabelText         = "WARN",
            LabelAnchor       = LabelAnchor.Bottom,
            LabelFontScale    = 0.8f,
            Shape             = LedShape.Round,
            LampSize          = 28,
            MainColor         = new Color(200, 50, 50),
            OnRangeMin        = 0.5,
            OnRangeMax        = double.PositiveInfinity,
            ColorRanges       = new List<LedColorRange>
            {
                new(0.5, 1.5, new Color( 40, 110,  55)),
                new(1.5, 2.5, new Color(220, 175,   0)),
                new(2.5, 3.5, new Color(210,  45,  45)),
                new(3.5, double.PositiveInfinity, new Color(210, 45, 45)),
            },
            BlinkRangeMin     = 3.5,
            BlinkRangeMax     = double.PositiveInfinity,
            MinBlinkFrequency = 2.0,
            MaxBlinkFrequency = 2.0,
        };

        if (_cockpitRail != null)
        {
            _cockpitRail.LeftConnectorLed  = _stopLed;
            _cockpitRail.RightConnectorLed = _warnLed;
        }

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
        if (_pcAltHandler  != null) DataBus.Instruments.Unsubscribe(Topics.PlanetCoord.Altitude,      _pcAltHandler);
        if (_pcVsHandler   != null) DataBus.Instruments.Unsubscribe(Topics.PlanetCoord.VerticalSpeed, _pcVsHandler);
        if (_pcLatHandler  != null) DataBus.Instruments.Unsubscribe(Topics.PlanetCoord.Latitude,      _pcLatHandler);
        if (_pcLonHandler  != null) DataBus.Instruments.Unsubscribe(Topics.PlanetCoord.Longitude,     _pcLonHandler);
        if (_pcHdgHandler  != null) DataBus.Instruments.Unsubscribe(Topics.PlanetCoord.Heading,       _pcHdgHandler);
        if (_pcGsHandler   != null) DataBus.Instruments.Unsubscribe(Topics.PlanetCoord.GroundSpeed,   _pcGsHandler);
        if (_pcTempHandler  != null) DataBus.Instruments.Unsubscribe(Topics.PlanetCoord.Temperature, _pcTempHandler);
        if (_pcPressHandler != null) DataBus.Instruments.Unsubscribe(Topics.PlanetCoord.Pressure,    _pcPressHandler);

        // Remove all radar contacts fed from this session so TargetingSystem is clean on re-entry
        foreach (string id in _radarContactIds)
            _targeting.OnContactLost(id);
        _radarContactIds.Clear();

        _stopLed?.Dispose();
        _stopLed = null;
        _warnLed?.Dispose();
        _warnLed = null;

        _ui?.Dispose();
        _ui = null;

        _effect?.Dispose();
        _sphereVb?.Dispose();
        _sphereIb?.Dispose();
        foreach (var v in _decoMeshes.Values)    { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var v in _glassMeshes.Values)   { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var v in _hullMeshes.Values)    { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var v in _planetSpheres.Values) { v.vb.Dispose(); v.ib.Dispose(); }
        _decoMeshes.Clear();
        _glassMeshes.Clear();
        _hullMeshes.Clear();
        _planetSpheres.Clear();
        _testContainers.Clear();
        _meshRenderer?.Dispose();
        _meshRenderer = null;
        _containerVb?.Dispose();
        _containerIb?.Dispose();
        _shipHullVb?.Dispose();    _shipHullIb?.Dispose();
        _shipNacelleVb?.Dispose(); _shipNacelleIb?.Dispose();
        _shipPylonVb?.Dispose();   _shipPylonIb?.Dispose();
        _shipHullVb    = null; _shipHullIb    = null;
        _shipNacelleVb = null; _shipNacelleIb = null;
        _shipPylonVb   = null; _shipPylonIb   = null;
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
        BlinkClock.Update(dt);
        _frameShipSnap = _simulation.ShipState;  // read once — consistent for this entire frame

        // TAB — UI mode toggle; F11 — ship/debug camera toggle; F3 — third-person toggle
        bool tabJustPressed = keys.IsKeyDown(Keys.Tab) && !_prevKeys.IsKeyDown(Keys.Tab);
        bool f11JustPressed = keys.IsKeyDown(Keys.F11) && !_prevKeys.IsKeyDown(Keys.F11);
        bool f3JustPressed  = keys.IsKeyDown(Keys.F3)  && !_prevKeys.IsKeyDown(Keys.F3);

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
            if (_debugCameraMode) _thirdPersonMode = false;  // can't combine with debug cam
        }
        if (f3JustPressed && !_debugCameraMode)
        {
            _thirdPersonMode = !_thirdPersonMode;
            _tpCamPosValid   = false;  // force immediate snap on first frame
        }

        // Animations always run, regardless of input mode
        _ui?.Animate(dt);
        _stopLed?.Update(dt);
        _warnLed?.Update(dt);

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

            // Track cursor position for the skybox star overlay drawn later
            _uiCursorScreen = new Vector2(mouse.X, mouse.Y);

            // Skybox star hover — find nearest targetable star under cursor each frame
            var uiVp = Matrix.Multiply(_camera.ViewMatrix, _camera.ProjectionMatrix);
            UpdateSkyboxHover(_uiCursorScreen, uiVp);

            // Click-to-target — left click selects the nearest radar contact bracket,
            // or a skybox star if the cursor is within hover range of one.
            if (mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released)
            {
                _targeting.SelectAtCursor(_uiCursorScreen, uiVp, _gd.Viewport);

                if (_hoveredSkyboxStar != null)
                {
                    _targeting.SetHyperspaceTarget(_hoveredSkyboxStar);
                    _lockedSkyboxStar = _hoveredSkyboxStar;
                }
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
        else if (_hyperspace.Mode is FlightMode.EnteringFlatHyperspace or FlightMode.FlatHyperspace)
        {
            // Hyperspace — sim is frozen; camera is driven directly by UpdateEnteringHyperspace /
            // UpdateFlatHyperspace. Do NOT let ship-follow overwrite the camera pose here.
            _simulation.SetInput(PlayerInput.Zero);
            if (IsGameActive) Mouse.SetPosition(_gd.Viewport.Width / 2, _gd.Viewport.Height / 2);
        }
        else
        {
            // Ship mode — input goes to the simulation; camera follows cockpit or third-person orbit.
            // Cursor is locked to window centre so mouse can't escape the window.
            _lastFlightInput = BuildShipInput(lookMouse, keys);
            _simulation.SetInput(_lastFlightInput);
            if (_frameShipSnap != null)
            {
                if (_thirdPersonMode)
                    UpdateThirdPersonCamera(_frameShipSnap);
                else
                    _camera.SetPose(_frameShipSnap.CockpitWorldPosition, _frameShipSnap.Orientation);
            }
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

        // Push nearest station surface distance to sim thread (LKM zones and Slipstream dropout)
        {
            DVec3  shipPosForDist     = _frameShipSnap?.Position ?? _camera.UniversePosition;
            double nearestStationDist = double.MaxValue;
            foreach (var (station, stPos) in _stationPositions)
            {
                double r    = StationPhysicalRadius(station);
                double dist = System.Math.Max((stPos - shipPosForDist).Length - r, 0.0);
                if (dist < nearestStationDist) nearestStationDist = dist;
            }
            _simulation.SetNearestStationDistance(nearestStationDist);
        }

        // Rebuild test container contacts — if newly populated this frame, repopulate
        if (_testContainers.Count == 0 && _stationPositions.Count > 0)
            SpawnTestContainers();

        // Update container orientations (slow seeded tumble)
        foreach (var tc in _testContainers)
        {
            double rate = tc.AngularVelocity.Length;
            if (rate > 1e-10)
            {
                DVec3 axis  = tc.AngularVelocity / rate;
                var   delta = Quaternion.CreateFromAxisAngle(
                    new Vector3((float)axis.X, (float)axis.Y, (float)axis.Z), (float)(rate * dt));
                tc.Orientation = Quaternion.Normalize(delta * tc.Orientation);
            }
        }

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

        // Update targeting system with pre-computed galaxy-space positions.
        // In FlatHyperspace use the actual galactic player position so distance/direction updates.
        DVec3 shipPosForTargeting = _frameShipSnap?.Position ?? _camera.UniversePosition;
        DVec3 galPosForTargeting  = _hyperspace.Mode is FlightMode.FlatHyperspace
                                    ? _hyperspace.GalacticPosition
                                    : _star.GalacticPos;
        _targeting.Update(shipPosForTargeting, galPosForTargeting, _bodyPositions, _stationPositions);
        UpdatePadTargetPosition();
        UpdateTargetingUI();
        UpdateRadarDisplay();
        UpdateLandingRadar();

        // Update reference frame (zero-speed object); alert HUD when it changes.
        string prevRefName = _refName;
        UpdateReferenceFrame(_camera.UniversePosition);
        if (_refName != prevRefName && prevRefName.Length > 0)
            _hudAlert.AddMessage(new SystemMessage(
                $"Zero reference speed set to {_refName}.", SystemMessagePriority.NB));
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

        // Keep drive panel filling the right wing (wing bounds are set by CockpitRail.Update)
        if (_drivePanel != null && _cockpitRail != null)
        {
            var rwb = _cockpitRail.RightWing.Bounds;
            _drivePanel.Bounds = new Rectangle(0, 0, rwb.Width, rwb.Height);
        }

        if (!_uiMouseMode)
            HandleKeyboard(keys, mouse);

        // Flat hyperspace update runs every tick regardless of UI mode
        _hyperspace.Update(dt, lookMouse, _camera, _star, _frameShipSnap);

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

        // Clunk roll — camera-space roll around the ship forward axis.
        // Multiply on the right so the rotation is in view space, not world space.
        // This prevents planets from gluing to the screen frame during the animation.
        if (_frameShipSnap?.ClunkPhase >= 0.0)
        {
            float phase = (float)_frameShipSnap.ClunkPhase;
            float roll  = MathHelper.ToRadians(FlightConstants.ClunkRollDegrees)
                        * MathF.Sin(phase * MathF.PI);
            _effect.View = _camera.ViewMatrix * Matrix.CreateRotationZ(roll);
        }

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
        DrawShipMesh();
        DrawStationGlows(sb);

        // Pass 2 — transparent (no depth write/read — shader ray-sphere handles visibility)
        gd.BlendState        = BlendState.AlphaBlend;
        gd.DepthStencilState = DepthStencilState.None;
        foreach (var (body, pos) in _bodyPositions)
            DrawAtmosphere(body, pos);

        // ── 2D overlay ────────────────────────────────────────────────────────
        gd.DepthStencilState = DepthStencilState.None;

        // Hyperspace sheets — drawn between 3D scene and 2D HUD so they sit behind text
        _hyperspace.DrawSheets(_gd, _camera);

        sb.Begin(blendState: BlendState.AlphaBlend);
        DrawHUD(sb);
        DrawStationDots(sb);
        DrawTargetingHUD(sb);
        DrawSkyboxStarOverlay(sb);
        _hyperspace.DrawOverlay(sb);
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

    // ── Input ─────────────────────────────────────────────────────────────────

    private void HandleKeyboard(KeyboardState keys, MouseState mouse)
    {
        bool mPressed    = keys.IsKeyDown(Keys.M)    && !_prevKeys.IsKeyDown(Keys.M);
        bool nPressed    = keys.IsKeyDown(Keys.N)    && !_prevKeys.IsKeyDown(Keys.N);
        bool cPressed    = keys.IsKeyDown(Keys.C)    && !_prevKeys.IsKeyDown(Keys.C);
        bool lPressed    = keys.IsKeyDown(Keys.L)    && !_prevKeys.IsKeyDown(Keys.L);
        bool hPressed    = keys.IsKeyDown(Keys.H)    && !_prevKeys.IsKeyDown(Keys.H);
        bool homePressed = keys.IsKeyDown(Keys.Home) && !_prevKeys.IsKeyDown(Keys.Home);

        if (hPressed)
            _hyperspace.HandleKey(_camera, _star, _frameShipSnap);

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
            // Home — snap ship back to near-star (useful during dev)
            if (homePressed)
                _simulation.RequestSnapToOrigin();
        }
    }

    // ── Ship ──────────────────────────────────────────────────────────────────
    // _ship persists between OnEnter/OnExit calls — only recreated for new spawns.

    private Ship?             _ship;
    private ShieldComponent?  _shield;
}
