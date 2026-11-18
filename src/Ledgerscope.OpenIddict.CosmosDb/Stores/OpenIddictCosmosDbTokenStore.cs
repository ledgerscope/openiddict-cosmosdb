/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * Derived from OpenIddict (https://github.com/openiddict/openiddict-core) and modified by Ledgerscope.
 */

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Ledgerscope.OpenIddict.CosmosDb.Models;
using static Ledgerscope.OpenIddict.CosmosDb.OpenIddictCosmosDbHelpers;

namespace Ledgerscope.OpenIddict.CosmosDb;

/// <summary>
/// Provides methods allowing to manage the tokens stored in a database.
/// </summary>
public class OpenIddictCosmosDbTokenStore : OpenIddictCosmosDbTokenStore<OpenIddictCosmosDbToken>
{
    /// <summary>
    /// Creates a new instance of the <see cref="OpenIddictCosmosDbTokenStore"/> class.
    /// </summary>
    public OpenIddictCosmosDbTokenStore(
        IOpenIddictCosmosDbContext context,
        IOptionsMonitor<OpenIddictCosmosDbOptions> options)
        : base(context, options)
    {
    }
}

/// <summary>
/// Provides methods allowing to manage the tokens stored in a database.
/// </summary>
/// <typeparam name="TToken">The type of the token entity.</typeparam>
public class OpenIddictCosmosDbTokenStore<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TToken> : IOpenIddictTokenStore<TToken>
    where TToken : OpenIddictCosmosDbToken
{
    /// <summary>
    /// Creates a new instance of the <see cref="OpenIddictCosmosDbTokenStore"/> class.
    /// </summary>
    public OpenIddictCosmosDbTokenStore(
        IOpenIddictCosmosDbContext context,
        IOptionsMonitor<OpenIddictCosmosDbOptions> options)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Gets the database context associated with the current store.
    /// </summary>
    protected IOpenIddictCosmosDbContext Context { get; }

    /// <summary>
    /// Gets the options associated with the current store.
    /// </summary>
    protected IOptionsMonitor<OpenIddictCosmosDbOptions> Options { get; }

    /// <summary>
    /// Gets the container storing the tokens.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>The container storing the tokens.</returns>
    protected virtual async ValueTask<Container> GetContainerAsync(CancellationToken cancellationToken)
    {
        var database = await Context.GetDatabaseAsync(cancellationToken);
        return database.GetContainer(Options.CurrentValue.TokensContainerName);
    }

    /// <inheritdoc/>
    public virtual async ValueTask<long> CountAsync(CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);

        return await container.FirstOrDefaultAsync<long>(
            new QueryDefinition("SELECT VALUE COUNT(1) FROM c"), cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<TToken>, IQueryable<TResult>> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var container = await GetContainerAsync(cancellationToken);

        return (await query(container.GetQueryable<TToken>()).CountAsync(cancellationToken)).Resource;
    }

    /// <inheritdoc/>
    public virtual async ValueTask CreateAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (string.IsNullOrEmpty(token.Id))
        {
            token.Id = Guid.NewGuid().ToString();
        }

        var container = await GetContainerAsync(cancellationToken);

        token.ConcurrencyToken = await container.CreateDocumentAsync(token, token.Id, cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async ValueTask DeleteAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        var identifier = EnsureIdentifier(token.Id, nameof(token));
        var container = await GetContainerAsync(cancellationToken);

        await container.DeleteDocumentAsync(identifier, token.ConcurrencyToken, SR.GetResourceString(SR.ID0247), cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async IAsyncEnumerable<TToken> FindAsync(
        string? subject, string? client, string? status, string? type,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);

        var (filters, parameters) = CreateFilters(subject, client, status, type);

        await foreach (var token in container.QueryAsync<TToken>(
            CreateQuery("SELECT * FROM c", filters, parameters), cancellationToken))
        {
            yield return token;
        }
    }

    /// <inheritdoc/>
    public virtual IAsyncEnumerable<TToken> FindByApplicationIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        return ExecuteAsync(cancellationToken);

        async IAsyncEnumerable<TToken> ExecuteAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var container = await GetContainerAsync(cancellationToken);

            await foreach (var token in container.QueryAsync<TToken>(CreateQuery(
                "SELECT * FROM c WHERE c.applicationId = @identifier", ("@identifier", identifier)), cancellationToken))
            {
                yield return token;
            }
        }
    }

    /// <inheritdoc/>
    public virtual IAsyncEnumerable<TToken> FindByAuthorizationIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        return ExecuteAsync(cancellationToken);

        async IAsyncEnumerable<TToken> ExecuteAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var container = await GetContainerAsync(cancellationToken);

            await foreach (var token in container.QueryAsync<TToken>(CreateQuery(
                "SELECT * FROM c WHERE c.authorizationId = @identifier", ("@identifier", identifier)), cancellationToken))
            {
                yield return token;
            }
        }
    }

    /// <inheritdoc/>
    public virtual async ValueTask<TToken?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        var container = await GetContainerAsync(cancellationToken);

        return await container.ReadDocumentAsync<TToken>(identifier, cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async ValueTask<TToken?> FindByReferenceIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        var container = await GetContainerAsync(cancellationToken);

        return await container.FirstOrDefaultAsync<TToken>(CreateQuery(
            "SELECT TOP 1 * FROM c WHERE c.referenceId = @identifier", ("@identifier", identifier)), cancellationToken);
    }

    /// <inheritdoc/>
    public virtual IAsyncEnumerable<TToken> FindBySubjectAsync(string subject, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);

        return ExecuteAsync(cancellationToken);

        async IAsyncEnumerable<TToken> ExecuteAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var container = await GetContainerAsync(cancellationToken);

            await foreach (var token in container.QueryAsync<TToken>(CreateQuery(
                "SELECT * FROM c WHERE c.subject = @subject", ("@subject", subject)), cancellationToken))
            {
                yield return token;
            }
        }
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetApplicationIdAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        return new(token.ApplicationId);
    }

    /// <inheritdoc/>
    public virtual async ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<TToken>, TState, IQueryable<TResult>> query,
        TState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var container = await GetContainerAsync(cancellationToken);

        await foreach (var result in container.QueryAsync(
            query(container.GetQueryable<TToken>(), state).Take(1), cancellationToken))
        {
            return result;
        }

        return default;
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetAuthorizationIdAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        return new(token.AuthorizationId);
    }

    /// <inheritdoc/>
    public virtual ValueTask<DateTimeOffset?> GetCreationDateAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        return new(token.CreationDate is DateTime date ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)) : null);
    }

    /// <inheritdoc/>
    public virtual ValueTask<DateTimeOffset?> GetExpirationDateAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        return new(token.ExpirationDate is DateTime date ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)) : null);
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetIdAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        return new(token.Id);
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetPayloadAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        return new(token.Payload);
    }

    /// <inheritdoc/>
    public virtual ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        return new(GetProperties(token.Properties));
    }

    /// <inheritdoc/>
    public virtual ValueTask<DateTimeOffset?> GetRedemptionDateAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        return new(token.RedemptionDate is DateTime date ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)) : null);
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetReferenceIdAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        return new(token.ReferenceId);
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetStatusAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        return new(token.Status);
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetSubjectAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        return new(token.Subject);
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetTypeAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        return new(token.Type);
    }

    /// <inheritdoc/>
    public virtual ValueTask<TToken> InstantiateAsync(CancellationToken cancellationToken)
    {
        try
        {
            return new(Activator.CreateInstance<TToken>());
        }

        catch (MemberAccessException exception)
        {
            return new(Task.FromException<TToken>(
                new InvalidOperationException(SR.GetResourceString(SR.ID0248), exception)));
        }
    }

    /// <inheritdoc/>
    public virtual async IAsyncEnumerable<TToken> ListAsync(
        int? count, int? offset, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);

        await foreach (var token in container.QueryAsync<TToken>(CreatePagedQuery(count, offset), cancellationToken))
        {
            yield return token;
        }
    }

    /// <inheritdoc/>
    public virtual IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<TToken>, TState, IQueryable<TResult>> query,
        TState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return ExecuteAsync(cancellationToken);

        async IAsyncEnumerable<TResult> ExecuteAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var container = await GetContainerAsync(cancellationToken);

            await foreach (var element in container.QueryAsync(
                query(container.GetQueryable<TToken>(), state), cancellationToken))
            {
                yield return element;
            }
        }
    }

    /// <inheritdoc/>
    public virtual async ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);
        var database = await Context.GetDatabaseAsync(cancellationToken);

        var date = FormatDate(threshold.UtcDateTime);
        var identifiers = new HashSet<string>(StringComparer.Ordinal);

        // Note: Azure Cosmos DB doesn't support joins between documents stored in different containers
        // nor deleting the documents returned by a query. To work around these limitations, the identifiers
        // of the tokens to prune are resolved first using separate queries and then removed individually.

        // Resolve the tokens that are no longer valid or that are expired.
        await foreach (var identifier in container.QueryAsync<string>(CreateQuery(
            """
            SELECT VALUE c.id FROM c
            WHERE c.creationDate < @threshold AND
                ((c.status != @inactive AND c.status != @valid) OR c.expirationDate < @now)
            """,
            ("@threshold", date),
            ("@inactive", Statuses.Inactive),
            ("@valid", Statuses.Valid),
            ("@now", FormatDate(DateTime.UtcNow))), cancellationToken))
        {
            identifiers.Add(identifier);

            if (identifiers.Count >= MaximumPrunedDocuments)
            {
                break;
            }
        }

        // Resolve the tokens attached to authorizations that are no longer valid.
        if (identifiers.Count < MaximumPrunedDocuments)
        {
            var authorizations = new List<string>();

            await foreach (var identifier in database.GetContainer(Options.CurrentValue.AuthorizationsContainerName)
                .QueryAsync<string>(CreateQuery("SELECT VALUE c.id FROM c WHERE c.status != @valid",
                    ("@valid", Statuses.Valid)), cancellationToken))
            {
                authorizations.Add(identifier);
            }

            foreach (var chunk in Chunk(authorizations, MaximumChunkSize))
            {
                await foreach (var identifier in container.QueryAsync<string>(CreateQuery(
                    "SELECT VALUE c.id FROM c WHERE c.creationDate < @threshold AND ARRAY_CONTAINS(@identifiers, c.authorizationId)",
                    ("@threshold", date),
                    ("@identifiers", chunk)), cancellationToken))
                {
                    identifiers.Add(identifier);
                }

                if (identifiers.Count >= MaximumPrunedDocuments)
                {
                    break;
                }
            }
        }

        return await container.DeleteDocumentsAsync([.. identifiers.Take(MaximumPrunedDocuments)], cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async ValueTask<long> RevokeAsync(string? subject, string? client, string? status, string? type, CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);

        var (filters, parameters) = CreateFilters(subject, client, status, type);

        return await container.UpdateStatusAsync(CreateQuery("SELECT VALUE c.id FROM c", filters, parameters),
            Statuses.Revoked, cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async ValueTask<long> RevokeByApplicationIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        var container = await GetContainerAsync(cancellationToken);

        return await container.UpdateStatusAsync(CreateQuery("SELECT VALUE c.id FROM c WHERE c.applicationId = @identifier",
            ("@identifier", identifier)), Statuses.Revoked, cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async ValueTask<long> RevokeByAuthorizationIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        var container = await GetContainerAsync(cancellationToken);

        return await container.UpdateStatusAsync(CreateQuery("SELECT VALUE c.id FROM c WHERE c.authorizationId = @identifier",
            ("@identifier", identifier)), Statuses.Revoked, cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async ValueTask<long> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);

        var container = await GetContainerAsync(cancellationToken);

        return await container.UpdateStatusAsync(CreateQuery("SELECT VALUE c.id FROM c WHERE c.subject = @subject",
            ("@subject", subject)), Statuses.Revoked, cancellationToken);
    }

    /// <inheritdoc/>
    public virtual ValueTask SetApplicationIdAsync(TToken token, string? identifier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.ApplicationId = !string.IsNullOrEmpty(identifier) ? identifier : null;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetAuthorizationIdAsync(TToken token, string? identifier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.AuthorizationId = !string.IsNullOrEmpty(identifier) ? identifier : null;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetCreationDateAsync(TToken token, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.CreationDate = date?.UtcDateTime;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetExpirationDateAsync(TToken token, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.ExpirationDate = date?.UtcDateTime;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetPayloadAsync(TToken token, string? payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.Payload = payload;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetPropertiesAsync(TToken token,
        ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.Properties = CreateProperties(properties);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetRedemptionDateAsync(TToken token, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.RedemptionDate = date?.UtcDateTime;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetReferenceIdAsync(TToken token, string? identifier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.ReferenceId = identifier;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetStatusAsync(TToken token, string? status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.Status = status;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetSubjectAsync(TToken token, string? subject, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.Subject = subject;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetTypeAsync(TToken token, string? type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.Type = type;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual async ValueTask UpdateAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        var identifier = EnsureIdentifier(token.Id, nameof(token));
        var container = await GetContainerAsync(cancellationToken);

        token.ConcurrencyToken = await container.ReplaceDocumentAsync(
            token, identifier, token.ConcurrencyToken, SR.GetResourceString(SR.ID0247), cancellationToken);
    }

    private static (List<string> Filters, List<(string Name, object? Value)> Parameters) CreateFilters(
        string? subject, string? client, string? status, string? type)
    {
        var filters = new List<string>();
        var parameters = new List<(string Name, object? Value)>();

        if (!string.IsNullOrEmpty(subject))
        {
            filters.Add("c.subject = @subject");
            parameters.Add(("@subject", subject));
        }

        if (!string.IsNullOrEmpty(client))
        {
            filters.Add("c.applicationId = @client");
            parameters.Add(("@client", client));
        }

        if (!string.IsNullOrEmpty(status))
        {
            filters.Add("c.status = @status");
            parameters.Add(("@status", status));
        }

        if (!string.IsNullOrEmpty(type))
        {
            filters.Add("c.type = @type");
            parameters.Add(("@type", type));
        }

        return (filters, parameters);
    }
}
