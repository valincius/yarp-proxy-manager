using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace ProxyManager.Tests;

public sealed class DiagnosticsIntegrationTests
{
    private const string BaseUrl = "http://localhost";

    private sealed record OverviewResponse(
        DateTimeOffset StartedAt,
        long TotalRequests,
        long TotalFailed,
        int TrackedHosts,
        int BufferedSamples,
        bool CaptureEnabled,
        int CaptureSize,
        string? TraceEndpoint,
        int Routes,
        int Clusters,
        int ProxyHosts,
        IReadOnlyList<object> Streams,
        CertificatesResponse Certificates);

    private sealed record CertificatesResponse(int Total, int Failed, int ExpiringSoon);

    private sealed record ApiKeyCreateResponse(KeyResponse Key, string Plaintext);

    private sealed record KeyResponse(Guid Id, string Name, string Prefix);

    [Fact]
    public async Task Overview_AfterLogin_ReportsSystemCounters()
    {
        using var factory = new ProxyApiFactory();
        var client = await TestApi.LoginAsync(factory, BaseUrl);

        var create = await client.PostAsJsonAsync("/api/v1/hosts", TestApi.ValidHostInput("diag.example.com"));
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        OverviewResponse? body = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await client.GetAsync("/api/v1/diagnostics/overview");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body = await response.Content.ReadFromJsonAsync<OverviewResponse>();
            if (body!.Routes > 0)
            {
                break;
            }

            await Task.Delay(100);
        }

        body!.Routes.Should().Be(1);
        body.Clusters.Should().Be(1);
        body.ProxyHosts.Should().Be(1);
        body.TotalRequests.Should().Be(0);
        body.TotalFailed.Should().Be(0);
        body.TrackedHosts.Should().Be(0);
        body.CaptureEnabled.Should().BeFalse();
        body.TraceEndpoint.Should().BeNull();
        body.Certificates.Total.Should().Be(0);
    }

    [Fact]
    public async Task Diagnostics_Endpoints_RequireAuthentication()
    {
        using var factory = new ProxyApiFactory();
        using var anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/api/v1/diagnostics/overview")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/diagnostics/traffic")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/diagnostics/requests")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Traffic_And_Overview_AllowApiKeys_But_Requests_RequiresAdmin()
    {
        using var factory = new ProxyApiFactory();
        var admin = await TestApi.LoginAsync(factory, BaseUrl);

        // Create an API key (admin), then use it for the diagnostics endpoints.
        var createKey = await admin.PostAsJsonAsync("/api/v1/api-keys", new { name = "diag-ci" });
        createKey.StatusCode.Should().Be(HttpStatusCode.Created);
        var key = (await createKey.Content.ReadFromJsonAsync<ApiKeyCreateResponse>())!;

        using var apiClient = factory.CreateClient();
        apiClient.DefaultRequestHeaders.Add("X-Api-Key", key.Plaintext);

        (await apiClient.GetAsync("/api/v1/diagnostics/traffic")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await apiClient.GetAsync("/api/v1/diagnostics/overview")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await apiClient.GetAsync("/api/v1/diagnostics/requests")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
