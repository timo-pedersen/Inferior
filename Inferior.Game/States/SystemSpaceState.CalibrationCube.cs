using Inferior.Core.Math;
using Inferior.Game.StationGen;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.States;

// A fixed-position, self-illuminating lighting test card near the starter station — six
// flat, axis-coded face albedos with orientation labels, built so a screenshot alone
// reveals cube orientation, sun direction, shading falloff, and winding/culling
// correctness at a glance. Same rails-kinematics and standard rendering path as
// SystemSpaceState.Containers.cs (fixed position, spin-only orientation, DrawDynamicLit) —
// no bespoke shading, no special draw ordering.
public sealed partial class SystemSpaceState
{
    private const float CalibrationCubeSize          = 10.0f;   // metres
    private const float CalibrationCubeSpawnDistance = 100.0f;  // metres in front of starter spawn pose
    private const float CalibrationCubeSpinRateRadPerSec = 0.05f;   // ~2 min per revolution
    private static readonly Vector3 CalibrationCubeSpinAxis =
        Vector3.Normalize(new Vector3(0.3f, 1.0f, 0.2f));

    private VertexBuffer? _calibrationCubeVb;
    private IndexBuffer?  _calibrationCubeIb;

    // Fixed universe position, computed once from the starter relocation result — it does
    // not follow the player. Persists across OnEnter calls (unlike the GPU buffers above,
    // which rebuild every entry like every other station-scene resource); only drawn while
    // the current system matches the one it was placed in.
    private DVec3? _calibrationCubePosition;
    private int    _calibrationCubeStarIndex = -1;
    private bool   _calibrationCubePending;
    // The RelocationSequence a ShipSnapshot must reach (>=) before the starter relocation's
    // result is safe to read for cube placement — see SpaceSimulation.RequestStationRelocation's
    // doc comment. A dedicated field (not shared with _expectedRelocationSequence) so the
    // cube's wait is self-contained and can't be perturbed by an unrelated later relocation
    // request reusing that field.
    private int    _calibrationCubeExpectedRelocationSequence;

    // Called from OnEnter, unconditionally (geometry never changes, matches the
    // dispose-every-exit/rebuild-every-entry convention used for _pixel/_navGlowTex/etc.
    // elsewhere in this file).
    private void BuildCalibrationCubeGpuMesh()
    {
        var (verts, indices) = BuildCalibrationCubeVertices();

        _calibrationCubeVb = new VertexBuffer(_gd, VertexPositionNormalColorTexture.VertexDeclaration,
            verts.Length, BufferUsage.WriteOnly);
        _calibrationCubeVb.SetData(verts);

        _calibrationCubeIb = new IndexBuffer(_gd, IndexElementSize.SixteenBits,
            indices.Length, BufferUsage.WriteOnly);
        _calibrationCubeIb.SetData(indices);
    }

    // Six flat, unlit-look-but-actually-dynamically-lit face albedos, axis coded, each
    // carrying a white "+X"/"-X"/... label. uAxis/vAxis per face reuse the exact same
    // per-face tangent-frame table as SystemSpaceState.Stations.cs' BuildHullMesh — that
    // table was specifically chosen so U/V (and therefore text) never mirrors; re-derived
    // for this cube would only risk reintroducing that bug.
    private static (VertexPositionNormalColorTexture[] verts, short[] indices) BuildCalibrationCubeVertices()
    {
        const float Half      = CalibrationCubeSize * 0.5f;
        const float PixelSize = 0.35f;   // ~4.2m-wide 2-char label on a 10m face
        const float Raise     = 0.05f;   // proud of the face surface — avoids z-fighting

        var mesh = new StationModuleMesh();

        (Vector3 normal, Vector3 uAxis, Vector3 vAxis, Color albedo, string label)[] faces =
        [
            ( Vector3.UnitZ,  Vector3.UnitX,  Vector3.UnitY, new Color( 50,  90, 210), "+Z"),
            (-Vector3.UnitZ, -Vector3.UnitX,  Vector3.UnitY, new Color( 20,  40,  95), "-Z"),
            (-Vector3.UnitX,  Vector3.UnitZ,  Vector3.UnitY, new Color( 90,  15,  15), "-X"),
            ( Vector3.UnitX, -Vector3.UnitZ,  Vector3.UnitY, new Color(200,  40,  40), "+X"),
            ( Vector3.UnitY,  Vector3.UnitX, -Vector3.UnitZ, new Color( 40, 170,  60), "+Y"),
            (-Vector3.UnitY,  Vector3.UnitX,  Vector3.UnitZ, new Color( 15,  75,  25), "-Y"),
        ];

        foreach (var (normal, uAxis, vAxis, albedo, label) in faces)
        {
            Vector3 center = normal * Half;
            mesh.AddQuad(center, normal, vAxis, CalibrationCubeSize, CalibrationCubeSize, albedo);

            float textW = label.Length * (BitmapFonts.CharW + 1) * PixelSize;
            float textH = BitmapFonts.CharH * PixelSize;
            Vector3 textOrigin = center + normal * Raise
                                - uAxis * (textW * 0.5f) - vAxis * (textH * 0.5f);

            Containers.ShippingContainerFactory.AddTextGeometry(
                mesh, label, textOrigin,
                textRight: uAxis, textUp: vAxis, textNormal: normal,
                PixelSize, Color.White);
        }

        return mesh.ToArrays();
    }

    private void DrawCalibrationCube(DetailLevel level)
    {
        if (_calibrationCubeVb == null || _calibrationCubeIb == null || _meshRenderer == null) return;
        if (_calibrationCubePosition == null || _star.GalaxyIndex != _calibrationCubeStarIndex) return;

        Vector3 renderPos = _camera.ToRenderSpace(_calibrationCubePosition.Value);
        if (renderPos.Length() > 30_000f) return;

        float  rs   = (float)Camera3D.RenderScale;
        Matrix view = _effect.View;
        Matrix proj = _effect.Projection;

        Quaternion orientation = RailsOrientation(
            CalibrationCubeSpinAxis, CalibrationCubeSpinRateRadPerSec, _gameTimeSeconds, Quaternion.Identity);

        Matrix world = Matrix.CreateScale(rs)
                     * Matrix.CreateFromQuaternion(orientation)
                     * Matrix.CreateTranslation(renderPos);

        _meshRenderer.DrawDynamicLit(_calibrationCubeVb, _calibrationCubeIb, world, view, proj,
            Color.White, SceneLighting.SunDirection, new Color(SceneLighting.SunColour), SceneLighting.Ambient);

        _gd.RasterizerState   = RasterizerState.CullCounterClockwise;
        _gd.DepthStencilState = DepthStencilState.Default;
    }
}
