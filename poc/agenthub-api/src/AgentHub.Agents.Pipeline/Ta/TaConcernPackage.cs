using System.Text.Json;

namespace AgentHub.Agents.Pipeline.Ta;

/// <summary>Serialized TA → IA handoff when live verification is red.</summary>
public static class TaConcernPackage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string Build(
        string deployedUrl,
        string resultsJson,
        IReadOnlyList<string> failingNames,
        int attempt)
    {
        object? results = null;
        try
        {
            results = JsonSerializer.Deserialize<object>(resultsJson);
        }
        catch (JsonException)
        {
            results = resultsJson;
        }

        return JsonSerializer.Serialize(
            new
            {
                kind = "RouteBack",
                routeBackTo = "IA_CODEGEN",
                attempt,
                deployedUrl,
                failing = failingNames,
                summary =
                    "Testing Agent found red live checks. Implementation must fix the product against the deployed URL, re-run unit tests, re-deploy, then return to TA.",
                taResults = results,
                at = DateTimeOffset.UtcNow
            },
            JsonOptions);
    }

    public static IReadOnlyList<string> FailingNames(string resultsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(resultsJson);
            if (!doc.RootElement.TryGetProperty("results", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
            {
                return ["unknown"];
            }

            var list = new List<string>();
            foreach (var el in arr.EnumerateArray())
            {
                var ok = el.TryGetProperty("ok", out var o) && o.ValueKind == JsonValueKind.True;
                if (!ok)
                {
                    list.Add(el.TryGetProperty("name", out var n) ? n.GetString() ?? "case" : "case");
                }
            }

            return list.Count == 0 ? ["unknown"] : list;
        }
        catch (JsonException)
        {
            return ["parse-error"];
        }
    }
}
