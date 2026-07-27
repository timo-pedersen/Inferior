using Inferior.Core.Math;
using Inferior.Gameplay.Hull;
using Inferior.ObjectDesigner.Editing;
using Inferior.Rendering;
using Inferior.UI;
using Inferior.UI.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Inferior.ObjectDesigner.Test;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ObjectDesignerGpuRenderCollection
{
    public const string Name = "Object Designer GPU render tests";
}

[Collection(ObjectDesignerGpuRenderCollection.Name)]
public sealed class ObjectDesignerRenderedSmokeTests
{
    private static readonly Color Root = Color.Black;
    private static readonly Color GridBackground = new(9, 12, 13);
    private static readonly Color Toolbar = Color.Red;
    private static readonly Color TwoD = Color.Green;
    private static readonly Color ThreeD = Color.Blue;
    private static readonly Color Properties = Color.Yellow;
    private static readonly Color Diagnostics = new(255, 128, 0);
    private static readonly Color Status = Color.Magenta;
    private static readonly Color Popup = Color.White;

    [Fact]
    public void ObjectDesigner_composition_renders_all_primary_regions_and_popup()
    {
        RenderedFrame frame = RenderHarness.Render(800, 600, gd =>
        {
            using var ui = new UIManager(gd, TestTheme());
            RenderTarget2D? previewTarget = null;
            GridPanel root = BuildDesignerLikeTree(() => previewTarget);
            ui.Add(root);
            ui.AddOverlay(new FlatControl(new Rectangle(60, 18, 150, 110), Popup));
            root.Update(0);
            DesignerSurfaceControl perspective = FindSurface(root, DesignerSurfaceKind.Perspective);
            using (previewTarget = RenderPreparedPerspectiveTarget(gd, perspective.ContentBounds))
            {
                gd.Clear(Root);
                ui.Draw();
            }
        });

        frame.AssertPixel(new Point(20, 20), Toolbar, "toolbar");
        frame.AssertPixel(new Point(250, 300), TwoD, "2D editor");
        frame.AssertPixel(new Point(650, 160), ThreeD, "3D preview");
        frame.AssertPixel(new Point(650, 455), Properties, "properties");
        frame.AssertPixel(new Point(650, 535), Diagnostics, "diagnostics");
        frame.AssertPixel(new Point(400, 585), Status, "status");
        frame.AssertPixel(new Point(90, 50), Popup, "popup over editor chrome");
    }

    [Fact]
    public void ObjectDesigner_preview_ship_renderer_uses_preview_eye_for_specular()
    {
        Vector3 previewEye = new(0f, 4f, 26f);

        ShipPreviewFrame tightMaterial = RenderShipPreview(previewEye, DynamicLitMaterialSettings.Tight);

        Assert.Equal(previewEye, tightMaterial.EyePositionWorld);
        Assert.Equal(DynamicLitMaterialSettings.Tight.SpecularStrength, tightMaterial.SpecularStrength);
        Assert.Equal(DynamicLitMaterialSettings.Tight.SpecularShininess, tightMaterial.SpecularShininess);
        int nonBackgroundPixels = CountNonBackgroundPixels(tightMaterial.Frame, new Color(8, 10, 11));
        Assert.True(nonBackgroundPixels > 100, $"The real preview ship render produced too few non-background pixels: {nonBackgroundPixels}.");
    }

    [Fact]
    public void Metric_grid_render_smoke_shows_zoom_levels_and_stays_under_editor_marks()
    {
        Rectangle viewport = new(20, 20, 160, 160);
        RenderedFrame hierarchy = RenderGridFrame(800f, viewport, drawEditorMarks: true);

        hierarchy.AssertPixel(new Point(100, 100), Color.Yellow, "editor mark over metre grid");
        double background = Luminance(GridBackground);
        double metreLuma = MaxLuminanceNear(hierarchy, new Point(100, 103));
        double decimetreLuma = MaxLuminanceNear(hierarchy, new Point(180, 103));
        double centimetreLuma = MaxLuminanceNear(hierarchy, new Point(108, 103));
        Assert.True(metreLuma - decimetreLuma > 30, $"metre {metreLuma} should be substantially brighter than decimetre {decimetreLuma}");
        Assert.True(decimetreLuma - centimetreLuma > 8, $"decimetre {decimetreLuma} should be substantially brighter than centimetre {centimetreLuma}");
        Assert.True(centimetreLuma > background, $"centimetre {centimetreLuma} should remain above background {background}");
        hierarchy.AssertPixel(new Point(10, 100), GridBackground, "outside 2D pane");

        AssertFramebufferFade([50f, 59f, 69.62f, 80f], MetricGrid.DecimetreSpacing, "decimetre");
        AssertFramebufferFade([500f, 590f, 696.2f, 800f], MetricGrid.CentimetreSpacing, "centimetre");
    }

    private static GridPanel BuildDesignerLikeTree(Func<Texture2D?> previewTexture)
    {
        var root = new GridPanel
        {
            Bounds = new Rectangle(0, 0, 800, 600),
            ContentPadding = 0,
            Overflow = OverflowMode.Clip,
        };
        root.Columns.Add(GridLength.Star());
        root.Columns.Add(GridLength.Fixed(300));
        root.Rows.Add(GridLength.Fixed(44));
        root.Rows.Add(GridLength.Star());
        root.Rows.Add(GridLength.Fixed(32));

        root.Add(new FlatControl(Rectangle.Empty, Toolbar), 0, 0, 2, 1);

        var twoD = new DesignerSurfaceControl(DesignerSurfaceKind.Orthographic, "2D editor")
        {
            DrawCustomContent = context => PrimitiveFill(context.GraphicsDevice, context.ClipBounds, context.ClipBounds, TwoD),
        };
        root.Add(twoD, 0, 1);

        var right = new GridPanel { Overflow = OverflowMode.Clip };
        right.Columns.Add(GridLength.Star());
        right.Rows.Add(GridLength.Star());
        right.Rows.Add(GridLength.Fixed(180));
        root.Add(right, 1, 1);

        var threeD = new DesignerSurfaceControl(DesignerSurfaceKind.Perspective, "3D preview")
        {
            DrawContent = (sb, renderer, bounds) =>
            {
                Texture2D? texture = previewTexture();
                if (texture is not null)
                    sb.Draw(texture, bounds, Color.White);
            },
        };
        right.Add(threeD, 0, 0);

        var lower = new GridPanel();
        lower.Columns.Add(GridLength.Star());
        lower.Rows.Add(GridLength.Star());
        lower.Rows.Add(GridLength.Fixed(70));
        lower.Add(new FlatControl(Rectangle.Empty, Properties), 0, 0);
        lower.Add(new FlatControl(Rectangle.Empty, Diagnostics), 0, 1);
        right.Add(lower, 0, 1);

        root.Add(new FlatControl(Rectangle.Empty, Status), 0, 2, 2, 1);
        return root;
    }

    private static RenderTarget2D RenderPreparedPerspectiveTarget(GraphicsDevice gd, Rectangle bounds)
    {
        var target = new RenderTarget2D(gd, bounds.Width, bounds.Height, false, SurfaceFormat.Color, DepthFormat.Depth24);
        RenderTargetBinding[] oldTargets = gd.GetRenderTargets();
        Viewport oldViewport = gd.Viewport;
        gd.SetRenderTarget(target);
        try
        {
            gd.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, ThreeD, 1f, 0);
        }
        finally
        {
            gd.SetRenderTargets(oldTargets);
            gd.Viewport = oldViewport;
        }
        return target;
    }

    private static DesignerSurfaceControl FindSurface(Control root, DesignerSurfaceKind kind)
    {
        if (root is DesignerSurfaceControl surface && surface.Kind == kind)
            return surface;
        foreach (Control child in root.Children)
        {
            DesignerSurfaceControl? found = FindSurfaceOrNull(child, kind);
            if (found is not null)
                return found;
        }
        throw new InvalidOperationException($"Could not find {kind} surface.");
    }

    private static DesignerSurfaceControl? FindSurfaceOrNull(Control root, DesignerSurfaceKind kind)
    {
        if (root is DesignerSurfaceControl surface && surface.Kind == kind)
            return surface;
        foreach (Control child in root.Children)
        {
            DesignerSurfaceControl? found = FindSurfaceOrNull(child, kind);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static Theme TestTheme() => new(null!)
    {
        PanelBackground = Root,
        PanelBorder = Root,
        WindowBackground = Root,
        WindowBorder = Root,
        WindowTitleBar = Root,
        WindowTitleText = Root,
        WindowBorderFocus = Root,
        ButtonBackground = Root,
        ButtonBackgroundHover = Root,
        ButtonBackgroundPressed = Root,
        ButtonBackgroundDisabled = Root,
        ButtonBorder = Root,
        ButtonBorderHover = Root,
        ButtonBorderFocus = Root,
        TextNormal = Root,
        TextHover = Root,
        TextDisabled = Root,
        TextTitle = Root,
        TextBoxBackground = Root,
        TextBoxBorder = Root,
        TextBoxBorderFocus = Root,
        TextBoxSelection = Root,
        TextBoxCursor = Root,
        TextBoxPlaceholder = Root,
        TextBoxScrollbar = Root,
        TextBoxScrollbarThumb = Root,
        ToggleOff = Root,
        ToggleOn = Root,
        TogglePending = Root,
        ToggleIndicatorOff = Root,
        ToggleIndicatorOn = Root,
        TargetShip = Root,
        TargetNav = Root,
        TargetHyp = Root,
        Accent = Root,
    };

    private sealed class FlatControl : Control
    {
        private readonly Color _color;

        public FlatControl(Rectangle bounds, Color color)
        {
            Bounds = bounds;
            _color = color;
        }

        public override Point DesiredSize => new(Math.Max(1, Bounds.Width), Math.Max(1, Bounds.Height));

        public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
        {
            if (!Visible)
                return;
            renderer.FillRect(sb, AbsoluteBounds, _color);
            DrawChildren(sb, renderer, theme);
        }
    }

    private static void PrimitiveFill(GraphicsDevice gd, Rectangle bounds, Rectangle clip, Color color)
    {
        using var effect = new BasicEffect(gd)
        {
            VertexColorEnabled = true,
            Projection = Matrix.CreateOrthographicOffCenter(0, gd.Viewport.Width, gd.Viewport.Height, 0, 0, 1),
        };
        gd.RasterizerState = new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None };
        gd.ScissorRectangle = clip;
        VertexPositionColor[] vertices =
        [
            new(new Vector3(bounds.Left, bounds.Top, 0), color),
            new(new Vector3(bounds.Right, bounds.Top, 0), color),
            new(new Vector3(bounds.Left, bounds.Bottom, 0), color),
            new(new Vector3(bounds.Right, bounds.Top, 0), color),
            new(new Vector3(bounds.Right, bounds.Bottom, 0), color),
            new(new Vector3(bounds.Left, bounds.Bottom, 0), color),
        ];
        foreach (EffectPass pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, 2);
        }
    }

    private static Texture2D CreatePixel(GraphicsDevice gd)
    {
        var texture = new Texture2D(gd, 1, 1);
        texture.SetData([Color.White]);
        return texture;
    }

    private static RenderedFrame RenderGridFrame(float pixelsPerMeter, Rectangle viewport, bool drawEditorMarks)
        => RenderHarness.Render(200, 200, gd =>
        {
            gd.Clear(GridBackground);
            var projection = new OrthographicProjection { Kind = ProjectionKind.Top, PixelsPerMeter = pixelsPerMeter };
            using var effect = new BasicEffect(gd)
            {
                VertexColorEnabled = true,
                Projection = Matrix.CreateOrthographicOffCenter(0, gd.Viewport.Width, gd.Viewport.Height, 0, 0, 1),
            };
            VertexPositionColor[] vertices = ProductionGridVertices(projection, viewport);
            gd.BlendState = BlendState.AlphaBlend;
            gd.RasterizerState = RasterizerState.CullNone;
            foreach (EffectPass pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                if (vertices.Length > 0)
                    gd.DrawUserPrimitives(PrimitiveType.LineList, vertices, 0, vertices.Length / 2);
            }

            if (drawEditorMarks)
            {
                using Texture2D pixel = CreatePixel(gd);
                using var sb = new SpriteBatch(gd);
                sb.Begin(blendState: BlendState.AlphaBlend);
                sb.Draw(pixel, new Rectangle(96, 96, 8, 8), Color.Yellow);
                sb.End();
            }
        });

    private static VertexPositionColor[] ProductionGridVertices(OrthographicProjection projection, Rectangle viewport)
    {
        var game = (ObjectDesignerGame)RuntimeHelpers.GetUninitializedObject(typeof(ObjectDesignerGame));
        typeof(ObjectDesignerGame).GetField("_projection", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(game, projection);
        var lines = new List<VertexPositionColor>();
        typeof(ObjectDesignerGame).GetMethod("AddGrid", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game, [lines, viewport]);
        return [.. lines];
    }

    private static void AssertFramebufferFade(float[] zooms, double coordinate, string label)
    {
        Rectangle viewport = new(20, 20, 160, 160);
        double[] values = zooms.Select(zoom =>
        {
            RenderedFrame frame = RenderGridFrame(zoom, viewport, drawEditorMarks: false);
            int x = (int)Math.Round(100 + coordinate * zoom);
            return MaxLuminanceNear(frame, new Point(x, 103));
        }).ToArray();

        double background = Luminance(GridBackground);
        Assert.True(values[0] <= background + 0.5, $"{label} first sample should be background, got {values[0]}");
        Assert.True(values[1] > values[0] + 1, $"{label} first visible step did not rise: {string.Join(", ", values)}");
        Assert.True(values[2] > values[1] + 1, $"{label} second visible step did not rise: {string.Join(", ", values)}");
        Assert.True(values[3] > values[2] + 1, $"{label} full step did not rise: {string.Join(", ", values)}");
    }

    private static double Luminance(Color color)
        => 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;

    private static double MaxLuminanceNear(RenderedFrame frame, Point center)
    {
        double max = 0;
        for (int y = center.Y - 1; y <= center.Y + 1; y++)
        for (int x = center.X - 1; x <= center.X + 1; x++)
            max = Math.Max(max, Luminance(frame.PixelAt(new Point(x, y))));
        return max;
    }

    private static ShipPreviewFrame RenderShipPreview(Vector3 eyePositionWorld, DynamicLitMaterialSettings material)
    {
        RenderedFrame? frame = null;
        Vector3 parameterValue = default;
        float specularStrength = float.NaN;
        float specularShininess = float.NaN;
        frame = RenderHarness.Render(256, 256, gd =>
        {
            using var content = new ContentManager(new GraphicsDeviceServiceProvider(gd), FindContentRoot());
            using Effect litSurface = content.Load<Effect>("Effects/LitSurface");
            using Effect engineGlow = content.Load<Effect>("Effects/EngineExhaustGlow");
            using var meshRenderer = new MeshRenderer(gd, litSurface);
            using var shipRenderer = new ShipMeshRenderer(gd, meshRenderer, engineGlow);

            Vector3 cameraPosition = new(0f, 4f, 26f);
            Matrix view = Matrix.CreateLookAt(cameraPosition, Vector3.Zero, Vector3.UnitY);
            Matrix projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(55f), 1f, 0.05f, 400f);
            var camera = new Camera3D(DVec3.Zero, 1f);
            camera.SetPose(DVec3.Zero, Quaternion.Identity);
            HullDefinition hull = HullDefinitionLibrary.Get(BerenHullDefinitionFactory.HullId);

            Vector3 oldSunDirection = SceneLighting.SunDirection;
            float oldAmbient = SceneLighting.Ambient;
            Vector3 oldSunColour = SceneLighting.SunColour;
            try
            {
                SceneLighting.SunDirection = Vector3.Normalize(new Vector3(0.15f, 0.75f, 0.65f));
                SceneLighting.Ambient = 0.09f;
                SceneLighting.SunColour = new Vector3(1.0f, 0.97f, 0.88f);

                gd.Clear(new Color(8, 10, 11));
                gd.BlendState = BlendState.Opaque;
                gd.DepthStencilState = DepthStencilState.Default;

                shipRenderer.Draw(
                    camera,
                    view,
                    projection,
                    hull.HullTypeId,
                    DVec3.Zero,
                    Quaternion.Identity,
                    DetailLevel.Full,
                    material.SpecularStrength,
                    material.SpecularShininess,
                    hullOverride: hull,
                    renderScaleOverride: 1.0f,
                    eyePositionWorld: eyePositionWorld);

                parameterValue = litSurface.Parameters["EyePositionWorld"].GetValueVector3();
                specularStrength = litSurface.Parameters["SpecularStrength"].GetValueSingle();
                specularShininess = litSurface.Parameters["SpecularShininess"].GetValueSingle();
            }
            finally
            {
                SceneLighting.SunDirection = oldSunDirection;
                SceneLighting.Ambient = oldAmbient;
                SceneLighting.SunColour = oldSunColour;
            }
        });

        return new ShipPreviewFrame(frame, parameterValue, specularStrength, specularShininess);
    }

    private static int CountNonBackgroundPixels(RenderedFrame frame, Color background)
    {
        int count = 0;
        foreach (Color pixel in frame.Pixels)
        {
            int delta = Math.Abs(pixel.R - background.R) + Math.Abs(pixel.G - background.G) + Math.Abs(pixel.B - background.B);
            if (delta > 4)
                count++;
        }
        return count;
    }

    private static string FindContentRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "Inferior.ObjectDesigner", "bin", "Debug", "net10.0", "Content");
            if (File.Exists(Path.Combine(candidate, "Effects", "LitSurface.xnb"))
                && File.Exists(Path.Combine(candidate, "Effects", "EngineExhaustGlow.xnb")))
            {
                return candidate;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find compiled Object Designer content.");
    }

    private sealed record RenderedFrame(int Width, int Height, Color[] Pixels)
    {
        public void AssertPixel(Point point, Color expected, string label)
        {
            Color actual = Pixels[point.Y * Width + point.X];
            Assert.True(actual == expected, $"{label} at {point}: expected {expected}, actual {actual}");
        }

        public Color PixelAt(Point point) => Pixels[point.Y * Width + point.X];
    }

    private sealed record ShipPreviewFrame(RenderedFrame Frame, Vector3 EyePositionWorld, float SpecularStrength, float SpecularShininess);

    private sealed class GraphicsDeviceServiceProvider(GraphicsDevice graphicsDevice) : IServiceProvider, IGraphicsDeviceService
    {
        public GraphicsDevice GraphicsDevice { get; } = graphicsDevice;

        event EventHandler<EventArgs>? IGraphicsDeviceService.DeviceCreated { add { } remove { } }
        event EventHandler<EventArgs>? IGraphicsDeviceService.DeviceDisposing { add { } remove { } }
        event EventHandler<EventArgs>? IGraphicsDeviceService.DeviceReset { add { } remove { } }
        event EventHandler<EventArgs>? IGraphicsDeviceService.DeviceResetting { add { } remove { } }

        public object? GetService(Type serviceType)
            => serviceType == typeof(IGraphicsDeviceService) ? this : null;
    }

    private static class RenderHarness
    {
        public static RenderedFrame Render(int width, int height, Action<GraphicsDevice> draw)
        {
            using var game = new OneFrameGame(width, height, draw);
            game.RunOneFrame();
            if (game.Exception is not null)
                throw game.Exception;
            return game.Frame ?? throw new InvalidOperationException("The render test did not produce a frame.");
        }

        private sealed class OneFrameGame : Game
        {
            private readonly int _width;
            private readonly int _height;
            private readonly Action<GraphicsDevice> _draw;

            public RenderedFrame? Frame { get; private set; }
            public Exception? Exception { get; private set; }

            public OneFrameGame(int width, int height, Action<GraphicsDevice> draw)
            {
                _width = width;
                _height = height;
                _draw = draw;
                _ = new GraphicsDeviceManager(this)
                {
                    PreferredBackBufferWidth = width,
                    PreferredBackBufferHeight = height,
                    SynchronizeWithVerticalRetrace = false,
                };
                IsFixedTimeStep = false;
            }

            protected override void Initialize()
            {
                Window.AllowUserResizing = false;
                Window.IsBorderless = true;
                Window.Position = new Point(-32000, -32000);
                base.Initialize();
            }

            protected override void Draw(GameTime gameTime)
            {
                try
                {
                    using var target = new RenderTarget2D(GraphicsDevice, _width, _height, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.PreserveContents);
                    GraphicsDevice.SetRenderTarget(target);
                    GraphicsDevice.Clear(Root);
                    _draw(GraphicsDevice);
                    GraphicsDevice.SetRenderTarget(null);
                    Color[] pixels = new Color[_width * _height];
                    target.GetData(pixels);
                    Frame = new RenderedFrame(_width, _height, pixels);
                }
                catch (Exception ex)
                {
                    Exception = ex;
                }
                finally
                {
                    GraphicsDevice.SetRenderTarget(null);
                    Exit();
                }
            }
        }
    }
}
