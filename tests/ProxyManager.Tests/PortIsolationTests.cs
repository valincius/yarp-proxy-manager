using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProxyManager.Infrastructure.Persistence;
using Xunit;

namespace ProxyManager.Tests;

/// <summary>
/// Boots the real production pipeline (<see cref="Program.BuildApp"/>) on Kestrel with
/// separate admin and proxy endpoints and verifies strict port-based separation.
/// </summary>
public sealed class PortIsolationTests
{
    private const int AdminPort = 51991;
    private const int ProxyPort = 51992;
    private const int HttpsPort = 51993;

    [Fact]
    public async Task AdminAndProxyPipelines_AreSeparatedByPort()
    {
        await using var upstream = await TestUpstream.StartAsync();
        var connectionString = $"Data Source=file:pm-port-{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keeper = new SqliteConnection(connectionString);
        keeper.Open();

        var app = Program.BuildApp(["--environment", "Development"], builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kestrel:Endpoints:ProxyHttp:Url"] = $"http://127.0.0.1:{ProxyPort}",
                ["Kestrel:Endpoints:Https:Url"] = $"https://127.0.0.1:{HttpsPort}",
                ["Kestrel:Endpoints:Admin:Url"] = $"http://127.0.0.1:{AdminPort}",
            });
            // In-process hosting from the test assembly: the app's entry assembly is the
            // test host, so controller discovery needs the app's assembly explicitly.
            builder.Services.AddControllers()
                .AddApplicationPart(typeof(Program).Assembly);
            var descriptor = builder.Services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ProxyDbContext>));
            if (descriptor is not null)
            {
                builder.Services.Remove(descriptor);
            }

            builder.Services.AddDbContext<ProxyDbContext>(o => o.UseSqlite(connectionString));
        });
        await Program.InitializeAsync(app);
        await app.StartAsync();

        var adminBase = $"http://127.0.0.1:{AdminPort}";
        var proxyBase = $"http://127.0.0.1:{ProxyPort}";

        try
        {
            // Anonymous liveness probe reachable on the admin port.
            using (var probe = new HttpClient())
            {
                (await probe.GetAsync(adminBase + "/healthz")).StatusCode.Should().Be(HttpStatusCode.OK);
            }

            // Create a proxy host pointing at the upstream.
            var upstreamUri = new Uri(upstream.Address);
            var admin = await TestApi.LoginAsync(adminBase);
            var input = TestApi.ValidHostInput("app.test", upstreamUri.Host) with { ForwardPort = upstreamUri.Port };
            var create = await admin.PostAsJsonAsync("/api/v1/hosts", input);
            create.StatusCode.Should().Be(HttpStatusCode.Created);

            // Poll the proxy port until the config reload lands and traffic flows.
            using var proxyClient = new HttpClient();
            HttpResponseMessage? last = null;
            for (var i = 0; i < 50; i++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, proxyBase + "/");
                request.Headers.Host = "app.test";
                last = await proxyClient.SendAsync(request);
                if (last.StatusCode == HttpStatusCode.OK)
                {
                    break;
                }

                await Task.Delay(100);
            }

            last!.StatusCode.Should().Be(HttpStatusCode.OK);
            (await last.Content.ReadAsStringAsync()).Should().Be("upstream-ok");

            // The proxy port must not expose the admin API.
            (await proxyClient.GetAsync(proxyBase + "/api/v1/health")).StatusCode.Should().Be(HttpStatusCode.NotFound);

            // The admin port must not proxy upstream traffic.
            using (var request = new HttpRequestMessage(HttpMethod.Get, adminBase + "/"))
            {
                request.Headers.Host = "app.test";
                var adminHome = await admin.SendAsync(request);
                (await adminHome.Content.ReadAsStringAsync()).Should().NotContain("upstream-ok");
            }
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private sealed class TestUpstream : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private TestUpstream(WebApplication app, string address)
        {
            _app = app;
            Address = address;
        }

        public string Address { get; }

        public static async Task<TestUpstream> StartAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "yarp-upstream-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = [],
                ContentRootPath = root,
            });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            app.MapGet("/", () => Results.Text("upstream-ok"));
            await app.StartAsync();
            return new TestUpstream(app, GetServerAddress(app));
        }

        public async ValueTask DisposeAsync() => await _app.StopAsync();

        private static string GetServerAddress(WebApplication app)
        {
            var server = app.Services.GetRequiredService<IServer>();
            var feature = server.Features.Get<IServerAddressesFeature>();
            Assert.NotNull(feature);
            return Assert.Single(feature.Addresses);
        }
    }
}
