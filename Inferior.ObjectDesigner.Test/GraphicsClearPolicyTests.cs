using System.Text.RegularExpressions;
using Xunit;

namespace Inferior.ObjectDesigner.Test;

public sealed class GraphicsClearPolicyTests
{
    [Fact]
    public void ObjectDesigner_frame_phases_prepare_before_backbuffer_clear_then_draw_ui()
    {
        string source = File.ReadAllText(FindRepoFile("Inferior.ObjectDesigner", "ObjectDesignerGame.cs"));

        string draw = ExtractMethod(source, "protected override void Draw");

        Assert.True(draw.IndexOf("RenderPerspectiveTarget();", StringComparison.Ordinal) < draw.IndexOf("GraphicsDevice.Clear(new Color(8, 10, 11));", StringComparison.Ordinal));
        Assert.True(draw.IndexOf("GraphicsDevice.Clear(new Color(8, 10, 11));", StringComparison.Ordinal) < draw.IndexOf("_ui.Draw();", StringComparison.Ordinal));
        Assert.True(draw.IndexOf("_ui.Draw();", StringComparison.Ordinal) < draw.IndexOf("base.Draw(gameTime);", StringComparison.Ordinal));
    }

    [Fact]
    public void ObjectDesigner_perspective_ui_composition_does_not_switch_render_targets()
    {
        string source = File.ReadAllText(FindRepoFile("Inferior.ObjectDesigner", "ObjectDesignerGame.cs"));
        string prepare = ExtractMethod(source, "private void RenderPerspectiveTarget");
        string compose = ExtractMethod(source, "private void DrawPerspectiveTexture");

        Assert.Contains("GraphicsDevice.SetRenderTarget(target);", prepare);
        Assert.Contains("GraphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer", prepare);
        Assert.Contains("GraphicsDevice.SetRenderTargets(oldTargets);", prepare);
        Assert.DoesNotContain("SetRenderTarget", compose);
        Assert.DoesNotContain("SetRenderTargets", compose);
        Assert.DoesNotContain("DrawCustomContent = DrawPerspective", source);
        Assert.Contains("DrawContent = DrawPerspectiveTexture", source);
    }

    [Fact]
    public void ObjectDesigner_root_backbuffer_colour_clear_remains_in_draw_only()
    {
        string source = File.ReadAllText(FindRepoFile("Inferior.ObjectDesigner", "ObjectDesignerGame.cs"));
        MatchCollection rootClears = Regex.Matches(source, @"GraphicsDevice\.Clear\s*\(\s*new\s+Color\s*\(");
        Match rootClear = Assert.Single(rootClears);
        string draw = ExtractMethod(source, "protected override void Draw");

        Assert.Contains("GraphicsDevice.Clear(new Color(8, 10, 11));", draw);
        Assert.InRange(rootClear.Index, source.IndexOf("protected override void Draw", StringComparison.Ordinal), source.IndexOf("private void RenderPerspectiveTarget", StringComparison.Ordinal));
    }

    private static string ExtractMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method signature: {signature}");
        int brace = source.IndexOf('{', start);
        Assert.True(brace >= 0, $"Could not find method body: {signature}");
        int depth = 0;
        for (int i = brace; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[start..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Could not extract method: {signature}");
    }

    private static string FindRepoFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {Path.Combine(parts)}");
    }
}
