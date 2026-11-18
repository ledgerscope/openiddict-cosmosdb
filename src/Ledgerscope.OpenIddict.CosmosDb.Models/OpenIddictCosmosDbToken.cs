/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * Derived from OpenIddict (https://github.com/openiddict/openiddict-core) and modified by Ledgerscope.
 */

using System.Diagnostics;

namespace Ledgerscope.OpenIddict.CosmosDb.Models;

/// <summary>
/// Represents an OpenIddict token.
/// </summary>
[DebuggerDisplay("Id = {Id,nq} ; Subject = {Subject,nq} ; Type = {Type,nq} ; Status = {Status,nq}")]
public class OpenIddictCosmosDbToken
{
    /// <summary>
    /// Gets or sets the identifier of the application associated with the token.
    /// </summary>
    [JsonPropertyName("applicationId")]
    public virtual string? ApplicationId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the authorization associated with the token.
    /// </summary>
    [JsonPropertyName("authorizationId")]
    public virtual string? AuthorizationId { get; set; }

    /// <summary>
    /// Gets or sets the concurrency token of the token.
    /// </summary>
    /// <remarks>
    /// Note: this property is mapped to the "_etag" system property managed by Azure Cosmos DB.
    /// </remarks>
    [JsonPropertyName("_etag")]
    public virtual string? ConcurrencyToken { get; set; }

    /// <summary>
    /// Gets or sets the UTC creation date of the token.
    /// </summary>
    [JsonPropertyName("creationDate")]
    public virtual DateTime? CreationDate { get; set; }

    /// <summary>
    /// Gets or sets the UTC expiration date of the token.
    /// </summary>
    [JsonPropertyName("expirationDate")]
    public virtual DateTime? ExpirationDate { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the token.
    /// </summary>
    [JsonPropertyName("id")]
    public virtual string? Id { get; set; }

    /// <summary>
    /// Gets or sets the payload of the token.
    /// </summary>
    /// <remarks>
    /// Note: this property is only used for reference tokens
    /// and may be hashed or encrypted for security reasons.
    /// </remarks>
    [JsonPropertyName("payload")]
    public virtual string? Payload { get; set; }

    /// <summary>
    /// Gets or sets the additional properties associated with the token.
    /// </summary>
    [JsonPropertyName("properties")]
    public virtual JsonElement? Properties { get; set; }

    /// <summary>
    /// Gets or sets the UTC redemption date of the token.
    /// </summary>
    [JsonPropertyName("redemptionDate")]
    public virtual DateTime? RedemptionDate { get; set; }

    /// <summary>
    /// Gets or sets the reference identifier associated with the token.
    /// </summary>
    /// <remarks>
    /// Note: this property is only used for reference tokens
    /// and may be hashed or encrypted for security reasons.
    /// </remarks>
    [JsonPropertyName("referenceId")]
    public virtual string? ReferenceId { get; set; }

    /// <summary>
    /// Gets or sets the status of the token.
    /// </summary>
    [JsonPropertyName("status")]
    public virtual string? Status { get; set; }

    /// <summary>
    /// Gets or sets the subject of the token.
    /// </summary>
    [JsonPropertyName("subject")]
    public virtual string? Subject { get; set; }

    /// <summary>
    /// Gets or sets the type of the token.
    /// </summary>
    [JsonPropertyName("type")]
    public virtual string? Type { get; set; }
}
