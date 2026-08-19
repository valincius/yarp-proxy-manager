using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ProxyManager.Application.ProxyHosts;
using Xunit;
using Yarp.ReverseProxy.Configuration;

namespace ProxyManager.Tests;

public sealed class ApiIntegrationTests
{
    private const string BaseUrl = "http://localhost";

    [Fact]
    public async Task Healthz_IsAnonymous()
    {
        using var factory = new ProxyApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Session_WithoutLogin_ReturnsUnauthorized()
    {
        using var factory = new ProxyApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/auth/session");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        using var factory = new ProxyApiFactory();
        using var client = factory.CreateClient();
        var xsrf = await TestApi.GetXsrfTokenAsync(client);
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", xsrf);

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = "admin@example.com", password = "wrong-password" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithValidCredentials_CreatesSession()
    {
        using var factory = new ProxyApiFactory();
        var client = await TestApi.LoginAsync(factory, BaseUrl);

        var response = await client.GetAsync("/api/v1/auth/session");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SessionResponse>();
        body!.Authenticated.Should().BeTrue();
        body.Email.Should().Be("admin@example.com");
        body.Roles.Should().Contain("Admin");
    }

    [Fact]
    public async Task Mutations_WithoutAntiforgeryToken_AreRejected()
    {
        using var factory = new ProxyApiFactory();
        var client = await TestApi.LoginAsync(factory, BaseUrl);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

        var response = await client.PostAsJsonAsync("/api/v1/hosts", TestApi.ValidHostInput());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Hosts_InvalidInput_Returns400()
    {
        using var factory = new ProxyApiFactory();
        var client = await TestApi.LoginAsync(factory, BaseUrl);

        var invalid = TestApi.ValidHostInput() with { DomainNames = ["not a domain"] };
        var response = await client.PostAsJsonAsync("/api/v1/hosts", invalid);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem!.Errors.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Hosts_DuplicateDomain_Returns409()
    {
        using var factory = new ProxyApiFactory();
        var client = await TestApi.LoginAsync(factory, BaseUrl);

        (await client.PostAsJsonAsync("/api/v1/hosts", TestApi.ValidHostInput("app.example.com")))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var second = await client.PostAsJsonAsync("/api/v1/hosts", TestApi.ValidHostInput("app.example.com"));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Hosts_WildcardOverlap_Returns409()
    {
        using var factory = new ProxyApiFactory();
        var client = await TestApi.LoginAsync(factory, BaseUrl);

        (await client.PostAsJsonAsync("/api/v1/hosts", TestApi.ValidHostInput("*.example.com")))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var second = await client.PostAsJsonAsync("/api/v1/hosts", TestApi.ValidHostInput("app.example.com"));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Hosts_FullLifecycle()
    {
        using var factory = new ProxyApiFactory();
        var client = await TestApi.LoginAsync(factory, BaseUrl);

        // Create
        var create = await client.PostAsJsonAsync("/api/v1/hosts", TestApi.ValidHostInput());
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<ProxyHostResponse>();
        created!.Id.Should().NotBeEmpty();

        // Get
        var get = await client.GetAsync($"/api/v1/hosts/{created.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        // List
        var list = await client.GetFromJsonAsync<ProxyHostResponse[]>("/api/v1/hosts");
        list!.Should().ContainSingle(h => h.Id == created.Id);

        // Update
        var update = await client.PutAsJsonAsync(
            $"/api/v1/hosts/{created.Id}",
            TestApi.ValidHostInput() with { ForwardPort = 9999, Name = "Renamed" });
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await update.Content.ReadFromJsonAsync<ProxyHostResponse>();
        updated!.ForwardPort.Should().Be(9999);
        updated.Name.Should().Be("Renamed");

        // Disable
        var disable = await client.PatchAsJsonAsync($"/api/v1/hosts/{created.Id}/enable", new { enabled = false });
        disable.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Delete
        var delete = await client.DeleteAsync($"/api/v1/hosts/{created.Id}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Gone
        var afterDelete = await client.GetAsync($"/api/v1/hosts/{created.Id}");
        afterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreatedHost_IsProjectedIntoYarpConfig()
    {
        using var factory = new ProxyApiFactory();
        var client = await TestApi.LoginAsync(factory, BaseUrl);

        var create = await client.PostAsJsonAsync(
            "/api/v1/hosts",
            TestApi.ValidHostInput("projected.example.com", "192.168.1.50"));
        var created = await create.Content.ReadFromJsonAsync<ProxyHostResponse>();

        var provider = factory.Services.GetRequiredService<IProxyConfigProvider>();
        IProxyConfig config;
        IReadOnlyList<RouteConfig> routes;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        do
        {
            config = provider.GetConfig();
            routes = config.Routes.ToList();
            if (routes.Any(r => r.RouteId == $"host-{created!.Id:N}"))
            {
                break;
            }

            await Task.Delay(50);
        }
        while (DateTime.UtcNow < deadline);

        var route = routes.Should().ContainSingle(r => r.RouteId == $"host-{created!.Id:N}").Subject;
        route.Match.Hosts.Should().Equal("projected.example.com");
        var cluster = config.Clusters.Single(c => c.ClusterId == route.ClusterId);
        cluster.Destinations.Should().ContainKey("d0");
        cluster.Destinations["d0"]!.Address.Should().Be("http://192.168.1.50:8080/");
    }

    private sealed record SessionResponse(bool Authenticated, string? Email, string? DisplayName, string[] Roles);

    private sealed record ProxyHostResponse(Guid Id, string Name, string[] DomainNames, bool Enabled,
        string Scheme, string ForwardHost, int ForwardPort, bool WebSocketsEnabled, bool BlockCommonExploits,
        bool ForceHttps, bool Http2Support, Guid? CertificateId, Guid? AccessListId,
        ProxyHeaderResponse[] RequestHeaders, ProxyHeaderResponse[] ResponseHeaders, ProxyLocationResponse[] Locations);

    private sealed record ProxyHeaderResponse(string Target, string Action, string Name, string Value);

    private sealed record ProxyLocationResponse(string PathPrefix, bool StripPrefix, string Scheme,
        string ForwardHost, int ForwardPort, int Order);

    private sealed record ProblemDetailsResponse(string Title, string[]? Errors);
}
