/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * Derived from OpenIddict (https://github.com/openiddict/openiddict-core) and modified by Ledgerscope.
 */

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Ledgerscope.OpenIddict.CosmosDb.Models;
using static Ledgerscope.OpenIddict.CosmosDb.OpenIddictCosmosDbHelpers;

namespace Ledgerscope.OpenIddict.CosmosDb;

/// <summary>
/// Provides methods allowing to manage the applications stored in a database.
/// </summary>
public class OpenIddictCosmosDbApplicationStore : OpenIddictCosmosDbApplicationStore<OpenIddictCosmosDbApplication>
{
    /// <summary>
    /// Creates a new instance of the <see cref="OpenIddictCosmosDbApplicationStore"/> class.
    /// </summary>
    public OpenIddictCosmosDbApplicationStore(
        IOpenIddictCosmosDbContext context,
        IOptionsMonitor<OpenIddictCosmosDbOptions> options)
        : base(context, options)
    {
    }
}

/// <summary>
/// Provides methods allowing to manage the applications stored in a database.
/// </summary>
/// <typeparam name="TApplication">The type of the application entity.</typeparam>
public class OpenIddictCosmosDbApplicationStore<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TApplication> : IOpenIddictApplicationStore<TApplication>
    where TApplication : OpenIddictCosmosDbApplication
{
    /// <summary>
    /// Creates a new instance of the <see cref="OpenIddictCosmosDbApplicationStore"/> class.
    /// </summary>
    public OpenIddictCosmosDbApplicationStore(
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
    /// Gets the container storing the applications.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>The container storing the applications.</returns>
    protected virtual async ValueTask<Container> GetContainerAsync(CancellationToken cancellationToken)
    {
        var database = await Context.GetDatabaseAsync(cancellationToken);
        return database.GetContainer(Options.CurrentValue.ApplicationsContainerName);
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
        Func<IQueryable<TApplication>, IQueryable<TResult>> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var container = await GetContainerAsync(cancellationToken);

        return (await query(container.GetQueryable<TApplication>()).CountAsync(cancellationToken)).Resource;
    }

    /// <inheritdoc/>
    public virtual async ValueTask CreateAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (string.IsNullOrEmpty(application.Id))
        {
            application.Id = Guid.NewGuid().ToString();
        }

        var container = await GetContainerAsync(cancellationToken);

        application.ConcurrencyToken = await container.CreateDocumentAsync(application, application.Id, cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async ValueTask DeleteAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        var identifier = EnsureIdentifier(application.Id, nameof(application));
        var container = await GetContainerAsync(cancellationToken);

        await container.DeleteDocumentAsync(identifier, application.ConcurrencyToken, SR.GetResourceString(SR.ID0239), cancellationToken);

        var database = await Context.GetDatabaseAsync(cancellationToken);
        var query = CreateQuery("SELECT VALUE c.id FROM c WHERE c.applicationId = @identifier", ("@identifier", identifier));

        // Note: Azure Cosmos DB doesn't support cascade deletes or deleting the documents returned by a query.
        // To ensure the entities associated with the application are also removed, they are deleted individually.

        // Delete the authorizations associated with the application.
        await database.GetContainer(Options.CurrentValue.AuthorizationsContainerName)
            .DeleteDocumentsAsync(query, cancellationToken);

        // Delete the tokens associated with the application.
        await database.GetContainer(Options.CurrentValue.TokensContainerName)
            .DeleteDocumentsAsync(query, cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async ValueTask<TApplication?> FindByClientIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        var container = await GetContainerAsync(cancellationToken);

        return await container.FirstOrDefaultAsync<TApplication>(CreateQuery(
            "SELECT TOP 1 * FROM c WHERE c.clientId = @identifier", ("@identifier", identifier)), cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async ValueTask<TApplication?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        var container = await GetContainerAsync(cancellationToken);

        return await container.ReadDocumentAsync<TApplication>(identifier, cancellationToken);
    }

    /// <inheritdoc/>
    public virtual IAsyncEnumerable<TApplication> FindByPostLogoutRedirectUriAsync(
        [StringSyntax(StringSyntaxAttribute.Uri)] string uri, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(uri);

        return ExecuteAsync(cancellationToken);

        async IAsyncEnumerable<TApplication> ExecuteAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var container = await GetContainerAsync(cancellationToken);

            await foreach (var application in container.QueryAsync<TApplication>(CreateQuery(
                "SELECT * FROM c WHERE ARRAY_CONTAINS(c.postLogoutRedirectUris, @uri)", ("@uri", uri)), cancellationToken))
            {
                yield return application;
            }
        }
    }

    /// <inheritdoc/>
    public virtual IAsyncEnumerable<TApplication> FindByRedirectUriAsync(
        [StringSyntax(StringSyntaxAttribute.Uri)] string uri, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(uri);

        return ExecuteAsync(cancellationToken);

        async IAsyncEnumerable<TApplication> ExecuteAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var container = await GetContainerAsync(cancellationToken);

            await foreach (var application in container.QueryAsync<TApplication>(CreateQuery(
                "SELECT * FROM c WHERE ARRAY_CONTAINS(c.redirectUris, @uri)", ("@uri", uri)), cancellationToken))
            {
                yield return application;
            }
        }
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetApplicationTypeAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new(application.ApplicationType);
    }

    /// <inheritdoc/>
    public virtual async ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<TApplication>, TState, IQueryable<TResult>> query,
        TState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var container = await GetContainerAsync(cancellationToken);

        await foreach (var result in container.QueryAsync(
            query(container.GetQueryable<TApplication>(), state).Take(1), cancellationToken))
        {
            return result;
        }

        return default;
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetClientIdAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new(application.ClientId);
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetClientSecretAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new(application.ClientSecret);
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetClientTypeAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new(application.ClientType);
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetConsentTypeAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new(application.ConsentType);
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetDisplayNameAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new(application.DisplayName);
    }

    /// <inheritdoc/>
    public virtual ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new(application.DisplayNames is { Count: > 0 } names
            ? names.ToImmutableDictionary(static pair => CultureInfo.GetCultureInfo(pair.Key), static pair => pair.Value)
            : []);
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetIdAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new(application.Id);
    }

    /// <inheritdoc/>
    public virtual ValueTask<JsonWebKeySet?> GetJsonWebKeySetAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new(application.JsonWebKeySet is { ValueKind: JsonValueKind.Object } set ? JsonWebKeySet.Create(set.GetRawText()) : null);
    }

    /// <inheritdoc/>
    public virtual ValueTask<ImmutableArray<string>> GetPermissionsAsync(
        TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new(application.Permissions is { IsDefaultOrEmpty: false } permissions ? permissions : []);
    }

    /// <inheritdoc/>
    public virtual ValueTask<ImmutableArray<string>> GetPostLogoutRedirectUrisAsync(
        TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new(application.PostLogoutRedirectUris is { IsDefaultOrEmpty: false } uris ? uris : []);
    }

    /// <inheritdoc/>
    public virtual ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new(GetProperties(application.Properties));
    }

    /// <inheritdoc/>
    public virtual ValueTask<ImmutableArray<string>> GetRedirectUrisAsync(
        TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new(application.RedirectUris is { IsDefaultOrEmpty: false } uris ? uris : []);
    }

    /// <inheritdoc/>
    public virtual ValueTask<ImmutableArray<string>> GetRequirementsAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new(application.Requirements is { IsDefaultOrEmpty: false } requirements ? requirements : []);
    }

    /// <inheritdoc/>
    public virtual ValueTask<ImmutableDictionary<string, string>> GetSettingsAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new(application.Settings is { IsEmpty: false } settings ? settings : []);
    }

    /// <inheritdoc/>
    public virtual ValueTask<TApplication> InstantiateAsync(CancellationToken cancellationToken)
    {
        try
        {
            return new(Activator.CreateInstance<TApplication>());
        }

        catch (MemberAccessException exception)
        {
            return new(Task.FromException<TApplication>(
                new InvalidOperationException(SR.GetResourceString(SR.ID0240), exception)));
        }
    }

    /// <inheritdoc/>
    public virtual async IAsyncEnumerable<TApplication> ListAsync(
        int? count, int? offset, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);

        await foreach (var application in container.QueryAsync<TApplication>(CreatePagedQuery(count, offset), cancellationToken))
        {
            yield return application;
        }
    }

    /// <inheritdoc/>
    public virtual IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<TApplication>, TState, IQueryable<TResult>> query,
        TState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return ExecuteAsync(cancellationToken);

        async IAsyncEnumerable<TResult> ExecuteAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var container = await GetContainerAsync(cancellationToken);

            await foreach (var element in container.QueryAsync(
                query(container.GetQueryable<TApplication>(), state), cancellationToken))
            {
                yield return element;
            }
        }
    }

    /// <inheritdoc/>
    public virtual ValueTask SetApplicationTypeAsync(TApplication application,
        string? type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.ApplicationType = type;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetClientIdAsync(TApplication application,
        string? identifier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.ClientId = identifier;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetClientSecretAsync(TApplication application,
        string? secret, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.ClientSecret = secret;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetClientTypeAsync(TApplication application,
        string? type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.ClientType = type;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetConsentTypeAsync(TApplication application,
        string? type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.ConsentType = type;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetDisplayNameAsync(TApplication application,
        string? name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.DisplayName = name;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetDisplayNamesAsync(TApplication application,
        ImmutableDictionary<CultureInfo, string> names, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.DisplayNames = names is { Count: > 0 }
            ? names.ToImmutableDictionary(static pair => pair.Key.Name, static pair => pair.Value, StringComparer.Ordinal)
            : null;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetJsonWebKeySetAsync(TApplication application,
        JsonWebKeySet? set, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.JsonWebKeySet = set is not null
            ? JsonSerializer.SerializeToElement(set, OpenIddictSerializer.Default.JsonWebKeySet)
            : null;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetPermissionsAsync(TApplication application,
        ImmutableArray<string> permissions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.Permissions = permissions is { IsDefaultOrEmpty: false } ? permissions : null;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetPostLogoutRedirectUrisAsync(TApplication application,
        ImmutableArray<string> uris, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.PostLogoutRedirectUris = uris is { IsDefaultOrEmpty: false } ? uris : null;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetPropertiesAsync(TApplication application,
        ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.Properties = CreateProperties(properties);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetRedirectUrisAsync(TApplication application,
        ImmutableArray<string> uris, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.RedirectUris = uris is { IsDefaultOrEmpty: false } ? uris : null;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetRequirementsAsync(TApplication application,
        ImmutableArray<string> requirements, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.Requirements = requirements is { IsDefaultOrEmpty: false } ? requirements : null;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetSettingsAsync(TApplication application,
        ImmutableDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.Settings = settings is { IsEmpty: false } ? settings : null;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual async ValueTask UpdateAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        var identifier = EnsureIdentifier(application.Id, nameof(application));
        var container = await GetContainerAsync(cancellationToken);

        application.ConcurrencyToken = await container.ReplaceDocumentAsync(
            application, identifier, application.ConcurrencyToken, SR.GetResourceString(SR.ID0239), cancellationToken);
    }
}
