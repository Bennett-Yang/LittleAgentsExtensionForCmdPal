using LittleAgentsExtension.Llm;
using Xunit;

namespace LittleAgentsExtension.Tests;

public sealed class TemplateRendererTests
{
    [Fact]
    public void Render_replaces_input_only_placeholder()
    {
        Assert.Equal("hello", TemplateRenderer.Render("{input}", "hello", null));
    }

    [Fact]
    public void Render_replaces_selection_only_placeholder()
    {
        Assert.Equal("world", TemplateRenderer.Render("{selection}", null, "world"));
    }

    [Fact]
    public void Render_replaces_both_placeholders()
    {
        Assert.Equal("a-b", TemplateRenderer.Render("{input}-{selection}", "a", "b"));
    }

    [Fact]
    public void Render_leaves_template_without_placeholders_verbatim()
    {
        Assert.Equal("plain text", TemplateRenderer.Render("plain text", "x", "y"));
    }

    [Fact]
    public void Render_uses_empty_string_for_null_input()
    {
        Assert.Equal("", TemplateRenderer.Render("{input}", null, "ignored"));
    }

    [Fact]
    public void Render_uses_empty_string_for_null_selection()
    {
        Assert.Equal("", TemplateRenderer.Render("{selection}", "ignored", null));
    }

    [Fact]
    public void Render_honors_literal_brace_escapes()
    {
        Assert.Equal("{x}", TemplateRenderer.Render("{{x}}", null, null));
    }

    [Fact]
    public void Render_leaves_unknown_placeholder_unchanged()
    {
        Assert.Equal("{foo}", TemplateRenderer.Render("{foo}", "x", "y"));
    }

    [Fact]
    public void Render_truncates_selection_over_8000_chars()
    {
        string selection = new('a', 8001);
        string expected = "[truncated to 8000 chars]\n" + new string('a', 8000);

        Assert.Equal(expected, TemplateRenderer.Render("{selection}", null, selection));
    }

    [Fact]
    public void Render_is_case_sensitive_for_input_placeholder()
    {
        Assert.Equal("{Input}", TemplateRenderer.Render("{Input}", "hello", "world"));
    }

    [Fact]
    public void Render_keeps_escaped_input_literal()
    {
        Assert.Equal("{input}", TemplateRenderer.Render("{{input}}", "hello", "world"));
    }
}
