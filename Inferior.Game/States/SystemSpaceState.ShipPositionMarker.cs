using System.Globalization;
using System.Text;
using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.States;

public sealed partial class SystemSpaceState
{
    private const float ShipPositionMarkerHalfSizeMeters = 12.0f;
    private const float ShipPositionMarkerAxisLengthMeters = 40.0f;

    private BasicEffect? _shipPositionMarkerEffect;
    private bool _shipPositionMarkerEnabled;
    private int _shipPositionMarkerObservedRelocationSequence;
    private bool _shipRenderDiagnosticPending;

    private void InitializeShipPositionMarker()
    {
        _shipPositionMarkerEffect?.Dispose();
        _shipPositionMarkerEffect = new BasicEffect(_gd)
        {
            VertexColorEnabled = true,
            LightingEnabled = false,
            TextureEnabled = false,
        };
        _shipPositionMarkerEnabled = false;
        _shipPositionMarkerObservedRelocationSequence =
            _frameShipSnap?.RelocationSequence ?? 0;
    }

    private void DisposeShipPositionMarker()
    {
        _shipPositionMarkerEffect?.Dispose();
        _shipPositionMarkerEffect = null;
        _shipPositionMarkerEnabled = false;
    }

    private void DrawShipPositionMarker()
    {
        if (!_shipPositionMarkerEnabled ||
            _frameShipSnap is null ||
            _shipPositionMarkerEffect is null)
        {
            return;
        }

        Vector3 renderPosition = _camera.ToRenderSpace(_frameShipSnap.Position);
        Matrix world = Matrix.CreateScale((float)Camera3D.RenderScale)
                     * Matrix.CreateTranslation(renderPosition);

        _shipPositionMarkerEffect.World = world;
        _shipPositionMarkerEffect.View = _effect.View;
        _shipPositionMarkerEffect.Projection = _effect.Projection;

        VertexPositionColor[] lines = BuildShipPositionMarkerLines();
        _gd.BlendState = BlendState.Opaque;
        _gd.RasterizerState = RasterizerState.CullNone;
        _gd.DepthStencilState = DepthStencilState.None;
        foreach (var pass in _shipPositionMarkerEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawUserPrimitives(PrimitiveType.LineList, lines, 0, lines.Length / 2);
        }

        _gd.RasterizerState = RasterizerState.CullCounterClockwise;
        _gd.DepthStencilState = DepthStencilState.Default;
    }

    private void UpdateShipPositionMarkerDiagnostics(bool toggledOn, bool f3JustPressed)
    {
        if (_frameShipSnap is null)
            return;

        bool relocationChanged =
            _frameShipSnap.RelocationSequence != _shipPositionMarkerObservedRelocationSequence;
        _shipPositionMarkerObservedRelocationSequence = _frameShipSnap.RelocationSequence;

        bool markerLogRequested =
            _shipPositionMarkerEnabled && (toggledOn || relocationChanged);
        if (!markerLogRequested && !f3JustPressed)
            return;

        ChaseCameraTargets? chaseTargets = f3JustPressed && _chaseCamera.IsActive
            ? new ChaseCameraTargets(
                _frameShipSnap.Position + ChaseCameraState.Transform(
                    _chaseCamera.DesiredHullLocalOffset,
                    _frameShipSnap.Orientation),
                _frameShipSnap.Position)
            : null;
        if (chaseTargets is not null)
            _shipRenderDiagnosticPending = true;
        string log = FormatShipPositionMarkerLog(
            _frameShipSnap.Position,
            _frameShipSnap.Position,
            _frameShipSnap.Position,
            _camera.UniversePosition,
            chaseTargets);
        Console.WriteLine(log);

        string path = Path.Combine(AppContext.BaseDirectory, "ship_chase_camera_diagnostic.log");
        File.AppendAllText(path, $"{DateTimeOffset.Now:O}\n{log}\n");
        DataBus.System.Publish(Topics.System.All,
            new SystemMessage($"Ship chase diagnostic written: {path}", SystemMessagePriority.Info));
    }

    private void WriteShipRenderDiagnostic(
        SpaceSimulation.ShipSnapshot snapshot,
        ShipRenderTransformDiagnostic diagnostic)
    {
        if (!_shipRenderDiagnosticPending)
            return;

        _shipRenderDiagnosticPending = false;
        DVec3 difference = diagnostic.ShipPosition - snapshot.Position;
        DVec3 cameraDifference = diagnostic.CameraPosition - _camera.UniversePosition;
        Vector3 translationDifference =
            diagnostic.WorldTranslation - diagnostic.CameraRelativeRenderPosition;
        float appliedCameraViewDifference =
            MaxMatrixElementDifference(diagnostic.AppliedView, diagnostic.CameraView);
        float appliedSceneViewDifference =
            MaxMatrixElementDifference(diagnostic.AppliedView, _effect.View);
        float appliedSceneProjectionDifference =
            MaxMatrixElementDifference(diagnostic.AppliedProjection, _effect.Projection);
        string log =
            "[ShipRender]\n" +
            $"Ship render position: {FormatVector(diagnostic.ShipPosition)}\n" +
            $"Ship render orientation: {FormatQuaternion(diagnostic.ShipOrientation)}\n" +
            $"Ship snapshot position: {FormatVector(snapshot.Position)}\n" +
            $"Difference vector: {FormatVector(difference)}\n" +
            $"Camera position: {FormatVector(diagnostic.CameraPosition)}\n" +
            $"Camera-relative render position: {FormatVector(diagnostic.CameraRelativeRenderPosition)}\n" +
            $"World matrix translation: {FormatVector(diagnostic.WorldTranslation)}\n" +
            $"Translation difference: {FormatVector(translationDifference)}\n" +
            $"Render path: {diagnostic.RenderPath}\n" +
            "[CameraTruth]\n" +
            $"Simulation tick number: {snapshot.TickSequence}\n" +
            $"Snapshot timestamp / SimTime: {snapshot.SimTime.ToString("R", CultureInfo.InvariantCulture)}\n" +
            $"Snapshot ship position / HUD truth: {FormatVector(snapshot.Position)}\n" +
            $"Normal camera position: {FormatVector(snapshot.CockpitWorldPosition)}\n" +
            $"Chase camera position: {FormatVector(diagnostic.CameraPosition)}\n" +
            $"Graphics view camera position / render origin: {FormatVector(diagnostic.CameraPosition)}\n" +
            $"HUD ship position: {FormatVector(snapshot.Position)}\n" +
            $"Target-distance origin position: {FormatVector(snapshot.Position)}\n" +
            $"Targeting projection origin position: {FormatVector(diagnostic.CameraPosition)}\n" +
            "[RenderCamera]\n" +
            $"Render camera position: {FormatVector(diagnostic.CameraPosition)}\n" +
            $"Render camera orientation: {FormatQuaternion(diagnostic.CameraOrientation)}\n" +
            $"View matrix translation: {FormatVector(diagnostic.AppliedView.Translation)}\n" +
            $"Marker camera/reference position: {FormatVector(_camera.UniversePosition)}\n" +
            $"Marker camera/reference orientation: {FormatQuaternion(_camera.Orientation)}\n" +
            $"Render/gameplay camera difference: {FormatVector(cameraDifference)}\n" +
            $"Applied view vs camera view max element difference: {FormatFloat(appliedCameraViewDifference)}\n" +
            $"Applied view vs current scene view max element difference: {FormatFloat(appliedSceneViewDifference)}\n" +
            $"Applied projection vs current scene projection max element difference: {FormatFloat(appliedSceneProjectionDifference)}\n" +
            "Camera view matrix:\n" +
            $"{FormatMatrix(diagnostic.CameraView)}\n" +
            "Applied render view matrix:\n" +
            $"{FormatMatrix(diagnostic.AppliedView)}\n" +
            "Applied render projection matrix:\n" +
            $"{FormatMatrix(diagnostic.AppliedProjection)}\n" +
            "Origin-shift note: camera world translation is applied by ToRenderSpace; " +
            "the view matrix intentionally carries orientation without universe-position translation.\n";

        Console.WriteLine(log);
        string path = Path.Combine(AppContext.BaseDirectory, "ship_render_transform_diagnostic.log");
        File.AppendAllText(path, $"{DateTimeOffset.Now:O}\n{log}\n");
        DataBus.System.Publish(Topics.System.All,
            new SystemMessage($"Ship render diagnostic written: {path}", SystemMessagePriority.Info));
    }

    internal static string FormatShipPositionMarkerLog(
        DVec3 simulationPosition,
        DVec3 snapshotPosition,
        DVec3 presentationPosition,
        DVec3 cameraPosition,
        ChaseCameraTargets? chaseTargets)
    {
        var text = new StringBuilder();
        text.AppendLine("[ShipMarker]");
        AppendPosition(text, "Sim position", simulationPosition);
        AppendPosition(text, "Snapshot ship position", snapshotPosition);
        AppendPosition(text, "Presentation ship position / render source", presentationPosition);
        if (chaseTargets is { } targets)
        {
            AppendPosition(text, "Camera desired position", targets.DesiredPosition);
            AppendPosition(text, "Camera target", targets.LookTarget);
        }
        AppendPosition(text, "Camera position", cameraPosition);
        return text.ToString();
    }

    internal static VertexPositionColor[] BuildShipPositionMarkerLines()
    {
        const float h = ShipPositionMarkerHalfSizeMeters;
        Vector3[] corners =
        [
            new(-h, -h, -h), new(h, -h, -h),
            new(h, h, -h), new(-h, h, -h),
            new(-h, -h, h), new(h, -h, h),
            new(h, h, h), new(-h, h, h),
        ];
        int[] edges =
        [
            0, 1, 1, 2, 2, 3, 3, 0,
            4, 5, 5, 6, 6, 7, 7, 4,
            0, 4, 1, 5, 2, 6, 3, 7,
        ];

        var lines = new List<VertexPositionColor>(30);
        for (int i = 0; i < edges.Length; i += 2)
            AddMarkerLine(lines, corners[edges[i]], corners[edges[i + 1]], Color.Yellow);

        AddMarkerLine(lines, Vector3.Zero, Vector3.UnitX * ShipPositionMarkerAxisLengthMeters, Color.Red);
        AddMarkerLine(lines, Vector3.Zero, Vector3.UnitY * ShipPositionMarkerAxisLengthMeters, Color.LimeGreen);
        AddMarkerLine(lines, Vector3.Zero, Vector3.UnitZ * ShipPositionMarkerAxisLengthMeters, Color.Cyan);
        return lines.ToArray();
    }

    private static void AppendPosition(StringBuilder text, string label, DVec3 position)
    {
        text.AppendLine($"{label}:");
        text.AppendLine($"    X: {position.X.ToString("R", CultureInfo.InvariantCulture)}");
        text.AppendLine($"    Y: {position.Y.ToString("R", CultureInfo.InvariantCulture)}");
        text.AppendLine($"    Z: {position.Z.ToString("R", CultureInfo.InvariantCulture)}");
    }

    private static string FormatVector(DVec3 value)
        => FormattableString.Invariant($"({value.X:R}, {value.Y:R}, {value.Z:R})");

    private static string FormatVector(Vector3 value)
        => FormattableString.Invariant($"({value.X:R}, {value.Y:R}, {value.Z:R})");

    private static string FormatQuaternion(Quaternion value)
        => FormattableString.Invariant($"({value.X:R}, {value.Y:R}, {value.Z:R}, {value.W:R})");

    private static string FormatFloat(float value)
        => value.ToString("R", CultureInfo.InvariantCulture);

    private static string FormatMatrix(Matrix value)
        => string.Join(
            '\n',
            FormattableString.Invariant($"    [{value.M11:R}, {value.M12:R}, {value.M13:R}, {value.M14:R}]"),
            FormattableString.Invariant($"    [{value.M21:R}, {value.M22:R}, {value.M23:R}, {value.M24:R}]"),
            FormattableString.Invariant($"    [{value.M31:R}, {value.M32:R}, {value.M33:R}, {value.M34:R}]"),
            FormattableString.Invariant($"    [{value.M41:R}, {value.M42:R}, {value.M43:R}, {value.M44:R}]"));

    internal static float MaxMatrixElementDifference(Matrix left, Matrix right)
    {
        return MathF.Max(
            MathF.Max(
                MathF.Max(
                    MathF.Max(MathF.Abs(left.M11 - right.M11), MathF.Abs(left.M12 - right.M12)),
                    MathF.Max(MathF.Abs(left.M13 - right.M13), MathF.Abs(left.M14 - right.M14))),
                MathF.Max(
                    MathF.Max(MathF.Abs(left.M21 - right.M21), MathF.Abs(left.M22 - right.M22)),
                    MathF.Max(MathF.Abs(left.M23 - right.M23), MathF.Abs(left.M24 - right.M24)))),
            MathF.Max(
                MathF.Max(
                    MathF.Max(MathF.Abs(left.M31 - right.M31), MathF.Abs(left.M32 - right.M32)),
                    MathF.Max(MathF.Abs(left.M33 - right.M33), MathF.Abs(left.M34 - right.M34))),
                MathF.Max(
                    MathF.Max(MathF.Abs(left.M41 - right.M41), MathF.Abs(left.M42 - right.M42)),
                    MathF.Max(MathF.Abs(left.M43 - right.M43), MathF.Abs(left.M44 - right.M44)))));
    }

    private static void AddMarkerLine(
        List<VertexPositionColor> lines,
        Vector3 start,
        Vector3 end,
        Color colour)
    {
        lines.Add(new VertexPositionColor(start, colour));
        lines.Add(new VertexPositionColor(end, colour));
    }
}
