using System.Net;
using System.Text;
using System.Text.Json;
using DeploySeal.Nop.Core;
using DeploySeal.Nop.Core.Inventory;

namespace DeploySeal.Nop.Core.Tests;

/// <summary>
/// The inventory snapshot's canonical form (contract §6: sorted, fixed key order, minimal
/// escaping, stable SHA-256), the item limits, the request body shape, and the client's
/// behaviour against every kind of answer (201, 200, 4xx problem, transport failure with one
/// retry, incomplete settings). C# 11 only: this file also compiles in the .NET 7 SDK container.
/// </summary>
public class InventoryTests
{
    private static readonly DateTime CapturedAt = new(2026, 9, 17, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ItemsJson_IsSortedCompactAndMinimallyEscaped()
    {
        var canonical = InventorySnapshotBuilder.Canonicalize(new[]
        {
            new InventoryItem("Widgets.Zeta", "Zeta <b>", "1.0", true),
            new InventoryItem("Payments.Alpha", "Ålpha & co", "2.1.0", false),
        });

        var json = InventorySnapshotBuilder.ItemsJson(canonical);

        Assert.Equal(
            "[{\"systemName\":\"Payments.Alpha\",\"name\":\"Ålpha & co\",\"version\":\"2.1.0\",\"enabled\":false}," +
            "{\"systemName\":\"Widgets.Zeta\",\"name\":\"Zeta <b>\",\"version\":\"1.0\",\"enabled\":true}]",
            json);
        Assert.Matches("^[0-9a-f]{64}$", InventorySnapshotBuilder.Sha256Hex(json));
    }

    [Fact]
    public void Build_GivesTheSameFingerprint_WhateverOrderTheItemsArrivedIn()
    {
        var a = InventorySnapshotBuilder.Build("4.90.8", "4.90.8+a1b2c3d", CapturedAt, new[]
        {
            new InventoryItem("B", "B", "1", true),
            new InventoryItem("A", "A", "1", true),
        });
        var b = InventorySnapshotBuilder.Build("4.90.8", "4.90.8+a1b2c3d", CapturedAt, new[]
        {
            new InventoryItem(" A ", "A ", "1", true),
            new InventoryItem("B", "", " 1", true),
        });

        Assert.Equal(a.Sha256, b.Sha256);
        Assert.Equal(new[] { "A", "B" }, a.Items.Select(i => i.SystemName).ToArray());
        Assert.Equal("B", b.Items[1].Name);
        Assert.Equal("nopcommerce", a.Platform);
        Assert.Equal("4.90.8+a1b2c3d", a.BuildMarker);
        Assert.Equal(0, a.DroppedCount);
    }

    [Fact]
    public void Canonicalize_DropsBlankSystemNames_KeepsTheFirstDuplicate_ClampsFields_AndCapsAt500()
    {
        var items = new List<InventoryItem>
        {
            new("", "no name", "1", true),
            new("Dup", "First", "1", true),
            new("Dup", "Second", "2", false),
            // Sorts before the "P…" fillers (ordinal), so the cap below cannot cut it.
            new(new string('A', 200), new string('n', 300), new string('v', 40), true),
            new("NoVersion", "x", "", true),
        };
        for (var i = 0; i < 600; i++)
            items.Add(new InventoryItem($"P{i:0000}", $"P{i}", "1", true));

        var canonical = InventorySnapshotBuilder.Canonicalize(items);

        Assert.Equal(DeploySealContract.InventoryMaxItems, canonical.Count);
        var dup = Assert.Single(canonical, i => i.SystemName == "Dup");
        Assert.Equal("First", dup.Name);
        var clamped = Assert.Single(canonical, i => i.SystemName.StartsWith("AAA", StringComparison.Ordinal));
        Assert.Equal(DeploySealContract.InventorySystemNameMaxLength, clamped.SystemName.Length);
        Assert.Equal(DeploySealContract.InventoryNameMaxLength, clamped.Name.Length);
        Assert.Equal(DeploySealContract.InventoryVersionMaxLength, clamped.Version.Length);
        Assert.Equal("0", Assert.Single(canonical, i => i.SystemName == "NoVersion").Version);
        Assert.DoesNotContain(canonical, i => i.SystemName.Length == 0);

        var snapshot = InventorySnapshotBuilder.Build("4.90.8", null, CapturedAt, items);
        Assert.Equal(items.Count - DeploySealContract.InventoryMaxItems, snapshot.DroppedCount);
    }

    [Fact]
    public void RequestJson_HasTheContractShape()
    {
        var snapshot = InventorySnapshotBuilder.Build(" 4.60.6 ", "", CapturedAt, new[]
        {
            new InventoryItem("Payments.PayPalCommerce", "PayPal Commerce", "4.60.1", true),
        });

        using var doc = JsonDocument.Parse(snapshot.ToRequestJson());
        var root = doc.RootElement;

        Assert.Equal(new[] { "platform", "platformVersion", "buildMarker", "capturedAt", "items" },
            root.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("nopcommerce", root.GetProperty("platform").GetString());
        Assert.Equal("4.60.6", root.GetProperty("platformVersion").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("buildMarker").ValueKind);
        Assert.Equal("2026-09-17T06:00:00.000Z", root.GetProperty("capturedAt").GetString());
        var item = Assert.Single(root.GetProperty("items").EnumerateArray());
        Assert.Equal(new[] { "systemName", "name", "version", "enabled" }, item.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.True(item.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void EndpointUrl_UsesTheDefaultHost_TrimsSlashes_AndEscapesTheKey()
    {
        Assert.Equal("https://api.deployseal.com/api/v1/sites/ls_abc/inventory", InventoryClient.EndpointUrl(null, "ls_abc"));
        Assert.Equal("https://api.deployseal.com/api/v1/sites/ls_abc/inventory", InventoryClient.EndpointUrl("  ", " ls_abc "));
        Assert.Equal("http://host.docker.internal:18093/api/v1/sites/ls_abc/inventory", InventoryClient.EndpointUrl("http://host.docker.internal:18093/", "ls_abc"));
        Assert.Equal("https://api.deployseal.com/api/v1/sites/a%20b%2Fc/inventory", InventoryClient.EndpointUrl(null, "a b/c"));
    }

    [Fact]
    public async Task Send_PostsBearerJson_AndReadsA201()
    {
        var handler = new FakeHandler((req, n) => Json(HttpStatusCode.Created, "{\"id\":\"9d1f\",\"capturedAt\":\"2026-09-17T06:00:00Z\",\"itemCount\":1}"));
        var client = new InventoryClient(new HttpClient(handler));
        var snapshot = InventorySnapshotBuilder.Build("4.90.8", "4.90.8", CapturedAt, new[] { new InventoryItem("X", "X", "1", true) });

        var result = await client.SendAsync("https://api.example.test/", "ds_live_key", "ls_site", snapshot);

        Assert.True(result.Ok);
        Assert.True(result.Created);
        Assert.Equal(201, result.StatusCode);
        Assert.Equal("9d1f", result.SnapshotId);
        Assert.Equal(1, result.ItemCount);
        Assert.Equal(CapturedAt, result.CapturedAt);
        Assert.Equal(1, result.Attempts);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://api.example.test/api/v1/sites/ls_site/inventory", sent.Url);
        Assert.Equal("Bearer ds_live_key", sent.Authorization);
        Assert.Equal("application/json", sent.ContentType);
        Assert.Equal(snapshot.ToRequestJson(), sent.Body);
    }

    [Fact]
    public async Task Send_TreatsA200AsAlreadyOnRecord()
    {
        var handler = new FakeHandler((req, n) => Json(HttpStatusCode.OK, "{\"id\":\"same\",\"itemCount\":3}"));
        var result = await new InventoryClient(new HttpClient(handler)).SendAsync(null, "k", "ls", Snapshot());

        Assert.True(result.Ok);
        Assert.False(result.Created);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("same", result.SnapshotId);
    }

    [Theory]
    [InlineData(401, "not accepted")]
    [InlineData(403, "Write scope")]
    [InlineData(404, "same DeploySeal organisation")]
    [InlineData(429, "rate limited")]
    public async Task Send_ExplainsAuthAndOwnershipFailures(int status, string expectedFragment)
    {
        var handler = new FakeHandler((req, n) => Json((HttpStatusCode)status, "{\"status\":" + status + ",\"title\":\"x\"}"));
        var result = await new InventoryClient(new HttpClient(handler)).SendAsync(null, "k", "ls", Snapshot());

        Assert.False(result.Ok);
        Assert.Equal(status, result.StatusCode);
        Assert.Contains("HTTP " + status, result.Error);
        Assert.Contains(expectedFragment, result.Error);
        Assert.Equal(1, result.Attempts);
    }

    [Fact]
    public async Task Send_SurfacesTheFirstFieldErrorOfA400()
    {
        var handler = new FakeHandler((req, n) => Json(HttpStatusCode.BadRequest,
            "{\"status\":400,\"title\":\"Invalid inventory snapshot.\",\"detail\":\"See errors.\",\"errors\":{\"items\":[\"At most 500 items per snapshot.\"]}}"));
        var result = await new InventoryClient(new HttpClient(handler)).SendAsync(null, "k", "ls", Snapshot());

        Assert.False(result.Ok);
        Assert.Equal("HTTP 400: See errors. items: At most 500 items per snapshot.", result.Error);
    }

    [Fact]
    public async Task Send_RetriesOnceOnATransportFailure_ThenReportsWithoutThrowing()
    {
        InventoryClient.RetryDelay = TimeSpan.Zero;
        var handler = new FakeHandler((req, n) => throw new HttpRequestException("Connection refused"));
        var result = await new InventoryClient(new HttpClient(handler)).SendAsync("http://127.0.0.1:1", "k", "ls", Snapshot());

        Assert.False(result.Ok);
        Assert.Null(result.StatusCode);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("Could not reach http://127.0.0.1:1/api/v1/sites/ls/inventory", result.Error);
        Assert.Contains("Connection refused", result.Error);
    }

    [Fact]
    public async Task Send_SucceedsOnTheRetry()
    {
        InventoryClient.RetryDelay = TimeSpan.Zero;
        var handler = new FakeHandler((req, n) => n == 1 ? throw new HttpRequestException("reset") : Json(HttpStatusCode.Created, "{\"id\":\"ok\"}"));
        var result = await new InventoryClient(new HttpClient(handler)).SendAsync(null, "k", "ls", Snapshot());

        Assert.True(result.Ok);
        Assert.Equal(2, result.Attempts);
    }

    [Fact]
    public async Task Send_DoesNotRetryAnHttpError_AndNeverCallsOutWithoutAKeyOrSite()
    {
        var handler = new FakeHandler((req, n) => Json(HttpStatusCode.InternalServerError, ""));
        var client = new InventoryClient(new HttpClient(handler));

        var server = await client.SendAsync(null, "k", "ls", Snapshot());
        Assert.False(server.Ok);
        Assert.Equal(1, server.Attempts);
        Assert.Equal("HTTP 500.", server.Error);

        var noKey = await client.SendAsync(null, "", "ls", Snapshot());
        var noSite = await client.SendAsync(null, "k", " ", Snapshot());
        Assert.False(noKey.Ok);
        Assert.False(noSite.Ok);
        Assert.Contains("API key", noKey.Error);
        Assert.Contains("site key", noSite.Error);
        Assert.Equal(1, handler.Requests.Count);
    }

    private static InventorySnapshot Snapshot() =>
        InventorySnapshotBuilder.Build("4.90.8", null, CapturedAt, new[] { new InventoryItem("X", "X", "1", true) });

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _respond;

        public FakeHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<(HttpMethod Method, string Url, string? Authorization, string? ContentType, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(),
                request.Content?.Headers.ContentType?.MediaType, body));
            return _respond(request, Requests.Count);
        }
    }
}
