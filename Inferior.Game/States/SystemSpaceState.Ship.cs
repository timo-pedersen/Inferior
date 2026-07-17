using Inferior.Core;
using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Core.Simulation;
using Inferior.Galaxy;
using Inferior.Game.Hyperspace;
using Inferior.Game.Input;
using Inferior.Game.Ships;
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
        var ship = ShipBuilder.NewShip("type-1")
            .WithPosition(startPos)
            .WithOrientation(orientation)
            .WithDefaultStartingComponents()
            .Build();

        _shield = ship.Components.OfType<ShieldComponent>().First();
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

    private PlayerInput BuildShipInput(MouseState mouse, KeyboardState keys, bool focused)
    {
        // Rotation — cursor is locked to window centre each frame; accumulate delta from centre.
        var lookInput = _shipMouseLook.Sample(
            mouse,
            new Point(_gd.Viewport.Width / 2, _gd.Viewport.Height / 2),
            active: true,
            focused,
            MouseSensitivity);

        int scroll = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        int gearChangeSteps = scroll / 120;
        if (gearChangeSteps == 0 && scroll != 0)
            gearChangeSteps = scroll > 0 ? 1 : -1;

        long gearSequence = 0;
        if (gearChangeSteps != 0)
            gearSequence = ++_gearInputSequence;

        bool xStopToggle = keys.IsKeyDown(Keys.X) && !_prevKeys.IsKeyDown(Keys.X);
        long xStopSequence = xStopToggle ? ++_xStopInputSequence : 0;

        return ShipInputMapper.Build(keys, _prevKeys, mouse, _prevMouse, lookInput) with
        {
            XStopToggleSequence = xStopSequence,
            GearChangeSequence = gearSequence,
            GearChangeSteps = gearChangeSteps
        };
    }

    // ── Cockpit layout ────────────────────────────────────────────────────────

    private void HandleStationCycleInput(KeyboardState keys)
    {
        StationCycleResult result = _stationCycle.Handle(
            keys,
            _prevKeys,
            _system,
            QueueStationCycleRelocation,
            IsGameActive && StationCyclePlatformInput.IsCtrlF12Down());
        switch (result.Kind)
        {
            case StationCycleResultKind.NoStations:
                DataBus.System.Publish(Topics.System.All,
                    new SystemMessage("Station cycle: current system has no stations.",
                        SystemMessagePriority.ImportantWarning));
                break;

            case StationCycleResultKind.InvalidStation:
                DataBus.System.Publish(Topics.System.All,
                    new SystemMessage(
                        $"Station cycle rejected: {result.StationName ?? "<unnamed>"} has no stable persistence id.",
                        SystemMessagePriority.ImportantWarning));
                break;

            case StationCycleResultKind.Requested:
                StationCycleRequest request = result.Request!.Value;
                DataBus.System.Publish(Topics.System.All,
                    new SystemMessage(
                        $"Station cycle: {request.StationName} ({request.OneBasedIndex}/{request.TotalCount})",
                        SystemMessagePriority.NB));
                break;
        }
    }

    private bool QueueStationCycleRelocation(StationCycleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.StationPersistenceId))
            return false;

        if (!double.IsFinite(request.SurfaceStandOffMeters) || request.SurfaceStandOffMeters < 0.0)
            return false;

        _simulation.RequestStationRelocation(
            request.StationPersistenceId,
            request.SurfaceStandOffMeters);
        return true;
    }

    private (DVec3? pos, Quaternion? ori) CaptureShipState()
    {
        var snap = _simulation.ShipState;
        if (snap == null) return (null, null);
        return (snap.Position, snap.Orientation);
    }
}
