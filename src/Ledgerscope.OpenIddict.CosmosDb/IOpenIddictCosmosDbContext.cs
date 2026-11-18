/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * Derived from OpenIddict (https://github.com/openiddict/openiddict-core) and modified by Ledgerscope.
 */

using System.ComponentModel;

namespace Ledgerscope.OpenIddict.CosmosDb;

/// <summary>
/// Exposes the Azure Cosmos DB database used by the OpenIddict stores.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface IOpenIddictCosmosDbContext
{
    /// <summary>
    /// Gets the <see cref="Database"/>.
    /// </summary>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the
    /// asynchronous operation, whose result returns the Azure Cosmos DB database.
    /// </returns>
    ValueTask<Database> GetDatabaseAsync(CancellationToken cancellationToken);
}
