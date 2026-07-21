using Inferior.Core.Math;
using Inferior.Gameplay.Cockpit;
using Inferior.Gameplay.Engines;
using Inferior.Gameplay.Hull;
using Inferior.Gameplay.Hull.Authoring;
using Inferior.ObjectDesigner.Editing;
using Inferior.Rendering;
using Inferior.UI;
using Inferior.UI.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Inferior.ObjectDesigner;

public sealed class ObjectDesignerGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;
    private SpriteFont _font = null!;
    private UIManager _ui = null!;
    private MeshRenderer _meshRenderer = null!;
    private ShipMeshRenderer _shipRenderer = null!;
    private BasicEffect _lineEffect = null!;

    private MouseState _previousMouse;
    private KeyboardState _previousKeys;

    private ObjectDesignerSession _session = null!;
    private readonly OrthographicProjection _projection = new();
    private SemanticHullDebugMode _debugMode = SemanticHullDebugMode.Normal;
    private bool _showEdges = true;
    private bool _showVertices = true;
    private bool _showEngines = true;
    private bool _showCockpit = true;
    private bool _showBounds = true;
    private bool _showCargo = true;
    private float _yaw = -0.6f;
    private float _pitch = -0.25f;
    private float _distance = 42f;
    private double _time;
    private string _status = "";

    private bool _draggingVertex;
    private string? _dragVertexId;
    private DVec3 _dragStartPosition;
    private Point _dragStartMouse;

    private Panel _rightPanel = null!;
    private TextBox _xBox = null!;
    private TextBox _yBox = null!;
    private TextBox _zBox = null!;
    private Label _titleLabel = null!;
    private Label _selectionLabel = null!;
    private Label _validationLabel = null!;
    private bool _updatingTextBoxes;

    public ObjectDesignerGame()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1500,
            PreferredBackBufferHeight = 920,
            SynchronizeWithVerticalRetrace = true,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "Inferior Object Designer - Beren";
        Window.AllowUserResizing = true;
        Window.TextInput += (_, e) => InputState.PushTypedChar(e.Character);
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _font = Content.Load<SpriteFont>("Fonts/DefaultFont");
        _ui = new UIManager(GraphicsDevice, Theme.InferiorDark(_font));

        Effect lit = Content.Load<Effect>("Effects/LitSurface");
        Effect exhaust = Content.Load<Effect>("Effects/EngineExhaustGlow");
        _meshRenderer = new MeshRenderer(GraphicsDevice, lit);
        _shipRenderer = new ShipMeshRenderer(GraphicsDevice, _meshRenderer, exhaust);
        _lineEffect = new BasicEffect(GraphicsDevice)
        {
            VertexColorEnabled = true,
            LightingEnabled = false,
            TextureEnabled = false,
        };

        string assetPath = AssetPathResolver.ResolveAssetPath(BerenHullDefinitionFactory.AssetPath);
        _session = ObjectDesignerSession.Load(assetPath);
        _session.History.MarkClean();
        BuildUi();
    }

    protected override void Update(GameTime gameTime)
    {
        double dt = gameTime.ElapsedGameTime.TotalSeconds;
        _time += dt;
        InputState input = InputState.Capture(ref _previousMouse, ref _previousKeys);
        KeyboardState keys = Keyboard.GetState();

        if (keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl))
        {
            if (input.IsKeyPressed(Keys.Z))
            {
                _session.Undo();
                _shipRenderer.InvalidateSemanticHull(_session.HullDefinition.HullTypeId);
                RefreshUiText();
            }
            if (input.IsKeyPressed(Keys.Y))
            {
                _session.Redo();
                _shipRenderer.InvalidateSemanticHull(_session.HullDefinition.HullTypeId);
                RefreshUiText();
            }
            if (input.IsKeyPressed(Keys.S))
                TrySave();
        }

        if (input.IsKeyPressed(Keys.D1))
            _projection.Kind = ProjectionKind.Top;
        if (input.IsKeyPressed(Keys.D2))
            _projection.Kind = ProjectionKind.Side;
        if (input.IsKeyPressed(Keys.D3))
            _projection.Kind = ProjectionKind.Front;
        if (input.IsKeyPressed(Keys.F4))
            _debugMode = _debugMode == SemanticHullDebugMode.Normal ? SemanticHullDebugMode.SurfaceRoles : SemanticHullDebugMode.Normal;

        if (input.RightHeld && PerspectiveViewport.Contains(input.MousePosition))
        {
            _yaw -= (Mouse.GetState().X - _previousMouse.X) * 0.01f;
            _pitch = MathHelper.Clamp(_pitch - (Mouse.GetState().Y - _previousMouse.Y) * 0.01f, -1.35f, 1.35f);
        }

        if (input.ScrollDelta != 0)
        {
            if (PerspectiveViewport.Contains(input.MousePosition))
                _distance = MathHelper.Clamp(_distance - input.ScrollDelta * 0.02f, 8f, 140f);
            else if (OrthoViewport.Contains(input.MousePosition))
                _projection.PixelsPerMeter = MathHelper.Clamp(_projection.PixelsPerMeter + input.ScrollDelta * 0.02f, 4f, 120f);
        }

        _ui.Animate(dt);
        _ui.Update(dt, input);
        HandleOrthographicInput(input);
        RefreshDynamicLabels();

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(8, 10, 11));
        DrawPerspective();
        DrawOrthographic();
        _ui.Draw();
        base.Draw(gameTime);
    }

    private void DrawPerspective()
    {
        Viewport old = GraphicsDevice.Viewport;
        GraphicsDevice.Viewport = new Viewport(PerspectiveViewport);
        Matrix rotation = Matrix.CreateRotationX(_pitch) * Matrix.CreateRotationY(_yaw);
        Vector3 cameraPosition = Vector3.Transform(new Vector3(0, 4, _distance), rotation);
        Matrix view = Matrix.CreateLookAt(cameraPosition, Vector3.Zero, Vector3.UnitY);
        Matrix projection = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.ToRadians(55f),
            Math.Max(0.1f, PerspectiveViewport.Width / (float)PerspectiveViewport.Height),
            0.05f,
            400f);
        var camera = new Camera3D(DVec3.Zero, 1f);
        camera.SetPose(DVec3.Zero, Quaternion.Identity);

        IReadOnlyList<EngineMountPresentationSnapshot>? engines = _showEngines
            ? BuildEngineMountSnapshots(_session.HullDefinition)
            : null;
        CockpitPresentationSnapshot? cockpit = _showCockpit
            ? BuildCockpitSnapshot(_session.HullDefinition)
            : null;
        _shipRenderer.Draw(
            camera,
            view,
            projection,
            _session.HullDefinition.HullTypeId,
            DVec3.Zero,
            Quaternion.Identity,
            DetailLevel.Full,
            _debugMode,
            engines,
            engineModuleDebug: false,
            engineVisualTimeSeconds: _time,
            cockpit,
            _session.HullDefinition,
            renderScaleOverride: 1.0f);

        if (_showCargo)
            DrawCargoPreview(view, projection);
        GraphicsDevice.Viewport = old;
    }

    private void DrawOrthographic()
    {
        Rectangle vp = OrthoViewport;
        var lines = new List<VertexPositionColor>();
        AddGrid(lines, vp);
        if (_showEdges)
            AddFaceEdges(lines, vp);
        if (_showBounds)
            AddBounds(lines, vp);

        _lineEffect.World = Matrix.Identity;
        _lineEffect.View = Matrix.Identity;
        _lineEffect.Projection = Matrix.CreateOrthographicOffCenter(
            0,
            GraphicsDevice.Viewport.Width,
            GraphicsDevice.Viewport.Height,
            0,
            0,
            1);
        foreach (EffectPass pass in _lineEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            if (lines.Count > 0)
                GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, lines.ToArray(), 0, lines.Count / 2);
        }

        _spriteBatch.Begin(blendState: BlendState.AlphaBlend);
        _spriteBatch.DrawString(_font, $"{_projection.Kind} | H: {_projection.HorizontalAxisLabel} | V: {_projection.VerticalAxisLabel}", new Vector2(vp.X + 8, vp.Y + 8), Color.White, 0, Vector2.Zero, 0.85f, SpriteEffects.None, 0);
        if (_showVertices)
            DrawVertexMarkers(vp);
        _spriteBatch.End();
    }

    private void HandleOrthographicInput(InputState input)
    {
        if (_ui.FindAt(input.MousePosition) is not null)
            return;
        Rectangle viewport = OrthoViewport;
        if (input.LeftPressed && viewport.Contains(input.MousePosition))
        {
            string? vertexId = PickVertex(input.MousePosition, viewport);
            if (vertexId is not null)
            {
                _session.SelectedVertexId = vertexId;
                _draggingVertex = true;
                _dragVertexId = vertexId;
                _dragStartPosition = _session.GetVertexPosition(vertexId);
                _dragStartMouse = input.MousePosition;
                RefreshUiText();
            }
        }

        if (_draggingVertex && input.LeftHeld && _dragVertexId is not null)
        {
            Vector2 delta = (input.MousePosition - _dragStartMouse).ToVector2();
            _session.SetVertexPosition(_dragVertexId, _projection.ApplyScreenDelta(_dragStartPosition, delta));
            _shipRenderer.InvalidateSemanticHull(_session.HullDefinition.HullTypeId);
            RefreshUiText();
        }

        if (_draggingVertex && input.LeftReleased && _dragVertexId is not null)
        {
            DVec3 after = _session.GetVertexPosition(_dragVertexId);
            if ((after - _dragStartPosition).Length > 1e-9)
            {
                _session.SetVertexPosition(_dragVertexId, _dragStartPosition);
                _session.Execute(new MoveVertexCommand(_dragVertexId, _dragStartPosition, after, $"Move {_dragVertexId}"));
            }
            _draggingVertex = false;
            _dragVertexId = null;
            _shipRenderer.InvalidateSemanticHull(_session.HullDefinition.HullTypeId);
            RefreshUiText();
        }
    }

    private string? PickVertex(Point mouse, Rectangle viewport)
    {
        const float radius = 8f;
        string? best = null;
        float bestDistance = radius * radius;
        foreach (var vertex in _session.HullDefinition.VisualGeometry!.Vertices)
        {
            Vector2 screen = _projection.Project(vertex.Position, viewport);
            float distance = Vector2.DistanceSquared(screen, mouse.ToVector2());
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = vertex.Id;
            }
        }
        return best;
    }

    private void BuildUi()
    {
        _ui.Clear();
        int width = GraphicsDevice.Viewport.Width;
        int rightX = width - 330;
        var toolbar = new Panel(new Rectangle(0, 0, width, 42)) { ContentPadding = 6 };
        _ui.Add(toolbar);
        AddButton(toolbar, "Save", 0, TrySave);
        AddButton(toolbar, "Reload", 74, () => { _session.Reload(); _shipRenderer.InvalidateSemanticHull(_session.HullDefinition.HullTypeId); RefreshUiText(); });
        AddButton(toolbar, "Undo", 160, () => { _session.Undo(); _shipRenderer.InvalidateSemanticHull(_session.HullDefinition.HullTypeId); RefreshUiText(); });
        AddButton(toolbar, "Redo", 232, () => { _session.Redo(); _shipRenderer.InvalidateSemanticHull(_session.HullDefinition.HullTypeId); RefreshUiText(); });
        AddButton(toolbar, "Role", 304, () => _debugMode = _debugMode == SemanticHullDebugMode.Normal ? SemanticHullDebugMode.SurfaceRoles : SemanticHullDebugMode.Normal);
        AddButton(toolbar, "Top", 376, () => _projection.Kind = ProjectionKind.Top);
        AddButton(toolbar, "Side", 432, () => _projection.Kind = ProjectionKind.Side);
        AddButton(toolbar, "Front", 496, () => _projection.Kind = ProjectionKind.Front);

        _rightPanel = new Panel(new Rectangle(rightX, 42, 330, GraphicsDevice.Viewport.Height - 42)) { ContentPadding = 10 };
        _ui.Add(_rightPanel);
        _titleLabel = new Label("", new Rectangle(0, 0, 300, 24));
        _selectionLabel = new Label("", new Rectangle(0, 72, 300, 42)) { FontScale = 0.78f };
        _validationLabel = new Label("", new Rectangle(0, 250, 300, 430)) { FontScale = 0.72f };
        _rightPanel.Add(_titleLabel);
        _rightPanel.Add(new Label("Hierarchy", new Rectangle(0, 34, 300, 22)) { TextColor = new Color(170, 196, 204) });
        _rightPanel.Add(new Label("Beren / semantic vertices", new Rectangle(0, 54, 300, 20)) { FontScale = 0.75f });
        _rightPanel.Add(_selectionLabel);
        _rightPanel.Add(new Label("X", new Rectangle(0, 125, 20, 26)));
        _rightPanel.Add(new Label("Y", new Rectangle(0, 158, 20, 26)));
        _rightPanel.Add(new Label("Z", new Rectangle(0, 191, 20, 26)));
        _xBox = CoordinateBox(24, 122);
        _yBox = CoordinateBox(24, 155);
        _zBox = CoordinateBox(24, 188);
        _rightPanel.Add(_xBox);
        _rightPanel.Add(_yBox);
        _rightPanel.Add(_zBox);
        _rightPanel.Add(new Label("Validation", new Rectangle(0, 225, 300, 24)) { TextColor = new Color(170, 196, 204) });
        _rightPanel.Add(_validationLabel);
        RefreshUiText();
    }

    private void AddButton(Panel parent, string text, int x, Action action)
    {
        var button = new Button(text, new Rectangle(x, 0, 64, 30));
        button.Clicked += _ => action();
        parent.Add(button);
    }

    private TextBox CoordinateBox(int x, int y)
    {
        var box = new TextBox
        {
            Bounds = new Rectangle(x, y, 112, 28),
            TextFilter = null,
            MaxLength = 24,
        };
        box.Submitted += _ => TryApplyNumericEdit();
        return box;
    }

    private void TryApplyNumericEdit()
    {
        if (_updatingTextBoxes || _session.SelectedVertexId is null)
            return;
        if (!double.TryParse(_xBox.Text, out double x)
            || !double.TryParse(_yBox.Text, out double y)
            || !double.TryParse(_zBox.Text, out double z))
        {
            _status = "Invalid coordinate text.";
            return;
        }

        DVec3 before = _session.GetVertexPosition(_session.SelectedVertexId);
        DVec3 after = new(x, y, z);
        _session.Execute(new MoveVertexCommand(_session.SelectedVertexId, before, after, $"Edit {_session.SelectedVertexId}"));
        _shipRenderer.InvalidateSemanticHull(_session.HullDefinition.HullTypeId);
        RefreshUiText();
    }

    private void TrySave()
    {
        try
        {
            _session.Save();
            _status = "Saved.";
        }
        catch (Exception ex)
        {
            _status = ex.Message;
        }
        RefreshUiText();
    }

    private void RefreshUiText()
    {
        if (_xBox is null)
            return;
        _updatingTextBoxes = true;
        if (_session.SelectedVertexId is not null)
        {
            DVec3 p = _session.GetVertexPosition(_session.SelectedVertexId);
            _xBox.Text = p.X.ToString("0.###");
            _yBox.Text = p.Y.ToString("0.###");
            _zBox.Text = p.Z.ToString("0.###");
        }
        else
        {
            _xBox.Text = "";
            _yBox.Text = "";
            _zBox.Text = "";
        }
        _updatingTextBoxes = false;
        RefreshDynamicLabels();
    }

    private void RefreshDynamicLabels()
    {
        string dirty = _session.IsDirty ? "*" : "";
        _titleLabel.Text = $"{_session.HullDefinition.DisplayName}{dirty} ({_session.HullDefinition.HullTypeId})";
        _selectionLabel.Text = _session.SelectedVertexId is null
            ? "No vertex selected"
            : $"Selected vertex:\n{_session.SelectedVertexId}";
        IEnumerable<AuthoringDiagnostic> diagnostics = _session.Diagnostics.Take(12);
        string validation = _session.Diagnostics.Count == 0
            ? "No validation errors."
            : string.Join("\n", diagnostics.Select(d => $"{d.Severity}: {d.Message}"));
        _validationLabel.Text = string.IsNullOrWhiteSpace(_status) ? validation : $"{_status}\n{validation}";
    }

    private Rectangle PerspectiveViewport => new(0, 42, GraphicsDevice.Viewport.Width - 330, (GraphicsDevice.Viewport.Height - 42) * 2 / 3);
    private Rectangle OrthoViewport
    {
        get
        {
            Rectangle p = PerspectiveViewport;
            return new Rectangle(0, p.Bottom, p.Width, GraphicsDevice.Viewport.Height - p.Bottom);
        }
    }

    private void AddGrid(List<VertexPositionColor> lines, Rectangle vp)
    {
        for (int i = -20; i <= 20; i++)
        {
            Color c = i == 0 ? new Color(95, 110, 115) : new Color(34, 40, 42);
            AddScreenLine(lines, new Vector2(vp.X, vp.Y + vp.Height / 2f + i * _projection.PixelsPerMeter), new Vector2(vp.Right, vp.Y + vp.Height / 2f + i * _projection.PixelsPerMeter), c);
            AddScreenLine(lines, new Vector2(vp.X + vp.Width / 2f + i * _projection.PixelsPerMeter, vp.Y), new Vector2(vp.X + vp.Width / 2f + i * _projection.PixelsPerMeter, vp.Bottom), c);
        }
    }

    private void AddFaceEdges(List<VertexPositionColor> lines, Rectangle vp)
    {
        SemanticHullGeometry geometry = _session.HullDefinition.VisualGeometry!;
        Dictionary<string, DVec3> vertices = geometry.Vertices.ToDictionary(v => v.Id, v => v.Position);
        foreach (SemanticHullFace face in geometry.Faces)
        {
            Color colour = face.AssemblyId is not null ? new Color(82, 174, 105) : new Color(130, 145, 148);
            for (int i = 0; i < face.VertexIds.Count; i++)
            {
                if (!vertices.TryGetValue(face.VertexIds[i], out DVec3 a) || !vertices.TryGetValue(face.VertexIds[(i + 1) % face.VertexIds.Count], out DVec3 b))
                    continue;
                AddScreenLine(lines, _projection.Project(a, vp), _projection.Project(b, vp), colour);
            }
        }
    }

    private void AddBounds(List<VertexPositionColor> lines, Rectangle vp)
    {
        SemanticHullGeometry geometry = _session.HullDefinition.VisualGeometry!;
        double minX = geometry.Vertices.Min(v => v.Position.X);
        double maxX = geometry.Vertices.Max(v => v.Position.X);
        double minY = geometry.Vertices.Min(v => v.Position.Y);
        double maxY = geometry.Vertices.Max(v => v.Position.Y);
        double minZ = geometry.Vertices.Min(v => v.Position.Z);
        double maxZ = geometry.Vertices.Max(v => v.Position.Z);
        DVec3[] corners =
        [
            new(minX, minY, minZ), new(maxX, minY, minZ), new(maxX, maxY, minZ), new(minX, maxY, minZ),
            new(minX, minY, maxZ), new(maxX, minY, maxZ), new(maxX, maxY, maxZ), new(minX, maxY, maxZ),
        ];
        int[] edges = [0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7];
        for (int i = 0; i < edges.Length; i += 2)
            AddScreenLine(lines, _projection.Project(corners[edges[i]], vp), _projection.Project(corners[edges[i + 1]], vp), Color.White);
    }

    private void DrawVertexMarkers(Rectangle vp)
    {
        Texture2D pixel = TexturePixel;
        foreach (SemanticHullVertex vertex in _session.HullDefinition.VisualGeometry!.Vertices)
        {
            Vector2 p = _projection.Project(vertex.Position, vp);
            bool selected = string.Equals(vertex.Id, _session.SelectedVertexId, StringComparison.Ordinal);
            int size = selected ? 8 : 5;
            Color colour = selected ? Color.Yellow : new Color(215, 230, 230);
            _spriteBatch.Draw(pixel, new Rectangle((int)p.X - size / 2, (int)p.Y - size / 2, size, size), colour);
        }
    }

    private Texture2D? _pixel;
    private Texture2D TexturePixel
    {
        get
        {
            if (_pixel is not null)
                return _pixel;
            _pixel = new Texture2D(GraphicsDevice, 1, 1);
            _pixel.SetData([Color.White]);
            return _pixel;
        }
    }

    private void AddScreenLine(List<VertexPositionColor> lines, Vector2 a, Vector2 b, Color color)
    {
        lines.Add(new VertexPositionColor(new Vector3(a, 0), color));
        lines.Add(new VertexPositionColor(new Vector3(b, 0), color));
    }

    private void DrawCargoPreview(Matrix view, Matrix projection)
    {
        // The first slice only needs a wire/translucent design-volume preview; semantic hull remains authoritative.
        CargoArrangementDefinition? cargo = _session.HullDefinition.CargoArrangement;
        if (cargo is null)
            return;
        var lines = new List<VertexPositionColor>();
        foreach (CargoContainerPlacementDefinition placement in cargo.ContainerPlacements)
            AddBoxLines(lines, placement.OccupiedBoundsMeters.Min, placement.OccupiedBoundsMeters.Max, new Color(200, 160, 70));
        _lineEffect.World = Matrix.Identity;
        _lineEffect.View = view;
        _lineEffect.Projection = projection;
        foreach (EffectPass pass in _lineEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            if (lines.Count > 0)
                GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, lines.ToArray(), 0, lines.Count / 2);
        }
    }

    private static void AddBoxLines(List<VertexPositionColor> lines, DVec3 min, DVec3 max, Color color)
    {
        Vector3[] c =
        [
            min.ToVector3(), new((float)max.X, (float)min.Y, (float)min.Z), new((float)max.X, (float)max.Y, (float)min.Z), new((float)min.X, (float)max.Y, (float)min.Z),
            new((float)min.X, (float)min.Y, (float)max.Z), new((float)max.X, (float)min.Y, (float)max.Z), max.ToVector3(), new((float)min.X, (float)max.Y, (float)max.Z),
        ];
        int[] e = [0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7];
        for (int i = 0; i < e.Length; i += 2)
        {
            lines.Add(new VertexPositionColor(c[e[i]], color));
            lines.Add(new VertexPositionColor(c[e[i + 1]], color));
        }
    }

    private static IReadOnlyList<EngineMountPresentationSnapshot> BuildEngineMountSnapshots(HullDefinition hull)
    {
        var snapshots = new List<EngineMountPresentationSnapshot>();
        if (hull.VisualGeometry is null)
            return snapshots;
        foreach (AttachmentPortDefinition port in hull.VisualGeometry.AttachmentPorts.Where(port => port.Capabilities.HasFlag(AttachmentCapability.Engine)))
        {
            if (port.ComponentSlotId is null || port.EngineMountStandardId is null || port.EngineMountSide is null)
                continue;
            var mount = new EngineMount(
                port.PortId,
                port.ComponentSlotId,
                port.EngineMountStandardId,
                port.EngineMountSide.Value,
                new EngineMountPose(port.Position, port.Normal, port.Up),
                port.MountRootPosition,
                port.AttachmentInterfacePosition);
            HullSlot? slot = hull.Slots.SingleOrDefault(slot => string.Equals(slot.SlotId, port.ComponentSlotId, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(slot?.DefaultComponentDefinitionId))
                EngineInstallationGenerator.Install(EngineDefinitionLibrary.GetVariant(slot.DefaultComponentDefinitionId), mount);
            EngineInstance? engine = mount.InstalledEngine;
            EnginePresentationSnapshot? engineSnapshot = engine?.GeometryTransform is not null && engine.Variant.Engine.VisualGeometry is not null
                ? new EnginePresentationSnapshot(engine.InstanceId, engine.Variant.VariantId, engine.Variant.Engine.VisualGeometry, engine.Variant.Engine.VisualDefinition, engine.VisualState, engine.GeometryTransform, engine.DamageFraction, engine.WearFraction)
                : null;
            snapshots.Add(new EngineMountPresentationSnapshot(mount.MountId, mount.ComponentSlotId, mount.MountStandardId, mount.Side, mount.Pose, mount.HullRootPosition, mount.AttachmentInterfacePosition, engineSnapshot));
        }
        return snapshots;
    }

    private static CockpitPresentationSnapshot? BuildCockpitSnapshot(HullDefinition hull)
    {
        CockpitMountDefinition? mount = hull.CockpitMounts.SingleOrDefault(mount => !string.IsNullOrWhiteSpace(mount.DefaultCockpitDefinitionId));
        if (mount?.DefaultCockpitDefinitionId is null)
            return null;
        var installed = new InstalledCockpit
        {
            MountId = mount.MountId,
            DefinitionId = mount.DefaultCockpitDefinitionId,
            InstallationRotation = CockpitRotationStep.Deg0,
        };
        CockpitModuleDefinition definition = CockpitDefinitionLibrary.Get(installed.DefinitionId);
        DVec3 root = installed.ResolveShipLocalRootPosition(mount, definition);
        return new CockpitPresentationSnapshot(
            installed.DefinitionId,
            new DVec3(root.X / Camera3D.RenderScale, root.Y / Camera3D.RenderScale, root.Z / Camera3D.RenderScale),
            installed.ResolveShipLocalRootOrientation(mount, definition),
            false,
            false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pixel?.Dispose();
            _lineEffect?.Dispose();
            _shipRenderer?.Dispose();
            _meshRenderer?.Dispose();
            _ui?.Dispose();
            _spriteBatch?.Dispose();
        }
        base.Dispose(disposing);
    }
}
