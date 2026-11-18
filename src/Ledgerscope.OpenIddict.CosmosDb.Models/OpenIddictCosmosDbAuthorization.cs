/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * Derived from OpenIddict (https://github.com/openiddict/openiddict-core) and modified by Ledgerscope.
 */

using System.Collections.Immutable;
using System.Diagnostics;

namespace Ledgerscope.OpenIddict.CosmosDb.Models;

/// <summary>
/// Represents an OpenIddict authorization.
/// </summary>
[DebuggerDisplay("Id = {Id,nq} ; Subject = {Subject,nq} ; Type = {Type,nq} ; Status = {Status,nq}")]
public class OpenIddictCosmosDbAuthorization
{
    /// <summary>
    /// Gets or sets the identifier of the application associated with the authorization.
    /// </summary>
    [JsonPropertyName("applicationId")]
    public virtual string? ApplicationId { get; set; }

    /// <summary>
    /// Gets or sets the concurrency token of the authorization.
    /// </summary>
    /// <remarks>
    /// Note: this property is mapped to the "_etag" system property managed by Azure Cosmos DB.
    /// </remarks>
    [JsonPropertyName("_etag")]
    public virtual string? ConcurrencyToken { get; set; }

    /// <summary>
    /// Gets or sets the UTC creation date of the authorization.
    /// </summary>
    [JsonPropertyName("creationDate")]
    public virtual DateTime? CreationDate { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the authorization.
    /// </summary>
    [JsonPropertyName("id")]
    public virtual string? Id { get; set; }

    /// <summary>
    /// Gets or sets the additional properties associated with the authorization.
    /// </summary>
    [JsonPropertyName("properties")]
    public virtual JsonElement? Properties { get; set; }

    /// <summary>
    /// Gets or sets the scopes associated with the authorization.
    /// </summary>
    [JsonPropertyName("scopes")]
    public virtual ImmutableArray<string>? Scopes { get; set; }

    /// <summary>
    /// Gets or sets the status of the authorization.
    /// </summary>
    [JsonPropertyName("status")]
    public virtual string? Status { get; set; }

    /// <summary>
    /// Gets or sets the subject associated with the authorization.
    /// </summary>
    [JsonPropertyName("subject")]
    public virtual string? Subject { get; set; }

    /// <summary>
    /// Gets or sets the type of the authorization.
    /// </summary>
    [JsonPropertyName("type")]
    public virtual string? Type { get; set; }
}
