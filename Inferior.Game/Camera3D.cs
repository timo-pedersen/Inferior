using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Inferior.Core.Math;

namespace Inferior.Game;

/// <summary>
/// Free-look 3D camera for system space flight.
///
/// Position is stored as DVec3 in universe coordinates (meters).
/// The View matrix always places the camera at render-space origin —
/// this is origin shifting. Everything else is rendered relative to
/// the camera's universe position, so float precision is always safe.
///
/// Controls:
///   WASD       — move forward/back/strafe
///   Q / E      — move up / down
///   Right drag  — look (yaw + pitch)
///   Shift       — 10× speed
///   Ctrl        — 0.1× speed
/// </summary>
public sealed class Camera3D
{
    // ── Universe position (double precision) ──────────────────────────────────
    public DVec3 UniversePosition { get; private set; }

    // ── Look direction ────────────────────────────────────────────────────────
    private float _yaw   = 0f;       // horizontal angle, radians
    private float _pitch = -0.2f;    // vertical angle, radians (slight downward look)

    private const float MaxPitch   =  MathF.PI * 0.48f;
    private const float MouseSens  =  0.003f;

    // ── Derived orientation vectors ───────────────────────────────────────────
    public Vector3 Forward { get; private set; } = -Vector3.UnitZ;
    public Vector3 Right   { get; private set; } =  Vector3.UnitX;
    public Vector3 Up      { get; private set; } =  Vector3.UnitY;

    // ── Matrices ──────────────────────────────────────────────────────────────
    public Matrix ViewMatrix       { get; private set; }
    public Matrix ProjectionMatrix { get; private set; }

    // ── Speed ─────────────────────────────────────────────────────────────────
    /// <summary>Base movement speed in meters per second.</summary>
    public double MoveSpeedMs { get; set; } = 5e9; // 5 billion m/s default — cross AU in ~30 sec

    // ── Mouse look state ──────────────────────────────────────────────────────
    private Point _prevMousePos;
    private bool  _wasRightHeld;

    // ── Constructor ───────────────────────────────────────────────────────────

    public Camera3D(DVec3 startPosition, float aspectRatio)
    {
        UniversePosition = startPosition;
        SetProjection(MathHelper.ToRadians(70f), aspectRatio, 0.1f, 50_000f);
        UpdateOrientation();
    }

    // ── Per-frame update ──────────────────────────────────────────────────────

    public void Update(double dt, MouseState mouse, KeyboardState keys)
    {
        HandleMouseLook(mouse);
        HandleMovement(dt, keys);
        UpdateOrientation();
        UpdateViewMatrix();
    }

    // ── Projection ────────────────────────────────────────────────────────────

    public void SetProjection(float fovRadians, float aspectRatio, float near, float far)
    {
        ProjectionMatrix = Matrix.CreatePerspectiveFieldOfView(
            fovRadians, aspectRatio, near, far);
    }

    // ── Coordinate conversion ─────────────────────────────────────────────────

    /// <summary>
    /// Convert a universe-space position to render-space Vector3.
    /// ALWAYS use this before passing positions to BasicEffect.World.
    /// The result is near zero since it's relative to camera — float is safe.
    /// </summary>
    public Vector3 ToRenderSpace(DVec3 universePos)
    {
        var rel = universePos - UniversePosition;
        return new Vector3(
            (float)(rel.X * RenderScale),
            (float)(rel.Y * RenderScale),
            (float)(rel.Z * RenderScale));
    }

    /// <summary>Scale factor: 1 render unit = 1 / RenderScale meters.</summary>
    public const double RenderScale = 1e-9; // 1 AU ≈ 150 render units

    // ── Private helpers ───────────────────────────────────────────────────────

    private void HandleMouseLook(MouseState mouse)
    {
        bool rightHeld = mouse.RightButton == ButtonState.Pressed;

        if (rightHeld)
        {
            if (!_wasRightHeld)
            {
                // First frame of right-hold — capture start position
                _prevMousePos = mouse.Position;
            }
            else
            {
                // Look delta
                int dx = mouse.X - _prevMousePos.X;
                int dy = mouse.Y - _prevMousePos.Y;

                _yaw   -= dx * MouseSens;
                _pitch -= dy * MouseSens;
                _pitch  = System.Math.Clamp(_pitch, -MaxPitch, MaxPitch);
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

        var moveMeters = DVec3.Zero;

        if (keys.IsKeyDown(Keys.W)) moveMeters += new DVec3(Forward.X, Forward.Y, Forward.Z);
        if (keys.IsKeyDown(Keys.S)) moveMeters -= new DVec3(Forward.X, Forward.Y, Forward.Z);
        if (keys.IsKeyDown(Keys.D)) moveMeters += new DVec3(Right.X,   Right.Y,   Right.Z);
        if (keys.IsKeyDown(Keys.A)) moveMeters -= new DVec3(Right.X,   Right.Y,   Right.Z);
        if (keys.IsKeyDown(Keys.E)) moveMeters += new DVec3(Up.X,      Up.Y,      Up.Z);
        if (keys.IsKeyDown(Keys.Q)) moveMeters -= new DVec3(Up.X,      Up.Y,      Up.Z);

        double len = moveMeters.Length;
        if (len > 0.001)
            UniversePosition += (moveMeters / len) * speed * dt;
    }

    private void UpdateOrientation()
    {
        // Yaw around world Y, then pitch around local X
        var rot = Matrix.CreateRotationX(_pitch) * Matrix.CreateRotationY(_yaw);

        Forward = Vector3.Transform(-Vector3.UnitZ, rot);
        Right   = Vector3.Transform( Vector3.UnitX, rot);
        Up      = Vector3.Transform( Vector3.UnitY, rot);

        Forward.Normalize();
        Right.Normalize();
        Up.Normalize();
    }

    private void UpdateViewMatrix()
    {
        // Camera is always at origin in render space.
        // The whole universe moves relative to it.
        ViewMatrix = Matrix.CreateLookAt(Vector3.Zero, Forward, Up);
    }
}
