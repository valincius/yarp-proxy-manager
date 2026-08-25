using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProxyManager.Application.ProxyHosts;
using ProxyManager.Application.Redirects;
using ProxyManager.Infrastructure.Persistence;
using Xunit;

namespace ProxyManager.Tests;

/// <summary>
/// Real-pipeline verification of the protection features: redirect hosts, access lists, exploit
/// blocking, the audit log, and user management.
/// </summary>
public sealed class ProtectionIntegrationTests
{
    private const int AdminPort = 51997;
    private const int ProxyPort = 51998;
    private const int HttpsPort = 51999;

    [Fact]
    public async Task Redirects_AccessLists_ExploitBlocking_Audit_And_Users()
    {
        var connectionString = $"Data Source=file:pm-protect-{Guid.NewGuid():N}?mode=memory&cache=shared";
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
            builder.Services.AddControllers().AddApplicationPart(typeof(Program).Assembly);
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
            var admin = await TestApi.LoginAsync(adminBase);

            // --- Redirect host ---
            var redirectInput = new RedirectHostInput(
                "Old site", ["old.test"], true, 301, true, "http", "127.0.0.1", AdminPort, null);
            var createRedirect = await admin.PostAsJsonAsync("/api/v1/redirects", redirectInput);
            createRedirect.StatusCode.Should().Be(HttpStatusCode.Created);

            // Conflict: redirect domains must not overlap proxy host domains.
            var conflict = await admin.PostAsJsonAsync("/api/v1/redirects",
                redirectInput with { DomainNames = ["old.test"] });
            conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);

            // --- Access list ---
            var accessList = await admin.PostAsJsonAsync("/api/v1/access-lists", new AccessListInput(
                "Block localhost", true,
                [new AccessListRuleInput("Deny", "127.0.0.1")]));
            accessList.StatusCode.Should().Be(HttpStatusCode.Created);
            var list = await accessList.Content.ReadFromJsonAsync<AccessListDto>();
            list!.Rules.Should().ContainSingle(r => r.Action == "Deny" && r.Pattern == "127.0.0.1");

            // --- Hosts with protection ---
            var lockedHost = await admin.PostAsJsonAsync("/api/v1/hosts",
                TestApi.ValidHostInput("locked.test", "127.0.0.1") with { ForwardPort = AdminPort, AccessListId = list.Id });
            lockedHost.StatusCode.Should().Be(HttpStatusCode.Created);

            var exploitHost = await admin.PostAsJsonAsync("/api/v1/hosts",
                TestApi.ValidHostInput("exploit.test", "127.0.0.1") with { ForwardPort = AdminPort, BlockCommonExploits = true });
            exploitHost.StatusCode.Should().Be(HttpStatusCode.Created);

            var openHost = await admin.PostAsJsonAsync("/api/v1/hosts",
                TestApi.ValidHostInput("open.test", "127.0.0.1") with { ForwardPort = AdminPort, BlockCommonExploits = false });
            openHost.StatusCode.Should().Be(HttpStatusCode.Created);

            // Wait until the config reload has picked up the three new hosts.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                var health = await admin.GetFromJsonAsync<HealthDto>("/api/v1/health");
                if (health!.Routes >= 3)
                {
                    break;
                }

                await Task.Delay(100);
            }

            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });

            // Retry helper: YARP applies a signaled config a beat after the health endpoint
            // reports it, so behavior checks poll the proxy itself.
            async Task<HttpResponseMessage> Probe(string host, string pathAndQuery)
            {
                var deadline = DateTime.UtcNow.AddSeconds(5);
                HttpResponseMessage? last = null;
                while (DateTime.UtcNow < deadline)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, proxyBase + pathAndQuery);
                    request.Headers.Host = host;
                    last = await client.SendAsync(request);
                    if (last.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.ServiceUnavailable))
                    {
                        return last;
                    }

                    await Task.Delay(100);
                }

                return last!;
            }

            // Redirect fires for old.test with the path preserved.
            var redirectResponse = await Probe("old.test", "/healthz");
            redirectResponse.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
            redirectResponse.Headers.Location!.ToString().Should().Be($"http://127.0.0.1:{AdminPort}/healthz");

            // Access list blocks the loopback client.
            var lockedResponse = await Probe("locked.test", "/healthz");
            lockedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            // Exploit blocking rejects a traversal attempt (query string is sent raw) but allows normal traffic.
            var exploitResponse = await Probe("exploit.test", "/healthz?p=../../etc/passwd");
            exploitResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var normalResponse = await Probe("exploit.test", "/healthz");
            normalResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            // A host without exploit blocking forwards the suspicious request to the upstream
            // (the upstream — the admin server — has no such route and returns 404, but the
            // point is that the proxy did NOT block it).
            var openResponse = await Probe("open.test", "/healthz?p=../../etc/passwd");
            openResponse.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);

            // --- Audit log records the changes ---
            var audit = await admin.GetFromJsonAsync<AuditLogDto[]>("/api/v1/audit?limit=50");
            audit!.Select(a => a.EntityType).Should().Contain(new[]
            {
                "ProxyHost", "RedirectHost", "AccessList",
            });

            // --- User management ---
            var createUser = await admin.PostAsJsonAsync("/api/v1/users", new
            {
                email = "user2@example.com",
                password = "password123",
                role = "User",
            });
            createUser.StatusCode.Should().Be(HttpStatusCode.OK);

            var users = await admin.GetFromJsonAsync<UserDto[]>("/api/v1/users");
            users!.Should().Contain(u => u.Email == "user2@example.com");

            // A non-admin user cannot manage users.
            var userClient = new HttpClient { BaseAddress = new Uri(adminBase) };
            var xsrf = await TestApi.GetXsrfTokenAsync(userClient);
            userClient.DefaultRequestHeaders.Add("X-XSRF-TOKEN", xsrf);
            var userSession = await userClient.PostAsJsonAsync("/api/v1/auth/login",
                new { email = "user2@example.com", password = "password123" });
            userSession.StatusCode.Should().Be(HttpStatusCode.OK);
            var denied = await userClient.GetAsync("/api/v1/users");
            denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private sealed record HealthDto(int Routes, int Clusters);

    private sealed record AccessListDto(Guid Id, string Name, bool SatisfyAny, AccessListRuleDto[] Rules);

    private sealed record AccessListRuleDto(Guid Id, Guid AccessListId, string Action, string Pattern);

    private sealed record AuditLogDto(Guid Id, DateTimeOffset Timestamp, Guid? UserId, string EntityType, Guid? EntityId, string Action, string Details);

    private sealed record UserDto(Guid Id, string Email, string DisplayName, string[] Roles);
}
