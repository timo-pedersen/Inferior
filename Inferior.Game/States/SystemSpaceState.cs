using Inferior.Core;
using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Core.Simulation;
using Inferior.Galaxy;
using Inferior.Game.Input;
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
    private Effect      _litSurfaceEffect = null!;
    private Matrix      _eclipticRotation = Matrix.Identity;

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
    private bool _waitingForStationRelocationSnapshot;
    // The RelocationSequence a ShipSnapshot must reach (>=) before the queued relocation
    // that set _waitingForStationRelocationSnapshot is guaranteed resolved. Meaningless
    // while _waitingForStationRelocationSnapshot is false.
    private int  _expectedRelocationSequence;

    // ── Cached body positions ─────────────────────────────────────────────────
    private readonly List<(OrbitalBody body, DVec3 pos)> _bodyPositions = [];

    // ── Station rendering ─────────────────────────────────────────────────────
    // Per-station placed module list — generated once per system entry from name seed.
    private readonly Dictionary<Galaxy.Station, List<PlacedModule>>                          _stationGeometry  = [];
    private readonly List<(Galaxy.Station station, DVec3 pos)>                               _stationPositions = [];
    // Shipping containers placed around each station — ordinary world objects (real
    // ShippingContainerFactory geometry, real rendering path); placement policy (near
    // stations, 3-6 per station) is for testing, the objects themselves are not.
    private readonly List<PlacedContainer> _containers = [];
    // GPU-side decoration meshes built from PlacedModule.Mesh after generation.
    // _decoMeshes carries the wear/ambient-occlusion-graded colours (DetailLevel.Full);
    // _decoMeshesFlat is a second snapshot built before that pass ran (Medium/Minimal) —
    // see the two Build() calls in OnEnter and DrawStations' DetailLevel gating.
    private readonly Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> _decoMeshes     = [];
    private readonly Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> _decoMeshesFlat = [];
    // GPU-side glass meshes built from PlacedModule.GlassMesh (windows, portholes).
    private readonly Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> _glassMeshes = [];
    // GPU-side hull meshes (VertexPositionNormalColorTexture) for real-time LitSurface.fx
    // DynamicLit lighting (Docs/station-lighting-pipeline-spec.md Phase A).
    private readonly Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> _hullMeshes  = [];

    // ── Container rendering ───────────────────────────────────────────────────
    // Renderer shared with ship/hull draw calls. Each container owns its own
    // VertexBuffer/IndexBuffer (see PlacedContainer) — geometry differs per instance.
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
    private string _refName = "";              // name of the reference object (display only)
    // Category-tagged identity for the simulation's continuous-carry tracking — a plain
    // name isn't quite enough (stations already dedupe among themselves, and planet/moon
    // names are unique within a system by construction, but nothing cross-checks a station
    // name against a body name). The tag makes cross-category collision impossible outright
    // rather than relying on the naming schemes happening to stay disjoint.
    private string _refSourceId = "";
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
    private readonly MouseLookRebaser _shipMouseLook = new();
    private readonly StationCycleController _stationCycle =
        new(SystemMapStationArrivalStandOffMeters);

    // Last thrust input from ship mode — preserved so UI mode keeps the same velocity.
    private PlayerInput _lastFlightInput = PlayerInput.Zero;
    private long _gearInputSequence;
    private long _xStopInputSequence;

    // ── Flat Hyperspace ───────────────────────────────────────────────────────
    private FlatHyperspaceController _hyperspace = null!;

    // Ship snapshot captured once at the top of Update() — all sub-systems use this
    // single consistent value so no two decisions in the same frame see different positions.
    private SpaceSimulation.ShipSnapshot? _frameShipSnap;

    // SpriteBatch captured once at the top of Draw() — DrawStationGlows now runs once
    // per render pass (see DrawFarPassContent/DrawMidPassContent/DrawNearPassContent),
    // not as a single one-shot call after the pass loop, so it needs sb inside those
    // methods rather than only as a local Draw() parameter.
    private SpriteBatch? _frameSpriteBatch;

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
        SystemSpacePayload? initialStarterRelocationPayload = null;
        StationArrivalTarget? stationArrivalPayload = null;

        if (payload is SystemSpacePayload p)
        {
            if (IsInitialNewGameStarterEntry(p))
                initialStarterRelocationPayload = p;
            stationArrivalPayload = p.StationArrival;
            _star            = p.Star;
            _system          = StarSystem.Generate(p.Star, GalaxyGenerator.SystemSeed(p.Star));
            _stationCycle.Reset();
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
            else if (p.StationArrival != null)
            {
                // Approach a station from system map double-click. The simulation
                // owns station resolution, position, velocity, reference and facing.
                _ship = null;
                var startPos = new DVec3(0, 0.5e11, 3e11);
                var startOri = Quaternion.CreateFromYawPitchRoll(0f, -0.2f, 0f);
                _camera = new Camera3D(startPos, AspectRatio);
                _camera.SetPose(startPos, startOri);
                SpawnShip(startPos, startOri);
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
            _stationCycle.Reset();
            ComputeEclipticRotation();
            var fallbackPos = new DVec3(0, 0.5e11, 3e11);
            _camera  = new Camera3D(fallbackPos, AspectRatio);
            SpawnShip(fallbackPos, Quaternion.CreateFromYawPitchRoll(0f, -0.2f, 0f));
        }

        _simulation.InstallSystem(_star, _system);
        _waitingForStationRelocationSnapshot = false;

        if (initialStarterRelocationPayload != null)
        {
            int? expectedSeq = QueueInitialStarterStationRelocation(initialStarterRelocationPayload);
            _waitingForStationRelocationSnapshot = expectedSeq != null;
            if (expectedSeq != null)
            {
                _expectedRelocationSequence = expectedSeq.Value;

                // Calibration cube position is computed once ever, from the first starter
                // relocation's result (see Update()) — not re-armed on later starter entries
                // (there aren't any: IsInitialNewGameStarterEntry is a true one-shot).
                if (_calibrationCubePosition == null)
                {
                    _calibrationCubePending = true;
                    _calibrationCubeStarIndex = _star.GalaxyIndex;
                    _calibrationCubeExpectedRelocationSequence = expectedSeq.Value;
                }
            }
        }
        else if (stationArrivalPayload != null)
        {
            int? expectedSeq = QueueStationArrivalRelocation(stationArrivalPayload.Value);
            _waitingForStationRelocationSnapshot = expectedSeq != null;
            if (expectedSeq != null)
                _expectedRelocationSequence = expectedSeq.Value;
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

        // Container renderer — geometry is built per-instance in SpawnContainers
        _litSurfaceEffect = _content.Load<Effect>("Effects/LitSurface");
        _meshRenderer     = new MeshRenderer(_gd, _litSurfaceEffect);

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
        foreach (var v in _decoMeshes.Values)     { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var v in _decoMeshesFlat.Values) { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var v in _glassMeshes.Values)    { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var v in _hullMeshes.Values)     { v.vb.Dispose(); v.ib.Dispose(); }
        _decoMeshes.Clear();
        _decoMeshesFlat.Clear();
        _glassMeshes.Clear();
        _hullMeshes.Clear();
        foreach (var station in _system.Stations)
        {
            var modules = StationGenerator.Generate(station, _gd, _gameTimeSeconds);
            _stationGeometry[station] = modules;

            // Flat (ungraded) snapshot — captured before ambient occlusion darkens
            // faces below — used for Medium/Minimal DetailLevel. Same generator,
            // fewer steps, same principle already established for containers.
            foreach (var mod in modules)
            {
                var flatGpu = mod.Mesh?.Build(_gd);
                if (flatGpu.HasValue)
                    _decoMeshesFlat[mod] = flatGpu.Value;
            }

            StationDecorator.ApplyAmbientOcclusion(modules);

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
        foreach (var pc in _containers) { pc.Vb.Dispose(); pc.Ib.Dispose(); }
        _containers.Clear();
        _prevCameraPosValid = false;

        // Calibration cube — geometry rebuilds every entry like everything else above;
        // its fixed universe position is computed once (see Update()) and persists across
        // entries, not reset here.
        _calibrationCubeVb?.Dispose();
        _calibrationCubeIb?.Dispose();
        BuildCalibrationCubeGpuMesh();

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
        _shipMouseLook.RequestRebase();

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

        // Gravity-direction subscriptions stay here for cockpit direction balls.
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
        _stationCycle.Reset();

        _cockpitUI?.Dispose();

        foreach (var sub in _subscriptions) sub.Dispose();
        _subscriptions.Clear();

        // Remove all radar contacts fed from this session so TargetingSystem is clean on re-entry
        foreach (string id in _radarContactIds)
            _targeting.OnContactLost(id);
        _radarContactIds.Clear();

        _celestialBodies?.Dispose();

        _effect?.Dispose();
        foreach (var v in _decoMeshes.Values)     { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var v in _decoMeshesFlat.Values) { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var v in _glassMeshes.Values)    { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var v in _hullMeshes.Values)     { v.vb.Dispose(); v.ib.Dispose(); }
        _decoMeshes.Clear();
        _decoMeshesFlat.Clear();
        _glassMeshes.Clear();
        _hullMeshes.Clear();
        foreach (var pc in _containers) { pc.Vb.Dispose(); pc.Ib.Dispose(); }
        _containers.Clear();
        _calibrationCubeVb?.Dispose();
        _calibrationCubeIb?.Dispose();
        _calibrationCubeVb = null;
        _calibrationCubeIb = null;
        _meshRenderer?.Dispose();
        _meshRenderer = null;
        _shipMeshRenderer?.Dispose();
        _pixel?.Dispose();
        _navGlowTex?.Dispose();
        _atmosEffect = null; // owned by ContentManager — do not dispose manually
        _litSurfaceEffect = null!; // owned by ContentManager — do not dispose manually
    }

    public override void OnResize(int width, int height)
    {
        // Transient fallback — overwritten by the real per-pass projections next Draw(),
        // and by the mid-tier representative projection next Update(). Matches that
        // default so nothing renders through a stale pre-3-tier value in between.
        _camera?.SetProjection(MathHelper.ToRadians(60f), AspectRatio,
            (float)(MidTierNear * Camera3D.RenderScale), (float)(MidTierFar * Camera3D.RenderScale));
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
        // A non-null snapshot alone is NOT sufficient here: several ticks can publish
        // snapshots (system install, station generation) before the sim thread even looks
        // at the queued relocation request, so an early snapshot still carries the
        // pre-relocation ship state. Gate on RelocationSequence instead — see
        // SpaceSimulation.RequestStationRelocation's doc comment.
        if (_waitingForStationRelocationSnapshot && _frameShipSnap != null
            && _frameShipSnap.RelocationSequence >= _expectedRelocationSequence)
            _waitingForStationRelocationSnapshot = false;

        // Calibration cube position — computed once, from the first snapshot whose
        // RelocationSequence proves the starter relocation has actually been resolved
        // (same reasoning as above; a separate expected-sequence field so this wait can't
        // be perturbed by an unrelated later relocation request).
        if (_calibrationCubePending && _frameShipSnap != null
            && _frameShipSnap.RelocationSequence >= _calibrationCubeExpectedRelocationSequence)
        {
            _calibrationCubePending  = false;
            _calibrationCubePosition = _frameShipSnap.Position
                                     + _frameShipSnap.Forward * CalibrationCubeSpawnDistance;
        }
        if (_frameShipSnap != null)
        {
            string prevRefName = _refName;
            _refVelocity = _frameShipSnap.ReferenceVelocity;
            _refName     = _frameShipSnap.ReferenceName;
            _refSourceId = _frameShipSnap.ReferenceSourceId;
            if (_refName != prevRefName && prevRefName.Length > 0)
                _hudAlert.AddMessage(new SystemMessage(
                    $"Zero reference speed set to {_refName}.", SystemMessagePriority.NB));
        }


        // TAB — UI mode toggle; F11 — ship/debug camera toggle; F3 — third-person toggle
        bool tabJustPressed = keys.IsKeyDown(Keys.Tab) && !_prevKeys.IsKeyDown(Keys.Tab);
        bool f10JustPressed = keys.IsKeyDown(Keys.F10) && !_prevKeys.IsKeyDown(Keys.F10);
        bool f11JustPressed = keys.IsKeyDown(Keys.F11) && !_prevKeys.IsKeyDown(Keys.F11);
        bool f3JustPressed  = keys.IsKeyDown(Keys.F3)  && !_prevKeys.IsKeyDown(Keys.F3);

        if (tabJustPressed)
        {
            _uiMouseMode = !_uiMouseMode;
            _cockpitUI.ApplyUiMode(_uiMouseMode);
        }
        if (f10JustPressed)
            RequestStationProximityDiagnostic();
        if (f11JustPressed)
        {
            if (_debugCameraMode)
            {
                if (_frameShipSnap != null)
                    _camera.SetPose(_frameShipSnap.CockpitWorldPosition, _frameShipSnap.Orientation);
                _shipMouseLook.RequestRebase();
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
        if (focusJustRegained)
            _shipMouseLook.RequestRebase();
        _prevIsGameActive = IsGameActive;
        bool justExitedUiMode = _prevUiMouseMode && !_uiMouseMode;
        if (justExitedUiMode)
            _shipMouseLook.RequestRebase();
        _prevUiMouseMode = _uiMouseMode;
        var lookMouse = (IsGameActive && !focusJustRegained && !justExitedUiMode) ? mouse : new MouseState(
            _gd.Viewport.Width / 2, _gd.Viewport.Height / 2,
            mouse.ScrollWheelValue,
            ButtonState.Released, ButtonState.Released, ButtonState.Released,
            ButtonState.Released, ButtonState.Released);

        if (_uiMouseMode)
        {
            _shipMouseLook.RequestRebase();
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
            _shipMouseLook.RequestRebase();
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
            _shipMouseLook.RequestRebase();
            // Hyperspace — sim is frozen; camera is driven directly by UpdateEnteringHyperspace /
            // UpdateFlatHyperspace. Do NOT let ship-follow overwrite the camera pose here.
            _simulation.SetInput(PlayerInput.Zero);
            if (IsGameActive) Mouse.SetPosition(_gd.Viewport.Width / 2, _gd.Viewport.Height / 2);
        }
        else
        {
            // Ship mode — input goes to the simulation; camera follows cockpit or third-person orbit.
            // Cursor is locked to window centre so mouse can't escape the window.
            HandleStationCycleInput(keys);
            _lastFlightInput = BuildShipInput(lookMouse, keys, IsGameActive);
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

        // _camera.ProjectionMatrix is only a representative projection now — actual
        // rendering uses three independent per-pass projections built fresh in Draw()
        // (see BuildActivePasses). This one is read by UI/targeting code that needs *a*
        // projection for screen-space math (radar contact hover, skybox star picking);
        // the mid tier's fixed range is a far better fit for that than the old dynamic
        // near-clip, which could shrink to sub-millimetre and made screen-space math
        // near the camera imprecise for no benefit to those callers.
        _camera.SetProjection(MathHelper.ToRadians(60f), AspectRatio,
            (float)(MidTierNear * Camera3D.RenderScale), (float)(MidTierFar * Camera3D.RenderScale));

        // Populate containers once station positions exist — a lazy one-time world
        // population, not a per-frame simulation step. Orientation itself is on rails
        // (pure function of sim time, evaluated at draw/query time — see RailsOrientation
        // in SystemSpaceState.Helpers.cs), so there is nothing to update here per frame.
        if (_containers.Count == 0 && _stationPositions.Count > 0)
            SpawnContainers();

        // Update direction balls — after position rebuild so current-frame station positions
        // are used, not the previous frame's. Avoids the ~1-frame (~350 m) visual offset
        // between the dot indicator and the rendered station.
        _cockpitUI.UpdateDirectionBalls(_camera, _eclipticRotation, _gravDirX, _gravDirY,
            _gravDirZ, _bodyPositions, _stationPositions);

        // Feed planets, moons, and stations into TargetingSystem so T-key / click-to-target work
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

        // Proximity speed scale — applied to debug camera each frame
        if (_debugCameraMode)
            _camera.ProximitySpeedScale = ComputeProximityScale();

        // Ship fly speed — base speed scaled by proximity, then hard-capped near stations
        if (!_debugCameraMode)
        {
            DVec3 shipPos = _frameShipSnap?.Position ?? _camera.UniversePosition;
            _simulation.SetShipMoveSpeed(ComputeShipSpeed(shipPos));
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
        _frameSpriteBatch = sb;
        gd.Clear(ColBackground);
        if (_waitingForStationRelocationSnapshot)
            return;

        // ── 3D scene ──────────────────────────────────────────────────────────

        // Depth buffer on, backface culling on
        gd.DepthStencilState = DepthStencilState.Default;
        gd.RasterizerState   = RasterizerState.CullCounterClockwise;
        gd.BlendState        = BlendState.Opaque;

        // View matrix shared by every pass; projection is set fresh per pass below.
        _effect.View = _camera.ViewMatrix;

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

        // Three render passes — far, mid, near — each with its own independently
        // scoped near/far (see BuildActivePasses in SystemSpaceState.Helpers.cs).
        // Rendered far-to-near; only the depth buffer is cleared between passes,
        // never colour, so a nearer pass paints over a farther one's output with no
        // cross-pass depth test needed. Correctness comes from the passes covering
        // strictly decreasing, non-overlapping-by-construction ranges, not from
        // depth comparison.
        var passes = BuildActivePasses();
        for (int i = 0; i < passes.Count; i++)
        {
            var pass = passes[i];
            _effect.Projection = Matrix.CreatePerspectiveFieldOfView(
                MathHelper.ToRadians(60f), AspectRatio, pass.Near, pass.Far);

            if (i > 0)
                gd.Clear(ClearOptions.DepthBuffer, Color.Black, 1f, 0);

            pass.DrawCallback(pass.Level);
        }

        // DrawStationGlows now runs once per pass (see DrawFarPassContent/DrawMidPassContent/
        // DrawNearPassContent below), each filtered to that pass's own distance range and
        // depth-tested against that pass's own freshly-populated depth buffer — running it
        // once here, after the loop, would depth-test every light against whatever the LAST
        // pass (near tier) left behind, which is "cleared to far" everywhere the near tier
        // didn't actually draw (i.e. everywhere ordinary, non-close-up station structure is),
        // so lights on any mid/far-tier module would never be occluded at all.
        _effect.Projection = _camera.ProjectionMatrix;

        // Transparent pass (no depth write/read — shader ray-sphere handles visibility).
        // Uses the mid tier's representative projection, already set above.
        gd.BlendState        = BlendState.AlphaBlend;
        gd.DepthStencilState = DepthStencilState.None;
        foreach (var (body, pos) in _bodyPositions)
            _celestialBodies.DrawAtmosphere(_camera, body, pos, DetailLevel.Medium);

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

    // ── Render pass content (see BuildActivePasses, SystemSpaceState.Helpers.cs) ────

    // Far tier: star, planets, distant/other stations, skybox — nothing here is ever
    // close to the camera by construction, so this pass's fixed near/far needs no
    // per-frame computation. DrawStations() runs here too (same call as the other two
    // passes) — this pass's near value hardware-clips away anything closer than the
    // mid tier's outer boundary, so only far-flung modules survive here.
    private void DrawFarPassContent(DetailLevel level)
    {
        // Skybox drawn first, before the depth buffer has any data, so geometry always wins
        _gd.DepthStencilState = DepthStencilState.None;
        if (_hyperspace.Mode is not FlightMode.FlatHyperspace)
            _skyboxRenderer.Draw();

        _gd.BlendState        = BlendState.AlphaBlend;
        _gd.DepthStencilState = DepthStencilState.Default;
        _celestialBodies.DrawOrbitRings(_camera, _eclipticRotation, _gameTimeSeconds, level);
        DrawStationOrbitRings();

        // Star glow — depth-read so planets drawn opaque afterward correctly overwrite
        // it on their disc areas (fixes glow bleeding through planets).
        _gd.BlendState        = BlendState.Additive;
        _gd.DepthStencilState = DepthStencilState.DepthRead;
        _celestialBodies.DrawStarGlow(_camera, _star, level);

        _gd.BlendState        = BlendState.Opaque;
        _gd.DepthStencilState = DepthStencilState.Default;
        _celestialBodies.DrawStar(_camera, _star, level);
        foreach (var (body, pos) in _bodyPositions)
            _celestialBodies.DrawPlanet(_camera, body, pos, level);

        DrawStations(level);
        DrawStationGlows(_frameSpriteBatch!, (float)MidTierFar, float.MaxValue);
    }

    // Mid tier: station/ship-scale structure — individual modules/greebles/panels
    // stay resolvable here. Fixed near/far (see BuildActivePasses); this is where
    // "flying between towers" and "circling a station" live, and where the ship
    // itself (third-person, ~80-90m away) actually belongs now the near tier is
    // fixed to 100mm-5m.
    private void DrawMidPassContent(DetailLevel level)
    {
        _gd.BlendState        = BlendState.Opaque;
        _gd.DepthStencilState = DepthStencilState.Default;
        DrawStations(level);
        DrawContainers(level);
        DrawCalibrationCube(level);
        if (_thirdPersonMode && _frameShipSnap != null)
            _shipMeshRenderer.Draw(_camera, _effect.View, _effect.Projection,
                _frameShipSnap.Position, _frameShipSnap.Orientation, level);
        DrawStationGlows(_frameSpriteBatch!, (float)MidTierNear, (float)MidTierFar);
    }

    // Near tier: extreme close-up inspection — fasteners, container insets, rivets.
    // Fixed 100mm-5m (see BuildActivePasses); no dynamic computation needed.
    private void DrawNearPassContent(DetailLevel level)
    {
        _gd.BlendState        = BlendState.Opaque;
        _gd.DepthStencilState = DepthStencilState.Default;
        DrawStations(level);
        DrawContainers(level);
        DrawCalibrationCube(level);
        DrawStationGlows(_frameSpriteBatch!, 0f, (float)NearTierFar);
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    private void HandleKeyboard(KeyboardState keys, MouseState mouse)
    {
        bool mPressed    = keys.IsKeyDown(Keys.M)    && !_prevKeys.IsKeyDown(Keys.M);
        bool nPressed    = keys.IsKeyDown(Keys.N)    && !_prevKeys.IsKeyDown(Keys.N);
        bool tPressed    = keys.IsKeyDown(Keys.T)    && !_prevKeys.IsKeyDown(Keys.T);
        // Keys.C is reserved for future "align ship to target" (docking assist)
        bool lPressed    = keys.IsKeyDown(Keys.L)    && !_prevKeys.IsKeyDown(Keys.L);
        bool hPressed    = keys.IsKeyDown(Keys.H)    && !_prevKeys.IsKeyDown(Keys.H);
        bool homePressed = keys.IsKeyDown(Keys.Home) && !_prevKeys.IsKeyDown(Keys.Home);

        if (hPressed && !(_frameShipSnap?.AfterburnerActive ?? false))
            _hyperspace.HandleKey(_camera, _star, _frameShipSnap);

        if (tPressed)
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
