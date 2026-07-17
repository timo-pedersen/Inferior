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

        ChaseCameraTargets? chaseTargets = f3JustPressed && _thirdPersonMode
            ? CalculateChaseCameraTargets(
                _frameShipSnap.Position,
                _frameShipSnap.Forward,
                _frameShipSnap.Up)
            : null;
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
