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

public sealed partial class SystemSpaceState
{

    private void DrawShipMesh()
    {
        if (!_thirdPersonMode) return;
        if (_frameShipSnap == null) return;
        if (_meshRenderer == null) return;
        if (_shipHullVb    == null || _shipHullIb    == null ||
            _shipNacelleVb == null || _shipNacelleIb == null ||
            _shipPylonVb   == null || _shipPylonIb   == null) return;

        float   rs        = (float)Camera3D.RenderScale;
        Vector3 renderPos = _camera.ToRenderSpace(_frameShipSnap.Position);
        Matrix  view      = _camera.ViewMatrix;
        Matrix  proj      = _camera.ProjectionMatrix;

        // RotationY(PI) maps the model's +Z-forward nose to the ship's -Z-forward convention.
        Matrix world = Matrix.CreateScale(rs)
                     * Matrix.CreateRotationY(MathF.PI)
                     * Matrix.CreateFromQuaternion(_frameShipSnap.Orientation)
                     * Matrix.CreateTranslation(renderPos);

        var sunCol = new Color(SceneLighting.SunColour);
        _meshRenderer.DrawDynamic(_shipHullVb,    _shipHullIb,    world, view, proj,
            Type1HullFactory.HullColour,    SceneLighting.SunDirection, sunCol);
        _meshRenderer.DrawDynamic(_shipNacelleVb, _shipNacelleIb, world, view, proj,
            Type1HullFactory.NacelleColour, SceneLighting.SunDirection, sunCol);
        _meshRenderer.DrawDynamic(_shipPylonVb,   _shipPylonIb,   world, view, proj,
            Type1HullFactory.PylonColour,   SceneLighting.SunDirection, sunCol);

        _gd.RasterizerState   = RasterizerState.CullCounterClockwise;
        _gd.DepthStencilState = DepthStencilState.Default;
    }

    private void UpdateThirdPersonCamera(SpaceSimulation.ShipSnapshot snap)
    {
        // Camera sits 80 m behind and 30 m above the ship, looks slightly ahead of CoM.
        DVec3 targetCamPos = snap.Position - snap.Forward * 80.0 + snap.Up * 30.0;
        DVec3 lookTarget   = snap.Position + snap.Forward * 8.0;

        // Snap on the first frame after entering third-person; lerp smoothly after that.
        _tpCamPos = _tpCamPosValid
            ? DVec3.Lerp(_tpCamPos, targetCamPos, 0.08)
            : targetCamPos;
        _tpCamPosValid = true;

        // Use ship's own up axis so the camera rolls with the ship — eliminates the
        // singularity that occurs when the ship points near vertical and world-up is
        // nearly parallel to the look direction.
        DVec3 lookDir = DVec3.Normalize(lookTarget - _tpCamPos);
        _camera.SetPose(_tpCamPos, QuatLookAtWithUp(lookDir, snap.Up));
    }

    // Builds a quaternion whose -Z axis aligns with `forward` and whose +Y axis
    // aligns as closely as possible with `shipUp`. No singularity because shipUp is
    // always perpendicular to shipForward (orthogonal ship axes).
    private static Quaternion QuatLookAtWithUp(DVec3 forward, DVec3 shipUp)
    {
        var fwd    = new Vector3((float)forward.X, (float)forward.Y, (float)forward.Z);
        var upHint = new Vector3((float)shipUp.X,  (float)shipUp.Y,  (float)shipUp.Z);

        var right = Vector3.Cross(fwd, upHint);  // Cross(fwd,up) → right; det = +1
        if (right.LengthSquared() < 1e-6f)
            right = Vector3.Cross(fwd, Vector3.UnitX);  // degenerate fallback
        right = Vector3.Normalize(right);
        var up = Vector3.Normalize(Vector3.Cross(right, fwd));  // reorthogonalise up

        // Build rotation matrix M so Transform(-Z, q) = fwd, Transform(+Y, q) = up.
        // MonoGame row-major: row 0 = right, row 1 = up, row 2 = -fwd.
        var m = new Matrix(
            right.X, right.Y, right.Z, 0f,
            up.X,    up.Y,    up.Z,    0f,
           -fwd.X,  -fwd.Y,  -fwd.Z,  0f,
            0f,      0f,      0f,      1f);

        return Quaternion.CreateFromRotationMatrix(m);
    }

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

        // V = Flight Assist toggle, G = Slipstream/mode toggle, X = X-Stop (rising-edge sent to sim)
        bool faToggle         = keys.IsKeyDown(Keys.V) && !_prevKeys.IsKeyDown(Keys.V);
        bool slipstreamToggle = keys.IsKeyDown(Keys.G) && !_prevKeys.IsKeyDown(Keys.G);
        bool xStopToggle      = keys.IsKeyDown(Keys.X) && !_prevKeys.IsKeyDown(Keys.X);

        // Scroll wheel → one gear shift per tick (forwarded to sim; debug cam handles its own scroll)
        int  scroll   = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        bool gearUp   = scroll > 0;
        bool gearDown = scroll < 0;

        return new PlayerInput(fwd, lat, vert, roll, pitchInput, yawInput, false,
            FlightAssistToggle: faToggle,
            SlipstreamToggle:   slipstreamToggle,
            XStopToggle:        xStopToggle,
            GearUp:             gearUp,
            GearDown:           gearDown);
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
}
