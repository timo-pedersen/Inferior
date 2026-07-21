using Xunit;

namespace Inferior.UI.Test;

public sealed class InstrumentedCompositionTests
{
    [Fact]
    public void Custom_content_restores_batch_before_following_siblings()
    {
        var log = new List<string>();
        var context = new InstrumentedUiContext(log);

        context.BeginBatch("root");
        context.DrawControl("toolbar");
        context.PushClip("surface");
        context.SuspendForCustom("surface");
        context.DrawCustom("surface");
        context.ResumeAfterCustom("surface");
        context.PopClip("surface");
        context.DrawControl("button");
        context.DrawControl("label");
        context.EndBatch("root");
        context.BeginBatch("overlay");
        context.DrawControl("popup");
        context.EndBatch("overlay");

        Assert.Equal(
        [
            "begin:root",
            "draw:toolbar",
            "push-clip:surface",
            "end:suspend-surface",
            "custom:surface",
            "begin:resume-surface",
            "pop-clip:surface",
            "draw:button",
            "draw:label",
            "end:root",
            "begin:overlay",
            "draw:popup",
            "end:overlay",
        ], log);
        Assert.True(context.BatchActive is false);
        Assert.Empty(context.Clips);
    }

    [Fact]
    public void Nested_custom_content_keeps_clip_stack_balanced()
    {
        var log = new List<string>();
        var context = new InstrumentedUiContext(log);

        context.BeginBatch("root");
        context.PushClip("outer");
        context.PushClip("inner");
        context.SuspendForCustom("inner");
        context.DrawCustom("inner");
        context.ResumeAfterCustom("inner");
        context.PopClip("inner");
        context.PopClip("outer");
        context.DrawControl("following-label");
        context.EndBatch("root");

        Assert.Equal(["outer", "inner", "inner", "outer"], context.ClipHistory);
        Assert.Empty(context.Clips);
        Assert.Contains("draw:following-label", log);
        Assert.True(log.IndexOf("begin:resume-inner") < log.IndexOf("draw:following-label"));
    }

    [Fact]
    public void Multiple_custom_controls_do_not_suppress_properties_or_status()
    {
        var log = new List<string>();
        var context = new InstrumentedUiContext(log);

        context.BeginBatch("root");
        context.DrawControl("toolbar");
        DrawCustomSurface(context, "2d");
        DrawCustomSurface(context, "3d");
        context.DrawControl("properties");
        context.DrawControl("status");
        context.EndBatch("root");
        context.BeginBatch("overlay");
        context.DrawControl("popup");
        context.EndBatch("overlay");

        Assert.Equal(["custom:2d", "custom:3d"], log.Where(entry => entry.StartsWith("custom:")));
        Assert.True(log.IndexOf("draw:properties") > log.IndexOf("begin:resume-3d"));
        Assert.True(log.IndexOf("draw:status") > log.IndexOf("draw:properties"));
        Assert.True(log.IndexOf("draw:popup") > log.IndexOf("draw:status"));
    }

    [Fact]
    public void Custom_draw_failure_restores_batch_for_error_surface()
    {
        var log = new List<string>();
        var context = new InstrumentedUiContext(log);

        context.BeginBatch("root");
        context.PushClip("custom");
        context.SuspendForCustom("custom");
        void Act()
        {
            try
            {
                throw new InvalidOperationException("controlled custom failure");
            }
            finally
            {
                context.ResumeAfterCustom("custom");
                context.PopClip("custom");
            }
        }

        Exception? exception = Record.Exception((Action)Act);
        InvalidOperationException ex = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal("controlled custom failure", ex.Message);
        context.DrawControl("following");
        context.EndBatch("root");
        Assert.Contains("draw:following", log);
        Assert.Empty(context.Clips);
        Assert.False(context.BatchActive);
    }

    [Fact]
    public void Empty_custom_clip_skips_custom_draw_and_keeps_following_sibling()
    {
        var log = new List<string>();
        var context = new InstrumentedUiContext(log);

        context.BeginBatch("root");
        context.DrawControl("before");
        context.SkipCustom("empty");
        context.DrawControl("after");
        context.EndBatch("root");

        Assert.DoesNotContain("custom:empty", log);
        Assert.Contains("draw:after", log);
    }

    private static void DrawCustomSurface(InstrumentedUiContext context, string name)
    {
        context.PushClip(name);
        context.SuspendForCustom(name);
        context.DrawCustom(name);
        context.ResumeAfterCustom(name);
        context.PopClip(name);
    }

    private sealed class InstrumentedUiContext(List<string> log)
    {
        public bool BatchActive { get; private set; }
        public Stack<string> Clips { get; } = [];
        public List<string> ClipHistory { get; } = [];

        public void BeginBatch(string name)
        {
            Assert.False(BatchActive);
            BatchActive = true;
            log.Add($"begin:{name}");
        }

        public void EndBatch(string name)
        {
            Assert.True(BatchActive);
            BatchActive = false;
            log.Add($"end:{name}");
        }

        public void PushClip(string name)
        {
            Assert.True(BatchActive);
            Clips.Push(name);
            ClipHistory.Add(name);
            log.Add($"push-clip:{name}");
        }

        public void PopClip(string name)
        {
            Assert.Equal(name, Clips.Pop());
            ClipHistory.Add(name);
            log.Add($"pop-clip:{name}");
        }

        public void SuspendForCustom(string name)
        {
            Assert.True(BatchActive);
            BatchActive = false;
            log.Add($"end:suspend-{name}");
        }

        public void ResumeAfterCustom(string name)
        {
            Assert.False(BatchActive);
            Assert.NotEmpty(Clips);
            BatchActive = true;
            log.Add($"begin:resume-{name}");
        }

        public void DrawControl(string name)
        {
            Assert.True(BatchActive);
            log.Add($"draw:{name}");
        }

        public void DrawCustom(string name)
        {
            Assert.False(BatchActive);
            Assert.NotEmpty(Clips);
            log.Add($"custom:{name}");
        }

        public void SkipCustom(string name)
        {
            Assert.True(BatchActive);
            log.Add($"skip-custom:{name}");
        }
    }
}
