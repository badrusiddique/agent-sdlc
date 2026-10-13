using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HelloWorld.Api.Tests;

public sealed class ApiRoundTripTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _client;

    public ApiRoundTripTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_round_trip()
    {
        var response = await _client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();
        RoundTripRecorder.Record("HealthOk", "GET", "/health", (int)response.StatusCode, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = JsonSerializer.Deserialize<HealthDto>(body, JsonOpts);
        Assert.Equal("ok", dto?.Status);
    }

    [Fact]
    public async Task Thought_http_round_trip()
    {
        // Force local fallback so TESTING stays fast and deterministic in the pipeline host.
        Environment.SetEnvironmentVariable("OLLAMA_BASE_URL", "http://127.0.0.1:9");

        var response = await _client.GetAsync("/api/thought");
        var body = await response.Content.ReadAsStringAsync();
        RoundTripRecorder.Record("Thought", "GET", "/api/thought", (int)response.StatusCode, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = JsonSerializer.Deserialize<ThoughtDto>(body, JsonOpts);
        Assert.NotNull(dto);
        Assert.False(string.IsNullOrWhiteSpace(dto.Thought));
        Assert.False(string.IsNullOrWhiteSpace(dto.Source));
    }

    private sealed record HealthDto(string Status);

    private sealed record ThoughtDto(string Thought, string Source);
}
