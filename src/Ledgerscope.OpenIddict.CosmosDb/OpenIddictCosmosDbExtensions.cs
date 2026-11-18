/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * Derived from OpenIddict (https://github.com/openiddict/openiddict-core) and modified by Ledgerscope.
 */

using Microsoft.Extensions.DependencyInjection.Extensions;
using Ledgerscope.OpenIddict.CosmosDb;
using Ledgerscope.OpenIddict.CosmosDb.Models;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Exposes extensions allowing to register the OpenIddict Azure Cosmos DB services.
/// </summary>
public static class OpenIddictCosmosDbExtensions
{
    /// <summary>
    /// Registers the Azure Cosmos DB stores services in the DI container and
    /// configures OpenIddict to use the Azure Cosmos DB entities by default.
    /// </summary>
    /// <param name="builder">The services builder used by OpenIddict to register new services.</param>
    /// <remarks>This extension can be safely called multiple times.</remarks>
    /// <returns>The <see cref="OpenIddictCosmosDbBuilder"/> instance.</returns>
    public static OpenIddictCosmosDbBuilder UseCosmosDb(this OpenIddictCoreBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Note: Azure Cosmos DB uses case-sensitive, binary string comparisons by default so the
        // additional query filtering applied by the default OpenIddict managers can be safely disabled.
        builder.DisableAdditionalFiltering();

        builder.SetDefaultApplicationEntity<OpenIddictCosmosDbApplication>()
               .SetDefaultAuthorizationEntity<OpenIddictCosmosDbAuthorization>()
               .SetDefaultScopeEntity<OpenIddictCosmosDbScope>()
               .SetDefaultTokenEntity<OpenIddictCosmosDbToken>();

        // Note: the Azure Cosmos DB stores don't depend on scoped/transient services and thus
        // can be safely registered as singleton services and shared/reused across requests.
        builder.ReplaceApplicationStore<OpenIddictCosmosDbApplication, OpenIddictCosmosDbApplicationStore>(ServiceLifetime.Singleton)
               .ReplaceAuthorizationStore<OpenIddictCosmosDbAuthorization, OpenIddictCosmosDbAuthorizationStore>(ServiceLifetime.Singleton)
               .ReplaceScopeStore<OpenIddictCosmosDbScope, OpenIddictCosmosDbScopeStore>(ServiceLifetime.Singleton)
               .ReplaceTokenStore<OpenIddictCosmosDbToken, OpenIddictCosmosDbTokenStore>(ServiceLifetime.Singleton);

        builder.Services.TryAddSingleton<IOpenIddictCosmosDbContext, OpenIddictCosmosDbContext>();

        return new OpenIddictCosmosDbBuilder(builder.Services);
    }

    /// <summary>
    /// Registers the Azure Cosmos DB stores services in the DI container and
    /// configures OpenIddict to use the Azure Cosmos DB entities by default.
    /// </summary>
    /// <param name="builder">The services builder used by OpenIddict to register new services.</param>
    /// <param name="configuration">The configuration delegate used to configure the Azure Cosmos DB services.</param>
    /// <remarks>This extension can be safely called multiple times.</remarks>
    /// <returns>The <see cref="OpenIddictCoreBuilder"/> instance.</returns>
    public static OpenIddictCoreBuilder UseCosmosDb(
        this OpenIddictCoreBuilder builder, Action<OpenIddictCosmosDbBuilder> configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        configuration(builder.UseCosmosDb());

        return builder;
    }
}
