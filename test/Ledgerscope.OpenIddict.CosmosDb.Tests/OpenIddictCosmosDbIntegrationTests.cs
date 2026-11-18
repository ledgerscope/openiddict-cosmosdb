/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * Derived from OpenIddict (https://github.com/openiddict/openiddict-core) and modified by Ledgerscope.
 */

using System.Net.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Moq;
using Ledgerscope.OpenIddict.CosmosDb.Models;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictExceptions;

namespace Ledgerscope.OpenIddict.CosmosDb.Tests;

/// <summary>
/// Exercises the stores against a real Azure Cosmos DB account or emulator. These tests are skipped
/// unless the OPENIDDICT_COSMOSDB_CONNECTION_STRING environment variable is set. A temporary
/// database is created for each test and removed once the test completes.
/// </summary>
public sealed class OpenIddictCosmosDbIntegrationTests : IAsyncLifetime
{
    private const string ConnectionStringVariable = "OPENIDDICT_COSMOSDB_CONNECTION_STRING";

    private CosmosClient? _client;
    private Database? _database;
    private IOptionsMonitor<OpenIddictCosmosDbOptions>? _options;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrEmpty(connectionString))
        {
            return;
        }

        var options = new CosmosClientOptions();

        // Note: the emulator uses a self-signed certificate that is not trusted by default.
        // Since the emulator is only reachable locally, certificate validation is disabled.
        if (connectionString.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("127.0.0.1", StringComparison.Ordinal))
        {
            options.ConnectionMode = ConnectionMode.Gateway;
            options.LimitToEndpoint = true;
#pragma warning disable MA0039
            options.HttpClientFactory = static () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
#pragma warning restore MA0039
        }

        _client = new CosmosClient(connectionString, options);
        _database = (await _client.CreateDatabaseAsync("openiddict-tests-" + Guid.NewGuid().ToString("n"))).Database;

        var settings = new OpenIddictCosmosDbOptions { Database = _database };

        foreach (var name in new[]
        {
            settings.ApplicationsContainerName, settings.AuthorizationsContainerName,
            settings.ScopesContainerName, settings.TokensContainerName
        })
        {
            await _database.CreateContainerAsync(name, partitionKeyPath: "/id");
        }

        _options = Mock.Of<IOptionsMonitor<OpenIddictCosmosDbOptions>>(mock => mock.CurrentValue == settings);
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DeleteAsync();
        }

        _client?.Dispose();
    }

    [SkippableFact]
    public async Task TokenStore_SupportsCompleteLifecycle()
    {
        Skip.If(_database is null, $"The {ConnectionStringVariable} environment variable is not set.");

        // Arrange
        var store = new OpenIddictCosmosDbTokenStore(CreateContext(), _options!);
        var authorizations = new OpenIddictCosmosDbAuthorizationStore(CreateContext(), _options!);

        var authorization = new OpenIddictCosmosDbAuthorization { Status = Statuses.Revoked, Subject = "bob" };
        await authorizations.CreateAsync(authorization, CancellationToken.None);

        var token = new OpenIddictCosmosDbToken
        {
            AuthorizationId = authorization.Id,
            CreationDate = DateTime.UtcNow.AddDays(-2),
            ExpirationDate = DateTime.UtcNow.AddDays(1),
            ReferenceId = "reference",
            Status = Statuses.Valid,
            Subject = "bob",
            Type = TokenTypeIdentifiers.RefreshToken
        };

        // Act and assert (create and read)
        await store.CreateAsync(token, CancellationToken.None);
        Assert.Equal(1, await store.CountAsync(CancellationToken.None));
        Assert.Equal(token.Id, (await store.FindByIdAsync(token.Id!, CancellationToken.None))?.Id);
        Assert.Equal(token.Id, (await store.FindByReferenceIdAsync("reference", CancellationToken.None))?.Id);
        Assert.Equal(token.Id, await store.GetAsync(static (tokens, subject) =>
            tokens.Where(token => token.Subject == subject).Select(token => token.Id), "bob", CancellationToken.None));
        Assert.Equal(1, await store.CountAsync(static tokens =>
            tokens.Where(token => token.Subject == "bob"), CancellationToken.None));

        // Act and assert (optimistic concurrency)
        var copy = await store.FindByIdAsync(token.Id!, CancellationToken.None);
        token.Status = Statuses.Redeemed;
        await store.UpdateAsync(token, CancellationToken.None);

        copy!.Status = Statuses.Revoked;
        await Assert.ThrowsAsync<ConcurrencyException>(async () => await store.UpdateAsync(copy, CancellationToken.None));

        // Act and assert (revocation)
        Assert.Equal(1, await store.RevokeBySubjectAsync("bob", CancellationToken.None));
        Assert.Equal(Statuses.Revoked, (await store.FindByIdAsync(token.Id!, CancellationToken.None))?.Status);

        // Act and assert (pruning, which removes the token because its authorization was revoked)
        Assert.Equal(1, await store.PruneAsync(DateTimeOffset.UtcNow.AddDays(-1), CancellationToken.None));
        Assert.Null(await store.FindByIdAsync(token.Id!, CancellationToken.None));
    }

    [SkippableFact]
    public async Task ApplicationStore_DeleteAsync_RemovesAssociatedEntities()
    {
        Skip.If(_database is null, $"The {ConnectionStringVariable} environment variable is not set.");

        // Arrange
        var applications = new OpenIddictCosmosDbApplicationStore(CreateContext(), _options!);
        var authorizations = new OpenIddictCosmosDbAuthorizationStore(CreateContext(), _options!);
        var tokens = new OpenIddictCosmosDbTokenStore(CreateContext(), _options!);

        var application = new OpenIddictCosmosDbApplication { ClientId = "client", RedirectUris = ["https://localhost/cb"] };
        await applications.CreateAsync(application, CancellationToken.None);

        var authorization = new OpenIddictCosmosDbAuthorization { ApplicationId = application.Id, Scopes = ["openid"] };
        await authorizations.CreateAsync(authorization, CancellationToken.None);

        var token = new OpenIddictCosmosDbToken { ApplicationId = application.Id, AuthorizationId = authorization.Id };
        await tokens.CreateAsync(token, CancellationToken.None);

        Assert.Equal(application.Id, (await applications.FindByClientIdAsync("client", CancellationToken.None))?.Id);

        var found = new List<string?>();
        await foreach (var element in applications.FindByRedirectUriAsync("https://localhost/cb", CancellationToken.None))
        {
            found.Add(element.Id);
        }

        Assert.Equal([application.Id], found);

        // Act
        await applications.DeleteAsync(application, CancellationToken.None);

        // Assert
        Assert.Null(await applications.FindByIdAsync(application.Id!, CancellationToken.None));
        Assert.Null(await authorizations.FindByIdAsync(authorization.Id!, CancellationToken.None));
        Assert.Null(await tokens.FindByIdAsync(token.Id!, CancellationToken.None));
    }

    private IOpenIddictCosmosDbContext CreateContext() => Mock.Of<IOpenIddictCosmosDbContext>(mock =>
        mock.GetDatabaseAsync(It.IsAny<CancellationToken>()) == new ValueTask<Database>(_database!));
}
