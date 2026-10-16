using System.Diagnostics;
using System.Text.Json;

namespace AgentHub.Agents.Pipeline.Ta;

/// <summary>
/// Runs HTTP smoke (hard gate) then Playwright FE (soft-skip if browsers missing).
/// Returns Combined results JSON and whether the overall status is green.
/// </summary>
public static class TaVerifyRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public sealed record Outcome(bool Green, string ResultsJson, string Detail);

    public static async Task<Outcome> RunAsync(
        string workspace,
        string deployedUrl,
        CancellationToken cancellationToken)
    {
        var e2e = Path.Combine(workspace, "tests", "e2e");
        Directory.CreateDirectory(e2e);
        var script = Path.Combine(e2e, "deployed-smoke.sh");
        if (!File.Exists(script))
        {
            DeployedSmokeGenerator.WriteSuite(workspace, DeployedSmokeGenerator.DefaultCases(null));
        }

        var httpResultsPath = Path.Combine(Path.GetTempPath(), $"ta-http-{Guid.NewGuid():N}.json");
        var (httpExit, httpOut) = await RunProcessAsync(
                "bash",
                script,
                e2e,
                cancellationToken,
                new Dictionary<string, string>
                {
                    ["DEPLOYED_URL"] = deployedUrl,
                    ["TA_RESULTS_PATH"] = httpResultsPath
                })
            .ConfigureAwait(false);

        var httpJson = File.Exists(httpResultsPath)
            ? await File.ReadAllTextAsync(httpResultsPath, cancellationToken).ConfigureAwait(false)
            : JsonSerializer.Serialize(new { status = "red", detail = Truncate(httpOut, 1500) }, JsonOptions);

        object? httpObj = null;
        try { httpObj = JsonSerializer.Deserialize<object>(httpJson); } catch { httpObj = httpJson; }

        var pwDir = Path.Combine(e2e, "playwright");
        var pwScript = Path.Combine(pwDir, "run-playwright.sh");
        object? pwObj = new { status = "skipped", reason = "suite not generated" };
        var pwExit = 0;
        var pwOut = "";
        if (File.Exists(pwScript))
        {
            var pwResultsPath = Path.Combine(Path.GetTempPath(), $"ta-pw-{Guid.NewGuid():N}.json");
            (pwExit, pwOut) = await RunProcessAsync(
                    "bash",
                    pwScript,
                    pwDir,
                    cancellationToken,
                    new Dictionary<string, string>
                    {
                        ["DEPLOYED_URL"] = deployedUrl,
                        ["PW_RESULTS_PATH"] = pwResultsPath
                    })
                .ConfigureAwait(false);
            if (File.Exists(pwResultsPath))
            {
                var pwJson = await File.ReadAllTextAsync(pwResultsPath, cancellationToken).ConfigureAwait(false);
                try { pwObj = JsonSerializer.Deserialize<object>(pwJson); } catch { pwObj = pwJson; }
                try
                {
                    using var doc = JsonDocument.Parse(pwJson);
                    if (doc.RootElement.TryGetProperty("status", out var st)
                        && string.Equals(st.GetString(), "skipped", StringComparison.OrdinalIgnoreCase))
                    {
                        pwExit = 0; // soft skip
                    }
                }
                catch (JsonException)
                {
                    // keep exit
                }
            }
        }

        var httpGreen = false;
        try
        {
            using var httpDoc = JsonDocument.Parse(httpJson);
            httpGreen = httpExit == 0
                        && httpDoc.RootElement.TryGetProperty("status", out var hs)
                        && string.Equals(hs.GetString(), "green", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            httpGreen = httpExit == 0;
        }
        // Playwright red only fails the run when it actually ran (not skipped).
        var pwGreen = pwExit == 0;
        var green = httpGreen && pwGreen;
        var combined = JsonSerializer.Serialize(
            new
            {
                status = green ? "green" : "red",
                deployedUrl,
                http = httpObj,
                playwright = pwObj,
                results = ExtractHttpResults(httpJson)
            },
            JsonOptions);
        return new Outcome(green, combined, Truncate(httpOut + "\n" + pwOut, 2000));
    }

    private static object ExtractHttpResults(string httpJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(httpJson);
            if (doc.RootElement.TryGetProperty("results", out var r))
            {
                return JsonSerializer.Deserialize<object>(r.GetRawText())!;
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        return Array.Empty<object>();
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(
        string fileName,
        string args,
        string workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? extraEnv = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (extraEnv is not null)
        {
            foreach (var (key, value) in extraEnv)
            {
                psi.Environment[key] = value;
            }
        }

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {fileName}.");
        var stdout = await p.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await p.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (p.ExitCode, stdout + stderr);
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max
            ? value
            : value[..(max - 14)] + "...[truncated]";
}
