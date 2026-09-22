using DeploySeal.Nop.Core;

namespace DeploySeal.Nop.Core.Tests;

public class SnippetBuilderTests
{
    [Fact]
    public void Output_is_byte_exact_to_the_contract_tag()
    {
        var tag = SnippetBuilder.Build("https://cdn.deployseal.com", "ls_test0000000000000000000000000000000", "staging", "4.90.8+a1b2c3d", "1.3.0");

        Assert.Equal(
            "<script src=\"https://cdn.deployseal.com/ds-widget.js\" data-ds-site-key=\"ls_test0000000000000000000000000000000\" data-ds-environment=\"staging\" data-ds-build=\"4.90.8+a1b2c3d\" data-ds-installer=\"nopcommerce-plugin/1.3.0\" async></script>",
            tag);
    }

    [Fact]
    public void Installer_is_the_contract_prefix_plus_the_plugin_version_and_obeys_the_alphabet()
    {
        Assert.Equal("nopcommerce-plugin/1.3.0", SnippetBuilder.Installer("1.3.0"));
        Assert.Equal("nopcommerce-plugin/1.3.0-rc.1", SnippetBuilder.Installer(" 1.3.0-RC.1 "));
        Assert.True(SnippetBuilder.Installer("1.3.0").Length <= DeploySealContract.InstallerMaxLength);
        Assert.Matches("^[a-z0-9./-]{1,48}$", SnippetBuilder.Installer("1.3.0"));
        Assert.Equal(48, SnippetBuilder.Installer(new string('9', 60)).Length);
    }

    [Fact]
    public void Installer_attribute_follows_the_build_marker_and_is_omitted_without_a_version()
    {
        Assert.Equal(
            "<script src=\"https://cdn.deployseal.com/ds-widget.js\" data-ds-site-key=\"ls_x\" data-ds-installer=\"nopcommerce-plugin/1.3.0\" async></script>",
            SnippetBuilder.Build(null, "ls_x", null, null, "1.3.0"));
        Assert.DoesNotContain("data-ds-installer", SnippetBuilder.Build(null, "ls_x", "staging", "4.90.8"));
        Assert.DoesNotContain("data-ds-installer", SnippetBuilder.Build(null, "ls_x", "staging", "4.90.8", "  "));
    }

    [Fact]
    public void Default_host_is_used_when_blank_and_trailing_slash_is_tolerated()
    {
        Assert.StartsWith("<script src=\"https://cdn.deployseal.com/ds-widget.js\"", SnippetBuilder.Build(null, "ls_x", null, null));
        Assert.StartsWith("<script src=\"https://cdn.deployseal.com/ds-widget.js\"", SnippetBuilder.Build("", "ls_x", null, null));
        Assert.StartsWith("<script src=\"https://cdn.deployseal.com/ds-widget.js\"", SnippetBuilder.Build("https://cdn.deployseal.com/", "ls_x", null, null));
        Assert.Equal("https://cdn.deployseal.com/ds-widget.js", SnippetBuilder.ScriptUrl(" https://cdn.deployseal.com// "));
    }

    [Fact]
    public void Recommended_attributes_are_omitted_when_empty_not_emitted_blank()
    {
        var tag = SnippetBuilder.Build(null, "ls_x", "", "  ");

        Assert.Equal("<script src=\"https://cdn.deployseal.com/ds-widget.js\" data-ds-site-key=\"ls_x\" async></script>", tag);
        Assert.DoesNotContain("data-ds-environment", tag);
        Assert.DoesNotContain("data-ds-build", tag);
    }

    [Fact]
    public void Build_only_omitted_keeps_environment()
    {
        var tag = SnippetBuilder.Build(null, "ls_x", "staging", null);
        Assert.Equal("<script src=\"https://cdn.deployseal.com/ds-widget.js\" data-ds-site-key=\"ls_x\" data-ds-environment=\"staging\" async></script>", tag);
    }

    [Fact]
    public void No_site_key_means_no_tag_at_all()
    {
        Assert.Equal(string.Empty, SnippetBuilder.Build(null, null, "staging", "4.90.8"));
        Assert.Equal(string.Empty, SnippetBuilder.Build(null, "   ", "staging", "4.90.8"));
    }

    [Fact]
    public void Attribute_values_are_encoded()
    {
        var tag = SnippetBuilder.Build(null, "ls_\"x\"<>&", null, null);
        Assert.Contains("data-ds-site-key=\"ls_&quot;x&quot;&lt;&gt;&amp;\"", tag);
    }

    [Fact]
    public void Tag_never_contains_a_newline()
    {
        var tag = SnippetBuilder.Build(null, "ls_x", "staging", "4.90.8");
        Assert.DoesNotContain('\n', tag);
        Assert.DoesNotContain('\r', tag);
    }
}
