using System.Net;
using System.Text.Json.Nodes;
using Azure.Data.Tables;
using Ledgerscope.OpenIddict.MigrateAts;
using Microsoft.Azure.Cosmos;
using Moq;
using Xunit;

namespace Ledgerscope.OpenIddict.CosmosDb.Tests;

public class MigrateAtsTests
{
    [Fact]
    public void ConvertsIdsLinksDatesAndJson()
    {
        var application = Migration.ConvertRow("applications", new TableEntity("app", "other")
        {
            ["Type"] = "confidential",
            ["RedirectUris"] = """["https://example.test/"]""",
            ["ClientSecret"] = "hash"
        }, 1);
        Assert.Equal("app", application["id"]!.GetValue<string>());
        Assert.Equal("confidential", application["clientType"]!.GetValue<string>());
        Assert.Equal("hash", application["clientSecret"]!.GetValue<string>());
        Assert.Equal("https://example.test/", application["redirectUris"]![0]!.GetValue<string>());
        Assert.Null(application["rowKey"]);

        var token = Migration.ConvertRow("tokens", new TableEntity("tok", "x")
        {
            ["ApplicationId"] = "app",
            ["AuthorizationId"] = "auth",
            ["ExpirationDate"] = "2026-01-02T03:04:05.1234567Z",
            ["Properties"] = """{"nested":[true,1]}"""
        }, 1);
        Assert.Equal("auth", token["authorizationId"]!.GetValue<string>());
        Assert.Equal("2026-01-02T03:04:05.1234567Z", token["expirationDate"]!.GetValue<string>());
        Assert.True(token["properties"]!["nested"]![0]!.GetValue<bool>());
    }

    [Fact]
    public void RejectsUnsupportedSourceWithoutLeakingValues()
    {
        var rows = new[]
        {
            new TableEntity("bad/id", "r"),
            new TableEntity("app", "r") { ["CustomSecret"] = "secret" },
            new TableEntity("app", "r") { ["RedirectUris"] = "[42]" },
            new TableEntity("app", "r") { ["RedirectUris"] = 42 }
        };
        foreach (var row in rows)
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Migration.ConvertRow("applications", row, 1));
            Assert.DoesNotContain("secret", ex.Message);
        }
    }

    [Fact]
    public void ParsesEndpoints()
    {
        Assert.Equal("auth", Program.ParseCosmosEndpoint("https://target.documents.azure.com/dbs/auth").Database);
        Assert.Equal("https://target.documents.azure.com/", Program.ParseCosmosEndpoint("https://target.documents.azure.com/dbs/auth/").Account.AbsoluteUri);
        Program.ParseTableEndpoint("https://source.table.core.windows.net");
        Assert.Throws<ArgumentException>(() => Program.ParseTableEndpoint("http://source.table.core.windows.net"));
        Assert.Throws<ArgumentException>(() => Program.ParseTableEndpoint("https://evil.example/"));
        Assert.Throws<ArgumentException>(() => Program.ParseCosmosEndpoint("https://target.documents.azure.com/"));
    }

    [Theory]
    [InlineData("applications", "openiddictapplications")]
    [InlineData("scopes", "openiddictscopes")]
    [InlineData("authorizations", "openiddictauthorizations")]
    [InlineData("tokens", "openiddicttokens")]
    public void UsesDotlessContainerNames(string kind, string expected)
    {
        Assert.Equal(expected, Migration.ContainerName(kind));
    }

    [Fact]
    public async Task DryRunWritesNothingAndApplyIsRepeatable()
    {
        var source = new FakeSource();
        var store = new FakeStore();
        var output = new StringWriter();
        var migration = new Migration(source, store, output);

        await migration.RunAsync(false, default);
        Assert.Empty(store.Documents);
        Assert.Contains("Dry run complete", output.ToString());

        await migration.RunAsync(true, default);
        Assert.Equal(4, store.Documents.Count);
        await migration.RunAsync(true, default);
        Assert.Equal(4, store.Documents.Count);

        store.Documents["app"]["clientType"] = "public";
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => migration.RunAsync(true, default));
        Assert.Contains("differs", ex.Message);
    }

    [Fact]
    public async Task StopsWhenSourceChangesBetweenPasses()
    {
        var source = new FakeSource { DropTokenAfterFirstRead = true };
        var migration = new Migration(source, new FakeStore(), TextWriter.Null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => migration.RunAsync(true, default));
        Assert.Contains("source changed", ex.Message);
    }

    [Fact]
    public async Task CosmosStoreAcceptsIdenticalAndRejectsDifferentConflicts()
    {
        var document = new JsonObject { ["id"] = "app", ["clientType"] = "confidential" };
        var stored = new JsonObject { ["id"] = "app", ["clientType"] = "confidential", ["_etag"] = "x", ["_ts"] = 1 };

        var response = new Mock<ItemResponse<JsonObject>>();
        response.SetupGet(r => r.Resource).Returns(() => stored.DeepClone().AsObject());
        var container = new Mock<Container>();
        container.Setup(c => c.CreateItemAsync(It.IsAny<JsonObject>(), It.IsAny<PartitionKey?>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("conflict", HttpStatusCode.Conflict, 0, "", 0));
        container.Setup(c => c.ReadItemAsync<JsonObject>("app", new PartitionKey("app"), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
        var database = new Mock<Database>();
        database.Setup(d => d.GetContainer("openiddictapplications")).Returns(container.Object);
        var store = new CosmosDocumentStore(database.Object);

        Assert.False(await store.CreateAsync("applications", document, default));
        stored["clientType"] = "public";
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync("applications", document, default));
        container.Verify(c => c.UpsertItemAsync(It.IsAny<JsonObject>(), It.IsAny<PartitionKey?>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class FakeSource : IRowSource
    {
        private int reads;
        public bool DropTokenAfterFirstRead { get; init; }

        public async IAsyncEnumerable<TableEntity> ReadAsync(string kind, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (kind == "tokens" && DropTokenAfterFirstRead && reads++ > 0) yield break;
            yield return kind switch
            {
                "applications" => new TableEntity("app", "r") { ["Type"] = "confidential" },
                "scopes" => new TableEntity("scope", "r") { ["Name"] = "test" },
                "authorizations" => new TableEntity("auth", "r") { ["ApplicationId"] = "app" },
                _ => new TableEntity("tok", "r") { ["ApplicationId"] = "app", ["AuthorizationId"] = "auth" }
            };
        }
    }

    private sealed class FakeStore : IDocumentStore
    {
        public Dictionary<string, JsonObject> Documents { get; } = new();

        public Task ValidateContainerAsync(string kind, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> CreateAsync(string kind, JsonObject document, CancellationToken cancellationToken)
        {
            var id = document["id"]!.GetValue<string>();
            if (Documents.TryAdd(id, document)) return Task.FromResult(true);
            return JsonNode.DeepEquals(Documents[id], document)
                ? Task.FromResult(false)
                : throw new InvalidOperationException($"{kind}: destination document differs from the source.");
        }
    }
}
