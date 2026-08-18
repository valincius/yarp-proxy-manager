using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using ProxyManager.Infrastructure.Dns;
using Xunit;

namespace ProxyManager.Tests;

public sealed class CloudflareDnsChallengeProviderTests
{
    [Fact]
    public async Task AddTxtRecord_FindsZoneAndCreatesRecord()
    {
        var handler = new FakeCloudflareHandler();
        handler.OnGetZones = (name) => name == "example.com"
            ? """{"success":true,"result":[{"id":"zone-1","name":"example.com"}]}"""
            : """{"success":true,"result":[]}""";
        handler.OnPostDnsRecord = """{"success":true,"result":{"id":"rec-1"}}""";
        var provider = new CloudflareDnsChallengeProvider(new FakeHttpClientFactory(handler.Client), "api-token");

        await provider.AddTxtRecordAsync("*.example.com", "_acme-challenge.example.com", "txt-value", CancellationToken.None);

        handler.Requests.Should().Contain(r => r.Method == HttpMethod.Get && r.Url.Contains("/zones?name=example.com"));
        handler.Requests.Should().Contain(r =>
            r.Method == HttpMethod.Post && r.Url.Contains("/zones/zone-1/dns_records"));
        var post = handler.Requests.Last(r => r.Method == HttpMethod.Post);
        post.Body.Should().Contain("\"type\":\"TXT\"");
        post.Body.Should().Contain("\"name\":\"_acme-challenge.example.com\"");
        post.Body.Should().Contain("\"content\":\"txt-value\"");
        handler.AuthorizationHeader.Should().Be("Bearer api-token");
    }

    [Fact]
    public async Task RemoveTxtRecord_LooksUpAndDeletes()
    {
        var handler = new FakeCloudflareHandler();
        handler.OnGetZones = (name) => """{"success":true,"result":[{"id":"zone-1","name":"example.com"}]}""";
        handler.OnGetDnsRecords = """{"success":true,"result":[{"id":"rec-9","type":"TXT"}]}""";
        handler.OnDeleteDnsRecord = """{"success":true,"result":{"id":"rec-9"}}""";
        var provider = new CloudflareDnsChallengeProvider(new FakeHttpClientFactory(handler.Client), "api-token");

        await provider.RemoveTxtRecordAsync("example.com", "_acme-challenge.example.com", "txt-value", CancellationToken.None);

        handler.Requests.Should().Contain(r => r.Method == HttpMethod.Get && r.Url.Contains("/dns_records?type=TXT"));
        handler.Requests.Should().Contain(r =>
            r.Method == HttpMethod.Delete && r.Url.EndsWith("/zones/zone-1/dns_records/rec-9"));
    }

    [Fact]
    public async Task AddTxtRecord_NoZoneFound_Throws()
    {
        var handler = new FakeCloudflareHandler();
        handler.OnGetZones = (_) => """{"success":true,"result":[]}""";
        var provider = new CloudflareDnsChallengeProvider(new FakeHttpClientFactory(handler.Client), "api-token");

        var act = async () => await provider.AddTxtRecordAsync("unknown.example.com", "_acme-challenge.unknown.example.com", "v", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FakeCloudflareHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? Body)> Requests { get; } = [];
        public Func<string, string> OnGetZones { get; set; } = (_) => """{"success":true,"result":[]}""";
        public string OnPostDnsRecord { get; set; } = """{"success":true,"result":{"id":"rec-1"}}""";
        public string OnGetDnsRecords { get; set; } = """{"success":true,"result":[]}""";
        public string OnDeleteDnsRecord { get; set; } = """{"success":true,"result":{"id":"rec-1"}}""";

        public string? AuthorizationHeader { get; private set; }

        public HttpClient Client { get; }

        public FakeCloudflareHandler()
        {
            Client = new HttpClient(this);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.ToString(), body));
            AuthorizationHeader = request.Headers.Authorization?.ToString();

            var path = request.RequestUri!.AbsolutePath;
            var response = request.Method switch
            {
                _ when request.Method == HttpMethod.Get && path.Contains("/zones") && !path.Contains("/dns_records") =>
                    OnGetZones(ParseQuery(request.RequestUri, "name")),
                _ when request.Method == HttpMethod.Post && path.Contains("/dns_records") => OnPostDnsRecord,
                _ when request.Method == HttpMethod.Get && path.Contains("/dns_records") => OnGetDnsRecords,
                _ when request.Method == HttpMethod.Delete => OnDeleteDnsRecord,
                _ => """{"success":false,"errors":[{"message":"unexpected"}]}""",
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json"),
            };
        }

        private static string ParseQuery(Uri uri, string key)
        {
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            return query[key] ?? string.Empty;
        }
    }
}
