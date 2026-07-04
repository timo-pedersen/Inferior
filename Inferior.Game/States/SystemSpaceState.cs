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

    // ── Celestial body rendering ──────────────────────────────────────────────
    private RingPrimitive         _ringPrimitive    = null!;
    private CelestialBodyRenderer _celestialBodies  = null!;

    // Skybox star field — built once on enter, static for the session
    private SkyboxRenderer              _skyboxRenderer  = null!;
    private (Vector3 pos, Star star)[]  _targetableStars = [];  // stars ≤1000 ly — hittable from cursor

    // Skybox targeting state
    private Star?   _hoveredSkyboxStar;   // star under cursor this frame (UI mode only)
    private Star?   _lockedSkyboxStar;    // currently selected hyperspace-target star
    private Vector2 _uiCursorScreen;      // cached cursor position for overlay drawing

    // ── 2D overlay (SpriteBatch for HUD) ──────────────────────────────────────
    private Texture2D _pixel       = null!;
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

    // ── Container rendering ───────────────────────────────────────────────────
    // Renderer shared with ship/hull draw calls. Each test container owns its own
    // VertexBuffer/IndexBuffer (see TestContainerEntry) — geometry differs per instance.
    private MeshRenderer?  _meshRenderer;

    // ── Ship mesh (three components, built once per session entry) ────────────
    private ShipMeshRenderer _shipMeshRenderer = null!;

    // ── UI ────────────────────────────────────────────────────────────────────
    private StateTransition? _pendingTransition;
    private MouseState       _prevMouse;
    private KeyboardState    _prevKeys;

    // ── DataBus UI ────────────────────────────────────────────────────────────
    // Disposed as a batch in OnExit — see BusSubscription<T>. Holds only the 3
    // gravity-direction subscriptions; CockpitUI owns its own list for the rest.
    private readonly List<IDisposable> _subscriptions = new();
    private HudAlertDisplay        _hudAlert = new();
    private double                _gravDirX, _gravDirY, _gravDirZ;

    // ── Cockpit UI ────────────────────────────────────────────────────────────
    private CockpitUI _cockpitUI = null!;

    // ── Targeting ─────────────────────────────────────────────────────────────
    private readonly TargetingSystem _targeting       = new();
    private readonly HashSet<string> _radarContactIds = [];   // IDs fed this session; cleared on exit

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

    // Colours
    private static readonly Color ColBackground = new(4, 4, 12);

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

        // Container renderer — geometry is built per-instance in SpawnTestContainers
        _meshRenderer = new MeshRenderer(_gd);

        // Ship mesh — three components; built once per session entry on the main thread
        _shipMeshRenderer = new ShipMeshRenderer(_gd, _meshRenderer);
        _thirdPersonMode  = false;
        _tpCamPosValid    = false;

        // Ring primitive reused for both orbit rings and station orbit rings
        _ringPrimitive = new RingPrimitive();

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
        foreach (var tc in _testContainers) { tc.Vb.Dispose(); tc.Ib.Dispose(); }
        _testContainers.Clear();
        _prevCameraPosValid = false;

        // Skybox — galaxy stars projected onto a far sphere around the current system
        _skyboxRenderer = new SkyboxRenderer(_gd, _effect);
        var (skyPoints, skyGlow, targetable) = SkyboxRenderer.Build(_star, GalaxyGenerator.Generate());
        _skyboxRenderer.Load(skyPoints, skyGlow);
        _targetableStars = targetable;

        _pixel = new Texture2D(_gd, 1, 1);
        _pixel.SetData([Color.White]);

        _hyperspace = new FlatHyperspaceController(_gd, _pixel, _simulation, _targeting, EnterSystem);

        _navGlowTex  = CreateNavGlowTexture(_gd, 64);
        _atmosEffect = _content.Load<Effect>("Effects/Atmosphere");

        _celestialBodies = new CelestialBodyRenderer(_gd, _effect, _atmosEffect,
            _ringPrimitive, EclipticToGalaxy, _system);

        _pendingTransition = null;
        UpdateUI();

        _cockpitUI = new CockpitUI(_gd, _font, _pixel, _targeting, _hudAlert,
            GalaxyToEcliptic, on => { if (_shield != null) _shield.PowerOn = on; });
        _uiMouseMode     = false;
        _debugCameraMode = false;

        // Restore panel layout if returning from system map
        if (payload is SystemSpacePayload { Layout: { } layout })
            _cockpitUI.ApplyLayout(layout);

        // Restore nav target selected in system map; clear if payload carries none
        if (payload is SystemSpacePayload ssp)
        {
            if (ssp.NavBody != null)          _targeting.SetNavTarget(ssp.NavBody);
            else if (ssp.NavStation != null)  _targeting.SetNavTarget(ssp.NavStation);
            else                              _targeting.ClearNavTarget();
        }

        // Start in ship-control mode — panels retracted, handles and buttons hidden
        _cockpitUI.ApplyUiMode(false);

        // Gravity-direction subscriptions stay here — UpdateReferenceFrame needs them too
        _subscriptions.Add(new BusSubscription<double>(DataBus.Instruments,
            $"GravitySensor.{Topics.GravitySensor.DirectionX}", v => _gravDirX = v));
        _subscriptions.Add(new BusSubscription<double>(DataBus.Instruments,
            $"GravitySensor.{Topics.GravitySensor.DirectionY}", v => _gravDirY = v));
        _subscriptions.Add(new BusSubscription<double>(DataBus.Instruments,
            $"GravitySensor.{Topics.GravitySensor.DirectionZ}", v => _gravDirZ = v));

        // First system message — confirms state entry
        DataBus.System.Publish(Topics.System.All, new($"Entered {_star.Name}"));
    }

    public override void OnExit()
    {
        // Stop ship from drifting while browsing maps
        _simulation.SetInput(PlayerInput.Zero);

        _cockpitUI?.Dispose();

        foreach (var sub in _subscriptions) sub.Dispose();
        _subscriptions.Clear();

        // Remove all radar contacts fed from this session so TargetingSystem is clean on re-entry
        foreach (string id in _radarContactIds)
            _targeting.OnContactLost(id);
        _radarContactIds.Clear();

        _celestialBodies?.Dispose();

        _effect?.Dispose();
        foreach (var v in _decoMeshes.Values)    { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var v in _glassMeshes.Values)   { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var v in _hullMeshes.Values)    { v.vb.Dispose(); v.ib.Dispose(); }
        _decoMeshes.Clear();
        _glassMeshes.Clear();
        _hullMeshes.Clear();
        foreach (var tc in _testContainers) { tc.Vb.Dispose(); tc.Ib.Dispose(); }
        _testContainers.Clear();
        _meshRenderer?.Dispose();
        _meshRenderer = null;
        _shipMeshRenderer?.Dispose();
        _pixel?.Dispose();
        _navGlowTex?.Dispose();
        _atmosEffect = null; // owned by ContentManager — do not dispose manually
    }

    public override void OnResize(int width, int height)
    {
        _camera?.SetProjection(MathHelper.ToRadians(60f), AspectRatio, 0.00001f, 50_000f);
        UpdateUI();
        _cockpitUI?.OnResize(width, height);
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
            _cockpitUI.ApplyUiMode(_uiMouseMode);
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
        _cockpitUI.Tick(dt);

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
            _cockpitUI.HandleUiInput(dt, new InputState(mouse, _prevMouse, keys, _prevKeys));
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
            var uiVp = Matrix.Multiply(_effect.View, _camera.ProjectionMatrix);
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
        var (nearClip, farClip) = ComputeNearFarClip();
        _camera.SetProjection(MathHelper.ToRadians(60f), AspectRatio, nearClip, farClip);

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
        _cockpitUI.UpdateDirectionBalls(_camera, _eclipticRotation, _gravDirX, _gravDirY,
            _gravDirZ, _bodyPositions, _stationPositions);

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
        if (!_targeting.HasHyperspaceTarget)
            _lockedSkyboxStar = null;
        UpdatePadTargetPosition();
        _cockpitUI.UpdateTargetingAndRadar(_camera, shipPosForTargeting, _frameShipSnap,
            _padWorldPos, _padDistance, _padDirection);

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
        if (_hyperspace.Mode is not FlightMode.FlatHyperspace)
            _skyboxRenderer.Draw();

        // Pass 1 — opaque geometry (depth writes on)
        gd.BlendState        = BlendState.AlphaBlend;
        gd.DepthStencilState = DepthStencilState.Default;
        _celestialBodies.DrawOrbitRings(_camera, _eclipticRotation, _gameTimeSeconds);
        DrawStationOrbitRings();

        // Star glow — 3D billboard with depth-read so planets drawn opaque afterward
        // correctly overwrite it on their disc areas (fixes glow bleeding through planets).
        gd.BlendState        = BlendState.Additive;
        gd.DepthStencilState = DepthStencilState.DepthRead;
        _celestialBodies.DrawStarGlow(_camera, _star);

        gd.BlendState        = BlendState.Opaque;
        gd.DepthStencilState = DepthStencilState.Default;
        _celestialBodies.DrawStar(_camera, _star);
        foreach (var (body, pos) in _bodyPositions)
            _celestialBodies.DrawPlanet(_camera, body, pos);
        DrawStations();
        DrawTestContainers();
        if (_thirdPersonMode && _frameShipSnap != null)
            _shipMeshRenderer.Draw(_camera, _effect.View, _frameShipSnap.Position, _frameShipSnap.Orientation);
        DrawStationGlows(sb);

        // Pass 2 — transparent (no depth write/read — shader ray-sphere handles visibility)
        gd.BlendState        = BlendState.AlphaBlend;
        gd.DepthStencilState = DepthStencilState.None;
        foreach (var (body, pos) in _bodyPositions)
            _celestialBodies.DrawAtmosphere(_camera, body, pos);

        // ── 2D overlay ────────────────────────────────────────────────────────
        gd.DepthStencilState = DepthStencilState.None;

        // Hyperspace sheets — drawn between 3D scene and 2D HUD so they sit behind text
        _hyperspace.DrawSheets(_gd, _camera);

        sb.Begin(blendState: BlendState.AlphaBlend);
        _cockpitUI.DrawHud(sb, _debugCameraMode, _cameraActualVelocity, _refVelocity, _refName,
            _frameShipSnap, _gameTimeSeconds, _uiMouseMode, _hyperspace.Mode, _camera.MoveSpeedMs);
        DrawStationDots(sb);
        _cockpitUI.DrawTargetingHud(sb, _camera, _effect.View, _padWorldPos, _padDistance);
        DrawSkyboxStarOverlay(sb);
        _hyperspace.DrawOverlay(sb);
        sb.End();

        // Crosshair — separate pass with colour-invert blend so it's readable against any background
        if (!_uiMouseMode)
        {
            sb.Begin(blendState: _invertBlend);
            _cockpitUI.DrawCrosshair(sb);
            sb.End();
        }

        // UI library draws on top — owns its own SpriteBatch
        _cockpitUI.DrawUiTree();

        // HUD alert overlay — drawn after UI so it's always on top
        sb.Begin(blendState: BlendState.AlphaBlend);
        _cockpitUI.DrawHudAlert(sb);
        sb.End();
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
            var vp = Matrix.Multiply(_effect.View, _camera.ProjectionMatrix);
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
                new SystemMapPayload(_star, _gameTimeSeconds, _cockpitUI.CaptureLayout(), pos, ori,
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
