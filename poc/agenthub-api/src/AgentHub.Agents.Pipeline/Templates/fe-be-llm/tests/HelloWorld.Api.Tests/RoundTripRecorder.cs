using System.Collections.Concurrent;
using System.Text.Json;

namespace HelloWorld.Api.Tests;

internal static class RoundTripRecorder
{
    private static readonly ConcurrentDictionary<string, RoundTrip> Items = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Record(string name, string method, string path, int status, string body)
    {
        Items[name] = new RoundTrip(name, method, path, status, body, DateTimeOffset.UtcNow);
        Flush();
    }

    private static void Flush()
    {
        var path = Environment.GetEnvironmentVariable("BGX_ROUNDTRIP_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var payload = new
        {
            kind = "http-roundtrip",
            roundTrips = Items.Values.OrderBy(v => v.Name, StringComparer.Ordinal).ToArray()
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions));
    }

    private sealed record RoundTrip(
        string Name,
        string Method,
        string Path,
        int Status,
        string Body,
        DateTimeOffset At);
}
