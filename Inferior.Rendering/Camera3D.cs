using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Inferior.Core.Math;

namespace Inferior.Rendering;

/// <summary>
/// Free-look 3D camera for system space flight.
///
/// Orientation is a quaternion — no pitch clamp, no gimbal lock, no preferred up.
/// Mouse drag rotates around the camera's own local axes:
///   mouse X → yaw  around local Up
///   mouse Y → pitch around local Right
/// This gives true 6DOF look with no world-Y preference.
///
/// Position is a DVec3 in universe metres. The view matrix places the camera at
/// render-space origin; everything else is rendered relative to it (origin shifting).
///
/// Controls:
///   Right drag  — look
///   WASD        — move forward/back/strafe
///   Q / E       — move up / down (local)
///   Shift       — 10× speed
///   Ctrl        — 0.1× speed
/// </summary>
public sealed class Camera3D
{
    // ── Universe position (double precision) ──────────────────────────────────
    public DVec3 UniversePosition { get; private set; }

    // ── Orientation ───────────────────────────────────────────────────────────
    private Quaternion _orientation;
    public  Quaternion Orientation => _orientation;

    private const float MouseSens = 0.003f;

    // ── Derived orientation vectors (updated each frame) ──────────────────────
    public Vector3 Forward { get; private set; } = -Vector3.UnitZ;
    public Vector3 Right   { get; private set; } =  Vector3.UnitX;
    public Vector3 Up      { get; private set; } =  Vector3.UnitY;

    // ── Matrices ──────────────────────────────────────────────────────────────
    public Matrix ViewMatrix       { get; private set; }
    public Matrix ProjectionMatrix { get; private set; }

    // ── Speed ─────────────────────────────────────────────────────────────────
    public double MoveSpeedMs { get; set; } = 5e9;

    // ── Mouse look state ──────────────────────────────────────────────────────
    private Point _prevMousePos;
    private bool  _wasRightHeld;

    // ── Scale ─────────────────────────────────────────────────────────────────
    /// <summary>1 AU ≈ 150 render units.</summary>
    public const double RenderScale = 1e-9;

    // ── Constructor ───────────────────────────────────────────────────────────

    public Camera3D(DVec3 startPosition, float aspectRatio)
    {
        UniversePosition = startPosition;

        // Start with a slight downward pitch so the ecliptic plane is visible
        _orientation = Quaternion.CreateFromYawPitchRoll(0f, -0.2f, 0f);

        SetProjection(MathHelper.ToRadians(60f), aspectRatio, 0.001f, 50_000f);
        RefreshAxes();
        UpdateViewMatrix();
    }

    // ── Per-frame update ──────────────────────────────────────────────────────

    private const float RollRateRps = 1.2f;  // radians per second

    public void Update(double dt, MouseState mouse, KeyboardState keys)
    {
        HandleMouseLook(mouse);
        HandleMovement(dt, keys);
        HandleRoll(dt, keys);
        RefreshAxes();
        UpdateViewMatrix();
    }

    /// <summary>
    /// Position the camera at the given universe position with the given orientation.
    /// Used in ship mode — the ship drives the camera rather than the camera driving itself.
    /// </summary>
    public void SetPose(DVec3 position, Quaternion orientation)
    {
        UniversePosition = position;
        _orientation     = Quaternion.Normalize(orientation);
        RefreshAxes();
        UpdateViewMatrix();
    }

    // ── Projection ────────────────────────────────────────────────────────────

    public void SetProjection(float fovRadians, float aspectRatio, float near, float far)
        => ProjectionMatrix = Matrix.CreatePerspectiveFieldOfView(fovRadians, aspectRatio, near, far);

    // ── Coordinate conversion ─────────────────────────────────────────────────

    /// <summary>
    /// Universe-space position → render-space Vector3 (relative to camera, safe for float).
    /// Always use this before passing positions to BasicEffect.World.
    /// </summary>
    public Vector3 ToRenderSpace(DVec3 universePos)
    {
        var rel = universePos - UniversePosition;
        return new Vector3(
            (float)(rel.X * RenderScale),
            (float)(rel.Y * RenderScale),
            (float)(rel.Z * RenderScale));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void HandleMouseLook(MouseState mouse)
    {
        bool rightHeld = mouse.RightButton == ButtonState.Pressed;

        if (rightHeld)
        {
            if (_wasRightHeld)
            {
                int dx = mouse.X - _prevMousePos.X;
                int dy = mouse.Y - _prevMousePos.Y;

                if (dx != 0 || dy != 0)
                {
                    // Rotate around camera's own local axes — no world-up preference
                    var yawQ   = Quaternion.CreateFromAxisAngle(Up,    -dx * MouseSens);
                    var pitchQ = Quaternion.CreateFromAxisAngle(Right, -dy * MouseSens);
                    _orientation = Quaternion.Normalize(pitchQ * yawQ * _orientation);
                }
            }
            _prevMousePos = mouse.Position;
        }

        _wasRightHeld = rightHeld;
    }

    private void HandleMovement(double dt, KeyboardState keys)
    {
        double speed = MoveSpeedMs;
        if (keys.IsKeyDown(Keys.LeftShift)   || keys.IsKeyDown(Keys.RightShift))   speed *= 10.0;
        if (keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl)) speed *= 0.1;

        var move = DVec3.Zero;

        if (keys.IsKeyDown(Keys.W)) move += ToDVec3(Forward);
        if (keys.IsKeyDown(Keys.S)) move -= ToDVec3(Forward);
        if (keys.IsKeyDown(Keys.D)) move += ToDVec3(Right);
        if (keys.IsKeyDown(Keys.A)) move -= ToDVec3(Right);
        if (keys.IsKeyDown(Keys.R)) move += ToDVec3(Up);
        if (keys.IsKeyDown(Keys.F)) move -= ToDVec3(Up);

        double len = move.Length;
        if (len > 0.001)
            UniversePosition += (move / len) * speed * dt;
    }

    private void HandleRoll(double dt, KeyboardState keys)
    {
        float roll = 0f;
        if (keys.IsKeyDown(Keys.E)) roll += 1f;
        if (keys.IsKeyDown(Keys.Q)) roll -= 1f;
        if (roll == 0f) return;

        float angle = roll * RollRateRps * (float)dt;
        var rollQ   = Quaternion.CreateFromAxisAngle(Forward, angle);
        _orientation = Quaternion.Normalize(rollQ * _orientation);
    }

    private void RefreshAxes()
    {
        var rot = Matrix.CreateFromQuaternion(_orientation);
        Forward = Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, rot));
        Right   = Vector3.Normalize(Vector3.Transform( Vector3.UnitX, rot));
        Up      = Vector3.Normalize(Vector3.Transform( Vector3.UnitY, rot));
    }

    private void UpdateViewMatrix()
        => ViewMatrix = Matrix.CreateLookAt(Vector3.Zero, Forward, Up);

    private static DVec3 ToDVec3(Vector3 v) => new(v.X, v.Y, v.Z);
}
