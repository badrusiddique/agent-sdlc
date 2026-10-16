using System.Text;
using System.Text.Json;
using AgentHub.Agents.Pipeline.Ia;

namespace AgentHub.Agents.Pipeline.Ta;

/// <summary>
/// Writes Playwright FE e2e under tests/e2e/playwright for the deployed app URL.
/// Specs click the generated FE controls and assert API payloads shown on the page.
/// </summary>
public static class PlaywrightSuiteGenerator
{
    public static void WriteSuite(
        string monoRoot,
        IReadOnlyList<SmokeCase> cases,
        string? userRequest,
        string? stackProfile,
        string? projectName)
    {
        var dir = Path.Combine(monoRoot, "tests", "e2e", "playwright");
        Directory.CreateDirectory(dir);
        var kind = RequestDrivenCodegen.Classify(userRequest, stackProfile, projectName);

        File.WriteAllText(Path.Combine(dir, "package.json"), PackageJson);
        File.WriteAllText(Path.Combine(dir, "playwright.config.js"), PlaywrightConfig);
        File.WriteAllText(Path.Combine(dir, "fe.spec.js"), BuildSpec(kind, cases));
        File.WriteAllText(
            Path.Combine(dir, "cases.json"),
            JsonSerializer.Serialize(new { cases, appKind = kind.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(dir, "run-playwright.sh"), RunnerScript);
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(
                    Path.Combine(dir, "run-playwright.sh"),
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static string BuildSpec(RequestDrivenCodegen.AppKind kind, IReadOnlyList<SmokeCase> cases)
    {
        var primary = cases.FirstOrDefault(c =>
            !string.Equals(c.Name, "HealthOk", StringComparison.OrdinalIgnoreCase)) ?? cases.FirstOrDefault();
        var needle = primary?.ExpectBodyContains ?? "ok";
        var sb = new StringBuilder();
        sb.AppendLine("const { test, expect } = require('@playwright/test');");
        sb.AppendLine("const base = process.env.DEPLOYED_URL;");
        sb.AppendLine("test.describe('FE live e2e', () => {");
        sb.AppendLine("  test.skip(!base, 'DEPLOYED_URL required');");
        sb.AppendLine("  test('health api', async ({ request }) => {");
        sb.AppendLine("    const r = await request.get(base.replace(/\\/$/, '') + '/health');");
        sb.AppendLine("    expect(r.ok()).toBeTruthy();");
        sb.AppendLine("    const body = await r.text();");
        sb.AppendLine("    expect(body.toLowerCase()).toContain('ok');");
        sb.AppendLine("  });");
        sb.AppendLine("  test('frontend primary action', async ({ page }) => {");
        sb.AppendLine("    await page.goto(base.replace(/\\/$/, '') + '/');");
        sb.AppendLine("    await expect(page.locator('h1')).toBeVisible();");
        switch (kind)
        {
            case RequestDrivenCodegen.AppKind.Calculator:
                sb.AppendLine("    await page.fill('#a', '2');");
                sb.AppendLine("    await page.fill('#b', '3');");
                sb.AppendLine("    await page.click('#go');");
                sb.AppendLine("    await expect(page.locator('#out')).toContainText('5', { timeout: 15000 });");
                break;
            case RequestDrivenCodegen.AppKind.Todo:
                sb.AppendLine("    await page.fill('#title', 'qa-item');");
                sb.AppendLine("    await page.click('#add');");
                sb.AppendLine("    await expect(page.locator('#list')).toContainText('qa-item', { timeout: 15000 });");
                break;
            case RequestDrivenCodegen.AppKind.HelloLlm:
                sb.AppendLine("    await page.click('#go');");
                sb.AppendLine("    await expect(page.locator('#out')).toContainText(/thought|ok/i, { timeout: 20000 });");
                break;
            default:
                sb.AppendLine("    await page.click('#go');");
                sb.AppendLine($"    await expect(page.locator('#out')).toContainText({JsonSerializer.Serialize(needle.Length > 40 ? "ok" : needle)}, {{ timeout: 15000 }});");
                break;
        }

        sb.AppendLine("  });");
        sb.AppendLine("});");
        return sb.ToString();
    }

    private const string PackageJson =
        """
        {
          "name": "bgx-ta-playwright",
          "private": true,
          "devDependencies": {
            "@playwright/test": "1.48.2"
          },
          "scripts": {
            "test": "playwright test"
          }
        }
        """;

    private const string PlaywrightConfig =
        """
        // @ts-check
        const { defineConfig } = require('@playwright/test');
        module.exports = defineConfig({
          testDir: '.',
          timeout: 60000,
          retries: 0,
          use: {
            headless: true,
            ignoreHTTPSErrors: true,
            trace: 'off'
          },
          reporter: [['list'], ['json', { outputFile: process.env.PW_JSON_REPORT || 'playwright-report.json' }]]
        });
        """;

    private const string RunnerScript =
        """
        #!/usr/bin/env bash
        set -euo pipefail
        HERE="$(cd "$(dirname "$0")" && pwd)"
        cd "$HERE"
        OUT="${PW_RESULTS_PATH:-/tmp/pw-results.json}"
        export PW_JSON_REPORT="$HERE/playwright-report.json"
        if ! command -v npm >/dev/null 2>&1; then
          echo '{"status":"skipped","reason":"npm missing"}' > "$OUT"
          exit 0
        fi
        npm install --silent --no-fund --no-audit >/tmp/pw-npm.log 2>&1 || {
          echo '{"status":"skipped","reason":"npm install failed"}' > "$OUT"
          exit 0
        }
        npx playwright install chromium >/tmp/pw-browser.log 2>&1 || {
          echo '{"status":"skipped","reason":"playwright browser install failed"}' > "$OUT"
          exit 0
        }
        set +e
        npx playwright test --reporter=list
        code=$?
        set -e
        python3 - "$OUT" "$code" "$PW_JSON_REPORT" <<'PY'
        import json, sys, pathlib
        out, code, report = sys.argv[1], int(sys.argv[2]), sys.argv[3]
        detail = {}
        p = pathlib.Path(report)
        if p.is_file():
            try:
                detail = json.loads(p.read_text())
            except Exception as ex:
                detail = {"parseError": str(ex)}
        status = "green" if code == 0 else "red"
        json.dump({"status": status, "exitCode": code, "playwright": detail}, open(out, "w"), indent=2)
        print("Playwright:", status)
        sys.exit(0 if code == 0 else 1)
        PY
        """;
}
