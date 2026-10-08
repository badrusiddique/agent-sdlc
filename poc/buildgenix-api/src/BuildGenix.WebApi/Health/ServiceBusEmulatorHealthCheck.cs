using BuildGenix.WebApi.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace BuildGenix.WebApi.Health;

/// <summary>
/// Local emulator readiness via the Service Bus emulator HTTP health endpoint.
/// AspNetCore.HealthChecks.AzureServiceBus topic checks are unreliable against UseDevelopmentEmulator.
/// </summary>
public sealed class ServiceBusEmulatorHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ServiceBusOptions _options;

    public ServiceBusEmulatorHealthCheck(
        IHttpClientFactory httpClientFactory,
        IOptions<ServiceBusOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(_options.EmulatorHealthUrl)
            ? "http://127.0.0.1:5300/health"
            : _options.EmulatorHealthUrl.Trim();

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(ServiceBusEmulatorHealthCheck));
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode
                && body.Contains("healthy", StringComparison.OrdinalIgnoreCase))
            {
                return HealthCheckResult.Healthy($"Service Bus emulator health OK ({url}).");
            }

            return HealthCheckResult.Unhealthy(
                $"Service Bus emulator health unexpected: HTTP {(int)response.StatusCode} body={Truncate(body)}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Service Bus emulator health unreachable at {url}.", ex);
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 200 ? value : value[..200];
}
