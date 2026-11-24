using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ledgerscope.OpenIddict.MigrateAts;
using Xunit;

namespace Ledgerscope.OpenIddict.CosmosDb.Tests;

public class MigrateAtsTests
{
    [Fact]
    public async Task CliUsesIsolatedIdentitySessionAndCleansItUp()
    {
        if (OperatingSystem.IsWindows()) return;
        var directory = Directory.CreateTempSubdirectory("migration-cli-test-");
        try
        {
            var executable = Path.Combine(directory.FullName, "az");
            var marker = Path.Combine(directory.FullName, "config-path");
            await File.WriteAllTextAsync(executable, $$"""
                #!/bin/sh
                if [ "$1" = "login" ]; then
                  [ "$2" = "--identity" ] || exit 1
                  printf '%s' "$AZURE_CONFIG_DIR" > '{{marker}}'
                else
                  printf '{"accessToken":"fake","expires_on":4102444800}'
                fi
                """);
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            string config;
            using (var identity = new AzureCliIdentity(executable))
            {
                await identity.LoginAsync();
                config = await File.ReadAllTextAsync(marker);
                Assert.True(Directory.Exists(config));
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(config));
                Assert.Equal("fake", await identity.GetTokenAsync("https://storage.azure.com/"));
            }
            Assert.False(Directory.Exists(config));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void ConvertsIdsLinksDatesAndJson()
    {
        var application = Migration.ConvertRow("applications", JsonNode.Parse(
            """{"PartitionKey":"app","RowKey":"other","Type":"confidential","RedirectUris":"[\"https://example.test/\"]","ClientSecret":"hash"}""")!.AsObject(), 1);
        Assert.Equal("app", application["id"]!.GetValue<string>());
        Assert.Equal("confidential", application["clientType"]!.GetValue<string>());
        Assert.Equal("hash", application["clientSecret"]!.GetValue<string>());
        Assert.Equal("https://example.test/", application["redirectUris"]![0]!.GetValue<string>());
        Assert.Null(application["RowKey"]);

        var token = Migration.ConvertRow("tokens", JsonNode.Parse(
            """{"PartitionKey":"tok","ApplicationId":"app","AuthorizationId":"auth","ExpirationDate":"2026-01-02T03:04:05.1234567Z","Properties":"{\"nested\":[true,1]}"}""")!.AsObject(), 1);
        Assert.Equal("auth", token["authorizationId"]!.GetValue<string>());
        Assert.Equal("2026-01-02T03:04:05.1234567Z", token["expirationDate"]!.GetValue<string>());
        Assert.True(token["properties"]!["nested"]![0]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("""{"PartitionKey":"bad/id"}""")]
    [InlineData("""{"PartitionKey":"app","CustomSecret":"secret"}""")]
    [InlineData("""{"PartitionKey":"app","RedirectUris":"[42]"}""")]
    public void RejectsUnsupportedSourceWithoutLeakingValues(string input)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Migration.ConvertRow("applications", JsonNode.Parse(input)!.AsObject(), 1));
        Assert.DoesNotContain("secret", ex.Message);
    }

    [Fact]
    public async Task DryRunApplyRetryAndConflict()
    {
        var handler = new MockService();
        using var http = new HttpClient(handler);
        var tokens = new List<string>();
        var migration = new Migration(http, resource => { tokens.Add(resource); return Task.FromResult("fake"); },
            new Uri("https://source.table.core.windows.net/"), new Uri("https://target.documents.azure.com/dbs/auth"));
        await migration.RunAsync(false);
        Assert.Empty(handler.Documents);
        await migration.RunAsync(true);
        Assert.Equal(4, handler.Documents.Count);
        await migration.RunAsync(true);
        Assert.Equal(4, handler.Documents.Count);
        handler.Documents["app"]["clientType"] = "public";
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => migration.RunAsync(true));
        Assert.Contains("destination document differs", ex.Message);
        Assert.Contains("https://storage.azure.com/", tokens);
        Assert.Contains("https://cosmos.azure.com/", tokens);
        Assert.True(handler.SawContinuation);
    }

    private sealed class MockService : HttpMessageHandler
    {
        public Dictionary<string, JsonObject> Documents { get; } = new();
        public bool SawContinuation { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host.EndsWith(".table.core.windows.net", StringComparison.Ordinal))
            {
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                if (uri.AbsolutePath == "/OpenIddictApplications()" && uri.Query.Length == 0)
                {
                    var first = Response(HttpStatusCode.OK, """{"value":[]}""");
                    first.Headers.TryAddWithoutValidation("x-ms-continuation-NextPartitionKey", "a+==");
                    return Task.FromResult(first);
                }
                if (uri.AbsolutePath == "/OpenIddictApplications()")
                {
                    Assert.Equal("?NextPartitionKey=a%2B%3D%3D", uri.Query);
                    SawContinuation = true;
                }
                var row = uri.AbsolutePath switch
                {
                    "/OpenIddictApplications()" => """{"PartitionKey":"app","Type":"confidential"}""",
                    "/OpenIddictScopes()" => """{"PartitionKey":"scope","Name":"test"}""",
                    "/OpenIddictAuthorizations()" => """{"PartitionKey":"auth","ApplicationId":"app"}""",
                    "/OpenIddictTokens()" => """{"PartitionKey":"tok","ApplicationId":"app","AuthorizationId":"auth"}""",
                    _ => throw new InvalidOperationException("Unexpected table request.")
                };
                return Task.FromResult(Response(HttpStatusCode.OK, $$"""{"value":[{{row}}]}"""));
            }
            Assert.Equal("type%3Daad%26ver%3D1.0%26sig%3Dfake", request.Headers.GetValues("Authorization").Single());
            if (request.Method == HttpMethod.Get && !uri.AbsolutePath.Contains("/docs", StringComparison.Ordinal))
                return Task.FromResult(Response(HttpStatusCode.OK, """{"partitionKey":{"paths":["/id"]}}"""));
            if (request.Method == HttpMethod.Post)
            {
                var doc = JsonNode.Parse(request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult())!.AsObject();
                var id = doc["id"]!.GetValue<string>();
                Assert.Equal($"[\"{id}\"]", request.Headers.GetValues("x-ms-documentdb-partitionkey").Single());
                if (!Documents.TryAdd(id, doc)) return Task.FromResult(Response(HttpStatusCode.Conflict, ""));
                return Task.FromResult(Response(HttpStatusCode.Created, ""));
            }
            var key = Uri.UnescapeDataString(uri.Segments[^1]);
            var existing = Documents[key].DeepClone().AsObject();
            existing["_etag"] = "test";
            return Task.FromResult(Response(HttpStatusCode.OK, existing.ToJsonString()));
        }

        private static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
