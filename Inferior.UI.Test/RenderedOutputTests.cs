using Inferior.UI.Controls;
using System.IO.Compression;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace Inferior.UI.Test;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GpuRenderCollection
{
    public const string Name = "GPU render tests";
}

[Collection(GpuRenderCollection.Name)]
public sealed class RenderedOutputTests
{
    private static readonly Color Root = Color.Black;
    private static readonly Color Toolbar = Color.Red;
    private static readonly Color TwoD = Color.Green;
    private static readonly Color ThreeD = Color.Blue;
    private static readonly Color Properties = Color.Yellow;
    private static readonly Color Status = Color.Magenta;
    private static readonly Color Popup = Color.White;

    [Fact]
    public void ObjectDesigner_like_composition_renders_all_regions_and_overlay()
    {
        RenderedFrame frame = UiRenderHarness.Render(800, 600, gd =>
        {
            using var ui = new UIManager(gd, TestTheme());

            var root = new Panel
            {
                Bounds = new Rectangle(0, 0, 800, 600),
                DrawBackground = false,
                DrawBorder = false,
                Overflow = OverflowMode.Clip,
            };
            root.Add(new FlatControl(new Rectangle(0, 0, 800, 50), Toolbar));
            root.Add(new CustomPrimitiveFillControl(new Rectangle(0, 50, 500, 510), TwoD));
            root.Add(new CustomRenderTargetControl(new Rectangle(500, 50, 300, 360), ThreeD));
            root.Add(new FlatControl(new Rectangle(500, 410, 300, 150), Properties));
            root.Add(new FlatControl(new Rectangle(0, 560, 800, 40), Status));
            ui.Add(root);
            ui.AddOverlay(new FlatControl(new Rectangle(20, 20, 160, 120), Popup) { Overflow = OverflowMode.Visible });
            ui.Draw();
        });

        frame.AssertPixel("ObjectDesigner_like_composition", new Point(12, 12), Toolbar, "toolbar outside popup");
        frame.AssertPixel("ObjectDesigner_like_composition", new Point(250, 300), TwoD, "2D custom surface");
        frame.AssertPixel("ObjectDesigner_like_composition", new Point(650, 200), ThreeD, "3D custom surface");
        frame.AssertPixel("ObjectDesigner_like_composition", new Point(650, 480), Properties, "properties");
        frame.AssertPixel("ObjectDesigner_like_composition", new Point(400, 580), Status, "status");
        frame.AssertPixel("ObjectDesigner_like_composition", new Point(60, 40), Popup, "popup overlay");
        frame.AssertPixel("ObjectDesigner_like_composition", new Point(790, 10), Toolbar, "toolbar not globally erased");
    }

    [Fact]
    public void Custom_surface_does_not_overpaint_adjacent_chrome()
    {
        RenderedFrame frame = UiRenderHarness.Render(300, 180, gd =>
        {
            using var ui = new UIManager(gd, TestTheme());
            var root = new Panel { Bounds = new Rectangle(0, 0, 300, 180), DrawBackground = false, DrawBorder = false, Overflow = OverflowMode.Clip };
            root.Add(new FlatControl(new Rectangle(0, 0, 300, 40), Toolbar));
            root.Add(new CustomPrimitiveFillControl(new Rectangle(0, 40, 180, 100), TwoD));
            root.Add(new FlatControl(new Rectangle(180, 40, 120, 100), Properties));
            root.Add(new FlatControl(new Rectangle(0, 140, 300, 40), Status));
            ui.Add(root);
            ui.Draw();
        });

        frame.AssertPixel("Custom_surface_does_not_overpaint_adjacent_chrome", new Point(150, 20), Toolbar, "toolbar");
        frame.AssertPixel("Custom_surface_does_not_overpaint_adjacent_chrome", new Point(150, 42), TwoD, "below toolbar");
        frame.AssertPixel("Custom_surface_does_not_overpaint_adjacent_chrome", new Point(220, 80), Properties, "right column");
        frame.AssertPixel("Custom_surface_does_not_overpaint_adjacent_chrome", new Point(150, 160), Status, "status");
    }

    [Fact]
    public void Clipping_limits_child_and_empty_custom_clip_draws_nothing()
    {
        RenderedFrame frame = UiRenderHarness.Render(220, 160, gd =>
        {
            using var ui = new UIManager(gd, TestTheme());
            var root = new Panel { Bounds = new Rectangle(0, 0, 220, 160), DrawBackground = false, DrawBorder = false };
            var clipping = new FlatControl(new Rectangle(20, 20, 100, 80), Toolbar) { Overflow = OverflowMode.Clip };
            clipping.Add(new FlatControl(new Rectangle(50, 30, 100, 80), TwoD));
            root.Add(clipping);
            root.Add(new CustomPrimitiveFillControl(new Rectangle(180, 20, 30, 30), ThreeD) { Visible = false });
            ui.Add(root);
            ui.Draw();
        });

        frame.AssertPixel("Clipping_limits_child_and_empty_custom_clip_draws_nothing", new Point(90, 70), TwoD, "visible child intersection");
        frame.AssertPixel("Clipping_limits_child_and_empty_custom_clip_draws_nothing", new Point(30, 30), Toolbar, "parent outside child");
        frame.AssertPixel("Clipping_limits_child_and_empty_custom_clip_draws_nothing", new Point(130, 70), Root, "outside clipping parent");
        frame.AssertPixel("Clipping_limits_child_and_empty_custom_clip_draws_nothing", new Point(190, 30), Root, "hidden custom surface");
    }

    [Fact]
    public void Nested_clipping_and_overflow_visible_match_pixel_output()
    {
        RenderedFrame frame = UiRenderHarness.Render(260, 180, gd =>
        {
            using var ui = new UIManager(gd, TestTheme());
            var root = new Panel { Bounds = new Rectangle(0, 0, 260, 180), DrawBackground = false, DrawBorder = false };
            var outer = new FlatControl(new Rectangle(20, 20, 140, 120), Toolbar) { Overflow = OverflowMode.Clip };
            var inner = new FlatControl(new Rectangle(40, 30, 90, 70), TwoD) { Overflow = OverflowMode.Clip };
            inner.Add(new FlatControl(new Rectangle(50, 35, 80, 70), ThreeD));
            outer.Add(inner);
            root.Add(outer);
            var unclipped = new FlatControl(new Rectangle(175, 25, 35, 35), Properties) { Overflow = OverflowMode.Visible };
            unclipped.Add(new FlatControl(new Rectangle(25, 10, 45, 25), Status));
            root.Add(unclipped);
            ui.Add(root);
            ui.Draw();
        });

        frame.AssertPixel("Nested_clipping_and_overflow_visible_match_pixel_output", new Point(120, 95), ThreeD, "nested clipped intersection");
        frame.AssertPixel("Nested_clipping_and_overflow_visible_match_pixel_output", new Point(80, 60), TwoD, "inner panel outside child");
        frame.AssertPixel("Nested_clipping_and_overflow_visible_match_pixel_output", new Point(155, 95), Toolbar, "child clipped by inner panel");
        frame.AssertPixel("Nested_clipping_and_overflow_visible_match_pixel_output", new Point(170, 95), Root, "outside outer clip");
        frame.AssertPixel("Nested_clipping_and_overflow_visible_match_pixel_output", new Point(220, 45), Status, "overflow visible child");
    }

    [Fact]
    public void Z_order_custom_and_following_ordinary_sibling_are_visible()
    {
        RenderedFrame frame = UiRenderHarness.Render(180, 120, gd =>
        {
            using var ui = new UIManager(gd, TestTheme());
            var root = new Panel { Bounds = new Rectangle(0, 0, 180, 120), DrawBackground = false, DrawBorder = false };
            root.Add(new FlatControl(new Rectangle(20, 20, 80, 60), Toolbar));
            root.Add(new CustomPrimitiveFillControl(new Rectangle(50, 35, 80, 60), TwoD));
            root.Add(new FlatControl(new Rectangle(80, 50, 80, 60), ThreeD));
            root.Add(new FlatControl(new Rectangle(10, 90, 40, 20), Properties) { Visible = false });
            root.Add(new FlatControl(new Rectangle(40, 90, 40, 20), Status) { Enabled = false });
            ui.Add(root);
            ui.Draw();
        });

        frame.AssertPixel("Z_order_custom_and_following_ordinary_sibling_are_visible", new Point(35, 35), Toolbar, "ordinary before custom");
        frame.AssertPixel("Z_order_custom_and_following_ordinary_sibling_are_visible", new Point(65, 45), TwoD, "custom over earlier ordinary");
        frame.AssertPixel("Z_order_custom_and_following_ordinary_sibling_are_visible", new Point(90, 60), ThreeD, "following ordinary over custom");
        frame.AssertPixel("Z_order_custom_and_following_ordinary_sibling_are_visible", new Point(20, 100), Root, "hidden control contributes no pixels");
        frame.AssertPixel("Z_order_custom_and_following_ordinary_sibling_are_visible", new Point(50, 100), Status, "disabled control still follows normal visual rendering");
    }

    [Fact]
    public void Popup_overlay_draws_last_and_closing_restores_underlying_pixels()
    {
        RenderedFrame openFrame = UiRenderHarness.Render(260, 180, gd =>
        {
            using var ui = new UIManager(gd, TestTheme());
            var root = new Panel { Bounds = new Rectangle(0, 0, 260, 180), DrawBackground = false, DrawBorder = false, Overflow = OverflowMode.Clip };
            root.Add(new FlatControl(new Rectangle(0, 0, 260, 40), Toolbar));
            root.Add(new CustomPrimitiveFillControl(new Rectangle(0, 40, 260, 140), TwoD));
            ui.Add(root);
            ui.AddOverlay(new FlatControl(new Rectangle(30, 20, 120, 90), Popup));
            ui.Draw();
        });

        RenderedFrame closedFrame = UiRenderHarness.Render(260, 180, gd =>
        {
            using var ui = new UIManager(gd, TestTheme());
            var root = new Panel { Bounds = new Rectangle(0, 0, 260, 180), DrawBackground = false, DrawBorder = false, Overflow = OverflowMode.Clip };
            root.Add(new FlatControl(new Rectangle(0, 0, 260, 40), Toolbar));
            root.Add(new CustomPrimitiveFillControl(new Rectangle(0, 40, 260, 140), TwoD));
            ui.Add(root);
            ui.Draw();
        });

        openFrame.AssertPixel("Popup_overlay_draws_last_and_closing_restores_underlying_pixels", new Point(40, 30), Popup, "popup over toolbar");
        openFrame.AssertPixel("Popup_overlay_draws_last_and_closing_restores_underlying_pixels", new Point(40, 70), Popup, "popup over 2D");
        openFrame.AssertPixel("Popup_overlay_draws_last_and_closing_restores_underlying_pixels", new Point(200, 20), Toolbar, "toolbar outside popup");
        openFrame.AssertPixel("Popup_overlay_draws_last_and_closing_restores_underlying_pixels", new Point(200, 70), TwoD, "2D outside popup");
        closedFrame.AssertPixel("Popup_overlay_draws_last_and_closing_restores_underlying_pixels", new Point(40, 30), Toolbar, "closed popup restores toolbar");
        closedFrame.AssertPixel("Popup_overlay_draws_last_and_closing_restores_underlying_pixels", new Point(40, 70), TwoD, "closed popup restores 2D");
    }

    [Fact]
    public void Render_target_and_graphics_state_are_restored_after_custom_content()
    {
        RenderedFrame frame = UiRenderHarness.Render(240, 160, gd =>
        {
            using var ui = new UIManager(gd, TestTheme());
            var root = new Panel { Bounds = new Rectangle(0, 0, 240, 160), DrawBackground = false, DrawBorder = false };
            root.Add(new StateMutatingCustomControl(new Rectangle(20, 20, 90, 80), TwoD));
            root.Add(new FlatControl(new Rectangle(120, 20, 90, 80), Properties));
            root.Add(new FlatControl(new Rectangle(20, 110, 190, 30), Status));
            ui.Add(root);
            ui.Draw();
            Assert.Single(gd.GetRenderTargets());
        });

        frame.AssertPixel("Render_target_and_graphics_state_are_restored_after_custom_content", new Point(40, 40), TwoD, "custom composited into bounds");
        frame.AssertPixel("Render_target_and_graphics_state_are_restored_after_custom_content", new Point(140, 40), Properties, "following sibling after custom");
        frame.AssertPixel("Render_target_and_graphics_state_are_restored_after_custom_content", new Point(50, 120), Status, "later ordinary sibling after state mutation");
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
        public Color Color { get; }

        public FlatControl(Rectangle bounds, Color color)
        {
            Bounds = bounds;
            Color = color;
        }

        public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
        {
            if (!Visible)
                return;
            renderer.FillRect(sb, AbsoluteBounds, Color);
            DrawChildren(sb, renderer, theme);
        }
    }

    private sealed class CustomPrimitiveFillControl : Control
    {
        public Color Color { get; }

        public CustomPrimitiveFillControl(Rectangle bounds, Color color)
        {
            Bounds = bounds;
            Color = color;
        }

        public CustomPrimitiveFillControl() : this(Rectangle.Empty, Color.Transparent)
        {
        }

        public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
        {
            if (!Visible)
                return;

            Rectangle clip = Rectangle.Intersect(AbsoluteBounds, EffectiveClipBounds);
            renderer.DrawCustomContent(sb, clip, context => PrimitiveFill(context.GraphicsDevice, AbsoluteBounds, clip, Color));
            DrawChildren(sb, renderer, theme);
        }
    }

    private sealed class CustomRenderTargetControl : Control
    {
        private readonly Color _color;

        public CustomRenderTargetControl(Rectangle bounds, Color color)
        {
            Bounds = bounds;
            _color = color;
        }

        public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
        {
            if (!Visible)
                return;

            Rectangle bounds = AbsoluteBounds;
            Rectangle clip = Rectangle.Intersect(bounds, EffectiveClipBounds);
            renderer.DrawCustomContent(sb, clip, context =>
            {
                using var target = new RenderTarget2D(context.GraphicsDevice, bounds.Width, bounds.Height, false, SurfaceFormat.Color, DepthFormat.None);
                using var localBatch = new SpriteBatch(context.GraphicsDevice);
                using Texture2D pixel = CreatePixel(context.GraphicsDevice);

                context.GraphicsDevice.SetRenderTarget(target);
                localBatch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.PointClamp);
                localBatch.Draw(pixel, new Rectangle(0, 0, bounds.Width, bounds.Height), _color);
                localBatch.End();

                context.RestoreUiRenderTarget();
                localBatch.Begin(blendState: BlendState.AlphaBlend, samplerState: SamplerState.PointClamp);
                localBatch.Draw(target, bounds, Color.White);
                localBatch.End();
            });
            DrawChildren(sb, renderer, theme);
        }
    }

    private sealed class StateMutatingCustomControl : Control
    {
        private readonly Color _color;

        public StateMutatingCustomControl(Rectangle bounds, Color color)
        {
            Bounds = bounds;
            _color = color;
        }

        public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
        {
            if (!Visible)
                return;

            Rectangle bounds = AbsoluteBounds;
            Rectangle clip = Rectangle.Intersect(bounds, EffectiveClipBounds);
            renderer.DrawCustomContent(sb, clip, context =>
            {
                using var target = new RenderTarget2D(context.GraphicsDevice, bounds.Width, bounds.Height, false, SurfaceFormat.Color, DepthFormat.Depth24);
                context.GraphicsDevice.SetRenderTarget(target);
                context.GraphicsDevice.ScissorRectangle = new Rectangle(1, 1, 2, 2);
                context.GraphicsDevice.RasterizerState = new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None };
                context.GraphicsDevice.BlendState = BlendState.Additive;
                context.GraphicsDevice.DepthStencilState = DepthStencilState.Default;
                context.GraphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
                PrimitiveFill(context.GraphicsDevice, new Rectangle(0, 0, bounds.Width, bounds.Height), new Rectangle(0, 0, bounds.Width, bounds.Height), _color);
                context.RestoreUiRenderTarget();
                using var localBatch = new SpriteBatch(context.GraphicsDevice);
                localBatch.Begin(blendState: BlendState.AlphaBlend, samplerState: SamplerState.PointClamp);
                localBatch.Draw(target, bounds, Color.White);
                localBatch.End();
            });
            DrawChildren(sb, renderer, theme);
        }
    }

    private static void PrimitiveFill(GraphicsDevice gd, Rectangle bounds, Rectangle clip, Color color)
    {
        Rectangle fill = Rectangle.Intersect(bounds, clip);
        if (fill.Width <= 0 || fill.Height <= 0)
            return;

        using var effect = new BasicEffect(gd)
        {
            VertexColorEnabled = true,
            World = Matrix.Identity,
            View = Matrix.Identity,
            Projection = Matrix.CreateOrthographicOffCenter(0, gd.Viewport.Width, gd.Viewport.Height, 0, 0, 1),
        };
        gd.RasterizerState = new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None };
        gd.ScissorRectangle = clip;

        var vertices = new[]
        {
            new VertexPositionColor(new Vector3(fill.Left, fill.Top, 0), color),
            new VertexPositionColor(new Vector3(fill.Right, fill.Top, 0), color),
            new VertexPositionColor(new Vector3(fill.Left, fill.Bottom, 0), color),
            new VertexPositionColor(new Vector3(fill.Right, fill.Top, 0), color),
            new VertexPositionColor(new Vector3(fill.Right, fill.Bottom, 0), color),
            new VertexPositionColor(new Vector3(fill.Left, fill.Bottom, 0), color),
        };

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

    private sealed class RenderedFrame(int width, int height, Color[] pixels)
    {
        public void AssertPixel(string testName, Point point, Color expected, string label)
        {
            Color actual = pixels[point.Y * width + point.X];
            if (actual == expected)
                return;

            string artifact = SaveArtifacts(testName, point, expected, actual, label);
            Assert.Fail($"{label} at {point}: expected {expected}, actual {actual}. Artifact: {artifact}");
        }

        private string SaveArtifacts(string testName, Point point, Color expected, Color actual, string label)
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "TestResults", "UiRenderFailures");
            Directory.CreateDirectory(dir);
            string safeName = string.Concat(testName.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
            string pngPath = Path.Combine(dir, safeName + ".png");
            string txtPath = Path.Combine(dir, safeName + ".txt");
            WritePng(pngPath, width, height, pixels);
            File.WriteAllText(txtPath,
                $"label: {label}{Environment.NewLine}coordinate: {point}{Environment.NewLine}expected: {expected}{Environment.NewLine}actual: {actual}{Environment.NewLine}");
            return pngPath;
        }

        private static void WritePng(string path, int width, int height, Color[] pixels)
        {
            using FileStream stream = File.Create(path);
            stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);
            WriteChunk(stream, "IHDR", BuildHeader(width, height));
            using var raw = new MemoryStream();
            for (int y = 0; y < height; y++)
            {
                raw.WriteByte(0);
                for (int x = 0; x < width; x++)
                {
                    Color color = pixels[y * width + x];
                    raw.WriteByte(color.R);
                    raw.WriteByte(color.G);
                    raw.WriteByte(color.B);
                    raw.WriteByte(color.A);
                }
            }
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                zlib.Write(raw.ToArray());
            WriteChunk(stream, "IDAT", compressed.ToArray());
            WriteChunk(stream, "IEND", []);
        }

        private static byte[] BuildHeader(int width, int height)
        {
            byte[] header = new byte[13];
            WriteBigEndian(header, 0, width);
            WriteBigEndian(header, 4, height);
            header[8] = 8;
            header[9] = 6;
            return header;
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            Span<byte> length = stackalloc byte[4];
            WriteBigEndian(length, 0, data.Length);
            stream.Write(length);
            stream.Write(typeBytes);
            stream.Write(data);
            uint crc = Crc32(typeBytes, data);
            Span<byte> crcBytes = stackalloc byte[4];
            WriteBigEndian(crcBytes, 0, unchecked((int)crc));
            stream.Write(crcBytes);
        }

        private static uint Crc32(byte[] type, byte[] data)
        {
            uint crc = 0xffffffff;
            foreach (byte value in type)
                crc = UpdateCrc(crc, value);
            foreach (byte value in data)
                crc = UpdateCrc(crc, value);
            return crc ^ 0xffffffff;
        }

        private static uint UpdateCrc(uint crc, byte value)
        {
            crc ^= value;
            for (int k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
            return crc;
        }

        private static void WriteBigEndian(Span<byte> buffer, int offset, int value)
        {
            buffer[offset] = (byte)((value >> 24) & 0xff);
            buffer[offset + 1] = (byte)((value >> 16) & 0xff);
            buffer[offset + 2] = (byte)((value >> 8) & 0xff);
            buffer[offset + 3] = (byte)(value & 0xff);
        }
    }

    private static class UiRenderHarness
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
            private readonly GraphicsDeviceManager _graphics;
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
                _graphics = new GraphicsDeviceManager(this)
                {
                    PreferredBackBufferWidth = width,
                    PreferredBackBufferHeight = height,
                    SynchronizeWithVerticalRetrace = false,
                };
                IsFixedTimeStep = false;
                IsMouseVisible = false;
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
