using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Calculator.Api.Tests;

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
        var doc = JsonSerializer.Deserialize<HealthDto>(body, JsonOpts);
        Assert.Equal("ok", doc?.Status);
    }

    [Theory]
    [InlineData(2, 3, 5)]
    [InlineData(0, 0, 0)]
    [InlineData(10, -4, 6)]
    public async Task Add_http_round_trip(double a, double b, double expected)
    {
        var path = $"/api/add?a={a}&b={b}";
        var response = await _client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        RoundTripRecorder.Record($"Add_{a}_{b}", "GET", path, (int)response.StatusCode, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = JsonSerializer.Deserialize<CalcDto>(body, JsonOpts);
        Assert.NotNull(dto);
        Assert.Equal("add", dto.Operation);
        Assert.Equal(expected, dto.Result);
    }

    [Fact]
    public async Task Subtract_http_round_trip_when_endpoint_present()
    {
        const string path = "/api/subtract?a=5&b=2";
        var response = await _client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        var media = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        // Add-only templates fall through to index.html (200 text/html), not 404.
        if (response.StatusCode == HttpStatusCode.NotFound
            || !media.Contains("json", StringComparison.OrdinalIgnoreCase)
            || body.TrimStart().StartsWith('<'))
        {
            RoundTripRecorder.Record(
                "Subtract_skipped",
                "GET",
                path,
                (int)response.StatusCode,
                "endpoint not materialized (add-only run)");
            return;
        }

        RoundTripRecorder.Record("Subtract_5_2", "GET", path, (int)response.StatusCode, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = JsonSerializer.Deserialize<CalcDto>(body, JsonOpts);
        Assert.NotNull(dto);
        Assert.Equal(3, dto.Result);
    }

    private sealed record HealthDto(string Status);

    private sealed record CalcDto(string? Operation, double A, double B, double Result);
}
