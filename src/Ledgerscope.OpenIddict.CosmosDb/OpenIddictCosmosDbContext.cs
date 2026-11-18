/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * Derived from OpenIddict (https://github.com/openiddict/openiddict-core) and modified by Ledgerscope.
 */

using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ledgerscope.OpenIddict.CosmosDb;

/// <inheritdoc/>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class OpenIddictCosmosDbContext : IOpenIddictCosmosDbContext
{
    private readonly IOptionsMonitor<OpenIddictCosmosDbOptions> _options;
    private readonly IServiceProvider _provider;

    /// <summary>
    /// Creates a new instance of the <see cref="OpenIddictCosmosDbContext"/> class.
    /// </summary>
    public OpenIddictCosmosDbContext(
        IOptionsMonitor<OpenIddictCosmosDbOptions> options,
        IServiceProvider provider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>
    /// Gets the message of the exception thrown when no database can be resolved.
    /// </summary>
    public const string DatabaseNotFoundMessage =
        "No suitable Azure Cosmos DB database can be found. To configure the OpenIddict Azure Cosmos DB stores " +
        "to use a specific database, use 'services.AddOpenIddict().AddCore().UseCosmosDb().UseDatabase()' or " +
        "register a 'Microsoft.Azure.Cosmos.Database' in the dependency injection container.";

    /// <inheritdoc/>
    public ValueTask<Database> GetDatabaseAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new(Task.FromCanceled<Database>(cancellationToken));
        }

        var database = _options.CurrentValue.Database ?? _provider.GetService<Database>();
        if (database is null)
        {
            return new(Task.FromException<Database>(
                new InvalidOperationException(DatabaseNotFoundMessage)));
        }

        return new(database);
    }
}
