/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * Derived from OpenIddict (https://github.com/openiddict/openiddict-core) and modified by Ledgerscope.
 */

using System.Collections.Immutable;
using System.Diagnostics;

namespace Ledgerscope.OpenIddict.CosmosDb.Models;

/// <summary>
/// Represents an OpenIddict scope.
/// </summary>
[DebuggerDisplay("Id = {Id,nq} ; Name = {Name,nq}")]
public class OpenIddictCosmosDbScope
{
    /// <summary>
    /// Gets or sets the concurrency token of the scope.
    /// </summary>
    /// <remarks>
    /// Note: this property is mapped to the "_etag" system property managed by Azure Cosmos DB.
    /// </remarks>
    [JsonPropertyName("_etag")]
    public virtual string? ConcurrencyToken { get; set; }

    /// <summary>
    /// Gets or sets the public description associated with the scope.
    /// </summary>
    [JsonPropertyName("description")]
    public virtual string? Description { get; set; }

    /// <summary>
    /// Gets or sets the localized public descriptions associated with the scope.
    /// </summary>
    [JsonPropertyName("descriptions")]
    public virtual ImmutableDictionary<string, string>? Descriptions { get; set; }

    /// <summary>
    /// Gets or sets the display name associated with the scope.
    /// </summary>
    [JsonPropertyName("displayName")]
    public virtual string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the localized display names associated with the scope.
    /// </summary>
    [JsonPropertyName("displayNames")]
    public virtual ImmutableDictionary<string, string>? DisplayNames { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the scope.
    /// </summary>
    [JsonPropertyName("id")]
    public virtual string? Id { get; set; }

    /// <summary>
    /// Gets or sets the unique name associated with the scope.
    /// </summary>
    [JsonPropertyName("name")]
    public virtual string? Name { get; set; }

    /// <summary>
    /// Gets or sets the additional properties associated with the scope.
    /// </summary>
    [JsonPropertyName("properties")]
    public virtual JsonElement? Properties { get; set; }

    /// <summary>
    /// Gets or sets the resources associated with the scope.
    /// </summary>
    [JsonPropertyName("resources")]
    public virtual ImmutableArray<string>? Resources { get; set; }
}
