using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProxyManager.Application.ProxyHosts;
using ProxyManager.Infrastructure.Persistence;

namespace ProxyManager.Tests;

/// <summary>WebApplicationFactory with an in-memory SQLite database (TestServer).</summary>
public sealed class ProxyApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public ProxyApiFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ProxyDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<ProxyDbContext>(o => o.UseSqlite(_connection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}

public static class TestApi
{
    private sealed record AntiforgeryResponse(string token);

    public static async Task<string> GetXsrfTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/auth/antiforgery");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AntiforgeryResponse>();
        return body!.token;
    }

    /// <summary>Logs in as the seeded admin and returns a client with the antiforgery header set.
    /// The antiforgery token is re-fetched after login because tokens are bound to the user identity.</summary>
    public static async Task<HttpClient> LoginAsync(HttpClient client)
    {
        var xsrf = await GetXsrfTokenAsync(client);
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", xsrf);
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "admin@example.com", password = "admin" });
        response.EnsureSuccessStatusCode();

        var rotated = await GetXsrfTokenAsync(client);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", rotated);
        return client;
    }

    public static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> factory, string baseAddress)
    {
        var client = factory.CreateClient();
        client.BaseAddress = new Uri(baseAddress);
        return await LoginAsync(client);
    }

    public static async Task<HttpClient> LoginAsync(string baseAddress)
    {
        var client = new HttpClient { BaseAddress = new Uri(baseAddress) };
        return await LoginAsync(client);
    }

    public static ProxyHostInput ValidHostInput(string domain = "app.example.com", string forwardHost = "127.0.0.1") =>
        new("Test host", [domain], true, "http", forwardHost, 8080,
            true, true, false, true, null, null, [], [], []);
}
