using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AudiobookServer.Tests.Integration;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class ServerInfoTests(ApiFixture api)
{
    [Fact]
    public async Task Answers_before_sign_in_with_a_stable_shape()
    {
        using var client = api.CreateClient();
        var response = await client.GetAsync("/api/server-info");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Pinned like the book contract: an app reads these before anything else.
        Assert.Equal(
            ["apiVersion", "product", "version"],
            info.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToList());
        Assert.Equal("AudiobookServer", info.GetProperty("product").GetString());
        Assert.Equal(1, info.GetProperty("apiVersion").GetInt32());
        Assert.False(string.IsNullOrEmpty(info.GetProperty("version").GetString()));
    }
}
