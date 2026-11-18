/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * Derived from OpenIddict (https://github.com/openiddict/openiddict-core) and modified by Ledgerscope.
 */

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Ledgerscope.OpenIddict.CosmosDb.Models;
using static Ledgerscope.OpenIddict.CosmosDb.OpenIddictCosmosDbHelpers;

namespace Ledgerscope.OpenIddict.CosmosDb;

/// <summary>
/// Provides methods allowing to manage the authorizations stored in a database.
/// </summary>
public class OpenIddictCosmosDbAuthorizationStore : OpenIddictCosmosDbAuthorizationStore<OpenIddictCosmosDbAuthorization>
{
    /// <summary>
    /// Creates a new instance of the <see cref="OpenIddictCosmosDbAuthorizationStore"/> class.
    /// </summary>
    public OpenIddictCosmosDbAuthorizationStore(
        IOpenIddictCosmosDbContext context,
        IOptionsMonitor<OpenIddictCosmosDbOptions> options)
        : base(context, options)
    {
    }
}

/// <summary>
/// Provides methods allowing to manage the authorizations stored in a database.
/// </summary>
/// <typeparam name="TAuthorization">The type of the authorization entity.</typeparam>
public class OpenIddictCosmosDbAuthorizationStore<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAuthorization> : IOpenIddictAuthorizationStore<TAuthorization>
    where TAuthorization : OpenIddictCosmosDbAuthorization
{
    /// <summary>
    /// Creates a new instance of the <see cref="OpenIddictCosmosDbAuthorizationStore"/> class.
    /// </summary>
    public OpenIddictCosmosDbAuthorizationStore(
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
    /// Gets the container storing the authorizations.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>The container storing the authorizations.</returns>
    protected virtual async ValueTask<Container> GetContainerAsync(CancellationToken cancellationToken)
    {
        var database = await Context.GetDatabaseAsync(cancellationToken);
        return database.GetContainer(Options.CurrentValue.AuthorizationsContainerName);
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
        Func<IQueryable<TAuthorization>, IQueryable<TResult>> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var container = await GetContainerAsync(cancellationToken);

        return (await query(container.GetQueryable<TAuthorization>()).CountAsync(cancellationToken)).Resource;
    }

    /// <inheritdoc/>
    public virtual async ValueTask CreateAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        if (string.IsNullOrEmpty(authorization.Id))
        {
            authorization.Id = Guid.NewGuid().ToString();
        }

        var container = await GetContainerAsync(cancellationToken);

        authorization.ConcurrencyToken = await container.CreateDocumentAsync(authorization, authorization.Id, cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async ValueTask DeleteAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        var identifier = EnsureIdentifier(authorization.Id, nameof(authorization));
        var container = await GetContainerAsync(cancellationToken);

        await container.DeleteDocumentAsync(identifier, authorization.ConcurrencyToken, SR.GetResourceString(SR.ID0241), cancellationToken);

        var database = await Context.GetDatabaseAsync(cancellationToken);
        var query = CreateQuery("SELECT VALUE c.id FROM c WHERE c.authorizationId = @identifier", ("@identifier", identifier));

        // Delete the tokens associated with the authorization.
        await database.GetContainer(Options.CurrentValue.TokensContainerName)
            .DeleteDocumentsAsync(query, cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async IAsyncEnumerable<TAuthorization> FindAsync(
        string? subject, string? client, string? status, string? type, ImmutableArray<string>? scopes,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);

        var (filters, parameters) = CreateFilters(subject, client, status, type);

        if (scopes is { IsDefaultOrEmpty: false } values)
        {
            for (var index = 0; index < values.Length; index++)
            {
                var name = "@scope" + index.ToString(CultureInfo.InvariantCulture);

                filters.Add($"ARRAY_CONTAINS(c.scopes, {name})");
                parameters.Add((name, values[index]));
            }
        }

        await foreach (var authorization in container.QueryAsync<TAuthorization>(
            CreateQuery("SELECT * FROM c", filters, parameters), cancellationToken))
        {
            yield return authorization;
        }
    }

    /// <inheritdoc/>
    public virtual IAsyncEnumerable<TAuthorization> FindByApplicationIdAsync(
        string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        return ExecuteAsync(cancellationToken);

        async IAsyncEnumerable<TAuthorization> ExecuteAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var container = await GetContainerAsync(cancellationToken);

            await foreach (var authorization in container.QueryAsync<TAuthorization>(CreateQuery(
                "SELECT * FROM c WHERE c.applicationId = @identifier", ("@identifier", identifier)), cancellationToken))
            {
                yield return authorization;
            }
        }
    }

    /// <inheritdoc/>
    public virtual async ValueTask<TAuthorization?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        var container = await GetContainerAsync(cancellationToken);

        return await container.ReadDocumentAsync<TAuthorization>(identifier, cancellationToken);
    }

    /// <inheritdoc/>
    public virtual IAsyncEnumerable<TAuthorization> FindBySubjectAsync(
        string subject, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);

        return ExecuteAsync(cancellationToken);

        async IAsyncEnumerable<TAuthorization> ExecuteAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var container = await GetContainerAsync(cancellationToken);

            await foreach (var authorization in container.QueryAsync<TAuthorization>(CreateQuery(
                "SELECT * FROM c WHERE c.subject = @subject", ("@subject", subject)), cancellationToken))
            {
                yield return authorization;
            }
        }
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetApplicationIdAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        return new(authorization.ApplicationId);
    }

    /// <inheritdoc/>
    public virtual async ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<TAuthorization>, TState, IQueryable<TResult>> query,
        TState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var container = await GetContainerAsync(cancellationToken);

        await foreach (var result in container.QueryAsync(
            query(container.GetQueryable<TAuthorization>(), state).Take(1), cancellationToken))
        {
            return result;
        }

        return default;
    }

    /// <inheritdoc/>
    public virtual ValueTask<DateTimeOffset?> GetCreationDateAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        return new(authorization.CreationDate is DateTime date ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)) : null);
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetIdAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        return new(authorization.Id);
    }

    /// <inheritdoc/>
    public virtual ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        return new(GetProperties(authorization.Properties));
    }

    /// <inheritdoc/>
    public virtual ValueTask<ImmutableArray<string>> GetScopesAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        return new(authorization.Scopes is { IsDefaultOrEmpty: false } scopes ? scopes : []);
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetStatusAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        return new(authorization.Status);
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetSubjectAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        return new(authorization.Subject);
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetTypeAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        return new(authorization.Type);
    }

    /// <inheritdoc/>
    public virtual ValueTask<TAuthorization> InstantiateAsync(CancellationToken cancellationToken)
    {
        try
        {
            return new(Activator.CreateInstance<TAuthorization>());
        }

        catch (MemberAccessException exception)
        {
            return new(Task.FromException<TAuthorization>(
                new InvalidOperationException(SR.GetResourceString(SR.ID0242), exception)));
        }
    }

    /// <inheritdoc/>
    public virtual async IAsyncEnumerable<TAuthorization> ListAsync(
        int? count, int? offset, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);

        await foreach (var authorization in container.QueryAsync<TAuthorization>(CreatePagedQuery(count, offset), cancellationToken))
        {
            yield return authorization;
        }
    }

    /// <inheritdoc/>
    public virtual IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<TAuthorization>, TState, IQueryable<TResult>> query,
        TState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return ExecuteAsync(cancellationToken);

        async IAsyncEnumerable<TResult> ExecuteAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var container = await GetContainerAsync(cancellationToken);

            await foreach (var element in container.QueryAsync(
                query(container.GetQueryable<TAuthorization>(), state), cancellationToken))
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
        var tokens = database.GetContainer(Options.CurrentValue.TokensContainerName);

        // Note: Azure Cosmos DB doesn't support joins between documents stored in different containers nor
        // deleting the documents returned by a query. To work around these limitations, the authorizations
        // that are no longer valid or ad-hoc are resolved first, the authorizations that still have tokens
        // attached are then excluded using separate queries and the remaining documents removed individually.

        var candidates = new List<string>();

        await foreach (var identifier in container.QueryAsync<string>(CreateQuery(
            "SELECT VALUE c.id FROM c WHERE c.creationDate < @threshold AND (c.status != @valid OR c.type = @type)",
            ("@threshold", FormatDate(threshold.UtcDateTime)),
            ("@valid", Statuses.Valid),
            ("@type", AuthorizationTypes.AdHoc)), cancellationToken))
        {
            candidates.Add(identifier);
        }

        var identifiers = new List<string>();

        foreach (var chunk in Chunk(candidates, MaximumChunkSize))
        {
            var referenced = new HashSet<string>(StringComparer.Ordinal);

            await foreach (var identifier in tokens.QueryAsync<string>(CreateQuery(
                "SELECT DISTINCT VALUE c.authorizationId FROM c WHERE ARRAY_CONTAINS(@identifiers, c.authorizationId)",
                ("@identifiers", chunk)), cancellationToken))
            {
                referenced.Add(identifier);
            }

            identifiers.AddRange(chunk.Where(identifier => !referenced.Contains(identifier)));

            if (identifiers.Count >= MaximumPrunedDocuments)
            {
                break;
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
    public virtual async ValueTask<long> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);

        var container = await GetContainerAsync(cancellationToken);

        return await container.UpdateStatusAsync(CreateQuery("SELECT VALUE c.id FROM c WHERE c.subject = @subject",
            ("@subject", subject)), Statuses.Revoked, cancellationToken);
    }

    /// <inheritdoc/>
    public virtual ValueTask SetApplicationIdAsync(TAuthorization authorization,
        string? identifier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        authorization.ApplicationId = !string.IsNullOrEmpty(identifier) ? identifier : null;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetCreationDateAsync(TAuthorization authorization,
        DateTimeOffset? date, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        authorization.CreationDate = date?.UtcDateTime;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetPropertiesAsync(TAuthorization authorization,
        ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        authorization.Properties = CreateProperties(properties);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetScopesAsync(TAuthorization authorization,
        ImmutableArray<string> scopes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        authorization.Scopes = scopes is { IsDefaultOrEmpty: false } ? scopes : null;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetStatusAsync(TAuthorization authorization,
        string? status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        authorization.Status = status;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetSubjectAsync(TAuthorization authorization,
        string? subject, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        authorization.Subject = subject;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetTypeAsync(TAuthorization authorization,
        string? type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        authorization.Type = type;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual async ValueTask UpdateAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        var identifier = EnsureIdentifier(authorization.Id, nameof(authorization));
        var container = await GetContainerAsync(cancellationToken);

        authorization.ConcurrencyToken = await container.ReplaceDocumentAsync(
            authorization, identifier, authorization.ConcurrencyToken, SR.GetResourceString(SR.ID0241), cancellationToken);
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
