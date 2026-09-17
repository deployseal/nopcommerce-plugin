using DeploySeal.Nop.Core;

namespace DeploySeal.Nop.Core.Tests;

public class EnvironmentLabelTests
{
    [Theory]
    [InlineData("staging", "staging")]
    [InlineData("Staging", "staging")]
    [InlineData("  UAT 2 ", "uat-2")]
    [InlineData("pre_prod/eu", "pre-prod-eu")]
    [InlineData("--prod--", "prod")]
    [InlineData("Ünïcödé Test", "unicode-test")]
    [InlineData("!!!", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Slugify_produces_contract_labels(string? input, string expected)
    {
        Assert.Equal(expected, EnvironmentLabel.Slugify(input));
    }

    [Fact]
    public void Slugify_cuts_at_32_and_never_ends_on_a_hyphen()
    {
        var input = "abcdefghij-klmnopqrst-uvwxyz0123-4567";
        var slug = EnvironmentLabel.Slugify(input);

        Assert.True(slug.Length <= DeploySealContract.EnvironmentLabelMaxLength);
        Assert.Equal("abcdefghij-klmnopqrst-uvwxyz0123", slug);
        Assert.False(slug.EndsWith('-'));

        var awkward = new string('a', 31) + "-b";
        Assert.Equal(new string('a', 31), EnvironmentLabel.Slugify(awkward));
    }

    [Theory]
    [InlineData("staging", true)]
    [InlineData("Staging", false)]
    [InlineData("stag ing", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_matches_slugify_fixed_point(string? input, bool expected)
    {
        Assert.Equal(expected, EnvironmentLabel.IsValid(input));
    }

    [Theory]
    [InlineData("https://staging.example.com/", "staging")]
    [InlineData("https://shop-uat.example.com/", "staging")]
    [InlineData("http://test.example.com:8080/", "staging")]
    [InlineData("https://dev.example.com", "staging")]
    [InlineData("http://localhost:8080/", "staging")]
    [InlineData("https://www.example.com/", "production")]
    [InlineData("https://shop.example.co.za/", "production")]
    [InlineData("", "production")]
    [InlineData(null, "production")]
    [InlineData("not a url", "production")]
    public void DeriveFromStoreUrl_guesses_from_host_hints(string? url, string expected)
    {
        Assert.Equal(expected, EnvironmentLabel.DeriveFromStoreUrl(url));
    }
}
