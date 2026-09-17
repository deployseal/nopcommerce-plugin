using DeploySeal.Nop.Core;

namespace DeploySeal.Nop.Core.Tests;

public class BuildMarkerTests
{
    private const string FullSha = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";

    [Fact]
    public void NopVersion_source_emits_the_version_alone()
    {
        var r = BuildMarkerResolver.Resolve(BuildMarkerSource.NopVersion, "4.90.8", FullSha, "ignored");

        Assert.Equal("4.90.8", r.Marker);
        Assert.True(r.HasMarker);
        Assert.Null(r.Warning);
    }

    [Fact]
    public void NopVersionPlusGitSha_appends_the_short_sha_in_contract_form()
    {
        var r = BuildMarkerResolver.Resolve(BuildMarkerSource.NopVersionPlusGitSha, "4.90.8", FullSha, null);

        Assert.Equal("4.90.8+a1b2c3d", r.Marker);
        Assert.Null(r.Warning);
    }

    [Fact]
    public void NopVersionPlusGitSha_degrades_to_the_version_with_a_warning_when_no_sha()
    {
        var r = BuildMarkerResolver.Resolve(BuildMarkerSource.NopVersionPlusGitSha, "4.90.8", null, null);

        Assert.Equal("4.90.8", r.Marker);
        Assert.NotNull(r.Warning);
        Assert.Contains("nopCommerce version", r.SourceDescription);
    }

    [Fact]
    public void GitSha_source_emits_the_full_sha()
    {
        var r = BuildMarkerResolver.Resolve(BuildMarkerSource.GitSha, "4.90.8", FullSha, null);

        Assert.Equal(FullSha, r.Marker);
        Assert.True(r.Marker.Length >= DeploySealContract.MinShaLength);
        Assert.Null(r.Warning);
    }

    [Fact]
    public void GitSha_source_emits_nothing_when_unreadable()
    {
        var r = BuildMarkerResolver.Resolve(BuildMarkerSource.GitSha, "4.90.8", null, null);

        Assert.Equal(string.Empty, r.Marker);
        Assert.False(r.HasMarker);
        Assert.NotNull(r.Warning);
    }

    [Theory]
    [InlineData("release 2026.09.17", "release2026.09.17")]
    [InlineData("  r-42\t\n", "r-42")]
    [InlineData("v1.2.3", "v1.2.3")]
    public void Manual_source_strips_whitespace_to_a_single_token(string manual, string expected)
    {
        var r = BuildMarkerResolver.Resolve(BuildMarkerSource.Manual, "4.90.8", null, manual);
        Assert.Equal(expected, r.Marker);
        Assert.DoesNotContain(' ', r.Marker);
    }

    [Fact]
    public void Manual_source_emits_nothing_when_empty()
    {
        var r = BuildMarkerResolver.Resolve(BuildMarkerSource.Manual, "4.90.8", FullSha, "   ");
        Assert.False(r.HasMarker);
        Assert.NotNull(r.Warning);
    }

    [Fact]
    public void Markers_are_clamped_to_64_characters()
    {
        var manual = new string('x', 100);
        var r = BuildMarkerResolver.Resolve(BuildMarkerSource.Manual, "4.90.8", null, manual);

        Assert.Equal(DeploySealContract.BuildMarkerMaxLength, r.Marker.Length);
    }

    [Fact]
    public void Unknown_source_falls_back_to_the_default()
    {
        var r = BuildMarkerResolver.Resolve((BuildMarkerSource)99, "4.90.8", FullSha, null);
        Assert.Equal("4.90.8+a1b2c3d", r.Marker);
    }

    [Theory]
    [InlineData("a1b2c3d", "a1b2c3d")]
    [InlineData("A1B2C3D4E5F6\n", "a1b2c3d4e5f6")]
    [InlineData("\n\n  " + FullSha + "  \nsecond line", FullSha)]
    [InlineData(FullSha + " (main) deploy 42", FullSha)]
    public void GitSha_parse_accepts_hex_7_to_40(string content, string expected)
    {
        Assert.Equal(expected, GitSha.Parse(content));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc123")]
    [InlineData("not-a-sha-at-all")]
    [InlineData("g1b2c3d")]
    [InlineData("a1b2c3d4e5f60718293a4b5c6d7e8f9012345678ff")]
    public void GitSha_parse_rejects_non_sha_content(string? content)
    {
        Assert.Null(GitSha.Parse(content));
    }

    [Fact]
    public void GitSha_TryReadFile_reads_a_file_and_tolerates_missing_ones()
    {
        var path = Path.Combine(Path.GetTempPath(), "deployseal-sha-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            Assert.Null(GitSha.TryReadFile(path));
            File.WriteAllText(path, FullSha + "\n");
            Assert.Equal(FullSha, GitSha.TryReadFile(path));
        }
        finally
        {
            File.Delete(path);
        }

        Assert.Null(GitSha.TryReadFile(null));
        Assert.Null(GitSha.TryReadFile(""));
    }

    [Fact]
    public void Options_expose_the_source_as_a_typed_enum_with_a_safe_default()
    {
        var options = new DeploySealWidgetOptions();
        Assert.Equal(BuildMarkerSource.NopVersionPlusGitSha, options.BuildMarkerSource);

        options.BuildMarkerSourceId = 99;
        Assert.Equal(BuildMarkerSource.NopVersionPlusGitSha, options.BuildMarkerSource);

        options.BuildMarkerSource = BuildMarkerSource.Manual;
        Assert.Equal(3, options.BuildMarkerSourceId);
    }
}
