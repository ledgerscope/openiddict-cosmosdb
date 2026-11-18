/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * Derived from OpenIddict (https://github.com/openiddict/openiddict-core) and modified by Ledgerscope.
 */

using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Ledgerscope.OpenIddict.CosmosDb.Models;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictExceptions;

namespace Ledgerscope.OpenIddict.CosmosDb.Tests;

public class OpenIddictCosmosDbStoreTests
{
    private const string Applications = "openiddict.applications";
    private const string Authorizations = "openiddict.authorizations";
    private const string Scopes = "openiddict.scopes";
    private const string Tokens = "openiddict.tokens";

    [Fact]
    public async Task CreateAsync_PersistsCamelCasedDocumentPartitionedByIdentifier()
    {
        // Arrange
        using var harness = new CosmosDbTestHarness();
        var store = new OpenIddictCosmosDbTokenStore(harness.Context, harness.Options);

        var token = new OpenIddictCosmosDbToken
        {
            ApplicationId = "application",
            CreationDate = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            ReferenceId = "reference",
            Status = Statuses.Valid,
            Subject = "subject",
            Type = TokenTypeIdentifiers.RefreshToken
        };

        // Act
        await store.CreateAsync(token, CancellationToken.None);

        // Assert
        Assert.False(string.IsNullOrEmpty(token.Id));
        Assert.Equal("\"etag-1\"", token.ConcurrencyToken);

        var write = Assert.Single(harness.Writes);
        Assert.Equal(Tokens, write.Container);
        Assert.Equal(new PartitionKey(token.Id), write.PartitionKey);

        using var document = JsonDocument.Parse(write.Body);
        var root = document.RootElement;

        Assert.Equal(token.Id, root.GetProperty("id").GetString());
        Assert.Equal("application", root.GetProperty("applicationId").GetString());
        Assert.Equal("reference", root.GetProperty("referenceId").GetString());
        Assert.Equal("subject", root.GetProperty("subject").GetString());

        // Dates must use a fixed-length format to ensure they can be compared using ordinal string comparisons.
        Assert.Equal("2026-01-02T03:04:05.0000000Z", root.GetProperty("creationDate").GetString());

        // Null values must be omitted.
        Assert.False(root.TryGetProperty("authorizationId", out _));
        Assert.False(root.TryGetProperty("payload", out _));
        Assert.False(root.TryGetProperty("_etag", out _));
    }

    [Fact]
    public async Task UpdateAsync_UsesEntityTagForOptimisticConcurrency()
    {
        // Arrange
        using var harness = new CosmosDbTestHarness();
        var store = new OpenIddictCosmosDbApplicationStore(harness.Context, harness.Options);

        var application = new OpenIddictCosmosDbApplication
        {
            ClientId = "client",
            ConcurrencyToken = "\"etag-1\"",
            Id = "identifier"
        };

        // Act
        await store.UpdateAsync(application, CancellationToken.None);

        // Assert
        var write = Assert.Single(harness.Writes);
        Assert.Equal(Applications, write.Container);
        Assert.Equal("identifier", write.Identifier);
        Assert.Equal(new PartitionKey("identifier"), write.PartitionKey);
        Assert.Equal("\"etag-1\"", write.IfMatch);
        Assert.Equal("\"etag-2\"", application.ConcurrencyToken);
    }

    [Theory]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task UpdateAsync_ThrowsConcurrencyExceptionWhenDocumentWasConcurrentlyModified(HttpStatusCode code)
    {
        // Arrange
        using var harness = new CosmosDbTestHarness { WriteStatusCode = code };
        var store = new OpenIddictCosmosDbScopeStore(harness.Context, harness.Options);

        var scope = new OpenIddictCosmosDbScope { ConcurrencyToken = "\"etag-1\"", Id = "identifier", Name = "scope" };

        // Act and assert
        var exception = await Assert.ThrowsAsync<ConcurrencyException>(async () =>
            await store.UpdateAsync(scope, CancellationToken.None));

        Assert.Equal(SR.GetResourceString(SR.ID0245), exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task DeleteAsync_ThrowsConcurrencyExceptionWhenDocumentWasConcurrentlyModified(HttpStatusCode code)
    {
        // Arrange
        using var harness = new CosmosDbTestHarness { WriteStatusCode = code };
        var store = new OpenIddictCosmosDbTokenStore(harness.Context, harness.Options);

        var token = new OpenIddictCosmosDbToken { ConcurrencyToken = "\"etag-1\"", Id = "identifier" };

        // Act and assert
        await Assert.ThrowsAsync<ConcurrencyException>(async () =>
            await store.DeleteAsync(token, CancellationToken.None));

        var delete = Assert.Single(harness.Deletes);
        Assert.Equal("\"etag-1\"", delete.IfMatch);
    }

    [Fact]
    public async Task DeleteAsync_RemovesEntitiesAssociatedWithApplication()
    {
        // Arrange
        using var harness = new CosmosDbTestHarness();
        var store = new OpenIddictCosmosDbApplicationStore(harness.Context, harness.Options);

        harness.EnqueueResults(Authorizations, "\"authorization\"");
        harness.EnqueueResults(Tokens, "\"token-1\"", "\"token-2\"");

        var application = new OpenIddictCosmosDbApplication { ConcurrencyToken = "\"etag-1\"", Id = "application" };

        // Act
        await store.DeleteAsync(application, CancellationToken.None);

        // Assert
        Assert.Equal(
        [
            (Applications, "application", "\"etag-1\""),
            (Authorizations, "authorization", null),
            (Tokens, "token-1", null),
            (Tokens, "token-2", null)
        ], harness.Deletes.OrderBy(static delete => delete.Container, StringComparer.Ordinal).ThenBy(static delete => delete.Identifier, StringComparer.Ordinal));

        Assert.All(harness.Queries, static query =>
        {
            Assert.Equal("SELECT VALUE c.id FROM c WHERE c.applicationId = @identifier", query.Query.QueryText);
            Assert.Equal(("@identifier", (object) "application"), Assert.Single(query.Query.GetQueryParameters()));
        });
    }

    [Fact]
    public async Task FindByIdAsync_UsesPointReadAndDeserializesDocument()
    {
        // Arrange
        using var harness = new CosmosDbTestHarness();
        var store = new OpenIddictCosmosDbTokenStore(harness.Context, harness.Options);

        harness.Documents["identifier"] = """
            {
              "id": "identifier",
              "_etag": "\"etag-1\"",
              "_ts": 1700000000,
              "authorizationId": "authorization",
              "expirationDate": "2026-01-02T03:04:05.1234567Z",
              "properties": { "claim": [1, 2] },
              "status": "valid"
            }
            """;

        // Act
        var token = await store.FindByIdAsync("identifier", CancellationToken.None);

        // Assert
        Assert.NotNull(token);
        Assert.Empty(harness.Queries);
        Assert.Equal("\"etag-1\"", token.ConcurrencyToken);
        Assert.Equal("authorization", await store.GetAuthorizationIdAsync(token, CancellationToken.None));
        Assert.Equal(Statuses.Valid, await store.GetStatusAsync(token, CancellationToken.None));
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(1234567),
            await store.GetExpirationDateAsync(token, CancellationToken.None));

        var properties = await store.GetPropertiesAsync(token, CancellationToken.None);
        Assert.Equal("[1,2]", properties["claim"].GetRawText().Replace(" ", string.Empty));
    }

    [Fact]
    public async Task FindByIdAsync_ReturnsNullWhenDocumentCannotBeFound()
    {
        // Arrange
        using var harness = new CosmosDbTestHarness();
        var store = new OpenIddictCosmosDbAuthorizationStore(harness.Context, harness.Options);

        // Act and assert
        Assert.Null(await store.FindByIdAsync("unknown", CancellationToken.None));
    }

    [Fact]
    public async Task FindByReferenceIdAsync_UsesParameterizedQuery()
    {
        // Arrange
        using var harness = new CosmosDbTestHarness();
        var store = new OpenIddictCosmosDbTokenStore(harness.Context, harness.Options);

        harness.EnqueueResults(Tokens, """{ "id": "identifier", "referenceId": "reference" }""");

        // Act
        var token = await store.FindByReferenceIdAsync("reference", CancellationToken.None);

        // Assert
        Assert.Equal("identifier", token?.Id);

        var query = Assert.Single(harness.Queries);
        Assert.Equal("SELECT TOP 1 * FROM c WHERE c.referenceId = @identifier", query.Query.QueryText);
        Assert.Equal(("@identifier", (object) "reference"), Assert.Single(query.Query.GetQueryParameters()));
    }

    [Fact]
    public async Task FindAsync_FiltersAuthorizationsOnRequiredScopes()
    {
        // Arrange
        using var harness = new CosmosDbTestHarness();
        var store = new OpenIddictCosmosDbAuthorizationStore(harness.Context, harness.Options);

        // Act
        await foreach (var _ in store.FindAsync("subject", "client", Statuses.Valid, null, ["openid", "profile"], CancellationToken.None))
        {
        }

        // Assert
        var query = Assert.Single(harness.Queries);
        Assert.Equal(
            "SELECT * FROM c WHERE c.subject = @subject AND c.applicationId = @client AND c.status = @status AND " +
            "ARRAY_CONTAINS(c.scopes, @scope0) AND ARRAY_CONTAINS(c.scopes, @scope1)", query.Query.QueryText);
    }

    [Fact]
    public async Task FindByNamesAsync_UsesArrayParameter()
    {
        // Arrange
        using var harness = new CosmosDbTestHarness();
        var store = new OpenIddictCosmosDbScopeStore(harness.Context, harness.Options);

        harness.EnqueueResults(Scopes, """{ "id": "1", "name": "openid" }""", """{ "id": "2", "name": "profile" }""");

        // Act
        var scopes = new List<OpenIddictCosmosDbScope>();
        await foreach (var scope in store.FindByNamesAsync(["openid", "profile"], CancellationToken.None))
        {
            scopes.Add(scope);
        }

        // Assert
        Assert.Equal(["openid", "profile"], scopes.Select(static scope => scope.Name), StringComparer.Ordinal);

        var query = Assert.Single(harness.Queries);
        Assert.Equal("SELECT * FROM c WHERE ARRAY_CONTAINS(@names, c.name)", query.Query.QueryText);
    }

    [Fact]
    public async Task GetAsync_TranslatesLinqQueriesUsingPersistedPropertyNames()
    {
        // Arrange
        using var harness = new CosmosDbTestHarness();
        var store = new OpenIddictCosmosDbTokenStore(harness.Context, harness.Options);

        harness.EnqueueResults(Tokens, "\"identifier\"");

        // Act
        var result = await store.GetAsync(
            static (tokens, state) => tokens.Where(token => token.ReferenceId == state && token.AuthorizationId != null)
                                            .Select(token => token.Id),
            "reference", CancellationToken.None);

        // Assert
        Assert.Equal("identifier", result);

        var query = Assert.Single(harness.Queries);
        Assert.Contains("root[\"referenceId\"]", query.Query.QueryText, StringComparison.Ordinal);
        Assert.Contains("root[\"authorizationId\"]", query.Query.QueryText, StringComparison.Ordinal);
        Assert.Contains("root[\"id\"]", query.Query.QueryText, StringComparison.Ordinal);
        Assert.Contains("TOP 1", query.Query.QueryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevokeBySubjectAsync_PatchesStatusOfMatchingDocuments()
    {
        // Arrange
        using var harness = new CosmosDbTestHarness();
        var store = new OpenIddictCosmosDbTokenStore(harness.Context, harness.Options);

        harness.EnqueueResults(Tokens, "\"token-1\"", "\"token-2\"");

        // Act
        var count = await store.RevokeBySubjectAsync("subject", CancellationToken.None);

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(["token-1", "token-2"], harness.Patches.Select(static patch => patch.Identifier).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.All(harness.Patches, static patch =>
        {
            var operation = Assert.Single(patch.Operations);
            Assert.Equal(PatchOperationType.Set, operation.OperationType);
            Assert.Equal("/status", operation.Path);
        });
    }

    [Fact]
    public async Task PruneAsync_RemovesTokensAttachedToInvalidAuthorizations()
    {
        // Arrange
        using var harness = new CosmosDbTestHarness();
        var store = new OpenIddictCosmosDbTokenStore(harness.Context, harness.Options);

        harness.EnqueueResults(Tokens, "\"expired\"");
        harness.EnqueueResults(Authorizations, "\"revoked-authorization\"");
        harness.EnqueueResults(Tokens, "\"orphaned\"", "\"expired\"");

        // Act
        var count = await store.PruneAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(["expired", "orphaned"], harness.Deletes.Select(static delete => delete.Identifier).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.All(harness.Deletes, static delete => Assert.Equal(Tokens, delete.Container));
    }

    [Fact]
    public async Task PruneAsync_DoesNotRemoveAuthorizationsThatStillHaveTokens()
    {
        // Arrange
        using var harness = new CosmosDbTestHarness();
        var store = new OpenIddictCosmosDbAuthorizationStore(harness.Context, harness.Options);

        harness.EnqueueResults(Authorizations, "\"with-tokens\"", "\"without-tokens\"");
        harness.EnqueueResults(Tokens, "\"with-tokens\"");

        // Act
        var count = await store.PruneAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        // Assert
        Assert.Equal(1, count);
        Assert.Equal("without-tokens", Assert.Single(harness.Deletes).Identifier);
    }

    [Fact]
    public async Task SetPropertiesAsync_RoundTripsProperties()
    {
        // Arrange
        using var harness = new CosmosDbTestHarness();
        var store = new OpenIddictCosmosDbTokenStore(harness.Context, harness.Options);
        var token = new OpenIddictCosmosDbToken();

        using var document = JsonDocument.Parse("""{ "number": 42, "array": ["a", "b"], "object": { "nested": true } }""");

        var properties = ImmutableDictionary.CreateRange(StringComparer.Ordinal,
            document.RootElement.EnumerateObject().Select(static property =>
                new KeyValuePair<string, JsonElement>(property.Name, property.Value.Clone())));

        // Act
        await store.SetPropertiesAsync(token, properties, CancellationToken.None);
        var result = await store.GetPropertiesAsync(token, CancellationToken.None);

        // Assert
        Assert.Equal(42, result["number"].GetInt32());
        Assert.Equal(["a", "b"], result["array"].EnumerateArray().Select(static element => element.GetString()), StringComparer.Ordinal);
        Assert.True(result["object"].GetProperty("nested").GetBoolean());

        await store.SetPropertiesAsync(token, [], CancellationToken.None);
        Assert.Null(token.Properties);
    }
}
