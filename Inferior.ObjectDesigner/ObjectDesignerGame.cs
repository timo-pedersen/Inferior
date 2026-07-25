using Inferior.Core.Math;
using Inferior.ObjectDesigner.Controls;
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
    private EditingConstraintMode _constraintMode = EditingConstraintMode.ViewPlane;
    private float _yaw = -0.6f;
    private float _pitch = -0.25f;
    private float _distance = 42f;
    private Vector3 _previewTarget = Vector3.Zero;
    private double _time;
    private string _status = "";

    private VertexDragOperation? _vertexDrag;
    private bool _panningOrtho;
    private Point _selectionStartMouse;
    private bool _rectangleSelecting;

    private GridPanel _rootLayout = null!;
    private DesignerSurfaceControl _perspectiveSurface = null!;
    private DesignerSurfaceControl _orthoSurface = null!;
    private Panel _propertiesPanel = null!;
    private TextBlock _validationBlock = null!;
    private TextBox _xBox = null!;
    private TextBox _yBox = null!;
    private TextBox _zBox = null!;
    private Label _titleLabel = null!;
    private Label _selectionLabel = null!;
    private IncidentFaceRow[] _faceRows = [];
    private readonly Dictionary<IncidentFaceRow, string> _faceRowIds = [];
    private Label _statusLabel = null!;
    private ChoiceGroup<ProjectionKind> _projectionChoices = null!;
    private ChoiceGroup<EditingConstraintMode> _constraintChoices = null!;
    private bool _updatingTextBoxes;
    private RenderTarget2D? _previewTargetTexture;
    private static readonly RasterizerState ScissorLineState = new() { ScissorTestEnable = true, CullMode = CullMode.None };

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
            SetProjection(ProjectionKind.Top);
        if (input.IsKeyPressed(Keys.D2))
            SetProjection(ProjectionKind.Side);
        if (input.IsKeyPressed(Keys.D3))
            SetProjection(ProjectionKind.Front);
        if (input.IsKeyPressed(Keys.F4))
            _debugMode = _debugMode == SemanticHullDebugMode.Normal ? SemanticHullDebugMode.SurfaceRoles : SemanticHullDebugMode.Normal;
        if (input.IsKeyPressed(Keys.G))
        {
            if (_session.CycleActiveFace(input.Shift ? -1 : 1))
                _status = $"Active face: {_session.ActiveFaceId}";
            else
                _status = "No incident faces for active vertex.";
            RefreshUiText();
        }

        if (input.ScrollDelta != 0)
        {
            if (PerspectiveViewport.Contains(input.MousePosition))
                _distance = MathHelper.Clamp(_distance - input.ScrollDelta * 0.02f, 8f, 140f);
            else if (OrthoViewport.Contains(input.MousePosition))
                _projection.PixelsPerMeter = MathHelper.Clamp(_projection.PixelsPerMeter + input.ScrollDelta * 0.02f, 4f, 120f);
        }

        _rootLayout.Bounds = new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        _ui.Animate(dt);
        _ui.Update(dt, input);
        HandlePerspectiveInput(input);
        HandleOrthographicInput(input);
        RefreshDynamicLabels();

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        RenderPerspectiveTarget();
        GraphicsDevice.Clear(new Color(8, 10, 11));
        _ui.Draw();
        base.Draw(gameTime);
    }

    private void RenderPerspectiveTarget()
    {
        if (_perspectiveSurface is null)
            return;

        Rectangle viewport = PerspectiveViewport;
        if (viewport.Width <= 2 || viewport.Height <= 2)
            return;

        Rectangle clip = Rectangle.Intersect(viewport, _perspectiveSurface.EffectiveClipBounds);
        if (clip.Width <= 2 || clip.Height <= 2)
            return;
        ValidateSurfaceRect(_perspectiveSurface, clip);

        EnsurePreviewTarget(viewport.Width, viewport.Height);
        RenderTarget2D target = _previewTargetTexture!;
        RenderTargetBinding[] oldTargets = GraphicsDevice.GetRenderTargets();
        Viewport oldViewport = GraphicsDevice.Viewport;
        Rectangle oldScissor = GraphicsDevice.ScissorRectangle;
        RasterizerState oldRasterizer = GraphicsDevice.RasterizerState;
        BlendState oldBlend = GraphicsDevice.BlendState;
        DepthStencilState oldDepth = GraphicsDevice.DepthStencilState;
        SamplerState oldSampler0 = GraphicsDevice.SamplerStates[0];

        GraphicsDevice.SetRenderTarget(target);
        try
        {
            GraphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(8, 10, 11), 1f, 0);
            Matrix rotation = Matrix.CreateRotationX(_pitch) * Matrix.CreateRotationY(_yaw);
            Vector3 cameraPosition = _previewTarget + Vector3.Transform(new Vector3(0, 4, _distance), rotation);
            Matrix view = Matrix.CreateLookAt(cameraPosition, _previewTarget, Vector3.UnitY);
            Matrix projection = Matrix.CreatePerspectiveFieldOfView(
                MathHelper.ToRadians(55f),
                Math.Max(0.1f, viewport.Width / (float)viewport.Height),
                0.05f,
                400f);
            var camera = new Camera3D(DVec3.Zero, 1f);
            camera.SetPose(DVec3.Zero, Quaternion.Identity);
            DynamicLitMaterialSettings material = DynamicLitMaterialSettings.Tight;

            HullDefinition previewHull = _session.PreviewHullDefinition;
            IReadOnlyList<EngineMountPresentationSnapshot>? engines = _showEngines
                ? BuildEngineMountSnapshots(previewHull)
                : null;
            CockpitPresentationSnapshot? cockpit = _showCockpit
                ? BuildCockpitSnapshot(previewHull)
                : null;
            _shipRenderer.Draw(
                camera,
                view,
                projection,
                previewHull.HullTypeId,
                DVec3.Zero,
                Quaternion.Identity,
                DetailLevel.Full,
                specularStrength: material.SpecularStrength,
                specularShininess: material.SpecularShininess,
                _debugMode,
                engines,
                engineModuleDebug: false,
                engineVisualTimeSeconds: _time,
                cockpit,
                previewHull,
                renderScaleOverride: 1.0f,
                eyePositionWorld: cameraPosition);
            DrawPerspectiveEditorOverlay(view, projection);

            if (_showCargo)
                DrawCargoPreview(view, projection);
        }
        finally
        {
            GraphicsDevice.SetRenderTargets(oldTargets);
            GraphicsDevice.Viewport = oldViewport;
            GraphicsDevice.ScissorRectangle = oldScissor;
            GraphicsDevice.RasterizerState = oldRasterizer;
            GraphicsDevice.BlendState = oldBlend;
            GraphicsDevice.DepthStencilState = oldDepth;
            GraphicsDevice.SamplerStates[0] = oldSampler0;
        }
    }

    private void DrawPerspectiveTexture(SpriteBatch sb, UIRenderer renderer, Rectangle viewport)
    {
        if (_previewTargetTexture is null || viewport.Width <= 2 || viewport.Height <= 2)
            return;
        renderer.FillRect(sb, viewport, new Color(8, 10, 11));
        sb.Draw(_previewTargetTexture, viewport, Color.White);
        if (_session.IsPreviewStale)
            renderer.DrawText(sb, "Preview using last valid hull", new Vector2(viewport.X + 10, viewport.Y + 28), _font, 0.78f, new Color(230, 190, 80));
    }

    private void DrawPerspectiveEditorOverlay(Matrix view, Matrix projection)
    {
        ActiveFaceOverlayData overlay = _session.GetActiveFaceOverlayData();
        IReadOnlyList<DVec3> vertices = overlay.FaceVertices;
        if (vertices.Count == 0 && overlay.ActiveVertexId is null)
            return;

        var lines = new List<VertexPositionColor>();
        if (vertices.Count >= 2)
        {
            DVec3 offset = ActiveFaceNormalForOverlay(vertices) * 0.025;
            for (int i = 0; i < vertices.Count; i++)
                AddWorldLine(lines, vertices[i] + offset, vertices[(i + 1) % vertices.Count] + offset, new Color(80, 230, 255, 220));
        }

        if (overlay.ActiveVertexPosition is { } activeVertexPosition)
            AddWorldCross(lines, activeVertexPosition, 0.35, new Color(255, 255, 80, 255));

        if (lines.Count == 0)
            return;

        _lineEffect.World = Matrix.Identity;
        _lineEffect.View = view;
        _lineEffect.Projection = projection;
        GraphicsDevice.BlendState = BlendState.AlphaBlend;
        GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        foreach (EffectPass pass in _lineEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, lines.ToArray(), 0, lines.Count / 2);
        }
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
    }

    private void EnsurePreviewTarget(int width, int height)
    {
        if (_previewTargetTexture is not null
            && _previewTargetTexture.Width == width
            && _previewTargetTexture.Height == height)
        {
            return;
        }
        _previewTargetTexture?.Dispose();
        _previewTargetTexture = new RenderTarget2D(GraphicsDevice, width, height, false, SurfaceFormat.Color, DepthFormat.Depth24);
    }

    private void DrawOrthographic(UiCustomDrawContext context)
    {
        Rectangle vp = context.ClipBounds;
        ValidateSurfaceRect(_orthoSurface, vp);
        if (vp.Width <= 2 || vp.Height <= 2)
            return;
        var lines = new List<VertexPositionColor>();
        AddGrid(lines, vp);
        if (_showEdges)
            AddFaceEdges(lines, vp);
        AddActiveFaceEdges(lines, vp);
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
        GraphicsDevice.ScissorRectangle = vp;
        GraphicsDevice.RasterizerState = ScissorLineState;
        foreach (EffectPass pass in _lineEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            if (lines.Count > 0)
                GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, lines.ToArray(), 0, lines.Count / 2);
        }

        _spriteBatch.Begin(blendState: BlendState.AlphaBlend);
        _spriteBatch.DrawString(_font, $"{_projection.Kind} | H: {_projection.HorizontalAxisLabel} | V: {_projection.VerticalAxisLabel}", new Vector2(vp.X + 8, vp.Y + 28), Color.White, 0, Vector2.Zero, 0.85f, SpriteEffects.None, 0);
        if (_showVertices)
            DrawVertexMarkers(vp);
        if (_rectangleSelecting)
            DrawSelectionRectangle();
        _spriteBatch.End();
    }

    private void HandleOrthographicInput(InputState input)
    {
        Control? hit = _ui.FindAt(input.MousePosition);
        if (hit is not null && !ReferenceEquals(hit, _orthoSurface))
            return;
        Rectangle viewport = OrthoViewport;
        if (input.IsKeyPressed(Keys.Escape))
        {
            if (_vertexDrag is not null)
            {
                _vertexDrag.Restore(_session);
                _vertexDrag = null;
                _shipRenderer.InvalidateSemanticHull(_session.PreviewHullDefinition.HullTypeId);
                RefreshUiText();
                return;
            }
            _session.ClearSelection();
            RefreshUiText();
            return;
        }

        if (input.MiddlePressed && viewport.Contains(input.MousePosition))
        {
            _panningOrtho = true;
            return;
        }

        if (_panningOrtho && input.MiddleReleased)
            _panningOrtho = false;
        if (_panningOrtho && input.MouseDelta != Point.Zero)
        {
            _projection.PanPixels += input.MouseDelta.ToVector2();
            return;
        }

        if (input.LeftPressed && viewport.Contains(input.MousePosition))
        {
            IReadOnlyList<VertexHitCandidate> candidates = GetVertexHitCandidates(input.MousePosition, viewport);
            string? vertexId = candidates.FirstOrDefault()?.VertexId;
            if (vertexId is not null)
            {
                _vertexDrag = null;
                if (input.Ctrl)
                {
                    _session.ToggleVertexSelection(vertexId);
                    _status = candidates.Count > 1
                        ? $"{candidates.Count} vertices overlap here; selected {vertexId}"
                        : "";
                }
                else
                {
                    bool hasDragSelection = _session.BeginVertexDragSelection(vertexId, ctrl: false);
                    if (candidates.Count > 1)
                        _status = $"{candidates.Count} vertices overlap here; selected {vertexId}";
                    if (hasDragSelection)
                    {
                        if (VertexDragOperation.TryCapture(_session, _constraintMode, input.MousePosition, _projection, viewport, out VertexDragOperation? drag, out string? failure))
                        {
                            _vertexDrag = drag;
                            _status = failure ?? _status;
                        }
                        else
                        {
                            _status = failure ?? "Cannot start vertex drag.";
                        }
                    }
                }
                RefreshUiText();
            }
            else
            {
                if (input.Ctrl)
                    return;
                _rectangleSelecting = true;
                _selectionStartMouse = input.MousePosition;
                RefreshUiText();
            }
        }

        if (_vertexDrag is not null && input.LeftHeld && _vertexDrag.OriginalPositions.Count > 0)
        {
            try
            {
                _vertexDrag.Apply(_session, _projection, input.MousePosition, input.Shift);
                _shipRenderer.InvalidateSemanticHull(_session.PreviewHullDefinition.HullTypeId);
                _status = _vertexDrag.ShiftDragStatus ?? "";
            }
            catch (InvalidOperationException ex)
            {
                _status = ex.Message;
            }
            RefreshUiText();
        }

        if (_vertexDrag is not null && input.LeftReleased && _vertexDrag.OriginalPositions.Count > 0)
        {
            IReadOnlyDictionary<string, DVec3> before = _vertexDrag.OriginalPositions;
            Dictionary<string, DVec3> after = before.Keys.ToDictionary(id => id, id => _session.GetVertexPosition(id), StringComparer.Ordinal);
            if (after.Any(pair => (pair.Value - before[pair.Key]).Length > 1e-9))
            {
                _vertexDrag.Restore(_session);
                _session.Execute(new MoveVerticesCommand(before, after, $"Move {before.Count} vertices"));
            }
            _vertexDrag = null;
            _shipRenderer.InvalidateSemanticHull(_session.PreviewHullDefinition.HullTypeId);
            RefreshUiText();
        }

        if (_rectangleSelecting && input.LeftReleased)
        {
            Rectangle selection = NormalizedRectangle(_selectionStartMouse, input.MousePosition);
            IEnumerable<string> selected = _session.HullDefinition.VisualGeometry!.Vertices
                .Where(vertex => selection.Contains(_projection.Project(vertex.Position, viewport).ToPoint()))
                .Select(vertex => vertex.Id);
            _session.SelectVertices(selected, replace: true);
            _rectangleSelecting = false;
            RefreshUiText();
        }
    }

    private void HandlePerspectiveInput(InputState input)
    {
        Control? hit = _ui.FindAt(input.MousePosition);
        if (hit is not null && !ReferenceEquals(hit, _perspectiveSurface))
            return;
        if (!PerspectiveViewport.Contains(input.MousePosition))
            return;

        Point delta = input.MouseDelta;
        if (input.LeftHeld && delta != Point.Zero)
        {
            _yaw -= delta.X * 0.01f;
            _pitch = MathHelper.Clamp(_pitch - delta.Y * 0.01f, -1.35f, 1.35f);
        }
        else if (input.MiddleHeld && delta != Point.Zero)
        {
            _previewTarget += new Vector3(-delta.X * 0.025f, delta.Y * 0.025f, 0f);
        }
        else if (input.RightHeld && delta != Point.Zero)
        {
            Vector3 light = SceneLighting.SunDirection;
            Matrix rotate = Matrix.CreateRotationY(-delta.X * 0.01f) * Matrix.CreateRotationX(-delta.Y * 0.01f);
            SceneLighting.SunDirection = Vector3.Normalize(Vector3.TransformNormal(light, rotate));
        }
    }

    private string? PickVertex(Point mouse, Rectangle viewport)
        => GetVertexHitCandidates(mouse, viewport).FirstOrDefault()?.VertexId;

    private IReadOnlyList<VertexHitCandidate> GetVertexHitCandidates(Point mouse, Rectangle viewport)
        => OrthographicVertexHitTester.GetVertexHitCandidates(
            _session.HullDefinition.VisualGeometry!.Vertices,
            _projection,
            viewport,
            mouse);

    private void BuildUi()
    {
        _ui.Clear();
        _rootLayout = new GridPanel
        {
            Bounds = new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
            ContentPadding = 6,
            DrawBackground = true,
            Overflow = OverflowMode.Clip,
        };
        _rootLayout.Columns.Add(GridLength.Star());
        _rootLayout.Columns.Add(GridLength.Fixed(360));
        _rootLayout.Rows.Add(GridLength.Fixed(36));
        _rootLayout.Rows.Add(GridLength.Star());
        _rootLayout.Rows.Add(GridLength.Fixed(30));
        _ui.Add(_rootLayout);

        var toolbar = new StackPanel
        {
            Orientation = StackOrientation.Horizontal,
            Spacing = 6,
            DrawBackground = true,
            ContentPadding = 3,
        };
        _rootLayout.Add(toolbar, 0, 0, 2, 1);
        var menu = new MenuBar { Bounds = new Rectangle(0, 0, 190, 30) };
        MenuButton fileMenu = menu.AddMenu("File");
        fileMenu.AddItem("Save", TrySave);
        fileMenu.AddItem("Reload", () => { _session.Reload(); _shipRenderer.InvalidateSemanticHull(_session.PreviewHullDefinition.HullTypeId); RefreshUiText(); });
        MenuButton viewMenu = menu.AddMenu("View");
        viewMenu.AddItem("Fit 2D", FitOrthographicView);
        viewMenu.AddItem("Reset 3D", () => { _previewTarget = Vector3.Zero; _distance = 42f; _yaw = -0.6f; _pitch = -0.25f; });
        toolbar.Add(menu);
        AddButton(toolbar, "Save", TrySave);
        AddButton(toolbar, "Reload", () => { _session.Reload(); _shipRenderer.InvalidateSemanticHull(_session.PreviewHullDefinition.HullTypeId); RefreshUiText(); });
        AddButton(toolbar, "Undo", () => { _session.Undo(); _shipRenderer.InvalidateSemanticHull(_session.PreviewHullDefinition.HullTypeId); RefreshUiText(); });
        AddButton(toolbar, "Redo", () => { _session.Redo(); _shipRenderer.InvalidateSemanticHull(_session.PreviewHullDefinition.HullTypeId); RefreshUiText(); });
        AddButton(toolbar, "Roles", () => _debugMode = _debugMode == SemanticHullDebugMode.Normal ? SemanticHullDebugMode.SurfaceRoles : SemanticHullDebugMode.Normal);
        _projectionChoices = new ChoiceGroup<ProjectionKind>(_projection.Kind);
        _projectionChoices.SelectionChanged += value => _projection.Kind = value;
        AddChoice(toolbar, _projectionChoices, ProjectionKind.Top, "Top");
        AddChoice(toolbar, _projectionChoices, ProjectionKind.Side, "Side");
        AddChoice(toolbar, _projectionChoices, ProjectionKind.Front, "Front");

        _constraintChoices = new ChoiceGroup<EditingConstraintMode>(_constraintMode);
        _constraintChoices.SelectionChanged += value => _constraintMode = value;
        AddChoice(toolbar, _constraintChoices, EditingConstraintMode.ViewPlane, "Plane");
        AddChoice(toolbar, _constraintChoices, EditingConstraintMode.AxisX, "X");
        AddChoice(toolbar, _constraintChoices, EditingConstraintMode.AxisY, "Y");
        AddChoice(toolbar, _constraintChoices, EditingConstraintMode.AxisZ, "Z");
        AddChoice(toolbar, _constraintChoices, EditingConstraintMode.ActiveFacePlane, "Face");

        _orthoSurface = new DesignerSurfaceControl(DesignerSurfaceKind.Orthographic, "2D editor")
        {
            Margin = new Thickness(0, 6, 6, 6),
            DrawCustomContent = DrawOrthographic,
        };
        _rootLayout.Add(_orthoSurface, 0, 1);

        var rightGrid = new GridPanel
        {
            Margin = new Thickness(0, 6, 0, 6),
            Overflow = OverflowMode.Clip,
        };
        rightGrid.Rows.Add(GridLength.Star());
        rightGrid.Rows.Add(GridLength.Fixed(380));
        rightGrid.Columns.Add(GridLength.Star());
        _rootLayout.Add(rightGrid, 1, 1);

        _perspectiveSurface = new DesignerSurfaceControl(DesignerSurfaceKind.Perspective, "3D preview")
        {
            DrawContent = DrawPerspectiveTexture,
        };
        rightGrid.Add(_perspectiveSurface, 0, 0);

        var properties = new CollapsiblePanel
        {
            Header = "Properties",
            Margin = new Thickness(0, 6, 0, 0),
            IsExpanded = true,
        };
        rightGrid.Add(properties, 0, 1);
        _propertiesPanel = new Panel { Bounds = new Rectangle(0, 0, 330, 340), ContentPadding = 8, Overflow = OverflowMode.Clip };
        properties.Add(_propertiesPanel);

        _titleLabel = new Label("", new Rectangle(0, 0, 304, 24));
        _selectionLabel = new Label("", new Rectangle(0, 28, 304, 58)) { FontScale = 0.58f };
        _propertiesPanel.Add(_titleLabel);
        _propertiesPanel.Add(_selectionLabel);
        _faceRows =
        [
            FaceRow(0, 88),
            FaceRow(0, 126),
            FaceRow(0, 164),
            FaceRow(0, 202),
        ];
        foreach (IncidentFaceRow faceRow in _faceRows)
            _propertiesPanel.Add(faceRow);
        _propertiesPanel.Add(new Label("X", new Rectangle(0, 250, 20, 26)));
        _propertiesPanel.Add(new Label("Y", new Rectangle(0, 278, 20, 26)));
        _propertiesPanel.Add(new Label("Z", new Rectangle(0, 306, 20, 26)));
        _xBox = CoordinateBox(24, 247);
        _yBox = CoordinateBox(24, 275);
        _zBox = CoordinateBox(24, 303);
        _propertiesPanel.Add(_xBox);
        _propertiesPanel.Add(_yBox);
        _propertiesPanel.Add(_zBox);
        _propertiesPanel.Add(new Label("Diagnostics", new Rectangle(146, 250, 172, 24)) { TextColor = new Color(170, 196, 204) });
        _validationBlock = new TextBlock { Bounds = new Rectangle(146, 275, 174, 57), FontScale = 0.58f, Padding = 2 };
        _propertiesPanel.Add(_validationBlock);

        _statusLabel = new Label("", new Rectangle(0, 0, 1000, 24)) { FontScale = 0.78f };
        _rootLayout.Add(_statusLabel, 0, 2, 2, 1);
        RefreshUiText();
    }

    private void AddButton(StackPanel parent, string text, Action action)
    {
        var button = new Button(text, new Rectangle(0, 0, Math.Max(58, text.Length * 10), 28));
        button.Clicked += _ => action();
        parent.Add(button);
    }

    private static void AddChoice<T>(StackPanel parent, ChoiceGroup<T> group, T value, string text)
        where T : notnull
    {
        ToggleButton button = group.AddChoice(value, text, new Rectangle(0, 0, Math.Max(58, text.Length * 10), 28));
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

    private IncidentFaceRow FaceRow(int x, int y)
    {
        var row = new IncidentFaceRow { Bounds = new Rectangle(x, y, 304, 36), FontScale = 0.58f };
        row.Clicked += clicked =>
        {
            string faceId = _faceRowIds.GetValueOrDefault(clicked, "");
            if (_session.SelectActiveFace(faceId))
                _status = $"Active face: {faceId}";
            else
                _status = $"Cannot select face: {faceId}";
            RefreshUiText();
        };
        row.MouseEnter += hovered =>
        {
            if (_faceRowIds.TryGetValue((IncidentFaceRow)hovered, out string? faceId))
                _status = faceId;
        };
        row.MouseLeave += _ => _status = "";
        return row;
    }

    private void TryApplyNumericEdit()
    {
        if (_updatingTextBoxes || _session.ActiveVertexId is null)
            return;
        if (!double.TryParse(_xBox.Text, out double x)
            || !double.TryParse(_yBox.Text, out double y)
            || !double.TryParse(_zBox.Text, out double z))
        {
            _status = "Invalid coordinate text.";
            return;
        }

        DVec3 before = _session.GetVertexPosition(_session.ActiveVertexId);
        DVec3 after = new(x, y, z);
        _session.Execute(new MoveVertexCommand(_session.ActiveVertexId, before, after, $"Edit {_session.ActiveVertexId}"));
        _shipRenderer.InvalidateSemanticHull(_session.PreviewHullDefinition.HullTypeId);
        RefreshUiText();
    }

    private void TrySave()
    {
        _status = _session.Save()
            ? "Saved."
            : "Save blocked: resolve authoring errors first.";
        RefreshUiText();
    }

    private void RefreshUiText()
    {
        if (_xBox is null)
            return;
        _updatingTextBoxes = true;
        if (_session.ActiveVertexId is not null)
        {
            DVec3 p = _session.GetVertexPosition(_session.ActiveVertexId);
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
        _selectionLabel.Text =
            $"Selected {_session.SelectedVertexIds.Count} vertex/vertices\n"
            + $"Active vertex\n{_session.ActiveVertexId ?? "None"}\n"
            + $"Active face\n{_session.ActiveFaceId ?? "None"}\n"
            + "Incident faces";
        RefreshFaceButtons();
        IEnumerable<AuthoringDiagnostic> diagnostics = _session.Diagnostics.Take(12);
        string validation = _session.Diagnostics.Count == 0
            ? "No validation errors."
            : string.Join("\n", diagnostics.Select(d => $"{d.Severity} [{d.Code}]: {d.Summary}"));
        _validationBlock.Text = validation;
        _statusLabel.Text = string.IsNullOrWhiteSpace(_status)
            ? (_session.IsPreviewStale ? "Preview is showing the last valid hull." : "Ready.")
            : _status;
    }

    private void SetProjection(ProjectionKind kind)
    {
        _projection.Kind = kind;
        if (_projectionChoices is not null)
            _projectionChoices.SelectedValue = kind;
    }

    private Rectangle PerspectiveViewport => _perspectiveSurface.ContentBounds;
    private Rectangle OrthoViewport => _orthoSurface.ContentBounds;

    private static void ValidateSurfaceRect(DesignerSurfaceControl surface, Rectangle content)
    {
        Rectangle clip = surface.EffectiveClipBounds;
        System.Diagnostics.Debug.Assert(surface.AbsoluteBounds.Width > 0 && surface.AbsoluteBounds.Height > 0, $"{surface.Kind} surface has empty arranged bounds.");
        System.Diagnostics.Debug.Assert(content.Width > 0 && content.Height > 0, $"{surface.Kind} surface has empty content bounds.");
        System.Diagnostics.Debug.Assert(clip.Width > 0 && clip.Height > 0, $"{surface.Kind} surface has empty effective clip.");
        System.Diagnostics.Debug.Assert(Rectangle.Intersect(clip, content).Width > 0 && Rectangle.Intersect(clip, content).Height > 0, $"{surface.Kind} surface clip does not intersect content.");
    }

    private string IncidentFaceText()
    {
        if (_session.ActiveVertexId is null)
            return "";
        string[] faces = _session.GetIncidentFaces(_session.ActiveVertexId)
            .Select(face => face.Id)
            .Take(3)
            .ToArray();
        return faces.Length == 0 ? "none" : string.Join(", ", faces);
    }

    private void RefreshFaceButtons()
    {
        if (_faceRows.Length == 0)
            return;
        IReadOnlyList<SemanticHullFaceDto> faces = _session.ActiveVertexId is null
            ? []
            : _session.GetIncidentFaces(_session.ActiveVertexId);
        for (int i = 0; i < _faceRows.Length; i++)
        {
            IncidentFaceRow row = _faceRows[i];
            if (i >= faces.Count)
            {
                row.Visible = false;
                row.Enabled = false;
                _faceRowIds.Remove(row);
                row.FaceId = "";
                row.Metadata = "";
                row.IsActiveFace = false;
                continue;
            }
            SemanticHullFaceDto face = faces[i];
            bool active = string.Equals(face.Id, _session.ActiveFaceId, StringComparison.Ordinal);
            row.Visible = true;
            row.Enabled = true;
            _faceRowIds[row] = face.Id;
            row.FaceId = face.Id;
            row.Metadata = IncidentFaceRow.BuildMetadata(face.Role.ToString(), face.MaterialGroup, face.VertexIds.Count);
            row.IsActiveFace = active;
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

    private void AddActiveFaceEdges(List<VertexPositionColor> lines, Rectangle vp)
    {
        IReadOnlyList<DVec3> vertices = _session.GetActiveFaceOverlayData().FaceVertices;
        if (vertices.Count < 2)
            return;
        for (int pass = 0; pass < 2; pass++)
        {
            Vector2 nudge = pass == 0 ? Vector2.Zero : new Vector2(1, 1);
            for (int i = 0; i < vertices.Count; i++)
                AddScreenLine(lines, _projection.Project(vertices[i], vp) + nudge, _projection.Project(vertices[(i + 1) % vertices.Count], vp) + nudge, new Color(80, 230, 255));
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
            bool selected = _session.SelectedVertexIds.Contains(vertex.Id, StringComparer.Ordinal);
            bool active = string.Equals(vertex.Id, _session.ActiveVertexId, StringComparison.Ordinal);
            int size = active ? 12 : selected ? 8 : 5;
            Color colour = active ? new Color(80, 230, 255) : selected ? Color.Yellow : new Color(215, 230, 230);
            _spriteBatch.Draw(pixel, new Rectangle((int)p.X - size / 2, (int)p.Y - size / 2, size, size), colour);
        }
    }

    private static DVec3 ActiveFaceNormalForOverlay(IReadOnlyList<DVec3> vertices)
    {
        double x = 0;
        double y = 0;
        double z = 0;
        for (int i = 0; i < vertices.Count; i++)
        {
            DVec3 current = vertices[i];
            DVec3 next = vertices[(i + 1) % vertices.Count];
            x += (current.Y - next.Y) * (current.Z + next.Z);
            y += (current.Z - next.Z) * (current.X + next.X);
            z += (current.X - next.X) * (current.Y + next.Y);
        }
        DVec3 normal = new(x, y, z);
        return normal.LengthSquared <= 1e-12 ? DVec3.Zero : normal.Normalized();
    }

    private static void AddWorldLine(List<VertexPositionColor> lines, DVec3 a, DVec3 b, Color color)
    {
        lines.Add(new VertexPositionColor(a.ToVector3(), color));
        lines.Add(new VertexPositionColor(b.ToVector3(), color));
    }

    private static void AddWorldCross(List<VertexPositionColor> lines, DVec3 center, double radius, Color color)
    {
        AddWorldLine(lines, center - DVec3.UnitX * radius, center + DVec3.UnitX * radius, color);
        AddWorldLine(lines, center - DVec3.UnitY * radius, center + DVec3.UnitY * radius, color);
        AddWorldLine(lines, center - DVec3.UnitZ * radius, center + DVec3.UnitZ * radius, color);
    }

    private void DrawSelectionRectangle()
    {
        Rectangle selection = NormalizedRectangle(_selectionStartMouse, Mouse.GetState().Position);
        Texture2D pixel = TexturePixel;
        _spriteBatch.Draw(pixel, new Rectangle(selection.Left, selection.Top, selection.Width, 1), Color.Yellow);
        _spriteBatch.Draw(pixel, new Rectangle(selection.Left, selection.Bottom, selection.Width, 1), Color.Yellow);
        _spriteBatch.Draw(pixel, new Rectangle(selection.Left, selection.Top, 1, selection.Height), Color.Yellow);
        _spriteBatch.Draw(pixel, new Rectangle(selection.Right, selection.Top, 1, selection.Height), Color.Yellow);
    }

    private static Rectangle NormalizedRectangle(Point a, Point b)
    {
        int left = Math.Min(a.X, b.X);
        int top = Math.Min(a.Y, b.Y);
        return new Rectangle(left, top, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    private void FitOrthographicView()
    {
        Rectangle viewport = OrthoViewport;
        IReadOnlyList<SemanticHullVertex> vertices = _session.HullDefinition.VisualGeometry!.Vertices;
        if (vertices.Count == 0 || viewport.Width <= 0 || viewport.Height <= 0)
            return;
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
        foreach (SemanticHullVertex vertex in vertices)
        {
            Vector2 axes = _projection.ToProjectionAxes(vertex.Position);
            min = Vector2.Min(min, axes);
            max = Vector2.Max(max, axes);
        }
        Vector2 span = Vector2.Max(max - min, new Vector2(1f, 1f));
        float scaleX = (viewport.Width - 48) / Math.Max(0.1f, span.X);
        float scaleY = (viewport.Height - 48) / Math.Max(0.1f, span.Y);
        _projection.PixelsPerMeter = MathHelper.Clamp(Math.Min(scaleX, scaleY), 4f, 120f);
        Vector2 center = (min + max) * 0.5f;
        _projection.PanPixels = new Vector2(-center.X * _projection.PixelsPerMeter, center.Y * _projection.PixelsPerMeter);
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
        CargoArrangementDefinition? cargo = _session.PreviewHullDefinition.CargoArrangement;
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
