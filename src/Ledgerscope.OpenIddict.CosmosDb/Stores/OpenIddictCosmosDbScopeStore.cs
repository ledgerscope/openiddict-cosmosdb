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
/// Provides methods allowing to manage the scopes stored in a database.
/// </summary>
public class OpenIddictCosmosDbScopeStore : OpenIddictCosmosDbScopeStore<OpenIddictCosmosDbScope>
{
    /// <summary>
    /// Creates a new instance of the <see cref="OpenIddictCosmosDbScopeStore"/> class.
    /// </summary>
    public OpenIddictCosmosDbScopeStore(
        IOpenIddictCosmosDbContext context,
        IOptionsMonitor<OpenIddictCosmosDbOptions> options)
        : base(context, options)
    {
    }
}

/// <summary>
/// Provides methods allowing to manage the scopes stored in a database.
/// </summary>
/// <typeparam name="TScope">The type of the scope entity.</typeparam>
public class OpenIddictCosmosDbScopeStore<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TScope> : IOpenIddictScopeStore<TScope>
    where TScope : OpenIddictCosmosDbScope
{
    /// <summary>
    /// Creates a new instance of the <see cref="OpenIddictCosmosDbScopeStore"/> class.
    /// </summary>
    public OpenIddictCosmosDbScopeStore(
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
    /// Gets the container storing the scopes.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>The container storing the scopes.</returns>
    protected virtual async ValueTask<Container> GetContainerAsync(CancellationToken cancellationToken)
    {
        var database = await Context.GetDatabaseAsync(cancellationToken);
        return database.GetContainer(Options.CurrentValue.ScopesContainerName);
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
        Func<IQueryable<TScope>, IQueryable<TResult>> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var container = await GetContainerAsync(cancellationToken);

        return (await query(container.GetQueryable<TScope>()).CountAsync(cancellationToken)).Resource;
    }

    /// <inheritdoc/>
    public virtual async ValueTask CreateAsync(TScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (string.IsNullOrEmpty(scope.Id))
        {
            scope.Id = Guid.NewGuid().ToString();
        }

        var container = await GetContainerAsync(cancellationToken);

        scope.ConcurrencyToken = await container.CreateDocumentAsync(scope, scope.Id, cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async ValueTask DeleteAsync(TScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var identifier = EnsureIdentifier(scope.Id, nameof(scope));
        var container = await GetContainerAsync(cancellationToken);

        await container.DeleteDocumentAsync(identifier, scope.ConcurrencyToken, SR.GetResourceString(SR.ID0245), cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async ValueTask<TScope?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        var container = await GetContainerAsync(cancellationToken);

        return await container.ReadDocumentAsync<TScope>(identifier, cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async ValueTask<TScope?> FindByNameAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var container = await GetContainerAsync(cancellationToken);

        return await container.FirstOrDefaultAsync<TScope>(CreateQuery(
            "SELECT TOP 1 * FROM c WHERE c.name = @name", ("@name", name)), cancellationToken);
    }

    /// <inheritdoc/>
    public virtual IAsyncEnumerable<TScope> FindByNamesAsync(ImmutableArray<string> names, CancellationToken cancellationToken)
    {
        if (names.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0203), nameof(names));
        }

        return ExecuteAsync(cancellationToken);

        async IAsyncEnumerable<TScope> ExecuteAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var container = await GetContainerAsync(cancellationToken);

            foreach (var chunk in Chunk([.. names], MaximumChunkSize))
            {
                await foreach (var scope in container.QueryAsync<TScope>(CreateQuery(
                    "SELECT * FROM c WHERE ARRAY_CONTAINS(@names, c.name)", ("@names", chunk)), cancellationToken))
                {
                    yield return scope;
                }
            }
        }
    }

    /// <inheritdoc/>
    public virtual IAsyncEnumerable<TScope> FindByResourceAsync(string resource, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(resource);

        return ExecuteAsync(cancellationToken);

        async IAsyncEnumerable<TScope> ExecuteAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var container = await GetContainerAsync(cancellationToken);

            await foreach (var scope in container.QueryAsync<TScope>(CreateQuery(
                "SELECT * FROM c WHERE ARRAY_CONTAINS(c.resources, @resource)", ("@resource", resource)), cancellationToken))
            {
                yield return scope;
            }
        }
    }

    /// <inheritdoc/>
    public virtual async ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<TScope>, TState, IQueryable<TResult>> query,
        TState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var container = await GetContainerAsync(cancellationToken);

        await foreach (var result in container.QueryAsync(
            query(container.GetQueryable<TScope>(), state).Take(1), cancellationToken))
        {
            return result;
        }

        return default;
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetDescriptionAsync(TScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return new(scope.Description);
    }

    /// <inheritdoc/>
    public virtual ValueTask<ImmutableDictionary<CultureInfo, string>> GetDescriptionsAsync(TScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return new(scope.Descriptions is { Count: > 0 } descriptions
            ? descriptions.ToImmutableDictionary(static pair => CultureInfo.GetCultureInfo(pair.Key), static pair => pair.Value)
            : []);
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetDisplayNameAsync(TScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return new(scope.DisplayName);
    }

    /// <inheritdoc/>
    public virtual ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(TScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return new(scope.DisplayNames is { Count: > 0 } names
            ? names.ToImmutableDictionary(static pair => CultureInfo.GetCultureInfo(pair.Key), static pair => pair.Value)
            : []);
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetIdAsync(TScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return new(scope.Id);
    }

    /// <inheritdoc/>
    public virtual ValueTask<string?> GetNameAsync(TScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return new(scope.Name);
    }

    /// <inheritdoc/>
    public virtual ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(TScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return new(GetProperties(scope.Properties));
    }

    /// <inheritdoc/>
    public virtual ValueTask<ImmutableArray<string>> GetResourcesAsync(TScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return new(scope.Resources is { IsDefaultOrEmpty: false } resources ? resources : []);
    }

    /// <inheritdoc/>
    public virtual ValueTask<TScope> InstantiateAsync(CancellationToken cancellationToken)
    {
        try
        {
            return new(Activator.CreateInstance<TScope>());
        }

        catch (MemberAccessException exception)
        {
            return new(Task.FromException<TScope>(
                new InvalidOperationException(SR.GetResourceString(SR.ID0246), exception)));
        }
    }

    /// <inheritdoc/>
    public virtual async IAsyncEnumerable<TScope> ListAsync(
        int? count, int? offset, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);

        await foreach (var scope in container.QueryAsync<TScope>(CreatePagedQuery(count, offset), cancellationToken))
        {
            yield return scope;
        }
    }

    /// <inheritdoc/>
    public virtual IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<TScope>, TState, IQueryable<TResult>> query,
        TState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return ExecuteAsync(cancellationToken);

        async IAsyncEnumerable<TResult> ExecuteAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var container = await GetContainerAsync(cancellationToken);

            await foreach (var element in container.QueryAsync(
                query(container.GetQueryable<TScope>(), state), cancellationToken))
            {
                yield return element;
            }
        }
    }

    /// <inheritdoc/>
    public virtual ValueTask SetDescriptionAsync(TScope scope, string? description, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        scope.Description = description;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetDescriptionsAsync(TScope scope,
        ImmutableDictionary<CultureInfo, string> descriptions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        scope.Descriptions = descriptions is { Count: > 0 }
            ? descriptions.ToImmutableDictionary(static pair => pair.Key.Name, static pair => pair.Value, StringComparer.Ordinal)
            : null;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetDisplayNamesAsync(TScope scope,
        ImmutableDictionary<CultureInfo, string> names, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        scope.DisplayNames = names is { Count: > 0 }
            ? names.ToImmutableDictionary(static pair => pair.Key.Name, static pair => pair.Value, StringComparer.Ordinal)
            : null;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetDisplayNameAsync(TScope scope, string? name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        scope.DisplayName = name;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetNameAsync(TScope scope, string? name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        scope.Name = name;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetPropertiesAsync(TScope scope,
        ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        scope.Properties = CreateProperties(properties);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask SetResourcesAsync(TScope scope, ImmutableArray<string> resources, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        scope.Resources = resources is { IsDefaultOrEmpty: false } ? resources : null;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual async ValueTask UpdateAsync(TScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var identifier = EnsureIdentifier(scope.Id, nameof(scope));
        var container = await GetContainerAsync(cancellationToken);

        scope.ConcurrencyToken = await container.ReplaceDocumentAsync(
            scope, identifier, scope.ConcurrencyToken, SR.GetResourceString(SR.ID0245), cancellationToken);
    }
}
