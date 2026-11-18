/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * Derived from OpenIddict (https://github.com/openiddict/openiddict-core) and modified by Ledgerscope.
 */

namespace Ledgerscope.OpenIddict.CosmosDb;

/// <summary>
/// Provides various settings needed to configure the OpenIddict Azure Cosmos DB integration.
/// </summary>
/// <remarks>
/// Note: the containers used by the OpenIddict stores MUST be partitioned using "/id" as the partition key path.
/// </remarks>
public sealed class OpenIddictCosmosDbOptions
{
    /// <summary>
    /// Gets or sets the name of the applications container (by default, openiddict.applications).
    /// </summary>
    public string ApplicationsContainerName { get; set; } = "openiddict.applications";

    /// <summary>
    /// Gets or sets the name of the authorizations container (by default, openiddict.authorizations).
    /// </summary>
    public string AuthorizationsContainerName { get; set; } = "openiddict.authorizations";

    /// <summary>
    /// Gets or sets the <see cref="Microsoft.Azure.Cosmos.Database"/> used by the OpenIddict stores.
    /// If no value is explicitly set, the database is resolved from the DI container.
    /// </summary>
    public Database? Database { get; set; }

    /// <summary>
    /// Gets or sets the name of the scopes container (by default, openiddict.scopes).
    /// </summary>
    public string ScopesContainerName { get; set; } = "openiddict.scopes";

    /// <summary>
    /// Gets or sets the name of the tokens container (by default, openiddict.tokens).
    /// </summary>
    public string TokensContainerName { get; set; } = "openiddict.tokens";
}
