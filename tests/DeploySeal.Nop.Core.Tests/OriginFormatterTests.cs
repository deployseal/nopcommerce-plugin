using DeploySeal.Nop.Core;

namespace DeploySeal.Nop.Core.Tests;

public class OriginFormatterTests
{
    [Theory]
    [InlineData("https://staging.example.com/", "https://staging.example.com")]
    [InlineData("https://staging.example.com", "https://staging.example.com")]
    [InlineData("HTTPS://Staging.Example.COM/some/path?x=1#f", "https://staging.example.com")]
    [InlineData("https://staging.example.com:443/", "https://staging.example.com")]
    [InlineData("http://staging.example.com:80/", "http://staging.example.com")]
    [InlineData("https://staging.example.com:8443/", "https://staging.example.com:8443")]
    [InlineData("http://localhost:8080/", "http://localhost:8080")]
    [InlineData("localhost:8080", "http://localhost:8080")]
    [InlineData("shop.example.com", "http://shop.example.com")]
    [InlineData("http://[::1]:5000/", "http://[::1]:5000")]
    public void FromUrl_matches_the_browser_origin(string url, string expected)
    {
        Assert.Equal(expected, OriginFormatter.FromUrl(url));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("ftp://example.com/")]
    [InlineData("not a url at all")]
    public void FromUrl_rejects_non_http_input(string? url)
    {
        Assert.Null(OriginFormatter.FromUrl(url));
    }

    [Fact]
    public void ForStore_lists_the_store_url_first()
    {
        var origins = OriginFormatter.ForStore("https://shop.example.com/", sslEnabled: true, hosts: null);
        Assert.Equal(new[] { "https://shop.example.com" }, origins);
    }

    [Fact]
    public void ForStore_adds_https_twin_when_ssl_is_enabled_on_an_http_url()
    {
        var origins = OriginFormatter.ForStore("http://shop.example.com/", sslEnabled: true, hosts: null);
        Assert.Equal(new[] { "http://shop.example.com", "https://shop.example.com" }, origins);
    }

    [Fact]
    public void ForStore_keeps_http_only_when_ssl_is_off()
    {
        var origins = OriginFormatter.ForStore("http://localhost:8080/", sslEnabled: false, hosts: null);
        Assert.Equal(new[] { "http://localhost:8080" }, origins);
    }

    [Fact]
    public void ForStore_expands_the_hosts_list_and_dedupes()
    {
        var origins = OriginFormatter.ForStore("https://shop.example.com/", sslEnabled: true,
            hosts: "shop.example.com, www.shop.example.com,shop.example.com:8443 , ,");

        Assert.Equal(
            new[] { "https://shop.example.com", "https://www.shop.example.com", "https://shop.example.com:8443" },
            origins);
    }

    [Fact]
    public void ForStore_uses_http_for_hosts_when_the_store_url_is_http_and_adds_https_when_ssl_enabled()
    {
        var origins = OriginFormatter.ForStore("http://shop.example.com/", sslEnabled: true, hosts: "www.shop.example.com");

        Assert.Equal(
            new[] { "http://shop.example.com", "https://shop.example.com", "http://www.shop.example.com", "https://www.shop.example.com" },
            origins);
    }

    [Fact]
    public void ForStore_is_empty_for_garbage()
    {
        Assert.Empty(OriginFormatter.ForStore(null, false, null));
        Assert.Empty(OriginFormatter.ForStore("", true, ""));
    }
}
